using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Services;

// ============================ نتائج ومدخلات ============================

public class TransferResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int TransferId { get; init; }
    public string TransferNumber { get; init; } = string.Empty;

    public static TransferResult Fail(string message) =>
        new() { Success = false, Message = message };

    public static TransferResult Ok(string message, StockTransfer t) => new()
    {
        Success = true,
        Message = message,
        TransferId = t.Id,
        TransferNumber = t.TransferNumber
    };
}

public class TransferLineRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class CreateTransferRequest
{
    /// <summary>المخزن المطلوب منه (المصدر)</summary>
    public int FromWarehouseId { get; set; }

    /// <summary>
    /// المخزن الطالب. يُملأ من مخزن المستخدم لا من النموذج المُرسَل: السماح
    /// للمتصفح بتحديد الجهة الطالبة يعني أن أي مستخدم يطلب لأي مخزن.
    /// </summary>
    public int ToWarehouseId { get; set; }

    public string? Notes { get; set; }
    public List<TransferLineRequest> Items { get; set; } = new();
}

/// <summary>سطر في شاشة «اطلب من مخزن آخر» — يعرض أرصدة كل المخازن لصنف</summary>
public class TransferCandidateRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;

    /// <summary>رصيد الصنف في المخزن الطالب</summary>
    public int QuantityHere { get; set; }

    /// <summary>أرصدة الصنف في المخازن الأخرى — مصدر قرار «من أطلب؟»</summary>
    public List<(int WarehouseId, string WarehouseName, int Quantity)> Elsewhere { get; set; } = new();

    public int TotalElsewhere => Elsewhere.Sum(x => x.Quantity);
}

// ============================ العقد ============================

public interface ITransferService
{
    Task<TransferResult> CreateAsync(CreateTransferRequest request, string userId, string userName);
    Task<TransferResult> ApproveAsync(int id, string userId, string userName);
    Task<TransferResult> RejectAsync(int id, string reason, string userId, string userName);

    /// <summary>الشحن: هنا فقط يُخصم من المصدر. <paramref name="shipped"/> = ProductId → الكمية</summary>
    Task<TransferResult> ShipAsync(int id, Dictionary<int, int> shipped, string userId, string userName);

    /// <summary>الاستلام: هنا فقط يُضاف للهدف. <paramref name="received"/> = ProductId → الكمية</summary>
    Task<TransferResult> ReceiveAsync(int id, Dictionary<int, int> received, string userId, string userName);

    Task<TransferResult> CancelAsync(int id, string userId, string userName);

    Task<StockTransfer?> GetAsync(int id);
    Task<List<StockTransfer>> ListOutgoingAsync(int warehouseId, TransferStatus? status = null);
    Task<List<StockTransfer>> ListIncomingAsync(int warehouseId, TransferStatus? status = null);
    Task<List<StockTransfer>> ListAllAsync(TransferStatus? status = null);
    Task<int> CountPendingApprovalAsync(int warehouseId);
    Task<List<StockTransfer>> ListInTransitAsync();
    Task<List<TransferCandidateRow>> GetCandidatesAsync(int toWarehouseId, string? search = null);
}

// ============================ التنفيذ ============================

/// <summary>
/// طلبات التحويل بين المخازن.
///
/// <para>
/// <b>القاعدة المركزية:</b> الخصم من المصدر عند <b>الشحن</b> والإضافة للهدف
/// عند <b>الاستلام</b>، وكلاهما عبر <see cref="IInventoryService"/> وحده.
/// بين الحدثين القطع «في الطريق» وتُحسب من الجدول نفسه.
/// </para>
///
/// <para>
/// كل تغيير حالة يتحقق من الحالة الحالية أولًا. هذا ليس تكرارًا زائدًا مع
/// الواجهة: زرّان مفتوحان في تبويبين، أو زر يُضغط مرتين، يصلان كطلبين
/// متتاليين — والتحقق هنا هو ما يمنع شحن نفس التحويل مرتين وخصم الكمية ضِعفين.
/// </para>
/// </summary>
public class TransferService : ITransferService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAuditService _audit;
    private readonly ILogger<TransferService> _logger;

    public TransferService(AppDbContext db, IInventoryService inventory,
        IAuditService audit, ILogger<TransferService> logger)
    {
        _db = db;
        _inventory = inventory;
        _audit = audit;
        _logger = logger;
    }

    // ==================== إنشاء الطلب ====================

    public async Task<TransferResult> CreateAsync(
        CreateTransferRequest request, string userId, string userName)
    {
        if (request.FromWarehouseId == request.ToWarehouseId)
            return TransferResult.Fail("لا يمكن التحويل من المخزن إلى نفسه");

        var from = await _db.Warehouses.FindAsync(request.FromWarehouseId);
        if (from is null) return TransferResult.Fail("المخزن المصدر غير موجود");
        if (!from.IsActive) return TransferResult.Fail($"المخزن المصدر «{from.Name}» غير مفعّل");

        var to = await _db.Warehouses.FindAsync(request.ToWarehouseId);
        if (to is null) return TransferResult.Fail("المخزن الطالب غير موجود");
        if (!to.IsActive) return TransferResult.Fail($"المخزن الطالب «{to.Name}» غير مفعّل");

        // دمج السطور المكررة لنفس الصنف بجمع كمياتها: سطران لنفس المنتج
        // يجعلان «المطلوب» غامضًا وقد يتجاوز مجموعهما المتاح بلا أن يُلاحظ.
        var merged = request.Items
            .Where(i => i.ProductId > 0 && i.Quantity > 0)
            .GroupBy(i => i.ProductId)
            .Select(g => new TransferLineRequest
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        if (merged.Count == 0)
            return TransferResult.Fail("أضف صنفًا واحدًا على الأقل بكمية صحيحة");

        if (merged.Any(i => i.Quantity > 100000))
            return TransferResult.Fail("الكمية المطلوبة كبيرة بشكل غير منطقي");

        var ids = merged.Select(i => i.ProductId).ToList();
        var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var transfer = new StockTransfer
            {
                TransferNumber = await GenerateNumberAsync(DateTime.Now),
                FromWarehouseId = from.Id,
                ToWarehouseId = to.Id,
                Status = TransferStatus.Pending,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                RequestedByUserId = userId,
                RequestedByName = userName,
                RequestedAt = DateTime.Now
            };

            foreach (var line in merged)
            {
                var product = products.FirstOrDefault(p => p.Id == line.ProductId);
                if (product is null)
                    return TransferResult.Fail($"أحد المنتجات غير موجود (رقم {line.ProductId})");

                if (!product.IsActive)
                    return TransferResult.Fail($"المنتج «{product.Name}» غير مفعل");

                // لا نتحقق من توفّر الكمية في المصدر الآن: الطلب نية لا حجز،
                // والرصيد قد يتغير بين الطلب والشحن (بيع أو استلام مشتريات).
                // التحقق الحقيقي يحدث عند الشحن حيث يقع الخصم فعلًا.
                transfer.Items.Add(new StockTransferItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,   // snapshot
                    Barcode = product.Barcode,    // snapshot
                    RequestedQuantity = line.Quantity
                });
            }

            _db.StockTransfers.Add(transfer);

            _audit.Track(AuditActions.Transfer, nameof(StockTransfer), transfer.TransferNumber,
                $"طلب تحويل من «{from.Name}» إلى «{to.Name}» — " +
                $"{transfer.Items.Count} صنف بإجمالي {transfer.TotalRequested} قطعة");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return TransferResult.Ok(
                $"تم إرسال طلب التحويل {transfer.TransferNumber} إلى «{from.Name}»", transfer);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل إنشاء طلب تحويل من {From} إلى {To}",
                request.FromWarehouseId, request.ToWarehouseId);
            return TransferResult.Fail("حدث خطأ أثناء إنشاء الطلب، حاول مرة أخرى");
        }
    }

    // ==================== الاعتماد والرفض ====================

    public async Task<TransferResult> ApproveAsync(int id, string userId, string userName)
    {
        var transfer = await LoadAsync(id);
        if (transfer is null) return TransferResult.Fail("طلب التحويل غير موجود");

        if (!transfer.CanDecide)
            return TransferResult.Fail(
                $"لا يمكن اعتماد الطلب وحالته «{TransferStatusLabel.Of(transfer.Status)}»");

        transfer.Status = TransferStatus.Approved;
        transfer.ApprovedByUserId = userId;
        transfer.ApprovedByName = userName;
        transfer.ApprovedAt = DateTime.Now;

        _audit.Track(AuditActions.Approve, nameof(StockTransfer), transfer.TransferNumber,
            $"اعتماد تحويل إلى «{transfer.ToWarehouse?.Name}» — لم تخرج القطع بعد");

        await _db.SaveChangesAsync();

        return TransferResult.Ok(
            $"تم اعتماد الطلب {transfer.TransferNumber} — بانتظار الشحن", transfer);
    }

    public async Task<TransferResult> RejectAsync(int id, string reason, string userId, string userName)
    {
        var transfer = await LoadAsync(id);
        if (transfer is null) return TransferResult.Fail("طلب التحويل غير موجود");

        if (!transfer.CanDecide)
            return TransferResult.Fail(
                $"لا يمكن رفض الطلب وحالته «{TransferStatusLabel.Of(transfer.Status)}»");

        // السبب إلزامي: رفض بلا سبب يجعل المخزن الطالب يعيد الطلب نفسه،
        // ويكرّر الرفض بلا نهاية لأن أحدًا لم يقل ما الخطأ.
        if (string.IsNullOrWhiteSpace(reason))
            return TransferResult.Fail("سبب الرفض مطلوب حتى يعرف المخزن الطالب ما العمل");

        transfer.Status = TransferStatus.Rejected;
        transfer.RejectionReason = reason.Trim();
        transfer.ApprovedByUserId = userId;    // من اتخذ القرار
        transfer.ApprovedByName = userName;
        transfer.ApprovedAt = DateTime.Now;

        _audit.Track(AuditActions.Reject, nameof(StockTransfer), transfer.TransferNumber,
            $"رفض تحويل إلى «{transfer.ToWarehouse?.Name}» — السبب: {transfer.RejectionReason}");

        await _db.SaveChangesAsync();

        return TransferResult.Ok($"تم رفض الطلب {transfer.TransferNumber}", transfer);
    }

    // ==================== الشحن — الخصم من المصدر ====================

    public async Task<TransferResult> ShipAsync(
        int id, Dictionary<int, int> shipped, string userId, string userName)
    {
        var transfer = await LoadAsync(id);
        if (transfer is null) return TransferResult.Fail("طلب التحويل غير موجود");

        if (!transfer.CanShip)
            return TransferResult.Fail(
                $"لا يمكن الشحن وحالة الطلب «{TransferStatusLabel.Of(transfer.Status)}»");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var movements = new List<StockMovement>();
            var totalShipped = 0;

            foreach (var item in transfer.Items)
            {
                // الكمية المشحونة افتراضها المطلوب، ويجوز أن تكون أقل لنقص
                // في المصدر، ولا يجوز أن تزيد لأن ذلك شحن لم يُطلب.
                var qty = shipped.TryGetValue(item.ProductId, out var v)
                    ? v : item.RequestedQuantity;

                if (qty < 0)
                    return TransferResult.Fail($"كمية سالبة غير مقبولة للصنف «{item.ProductName}»");

                if (qty > item.RequestedQuantity)
                    return TransferResult.Fail(
                        $"لا يمكن شحن {qty} من «{item.ProductName}» " +
                        $"والمطلوب {item.RequestedQuantity} فقط");

                if (qty == 0) continue;   // صنف لم يُشحن إطلاقًا — يبقى فرقًا ظاهرًا

                var result = await _inventory.ApplyAsync(new StockChangeRequest
                {
                    ProductId = item.ProductId,
                    WarehouseId = transfer.FromWarehouseId,
                    Change = -qty,                              // خروج من المصدر
                    Reason = StockMovementReason.TransferOut,
                    ReferenceTransferId = transfer.Id,
                    Note = $"تحويل {transfer.TransferNumber} إلى «{transfer.ToWarehouse?.Name}»",
                    UserId = userId
                });

                if (!result.Success)
                    return TransferResult.Fail($"«{item.ProductName}»: {result.Message}");

                item.ShippedQuantity = qty;
                totalShipped += qty;
                if (result.Movement is not null) movements.Add(result.Movement);
            }

            if (totalShipped == 0)
                return TransferResult.Fail("لم تُحدَّد أي كمية للشحن");

            transfer.Status = TransferStatus.Shipped;
            transfer.ShippedByUserId = userId;
            transfer.ShippedByName = userName;
            transfer.ShippedAt = DateTime.Now;

            _audit.Track(AuditActions.Transfer, nameof(StockTransfer), transfer.TransferNumber,
                $"شحن {totalShipped} قطعة من «{transfer.FromWarehouse?.Name}» " +
                $"إلى «{transfer.ToWarehouse?.Name}» — في الطريق الآن");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            var partial = transfer.Items.Any(i => i.IsPartiallyShipped)
                ? " (شحن جزئي — الفرق ظاهر في تفاصيل الطلب)" : "";

            return TransferResult.Ok(
                $"تم شحن {totalShipped} قطعة — في الطريق إلى «{transfer.ToWarehouse?.Name}»{partial}",
                transfer);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل شحن التحويل {Id}", id);
            return TransferResult.Fail("حدث خطأ أثناء الشحن، حاول مرة أخرى");
        }
    }

    // ==================== الاستلام — الإضافة للهدف ====================

    public async Task<TransferResult> ReceiveAsync(
        int id, Dictionary<int, int> received, string userId, string userName)
    {
        var transfer = await LoadAsync(id);
        if (transfer is null) return TransferResult.Fail("طلب التحويل غير موجود");

        if (!transfer.CanReceive)
            return TransferResult.Fail(
                $"لا يمكن الاستلام وحالة الطلب «{TransferStatusLabel.Of(transfer.Status)}»");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var movements = new List<StockMovement>();
            var totalReceived = 0;

            foreach (var item in transfer.Items)
            {
                if (item.ShippedQuantity == 0) continue;

                var qty = received.TryGetValue(item.ProductId, out var v)
                    ? v : item.ShippedQuantity;

                if (qty < 0)
                    return TransferResult.Fail($"كمية سالبة غير مقبولة للصنف «{item.ProductName}»");

                // الاستلام لا يزيد عن المشحون: استلام أكثر مما خرج يعني خلق
                // قطع من العدم — أي زيادة حقيقية مصدرها جرد لا تحويل.
                if (qty > item.ShippedQuantity)
                    return TransferResult.Fail(
                        $"لا يمكن استلام {qty} من «{item.ProductName}» " +
                        $"والمشحون {item.ShippedQuantity} فقط");

                if (qty == 0) continue;

                var result = await _inventory.ApplyAsync(new StockChangeRequest
                {
                    ProductId = item.ProductId,
                    WarehouseId = transfer.ToWarehouseId,
                    Change = qty,                               // دخول للهدف
                    Reason = StockMovementReason.TransferIn,
                    ReferenceTransferId = transfer.Id,
                    Note = $"تحويل {transfer.TransferNumber} من «{transfer.FromWarehouse?.Name}»",
                    UserId = userId
                });

                if (!result.Success)
                    return TransferResult.Fail($"«{item.ProductName}»: {result.Message}");

                item.ReceivedQuantity = qty;
                totalReceived += qty;
                if (result.Movement is not null) movements.Add(result.Movement);
            }

            transfer.Status = TransferStatus.Received;
            transfer.ReceivedByUserId = userId;
            transfer.ReceivedByName = userName;
            transfer.ReceivedAt = DateTime.Now;

            // الفرق لا يُصفَّر ولا يُخفى: يبقى محفوظًا في السطور ويظهر في
            // التفاصيل وتقرير «في الطريق» حتى يُسوّى بجرد صريح مع سببه.
            var shortage = transfer.TotalShipped - totalReceived;

            _audit.Track(AuditActions.Receive, nameof(StockTransfer), transfer.TransferNumber,
                $"استلام {totalReceived} قطعة في «{transfer.ToWarehouse?.Name}»" +
                (shortage > 0 ? $" — نقص {shortage} قطعة عن المشحون" : ""));

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            var msg = $"تم استلام {totalReceived} قطعة في «{transfer.ToWarehouse?.Name}»";
            if (shortage > 0)
                msg += $" — تنبيه: نقص {shortage} قطعة عن المشحون، راجع الفرق";

            return TransferResult.Ok(msg, transfer);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل استلام التحويل {Id}", id);
            return TransferResult.Fail("حدث خطأ أثناء الاستلام، حاول مرة أخرى");
        }
    }

    // ==================== الإلغاء ====================

    public async Task<TransferResult> CancelAsync(int id, string userId, string userName)
    {
        var transfer = await LoadAsync(id);
        if (transfer is null) return TransferResult.Fail("طلب التحويل غير موجود");

        // لا إلغاء بعد الشحن: القطع خرجت فعلًا من المصدر، وإلغاء الورقة لا
        // يُعيدها. الطريق الصحيح أن تُستلم ثم تُحوَّل عكسيًا بطلب جديد.
        if (!transfer.CanCancel)
            return TransferResult.Fail(transfer.Status == TransferStatus.Shipped
                ? "لا يمكن إلغاء طلب شُحن فعلًا — استلمه ثم أنشئ تحويلًا عكسيًا"
                : $"لا يمكن إلغاء طلب حالته «{TransferStatusLabel.Of(transfer.Status)}»");

        transfer.Status = TransferStatus.Cancelled;

        _audit.Track(AuditActions.Update, nameof(StockTransfer), transfer.TransferNumber,
            $"إلغاء طلب تحويل من «{transfer.FromWarehouse?.Name}» بواسطة {userName}");

        await _db.SaveChangesAsync();

        return TransferResult.Ok($"تم إلغاء الطلب {transfer.TransferNumber}", transfer);
    }

    // ==================== القراءات ====================

    public async Task<StockTransfer?> GetAsync(int id) =>
        await _db.StockTransfers.AsNoTracking()
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>الطلبات الواردة لهذا المخزن ليقرر فيها (هو المصدر)</summary>
    public async Task<List<StockTransfer>> ListIncomingAsync(int warehouseId, TransferStatus? status = null)
    {
        var query = _db.StockTransfers.AsNoTracking()
            .Include(t => t.FromWarehouse).Include(t => t.ToWarehouse).Include(t => t.Items)
            .Where(t => t.FromWarehouseId == warehouseId);

        if (status is not null) query = query.Where(t => t.Status == status);

        return await query.OrderByDescending(t => t.RequestedAt).ToListAsync();
    }

    /// <summary>الطلبات التي أنشأها هذا المخزن (هو الطالب)</summary>
    public async Task<List<StockTransfer>> ListOutgoingAsync(int warehouseId, TransferStatus? status = null)
    {
        var query = _db.StockTransfers.AsNoTracking()
            .Include(t => t.FromWarehouse).Include(t => t.ToWarehouse).Include(t => t.Items)
            .Where(t => t.ToWarehouseId == warehouseId);

        if (status is not null) query = query.Where(t => t.Status == status);

        return await query.OrderByDescending(t => t.RequestedAt).ToListAsync();
    }

    public async Task<List<StockTransfer>> ListAllAsync(TransferStatus? status = null)
    {
        var query = _db.StockTransfers.AsNoTracking()
            .Include(t => t.FromWarehouse).Include(t => t.ToWarehouse).Include(t => t.Items)
            .AsQueryable();

        if (status is not null) query = query.Where(t => t.Status == status);

        return await query.OrderByDescending(t => t.RequestedAt).Take(300).ToListAsync();
    }

    public async Task<int> CountPendingApprovalAsync(int warehouseId) =>
        await _db.StockTransfers
            .CountAsync(t => t.FromWarehouseId == warehouseId && t.Status == TransferStatus.Pending);

    /// <summary>كل ما خرج ولم يُستلم — تقرير «في الطريق»</summary>
    public async Task<List<StockTransfer>> ListInTransitAsync() =>
        await _db.StockTransfers.AsNoTracking()
            .Include(t => t.FromWarehouse).Include(t => t.ToWarehouse).Include(t => t.Items)
            .Where(t => t.Status == TransferStatus.Shipped)
            .OrderBy(t => t.ShippedAt)   // الأقدم أولًا: هو الأكثر إلحاحًا
            .ToListAsync();

    /// <summary>
    /// أصناف يمكن طلبها: لكل صنف رصيده هنا وأرصدته في المخازن الأخرى.
    ///
    /// <para>
    /// استعلام واحد مُجمَّع لا استعلام لكل صنف: الشاشة تعرض عشرات الأصناف،
    /// وحلقة استعلامات هنا تعني مئات الرحلات لقاعدة البيانات.
    /// </para>
    /// </summary>
    public async Task<List<TransferCandidateRow>> GetCandidatesAsync(int toWarehouseId, string? search = null)
    {
        var productsQuery = _db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            productsQuery = productsQuery.Where(p => p.Name.Contains(s) || p.Barcode.Contains(s));
        }

        var products = await productsQuery
            .OrderBy(p => p.Name)
            .Take(200)
            .Select(p => new { p.Id, p.Name, p.Barcode })
            .ToListAsync();

        if (products.Count == 0) return new List<TransferCandidateRow>();

        var ids = products.Select(p => p.Id).ToList();

        var stocks = await _db.ProductStocks.AsNoTracking()
            .Where(ps => ids.Contains(ps.ProductId) && ps.Quantity > 0)
            .Select(ps => new
            {
                ps.ProductId,
                ps.WarehouseId,
                ps.Quantity,
                WarehouseName = ps.Warehouse!.Name,
                IsActive = ps.Warehouse!.IsActive
            })
            .ToListAsync();

        var rows = new List<TransferCandidateRow>();
        foreach (var p in products)
        {
            var mine = stocks.FirstOrDefault(s => s.ProductId == p.Id && s.WarehouseId == toWarehouseId);

            // المخازن غير المفعّلة تُستثنى: لا يصح اقتراح طلب من مخزن مُعطَّل
            var others = stocks
                .Where(s => s.ProductId == p.Id && s.WarehouseId != toWarehouseId && s.IsActive)
                .OrderByDescending(s => s.Quantity)   // الأوفر رصيدًا أولًا
                .Select(s => (s.WarehouseId, s.WarehouseName, s.Quantity))
                .ToList();

            // لا معنى لعرض صنف لا يوجد في أي مخزن آخر — لا يمكن طلبه من أحد
            if (others.Count == 0) continue;

            rows.Add(new TransferCandidateRow
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Barcode = p.Barcode,
                QuantityHere = mine?.Quantity ?? 0,
                Elsewhere = others
            });
        }

        // الأصناف الناقصة هنا أولًا: هي سبب زيارة هذه الشاشة
        return rows.OrderBy(r => r.QuantityHere).ThenBy(r => r.ProductName).ToList();
    }

    // ==================== مساعدات ====================

    /// <summary>تحميل متتبَّع (بلا AsNoTracking) — التعديل يحتاج كيانات متتبَّعة</summary>
    private async Task<StockTransfer?> LoadAsync(int id) =>
        await _db.StockTransfers
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>
    /// رقم مرجعي بصيغة TRF-yyyyMMdd-0001. مع الفهرس الفريد على العمود فإن أي
    /// تعارض تزامن يفشل الحفظ بدلًا من إنتاج رقم مكرر.
    /// </summary>
    private async Task<string> GenerateNumberAsync(DateTime date)
    {
        var prefix = $"TRF-{date:yyyyMMdd}-";
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var countToday = await _db.StockTransfers
            .CountAsync(t => t.RequestedAt >= dayStart && t.RequestedAt < dayEnd);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = prefix + (countToday + attempt).ToString("D4");
            if (!await _db.StockTransfers.AnyAsync(t => t.TransferNumber == candidate))
                return candidate;
        }

        return prefix + DateTime.Now.ToString("HHmmssfff");
    }
}

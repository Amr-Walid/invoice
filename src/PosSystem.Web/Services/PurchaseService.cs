using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;

namespace PosSystem.Web.Services;

public class PurchaseResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public PurchaseOrder? Order { get; init; }
    public PurchaseReceipt? Receipt { get; init; }

    public static PurchaseResult Fail(string message) =>
        new() { Success = false, Message = message };

    public static PurchaseResult Ok(string message, PurchaseOrder? order = null,
        PurchaseReceipt? receipt = null) =>
        new() { Success = true, Message = message, Order = order, Receipt = receipt };
}

public class CreatePurchaseRequest
{
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public decimal DiscountPercentage { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseLineInput> Lines { get; set; } = new();

    /// <summary>اعتماد الأمر فورًا بعد إنشائه (زر «حفظ واعتماد»)</summary>
    public bool ConfirmImmediately { get; set; }
}

public interface IPurchaseService
{
    Task<PurchaseOrderListVm> ListAsync(string? q, int? supplierId, int? warehouseId,
        PurchaseStatus? status, DateTime? from, DateTime? to, int page, int pageSize = 20);

    Task<PurchaseOrder?> GetAsync(int id);
    Task<PurchaseDetailsVm?> GetDetailsAsync(int id);

    /// <summary>أصناف مقترحة لأمر الشراء مع رصيدها في المخزن وآخر تكلفة لها</summary>
    Task<List<PurchaseProductRowVm>> GetProductRowsAsync(
        int warehouseId, string? search, bool lowStockOnly, int take = 100);

    Task<PurchaseResult> CreateAsync(CreatePurchaseRequest request, string userId, string userName);
    Task<PurchaseResult> UpdateAsync(int id, CreatePurchaseRequest request, string userId, string userName);
    Task<PurchaseResult> ConfirmAsync(int id, string userId, string userName);

    /// <summary>
    /// استلام دفعة. المفتاح = معرّف سطر الأمر (لا المنتج) لأن الصنف قد يتكرر
    /// في سطرين بتكلفتين مختلفتين.
    /// </summary>
    Task<PurchaseResult> ReceiveAsync(int id, Dictionary<int, int> quantities,
        string? notes, string userId, string userName);

    Task<PurchaseResult> CancelAsync(int id, string? reason, string userId, string userName);
    Task<PurchaseResult> DeleteDraftAsync(int id, string userId, string userName);

    Task<PurchaseReceipt?> GetReceiptAsync(int receiptId);
    Task<List<PurchaseReceipt>> ListReceiptsAsync(int? warehouseId, DateTime? from, DateTime? to, int take = 200);

    /// <summary>عدد الأوامر المفتوحة (للتنبيه في الشريط الجانبي)</summary>
    Task<int> CountOpenAsync(int? warehouseId = null);

    Task<PurchaseReportVm> BuildReportAsync(DateTime? from, DateTime? to, int? warehouseId);
}

/// <summary>
/// منطق المشتريات.
///
/// <para><b>المبادئ المُطبَّقة:</b></para>
///
/// <para><b>1) لا قطعة تدخل المخزون قبل الاستلام.</b> الإنشاء نية والاعتماد
/// التزام؛ لو أضفنا الكميات عند الاعتماد لبِعنا بضاعة لم تصل، ولظهر المخزون
/// أكبر من الواقع بين الاعتماد والتوريد.</para>
///
/// <para><b>2) الاستلام يمرّ من <see cref="IInventoryService"/> وحده.</b> فلا
/// تُكتب كمية في مكانين بمنطقَين، وكل دفعة تُخلّف سطرًا في
/// <see cref="StockMovement"/> بسبب <c>Purchase</c> ومرجع إذن الاستلام،
/// فيُقرأ تاريخ أي قطعة من أول دخولها.</para>
///
/// <para><b>3) إذن الاستلام كيان مستقل لا تحديث للكمية.</b> المورد يُوصّل على
/// دفعات، وبدون الإذن نعرف «وصل 30 من 50» ولا نعرف متى وصلت كل دفعة ولا من
/// استلمها — وهي أول معلومة تُطلب عند أي خلاف مع مورد.</para>
///
/// <para><b>4) كل عملية داخل transaction واحدة.</b> إذن الاستلام + زيادة
/// المخزون + تحديث كميات السطور + حالة الأمر تحدث معًا أو لا تحدث.</para>
///
/// <para><b>5) إعادة القراءة داخل الـ transaction تمنع الاستلام المزدوج</b> عند
/// ضغط «استلام» مرتين أو من نافذتين — نفس الحماية المستخدمة في المرتجعات.</para>
///
/// <para><b>6) الإلغاء لا يسحب ما استُلم.</b> القطع في المخزن فعلًا؛ سحبها
/// بالإلغاء يجعل المخزون يكذب. الإلغاء يُغلق ما تبقّى فقط.</para>
/// </summary>
public class PurchaseService : IPurchaseService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IInventoryService _inventory;
    private readonly ILogger<PurchaseService> _logger;

    public PurchaseService(AppDbContext db, IAuditService audit,
        IInventoryService inventory, ILogger<PurchaseService> logger)
    {
        _db = db;
        _audit = audit;
        _inventory = inventory;
        _logger = logger;
    }

    // ==================================================================
    //  قراءة
    // ==================================================================

    public async Task<PurchaseOrderListVm> ListAsync(string? q, int? supplierId,
        int? warehouseId, PurchaseStatus? status, DateTime? from, DateTime? to,
        int page, int pageSize = 20)
    {
        if (pageSize <= 0) pageSize = 20;
        if (page <= 0) page = 1;

        var query = _db.PurchaseOrders.AsNoTracking().AsQueryable();

        if (supplierId is > 0) query = query.Where(o => o.SupplierId == supplierId);
        if (warehouseId is > 0) query = query.Where(o => o.WarehouseId == warehouseId);
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAt < to.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(o =>
                o.PurchaseNumber.Contains(term) ||
                o.SupplierName.Contains(term) ||
                o.Items.Any(i => i.ProductName.Contains(term) || i.Barcode.Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new PurchaseOrderRowVm
            {
                Id = o.Id,
                PurchaseNumber = o.PurchaseNumber,
                SupplierName = o.SupplierName,
                WarehouseName = o.Warehouse!.Name,
                Status = o.Status,
                Total = o.Total,
                CreatedAt = o.CreatedAt,
                ExpectedDate = o.ExpectedDate,
                CreatedByName = o.CreatedByName,
                ItemCount = o.Items.Count,
                TotalOrdered = o.Items.Sum(i => i.Quantity),
                TotalReceived = o.Items.Sum(i => i.ReceivedQuantity)
            })
            .ToListAsync();

        // المجاميع على كل النتائج لا على الصفحة المعروضة: «إجمالي مشتريات
        // الفترة» يجب أن يكون رقم الفترة كاملة وإلا كان مضللًا.
        // الملغاة تُستثنى من المبلغ لأنها لم تُنفَّق.
        var effective = query.Where(o => o.Status != PurchaseStatus.Cancelled);

        decimal grandTotal;
        if (_db.Database.IsSqlite())
        {
            // SQLite لا يدعم SUM على decimal
            var totals = await effective.Select(o => o.Total).ToListAsync();
            grandTotal = totals.Sum();
        }
        else
        {
            grandTotal = await effective.SumAsync(o => (decimal?)o.Total) ?? 0m;
        }

        var openQuery = query.Where(o =>
            o.Status == PurchaseStatus.Confirmed ||
            o.Status == PurchaseStatus.PartiallyReceived);

        var openCount = await openQuery.CountAsync();
        var today = DateTime.Today;
        var overdueCount = await openQuery
            .CountAsync(o => o.ExpectedDate != null && o.ExpectedDate < today);

        var pending = await openQuery
            .Select(o => new
            {
                Ordered = o.Items.Sum(i => i.Quantity),
                Received = o.Items.Sum(i => i.ReceivedQuantity)
            })
            .ToListAsync();

        return new PurchaseOrderListVm
        {
            Query = q,
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Status = status,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalCount = totalCount,
            Orders = rows,
            GrandTotal = grandTotal,
            OpenCount = openCount,
            OverdueCount = overdueCount,
            PiecesPending = pending.Sum(p => Math.Max(0, p.Ordered - p.Received))
        };
    }

    public Task<PurchaseOrder?> GetAsync(int id) => LoadAsync(id);

    public async Task<PurchaseDetailsVm?> GetDetailsAsync(int id)
    {
        var order = await _db.PurchaseOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Warehouse)
            .Include(o => o.Items.OrderBy(i => i.Id))
            .Include(o => o.Receipts.OrderByDescending(r => r.CreatedAt))
                .ThenInclude(r => r.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return null;

        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var stock = await _inventory.GetQuantitiesAsync(productIds, order.WarehouseId);

        return new PurchaseDetailsVm
        {
            Order = order,
            StockHere = stock,
            CanManage = order.CanEdit,
            CanReceive = order.CanReceive
        };
    }

    public async Task<List<PurchaseProductRowVm>> GetProductRowsAsync(
        int warehouseId, string? search, bool lowStockOnly, int take = 100)
    {
        take = Math.Clamp(take, 1, 500);

        var query = _db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(term) ||
                p.Barcode.Contains(term) ||
                p.Brand!.Name.Contains(term) ||
                p.Category!.Name.Contains(term));
        }

        var products = await query
            .OrderBy(p => p.Name)
            .Take(take)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Barcode,
                BrandName = p.Brand!.Name,
                CategoryName = p.Category!.Name,
                p.Price,
                p.LowStockThreshold
            })
            .ToListAsync();

        var ids = products.Select(p => p.Id).ToList();
        if (ids.Count == 0) return new List<PurchaseProductRowVm>();

        var stock = await _db.ProductStocks.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && ids.Contains(s.ProductId))
            .Select(s => new { s.ProductId, s.Quantity, s.LowStockThreshold })
            .ToListAsync();

        var stockMap = stock.ToDictionary(s => s.ProductId);

        // آخر تكلفة معروفة لكل صنف: تُقترح في الشاشة فلا يُبحث عنها يدويًا في
        // أوامر قديمة. تُقرأ من أحدث سطر غير ملغي — استعلام واحد لا N+1.
        var costRows = await _db.PurchaseOrderItems.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId) &&
                        i.PurchaseOrder!.Status != PurchaseStatus.Cancelled)
            .OrderByDescending(i => i.PurchaseOrderId)
            .Select(i => new { i.ProductId, i.UnitCost, i.PurchaseOrderId })
            .ToListAsync();

        var lastCost = costRows
            .GroupBy(c => c.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.PurchaseOrderId).First().UnitCost);

        var rows = products.Select(p =>
        {
            stockMap.TryGetValue(p.Id, out var s);
            return new PurchaseProductRowVm
            {
                ProductId = p.Id,
                Name = p.Name,
                Barcode = p.Barcode,
                BrandName = p.BrandName,
                CategoryName = p.CategoryName,
                Price = p.Price,
                QuantityHere = s?.Quantity ?? 0,
                Threshold = s?.LowStockThreshold ?? p.LowStockThreshold,
                LastCost = lastCost.TryGetValue(p.Id, out var c) ? c : null
            };
        });

        if (lowStockOnly)
            rows = rows.Where(r => r.IsLowStock);

        return rows.ToList();
    }

    // ==================================================================
    //  إنشاء أمر شراء
    // ==================================================================

    public async Task<PurchaseResult> CreateAsync(
        CreatePurchaseRequest request, string userId, string userName)
    {
        var validation = await ValidateHeaderAsync(request);
        if (validation is not null) return validation;

        var lines = MergeLines(request.Lines);
        if (lines.Count == 0)
            return PurchaseResult.Fail("أضف صنفًا واحدًا على الأقل لأمر الشراء");

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId);
        if (supplier is null) return PurchaseResult.Fail("المورد غير موجود");
        if (!supplier.IsActive)
            return PurchaseResult.Fail($"المورد «{supplier.Name}» معطَّل — فعّله أولًا");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var order = new PurchaseOrder
            {
                PurchaseNumber = await GeneratePurchaseNumberAsync(now),
                SupplierId = supplier.Id,
                SupplierName = supplier.Name,   // snapshot: لا يتغير بتعديل المورد لاحقًا
                WarehouseId = request.WarehouseId,
                Status = PurchaseStatus.Draft,
                ExpectedDate = request.ExpectedDate,
                Notes = TrimOrNull(request.Notes, 500),
                CreatedByUserId = userId,
                CreatedByName = userName,
                CreatedAt = now
            };

            var buildResult = await BuildItemsAsync(order, lines, request.DiscountPercentage);
            if (buildResult is not null) return buildResult;

            _db.PurchaseOrders.Add(order);

            if (request.ConfirmImmediately)
            {
                order.Status = PurchaseStatus.Confirmed;
                order.ConfirmedByUserId = userId;
                order.ConfirmedByName = userName;
                order.ConfirmedAt = now;
            }

            var stateNote = request.ConfirmImmediately ? " واعتماده" : " كمسودة";
            _audit.Track(AuditActions.Purchase, nameof(PurchaseOrder), order.PurchaseNumber,
                $"إنشاء أمر شراء{stateNote} من «{order.SupplierName}» — " +
                $"{order.Items.Count} صنف، {order.Items.Sum(i => i.Quantity)} قطعة، " +
                $"إجمالي {order.Total:N2}");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            var msg = request.ConfirmImmediately
                ? $"تم إنشاء أمر الشراء {order.PurchaseNumber} واعتماده — بانتظار التوريد"
                : $"تم حفظ أمر الشراء {order.PurchaseNumber} كمسودة";

            return PurchaseResult.Ok(msg, order);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل إنشاء أمر شراء");
            return PurchaseResult.Fail("حدث خطأ أثناء حفظ أمر الشراء، حاول مرة أخرى");
        }
    }

    public async Task<PurchaseResult> UpdateAsync(
        int id, CreatePurchaseRequest request, string userId, string userName)
    {
        var order = await LoadAsync(id);
        if (order is null) return PurchaseResult.Fail("أمر الشراء غير موجود");

        // بعد الاعتماد صار الأمر التزامًا مُرسَلًا للمورد؛ تعديله يجعل الورقة
        // التي بيد المورد مختلفة عمّا في النظام
        if (!order.CanEdit)
            return PurchaseResult.Fail(
                $"لا يمكن تعديل أمر حالته «{PurchaseStatusLabel.Of(order.Status)}» — " +
                "المسودة وحدها قابلة للتعديل");

        var validation = await ValidateHeaderAsync(request);
        if (validation is not null) return validation;

        var lines = MergeLines(request.Lines);
        if (lines.Count == 0)
            return PurchaseResult.Fail("أضف صنفًا واحدًا على الأقل لأمر الشراء");

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId);
        if (supplier is null) return PurchaseResult.Fail("المورد غير موجود");
        if (!supplier.IsActive)
            return PurchaseResult.Fail($"المورد «{supplier.Name}» معطَّل — فعّله أولًا");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // المسودة بلا استلام، فحذف سطورها وإعادة بنائها آمن
            _db.PurchaseOrderItems.RemoveRange(order.Items);
            order.Items.Clear();

            order.SupplierId = supplier.Id;
            order.SupplierName = supplier.Name;
            order.WarehouseId = request.WarehouseId;
            order.ExpectedDate = request.ExpectedDate;
            order.Notes = TrimOrNull(request.Notes, 500);

            var buildResult = await BuildItemsAsync(order, lines, request.DiscountPercentage);
            if (buildResult is not null) return buildResult;

            if (request.ConfirmImmediately)
            {
                order.Status = PurchaseStatus.Confirmed;
                order.ConfirmedByUserId = userId;
                order.ConfirmedByName = userName;
                order.ConfirmedAt = DateTime.Now;
            }

            _audit.Track(AuditActions.Update, nameof(PurchaseOrder), order.PurchaseNumber,
                $"تعديل أمر الشراء — {order.Items.Count} صنف، " +
                $"{order.Items.Sum(i => i.Quantity)} قطعة، إجمالي {order.Total:N2}" +
                (request.ConfirmImmediately ? " ثم اعتماده" : ""));

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return PurchaseResult.Ok(
                $"تم تحديث أمر الشراء {order.PurchaseNumber}", order);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل تعديل أمر الشراء {Id}", id);
            return PurchaseResult.Fail("حدث خطأ أثناء التعديل، حاول مرة أخرى");
        }
    }

    // ==================================================================
    //  الاعتماد
    // ==================================================================

    public async Task<PurchaseResult> ConfirmAsync(int id, string userId, string userName)
    {
        var order = await LoadAsync(id);
        if (order is null) return PurchaseResult.Fail("أمر الشراء غير موجود");

        if (!order.CanConfirm)
            return PurchaseResult.Fail(
                $"الأمر حالته «{PurchaseStatusLabel.Of(order.Status)}» ولا يحتاج اعتمادًا");

        if (order.Items.Count == 0)
            return PurchaseResult.Fail("لا يمكن اعتماد أمر بلا أصناف");

        order.Status = PurchaseStatus.Confirmed;
        order.ConfirmedByUserId = userId;
        order.ConfirmedByName = userName;
        order.ConfirmedAt = DateTime.Now;

        // الاعتماد لا يُدخل قطعة واحدة المخزون — هو التزام تجاه المورد فقط
        _audit.Track(AuditActions.Approve, nameof(PurchaseOrder), order.PurchaseNumber,
            $"اعتماد أمر الشراء من «{order.SupplierName}» بإجمالي {order.Total:N2} — " +
            "بانتظار التوريد (لم يُضف للمخزون)");

        await _db.SaveChangesAsync();

        return PurchaseResult.Ok(
            $"تم اعتماد أمر الشراء {order.PurchaseNumber} — " +
            "لن تُضاف الكميات للمخزون قبل تسجيل الاستلام", order);
    }

    // ==================================================================
    //  الاستلام — النقطة الوحيدة التي يزيد فيها المخزون
    // ==================================================================

    public async Task<PurchaseResult> ReceiveAsync(int id, Dictionary<int, int> quantities,
        string? notes, string userId, string userName)
    {
        var order = await LoadAsync(id);
        if (order is null) return PurchaseResult.Fail("أمر الشراء غير موجود");

        if (!order.CanReceive)
            return PurchaseResult.Fail(
                $"لا يمكن الاستلام وحالة الأمر «{PurchaseStatusLabel.Of(order.Status)}»");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var receipt = new PurchaseReceipt
            {
                ReceiptNumber = await GenerateReceiptNumberAsync(now),
                PurchaseOrderId = order.Id,
                // المخزن مُكرَّر من الأمر عن قصد: لو نُقل الأمر لمخزن آخر
                // لاحقًا تبقى هذه الدفعة مسجّلة على مخزنها الحقيقي
                WarehouseId = order.WarehouseId,
                ReceivedByUserId = userId,
                ReceivedByName = userName,
                Notes = TrimOrNull(notes, 500),
                CreatedAt = now
            };

            var movements = new List<StockMovement>();
            var totalReceived = 0;

            foreach (var item in order.Items)
            {
                // الافتراض: استلام المتبقّي كاملًا. الأقل مقبول (توريد جزئي)،
                // والأكثر مرفوض لأنه توريد لم يُطلب ولا سعر متفق عليه له.
                var qty = quantities.TryGetValue(item.Id, out var v)
                    ? v : item.RemainingQuantity;

                if (qty < 0)
                    return PurchaseResult.Fail(
                        $"كمية سالبة غير مقبولة للصنف «{item.ProductName}»");

                if (qty > item.RemainingQuantity)
                    return PurchaseResult.Fail(
                        $"لا يمكن استلام {qty} من «{item.ProductName}» " +
                        $"والمتبقّي {item.RemainingQuantity} فقط");

                if (qty == 0) continue;   // صنف لم يصل في هذه الدفعة

                var stockResult = await _inventory.ApplyAsync(new StockChangeRequest
                {
                    ProductId = item.ProductId,
                    WarehouseId = order.WarehouseId,
                    Change = +qty,                              // دخول للمخزن
                    Reason = StockMovementReason.Purchase,
                    Note = $"استلام {receipt.ReceiptNumber} من أمر الشراء " +
                           $"{order.PurchaseNumber} — المورد «{order.SupplierName}»",
                    UserId = userId
                });

                if (!stockResult.Success)
                    return PurchaseResult.Fail($"«{item.ProductName}»: {stockResult.Message}");

                item.ReceivedQuantity += qty;
                totalReceived += qty;

                receipt.Items.Add(new PurchaseReceiptItem
                {
                    PurchaseOrderItemId = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = qty
                });

                if (stockResult.Movement is not null) movements.Add(stockResult.Movement);
            }

            if (totalReceived == 0)
                return PurchaseResult.Fail("لم تُحدَّد أي كمية للاستلام");

            _db.PurchaseReceipts.Add(receipt);

            // الحالة تُشتق من الكميات لا من إدخال المستخدم: لا فرصة لأمر
            // مكتمل الكميات وحالته «استلام جزئي»
            var fullyReceived = order.Items.All(i => i.RemainingQuantity <= 0);
            order.Status = fullyReceived
                ? PurchaseStatus.Received
                : PurchaseStatus.PartiallyReceived;
            if (fullyReceived) order.CompletedAt = now;

            _audit.Track(AuditActions.Receive, nameof(PurchaseReceipt), receipt.ReceiptNumber,
                $"استلام {totalReceived} قطعة من أمر الشراء {order.PurchaseNumber} " +
                $"إلى «{order.Warehouse?.Name}»" +
                (fullyReceived ? " — اكتمل الأمر" : $" — متبقٍ {order.TotalRemaining} قطعة"));

            // أول SaveChanges يُولّد معرّف الإذن، فنربط به حركات المخزون
            // بعده — نفس نمط المرتجعات، وكله داخل نفس الـ transaction
            await _db.SaveChangesAsync();

            foreach (var movement in movements)
                movement.ReferencePurchaseReceiptId = receipt.Id;

            if (movements.Count > 0)
                await _db.SaveChangesAsync();

            await tx.CommitAsync();

            var suffix = fullyReceived
                ? " — اكتمل استلام الأمر"
                : $" — ما زال متبقيًا {order.TotalRemaining} قطعة";

            return PurchaseResult.Ok(
                $"تم استلام {totalReceived} قطعة بإذن {receipt.ReceiptNumber} " +
                $"وإضافتها لمخزن «{order.Warehouse?.Name}»{suffix}",
                order, receipt);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل استلام أمر الشراء {Id}", id);
            return PurchaseResult.Fail("حدث خطأ أثناء الاستلام، حاول مرة أخرى");
        }
    }

    // ==================================================================
    //  الإلغاء والحذف
    // ==================================================================

    public async Task<PurchaseResult> CancelAsync(
        int id, string? reason, string userId, string userName)
    {
        var order = await LoadAsync(id);
        if (order is null) return PurchaseResult.Fail("أمر الشراء غير موجود");

        if (!order.CanCancel)
            return PurchaseResult.Fail(
                $"لا يمكن إلغاء أمر حالته «{PurchaseStatusLabel.Of(order.Status)}»");

        var cleanReason = TrimOrNull(reason, 300);
        if (cleanReason is null)
            return PurchaseResult.Fail(
                "سبب الإلغاء مطلوب — بدونه لا يُعرف لاحقًا لماذا أُلغي الأمر");

        var received = order.TotalReceived;

        order.Status = PurchaseStatus.Cancelled;
        order.CancellationReason = cleanReason;
        order.CompletedAt = DateTime.Now;

        // ⭐ ما استُلم لا يُسحب من المخزون: القطع موجودة في المخزن فعلًا،
        // وسحبها بالإلغاء يجعل الرصيد أقل من الواقع. الإلغاء يُغلق المتبقّي.
        var stockNote = received > 0
            ? $" — {received} قطعة مستلمة تبقى في المخزون (موجودة فعلًا)"
            : "";

        _audit.Track(AuditActions.Reject, nameof(PurchaseOrder), order.PurchaseNumber,
            $"إلغاء أمر الشراء من «{order.SupplierName}» — السبب: {cleanReason}{stockNote}");

        await _db.SaveChangesAsync();

        return PurchaseResult.Ok(
            $"تم إلغاء أمر الشراء {order.PurchaseNumber}" +
            (received > 0
                ? $" — الكميات المستلمة ({received} قطعة) تبقى في المخزون"
                : ""),
            order);
    }

    public async Task<PurchaseResult> DeleteDraftAsync(int id, string userId, string userName)
    {
        var order = await LoadAsync(id);
        if (order is null) return PurchaseResult.Fail("أمر الشراء غير موجود");

        // المسودة وحدها تُحذف: لم تُرسَل لمورد ولم تُدخل قطعة، فلا أثر لها
        // يُفقد. غير ذلك يُلغى ليبقى في السجل بسببه.
        if (order.Status != PurchaseStatus.Draft)
            return PurchaseResult.Fail(
                $"لا يُحذف إلا المسودة — أمر حالته «{PurchaseStatusLabel.Of(order.Status)}» " +
                "يُلغى ليبقى في السجل بسبب إلغائه");

        var number = order.PurchaseNumber;
        _db.PurchaseOrderItems.RemoveRange(order.Items);
        _db.PurchaseOrders.Remove(order);

        _audit.Track(AuditActions.Delete, nameof(PurchaseOrder), number,
            $"حذف مسودة أمر الشراء من «{order.SupplierName}»");

        await _db.SaveChangesAsync();

        return PurchaseResult.Ok($"تم حذف مسودة أمر الشراء {number}");
    }

    // ==================================================================
    //  أذون الاستلام
    // ==================================================================

    public Task<PurchaseReceipt?> GetReceiptAsync(int receiptId) =>
        _db.PurchaseReceipts.AsNoTracking()
            .Include(r => r.Warehouse)
            .Include(r => r.PurchaseOrder)!
                .ThenInclude(o => o!.Supplier)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == receiptId);

    public Task<List<PurchaseReceipt>> ListReceiptsAsync(
        int? warehouseId, DateTime? from, DateTime? to, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);

        var query = _db.PurchaseReceipts.AsNoTracking()
            .Include(r => r.Warehouse)
            .Include(r => r.PurchaseOrder)
            .Include(r => r.Items)
            .AsQueryable();

        if (warehouseId is > 0) query = query.Where(r => r.WarehouseId == warehouseId);
        if (from.HasValue) query = query.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(r => r.CreatedAt < to.Value);

        return query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync();
    }

    public Task<int> CountOpenAsync(int? warehouseId = null)
    {
        var query = _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Status == PurchaseStatus.Confirmed ||
                        o.Status == PurchaseStatus.PartiallyReceived);

        if (warehouseId is > 0) query = query.Where(o => o.WarehouseId == warehouseId);

        return query.CountAsync();
    }

    // ==================================================================
    //  تقرير المشتريات
    // ==================================================================

    public async Task<PurchaseReportVm> BuildReportAsync(
        DateTime? from, DateTime? to, int? warehouseId)
    {
        var query = _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Status != PurchaseStatus.Cancelled);

        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAt < to.Value);
        if (warehouseId is > 0) query = query.Where(o => o.WarehouseId == warehouseId);

        // القراءة في الذاكرة مرة واحدة: SQLite لا يجمع decimal، والفترة
        // المعروضة محدودة عمليًا، فاستعلام واحد أرخص من أربع تجميعات
        var orders = await query
            .Select(o => new
            {
                o.Id,
                o.SupplierName,
                WarehouseName = o.Warehouse!.Name,
                o.Total,
                o.CreatedAt,
                Ordered = o.Items.Sum(i => i.Quantity),
                Received = o.Items.Sum(i => i.ReceivedQuantity)
            })
            .ToListAsync();

        var itemsQuery = _db.PurchaseOrderItems.AsNoTracking()
            .Where(i => i.PurchaseOrder!.Status != PurchaseStatus.Cancelled);

        if (from.HasValue) itemsQuery = itemsQuery.Where(i => i.PurchaseOrder!.CreatedAt >= from.Value);
        if (to.HasValue) itemsQuery = itemsQuery.Where(i => i.PurchaseOrder!.CreatedAt < to.Value);
        if (warehouseId is > 0) itemsQuery = itemsQuery.Where(i => i.PurchaseOrder!.WarehouseId == warehouseId);

        var items = await itemsQuery
            .Select(i => new { i.ProductName, i.Quantity, i.LineTotal, i.PurchaseOrderId })
            .ToListAsync();

        return new PurchaseReportVm
        {
            From = from,
            To = to,
            WarehouseId = warehouseId,

            BySupplier = orders
                .GroupBy(o => o.SupplierName)
                .Select(g => new PurchaseReportRowVm
                {
                    Label = g.Key,
                    OrderCount = g.Count(),
                    Pieces = g.Sum(o => o.Ordered),
                    Total = g.Sum(o => o.Total)
                })
                .OrderByDescending(r => r.Total)
                .ToList(),

            ByWarehouse = orders
                .GroupBy(o => o.WarehouseName)
                .Select(g => new PurchaseReportRowVm
                {
                    Label = g.Key,
                    OrderCount = g.Count(),
                    Pieces = g.Sum(o => o.Ordered),
                    Total = g.Sum(o => o.Total)
                })
                .OrderByDescending(r => r.Total)
                .ToList(),

            ByDay = orders
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new PurchaseReportRowVm
                {
                    Label = g.Key.ToString("yyyy-MM-dd"),
                    OrderCount = g.Count(),
                    Pieces = g.Sum(o => o.Ordered),
                    Total = g.Sum(o => o.Total)
                })
                .OrderByDescending(r => r.Label)
                .Take(60)
                .ToList(),

            TopProducts = items
                .GroupBy(i => i.ProductName)
                .Select(g => new PurchaseReportRowVm
                {
                    Label = g.Key,
                    OrderCount = g.Select(x => x.PurchaseOrderId).Distinct().Count(),
                    Pieces = g.Sum(x => x.Quantity),
                    Total = g.Sum(x => x.LineTotal)
                })
                .OrderByDescending(r => r.Pieces)
                .Take(20)
                .ToList(),

            GrandTotal = orders.Sum(o => o.Total),
            OrderCount = orders.Count,
            PiecesOrdered = orders.Sum(o => o.Ordered),
            PiecesReceived = orders.Sum(o => o.Received)
        };
    }

    // ==================================================================
    //  أدوات داخلية
    // ==================================================================

    private Task<PurchaseOrder?> LoadAsync(int id) =>
        _db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.Warehouse)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

    private async Task<PurchaseResult?> ValidateHeaderAsync(CreatePurchaseRequest request)
    {
        if (request.SupplierId <= 0)
            return PurchaseResult.Fail("يجب اختيار المورد");

        if (request.WarehouseId <= 0)
            return PurchaseResult.Fail("يجب اختيار المخزن المستقبِل");

        if (request.DiscountPercentage is < 0 or > 100)
            return PurchaseResult.Fail("نسبة الخصم يجب أن تكون بين 0 و 100");

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId);

        if (warehouse is null)
            return PurchaseResult.Fail("المخزن المستقبِل غير موجود");

        // شراء لمخزن معطَّل يُدخل بضاعة لمكان لا يُباع منه ولا يُحوَّل عنه
        if (!warehouse.IsActive)
            return PurchaseResult.Fail($"المخزن «{warehouse.Name}» معطَّل — اختر مخزنًا نشطًا");

        return null;
    }

    /// <summary>
    /// يدمج السطور المكرّرة لنفس المنتج بجمع الكميات. بلا الدمج ينشأ سطران
    /// لصنف واحد فتظهر شاشة الاستلام صفَّين متطابقين لا يُعرف أيهما وصل.
    /// السطر ذو التكلفة الأعلى يُرجَّح: الأمان في تقدير الالتزام المالي.
    /// </summary>
    private static List<PurchaseLineInput> MergeLines(List<PurchaseLineInput> lines)
    {
        return lines
            .Where(l => l.ProductId > 0 && l.Quantity > 0)
            .GroupBy(l => l.ProductId)
            .Select(g => new PurchaseLineInput
            {
                ProductId = g.Key,
                Quantity = g.Sum(l => l.Quantity),
                UnitCost = g.Max(l => l.UnitCost)
            })
            .ToList();
    }

    /// <summary>
    /// يبني سطور الأمر ويحسب مبالغه. يعيد <c>null</c> عند النجاح، أو نتيجة
    /// فاشلة عند أول خطأ — فلا يُحفظ أمر نصفه صحيح.
    /// </summary>
    private async Task<PurchaseResult?> BuildItemsAsync(
        PurchaseOrder order, List<PurchaseLineInput> lines, decimal discountPercentage)
    {
        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var subTotal = 0m;

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                return PurchaseResult.Fail($"صنف غير موجود (معرّف {line.ProductId})");

            if (line.Quantity is < 1 or > 100000)
                return PurchaseResult.Fail(
                    $"كمية «{product.Name}» يجب أن تكون بين 1 و 100000");

            if (line.UnitCost < 0)
                return PurchaseResult.Fail($"تكلفة «{product.Name}» لا تكون سالبة");

            var unitCost = Round(line.UnitCost);
            var lineTotal = Round(unitCost * line.Quantity);

            order.Items.Add(new PurchaseOrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,   // snapshot: تعديل اسم المنتج لا يغيّر الأمر
                Barcode = product.Barcode,
                UnitCost = unitCost,
                Quantity = line.Quantity,
                ReceivedQuantity = 0,
                LineTotal = lineTotal
            });

            subTotal += lineTotal;
        }

        // المبالغ تُخزَّن لا تُحسب عند العرض: إعادة حسابها لاحقًا بتكلفة
        // مختلفة تُعطي رقمًا غير الذي اتُّفق عليه مع المورد
        order.SubTotal = Round(subTotal);
        order.DiscountPercentage = Round(discountPercentage);
        order.DiscountAmount = Round(order.SubTotal * order.DiscountPercentage / 100m);
        order.Total = Round(order.SubTotal - order.DiscountAmount);

        return null;
    }

    /// <summary>
    /// رقم مرجعي بصيغة PO-yyyyMMdd-0001 — نفس منطق ترقيم الفواتير
    /// والمرتجعات، ومع الفهرس الفريد يفشل الحفظ عند التعارض لا يُنتج مكررًا.
    /// </summary>
    private async Task<string> GeneratePurchaseNumberAsync(DateTime date)
    {
        var prefix = $"PO-{date:yyyyMMdd}-";
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var countToday = await _db.PurchaseOrders
            .CountAsync(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = prefix + (countToday + attempt).ToString("D4");
            if (!await _db.PurchaseOrders.AnyAsync(o => o.PurchaseNumber == candidate))
                return candidate;
        }

        return prefix + DateTime.Now.ToString("HHmmssfff");
    }

    private async Task<string> GenerateReceiptNumberAsync(DateTime date)
    {
        var prefix = $"RCV-{date:yyyyMMdd}-";
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var countToday = await _db.PurchaseReceipts
            .CountAsync(r => r.CreatedAt >= dayStart && r.CreatedAt < dayEnd);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = prefix + (countToday + attempt).ToString("D4");
            if (!await _db.PurchaseReceipts.AnyAsync(r => r.ReceiptNumber == candidate))
                return candidate;
        }

        return prefix + DateTime.Now.ToString("HHmmssfff");
    }

    private static string? TrimOrNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..max];
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

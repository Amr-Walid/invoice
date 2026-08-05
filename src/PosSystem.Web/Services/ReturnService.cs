using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;

namespace PosSystem.Web.Services;

public interface IReturnService
{
    Task<List<InvoiceSearchRowVm>> SearchInvoicesAsync(string? query, string? restrictToAgentId, int take = 20);
    Task<ReturnFormVm?> BuildFormAsync(int invoiceId, string? restrictToAgentId);
    Task<CreateReturnResult> CreateReturnAsync(CreateReturnRequest request, string userId, string userName);
    /// <summary>
    /// قائمة المرتجعات. عند تمرير <paramref name="restrictToUserId"/> يرى
    /// المندوب المرتجعات التي نفّذها <b>أو</b> الواقعة على فواتيره.
    /// الشرط الثاني مقصود: لو نفّذ المدير مرتجعًا لفاتورة مندوب، فمن حقّ
    /// المندوب أن يعرف أن فاتورته نُقصت — وإلا لما فهم فرق أرقامه.
    /// </summary>
    Task<ReturnListVm> ListAsync(string? q, DateTime? from, DateTime? to, int page,
        string? restrictToUserId, int pageSize = 20);
    Task<Return?> GetDetailsAsync(int id, string? restrictToUserId);
}

/// <summary>
/// منطق المرتجعات.
///
/// المبادئ المُطبَّقة:
///  1. الفاتورة الأصلية لا تُعدَّل ولا تُحذف — المرتجع حركة معاكسة مستقلة
///     مرتبطة بها. فتبقى المبيعات التاريخية والتقارير القديمة صحيحة.
///  2. الأسعار تُقرأ من سطر الفاتورة الأصلي، لا من المنتج الحالي ولا من
///     المتصفح. فلو غلا المنتج بعد البيع لا يُرد للعميل أكثر مما دفع.
///  3. الخصم يُطبَّق بنفس نسبة الفاتورة على قيمة المُرجَع — وإلا لاسترد
///     العميل السعر الكامل لصنف اشتراه مخفَّضًا.
///  4. كل شيء داخل transaction واحدة: المرتجع وإعادة المخزون وتحديث حالة
///     الفاتورة تحدث معًا أو لا تحدث.
///  5. إعادة قراءة الكميات داخل الـ transaction تمنع الإرجاع المزدوج عند
///     ضغط «تم» مرتين أو من نافذتين في نفس الوقت.
/// </summary>
public class ReturnService : IReturnService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IInventoryService _inventory;
    private readonly ILogger<ReturnService> _logger;

    public ReturnService(AppDbContext db, IAuditService audit,
        IInventoryService inventory, ILogger<ReturnService> logger)
    {
        _db = db;
        _audit = audit;
        _inventory = inventory;
        _logger = logger;
    }

    // ==================================================================
    //  البحث عن الفاتورة المطلوب إرجاعها
    // ==================================================================
    public async Task<List<InvoiceSearchRowVm>> SearchInvoicesAsync(
        string? query, string? restrictToAgentId, int take = 20)
    {
        take = Math.Clamp(take, 1, 100);

        var q = _db.Invoices.AsNoTracking()
            .Include(i => i.Agent)
            .Include(i => i.Items)
            .Where(i => i.Status != InvoiceStatus.Cancelled);

        // المندوب لا يُرجع إلا فواتيره — نفس قاعدة العزل المطبَّقة في التقارير
        if (!string.IsNullOrEmpty(restrictToAgentId))
            q = q.Where(i => i.AgentId == restrictToAgentId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var normalizedPhone = PhoneHelper.Normalize(term);

            // البحث برقم الهاتف يستخدم الصيغة الموحَّدة عبر جدول العملاء،
            // فيجد الفاتورة أيًّا كانت طريقة كتابة الرقم.
            if (normalizedPhone.Length >= 6)
            {
                q = q.Where(i =>
                    i.InvoiceNumber.Contains(term) ||
                    (i.CustomerPhone != null && i.CustomerPhone.Contains(term)) ||
                    (i.Customer != null && i.Customer.PhoneNormalized.Contains(normalizedPhone)));
            }
            else
            {
                q = q.Where(i =>
                    i.InvoiceNumber.Contains(term) ||
                    (i.CustomerName != null && i.CustomerName.Contains(term)) ||
                    (i.CustomerPhone != null && i.CustomerPhone.Contains(term)));
            }
        }

        var rows = await q
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(take)
            .Select(i => new InvoiceSearchRowVm
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                CreatedAt = i.CreatedAt,
                CustomerName = i.CustomerName,
                CustomerPhone = i.CustomerPhone,
                AgentName = i.Agent!.FullName,
                Total = i.Total,
                RefundedAmount = i.RefundedAmount,
                Status = i.Status,
                TotalQuantity = i.Items.Sum(x => x.Quantity),
                ReturnedQuantity = i.Items.Sum(x => x.ReturnedQuantity)
            })
            .ToListAsync();

        return rows;
    }

    // ==================================================================
    //  بناء شاشة الإرجاع
    // ==================================================================
    public async Task<ReturnFormVm?> BuildFormAsync(int invoiceId, string? restrictToAgentId)
    {
        var q = _db.Invoices.AsNoTracking()
            .Include(i => i.Agent)
            .Include(i => i.Items)
            .Where(i => i.Id == invoiceId);

        if (!string.IsNullOrEmpty(restrictToAgentId))
            q = q.Where(i => i.AgentId == restrictToAgentId);

        var invoice = await q.FirstOrDefaultAsync();
        if (invoice is null) return null;

        return new ReturnFormVm
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.CreatedAt,
            AgentName = invoice.Agent?.FullName ?? "—",
            CustomerName = invoice.CustomerName,
            CustomerPhone = invoice.CustomerPhone,
            InvoiceTotal = invoice.Total,
            DiscountPercentage = invoice.DiscountPercentage,
            AlreadyRefunded = invoice.RefundedAmount,
            Status = invoice.Status,
            Items = invoice.Items
                .OrderBy(it => it.Id)
                .Select(it => new ReturnableItemVm
                {
                    InvoiceItemId = it.Id,
                    ProductId = it.ProductId,
                    ProductName = it.ProductName,
                    Barcode = it.Barcode,
                    UnitPrice = it.UnitPrice,
                    SoldQuantity = it.Quantity,
                    AlreadyReturned = it.ReturnedQuantity,
                    QuantityToReturn = 0
                })
                .ToList()
        };
    }

    // ==================================================================
    //  تنفيذ الإرجاع
    // ==================================================================
    public async Task<CreateReturnResult> CreateReturnAsync(
        CreateReturnRequest request, string userId, string userName)
    {
        if (request is null)
            return CreateReturnResult.Fail("لم تُرسل بيانات المرتجع");

        if (string.IsNullOrWhiteSpace(userId))
            return CreateReturnResult.Fail("تعذر التعرف على المستخدم الحالي");

        // دمج الأسطر المكررة لنفس سطر الفاتورة، وتجاهل الكميات الصفرية
        var requested = (request.Items ?? new List<ReturnLineDto>())
            .Where(l => l.Quantity > 0)
            .GroupBy(l => l.InvoiceItemId)
            .Select(g => new { InvoiceItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (requested.Count == 0)
            return CreateReturnResult.Fail("اختر صنفًا واحدًا على الأقل وحدّد الكمية المُرجَعة");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // إعادة القراءة داخل الـ transaction — مصدر الحقيقة الوحيد،
            // ويمنع الإرجاع المزدوج لو وصل الطلب مرتين.
            var invoice = await _db.Invoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == request.InvoiceId);

            if (invoice is null)
                return CreateReturnResult.Fail("الفاتورة غير موجودة");

            if (invoice.Status == InvoiceStatus.Cancelled)
                return CreateReturnResult.Fail("لا يمكن إرجاع أصناف من فاتورة ملغاة");

            var itemIds = requested.Select(r => r.InvoiceItemId).ToList();
            var invoiceItemIds = invoice.Items.Select(i => i.Id).ToHashSet();

            if (itemIds.Any(id => !invoiceItemIds.Contains(id)))
                return CreateReturnResult.Fail("أحد الأصناف المطلوب إرجاعها لا ينتمي لهذه الفاتورة");

            var productIds = invoice.Items
                .Where(i => itemIds.Contains(i.Id))
                .Select(i => i.ProductId)
                .Distinct()
                .ToList();

            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();

            var ret = new Return
            {
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                ProcessedByUserId = userId,
                ProcessedByName = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName,
                CustomerId = invoice.CustomerId,
                CustomerName = invoice.CustomerName,
                CustomerPhone = invoice.CustomerPhone,
                Reason = request.Reason,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes!.Trim(),
                RestockedToInventory = request.RestockToInventory,
                DiscountPercentage = invoice.DiscountPercentage,
                // القطعة تعود لمخزن الفاتورة لا لمخزن من يُنفّذ المرتجع.
                // لو نفّذ مدير مرتجعًا لفاتورة فرع آخر فلا يجوز أن تنتقل
                // القطعة لمخزنه — ذلك تحويل بلا مستند ويُنتج عجزًا في الفرع.
                WarehouseId = invoice.WarehouseId,
                CreatedAt = DateTime.Now
            };

            decimal subTotal = 0m;
            var restockedQuantity = 0;

            // نحتفظ بمرجع لحركات المخزون لأن Id المرتجع لا يوجد قبل الحفظ،
            // فنربطها به بعد أول SaveChanges وداخل نفس الـ transaction.
            var movements = new List<StockMovement>();

            foreach (var line in requested)
            {
                var invoiceItem = invoice.Items.First(i => i.Id == line.InvoiceItemId);
                var remaining = invoiceItem.Quantity - invoiceItem.ReturnedQuantity;

                if (remaining <= 0)
                    return CreateReturnResult.Fail(
                        $"الصنف «{invoiceItem.ProductName}» أُرجع بالكامل من قبل");

                if (line.Quantity > remaining)
                    return CreateReturnResult.Fail(
                        $"لا يمكن إرجاع {line.Quantity} من «{invoiceItem.ProductName}» — " +
                        $"المتبقي القابل للإرجاع {remaining} فقط");

                // السعر من سطر الفاتورة الأصلي — لا من المنتج الحالي
                var lineTotal = Round(invoiceItem.UnitPrice * line.Quantity);
                subTotal += lineTotal;

                ret.Items.Add(new ReturnItem
                {
                    InvoiceItemId = invoiceItem.Id,
                    ProductId = invoiceItem.ProductId,
                    ProductName = invoiceItem.ProductName,
                    Barcode = invoiceItem.Barcode,
                    UnitPrice = invoiceItem.UnitPrice,
                    Quantity = line.Quantity,
                    LineTotal = lineTotal
                });

                invoiceItem.ReturnedQuantity += line.Quantity;

                // ===== إعادة الكمية لمخزن الفاتورة =====
                if (request.RestockToInventory)
                {
                    var stockResult = await _inventory.ApplyAsync(new StockChangeRequest
                    {
                        ProductId = invoiceItem.ProductId,
                        WarehouseId = invoice.WarehouseId,
                        Change = line.Quantity,               // موجب: دخول للمخزون
                        Reason = StockMovementReason.Return,
                        ReferenceInvoiceId = invoice.Id,
                        UserId = userId
                    });

                    if (!stockResult.Success)
                        return CreateReturnResult.Fail(stockResult.Message);

                    restockedQuantity += line.Quantity;
                    if (stockResult.Movement is not null) movements.Add(stockResult.Movement);
                }
            }

            // ===== حساب المبلغ المُرَد بنفس نسبة خصم الفاتورة =====
            ret.SubTotal = Round(subTotal);
            ret.DiscountAmount = invoice.DiscountPercentage > 0
                ? Round(ret.SubTotal * invoice.DiscountPercentage / 100m)
                : 0m;
            ret.RefundAmount = Round(ret.SubTotal - ret.DiscountAmount);

            // حماية حسابية: لا يُرد أكثر من صافي الفاتورة مطلقًا. لو أنتج
            // تقريب القروش فرقًا في آخر صنف مُرجَع، نُقيّده بالمتبقي.
            var maxRefundable = Round(invoice.Total - invoice.RefundedAmount);
            if (ret.RefundAmount > maxRefundable)
                ret.RefundAmount = maxRefundable < 0 ? 0m : maxRefundable;

            ret.ReturnNumber = await GenerateReturnNumberAsync(ret.CreatedAt);

            _db.Returns.Add(ret);

            // ===== تحديث حالة الفاتورة =====
            invoice.RefundedAmount = Round(invoice.RefundedAmount + ret.RefundAmount);

            var totalSold = invoice.Items.Sum(i => i.Quantity);
            var totalReturned = invoice.Items.Sum(i => i.ReturnedQuantity);

            invoice.Status = totalReturned >= totalSold
                ? InvoiceStatus.FullyReturned
                : totalReturned > 0
                    ? InvoiceStatus.PartiallyReturned
                    : InvoiceStatus.Completed;

            var restockNote = request.RestockToInventory
                ? $"أُعيد للمخزون {restockedQuantity} قطعة"
                : "بدون إعادة للمخزون";

            _audit.Track(AuditActions.Return, nameof(Return), ret.ReturnNumber,
                $"مرتجع للفاتورة {invoice.InvoiceNumber} — " +
                $"أصناف {ret.Items.Count} — قطع {ret.TotalQuantity} — " +
                $"مُرَد {ret.RefundAmount:0.00} — {ReturnReasons.Label(ret.Reason)} — {restockNote}");

            await _db.SaveChangesAsync();

            // الآن صار للمرتجع Id، فنربط حركات المخزون به ليصبح أثر كل قطعة
            // قابلًا للتتبّع من الحركة إلى المرتجع إلى الفاتورة.
            if (movements.Count > 0)
            {
                foreach (var movement in movements) movement.ReferenceReturnId = ret.Id;
                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();

            return new CreateReturnResult
            {
                Success = true,
                ReturnId = ret.Id,
                ReturnNumber = ret.ReturnNumber,
                RefundAmount = ret.RefundAmount,
                RestockedQuantity = restockedQuantity,
                InvoiceFullyReturned = invoice.Status == InvoiceStatus.FullyReturned,
                Message = request.RestockToInventory
                    ? $"تم تسجيل المرتجع {ret.ReturnNumber} وإعادة {restockedQuantity} قطعة للمخزون"
                    : $"تم تسجيل المرتجع {ret.ReturnNumber} بدون إعادة الأصناف للمخزون"
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل تسجيل المرتجع للفاتورة {InvoiceId}", request.InvoiceId);
            return CreateReturnResult.Fail("حدث خطأ أثناء تسجيل المرتجع، حاول مرة أخرى");
        }
    }

    // ==================================================================
    //  قائمة المرتجعات
    // ==================================================================
    public async Task<ReturnListVm> ListAsync(string? q, DateTime? from, DateTime? to,
        int page, string? restrictToUserId, int pageSize = 20)
    {
        if (pageSize <= 0) pageSize = 20;

        var query = _db.Returns.AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.ProcessedByUser)
            .AsQueryable();

        if (!string.IsNullOrEmpty(restrictToUserId))
            query = query.Where(r => r.ProcessedByUserId == restrictToUserId ||
                                     r.Invoice!.AgentId == restrictToUserId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(r =>
                r.ReturnNumber.Contains(term) ||
                r.InvoiceNumber.Contains(term) ||
                (r.CustomerName != null && r.CustomerName.Contains(term)) ||
                (r.CustomerPhone != null && r.CustomerPhone.Contains(term)) ||
                r.ProcessedByName.Contains(term));
        }

        if (from.HasValue) query = query.Where(r => r.CreatedAt >= from.Value.Date);
        // النهاية شاملة ليومها — نفس السلوك المُصلَح في «سجل العمليات»
        if (to.HasValue) query = query.Where(r => r.CreatedAt < to.Value.Date.AddDays(1));

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page <= 0 ? 1 : page, 1, totalPages);

        var returns = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)   // ترتيب مستقر داخل الثانية نفسها
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // SQLite لا يدعم SUM على decimal، فنجمع على العميل في هذه الحالة فقط
        decimal totalRefunded;
        int totalItems;
        if (_db.Database.IsSqlite())
        {
            var rows = await query.Select(r => new
            {
                r.RefundAmount,
                Qty = r.Items.Sum(i => i.Quantity)
            }).ToListAsync();
            totalRefunded = rows.Sum(r => r.RefundAmount);
            totalItems = rows.Sum(r => r.Qty);
        }
        else
        {
            totalRefunded = await query.SumAsync(r => (decimal?)r.RefundAmount) ?? 0m;
            totalItems = await query.SelectMany(r => r.Items).SumAsync(i => (int?)i.Quantity) ?? 0;
        }

        return new ReturnListVm
        {
            Query = q,
            From = from,
            To = to,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total,
            Returns = returns,
            TotalRefunded = totalRefunded,
            TotalItemsReturned = totalItems,
            IsAdmin = string.IsNullOrEmpty(restrictToUserId)
        };
    }

    public async Task<Return?> GetDetailsAsync(int id, string? restrictToUserId)
    {
        var query = _db.Returns.AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.ProcessedByUser)
            .Include(r => r.Customer)
            .Include(r => r.Invoice)!
                .ThenInclude(i => i!.Agent)
            .Where(r => r.Id == id);

        if (!string.IsNullOrEmpty(restrictToUserId))
            query = query.Where(r => r.ProcessedByUserId == restrictToUserId ||
                                     r.Invoice!.AgentId == restrictToUserId);

        return await query.FirstOrDefaultAsync();
    }

    /// <summary>
    /// رقم مرجعي بصيغة RET-yyyyMMdd-0001 — نفس منطق ترقيم الفواتير،
    /// ومع الفهرس الفريد يفشل الحفظ عند التعارض بدلًا من إنتاج رقم مكرر.
    /// </summary>
    private async Task<string> GenerateReturnNumberAsync(DateTime date)
    {
        var prefix = $"RET-{date:yyyyMMdd}-";
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var countToday = await _db.Returns
            .CountAsync(r => r.CreatedAt >= dayStart && r.CreatedAt < dayEnd);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = prefix + (countToday + attempt).ToString("D4");
            if (!await _db.Returns.AnyAsync(r => r.ReturnNumber == candidate))
                return candidate;
        }

        return prefix + DateTime.Now.ToString("HHmmssfff");
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

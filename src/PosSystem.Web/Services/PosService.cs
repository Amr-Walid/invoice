using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;

namespace PosSystem.Web.Services;

public interface IPosService
{
    Task<ProductLookupResult> LookupByBarcodeAsync(string? barcode);
    Task<CreateInvoiceResult> CreateInvoiceAsync(CreateInvoiceRequest request, string agentId);
}

/// <summary>مخزن البائع المُحدَّد لعملية البيع، أو سبب منعه من البيع</summary>
public class SellingWarehouse
{
    public bool Allowed { get; init; }
    public int WarehouseId { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static SellingWarehouse Deny(string message) =>
        new() { Allowed = false, Message = message };
}

/// <summary>
/// منطق نقطة البيع.
///
/// قاعدة أمنية أساسية: لا تُقرأ أي أسعار من العميل. الواجهة ترسل
/// (ProductId/Barcode + Quantity) فقط، والسيرفر يعيد قراءة السعر من
/// قاعدة البيانات ويحسب الإجماليات. هذا يمنع التلاعب بالأسعار من المتصفح.
///
/// كل العملية تجري داخل transaction واحدة: إما تُحفظ الفاتورة ويُخصم المخزون
/// بالكامل، أو لا يحدث شيء على الإطلاق.
/// </summary>
public class PosService : IPosService
{
    private readonly AppDbContext _db;
    private readonly ICouponService _coupons;
    private readonly ICustomerService _customers;
    private readonly IAuditService _audit;
    private readonly IInventoryService _inventory;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PosService> _logger;

    public PosService(AppDbContext db, ICouponService coupons, ICustomerService customers,
        IAuditService audit, IInventoryService inventory, ICurrentUserService currentUser,
        ILogger<PosService> logger)
    {
        _db = db;
        _coupons = coupons;
        _customers = customers;
        _audit = audit;
        _inventory = inventory;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// يحدّد من أي مخزن يبيع هذا المستخدم.
    ///
    /// <para><b>المندوب بلا مخزن مُمنوع من البيع برسالة واضحة</b> — ولا
    /// يُرجَّع للمخزن الافتراضي بصمت. الرجوع الصامت يخصم من مخزن
    /// لا يملكه المندوب، فتظهر عجوزات في الجرد بلا سبب مفهوم — وهو
    /// أسوأ من منع البيع لأنه يُفسد الأرقام بلا إنذار.</para>
    ///
    /// <para>الأدمن بلا مخزن يبيع من المخزن الافتراضي: NULL عنده تعني
    /// «كل المخازن» لا «بلا مخزن»، وهو يملك صلاحية التصرف في جميعها.</para>
    /// </summary>
    private async Task<SellingWarehouse> ResolveSellingWarehouseAsync(string userId)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.WarehouseId })
            .FirstOrDefaultAsync();

        if (user is null)
            return SellingWarehouse.Deny("تعذر التعرف على المستخدم الحالي");

        Warehouse? warehouse;

        if (user.WarehouseId is int assignedId)
        {
            warehouse = await _db.Warehouses.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == assignedId);

            if (warehouse is null)
                return SellingWarehouse.Deny(
                    "المخزن المربوط بحسابك غير موجود — راجع مدير النظام");

            if (!warehouse.IsActive)
                return SellingWarehouse.Deny(
                    $"مخزنك «{warehouse.Name}» مُعطَّل حاليًا — راجع مدير النظام");
        }
        else if (_currentUser.IsAdmin)
        {
            warehouse = await _db.Warehouses.AsNoTracking()
                           .FirstOrDefaultAsync(w => w.IsDefault && w.IsActive)
                       ?? await _db.Warehouses.AsNoTracking()
                           .Where(w => w.IsActive).OrderBy(w => w.Id).FirstOrDefaultAsync();

            if (warehouse is null)
                return SellingWarehouse.Deny(
                    "لا يوجد مخزن نشط في النظام — أنشِئ مخزنًا قبل البيع");
        }
        else
        {
            return SellingWarehouse.Deny(
                "حسابك غير مربوط بأي مخزن، والبيع يتم من مخزن محدّد. " +
                "اطلب من مدير النظام ربط حسابك بمخزن");
        }

        return new SellingWarehouse
        {
            Allowed = true,
            WarehouseId = warehouse.Id,
            WarehouseName = warehouse.Name
        };
    }

    public async Task<ProductLookupResult> LookupByBarcodeAsync(string? barcode)
    {
        var code = (barcode ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
            return new ProductLookupResult { Found = false, Message = "من فضلك امسح أو أدخل كود الباركود" };

        var product = await _db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Barcode == code);

        if (product is null)
            return new ProductLookupResult { Found = false, Message = $"لا يوجد منتج بالكود: {code}" };

        if (!product.IsActive)
            return new ProductLookupResult { Found = false, Message = $"المنتج «{product.Name}» غير مفعل" };

        // الرصيد المعروض هو رصيد مخزن البائع وحده لا مجموع المخازن.
        // عرض المجموع يجعل المندوب يرى 20 قطعة فيبدأ البيع ثم يفشل
        // عند الحفظ لأن القطع في مخزن مدينة أخرى — تجربة محبطة ومضيعة للوقت.
        var selling = await ResolveSellingWarehouseAsync(_currentUser.UserId ?? string.Empty);
        if (!selling.Allowed)
            return new ProductLookupResult { Found = false, Message = selling.Message };

        var available = await _inventory.GetQuantityAsync(product.Id, selling.WarehouseId);

        if (available <= 0)
        {
            // نخبره أين توجد القطعة بدلاً من «غير متوفر» الجافة، ليطلب تحويلًا
            // من المخزن الذي به رصيد بدل أن يُخبر العميل أن المنتج نافد.
            var elsewhere = await _db.ProductStocks.AsNoTracking()
                .Where(s => s.ProductId == product.Id
                         && s.WarehouseId != selling.WarehouseId
                         && s.Quantity > 0)
                .Include(s => s.Warehouse)
                .OrderByDescending(s => s.Quantity)
                .Select(s => new { s.Quantity, WarehouseName = s.Warehouse!.Name })
                .Take(3)
                .ToListAsync();

            var message = $"المنتج «{product.Name}» غير متوفر في مخزن «{selling.WarehouseName}»";

            if (elsewhere.Count > 0)
            {
                var where = string.Join("، ", elsewhere.Select(e => $"{e.WarehouseName} ({e.Quantity})"));
                message += $" — متوفر في: {where}. يمكن طلب تحويل";
            }

            return new ProductLookupResult { Found = false, Message = message };
        }

        return new ProductLookupResult
        {
            Found = true,
            Id = product.Id,
            Barcode = product.Barcode,
            Name = product.Name,
            Price = product.Price,
            StockQuantity = available,
            BrandName = product.Brand?.Name ?? string.Empty,
            CategoryName = product.Category?.Name ?? string.Empty
        };
    }

    public async Task<CreateInvoiceResult> CreateInvoiceAsync(CreateInvoiceRequest request, string agentId)
    {
        if (request.Items is null || request.Items.Count == 0)
            return CreateInvoiceResult.Fail("لا يمكن حفظ فاتورة فارغة، أضف منتجًا واحدًا على الأقل");

        if (string.IsNullOrWhiteSpace(agentId))
            return CreateInvoiceResult.Fail("تعذر التعرف على المستخدم الحالي");

        // دمج الأسطر المكررة لنفس المنتج في سطر واحد
        var merged = request.Items
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (merged.Count == 0)
            return CreateInvoiceResult.Fail("جميع الكميات غير صحيحة");

        if (merged.Any(i => i.Quantity > 100000))
            return CreateInvoiceResult.Fail("الكمية المطلوبة كبيرة بشكل غير منطقي");

        // المخزن يُحدَّد قبل فتح الـ transaction: لا داعي لفتحها لنكتشف
        // أن المندوب أصلًا ممنوع من البيع.
        var selling = await ResolveSellingWarehouseAsync(agentId);
        if (!selling.Allowed)
            return CreateInvoiceResult.Fail(selling.Message);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var ids = merged.Select(i => i.ProductId).ToList();
            var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync();

            var invoice = new Invoice
            {
                AgentId = agentId,
                WarehouseId = selling.WarehouseId,
                CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? null : request.CustomerName!.Trim(),
                CustomerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone!.Trim(),
                CreatedAt = DateTime.Now,
                Status = InvoiceStatus.Completed
            };

            // ===== ربط الفاتورة بالعميل عبر رقم الهاتف =====
            // سجل العملاء يتكوّن تلقائيًا من حركة البيع: إن كان الرقم معروفًا
            // تُربط الفاتورة بالعميل نفسه، وإن كان جديدًا يُنشأ عميل جديد.
            // الـ snapshot (CustomerName/Phone) يبقى كما كُتب في الفاتورة.
            var customer = await _customers.FindOrCreateAsync(invoice.CustomerName, invoice.CustomerPhone);
            if (customer is not null)
            {
                // العميل الجديد يحتاج Id قبل الربط، فنحفظه أولًا داخل نفس الـ transaction
                if (customer.Id == 0) await _db.SaveChangesAsync();
                invoice.Customer = customer;
                invoice.CustomerId = customer.Id;
            }

            decimal subTotal = 0m;

            // حركات المخزون تُجمَع لتُربط بالفاتورة بعد الحفظ: معرّف الفاتورة
            // لا يوجد قبل SaveChanges، وبدون هذا الربط تظهر الحركة في كارت
            // الصنف بلا مستند يفسّرها فيستحيل تتبّع أي بيع خصم القطع.
            var movements = new List<StockMovement>();

            foreach (var line in merged)
            {
                var product = products.FirstOrDefault(p => p.Id == line.ProductId);

                // ===== التحقق من كل سطر — الأسعار تأتي من DB فقط =====
                if (product is null)
                    return CreateInvoiceResult.Fail($"أحد المنتجات غير موجود (رقم {line.ProductId})");

                if (!product.IsActive)
                    return CreateInvoiceResult.Fail($"المنتج «{product.Name}» غير مفعل");

                var lineTotal = product.Price * line.Quantity;
                subTotal += lineTotal;

                invoice.Items.Add(new InvoiceItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,   // snapshot
                    Barcode = product.Barcode,    // snapshot
                    UnitPrice = product.Price,    // snapshot
                    Quantity = line.Quantity,
                    LineTotal = lineTotal
                });

                // ===== خصم المخزون من مخزن البائع =====
                // الخدمة تتولّى التحقق من كفاية الكمية وتحديث الرصيد والكاش
                // وتسجيل الحركة معًا. لا نفحص الرصيد قبلها فحصًا منفصلًا لأن
                // الفحص المنفصل يفتح نافذة تزامن (تتغير الكمية بين الفحص والخصم)
                // — الخدمة تقرأ وتخصم متتبّعًا في خطوة واحدة.
                var stockResult = await _inventory.ApplyAsync(new StockChangeRequest
                {
                    ProductId = product.Id,
                    WarehouseId = selling.WarehouseId,
                    Change = -line.Quantity,
                    Reason = StockMovementReason.Sale,
                    UserId = agentId
                });

                if (!stockResult.Success)
                    return CreateInvoiceResult.Fail(stockResult.Message);

                if (stockResult.Movement is not null)
                    movements.Add(stockResult.Movement);
            }

            // ===== تطبيق الخصم =====
            Coupon? coupon = null;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                coupon = await _coupons.GetValidCouponAsync(request.CouponCode);
                if (coupon is null)
                    return CreateInvoiceResult.Fail("كود الخصم غير صالح أو منتهي الصلاحية");
            }

            invoice.SubTotal = Round(subTotal);

            if (coupon is not null)
            {
                invoice.CouponId = coupon.Id;
                invoice.CouponCode = coupon.Code;
                invoice.DiscountPercentage = coupon.DiscountPercentage;
                // التقريب مرة واحدة على قيمة الخصم — يمنع فروق القروش
                invoice.DiscountAmount = Round(invoice.SubTotal * coupon.DiscountPercentage / 100m);
                coupon.UsedCount++;
            }

            invoice.Total = Round(invoice.SubTotal - invoice.DiscountAmount);

            // ===== الرقم المرجعي الفريد (تسلسل يومي) =====
            invoice.InvoiceNumber = await GenerateInvoiceNumberAsync(invoice.CreatedAt);

            _db.Invoices.Add(invoice);

            _audit.Track(AuditActions.Sale, nameof(Invoice), invoice.InvoiceNumber,
                $"إجمالي {invoice.Total:0.00} — خصم {invoice.DiscountPercentage:0.##}% — أصناف {invoice.Items.Count}");

            await _db.SaveChangesAsync();

            // الآن للفاتورة معرّف — نربط به الحركات ثم نحفظ داخل نفس الـ transaction
            foreach (var movement in movements)
                movement.ReferenceInvoiceId = invoice.Id;

            if (movements.Count > 0)
                await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return new CreateInvoiceResult
            {
                Success = true,
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                SubTotal = invoice.SubTotal,
                DiscountAmount = invoice.DiscountAmount,
                DiscountPercentage = invoice.DiscountPercentage,
                Total = invoice.Total,
                Message = "تم حفظ الفاتورة بنجاح"
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "فشل إنشاء الفاتورة للمندوب {AgentId}", agentId);
            return CreateInvoiceResult.Fail("حدث خطأ أثناء حفظ الفاتورة، حاول مرة أخرى");
        }
    }

    /// <summary>
    /// توليد رقم مرجعي بصيغة INV-yyyyMMdd-0001.
    /// يُنفَّذ داخل نفس transaction الفاتورة، ومع فهرس UNIQUE على العمود
    /// فإن أي تعارض في التزامن سيفشل الحفظ بدلًا من إنتاج رقم مكرر.
    /// </summary>
    private async Task<string> GenerateInvoiceNumberAsync(DateTime date)
    {
        var prefix = $"INV-{date:yyyyMMdd}-";
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var countToday = await _db.Invoices
            .CountAsync(i => i.CreatedAt >= dayStart && i.CreatedAt < dayEnd);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = prefix + (countToday + attempt).ToString("D4");
            if (!await _db.Invoices.AnyAsync(i => i.InvoiceNumber == candidate))
                return candidate;
        }

        // احتياطي نادر جدًا: نضيف طابعًا زمنيًا لضمان التفرد
        return prefix + DateTime.Now.ToString("HHmmssfff");
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

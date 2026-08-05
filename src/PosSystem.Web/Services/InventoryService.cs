using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Services;

/// <summary>نتيجة محاولة تغيير المخزون</summary>
public class StockChangeResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int QuantityAfter { get; init; }

    /// <summary>
    /// سطر الحركة المُنشَأ (غير محفوظ بعد). يُعاد للمنادي لأن بعض
    /// المراجع لا يُعرف معرّفها قبل الحفظ (معرف المرتجع مثلًا يُولَّد عند
    /// الإدخال)، فيربطها المنادي بعد أول SaveChanges داخل نفس الـ transaction.
    /// </summary>
    public StockMovement? Movement { get; init; }

    public static StockChangeResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>طلب تغيير رصيد صنف واحد</summary>
public class StockChangeRequest
{
    public int ProductId { get; init; }
    public int WarehouseId { get; init; }

    /// <summary>موجب = إضافة، سالب = خصم</summary>
    public int Change { get; init; }

    public StockMovementReason Reason { get; init; }
    public string? UserId { get; init; }
    public string? Note { get; init; }

    public int? ReferenceInvoiceId { get; init; }
    public int? ReferenceReturnId { get; init; }
    public int? ReferencePurchaseReceiptId { get; init; }
    public int? ReferenceTransferId { get; init; }
    public int? ReferenceAdjustmentId { get; init; }
}

public interface IInventoryService
{
    /// <summary>
    /// يطبّق تغييرًا واحدًا على رصيد صنف في مخزن. لا يحفظ — الاستدعاء يجري
    /// داخل transaction الخدمة المنادية فتُحفظ كل التغييرات معًا أو لا شيء.
    /// </summary>
    Task<StockChangeResult> ApplyAsync(StockChangeRequest request);

    /// <summary>يطبّق عدة تغييرات معًا؛ يفشل الكل إن فشل أحدها</summary>
    Task<StockChangeResult> ApplyManyAsync(IEnumerable<StockChangeRequest> requests);

    /// <summary>رصيد صنف في مخزن معيّن (صفر إن لم يوجد صف)</summary>
    Task<int> GetQuantityAsync(int productId, int warehouseId);

    /// <summary>أرصدة مجموعة أصناف في مخزن — استعلام واحد لتجنّب N+1</summary>
    Task<Dictionary<int, int>> GetQuantitiesAsync(IEnumerable<int> productIds, int warehouseId);

    /// <summary>أرصدة صنف واحد موزَّعة على كل المخازن</summary>
    Task<List<ProductStock>> GetStockAcrossWarehousesAsync(int productId);

    /// <summary>
    /// يعيد حساب كاش <c>Product.StockQuantity</c> من مجموع المخازن.
    /// يُستخدم في اختبار المطابقة وفي إصلاح أي انحراف.
    /// </summary>
    Task<int> RecalculateProductCacheAsync(int productId);
}

/// <summary>
/// ⭐ نقطة العبور الوحيدة لكل تغيير في المخزون.
///
/// <para><b>لماذا خدمة واحدة؟</b> لأن الكمية صارت مخزَّنة في مكانين:
/// الحقيقة في <see cref="ProductStock"/>، والكاش في
/// <c>Product.StockQuantity</c> (مجموع كل المخازن). أي كود يعدّل أحدهما دون
/// الآخر يجعل الأرقام تكذب. فكل الكتابة محصورة هنا، وكل عملية تُنجز الثلاثة
/// معًا: رصيد المخزن + الكاش + سطر في <see cref="StockMovement"/>.</para>
///
/// <para><b>لا حفظ ولا transaction هنا.</b> الخدمة تعدّل كيانات مُتتبَّعة فقط،
/// والخدمة المنادية (البيع، المرتجع، الشراء، التحويل) هي التي تفتح الـ
/// transaction وتحفظ. وإلا لانفصل خصم المخزون عن حفظ الفاتورة وأمكن أن ينجح
/// أحدهما ويفشل الآخر.</para>
///
/// <para><b>القراءة داخل الـ transaction.</b> الأرصدة تُقرأ متتبَّعة
/// (بلا AsNoTracking) لحظة التطبيق، فلا تُخصم كمية بناءً على رصيد قديم عند
/// وقوع عمليتين متزامنتين — نفس الحماية المستخدمة في منع الإرجاع المزدوج.</para>
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(AppDbContext db, ILogger<InventoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<StockChangeResult> ApplyAsync(StockChangeRequest request)
    {
        if (request.Change == 0)
            return StockChangeResult.Fail("لا يوجد تغيير في الكمية");

        if (request.WarehouseId <= 0)
            return StockChangeResult.Fail("يجب تحديد المخزن");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null)
            return StockChangeResult.Fail($"المنتج غير موجود (رقم {request.ProductId})");

        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId);
        if (warehouse is null)
            return StockChangeResult.Fail($"المخزن غير موجود (رقم {request.WarehouseId})");

        // صف الرصيد يُنشأ عند أول حركة — لا نُنشئ صفًا لكل منتج × كل مخزن مقدمًا
        // لأن ذلك يُنتج ملايين الصفوف الفارغة بلا فائدة.
        var stock = await _db.ProductStocks
            .FirstOrDefaultAsync(s => s.ProductId == request.ProductId
                                   && s.WarehouseId == request.WarehouseId);

        if (stock is null)
        {
            if (request.Change < 0)
                return StockChangeResult.Fail(
                    $"لا يوجد رصيد للمنتج «{product.Name}» في مخزن «{warehouse.Name}»");

            stock = new ProductStock
            {
                ProductId = request.ProductId,
                WarehouseId = request.WarehouseId,
                Quantity = 0
            };
            _db.ProductStocks.Add(stock);
        }

        var newQuantity = stock.Quantity + request.Change;

        // المخزون السالب مستحيل ماديًا — نمنعه هنا لا في الواجهة فقط
        if (newQuantity < 0)
            return StockChangeResult.Fail(
                $"الكمية غير كافية للمنتج «{product.Name}» في مخزن «{warehouse.Name}» " +
                $"— المتاح {stock.Quantity} والمطلوب {Math.Abs(request.Change)}");

        stock.Quantity = newQuantity;
        stock.UpdatedAt = DateTime.Now;

        // ===== تحديث الكاش =====
        // نُعدّل الكاش بمقدار التغيير نفسه لا بإعادة الجمع من قاعدة البيانات،
        // لأن صفوف مخازن أخرى قد تكون مُعدَّلة في نفس الـ transaction ولم
        // تُحفظ بعد، فإعادة الجمع الآن ستقرأ قيمًا قديمة.
        product.StockQuantity += request.Change;
        if (product.StockQuantity < 0) product.StockQuantity = 0;
        product.UpdatedAt = DateTime.Now;

        var movement = new StockMovement
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            Change = request.Change,
            QuantityAfter = newQuantity,
            Reason = request.Reason,
            UserId = request.UserId,
            Note = request.Note,
            ReferenceInvoiceId = request.ReferenceInvoiceId,
            ReferenceReturnId = request.ReferenceReturnId,
            ReferencePurchaseReceiptId = request.ReferencePurchaseReceiptId,
            ReferenceTransferId = request.ReferenceTransferId,
            ReferenceAdjustmentId = request.ReferenceAdjustmentId,
            CreatedAt = DateTime.Now
        };

        _db.StockMovements.Add(movement);

        return new StockChangeResult
        {
            Success = true,
            QuantityAfter = newQuantity,
            Movement = movement,
            Message = "تم تحديث المخزون"
        };
    }

    public async Task<StockChangeResult> ApplyManyAsync(IEnumerable<StockChangeRequest> requests)
    {
        var last = new StockChangeResult { Success = true, Message = "تم تحديث المخزون" };

        foreach (var request in requests)
        {
            var result = await ApplyAsync(request);
            if (!result.Success) return result;   // أول فشل يُوقف الكل
            last = result;
        }

        return last;
    }

    public async Task<int> GetQuantityAsync(int productId, int warehouseId)
    {
        var stock = await _db.ProductStocks.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
        return stock?.Quantity ?? 0;
    }

    public async Task<Dictionary<int, int>> GetQuantitiesAsync(
        IEnumerable<int> productIds, int warehouseId)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();

        return await _db.ProductStocks.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && ids.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);
    }

    public async Task<List<ProductStock>> GetStockAcrossWarehousesAsync(int productId)
    {
        return await _db.ProductStocks.AsNoTracking()
            .Include(s => s.Warehouse)
            .Include(s => s.Product)
            .Where(s => s.ProductId == productId)
            .OrderByDescending(s => s.Quantity)
            .ToListAsync();
    }

    public async Task<int> RecalculateProductCacheAsync(int productId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return 0;

        var total = await _db.ProductStocks
            .Where(s => s.ProductId == productId)
            .SumAsync(s => (int?)s.Quantity) ?? 0;

        if (product.StockQuantity != total)
        {
            _logger.LogWarning(
                "انحراف في كاش المخزون للمنتج {ProductId}: الكاش {Cached} والحقيقة {Actual} — تم التصحيح",
                productId, product.StockQuantity, total);
            product.StockQuantity = total;
        }

        return total;
    }
}

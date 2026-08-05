using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Services;

public class WarehouseResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public Warehouse? Warehouse { get; init; }

    public static WarehouseResult Fail(string message) =>
        new() { Success = false, Message = message };

    public static WarehouseResult Ok(string message, Warehouse? warehouse = null) =>
        new() { Success = true, Message = message, Warehouse = warehouse };
}

/// <summary>صف في شاشة «مخزون المخزن»</summary>
public class WarehouseStockRow
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string Barcode { get; init; } = string.Empty;
    public string BrandName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public int Threshold { get; init; }
    public int? CustomThreshold { get; init; }
    public bool IsLowStock => Quantity <= Threshold;

    /// <summary>الرصيد في بقية المخازن — يجيب «هل أطلب تحويلًا؟»</summary>
    public int QuantityElsewhere { get; init; }
}

public interface IWarehouseService
{
    Task<List<Warehouse>> ListAsync(bool includeInactive = true);
    Task<List<Warehouse>> ListActiveAsync();
    Task<Warehouse?> GetAsync(int id);
    Task<Warehouse?> GetDefaultAsync();

    Task<WarehouseResult> CreateAsync(Warehouse input, string? userId);
    Task<WarehouseResult> UpdateAsync(Warehouse input, string? userId);
    Task<WarehouseResult> DeleteAsync(int id, string? userId);
    Task<WarehouseResult> SetDefaultAsync(int id, string? userId);

    Task<List<WarehouseStockRow>> GetStockRowsAsync(
        int warehouseId, string? search = null, bool lowStockOnly = false);

    Task<WarehouseResult> SetProductThresholdAsync(
        int warehouseId, int productId, int? threshold);

    /// <summary>عدد الأصناف الناقصة في مخزن — للتنبيهات</summary>
    Task<int> CountLowStockAsync(int warehouseId);

    /// <summary>كل الحسابات المرتبطة بمخزن (لشاشة المخزن)</summary>
    Task<List<ApplicationUser>> GetUsersAsync(int warehouseId);
}

/// <summary>
/// إدارة المخازن. يحرس ثلاث قواعد لا يستطيع نوع البيانات وحده حراستها:
///
/// <para><b>1) مخزن افتراضي واحد دائمًا.</b> عمود <c>IsDefault</c> منطقي بسيط
/// ولا يمنع وجود اثنين أو صفر. فكل مسار يمرّ من هنا يضمن أن المخازن النشطة
/// إن وُجدت فبينها افتراضي واحد بالضبط — لأن المشتريات والتسويات ترجع إليه
/// عند غياب اختيار صريح، فصفر افتراضي يعني توقّف العمل واثنان يعني سلوكًا عشوائيًا.</para>
///
/// <para><b>2) لا حذف لمخزن به أثر.</b> الحذف مسموح فقط للمخزن الفارغ تمامًا:
/// لا أرصدة (ولا حتى صفرية)، لا حركات، لا مستخدمين. غير ذلك يُعطَّل
/// (<c>IsActive=false</c>) فتبقى الحركات التاريخية مقروءة.</para>
///
/// <para><b>3) لا تعطيل للمخزن الافتراضي.</b> تعطيله يترك النظام بلا مرجع.</para>
/// </summary>
public class WarehouseService : IWarehouseService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<WarehouseService> _logger;

    public WarehouseService(AppDbContext db, IAuditService audit, ILogger<WarehouseService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    // ==================== قراءة ====================

    public async Task<List<Warehouse>> ListAsync(bool includeInactive = true)
    {
        var query = _db.Warehouses.AsNoTracking();
        if (!includeInactive) query = query.Where(w => w.IsActive);

        return await query
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Name)
            .ToListAsync();
    }

    public Task<List<Warehouse>> ListActiveAsync() => ListAsync(includeInactive: false);

    public async Task<Warehouse?> GetAsync(int id) =>
        await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);

    public async Task<Warehouse?> GetDefaultAsync()
    {
        // الافتراضي المُعلَن، ثم أي مخزن نشط كخطة بديلة حتى لا يتوقف
        // العمل لو أُفسدت العلامة بتعديل مباشر على قاعدة البيانات.
        return await _db.Warehouses.AsNoTracking()
                   .FirstOrDefaultAsync(w => w.IsDefault && w.IsActive)
               ?? await _db.Warehouses.AsNoTracking()
                   .Where(w => w.IsActive).OrderBy(w => w.Id).FirstOrDefaultAsync();
    }

    public async Task<List<ApplicationUser>> GetUsersAsync(int warehouseId) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.WarehouseId == warehouseId)
            .OrderBy(u => u.FullName)
            .ToListAsync();

    // ==================== كتابة ====================

    public async Task<WarehouseResult> CreateAsync(Warehouse input, string? userId)
    {
        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (input.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code)) return WarehouseResult.Fail("كود المخزن مطلوب");
        if (string.IsNullOrWhiteSpace(name)) return WarehouseResult.Fail("اسم المخزن مطلوب");

        if (await _db.Warehouses.AnyAsync(w => w.Code == code))
            return WarehouseResult.Fail($"الكود «{code}» مستخدم بالفعل لمخزن آخر");

        var isFirst = !await _db.Warehouses.AnyAsync();

        var warehouse = new Warehouse
        {
            Code = code,
            Name = name,
            Address = input.Address?.Trim(),
            Phone = input.Phone?.Trim(),
            ManagerName = input.ManagerName?.Trim(),
            IsActive = input.IsActive,
            // أول مخزن يُنشأ هو الافتراضي حتمًا — وإلا بقي النظام بلا مرجع
            IsDefault = isFirst || input.IsDefault,
            CreatedAt = DateTime.Now
        };

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync();

        if (warehouse.IsDefault) await ClearOtherDefaultsAsync(warehouse.Id);

        await _audit.LogAsync(AuditActions.Create, nameof(Warehouse), warehouse.Id.ToString(),
            $"إنشاء مخزن «{warehouse.Name}» بالكود {warehouse.Code}");

        return WarehouseResult.Ok($"تم إنشاء المخزن «{warehouse.Name}»", warehouse);
    }

    public async Task<WarehouseResult> UpdateAsync(Warehouse input, string? userId)
    {
        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == input.Id);
        if (warehouse is null) return WarehouseResult.Fail("المخزن غير موجود");

        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (input.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code)) return WarehouseResult.Fail("كود المخزن مطلوب");
        if (string.IsNullOrWhiteSpace(name)) return WarehouseResult.Fail("اسم المخزن مطلوب");

        if (await _db.Warehouses.AnyAsync(w => w.Code == code && w.Id != warehouse.Id))
            return WarehouseResult.Fail($"الكود «{code}» مستخدم بالفعل لمخزن آخر");

        // تعطيل المخزن الافتراضي يترك المشتريات والتسويات بلا مرجع
        if (warehouse.IsDefault && !input.IsActive)
            return WarehouseResult.Fail(
                "لا يمكن تعطيل المخزن الافتراضي — اجعل مخزنًا آخر افتراضيًا أولًا");

        // تعطيل مخزن مرتبط بمناديب يمنعهم من البيع بلا إشعار مسبق
        if (warehouse.IsActive && !input.IsActive)
        {
            var linkedUsers = await _db.Users.CountAsync(u => u.WarehouseId == warehouse.Id);
            if (linkedUsers > 0)
                return WarehouseResult.Fail(
                    $"لا يمكن تعطيل المخزن — مرتبط به {linkedUsers} مستخدم. " +
                    "انقلهم إلى مخزن آخر أولًا حتى لا يتوقف بيعهم بلا سبب واضح");
        }

        warehouse.Code = code;
        warehouse.Name = name;
        warehouse.Address = input.Address?.Trim();
        warehouse.Phone = input.Phone?.Trim();
        warehouse.ManagerName = input.ManagerName?.Trim();
        warehouse.IsActive = input.IsActive;

        // رفع العلامة مسموح من هنا؛ إنزالها يجب أن يمرّ من SetDefaultAsync
        // وإلا بقي النظام بلا افتراضي.
        if (input.IsDefault && !warehouse.IsDefault)
        {
            warehouse.IsDefault = true;
            await _db.SaveChangesAsync();
            await ClearOtherDefaultsAsync(warehouse.Id);
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync(AuditActions.Update, nameof(Warehouse), warehouse.Id.ToString(),
            $"تعديل مخزن «{warehouse.Name}»");

        return WarehouseResult.Ok($"تم تحديث المخزن «{warehouse.Name}»", warehouse);
    }

    public async Task<WarehouseResult> SetDefaultAsync(int id, string? userId)
    {
        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id);
        if (warehouse is null) return WarehouseResult.Fail("المخزن غير موجود");
        if (!warehouse.IsActive)
            return WarehouseResult.Fail("لا يمكن جعل مخزن معطَّل افتراضيًا");

        warehouse.IsDefault = true;
        await _db.SaveChangesAsync();
        await ClearOtherDefaultsAsync(warehouse.Id);

        await _audit.LogAsync(AuditActions.Update, nameof(Warehouse), warehouse.Id.ToString(),
            $"تعيين «{warehouse.Name}» مخزنًا افتراضيًا");

        return WarehouseResult.Ok($"«{warehouse.Name}» صار المخزن الافتراضي", warehouse);
    }

    public async Task<WarehouseResult> DeleteAsync(int id, string? userId)
    {
        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id);
        if (warehouse is null) return WarehouseResult.Fail("المخزن غير موجود");

        if (warehouse.IsDefault)
            return WarehouseResult.Fail(
                "لا يمكن حذف المخزن الافتراضي — اجعل مخزنًا آخر افتراضيًا أولًا");

        // ===== حرّاس الحذف =====
        // نمنع الحذف عند وجود أي أثر، ونشرح للمستخدم السبب بالرقم بدلًا من
        // رسالة قاعدة بيانات غامضة عن قيد مرجعي.
        var stockRows = await _db.ProductStocks.CountAsync(s => s.WarehouseId == id);
        if (stockRows > 0)
        {
            var withQuantity = await _db.ProductStocks
                .CountAsync(s => s.WarehouseId == id && s.Quantity > 0);

            return WarehouseResult.Fail(withQuantity > 0
                ? $"لا يمكن حذف المخزن — به أرصدة لـ {withQuantity} صنف. " +
                  "حوّلها إلى مخزن آخر أو عطّل المخزن بدلًا من حذفه"
                : $"لا يمكن حذف المخزن — له {stockRows} سجل مخزون. " +
                  "عطّله بدلًا من حذفه للحفاظ على السجل");
        }

        if (await _db.StockMovements.AnyAsync(m => m.WarehouseId == id))
            return WarehouseResult.Fail(
                "لا يمكن حذف المخزن — له حركات مخزنية مسجَّلة. " +
                "عطّله بدلًا من حذفه حتى تبقى الحركات التاريخية مقروءة");

        var users = await _db.Users.CountAsync(u => u.WarehouseId == id);
        if (users > 0)
            return WarehouseResult.Fail(
                $"لا يمكن حذف المخزن — مرتبط به {users} مستخدم. انقلهم إلى مخزن آخر أولًا");

        var name = warehouse.Name;
        _db.Warehouses.Remove(warehouse);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(AuditActions.Delete, nameof(Warehouse), id.ToString(),
            $"حذف مخزن «{name}»");

        return WarehouseResult.Ok($"تم حذف المخزن «{name}»");
    }

    // ==================== مخزون المخزن ====================

    public async Task<List<WarehouseStockRow>> GetStockRowsAsync(
        int warehouseId, string? search = null, bool lowStockOnly = false)
    {
        var query = _db.ProductStocks.AsNoTracking()
            .Include(s => s.Product!).ThenInclude(p => p.Brand)
            .Include(s => s.Product!).ThenInclude(p => p.Category)
            .Where(s => s.WarehouseId == warehouseId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s => s.Product!.Name.Contains(term)
                                  || s.Product!.Barcode.Contains(term));
        }

        var stocks = await query.ToListAsync();
        if (stocks.Count == 0) return new List<WarehouseStockRow>();

        // رصيد بقية المخازن في استعلام واحد مُجمَّع — لا استعلام لكل صنف.
        var productIds = stocks.Select(s => s.ProductId).ToList();
        var elsewhere = await _db.ProductStocks.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId) && s.WarehouseId != warehouseId)
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Total);

        var rows = stocks.Select(s => new WarehouseStockRow
        {
            ProductId = s.ProductId,
            ProductName = s.Product?.Name ?? "—",
            Barcode = s.Product?.Barcode ?? string.Empty,
            BrandName = s.Product?.Brand?.Name ?? "—",
            CategoryName = s.Product?.Category?.Name ?? "—",
            Price = s.Product?.Price ?? 0m,
            Quantity = s.Quantity,
            CustomThreshold = s.LowStockThreshold,
            Threshold = s.LowStockThreshold ?? s.Product?.LowStockThreshold ?? 0,
            QuantityElsewhere = elsewhere.TryGetValue(s.ProductId, out var q) ? q : 0
        });

        if (lowStockOnly) rows = rows.Where(r => r.IsLowStock);

        // الناقص أولًا: الشاشة تخدم قرار الشراء/التحويل قبل الجرد
        return rows
            .OrderByDescending(r => r.IsLowStock)
            .ThenBy(r => r.Quantity)
            .ThenBy(r => r.ProductName)
            .ToList();
    }

    public async Task<WarehouseResult> SetProductThresholdAsync(
        int warehouseId, int productId, int? threshold)
    {
        if (threshold is < 0) return WarehouseResult.Fail("حد التنبيه لا يمكن أن يكون سالبًا");

        var stock = await _db.ProductStocks
            .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);

        if (stock is null)
            return WarehouseResult.Fail("لا يوجد سجل مخزون لهذا الصنف في هذا المخزن");

        stock.LowStockThreshold = threshold;
        stock.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return WarehouseResult.Ok(threshold is null
            ? "تم إرجاع حد التنبيه إلى الحد العام للمنتج"
            : $"تم تعيين حد التنبيه إلى {threshold}");
    }

    public async Task<int> CountLowStockAsync(int warehouseId)
    {
        // المقارنة تحتاج حدّ المنتج عند غياب الحد الخاص، فنُسقط الحقول
        // المطلوبة فقط ثم نُقيّم في الذاكرة — أخفّ من تحميل الكيانات كاملة.
        var rows = await _db.ProductStocks.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId)
            .Select(s => new { s.Quantity, s.LowStockThreshold, ProductThreshold = s.Product!.LowStockThreshold })
            .ToListAsync();

        return rows.Count(r => r.Quantity <= (r.LowStockThreshold ?? r.ProductThreshold));
    }

    // ==================== داخلي ====================

    /// <summary>ينزل علامة الافتراضي عن كل مخزن آخر — يضمن واحدًا بالضبط</summary>
    private async Task ClearOtherDefaultsAsync(int keepId)
    {
        var others = await _db.Warehouses
            .Where(w => w.IsDefault && w.Id != keepId)
            .ToListAsync();

        if (others.Count == 0) return;

        foreach (var other in others) other.IsDefault = false;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "تم إنزال علامة الافتراضي عن {Count} مخزن بعد تعيين المخزن {KeepId}",
            others.Count, keepId);
    }
}

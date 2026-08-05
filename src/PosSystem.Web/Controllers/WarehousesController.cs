using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// إدارة المخازن ومخزونها.
///
/// <para>الصلاحيات: الإنشاء والتعديل والحذف للمدير وحده — هيكل المخازن قرار
/// إداري. أما القراءة (المخزون وكارت الصنف) فمتاحة لأمين المخزن أيضًا،
/// فهو من يحتاجها في عمله اليومي.</para>
/// </summary>
[Authorize(Roles = AppRoles.AdminOrKeeper)]
public class WarehousesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWarehouseService _warehouses;
    private readonly IInventoryService _inventory;
    private readonly ICurrentUserService _currentUser;

    public WarehousesController(AppDbContext db, IWarehouseService warehouses,
        IInventoryService inventory, ICurrentUserService currentUser)
    {
        _db = db;
        _warehouses = warehouses;
        _inventory = inventory;
        _currentUser = currentUser;
    }

    // ==================== القائمة ====================

    public async Task<IActionResult> Index()
    {
        var warehouses = await _warehouses.ListAsync();

        // إحصاءات كل مخزن في استعلامين مُجمَّعين لا استعلام لكل مخزن.
        var stockStats = await _db.ProductStocks.AsNoTracking()
            .GroupBy(s => s.WarehouseId)
            .Select(g => new
            {
                WarehouseId = g.Key,
                Products = g.Count(),
                Pieces = g.Sum(x => x.Quantity)
            })
            .ToDictionaryAsync(x => x.WarehouseId);

        var userCounts = await _db.Users.AsNoTracking()
            .Where(u => u.WarehouseId != null)
            .GroupBy(u => u.WarehouseId!.Value)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count);

        ViewBag.StockStats = warehouses.ToDictionary(
            w => w.Id,
            w => stockStats.TryGetValue(w.Id, out var s)
                ? (Products: s.Products, Pieces: s.Pieces)
                : (Products: 0, Pieces: 0));

        ViewBag.UserCounts = warehouses.ToDictionary(
            w => w.Id, w => userCounts.TryGetValue(w.Id, out var c) ? c : 0);

        return View(warehouses);
    }

    // ==================== إنشاء ====================

    [Authorize(Roles = AppRoles.Admin)]
    public IActionResult Create() => View(new Warehouse { IsActive = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Create(Warehouse model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _warehouses.CreateAsync(model, _currentUser.UserId);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    // ==================== تعديل ====================

    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(int id)
    {
        var warehouse = await _warehouses.GetAsync(id);
        if (warehouse is null) return NotFound();
        return View(warehouse);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(int id, Warehouse model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);

        var result = await _warehouses.UpdateAsync(model, _currentUser.UserId);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    // ==================== الافتراضي والحذف ====================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> SetDefault(int id)
    {
        var result = await _warehouses.SetDefaultAsync(id, _currentUser.UserId);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _warehouses.DeleteAsync(id, _currentUser.UserId);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    // ==================== مخزون المخزن ====================

    /// <summary>
    /// أرصدة مخزن واحد. أمين المخزن مقيَّد بمخزنه: لا يرى أرصدة الفروع
    /// الأخرى، وهي معلومة تجارية لا تخصّه.
    /// </summary>
    public async Task<IActionResult> Stock(int id, string? search, bool lowStockOnly = false)
    {
        var warehouse = await _warehouses.GetAsync(id);
        if (warehouse is null) return NotFound();

        if (!_currentUser.IsAdmin && !await UserOwnsWarehouseAsync(id))
            return Forbid();

        ViewBag.Warehouse = warehouse;
        ViewBag.Search = search;
        ViewBag.LowStockOnly = lowStockOnly;
        ViewBag.LowStockCount = await _warehouses.CountLowStockAsync(id);
        ViewBag.Users = await _warehouses.GetUsersAsync(id);

        var rows = await _warehouses.GetStockRowsAsync(id, search, lowStockOnly);
        return View(rows);
    }

    /// <summary>
    /// كارت الصنف: كل حركة على منتج داخل مخزن، وأرصدته في بقية المخازن.
    /// هذه الشاشة تجيب سؤال «فين القطع دي رحت؟» الذي لا يجيبه رصيد مجرَّد.
    /// </summary>
    public async Task<IActionResult> ProductLedger(int id, int productId)
    {
        var warehouse = await _warehouses.GetAsync(id);
        if (warehouse is null) return NotFound();

        if (!_currentUser.IsAdmin && !await UserOwnsWarehouseAsync(id))
            return Forbid();

        var product = await _db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product is null) return NotFound();

        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.WarehouseId == id && m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(200)
            .ToListAsync();

        ViewBag.Warehouse = warehouse;
        ViewBag.Product = product;
        ViewBag.Quantity = await _inventory.GetQuantityAsync(productId, id);
        ViewBag.AcrossWarehouses = await _inventory.GetStockAcrossWarehousesAsync(productId);

        return View(movements);
    }

    /// <summary>تعديل حد التنبيه الخاص بصنف في مخزن</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetThreshold(int id, int productId, int? threshold)
    {
        if (!_currentUser.IsAdmin && !await UserOwnsWarehouseAsync(id))
            return Forbid();

        var result = await _warehouses.SetProductThresholdAsync(id, productId, threshold);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Stock), new { id });
    }

    /// <summary>
    /// تسوية يدوية لرصيد صنف في مخزن (إيداع أو سحب).
    ///
    /// <para>
    /// هذه هي الطريقة الوحيدة لتعديل المخزون يدويًا بعد تعدد المخازن: شاشة
    /// المنتجات لم تعد تسمح بذلك لأن الرقم المعروض هناك مجموع كل المخازن،
    /// وتعديله لا يقول في أي مخزن حدث التغيير.
    /// </para>
    ///
    /// <para>
    /// نطلب <paramref name="note"/> إلزاميًا: تسوية بلا سبب مكتوب تُنتج فرق
    /// جرد لا يستطيع أحد تفسيره بعد أسبوع، وهي أكثر ما يُشكَّك فيه عند المراجعة.
    /// </para>
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(int id, int productId, int change, string? note)
    {
        if (!_currentUser.IsAdmin && !await UserOwnsWarehouseAsync(id))
            return Forbid();

        if (change == 0)
        {
            TempData["Error"] = "قيمة التسوية صفر — لا تغيير";
            return RedirectToAction(nameof(Stock), new { id });
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            TempData["Error"] = "سبب التسوية مطلوب — التسوية بلا سبب تُنتج فرقًا لا يمكن تفسيره لاحقًا";
            return RedirectToAction(nameof(Stock), new { id });
        }

        // الترقيم في transaction: الخدمة تُعدّل الرصيد والكاش وتسجّل الحركة،
        // ولا تحفظ بنفسها — الحفظ والتراجع مسؤولية هذا الإجراء.
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var result = await _inventory.ApplyAsync(new StockChangeRequest
            {
                ProductId = productId,
                WarehouseId = id,
                Change = change,
                Reason = StockMovementReason.ManualAdjustment,
                Note = note.Trim(),
                UserId = _currentUser.UserId
            });

            if (!result.Success)
            {
                await tx.RollbackAsync();
                TempData["Error"] = result.Message;
                return RedirectToAction(nameof(Stock), new { id });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            var verb = change > 0 ? "إيداع" : "سحب";
            TempData["Success"] =
                $"تم {verb} {Math.Abs(change)} — الرصيد الآن {result.QuantityAfter}";
        }
        catch
        {
            await tx.RollbackAsync();
            TempData["Error"] = "تعذّر تنفيذ التسوية، حاول مرة أخرى";
        }

        return RedirectToAction(nameof(Stock), new { id });
    }

    /// <summary>هل هذا المخزن هو مخزن المستخدم الحالي؟</summary>
    private async Task<bool> UserOwnsWarehouseAsync(int warehouseId)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return false;

        return await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.WarehouseId == warehouseId);
    }
}

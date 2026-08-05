using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class CategoriesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public CategoriesController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(int? brandId)
    {
        var query = _db.Categories.AsNoTracking()
            .Include(c => c.Brand)
            .Include(c => c.Products)
            .AsQueryable();

        if (brandId.HasValue)
            query = query.Where(c => c.BrandId == brandId.Value);

        ViewBag.BrandId = brandId;
        await LoadBrandsAsync(brandId);

        var list = await query
            .OrderBy(c => c.Brand!.Name).ThenBy(c => c.Name)
            .ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Create(int? brandId)
    {
        await LoadBrandsAsync(brandId);
        return View(new Category { BrandId = brandId ?? 0 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category model)
    {
        if (await _db.Categories.AnyAsync(c => c.BrandId == model.BrandId && c.Name == model.Name))
            ModelState.AddModelError(nameof(model.Name), "هذا التصنيف موجود بالفعل لنفس البراند");

        if (!ModelState.IsValid)
        {
            await LoadBrandsAsync(model.BrandId);
            return View(model);
        }

        model.CreatedAt = DateTime.Now;
        _db.Categories.Add(model);
        _audit.Track(AuditActions.Create, nameof(Category), null, $"تصنيف جديد: {model.Name}");
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم إضافة التصنيف «{model.Name}» بنجاح";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null) return NotFound();

        await LoadBrandsAsync(category.BrandId);
        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Category model)
    {
        if (id != model.Id) return BadRequest();

        if (await _db.Categories.AnyAsync(c => c.BrandId == model.BrandId && c.Name == model.Name && c.Id != id))
            ModelState.AddModelError(nameof(model.Name), "هذا التصنيف موجود بالفعل لنفس البراند");

        if (!ModelState.IsValid)
        {
            await LoadBrandsAsync(model.BrandId);
            return View(model);
        }

        var category = await _db.Categories.FindAsync(id);
        if (category is null) return NotFound();

        category.Name = model.Name;
        category.BrandId = model.BrandId;
        category.IsActive = model.IsActive;
        _audit.Track(AuditActions.Update, nameof(Category), id.ToString(), $"تعديل تصنيف: {category.Name}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم تحديث التصنيف بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null) return NotFound();

        if (category.Products.Any())
        {
            TempData["Error"] = $"لا يمكن حذف «{category.Name}» لوجود منتجات مرتبطة به. يمكنك تعطيله بدلًا من ذلك.";
            return RedirectToAction(nameof(Index));
        }

        _db.Categories.Remove(category);
        _audit.Track(AuditActions.Delete, nameof(Category), id.ToString(), $"حذف تصنيف: {category.Name}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم حذف التصنيف بنجاح";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>لتحديث قائمة التصنيفات ديناميكيًا عند اختيار البراند في شاشة المنتجات</summary>
    [HttpGet]
    public async Task<IActionResult> ByBrand(int brandId)
    {
        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.BrandId == brandId && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();
        return Json(categories);
    }

    private async Task LoadBrandsAsync(int? selected)
    {
        var brands = await _db.Brands.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync();

        ViewBag.Brands = new SelectList(brands, "Id", "Name", selected);
    }
}

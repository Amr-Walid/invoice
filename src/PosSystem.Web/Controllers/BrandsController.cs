using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class BrandsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public BrandsController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var brands = await _db.Brands.AsNoTracking()
            .Include(b => b.Categories)
            .Include(b => b.Products)
            .OrderBy(b => b.Name)
            .ToListAsync();
        return View(brands);
    }

    public IActionResult Create() => View(new Brand());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Brand model)
    {
        if (await _db.Brands.AnyAsync(b => b.Name == model.Name))
            ModelState.AddModelError(nameof(model.Name), "هذا البراند موجود بالفعل");

        if (!ModelState.IsValid) return View(model);

        model.CreatedAt = DateTime.Now;
        _db.Brands.Add(model);
        _audit.Track(AuditActions.Create, nameof(Brand), null, $"براند جديد: {model.Name}");
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم إضافة البراند «{model.Name}» بنجاح";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var brand = await _db.Brands.FindAsync(id);
        if (brand is null) return NotFound();
        return View(brand);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Brand model)
    {
        if (id != model.Id) return BadRequest();

        if (await _db.Brands.AnyAsync(b => b.Name == model.Name && b.Id != id))
            ModelState.AddModelError(nameof(model.Name), "هذا البراند موجود بالفعل");

        if (!ModelState.IsValid) return View(model);

        var brand = await _db.Brands.FindAsync(id);
        if (brand is null) return NotFound();

        brand.Name = model.Name;
        brand.IsActive = model.IsActive;
        _audit.Track(AuditActions.Update, nameof(Brand), id.ToString(), $"تعديل براند: {brand.Name}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم تحديث البراند بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var brand = await _db.Brands
            .Include(b => b.Categories)
            .Include(b => b.Products)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (brand is null) return NotFound();

        // منع الحذف إن كان مرتبطًا ببيانات — حفاظًا على سلامة السجلات
        if (brand.Categories.Any() || brand.Products.Any())
        {
            TempData["Error"] = $"لا يمكن حذف «{brand.Name}» لوجود تصنيفات أو منتجات مرتبطة به. يمكنك تعطيله بدلًا من ذلك.";
            return RedirectToAction(nameof(Index));
        }

        _db.Brands.Remove(brand);
        _audit.Track(AuditActions.Delete, nameof(Brand), id.ToString(), $"حذف براند: {brand.Name}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم حذف البراند بنجاح";
        return RedirectToAction(nameof(Index));
    }
}

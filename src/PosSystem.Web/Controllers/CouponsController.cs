using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class CouponsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public CouponsController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var coupons = await _db.Coupons.AsNoTracking()
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.Code)
            .ToListAsync();
        return View(coupons);
    }

    public IActionResult Create() => View(new Coupon());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Coupon model)
    {
        Normalize(model);
        await ValidateAsync(model, null);

        if (!ModelState.IsValid) return View(model);

        model.CreatedAt = DateTime.Now;
        model.UsedCount = 0;
        _db.Coupons.Add(model);
        _audit.Track(AuditActions.Create, nameof(Coupon), null,
            $"كود خصم جديد: {model.Code} — {model.DiscountPercentage:0.##}%");
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم إضافة كود الخصم «{model.Code}» بنجاح";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon is null) return NotFound();
        return View(coupon);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Coupon model)
    {
        if (id != model.Id) return BadRequest();

        Normalize(model);
        await ValidateAsync(model, id);

        if (!ModelState.IsValid) return View(model);

        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon is null) return NotFound();

        coupon.Code = model.Code;
        coupon.DiscountPercentage = model.DiscountPercentage;
        coupon.IsActive = model.IsActive;
        coupon.StartDate = model.StartDate;
        coupon.EndDate = model.EndDate;
        coupon.MaxUsageCount = model.MaxUsageCount;

        _audit.Track(AuditActions.Update, nameof(Coupon), id.ToString(),
            $"تعديل كود خصم: {coupon.Code} — {coupon.DiscountPercentage:0.##}%");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم تحديث كود الخصم بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon is null) return NotFound();

        coupon.IsActive = !coupon.IsActive;
        _audit.Track(AuditActions.Update, nameof(Coupon), id.ToString(),
            $"{(coupon.IsActive ? "تفعيل" : "تعطيل")} كود: {coupon.Code}");
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم {(coupon.IsActive ? "تفعيل" : "تعطيل")} الكود «{coupon.Code}»";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var coupon = await _db.Coupons
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (coupon is null) return NotFound();

        // كود استُخدم في فواتير لا يُحذف — يُعطَّل فقط
        if (coupon.Invoices.Any())
        {
            coupon.IsActive = false;
            _audit.Track(AuditActions.Update, nameof(Coupon), id.ToString(),
                $"تعطيل كود مستخدم في فواتير: {coupon.Code}");
            await _db.SaveChangesAsync();

            TempData["Success"] = $"تم تعطيل «{coupon.Code}» لأنه مستخدم في فواتير سابقة";
            return RedirectToAction(nameof(Index));
        }

        _db.Coupons.Remove(coupon);
        _audit.Track(AuditActions.Delete, nameof(Coupon), id.ToString(), $"حذف كود خصم: {coupon.Code}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم حذف كود الخصم بنجاح";
        return RedirectToAction(nameof(Index));
    }

    private static void Normalize(Coupon model) =>
        model.Code = (model.Code ?? string.Empty).Trim().ToUpperInvariant();

    private async Task ValidateAsync(Coupon model, int? excludeId)
    {
        if (!string.IsNullOrEmpty(model.Code) &&
            await _db.Coupons.AnyAsync(c => c.Code == model.Code && (!excludeId.HasValue || c.Id != excludeId.Value)))
        {
            ModelState.AddModelError(nameof(model.Code), "هذا الكود موجود بالفعل");
        }

        if (model.StartDate.HasValue && model.EndDate.HasValue && model.EndDate < model.StartDate)
            ModelState.AddModelError(nameof(model.EndDate), "تاريخ الانتهاء يجب أن يكون بعد تاريخ البداية");
    }
}

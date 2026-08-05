using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// إدارة الموردين.
///
/// <para><b>الصلاحيات:</b> الإضافة والتعديل والحذف للمدير وحده — من نشتري منه
/// قرار إداري ومالي. أما القراءة فمتاحة لأمين المخزن أيضًا: هو من يستلم
/// البضاعة ويحتاج رقم المورد ومسؤول التواصل عند أي نقص في التوريد.</para>
///
/// <para>خلافًا للعملاء الذين يُنشأون تلقائيًا من حركة البيع، المورد يُدخل
/// يدويًا: لا حركة تُنشئه، والشراء يبدأ باختياره من قائمة موجودة.</para>
/// </summary>
[Authorize(Roles = AppRoles.AdminOrKeeper)]
public class SuppliersController : Controller
{
    private const int PageSize = 20;

    private readonly ISupplierService _suppliers;
    private readonly ICurrentUserService _currentUser;

    public SuppliersController(ISupplierService suppliers, ICurrentUserService currentUser)
    {
        _suppliers = suppliers;
        _currentUser = currentUser;
    }

    // ==================== القائمة ====================

    [HttpGet]
    public async Task<IActionResult> Index(string? q, bool includeInactive = false, int page = 1)
    {
        var vm = await _suppliers.SearchAsync(q, includeInactive, page, PageSize);
        ViewBag.CanManage = _currentUser.IsAdmin;
        return View(vm);
    }

    // ==================== كشف الحساب ====================

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var vm = await _suppliers.GetStatementAsync(id);
        if (vm is null) return NotFound();

        ViewBag.CanManage = _currentUser.IsAdmin;
        return View(vm);
    }

    // ==================== إضافة ====================

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public IActionResult Create() => View(new Supplier());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Create(Supplier input)
    {
        if (!ModelState.IsValid) return View(input);

        var result = await _suppliers.CreateAsync(input, _currentUser.UserId);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(input);
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    // ==================== تعديل ====================

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(int id)
    {
        var supplier = await _suppliers.GetAsync(id);
        if (supplier is null) return NotFound();
        return View(supplier);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(Supplier input)
    {
        if (!ModelState.IsValid) return View(input);

        var result = await _suppliers.UpdateAsync(input, _currentUser.UserId);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(input);
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = input.Id });
    }

    // ==================== حذف / تعطيل ====================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _suppliers.DeleteAsync(id, _currentUser.UserId);

        // الفشل هنا ليس خطأ نظام بل قاعدة عمل («لا حذف لمورد له أوامر»)،
        // فتُعرض الرسالة تحذيرًا لا خطأً ويُقترح فيها البديل
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Warning"] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> ToggleActive(int id, string? returnTo)
    {
        var result = await _suppliers.ToggleActiveAsync(id, _currentUser.UserId);

        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Warning"] = result.Message;

        if (returnTo == "details")
            return RedirectToAction(nameof(Details), new { id });

        return RedirectToAction(nameof(Index));
    }
}

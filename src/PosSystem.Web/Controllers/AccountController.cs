using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditService audit)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _audit = audit;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToLanding();

        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null)
        {
            // رسالة موحدة — لا نكشف إن كان البريد مسجلًا أم لا
            ModelState.AddModelError(string.Empty, "البريد الإلكتروني أو كلمة المرور غير صحيحة");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "هذا الحساب معطّل، تواصل مع المدير");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "تم إيقاف الحساب مؤقتًا بسبب محاولات دخول خاطئة، حاول بعد 5 دقائق");
            return View(model);
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "البريد الإلكتروني أو كلمة المرور غير صحيحة");
            return View(model);
        }

        await _audit.LogAsync(AuditActions.Login, nameof(ApplicationUser), user.Id, $"دخول: {user.Email}");

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        // ===== التوجيه التلقائي حسب الـ Role =====
        return await RedirectByRoleAsync(user);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is not null)
            await _audit.LogAsync(AuditActions.Logout, nameof(ApplicationUser), user.Id, $"خروج: {user.Email}");

        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    /// <summary>
    /// الصفحة الرئيسية تختلف بحسب الدور.
    ///
    /// <para>
    /// أمين المخزن لا يُرسل لنقطة البيع: هو ليس بائعًا ولا يملك صلاحية
    /// إنشاء الفواتير، فإنزاله على شاشة ممنوعة عليه يجعل أول ما يراه بعد
    /// تسجيل الدخول رفضًا للوصول. مكانه الصحيح قائمة المخازن.
    /// </para>
    /// </summary>
    private async Task<IActionResult> RedirectByRoleAsync(ApplicationUser user)
    {
        if (await _userManager.IsInRoleAsync(user, AppRoles.Admin))
            return RedirectToAction("Index", "Dashboard");

        if (await _userManager.IsInRoleAsync(user, AppRoles.WarehouseKeeper))
            return RedirectToAction("Index", "Warehouses");

        return RedirectToAction("Index", "Pos");
    }

    private IActionResult RedirectToLanding()
    {
        if (User.IsInRole(AppRoles.Admin))
            return RedirectToAction("Index", "Dashboard");

        if (User.IsInRole(AppRoles.WarehouseKeeper))
            return RedirectToAction("Index", "Warehouses");

        return RedirectToAction("Index", "Pos");
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>نقطة الدخول — توزّع المستخدم حسب دوره</summary>
    [AllowAnonymous]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account");

        return User.IsInRole(AppRoles.Admin)
            ? RedirectToAction("Index", "Dashboard")
            : RedirectToAction("Index", "Pos");
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}

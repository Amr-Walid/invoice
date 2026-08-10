using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>نقطة الدخول — توزّع المستخدم حسب دوره</summary>
    ///
    /// <para>
    /// أمين المخزن يُرسل لقائمة المخازن لا لنقطة البيع: هو ليس بائعًا ولا
    /// يملك صلاحية الفواتير، فإنزاله على شاشة ممنوعة عليه يعطيه «غير مصرح
    /// لك بالوصول». وهذا المسار يُطرق كثيرًا: الشعار في الترويسة والقائمة
    /// الجانبية كلاهما يشير إلى «/»، فكان أمين المخزن يُطرد من النظام بمجرد
    /// أن يضغط اسم النظام — بينما تسجيل الدخول ينزله في مكانه الصحيح.
    /// (نفس قاعدة <c>AccountController.RedirectByRoleAsync</c>.)
    /// </para>
    [AllowAnonymous]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account");

        if (User.IsInRole(AppRoles.Admin))
            return RedirectToAction("Index", "Dashboard");

        if (User.IsInRole(AppRoles.WarehouseKeeper))
            return RedirectToAction("Index", "Warehouses");

        return RedirectToAction("Index", "Pos");
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}

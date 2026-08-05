using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// صفحة العملاء — بيانات العميل (الاسم والرقم) والفواتير التابعة له.
///
/// ملاحظتان على التصميم:
///  • سجل العملاء يتكوّن تلقائيًا من حركة البيع (انظر
///    <see cref="ICustomerService.FindOrCreateAsync"/>)، فلا توجد شاشة
///    «إضافة عميل» منفصلة — العميل يُنشأ بمجرد كتابة رقمه في الفاتورة.
///  • المندوب يرى العملاء لكن الفواتير المعروضة في صفحة العميل هي فواتيره
///    هو فقط، تماشيًا مع قاعدة عزل بيانات المندوب المطبَّقة في كل النظام.
/// </summary>
[Authorize]
public class CustomersController : Controller
{
    private const int PageSize = 20;

    private readonly ICustomerService _customers;

    public CustomersController(ICustomerService customers) => _customers = customers;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var vm = await _customers.SearchAsync(q, page, PageSize);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        // المندوب لا يرى إلا فواتيره ومرتجعاته؛ المدير يرى كل شيء
        var restrictTo = User.IsInRole(AppRoles.Admin) ? null : CurrentUserId();
        var vm = await _customers.GetDetailsAsync(id, restrictTo);

        if (vm is null) return NotFound();
        return View(vm);
    }

    /// <summary>
    /// إكمال تلقائي لخانة العميل داخل نافذة الفاتورة. يُعيد JSON خفيفًا،
    /// فيكتب المندوب أول أرقام الهاتف ويختار العميل بلا إعادة كتابة اسمه.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Suggest(string? term)
    {
        var results = await _customers.SuggestAsync(term);
        return Json(results);
    }

    private string? CurrentUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}

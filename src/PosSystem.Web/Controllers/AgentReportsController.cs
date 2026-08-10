using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// تقارير المندوب — يرى فواتيره هو فقط.
/// العزل مطبَّق في الـ Service عبر restrictToAgentId، لا في الواجهة فقط.
///
/// أمين المخزن لا يبيع فلا فواتير له، وكانت الشاشة تفتح له فارغة —
/// وهو ما يُقرأ «النظام لا يعمل» لا «لا صلاحية».
/// </summary>
[Authorize(Roles = AppRoles.AdminOrAgent)]
public class AgentReportsController : Controller
{
    private readonly IReportService _reports;
    private readonly UserManager<ApplicationUser> _userManager;

    public AgentReportsController(IReportService reports, UserManager<ApplicationUser> userManager)
    {
        _reports = reports;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(ReportFilterVm filter)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await _reports.GetReportAsync(filter, restrictToAgentId: user.Id);
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var invoice = await _reports.GetInvoiceDetailsAsync(id, restrictToAgentId: user.Id);
        if (invoice is null) return NotFound();

        return View(invoice);
    }
}

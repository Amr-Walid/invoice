using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class DashboardController : Controller
{
    private readonly IReportService _reports;

    public DashboardController(IReportService reports) => _reports = reports;

    public async Task<IActionResult> Index()
    {
        var vm = await _reports.GetAdminDashboardAsync();
        return View(vm);
    }
}

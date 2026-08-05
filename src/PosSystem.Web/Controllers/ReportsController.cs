using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;
using PosSystem.Web.Services;
using System.Text;

namespace PosSystem.Web.Controllers;

/// <summary>التقارير الشاملة — للمدير فقط</summary>
[Authorize(Roles = AppRoles.Admin)]
public class ReportsController : Controller
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    public async Task<IActionResult> Index(ReportFilterVm filter)
    {
        var result = await _reports.GetReportAsync(filter);
        return View(result);
    }

    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _reports.GetInvoiceDetailsAsync(id);
        if (invoice is null) return NotFound();
        return View(invoice);
    }

    /// <summary>تقرير المنتجات الأكثر مبيعًا</summary>
    public async Task<IActionResult> TopProducts(ReportFilterVm filter)
    {
        var (from, to) = filter.Resolve(DateTime.Now);
        var items = await _reports.GetTopProductsAsync(from, to, 50);
        ViewBag.Filter = filter;
        return View(items);
    }

    /// <summary>تصدير التقرير إلى CSV (يفتح في Excel)</summary>
    public async Task<IActionResult> ExportCsv(ReportFilterVm filter)
    {
        filter.Page = 1;
        filter.PageSize = 100000;
        var result = await _reports.GetReportAsync(filter);

        var sb = new StringBuilder();
        sb.AppendLine("رقم الفاتورة,التاريخ,المندوب,العميل,الإجمالي الفرعي,نسبة الخصم,قيمة الخصم,الإجمالي");

        foreach (var i in result.Invoices)
        {
            sb.AppendLine(string.Join(',',
                Csv(i.InvoiceNumber),
                Csv(i.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                Csv(i.Agent?.FullName ?? ""),
                Csv(i.CustomerName ?? ""),
                i.SubTotal.ToString("0.00"),
                i.DiscountPercentage.ToString("0.##"),
                i.DiscountAmount.ToString("0.00"),
                i.Total.ToString("0.00")));
        }

        // BOM لضمان قراءة العربية بشكل صحيح في Excel
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

        return File(bytes, "text/csv", $"sales-report-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

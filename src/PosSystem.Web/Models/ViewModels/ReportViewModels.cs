using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Models.ViewModels;

public enum ReportPeriod
{
    Today = 1,
    Week = 2,
    Month = 3,
    All = 4,
    Custom = 5
}

/// <summary>فلتر التقارير — يترجم الفترة المختارة إلى نطاق تاريخي</summary>
public class ReportFilterVm
{
    public ReportPeriod Period { get; set; } = ReportPeriod.Today;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? AgentId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public string PeriodLabel => Period switch
    {
        ReportPeriod.Today => "اليوم",
        ReportPeriod.Week => "هذا الأسبوع",
        ReportPeriod.Month => "هذا الشهر",
        ReportPeriod.All => "كل الفترات",
        _ => "فترة مخصصة"
    };

    /// <summary>
    /// حساب النطاق الزمني. النهاية دائمًا exclusive (بداية اليوم التالي)
    /// حتى تُحتسب فواتير اليوم الأخير بالكامل بما فيها آخر ثانية.
    /// </summary>
    public (DateTime? from, DateTime? to) Resolve(DateTime now)
    {
        var today = now.Date;
        return Period switch
        {
            ReportPeriod.Today => (today, today.AddDays(1)),
            ReportPeriod.Week => (today.AddDays(-6), today.AddDays(1)),
            ReportPeriod.Month => (new DateTime(today.Year, today.Month, 1), today.AddDays(1)),
            ReportPeriod.All => (null, null),
            _ => (From?.Date, (To?.Date ?? today).AddDays(1))
        };
    }
}

/// <summary>ملخص إحصائي لفترة</summary>
public class ReportSummaryVm
{
    public int InvoiceCount { get; set; }
    public int ItemsSold { get; set; }
    public decimal GrossSales { get; set; }      // قبل الخصم
    public decimal TotalDiscount { get; set; }
    public decimal NetSales { get; set; }        // بعد الخصم

    /// <summary>إجمالي ما رُدّ للعملاء خلال الفترة (من المرتجعات)</summary>
    public decimal TotalRefunded { get; set; }

    /// <summary>عدد القطع المُرجَعة خلال الفترة</summary>
    public int ItemsReturned { get; set; }

    /// <summary>
    /// المبيعات الفعلية بعد خصم المرتجعات — هذا ما دخل الخزنة حقًا،
    /// والرقم الذي يهم المدير أكثر من NetSales وحده.
    /// </summary>
    public decimal NetAfterReturns => NetSales - TotalRefunded;

    /// <summary>صافي القطع المُباعة بعد المرتجعات</summary>
    public int NetItemsSold => ItemsSold - ItemsReturned;

    // لا تكلفة ولا أرباح: النظام يركّز على المبيعات والخصومات فقط
    public decimal AverageInvoice => InvoiceCount == 0 ? 0 : Math.Round(NetSales / InvoiceCount, 2);
}

public class ReportResultVm
{
    public ReportFilterVm Filter { get; set; } = new();
    public ReportSummaryVm Summary { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();
    public int TotalCount { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1
        : (int)Math.Ceiling(TotalCount / (double)Filter.PageSize);
    public List<AgentOptionVm> Agents { get; set; } = new();
}

public class AgentOptionVm
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public class TopProductVm
{
    public string ProductName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
}

public class AgentPerformanceVm
{
    public string AgentName { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal NetSales { get; set; }
}

/// <summary>نقطة على الرسم البياني اليومي</summary>
public class DailySalesPointVm
{
    public DateTime Date { get; set; }
    public decimal NetSales { get; set; }
    public int InvoiceCount { get; set; }
}

/// <summary>لوحة تحكم المدير</summary>
public class AdminDashboardVm
{
    public ReportSummaryVm Today { get; set; } = new();
    public ReportSummaryVm Month { get; set; } = new();
    public int ProductCount { get; set; }
    public int BrandCount { get; set; }
    public int CategoryCount { get; set; }
    public int ActiveCouponCount { get; set; }
    public int AgentCount { get; set; }
    public List<Product> LowStockProducts { get; set; } = new();
    public List<TopProductVm> TopProducts { get; set; } = new();
    public List<AgentPerformanceVm> AgentPerformance { get; set; } = new();
    public List<DailySalesPointVm> Last7Days { get; set; } = new();
}

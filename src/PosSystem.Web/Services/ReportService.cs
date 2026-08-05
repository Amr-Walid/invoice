using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;

namespace PosSystem.Web.Services;

public interface IReportService
{
    Task<ReportResultVm> GetReportAsync(ReportFilterVm filter, string? restrictToAgentId = null);
    Task<Invoice?> GetInvoiceDetailsAsync(int id, string? restrictToAgentId = null);
    Task<AdminDashboardVm> GetAdminDashboardAsync();
    Task<AgentDashboardVm> GetAgentDashboardAsync(string agentId, string agentName);
    Task<List<TopProductVm>> GetTopProductsAsync(DateTime? from, DateTime? to, int take = 10);
}

public class ReportService : IReportService
{
    private readonly AppDbContext _db;

    public ReportService(AppDbContext db) => _db = db;

    /// <summary>
    /// التقرير الرئيسي. عند تمرير restrictToAgentId يتم عزل البيانات
    /// على مستوى الاستعلام نفسه — المندوب لا يستطيع رؤية فواتير غيره بأي حال.
    /// </summary>
    public async Task<ReportResultVm> GetReportAsync(ReportFilterVm filter, string? restrictToAgentId = null)
    {
        var (from, to) = filter.Resolve(DateTime.Now);

        var query = _db.Invoices.AsNoTracking()
            .Include(i => i.Agent)
            .Include(i => i.Items)
            .Where(i => i.Status != InvoiceStatus.Cancelled);

        // عزل بيانات المندوب — إلزامي على مستوى الـ Service
        if (!string.IsNullOrEmpty(restrictToAgentId))
            query = query.Where(i => i.AgentId == restrictToAgentId);
        else if (!string.IsNullOrEmpty(filter.AgentId))
            query = query.Where(i => i.AgentId == filter.AgentId);

        if (from.HasValue) query = query.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(i => i.CreatedAt < to.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(i =>
                i.InvoiceNumber.Contains(term) ||
                (i.CustomerName != null && i.CustomerName.Contains(term)) ||
                (i.CustomerPhone != null && i.CustomerPhone.Contains(term)));
        }

        // الملخص يُحسب على كامل النطاق (ليس الصفحة الحالية فقط)
        var summary = await BuildSummaryAsync(query);

        var page = Math.Max(1, filter.Page);
        var pageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;

        var invoices = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var agents = string.IsNullOrEmpty(restrictToAgentId)
            ? await GetAgentOptionsAsync()
            : new List<AgentOptionVm>();

        filter.Page = page;
        return new ReportResultVm
        {
            Filter = filter,
            Summary = summary,
            Invoices = invoices,
            TotalCount = summary.InvoiceCount,
            Agents = agents
        };
    }

    /// <summary>
    /// تفاصيل الفاتورة. عند تمرير restrictToAgentId ترجع null إذا كانت
    /// الفاتورة لمندوب آخر — يمنع الوصول عبر تعديل الـ id في الرابط.
    /// </summary>
    public async Task<Invoice?> GetInvoiceDetailsAsync(int id, string? restrictToAgentId = null)
    {
        var query = _db.Invoices.AsNoTracking()
            .Include(i => i.Agent)
            .Include(i => i.Items)
            .ThenInclude(it => it.Product)
                .ThenInclude(p => p!.Brand)
            .Include(i => i.Items)
            .ThenInclude(it => it.Product)
                .ThenInclude(p => p!.Category)
            .Where(i => i.Id == id);

        if (!string.IsNullOrEmpty(restrictToAgentId))
            query = query.Where(i => i.AgentId == restrictToAgentId);

        return await query.FirstOrDefaultAsync();
    }

    public async Task<AdminDashboardVm> GetAdminDashboardAsync()
    {
        var today = DateTime.Now.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var tomorrow = today.AddDays(1);

        var vm = new AdminDashboardVm
        {
            Today = await SummarizeAsync(today, tomorrow),
            Month = await SummarizeAsync(monthStart, tomorrow),
            ProductCount = await _db.Products.CountAsync(p => p.IsActive),
            BrandCount = await _db.Brands.CountAsync(b => b.IsActive),
            CategoryCount = await _db.Categories.CountAsync(c => c.IsActive),
            ActiveCouponCount = await _db.Coupons.CountAsync(c => c.IsActive),
            LowStockProducts = await _db.Products.AsNoTracking()
                .Include(p => p.Brand).Include(p => p.Category)
                .Where(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold)
                .OrderBy(p => p.StockQuantity)
                .Take(10)
                .ToListAsync(),
            TopProducts = await GetTopProductsAsync(monthStart, tomorrow, 5),
            Last7Days = await GetDailySalesAsync(today.AddDays(-6), tomorrow)
        };

        // أداء المناديب خلال الشهر
        vm.AgentPerformance = await GetAgentPerformanceAsync(monthStart, tomorrow);

        vm.AgentCount = await _db.Users.CountAsync(u => u.IsActive);
        return vm;
    }

    public async Task<AgentDashboardVm> GetAgentDashboardAsync(string agentId, string agentName)
    {
        var today = DateTime.Now.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var tomorrow = today.AddDays(1);

        var todayQ = _db.Invoices.AsNoTracking().Where(i =>
            i.AgentId == agentId && i.Status != InvoiceStatus.Cancelled &&
            i.CreatedAt >= today && i.CreatedAt < tomorrow);

        var monthQ = _db.Invoices.AsNoTracking().Where(i =>
            i.AgentId == agentId && i.Status != InvoiceStatus.Cancelled &&
            i.CreatedAt >= monthStart && i.CreatedAt < tomorrow);

        return new AgentDashboardVm
        {
            AgentName = agentName,
            TodayInvoices = await todayQ.CountAsync(),
            TodaySales = await SumTotalAsync(todayQ),
            MonthInvoices = await monthQ.CountAsync(),
            MonthSales = await SumTotalAsync(monthQ)
        };
    }

    public async Task<List<TopProductVm>> GetTopProductsAsync(DateTime? from, DateTime? to, int take = 10)
    {
        var query = _db.InvoiceItems.AsNoTracking()
            .Where(it => it.Invoice!.Status != InvoiceStatus.Cancelled);

        if (from.HasValue) query = query.Where(it => it.Invoice!.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(it => it.Invoice!.CreatedAt < to.Value);

        // SQLite لا يدعم SUM على decimal، فنجمع الإيراد على العميل في هذه الحالة فقط
        if (_db.Database.IsSqlite())
        {
            var rows = await query
                .Select(it => new { it.ProductName, it.Barcode, it.Quantity, it.LineTotal })
                .ToListAsync();

            return rows
                .GroupBy(x => new { x.ProductName, x.Barcode })
                .Select(g => new TopProductVm
                {
                    ProductName = g.Key.ProductName,
                    Barcode = g.Key.Barcode,
                    QuantitySold = g.Sum(x => x.Quantity),
                    Revenue = g.Sum(x => x.LineTotal)
                })
                .OrderByDescending(x => x.QuantitySold)
                .Take(take)
                .ToList();
        }

        return await query
            .GroupBy(it => new { it.ProductId, it.ProductName, it.Barcode })
            .Select(g => new TopProductVm
            {
                ProductName = g.Key.ProductName,
                Barcode = g.Key.Barcode,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(x => x.QuantitySold)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// مجموع صافي المبيعات بعد خصم المرتجعات — هذا ما دخل الخزنة فعلًا،
    /// وهو الرقم الذي يجب أن يراه المندوب في لوحته. مع مراعاة قيود SQLite
    /// على SUM للأرقام العشرية.
    /// </summary>
    private async Task<decimal> SumTotalAsync(IQueryable<Invoice> query)
    {
        if (_db.Database.IsSqlite())
        {
            var rows = await query.Select(i => new { i.Total, i.RefundedAmount }).ToListAsync();
            return rows.Sum(r => r.Total - r.RefundedAmount);
        }

        var total = await query.SumAsync(i => (decimal?)i.Total) ?? 0m;
        var refunded = await query.SumAsync(i => (decimal?)i.RefundedAmount) ?? 0m;
        return total - refunded;
    }

    private Task<ReportSummaryVm> SummarizeAsync(DateTime from, DateTime to)
    {
        var q = _db.Invoices.AsNoTracking().Where(i =>
            i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= from && i.CreatedAt < to);

        return BuildSummaryAsync(q);
    }

    /// <summary>
    /// يبني ملخّص التقرير من أي استعلام فواتير.
    /// SQL Server (المزوّد الأساسي) يجمع الأرقام العشرية داخل قاعدة البيانات،
    /// أما SQLite فلا يدعم SUM على decimal، فنجمع على العميل في هذه الحالة فقط.
    ///
    /// المرتجعات تُنسَب لفترة الفاتورة الأصلية لا لفترة الإرجاع، لأن
    /// <see cref="Invoice.RefundedAmount"/> يُقرأ من الفاتورة نفسها. هذا هو
    /// السلوك المطلوب: «كم صافي ما تحقّق فعلًا من مبيعات هذه الفترة؟»
    /// </summary>
    private async Task<ReportSummaryVm> BuildSummaryAsync(IQueryable<Invoice> query)
    {
        if (_db.Database.IsSqlite())
        {
            var rows = await query
                .Select(i => new
                {
                    i.SubTotal,
                    i.DiscountAmount,
                    i.Total,
                    i.RefundedAmount,
                    Quantity = i.Items.Sum(x => x.Quantity),
                    Returned = i.Items.Sum(x => x.ReturnedQuantity)
                })
                .ToListAsync();

            return new ReportSummaryVm
            {
                InvoiceCount = rows.Count,
                GrossSales = rows.Sum(r => r.SubTotal),
                TotalDiscount = rows.Sum(r => r.DiscountAmount),
                NetSales = rows.Sum(r => r.Total),
                ItemsSold = rows.Sum(r => r.Quantity),
                TotalRefunded = rows.Sum(r => r.RefundedAmount),
                ItemsReturned = rows.Sum(r => r.Returned)
            };
        }

        return new ReportSummaryVm
        {
            InvoiceCount = await query.CountAsync(),
            GrossSales = await query.SumAsync(i => (decimal?)i.SubTotal) ?? 0m,
            TotalDiscount = await query.SumAsync(i => (decimal?)i.DiscountAmount) ?? 0m,
            NetSales = await query.SumAsync(i => (decimal?)i.Total) ?? 0m,
            ItemsSold = await query.SelectMany(i => i.Items).SumAsync(x => (int?)x.Quantity) ?? 0,
            TotalRefunded = await query.SumAsync(i => (decimal?)i.RefundedAmount) ?? 0m,
            ItemsReturned = await query.SelectMany(i => i.Items)
                .SumAsync(x => (int?)x.ReturnedQuantity) ?? 0
        };
    }

    /// <summary>أداء المناديب خلال فترة، مع مراعاة قيود SQLite على decimal.</summary>
    private async Task<List<AgentPerformanceVm>> GetAgentPerformanceAsync(DateTime from, DateTime to)
    {
        var q = _db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= from && i.CreatedAt < to);

        if (_db.Database.IsSqlite())
        {
            var rows = await q
                .Select(i => new { Name = i.Agent!.FullName, i.Total })
                .ToListAsync();

            return rows
                .GroupBy(x => x.Name)
                .Select(g => new AgentPerformanceVm
                {
                    AgentName = g.Key,
                    InvoiceCount = g.Count(),
                    NetSales = g.Sum(x => x.Total)
                })
                .OrderByDescending(x => x.NetSales)
                .ToList();
        }

        return await q
            .GroupBy(i => new { i.AgentId, Name = i.Agent!.FullName })
            .Select(g => new AgentPerformanceVm
            {
                AgentName = g.Key.Name,
                InvoiceCount = g.Count(),
                NetSales = g.Sum(x => x.Total)
            })
            .OrderByDescending(x => x.NetSales)
            .ToListAsync();
    }

    private async Task<List<DailySalesPointVm>> GetDailySalesAsync(DateTime from, DateTime to)
    {
        var invoiceQuery = _db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= from && i.CreatedAt < to);

        List<DailySalesPointVm> raw;

        if (_db.Database.IsSqlite())
        {
            var rows = await invoiceQuery
                .Select(i => new { i.CreatedAt, i.Total })
                .ToListAsync();

            raw = rows
                .GroupBy(x => x.CreatedAt.Date)
                .Select(g => new DailySalesPointVm
                {
                    Date = g.Key,
                    NetSales = g.Sum(x => x.Total),
                    InvoiceCount = g.Count()
                })
                .ToList();
        }
        else
        {
            raw = await invoiceQuery
                .GroupBy(i => i.CreatedAt.Date)
                .Select(g => new DailySalesPointVm
                {
                    Date = g.Key,
                    NetSales = g.Sum(x => x.Total),
                    InvoiceCount = g.Count()
                })
                .ToListAsync();
        }

        // نُكمل الأيام الفارغة بأصفار حتى يكون الرسم البياني متصلًا
        var result = new List<DailySalesPointVm>();
        for (var d = from.Date; d < to.Date; d = d.AddDays(1))
        {
            var found = raw.FirstOrDefault(x => x.Date == d);
            result.Add(found ?? new DailySalesPointVm { Date = d, NetSales = 0, InvoiceCount = 0 });
        }
        return result;
    }

    private Task<List<AgentOptionVm>> GetAgentOptionsAsync() =>
        _db.Users.AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new AgentOptionVm { Id = u.Id, FullName = u.FullName })
            .ToListAsync();
}

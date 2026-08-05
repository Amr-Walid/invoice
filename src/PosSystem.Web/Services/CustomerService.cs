using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;

namespace PosSystem.Web.Services;

public interface ICustomerService
{
    /// <summary>
    /// يجد العميل برقم هاتفه أو يُنشئه. يُستدعى عند حفظ كل فاتورة،
    /// فيتكوّن سجل العملاء تلقائيًا من حركة البيع بلا إدخال منفصل.
    /// </summary>
    Task<Customer?> FindOrCreateAsync(string? name, string? phone);

    Task<Customer?> FindByPhoneAsync(string? phone);
    Task<CustomerListVm> SearchAsync(string? q, int page, int pageSize = 20);

    /// <summary>
    /// تفاصيل العميل وفواتيره. عند تمرير <paramref name="restrictToAgentId"/>
    /// تُعرض فواتير هذا المندوب فقط — نفس قاعدة العزل المطبَّقة في التقارير.
    /// </summary>
    Task<CustomerDetailsVm?> GetDetailsAsync(int id, string? restrictToAgentId = null);
    Task<List<CustomerSuggestionVm>> SuggestAsync(string? term, int take = 8);
}

public class CustomerService : ICustomerService
{
    private readonly AppDbContext _db;

    public CustomerService(AppDbContext db) => _db = db;

    public Task<Customer?> FindByPhoneAsync(string? phone)
    {
        var normalized = PhoneHelper.Normalize(phone);
        if (normalized.Length == 0) return Task.FromResult<Customer?>(null);

        return _db.Customers.FirstOrDefaultAsync(c => c.PhoneNormalized == normalized);
    }

    public async Task<Customer?> FindOrCreateAsync(string? name, string? phone)
    {
        // بلا رقم هاتف لا يوجد عميل — الرقم هو المُعرِّف. البيع بلا عميل مسموح.
        var normalized = PhoneHelper.Normalize(phone);
        if (normalized.Length == 0) return null;

        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.PhoneNormalized == normalized);
        var cleanName = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();

        if (existing is not null)
        {
            // نُحدّث الاسم فقط إن كان ناقصًا أو مؤقتًا — لا نطمس اسمًا صحيحًا
            // بمدخل عابر، ولا نُبقي «عميل بلا اسم» لو صار الاسم معروفًا لاحقًا.
            if (cleanName is not null && IsPlaceholderName(existing.Name) &&
                !IsPlaceholderName(cleanName))
            {
                existing.Name = Trim(cleanName, 150);
                existing.UpdatedAt = DateTime.Now;
            }
            return existing;
        }

        var customer = new Customer
        {
            Name = Trim(cleanName ?? "عميل " + normalized, 150),
            Phone = Trim((phone ?? string.Empty).Trim(), 30),
            PhoneNormalized = normalized,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        _db.Customers.Add(customer);
        return customer;
    }

    public async Task<CustomerListVm> SearchAsync(string? q, int page, int pageSize = 20)
    {
        if (pageSize <= 0) pageSize = 20;
        var query = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            // البحث بالرقم يستخدم الصيغة الموحَّدة، فيجد العميل أيًّا كانت
            // طريقة كتابة الرقم في خانة البحث.
            var normalized = PhoneHelper.Normalize(term);
            query = normalized.Length >= 3
                ? query.Where(c => c.Name.Contains(term) || c.PhoneNormalized.Contains(normalized))
                : query.Where(c => c.Name.Contains(term) || c.Phone.Contains(term));
        }

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page <= 0 ? 1 : page, 1, totalPages);

        // الإحصائيات تُحسب في نفس الاستعلام — لا N+1 لكل عميل
        var rows = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Phone,
                c.IsActive,
                c.CreatedAt,
                InvoiceCount = c.Invoices.Count,
                ReturnCount = c.Returns.Count,
                LastPurchase = c.Invoices
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(i => (DateTime?)i.CreatedAt)
                    .FirstOrDefault(),
                Totals = c.Invoices.Select(i => new { i.Total, i.RefundedAmount }).ToList()
            })
            .ToListAsync();

        return new CustomerListVm
        {
            Query = q,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total,
            PageSize = pageSize,
            Customers = rows.Select(r => new CustomerRowVm
            {
                Id = r.Id,
                Name = r.Name,
                Phone = r.Phone,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
                InvoiceCount = r.InvoiceCount,
                ReturnCount = r.ReturnCount,
                LastPurchase = r.LastPurchase,
                TotalSpent = r.Totals.Sum(t => t.Total - t.RefundedAmount)
            }).ToList()
        };
    }

    public async Task<CustomerDetailsVm?> GetDetailsAsync(int id, string? restrictToAgentId = null)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return null;

        // فواتير العميل مربوطة إما بمعرّفه أو برقم هاتفه الموحَّد — الشرط
        // الثاني يضمن ظهور الفواتير القديمة التي سُجّلت قبل وجود سجل العملاء.
        var invoiceQuery = _db.Invoices.AsNoTracking()
            .Include(i => i.Agent)
            .Include(i => i.Items)
            .Where(i => i.CustomerId == customer.Id ||
                        (i.CustomerId == null && i.CustomerPhone != null &&
                         i.CustomerPhone == customer.Phone));

        var returnQuery = _db.Returns.AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.CustomerId == customer.Id ||
                        r.Invoice!.CustomerId == customer.Id);

        // عزل بيانات المندوب على مستوى الاستعلام لا الواجهة
        if (!string.IsNullOrEmpty(restrictToAgentId))
        {
            invoiceQuery = invoiceQuery.Where(i => i.AgentId == restrictToAgentId);
            returnQuery = returnQuery.Where(r => r.Invoice!.AgentId == restrictToAgentId);
        }

        var invoices = await invoiceQuery
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

        var returns = await returnQuery
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync();

        return new CustomerDetailsVm
        {
            Customer = customer,
            Invoices = invoices,
            Returns = returns,
            TotalSpent = invoices.Sum(i => i.Total - i.RefundedAmount),
            GrossSpent = invoices.Sum(i => i.Total),
            TotalRefunded = invoices.Sum(i => i.RefundedAmount),
            ItemsBought = invoices.Sum(i => i.TotalQuantity)
        };
    }

    public async Task<List<CustomerSuggestionVm>> SuggestAsync(string? term, int take = 8)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
            return new List<CustomerSuggestionVm>();

        var t = term.Trim();
        var normalized = PhoneHelper.Normalize(t);
        take = Math.Clamp(take, 1, 20);

        var query = _db.Customers.AsNoTracking().Where(c => c.IsActive);
        query = normalized.Length >= 3
            ? query.Where(c => c.Name.Contains(t) || c.PhoneNormalized.Contains(normalized))
            : query.Where(c => c.Name.Contains(t) || c.Phone.Contains(t));

        return await query
            .OrderBy(c => c.Name)
            .Take(take)
            .Select(c => new CustomerSuggestionVm
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                InvoiceCount = c.Invoices.Count
            })
            .ToListAsync();
    }

    /// <summary>هل الاسم مُولَّد تلقائيًا («عميل 0100…») لا اسمًا حقيقيًا؟</summary>
    private static bool IsPlaceholderName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.TrimStart().StartsWith("عميل ", StringComparison.Ordinal);

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;

namespace PosSystem.Web.Services;

public class SupplierResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public Supplier? Supplier { get; init; }

    public static SupplierResult Fail(string message) =>
        new() { Success = false, Message = message };

    public static SupplierResult Ok(string message, Supplier? supplier = null) =>
        new() { Success = true, Message = message, Supplier = supplier };
}

public interface ISupplierService
{
    Task<SupplierListVm> SearchAsync(string? q, bool includeInactive, int page, int pageSize = 20);
    Task<List<Supplier>> ListActiveAsync();
    Task<Supplier?> GetAsync(int id);

    /// <summary>كشف حساب المورد: أوامره ومجاميعها — أول شاشة تُطلب عند أي مراجعة</summary>
    Task<SupplierStatementVm?> GetStatementAsync(int id);

    Task<SupplierResult> CreateAsync(Supplier input, string? userId);
    Task<SupplierResult> UpdateAsync(Supplier input, string? userId);

    /// <summary>
    /// حذف المورد إن لم يكن له أمر شراء واحد، وإلا تعطيله فقط.
    /// </summary>
    Task<SupplierResult> DeleteAsync(int id, string? userId);
    Task<SupplierResult> ToggleActiveAsync(int id, string? userId);
}

/// <summary>
/// إدارة الموردين.
///
/// <para><b>الهاتف هو المُعرِّف لا الاسم.</b> نفس قاعدة العملاء: الأسماء تُكتب
/// بصيغ متعددة لنفس الجهة، فيتفرّق تاريخها على سجلَّين ويصير كشف الحساب
/// كاذبًا. لذا نُطبّع الرقم بـ <see cref="PhoneHelper"/> — بلا منطق ثانٍ
/// يختلف عنه — ونتحقق من التفرّد في الخدمة قبل أن يرفضه الفهرس الفريد،
/// حتى تصل للمستخدم رسالة عربية مفهومة لا استثناء قاعدة بيانات.</para>
///
/// <para><b>المورد لا يُحذف إن كان له أمر شراء.</b> أمر الشراء سِجل مالي،
/// وحذف الجهة التي وردّت منه يُفقد نصف قصته. البديل التعطيل: يختفي من قوائم
/// الاختيار وتبقى حركاته مقروءة.</para>
/// </summary>
public class SupplierService : ISupplierService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<SupplierService> _logger;

    public SupplierService(AppDbContext db, IAuditService audit, ILogger<SupplierService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    // ==================== قراءة ====================

    public async Task<SupplierListVm> SearchAsync(
        string? q, bool includeInactive, int page, int pageSize = 20)
    {
        if (pageSize <= 0) pageSize = 20;
        if (page <= 0) page = 1;

        var query = _db.Suppliers.AsNoTracking().AsQueryable();

        if (!includeInactive)
            query = query.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var normalized = PhoneHelper.Normalize(term);

            // البحث برقم يستخدم الصيغة الموحَّدة، فيجد المورد أيًّا كانت
            // طريقة كتابة رقمه (بمسافات أو شرطات أو أرقام عربية)
            if (normalized.Length >= 4)
            {
                query = query.Where(s =>
                    s.PhoneNormalized.Contains(normalized) ||
                    s.Name.Contains(term));
            }
            else
            {
                query = query.Where(s =>
                    s.Name.Contains(term) ||
                    (s.ContactPerson != null && s.ContactPerson.Contains(term)) ||
                    (s.Email != null && s.Email.Contains(term)));
            }
        }

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;

        // المجاميع المالية تُحسب من الأوامر غير الملغاة: الأمر الملغي لم
        // يُنفَّق عليه شيء، وإدخاله في «إجمالي التعامل» يُضخّم الرقم كذبًا.
        var rows = await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierRowVm
            {
                Id = s.Id,
                Name = s.Name,
                Phone = s.Phone,
                ContactPerson = s.ContactPerson,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                OrderCount = s.PurchaseOrders.Count(o => o.Status != PurchaseStatus.Cancelled),
                OpenOrderCount = s.PurchaseOrders.Count(o =>
                    o.Status == PurchaseStatus.Confirmed ||
                    o.Status == PurchaseStatus.PartiallyReceived),
                LastOrderAt = s.PurchaseOrders
                    .Where(o => o.Status != PurchaseStatus.Cancelled)
                    .Max(o => (DateTime?)o.CreatedAt)
            })
            .ToListAsync();

        // SUM على decimal غير مدعوم في SQLite، فنجمع المبالغ في الذاكرة
        // باستعلام واحد للصفحة المعروضة فقط (لا N+1)
        var ids = rows.Select(r => r.Id).ToList();
        if (ids.Count > 0)
        {
            var amounts = await _db.PurchaseOrders.AsNoTracking()
                .Where(o => ids.Contains(o.SupplierId) && o.Status != PurchaseStatus.Cancelled)
                .Select(o => new { o.SupplierId, o.Total })
                .ToListAsync();

            var bySupplier = amounts
                .GroupBy(a => a.SupplierId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Total));

            foreach (var row in rows)
                row.TotalPurchased = bySupplier.TryGetValue(row.Id, out var v) ? v : 0m;
        }

        return new SupplierListVm
        {
            Query = q,
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalCount = totalCount,
            Suppliers = rows
        };
    }

    public Task<List<Supplier>> ListActiveAsync() =>
        _db.Suppliers.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();

    public Task<Supplier?> GetAsync(int id) =>
        _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);

    public async Task<SupplierStatementVm?> GetStatementAsync(int id)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return null;

        var orders = await _db.PurchaseOrders.AsNoTracking()
            .Include(o => o.Warehouse)
            .Include(o => o.Items)
            .Where(o => o.SupplierId == id)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(200)
            .ToListAsync();

        var effective = orders.Where(o => o.Status != PurchaseStatus.Cancelled).ToList();

        return new SupplierStatementVm
        {
            Supplier = supplier,
            Orders = orders,
            TotalPurchased = effective.Sum(o => o.Total),
            OrderCount = effective.Count,
            OpenOrderCount = effective.Count(o => o.CanReceive),
            CancelledCount = orders.Count(o => o.Status == PurchaseStatus.Cancelled),
            PiecesOrdered = effective.Sum(o => o.Items.Sum(i => i.Quantity)),
            PiecesReceived = effective.Sum(o => o.Items.Sum(i => i.ReceivedQuantity))
        };
    }

    // ==================== كتابة ====================

    public async Task<SupplierResult> CreateAsync(Supplier input, string? userId)
    {
        var name = Trim(input.Name, 150);
        if (string.IsNullOrWhiteSpace(name))
            return SupplierResult.Fail("اسم المورد مطلوب");

        var normalized = PhoneHelper.Normalize(input.Phone);
        if (normalized.Length < 6)
            return SupplierResult.Fail("رقم هاتف المورد غير صحيح");

        // التحقق قبل الحفظ لتصل رسالة مفهومة، والفهرس الفريد شبكة الأمان
        // للحالة النادرة: إدخالان متزامنان لنفس الرقم
        var duplicate = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.PhoneNormalized == normalized);
        if (duplicate is not null)
            return SupplierResult.Fail(
                $"هذا الرقم مسجّل بالفعل للمورد «{duplicate.Name}»");

        var supplier = new Supplier
        {
            Name = name,
            Phone = Trim(input.Phone, 30),
            PhoneNormalized = normalized,
            Email = TrimOrNull(input.Email, 150),
            Address = TrimOrNull(input.Address, 200),
            ContactPerson = TrimOrNull(input.ContactPerson, 150),
            Notes = TrimOrNull(input.Notes, 500),
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        _db.Suppliers.Add(supplier);
        _audit.Track(AuditActions.Create, nameof(Supplier), null,
            $"إضافة مورد «{supplier.Name}» — {supplier.Phone}");
        await _db.SaveChangesAsync();

        return SupplierResult.Ok($"تم إضافة المورد «{supplier.Name}»", supplier);
    }

    public async Task<SupplierResult> UpdateAsync(Supplier input, string? userId)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == input.Id);
        if (supplier is null) return SupplierResult.Fail("المورد غير موجود");

        var name = Trim(input.Name, 150);
        if (string.IsNullOrWhiteSpace(name))
            return SupplierResult.Fail("اسم المورد مطلوب");

        var normalized = PhoneHelper.Normalize(input.Phone);
        if (normalized.Length < 6)
            return SupplierResult.Fail("رقم هاتف المورد غير صحيح");

        if (normalized != supplier.PhoneNormalized)
        {
            var duplicate = await _db.Suppliers
                .FirstOrDefaultAsync(s => s.PhoneNormalized == normalized && s.Id != supplier.Id);
            if (duplicate is not null)
                return SupplierResult.Fail(
                    $"هذا الرقم مسجّل بالفعل للمورد «{duplicate.Name}»");
        }

        var changes = new List<string>();
        if (supplier.Name != name) changes.Add($"الاسم: «{supplier.Name}» ← «{name}»");
        if (supplier.PhoneNormalized != normalized)
            changes.Add($"الهاتف: {supplier.Phone} ← {input.Phone}");

        supplier.Name = name;
        supplier.Phone = Trim(input.Phone, 30);
        supplier.PhoneNormalized = normalized;
        supplier.Email = TrimOrNull(input.Email, 150);
        supplier.Address = TrimOrNull(input.Address, 200);
        supplier.ContactPerson = TrimOrNull(input.ContactPerson, 150);
        supplier.Notes = TrimOrNull(input.Notes, 500);
        supplier.UpdatedAt = DateTime.Now;

        _audit.Track(AuditActions.Update, nameof(Supplier), supplier.Id.ToString(),
            changes.Count > 0
                ? $"تعديل المورد «{supplier.Name}» — {string.Join("، ", changes)}"
                : $"تعديل بيانات المورد «{supplier.Name}»");
        await _db.SaveChangesAsync();

        return SupplierResult.Ok($"تم تحديث بيانات «{supplier.Name}»", supplier);
    }

    public async Task<SupplierResult> DeleteAsync(int id, string? userId)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return SupplierResult.Fail("المورد غير موجود");

        var orderCount = await _db.PurchaseOrders.CountAsync(o => o.SupplierId == id);
        if (orderCount > 0)
            return SupplierResult.Fail(
                $"لا يمكن حذف «{supplier.Name}» ولديه {orderCount} أمر شراء — " +
                "استخدم التعطيل ليختفي من قوائم الاختيار وتبقى حركاته في السجل");

        var name = supplier.Name;
        _db.Suppliers.Remove(supplier);
        _audit.Track(AuditActions.Delete, nameof(Supplier), id.ToString(),
            $"حذف المورد «{name}» (بلا أوامر شراء)");
        await _db.SaveChangesAsync();

        return SupplierResult.Ok($"تم حذف المورد «{name}»");
    }

    public async Task<SupplierResult> ToggleActiveAsync(int id, string? userId)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return SupplierResult.Fail("المورد غير موجود");

        // التعطيل يُمنع ولديه أمر مفتوح: الأمر ينتظر توريدًا من هذه الجهة،
        // وإخفاؤها يترك الأمر معلّقًا بلا مسار لإكماله
        if (supplier.IsActive)
        {
            var openCount = await _db.PurchaseOrders.CountAsync(o =>
                o.SupplierId == id &&
                (o.Status == PurchaseStatus.Confirmed ||
                 o.Status == PurchaseStatus.PartiallyReceived));

            if (openCount > 0)
                return SupplierResult.Fail(
                    $"لا يمكن تعطيل «{supplier.Name}» ولديه {openCount} أمر بانتظار التوريد — " +
                    "أكمل استلامها أو ألغِها أولًا");
        }

        supplier.IsActive = !supplier.IsActive;
        supplier.UpdatedAt = DateTime.Now;

        var state = supplier.IsActive ? "تفعيل" : "تعطيل";
        _audit.Track(AuditActions.Update, nameof(Supplier), supplier.Id.ToString(),
            $"{state} المورد «{supplier.Name}»");
        await _db.SaveChangesAsync();

        return SupplierResult.Ok($"تم {state} المورد «{supplier.Name}»", supplier);
    }

    // ==================== أدوات ====================

    private static string Trim(string? value, int max)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length <= max ? v : v[..max];
    }

    private static string? TrimOrNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Trim(value, max);
    }
}

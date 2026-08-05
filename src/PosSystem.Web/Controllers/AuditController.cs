using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Controllers;

/// <summary>
/// سجل العمليات — للمدير فقط. يجيب على سؤال «من فعل ماذا ومتى ومن أي جهاز».
///
/// ملاحظة على تسمية الوسائط: الوسيط يُسمّى <c>op</c> لا <c>action</c> عن قصد.
/// الاسم <c>action</c> مفتاح محجوز في توجيه ASP.NET Core، فتمريره عبر
/// <c>asp-route-action</c> في روابط الترقيم كان يُلغي اسم الأكشن الحقيقي
/// فتتعطّل أزرار الصفحات كليًا عند تفعيل التصفية.
/// </summary>
[Authorize(Roles = AppRoles.Admin)]
public class AuditController : Controller
{
    private const int PageSize = 50;
    private readonly AppDbContext _db;

    public AuditController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(
        string? op, string? q, DateTime? from, DateTime? to, int page = 1)
    {
        page = Math.Max(1, page);

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(op))
            query = query.Where(l => l.Action == op);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(l =>
                l.UserName.Contains(term) ||
                (l.Details != null && l.Details.Contains(term)) ||
                (l.EntityName != null && l.EntityName.Contains(term)) ||
                (l.EntityId != null && l.EntityId.Contains(term)));
        }

        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value.Date);

        // «إلى» شامل ليومه بالكامل: المدير يكتب 2026-08-03 ويقصد نهاية ذلك اليوم،
        // فلو قارنّا <= التاريخ فقط لأسقطنا كل عمليات اليوم المطلوب.
        if (to.HasValue)
            query = query.Where(l => l.CreatedAt < to.Value.Date.AddDays(1));

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        if (page > totalPages) page = totalPages;   // رقم صفحة مبالغ فيه => آخر صفحة لا قائمة فارغة

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id)   // فاصل حاسم: عمليتان في نفس الثانية تُرتَّبان بثبات
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        // قائمة التصفية = الأنواع المعروفة + أي نوع موجود فعلًا في القاعدة.
        // الاعتماد على DISTINCT وحده يُخفي نوعًا لم يُسجَّل بعد فيظن المدير أنه غير مدعوم.
        var known = await _db.AuditLogs.AsNoTracking()
            .Select(l => l.Action).Distinct().ToListAsync();

        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Op = op;
        ViewBag.Q = q;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Total = total;
        ViewBag.Actions = AuditActions.All.Union(known).OrderBy(a => a).ToList();

        return View(logs);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// المرتجعات — المربع الثالث في شاشة المندوب.
///
/// المسار الكامل الذي يمشي فيه المستخدم:
///   1. Index  → يبحث بالفاتورة أو برقم هاتف العميل.
///   2. Create → يختار الأصناف والكميات ويحدّد السبب.
///   3. Confirm→ يضغط «تم»، فتُسجَّل الحركة وتُعاد الكميات للمخزون.
///   4. Details→ إيصال المرتجع، قابل للطباعة.
///
/// العزل: المندوب لا يُرجع إلا فواتيره ولا يرى إلا مرتجعاته. مطبَّق داخل
/// <see cref="IReturnService"/> عبر restrictTo، فلا يمكن تجاوزه بتعديل الرابط.
///
/// الأدوار: البيع والإرجاع وجهان لعملٍ واحد، فمن لا يبيع لا يُرجع. أمين
/// المخزن مستثنى صراحةً — المرتجع يردّ نقودًا ويحرّك رصيدًا، و<c>[Authorize]</c>
/// المجرّدة كانت تفتحه لأي مستخدم مسجَّل.
/// </summary>
[Authorize(Roles = AppRoles.AdminOrAgent)]
public class ReturnsController : Controller
{
    private readonly IReturnService _returns;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReturnsController(IReturnService returns, UserManager<ApplicationUser> userManager)
    {
        _returns = returns;
        _userManager = userManager;
    }

    /// <summary>شاشة البحث عن الفاتورة المطلوب إرجاعها</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var vm = new ReturnSearchVm { Query = q };

        // لا نعرض قائمة عند فتح الصفحة بلا بحث: الشاشة تبدأ نظيفة بخانة بحث
        // واضحة، وهو ما يتوقعه من جاء ليُرجع فاتورة بعينها.
        if (!string.IsNullOrWhiteSpace(q))
        {
            vm.Searched = true;
            vm.Results = await _returns.SearchInvoicesAsync(q, RestrictTo(user));
        }

        return View(vm);
    }

    /// <summary>شاشة اختيار الأصناف والكميات المُرجَعة</summary>
    [HttpGet]
    public async Task<IActionResult> Create(int invoiceId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var vm = await _returns.BuildFormAsync(invoiceId, RestrictTo(user));
        if (vm is null) return NotFound();

        ViewBag.Reasons = ReturnReasons.All;
        return View(vm);
    }

    /// <summary>
    /// تنفيذ الإرجاع فعليًا — زر «تم».
    /// تُستقبل معرّفات أسطر الفاتورة والكميات فقط؛ كل الأسعار وقيمة المُرَد
    /// تُحسب على السيرفر من الفاتورة الأصلية، فلا يُتلاعب بها من المتصفح.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(CreateReturnRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (request is null || request.InvoiceId <= 0)
        {
            TempData["Error"] = "لم تُرسل بيانات المرتجع";
            return RedirectToAction(nameof(Index));
        }

        // إعادة التحقق من ملكية الفاتورة قبل أي كتابة — لا نعتمد على أن
        // المستخدم وصل من شاشة Create المحمية أصلًا.
        var form = await _returns.BuildFormAsync(request.InvoiceId, RestrictTo(user));
        if (form is null) return NotFound();

        var result = await _returns.CreateReturnAsync(request, user.Id, user.FullName);

        if (!result.Success)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Create), new { invoiceId = request.InvoiceId });
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = result.ReturnId });
    }

    /// <summary>قائمة المرتجعات المُسجَّلة</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? q, DateTime? from, DateTime? to, int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var vm = await _returns.ListAsync(q, from, to, page, RestrictTo(user));
        return View(vm);
    }

    /// <summary>إيصال المرتجع</summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var ret = await _returns.GetDetailsAsync(id, RestrictTo(user));
        if (ret is null) return NotFound();

        return View(ret);
    }

    /// <summary>المدير يرى الكل؛ المندوب يُقيَّد بمعرّفه</summary>
    private string? RestrictTo(ApplicationUser user) =>
        User.IsInRole(AppRoles.Admin) ? null : user.Id;
}

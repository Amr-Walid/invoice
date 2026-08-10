using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// واجهة نقطة البيع. متاحة للمندوب والمدير (المدير قد يحتاج البيع أيضًا).
/// </summary>
///
/// <para>
/// الأدوار مُقيَّدة صراحةً: <c>[Authorize]</c> المجرّدة كانت تعني «أي مستخدم
/// مسجَّل»، فكان أمين المخزن — وهو ليس بائعًا — يفتح الشاشة ويُصدر فاتورة
/// كاملة تُخصم من رصيد مخزنه. القائمة الجانبية لا تُظهر له الرابط، لكن إخفاء
/// الرابط ليس صلاحية: الطلب المباشر على <c>/Pos/Checkout</c> كان ينجح.
/// </para>
[Authorize(Roles = AppRoles.AdminOrAgent)]
public class PosController : Controller
{
    private readonly IPosService _pos;
    private readonly ICouponService _coupons;
    private readonly IReportService _reports;
    private readonly UserManager<ApplicationUser> _userManager;

    public PosController(
        IPosService pos,
        ICouponService coupons,
        IReportService reports,
        UserManager<ApplicationUser> userManager)
    {
        _pos = pos;
        _coupons = coupons;
        _reports = reports;
        _userManager = userManager;
    }

    /// <summary>الصفحة الرئيسية للمندوب — المربعان الكبيران</summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var vm = await _reports.GetAgentDashboardAsync(user.Id, user.FullName);
        return View(vm);
    }

    // ==================== نقاط الـ AJAX ====================

    /// <summary>البحث عن منتج بالباركود — يُستدعى عند كل مسح</summary>
    [HttpGet]
    public async Task<IActionResult> Lookup(string barcode)
    {
        var result = await _pos.LookupByBarcodeAsync(barcode);
        return Json(result);
    }

    /// <summary>التحقق من كود الخصم</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateCoupon([FromBody] CouponCodeDto dto)
    {
        var result = await _coupons.ValidateAsync(dto?.Code);
        return Json(result);
    }

    /// <summary>
    /// إتمام الفاتورة. يستقبل المنتجات والكميات فقط —
    /// كل الأسعار تُحسب على السيرفر من قاعدة البيانات.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout([FromBody] CreateInvoiceRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Json(CreateInvoiceResult.Fail("انتهت الجلسة، سجّل الدخول من جديد"));

        if (request is null) return Json(CreateInvoiceResult.Fail("لم يتم إرسال بيانات الفاتورة"));

        var result = await _pos.CreateInvoiceAsync(request, user.Id);
        return Json(result);
    }

    /// <summary>عرض/طباعة الفاتورة بمقاس الطابعة الحرارية 80mm</summary>
    [HttpGet]
    public async Task<IActionResult> Receipt(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        // المندوب لا يرى إلا فواتيره؛ المدير يرى الكل
        var restrictTo = User.IsInRole(AppRoles.Admin) ? null : user.Id;
        var invoice = await _reports.GetInvoiceDetailsAsync(id, restrictTo);

        if (invoice is null) return NotFound();
        return View(invoice);
    }

    public class CouponCodeDto
    {
        public string? Code { get; set; }
    }
}

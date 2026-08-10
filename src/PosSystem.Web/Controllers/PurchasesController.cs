using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// أوامر الشراء وأذون الاستلام.
///
/// <para><b>تقسيم الصلاحيات:</b></para>
/// <list type="bullet">
///   <item>الإنشاء والاعتماد والإلغاء: <b>المدير وحده</b> — التزام مالي
///   تجاه المورد، وليس قرار من يستلم البضاعة.</item>
///   <item>الاستلام: <b>المدير أو أمين المخزن</b> — أمين المخزن هو من يفتح
///   الكرتونة ويعدّ القطع، فحرمانه من تسجيل الاستلام يعني أن يسجّله شخص
///   لم يره فيضيع معنى «من استلم».</item>
///   <item>القراءة: المدير وأمين المخزن. المندوب لا يرى المشتريات: تكلفة
///   الشراء معلومة مالية لا تخصّه، ولا يفعل بها شيئًا في عمله.</item>
/// </list>
///
/// <para><b>أمين المخزن يستلم لمخزنه وحده.</b> الأمر يحمل مخزنه المستقبِل،
/// ومن ليس مسؤولًا عنه لا يسجّل استلامه — وإلا دخلت بضاعة لمخزن على يد من
/// لا يعرف واقعه.</para>
/// </summary>
[Authorize(Roles = AppRoles.AdminOrKeeper)]
public class PurchasesController : Controller
{
    private const int PageSize = 20;

    private readonly AppDbContext _db;
    private readonly IPurchaseService _purchases;
    private readonly ISupplierService _suppliers;
    private readonly IWarehouseService _warehouses;
    private readonly ICurrentUserService _currentUser;

    public PurchasesController(AppDbContext db, IPurchaseService purchases,
        ISupplierService suppliers, IWarehouseService warehouses,
        ICurrentUserService currentUser)
    {
        _db = db;
        _purchases = purchases;
        _suppliers = suppliers;
        _warehouses = warehouses;
        _currentUser = currentUser;
    }

    // ==================================================================
    //  القائمة
    // ==================================================================

    [HttpGet]
    public async Task<IActionResult> Index(string? q, int? supplierId, int? warehouseId,
        PurchaseStatus? status, DateTime? from, DateTime? to, int page = 1)
    {
        // التاريخ «إلى» يُعامل شاملًا لليوم كله: من يكتب 2026-08-04 يقصد
        // نهاية ذلك اليوم لا بدايته، وإلا اختفت أوامر اليوم الأخير
        var toExclusive = to?.Date.AddDays(1);

        // القيد بالمخزن هنا كما في Open: كانت هذه الشاشة تعرض لأمين المخزن
        // أوامر المخازن كلها، فيرى مشتريات فروع ليست شغله بمجرد تعديل الرابط.
        var effectiveWarehouse = await ResolveWarehouseFilterAsync(warehouseId);

        var vm = await _purchases.ListAsync(q, supplierId, effectiveWarehouse, status,
            from?.Date, toExclusive, page, PageSize);

        await FillFiltersAsync(supplierId, effectiveWarehouse);
        ViewBag.CanManage = _currentUser.IsAdmin;
        ViewBag.CanSeeCost = _currentUser.IsAdmin;
        ViewBag.LockedWarehouse = effectiveWarehouse.HasValue && !_currentUser.IsAdmin;
        return View(vm);
    }

    /// <summary>
    /// الأوامر المفتوحة وحدها — شاشة العمل اليومي لأمين المخزن: ما ينتظر
    /// توريدًا الآن، مرتّبًا، بلا ضجيج الأوامر المكتملة والملغاة.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Open(int? warehouseId, int page = 1)
    {
        // أمين المخزن يرى أوامر مخزنه: بقية المخازن ليست شغله
        var effectiveWarehouse = await ResolveWarehouseFilterAsync(warehouseId);

        var vm = await _purchases.ListAsync(null, null, effectiveWarehouse,
            null, null, null, page, PageSize);

        vm.Orders = vm.Orders
            .Where(o => o.Status == PurchaseStatus.Confirmed ||
                        o.Status == PurchaseStatus.PartiallyReceived)
            .ToList();

        await FillFiltersAsync(null, effectiveWarehouse);
        ViewBag.CanManage = _currentUser.IsAdmin;
        ViewBag.CanSeeCost = _currentUser.IsAdmin;
        ViewBag.LockedWarehouse = effectiveWarehouse.HasValue && !_currentUser.IsAdmin;
        return View(vm);
    }

    // ==================================================================
    //  التفاصيل
    // ==================================================================

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var vm = await _purchases.GetDetailsAsync(id);
        if (vm is null) return NotFound();

        vm.CanManage = _currentUser.IsAdmin && vm.Order.CanEdit;
        vm.CanReceive = vm.Order.CanReceive && await CanReceiveOrderAsync(vm.Order);

        ViewBag.IsAdmin = _currentUser.IsAdmin;

        // التكلفة معلومة تعاقدية بين المدير والمورد: أمين المخزن يعدّ القطع
        // ويوقّع الاستلام، ولا شغل له بالسعر — وإظهاره له تسريبٌ لهامش الشراء
        // لمن لا يحتاجه. (نفس القرار الذي بُني عليه غياب تقارير الربح.)
        ViewBag.CanSeeCost = _currentUser.IsAdmin;
        ViewBag.CanCancel = _currentUser.IsAdmin && vm.Order.CanCancel;
        return View(vm);
    }

    /// <summary>إذن استلام للطباعة — الورقة التي تُوقَّع وتُحفظ مع فاتورة المورد</summary>
    [HttpGet]
    public async Task<IActionResult> Receipt(int id)
    {
        var receipt = await _purchases.GetReceiptAsync(id);
        if (receipt is null) return NotFound();
        return View(receipt);
    }

    [HttpGet]
    public async Task<IActionResult> Receipts(int? warehouseId, DateTime? from, DateTime? to)
    {
        var effectiveWarehouse = await ResolveWarehouseFilterAsync(warehouseId);

        var receipts = await _purchases.ListReceiptsAsync(
            effectiveWarehouse, from?.Date, to?.Date.AddDays(1));

        await FillFiltersAsync(null, effectiveWarehouse);
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.LockedWarehouse = effectiveWarehouse.HasValue && !_currentUser.IsAdmin;
        return View(receipts);
    }

    // ==================================================================
    //  إنشاء أمر شراء — المدير وحده
    // ==================================================================

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Create(int? warehouseId, string? search, bool lowStockOnly = false)
    {
        var target = warehouseId ?? (await _warehouses.GetDefaultAsync())?.Id ?? 0;
        if (target == 0)
        {
            TempData["Warning"] = "لا يوجد مخزن نشط — أضف مخزنًا أولًا قبل الشراء";
            return RedirectToAction("Index", "Warehouses");
        }

        await FillCreateAsync(target, search, lowStockOnly);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Create(int supplierId, int warehouseId,
        decimal discountPercentage, DateTime? expectedDate, string? notes,
        List<int> productIds, List<int> quantities, List<decimal> unitCosts,
        bool confirmImmediately = false, string? search = null, bool lowStockOnly = false)
    {
        var lines = BuildLines(productIds, quantities, unitCosts);
        if (lines is null)
        {
            TempData["Error"] = "بيانات الأصناف غير متسقة — أعد المحاولة";
            return RedirectToAction(nameof(Create), new { warehouseId });
        }

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            DiscountPercentage = discountPercentage,
            ExpectedDate = expectedDate,
            Notes = notes,
            Lines = lines,
            ConfirmImmediately = confirmImmediately
        };

        var result = await _purchases.CreateAsync(request,
            _currentUser.UserId ?? string.Empty, _currentUser.DisplayName);

        if (!result.Success)
        {
            // نُعيد الشاشة لا نحوّل: المستخدم أدخل عشرة أسطر ولن يُعيدها
            TempData["Error"] = result.Message;
            await FillCreateAsync(warehouseId, search, lowStockOnly);
            ViewBag.SelectedSupplierId = supplierId;
            ViewBag.DiscountPercentage = discountPercentage;
            ViewBag.ExpectedDate = expectedDate;
            ViewBag.Notes = notes;
            return View();
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = result.Order!.Id });
    }

    // ==================================================================
    //  تعديل المسودة — المدير وحده
    // ==================================================================

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(int id, string? search, bool lowStockOnly = false)
    {
        var order = await _purchases.GetAsync(id);
        if (order is null) return NotFound();

        if (!order.CanEdit)
        {
            TempData["Warning"] =
                $"لا يمكن تعديل أمر حالته «{PurchaseStatusLabel.Of(order.Status)}» — " +
                "المسودة وحدها قابلة للتعديل";
            return RedirectToAction(nameof(Details), new { id });
        }

        await FillCreateAsync(order.WarehouseId, search, lowStockOnly);
        ViewBag.SelectedSupplierId = order.SupplierId;
        ViewBag.DiscountPercentage = order.DiscountPercentage;
        ViewBag.ExpectedDate = order.ExpectedDate;
        ViewBag.Notes = order.Notes;
        ViewBag.EditOrder = order;

        // نفس شاشة الإنشاء: تعمل بوضعين حسب ViewBag.EditOrder — فلا تُكرَّر
        // 300 سطر من بناء السطور والحسابات في ملف ثانٍ يتباعد عنها بعد أول تعديل
        return View("Create", order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(int id, int supplierId, int warehouseId,
        decimal discountPercentage, DateTime? expectedDate, string? notes,
        List<int> productIds, List<int> quantities, List<decimal> unitCosts,
        bool confirmImmediately = false)
    {
        var lines = BuildLines(productIds, quantities, unitCosts);
        if (lines is null)
        {
            TempData["Error"] = "بيانات الأصناف غير متسقة — أعد المحاولة";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            DiscountPercentage = discountPercentage,
            ExpectedDate = expectedDate,
            Notes = notes,
            Lines = lines,
            ConfirmImmediately = confirmImmediately
        };

        var result = await _purchases.UpdateAsync(id, request,
            _currentUser.UserId ?? string.Empty, _currentUser.DisplayName);

        if (!result.Success)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Edit), new { id });
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ==================================================================
    //  الاعتماد — المدير وحده
    // ==================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Confirm(int id)
    {
        var result = await _purchases.ConfirmAsync(id,
            _currentUser.UserId ?? string.Empty, _currentUser.DisplayName);

        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Warning"] = result.Message;

        return RedirectToAction(nameof(Details), new { id });
    }

    // ==================================================================
    //  الاستلام — المدير أو أمين المخزن المسؤول
    // ==================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id, List<int> itemIds,
        List<int> quantities, string? notes)
    {
        var order = await _purchases.GetAsync(id);
        if (order is null) return NotFound();

        if (!await CanReceiveOrderAsync(order))
        {
            TempData["Warning"] =
                $"لا يمكنك تسجيل استلام لمخزن «{order.Warehouse?.Name}» — " +
                "أمين المخزن يستلم لمخزنه وحده";
            return RedirectToAction(nameof(Details), new { id });
        }

        // المفتاح معرّف سطر الأمر لا المنتج: الصنف قد يتكرر في سطرين
        // بتكلفتين، فالخريطة بالمنتج تُسند الكمية لسطر خطأ
        var map = BuildQuantityMap(itemIds, quantities);
        if (map is null)
        {
            TempData["Error"] = "بيانات الكميات غير متسقة — أعد المحاولة";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _purchases.ReceiveAsync(id, map, notes,
            _currentUser.UserId ?? string.Empty, _currentUser.DisplayName);

        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Warning"] = result.Message;

        return RedirectToAction(nameof(Details), new { id });
    }

    // ==================================================================
    //  الإلغاء والحذف — المدير وحده
    // ==================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _purchases.CancelAsync(id, reason,
            _currentUser.UserId ?? string.Empty, _currentUser.DisplayName);

        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Warning"] = result.Message;

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _purchases.DeleteDraftAsync(id,
            _currentUser.UserId ?? string.Empty, _currentUser.DisplayName);

        if (result.Success)
        {
            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData["Warning"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ==================================================================
    //  تقرير المشتريات — المدير وحده (معلومة مالية)
    // ==================================================================

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Report(DateTime? from, DateTime? to, int? warehouseId)
    {
        // الافتراض: الشهر الحالي — أكثر فترة تُطلب، فلا تُفتح الشاشة فارغة
        var start = from?.Date ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var end = to?.Date.AddDays(1) ?? DateTime.Today.AddDays(1);

        var vm = await _purchases.BuildReportAsync(start, end, warehouseId);
        vm.From = start;
        vm.To = to?.Date ?? DateTime.Today;

        await FillFiltersAsync(null, warehouseId);
        return View(vm);
    }

    // ==================================================================
    //  أدوات
    // ==================================================================

    /// <summary>
    /// أمين المخزن يُقيَّد بمخزنه: تمرير معرّف آخر في الرابط لا يُغيِّر شيئًا.
    /// المدير يرى الكل ويختار بحرية.
    /// </summary>
    private async Task<int?> ResolveWarehouseFilterAsync(int? requested)
    {
        if (_currentUser.IsAdmin) return requested;

        var mine = await MyWarehouseIdAsync();
        return mine ?? requested;   // ⭐ من المستخدم لا من الرابط
    }

    /// <summary>
    /// من يحقّ له تسجيل استلام هذا الأمر: المدير دائمًا، وأمين المخزن إن
    /// كان الأمر موجَّهًا لمخزنه. غير ذلك مرفوض حتى لو وصل الطلب مباشرة
    /// بتزوير المعرّف في النموذج.
    /// </summary>
    private async Task<bool> CanReceiveOrderAsync(PurchaseOrder order)
    {
        if (_currentUser.IsAdmin) return true;
        if (!_currentUser.IsWarehouseKeeper) return false;

        var mine = await MyWarehouseIdAsync();
        return mine is not null && mine.Value == order.WarehouseId;
    }

    /// <summary>
    /// مخزن المستخدم يُقرأ من قاعدة البيانات لا من مطالبات التذكرة: المطالبة
    /// تُبنى عند تسجيل الدخول، فنقل أمين مخزن إلى مخزن آخر يترك تذكرته قديمة
    /// فيستلم لمخزن لم يعد مسؤولًا عنه. نفس الاختيار في TransfersController.
    /// </summary>
    private async Task<int?> MyWarehouseIdAsync()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;

        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();
    }

    private async Task FillFiltersAsync(int? supplierId, int? warehouseId)
    {
        ViewBag.Suppliers = await _suppliers.ListActiveAsync();
        ViewBag.Warehouses = await _warehouses.ListActiveAsync();
        ViewBag.SelectedSupplierId = supplierId;
        ViewBag.SelectedWarehouseId = warehouseId;
    }

    private async Task FillCreateAsync(int warehouseId, string? search, bool lowStockOnly)
    {
        ViewBag.Suppliers = await _suppliers.ListActiveAsync();
        ViewBag.Warehouses = await _warehouses.ListActiveAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.Warehouse = await _warehouses.GetAsync(warehouseId);
        ViewBag.Search = search;
        ViewBag.LowStockOnly = lowStockOnly;
        ViewBag.Products = await _purchases.GetProductRowsAsync(
            warehouseId, search, lowStockOnly);
    }

    /// <summary>
    /// يبني سطور الأمر من ثلاث مصفوفات متوازية. يعيد <c>null</c> عند اختلاف
    /// الأطوال: الدمج وقتها يُسند كمية أو تكلفة لصنف خطأ، وأمر شراء بتكلفة
    /// صنف آخر خطأ مالي لا يُكتشف إلا بعد الدفع.
    /// </summary>
    private static List<PurchaseLineInput>? BuildLines(
        List<int> productIds, List<int> quantities, List<decimal> unitCosts)
    {
        if (productIds is null || quantities is null || unitCosts is null)
            return new List<PurchaseLineInput>();

        if (productIds.Count != quantities.Count || productIds.Count != unitCosts.Count)
            return null;

        var lines = new List<PurchaseLineInput>();
        for (var i = 0; i < productIds.Count; i++)
        {
            if (productIds[i] <= 0 || quantities[i] <= 0) continue;

            lines.Add(new PurchaseLineInput
            {
                ProductId = productIds[i],
                Quantity = quantities[i],
                UnitCost = unitCosts[i]
            });
        }

        return lines;
    }

    /// <summary>
    /// خريطة «معرّف سطر الأمر ← الكمية». الأطوال المختلفة ترفض العملية
    /// بدلًا من تخصيص كمية لسطر غير المقصود.
    /// </summary>
    private static Dictionary<int, int>? BuildQuantityMap(List<int> ids, List<int> quantities)
    {
        var map = new Dictionary<int, int>();
        if (ids is null || quantities is null) return map;
        if (ids.Count != quantities.Count) return null;

        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] <= 0) continue;

            // الجمع لا الاستبدال: صنف مُدخل في صفَّين يُجمع لا يُلغى أحدهما
            map[ids[i]] = map.TryGetValue(ids[i], out var existing)
                ? existing + quantities[i]
                : quantities[i];
        }

        return map;
    }
}

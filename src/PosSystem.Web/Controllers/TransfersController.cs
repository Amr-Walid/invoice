using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

/// <summary>
/// التحويلات بين المخازن.
///
/// <para>
/// <b>من يفعل ماذا:</b> الطلب حقّ لكل من يعمل في مخزن — المندوب الذي نفدت
/// منه قطعة هو أول من يكتشف الحاجة، ومنعُه من الطلب يعني أن ينتظر مرور
/// أمين المخزن. أما القرار (اعتماد/رفض/شحن) فمِلك <b>المخزن المصدر</b>:
/// لا أحد يسحب من مخزن غيره. والاستلام مِلك <b>المخزن الطالب</b>.
/// </para>
///
/// <para>
/// <b>المخزن العامل:</b> كل إجراء يُنفَّذ باسم مخزن المستخدم لا باسم مخزن
/// يُرسله المتصفح. المدير وحده — وهو غير مرتبط بمخزن — يختار المخزن الذي
/// يعمل بالنيابة عنه، وذلك عبر <c>?warehouseId=</c>.
/// </para>
/// </summary>
[Authorize]
public class TransfersController : Controller
{
    private readonly AppDbContext _db;
    private readonly ITransferService _transfers;
    private readonly IWarehouseService _warehouses;
    private readonly ICurrentUserService _currentUser;

    public TransfersController(AppDbContext db, ITransferService transfers,
        IWarehouseService warehouses, ICurrentUserService currentUser)
    {
        _db = db;
        _transfers = transfers;
        _warehouses = warehouses;
        _currentUser = currentUser;
    }

    // ==================== طلباتي (المخزن الطالب) ====================

    /// <summary>الطلبات التي أنشأها مخزني من مخازن أخرى</summary>
    public async Task<IActionResult> Index(int? warehouseId, TransferStatus? status)
    {
        var ctx = await ResolveAsync(warehouseId);
        if (ctx.Error is not null) return ctx.Error;

        var list = await _transfers.ListOutgoingAsync(ctx.WarehouseId, status);

        await FillContextAsync(ctx, status);
        ViewBag.PendingIncoming = await _transfers.CountPendingApprovalAsync(ctx.WarehouseId);
        return View(list);
    }

    // ==================== طلبات واردة للاعتماد (المخزن المصدر) ====================

    /// <summary>
    /// طلبات مخازن أخرى موجّهة لمخزني. متاحة للمدير وأمين المخزن فقط:
    /// من يقرر السحب من المخزن يجب أن يكون مسؤولًا عنه.
    /// </summary>
    [Authorize(Roles = AppRoles.AdminOrKeeper)]
    public async Task<IActionResult> Incoming(int? warehouseId, TransferStatus? status)
    {
        var ctx = await ResolveAsync(warehouseId);
        if (ctx.Error is not null) return ctx.Error;

        var list = await _transfers.ListIncomingAsync(ctx.WarehouseId, status);

        await FillContextAsync(ctx, status);
        return View(list);
    }

    // ==================== شاشة «اطلب من مخزن آخر» ====================

    public async Task<IActionResult> Create(int? warehouseId, string? search)
    {
        var ctx = await ResolveAsync(warehouseId);
        if (ctx.Error is not null) return ctx.Error;

        var rows = await _transfers.GetCandidatesAsync(ctx.WarehouseId, search);

        await FillContextAsync(ctx, null);
        ViewBag.Search = search;

        // المخازن الأخرى المفعّلة — لقائمة «اطلب من»
        ViewBag.OtherWarehouses = (await _warehouses.ListActiveAsync())
            .Where(w => w.Id != ctx.WarehouseId)
            .ToList();

        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int? warehouseId, int fromWarehouseId,
        string? notes, List<int> productIds, List<int> quantities)
    {
        var ctx = await ResolveAsync(warehouseId);
        if (ctx.Error is not null) return ctx.Error;

        // الكميات تُرسل موازية للمعرّفات؛ اختلاف الطولين يعني نموذجًا مُشوَّهًا
        // ودمجهما وقتها يربط كمية بصنف خطأ — نرفض بدلًا من التخمين.
        if (productIds.Count == 0 || productIds.Count != quantities.Count)
        {
            TempData["Error"] = "حدّد صنفًا واحدًا على الأقل وكميته";
            return RedirectToAction(nameof(Create), RouteFor(ctx));
        }

        var items = productIds
            .Select((pid, i) => new TransferLineRequest { ProductId = pid, Quantity = quantities[i] })
            .Where(x => x.Quantity > 0)
            .ToList();

        if (items.Count == 0)
        {
            TempData["Error"] = "كل الكميات صفر — لا يوجد ما يُطلب";
            return RedirectToAction(nameof(Create), RouteFor(ctx));
        }

        var result = await _transfers.CreateAsync(new CreateTransferRequest
        {
            FromWarehouseId = fromWarehouseId,
            ToWarehouseId = ctx.WarehouseId,   // ⭐ من المستخدم لا من النموذج
            Notes = notes,
            Items = items
        }, ctx.UserId, _currentUser.DisplayName);

        if (!result.Success)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Create), RouteFor(ctx));
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = result.TransferId });
    }

    // ==================== التفاصيل ====================

    public async Task<IActionResult> Details(int id)
    {
        var transfer = await _transfers.GetAsync(id);
        if (transfer is null) return NotFound();

        var access = await AccessAsync(transfer);
        if (!access.CanView) return Forbid();

        ViewBag.Access = access;
        return View(transfer);
    }

    // ==================== الاعتماد / الرفض / الشحن (المصدر) ====================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.AdminOrKeeper)]
    public async Task<IActionResult> Approve(int id)
    {
        var transfer = await _transfers.GetAsync(id);
        if (transfer is null) return NotFound();

        var access = await AccessAsync(transfer);
        if (!access.IsSource) return Forbid();

        var result = await _transfers.ApproveAsync(id, access.UserId, _currentUser.DisplayName);
        Flash(result);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.AdminOrKeeper)]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var transfer = await _transfers.GetAsync(id);
        if (transfer is null) return NotFound();

        var access = await AccessAsync(transfer);
        if (!access.IsSource) return Forbid();

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "سبب الرفض مطلوب — الطالب يحتاج معرفة سبب رفض طلبه";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _transfers.RejectAsync(id, reason, access.UserId, _currentUser.DisplayName);
        Flash(result);
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// الشحن: الحدث الذي يخصم من المصدر فعلًا. الكميات قابلة للتعديل لأن
    /// المتاح قد يكون أقل من المطلوب — الشحن الجزئي أنفع من رفض الطلب كله.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.AdminOrKeeper)]
    public async Task<IActionResult> Ship(int id, List<int> productIds, List<int> quantities)
    {
        var transfer = await _transfers.GetAsync(id);
        if (transfer is null) return NotFound();

        var access = await AccessAsync(transfer);
        if (!access.IsSource) return Forbid();

        var map = BuildQuantityMap(productIds, quantities);
        if (map is null)
        {
            TempData["Error"] = "بيانات الكميات غير متطابقة، أعد المحاولة";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _transfers.ShipAsync(id, map, access.UserId, _currentUser.DisplayName);
        Flash(result);
        return RedirectToAction(nameof(Details), new { id });
    }

    // ==================== الاستلام / الإلغاء (الطالب) ====================

    /// <summary>
    /// الاستلام: الحدث الذي يضيف للمخزن الطالب. الكميات قابلة للتعديل لأن
    /// ما يصل قد يكون أقل من المشحون (نقص أو تلف) — والفرق يُسجَّل في التدقيق.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id, List<int> productIds, List<int> quantities)
    {
        var transfer = await _transfers.GetAsync(id);
        if (transfer is null) return NotFound();

        var access = await AccessAsync(transfer);
        if (!access.IsDestination) return Forbid();

        var map = BuildQuantityMap(productIds, quantities);
        if (map is null)
        {
            TempData["Error"] = "بيانات الكميات غير متطابقة، أعد المحاولة";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _transfers.ReceiveAsync(id, map, access.UserId, _currentUser.DisplayName);
        Flash(result);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var transfer = await _transfers.GetAsync(id);
        if (transfer is null) return NotFound();

        var access = await AccessAsync(transfer);
        if (!access.IsDestination) return Forbid();

        var result = await _transfers.CancelAsync(id, access.UserId, _currentUser.DisplayName);
        Flash(result);
        return RedirectToAction(nameof(Details), new { id });
    }

    // ==================== تقرير «في الطريق» ====================

    /// <summary>
    /// كل ما خرج من مخزن ولم يصل هدفه. تقرير رقابي: القطعة هنا ليست في أي
    /// مخزن، فطول بقاء الطلب في هذه القائمة إشارة إلى فقد أو إهمال استلام.
    /// </summary>
    [Authorize(Roles = AppRoles.AdminOrKeeper)]
    public async Task<IActionResult> InTransit()
    {
        var list = await _transfers.ListInTransitAsync();

        // للمدير: كل ما في الطريق. لأمين المخزن: ما يخصّ مخزنه فقط
        // (وارد إليه أو صادر منه) — بقية المخازن ليست مسؤوليته.
        if (!_currentUser.IsAdmin)
        {
            var myWarehouseId = await MyWarehouseIdAsync();
            if (myWarehouseId is null)
            {
                TempData["Error"] = "حسابك غير مرتبط بمخزن";
                return RedirectToAction("Index", "Home");
            }

            list = list
                .Where(t => t.FromWarehouseId == myWarehouseId || t.ToWarehouseId == myWarehouseId)
                .ToList();

            ViewBag.MyWarehouseId = myWarehouseId;
        }

        return View(list);
    }

    // ==================== السجل الكامل (المدير) ====================

    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> All(TransferStatus? status)
    {
        var list = await _transfers.ListAllAsync(status);
        ViewBag.Status = status;
        return View(list);
    }

    // ==================== أدوات داخلية ====================

    /// <summary>سياق التنفيذ: المخزن الذي يعمل المستخدم باسمه</summary>
    private sealed class WorkContext
    {
        public int WarehouseId { get; init; }
        public string UserId { get; init; } = string.Empty;
        public Warehouse Warehouse { get; init; } = null!;

        /// <summary>المدير غير مرتبط بمخزن، فله أن يبدّل المخزن العامل</summary>
        public bool CanSwitch { get; init; }

        public IActionResult? Error { get; init; }
    }

    /// <summary>
    /// يحدد المخزن العامل. مخزن المستخدم مُلزِم إن وُجد — تجاهل ذلك يعني
    /// أن أمين مخزن يستطيع الطلب أو الاستلام باسم مخزن ليس مخزنه.
    /// </summary>
    private async Task<WorkContext> ResolveAsync(int? requested)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return new WorkContext { Error = Challenge() };

        var mine = await MyWarehouseIdAsync();

        int targetId;
        var canSwitch = false;

        if (mine is not null)
        {
            targetId = mine.Value;   // ⭐ لا يُبدَّل ولو أُرسل غيره
        }
        else if (_currentUser.IsAdmin)
        {
            canSwitch = true;
            if (requested is not null)
            {
                targetId = requested.Value;
            }
            else
            {
                var def = await _warehouses.GetDefaultAsync();
                if (def is null)
                    return new WorkContext
                    {
                        Error = RedirectToActionWithError("لا يوجد مخزن افتراضي — أنشئ مخزنًا أولًا",
                            "Index", "Warehouses")
                    };
                targetId = def.Id;
            }
        }
        else
        {
            return new WorkContext
            {
                Error = RedirectToActionWithError(
                    "حسابك غير مرتبط بمخزن — راجع مدير النظام لربطه بمخزن قبل استخدام التحويلات",
                    "Index", "Home")
            };
        }

        var warehouse = await _warehouses.GetAsync(targetId);
        if (warehouse is null)
            return new WorkContext
            {
                Error = RedirectToActionWithError("المخزن غير موجود", "Index", "Warehouses")
            };

        return new WorkContext
        {
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            UserId = userId,
            CanSwitch = canSwitch
        };
    }

    private IActionResult RedirectToActionWithError(string message, string action, string controller)
    {
        TempData["Error"] = message;
        return RedirectToAction(action, controller);
    }

    private async Task<int?> MyWarehouseIdAsync()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;

        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();
    }

    private async Task FillContextAsync(WorkContext ctx, TransferStatus? status)
    {
        ViewBag.Warehouse = ctx.Warehouse;
        ViewBag.CanSwitch = ctx.CanSwitch;
        ViewBag.Status = status;

        if (ctx.CanSwitch)
            ViewBag.AllWarehouses = await _warehouses.ListActiveAsync();
    }

    private static object RouteFor(WorkContext ctx) =>
        ctx.CanSwitch ? new { warehouseId = ctx.WarehouseId } : new { };

    /// <summary>صلاحيات المستخدم على طلب تحويل بعينه — تُستخدم في الإجراءات والعرض</summary>
    public sealed class TransferAccess
    {
        public string UserId { get; init; } = string.Empty;

        /// <summary>ينتمي للمخزن المصدر (أو مدير) — يعتمد/يرفض/يشحن</summary>
        public bool IsSource { get; init; }

        /// <summary>ينتمي للمخزن الطالب (أو مدير) — يستلم/يلغي</summary>
        public bool IsDestination { get; init; }

        public bool CanView => IsSource || IsDestination;
    }

    private async Task<TransferAccess> AccessAsync(StockTransfer transfer)
    {
        var userId = _currentUser.UserId ?? string.Empty;

        // المدير يرى ويقرر في كل المخازن — هو المسؤول عن هيكلها كله.
        if (_currentUser.IsAdmin)
            return new TransferAccess { UserId = userId, IsSource = true, IsDestination = true };

        var mine = await MyWarehouseIdAsync();
        if (mine is null)
            return new TransferAccess { UserId = userId };

        // أمين المخزن وحده يقرر في الصادر من مخزنه؛ المندوب يستلم ويلغي فقط.
        var isKeeper = _currentUser.IsWarehouseKeeper;

        return new TransferAccess
        {
            UserId = userId,
            IsSource = isKeeper && transfer.FromWarehouseId == mine.Value,
            IsDestination = transfer.ToWarehouseId == mine.Value
        };
    }

    /// <summary>
    /// يبني خريطة ProductId → كمية من مصفوفتين متوازيتين.
    /// يُرجع null عند اختلاف الطولين: الدمج وقتها يُسند كمية لصنف خطأ.
    /// </summary>
    private static Dictionary<int, int>? BuildQuantityMap(List<int> productIds, List<int> quantities)
    {
        if (productIds.Count != quantities.Count) return null;

        var map = new Dictionary<int, int>();
        for (var i = 0; i < productIds.Count; i++)
        {
            var qty = quantities[i] < 0 ? 0 : quantities[i];
            // سطران لنفس الصنف: نجمع لا نستبدل، فالاستبدال يُسقط كمية صامتًا.
            map[productIds[i]] = map.TryGetValue(productIds[i], out var prev) ? prev + qty : qty;
        }

        return map;
    }

    private void Flash(TransferResult result)
    {
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;
    }
}

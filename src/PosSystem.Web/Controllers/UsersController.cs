using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;
using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Controllers;

/// <summary>إدارة المستخدمين (المناديب والمديرين) — للمدير فقط</summary>
[Authorize(Roles = AppRoles.Admin)]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;
    private readonly IWarehouseService _warehouses;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        IAuditService audit,
        IWarehouseService warehouses)
    {
        _userManager = userManager;
        _audit = audit;
        _warehouses = warehouses;
    }

    public async Task<IActionResult> Index()
    {
        // Include للمخزن: بدونه يحتاج كل صف استعلامًا منفصلاً لاسم مخزنه
        var users = await _userManager.Users.AsNoTracking()
            .Include(u => u.Warehouse)
            .OrderBy(u => u.FullName).ToListAsync();

        var rows = new List<UserRowVm>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            rows.Add(new UserRowVm
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                Role = roles.FirstOrDefault() ?? "-",
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                WarehouseId = u.WarehouseId,
                WarehouseName = u.Warehouse?.DisplayName
            });
        }

        await LoadWarehousesAsync();
        return View(rows);
    }

    public async Task<IActionResult> Create()
    {
        await LoadWarehousesAsync();
        return View(new CreateUserVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserVm model)
    {
        if (!ModelState.IsValid)
        {
            await LoadWarehousesAsync();
            return View(model);
        }

        if (await _userManager.FindByEmailAsync(model.Email.Trim()) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "هذا البريد مستخدم بالفعل");
            await LoadWarehousesAsync();
            return View(model);
        }

        // المخزن إلزامي لغير المدير. المندوب بلا مخزن لا يستطيع البيع إطلاقًا،
        // فالسماح بإنشائه هكذا يعني تسليم حساب معطّل عمليًا يظهر سليمًا في
        // القائمة — يُكتشف الخلل عند أول محاولة بيع لا عند الإنشاء.
        var needsWarehouse = model.Role != AppRoles.Admin;
        if (needsWarehouse && model.WarehouseId is null)
        {
            ModelState.AddModelError(nameof(model.WarehouseId),
                $"المخزن مطلوب لدور «{AppRoles.Label(model.Role)}» — " +
                "المستخدم بلا مخزن لا يستطيع البيع أو إدارة مخزون");
            await LoadWarehousesAsync();
            return View(model);
        }

        // المدير يرى كل المخازن، فربطه بمخزن واحد لا معنى له ويُضلِّل من يقرأ
        // القائمة لاحقًا. نُفرِّغ الحقل صراحة بدل الاعتماد على انتباه المستخدم.
        var warehouseId = needsWarehouse ? model.WarehouseId : null;

        if (warehouseId is not null)
        {
            var warehouse = await _warehouses.GetAsync(warehouseId.Value);
            if (warehouse is null || !warehouse.IsActive)
            {
                ModelState.AddModelError(nameof(model.WarehouseId),
                    "المخزن المختار غير موجود أو غير مفعّل");
                await LoadWarehousesAsync();
                return View(model);
            }
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true,
            FullName = model.FullName.Trim(),
            IsActive = true,
            WarehouseId = warehouseId
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            await LoadWarehousesAsync();
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, model.Role);
        await _audit.LogAsync(AuditActions.Create, nameof(ApplicationUser), user.Id,
            $"مستخدم جديد: {user.Email} — الدور {AppRoles.Label(model.Role)}" +
            (warehouseId is null ? " — كل المخازن" : $" — مخزن #{warehouseId}"));

        TempData["Success"] = $"تم إنشاء المستخدم «{user.FullName}» بنجاح";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// نقل مستخدم إلى مخزن آخر.
    ///
    /// <para>
    /// لا يُنقل أي مخزون مع المستخدم: النقل يغيّر «من أين يبيع» فقط. فواتيره
    /// السابقة تبقى منسوبة لمخزنها الأصلي لأن القطع خرجت منه فعلًا، ومرتجعاتها
    /// ترجع إليه لا إلى المخزن الجديد.
    /// </para>
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeWarehouse(string id, int? warehouseId)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        var isAdmin = roles.Contains(AppRoles.Admin);

        if (!isAdmin && warehouseId is null)
        {
            TempData["Error"] = "لا يمكن ترك مستخدم غير مدير بلا مخزن — لن يستطيع البيع";
            return RedirectToAction(nameof(Index));
        }

        if (warehouseId is not null)
        {
            var warehouse = await _warehouses.GetAsync(warehouseId.Value);
            if (warehouse is null)
            {
                TempData["Error"] = "المخزن غير موجود";
                return RedirectToAction(nameof(Index));
            }
            if (!warehouse.IsActive)
            {
                TempData["Error"] = $"المخزن «{warehouse.Name}» غير مفعّل";
                return RedirectToAction(nameof(Index));
            }
        }

        var oldWarehouseId = user.WarehouseId;
        if (oldWarehouseId == warehouseId)
        {
            TempData["Success"] = "لا تغيير — المستخدم مربوط بهذا المخزن بالفعل";
            return RedirectToAction(nameof(Index));
        }

        user.WarehouseId = warehouseId;
        await _userManager.UpdateAsync(user);

        await _audit.LogAsync(AuditActions.Update, nameof(ApplicationUser), user.Id,
            $"نقل «{user.FullName}» من مخزن {oldWarehouseId?.ToString() ?? "كل المخازن"} " +
            $"إلى {warehouseId?.ToString() ?? "كل المخازن"}");

        TempData["Success"] = $"تم تحديث مخزن «{user.FullName}»";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>قائمة المخازن المفعّلة للاختيار — تُستخدم في الإنشاء والنقل</summary>
    private async Task LoadWarehousesAsync()
    {
        var warehouses = await _warehouses.ListActiveAsync();
        ViewBag.Warehouses = new SelectList(
            warehouses.Select(w => new { w.Id, Label = w.DisplayName }),
            "Id", "Label");
        ViewBag.Roles = new SelectList(
            AppRoles.All.Select(r => new { Value = r, Label = AppRoles.Label(r) }),
            "Value", "Label");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        // منع المدير من تعطيل حسابه الشخصي
        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "لا يمكنك تعطيل حسابك الشخصي";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        await _audit.LogAsync(AuditActions.Update, nameof(ApplicationUser), user.Id,
            $"{(user.IsActive ? "تفعيل" : "تعطيل")} حساب: {user.Email}");

        TempData["Success"] = $"تم {(user.IsActive ? "تفعيل" : "تعطيل")} حساب «{user.FullName}»";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["Error"] = "كلمة المرور يجب أن تكون 6 أحرف على الأقل";
            return RedirectToAction(nameof(Index));
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        await _audit.LogAsync(AuditActions.Update, nameof(ApplicationUser), user.Id,
            $"إعادة تعيين كلمة مرور: {user.Email}");

        TempData["Success"] = $"تم تغيير كلمة مرور «{user.FullName}»";
        return RedirectToAction(nameof(Index));
    }

    // ==================== ViewModels ====================
    public class UserRowVm
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary><c>null</c> عند المدير وتعني «كل المخازن»</summary>
        public int? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }

        public string RoleLabel => AppRoles.Label(Role);

        /// <summary>
        /// مستخدم غير مدير بلا مخزن — حساب معطّل عمليًا يجب تمييزه في القائمة
        /// بدل انتظار شكوى «لا أستطيع البيع».
        /// </summary>
        public bool IsMissingWarehouse => Role != AppRoles.Admin && WarehouseId is null;
    }

    public class CreateUserVm
    {
        [Required(ErrorMessage = "الاسم الكامل مطلوب")]
        [Display(Name = "الاسم الكامل")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
        [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
        [Display(Name = "البريد الإلكتروني")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "كلمة المرور مطلوبة")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة المرور 6 أحرف على الأقل")]
        [DataType(DataType.Password)]
        [Display(Name = "كلمة المرور")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "الدور مطلوب")]
        [Display(Name = "الدور")]
        public string Role { get; set; } = AppRoles.Agent;

        /// <summary>
        /// إلزامي لكل دور غير المدير — يُتحقَّق منه في الإجراء لا بـ<c>[Required]</c>
        /// لأن الإلزام مشروط بالدور، والتحقق التصريحي لا يعرف قيمة حقل آخر.
        /// </summary>
        [Display(Name = "المخزن")]
        public int? WarehouseId { get; set; }
    }
}

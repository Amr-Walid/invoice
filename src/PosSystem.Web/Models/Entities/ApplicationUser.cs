using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

/// <summary>مستخدم النظام — توسعة لـ IdentityUser</summary>
public class ApplicationUser : IdentityUser
{
    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    [StringLength(150)]
    [Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// المخزن المربوط به المستخدم.
    ///
    /// <para>
    /// مندوب المبيعات يبيع من مخزنه فقط، وأمين المخزن يدير مخزنه.
    /// المدير يترك الحقل فارغًا (<c>null</c>) فيرى كل المخازن.
    /// </para>
    ///
    /// <para>
    /// مندوب بلا مخزن <b>يُمنع من البيع</b> برسالة واضحة؛ ولا يُسحب من
    /// المخزن الافتراضي بصمت، لأن السحب الصامت يعني بيعًا من مخزن لا
    /// يملكه وفروق جرد غامضة لا يمكن تفسيرها لاحقًا.
    /// </para>
    /// </summary>
    [Display(Name = "المخزن")]
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

/// <summary>أسماء الأدوار — ثوابت لتجنب الأخطاء الكتابية</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Agent = "Agent";

    /// <summary>
    /// أمين المخزن — يعتمد ويشحن التحويلات ويستلم المشتريات ويجرد،
    /// دون صلاحيات المدير الكاملة (لا مستخدمين، ولا أكواد خصم، ولا سجل عمليات).
    /// </summary>
    public const string WarehouseKeeper = "WarehouseKeeper";

    public static readonly string[] All = { Admin, Agent, WarehouseKeeper };

    /// <summary>الأسماء العربية للأدوار — تُعرض في الواجهة وسجل العمليات</summary>
    public static string Label(string role) => role switch
    {
        Admin => "مدير",
        Agent => "مندوب",
        WarehouseKeeper => "أمين مخزن",
        _ => role
    };

    /// <summary>الأدوار التي تدير المخازن — تُستخدم في سمات [Authorize]</summary>
    public const string AdminOrKeeper = Admin + "," + WarehouseKeeper;
}

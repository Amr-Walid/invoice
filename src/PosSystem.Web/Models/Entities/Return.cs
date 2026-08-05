using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

/// <summary>سبب المرتجع — يُختار من قائمة ثابتة ليصلح للتقارير</summary>
public enum ReturnReason
{
    [Display(Name = "عيب في المنتج")]
    Defective = 1,

    [Display(Name = "المنتج غير مطابق للمطلوب")]
    WrongItem = 2,

    [Display(Name = "العميل غيّر رأيه")]
    ChangedMind = 3,

    [Display(Name = "سبب آخر")]
    Other = 99
}

public static class ReturnReasons
{
    public static string Label(ReturnReason reason) => reason switch
    {
        ReturnReason.Defective => "عيب في المنتج",
        ReturnReason.WrongItem => "المنتج غير مطابق للمطلوب",
        ReturnReason.ChangedMind => "العميل غيّر رأيه",
        _ => "سبب آخر"
    };

    public static readonly ReturnReason[] All =
    {
        ReturnReason.Defective,
        ReturnReason.WrongItem,
        ReturnReason.ChangedMind,
        ReturnReason.Other
    };
}

/// <summary>
/// مرتجع — عملية إرجاع صنف أو أكثر من فاتورة موجودة.
///
/// مبادئ التصميم:
///  • المرتجع كيان مستقل ولا يُعدَّل على الفاتورة الأصلية أبدًا. الفاتورة
///    وثيقة تاريخية؛ المرتجع حركة معاكسة مرتبطة بها. هذا يجعل السجل قابلًا
///    للتدقيق ويمنع اختفاء المبيعات من التقارير القديمة.
///  • يدعم الإرجاع الجزئي: يمكن إرجاع 1 من 3 قطع، وباقي القطع يبقى مبيعًا.
///  • قيمة المرتجع تُحسب على السيرفر بنفس نسبة خصم الفاتورة، فلا يُرد
///    للعميل أكثر مما دفع فعلًا.
/// </summary>
public class Return
{
    public int Id { get; set; }

    /// <summary>الرقم المرجعي الفريد — RET-yyyyMMdd-0001</summary>
    [Display(Name = "رقم المرتجع")]
    [StringLength(30)]
    public string ReturnNumber { get; set; } = string.Empty;

    [Display(Name = "الفاتورة الأصلية")]
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    /// <summary>snapshot لرقم الفاتورة — يبقى مقروءًا في التقارير</summary>
    [StringLength(30)]
    [Display(Name = "رقم الفاتورة")]
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>المستخدم الذي نفّذ الإرجاع — «مين عملها»</summary>
    [Display(Name = "نفّذها")]
    [StringLength(450)]
    public string ProcessedByUserId { get; set; } = string.Empty;
    public ApplicationUser? ProcessedByUser { get; set; }

    /// <summary>snapshot لاسم المُنفِّذ — يبقى ظاهرًا لو حُذف المستخدم لاحقًا</summary>
    [StringLength(150)]
    [Display(Name = "المُنفِّذ")]
    public string ProcessedByName { get; set; } = string.Empty;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    [StringLength(150)]
    [Display(Name = "اسم العميل")]
    public string? CustomerName { get; set; }

    [StringLength(30)]
    [Display(Name = "رقم العميل")]
    public string? CustomerPhone { get; set; }

    [Display(Name = "سبب الإرجاع")]
    public ReturnReason Reason { get; set; } = ReturnReason.Defective;

    [StringLength(500, ErrorMessage = "الملاحظات لا تزيد عن 500 حرف")]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>مجموع أسعار الأصناف المُرجَعة قبل الخصم</summary>
    [Display(Name = "الإجمالي قبل الخصم")]
    public decimal SubTotal { get; set; }

    /// <summary>نسبة خصم الفاتورة الأصلية — snapshot</summary>
    [Display(Name = "نسبة الخصم %")]
    public decimal DiscountPercentage { get; set; }

    [Display(Name = "قيمة الخصم المُستقطعة")]
    public decimal DiscountAmount { get; set; }

    /// <summary>المبلغ المُرَد فعلًا للعميل = SubTotal - DiscountAmount</summary>
    [Display(Name = "المبلغ المُرَد")]
    public decimal RefundAmount { get; set; }

    /// <summary>هل أُعيدت الأصناف للمخزون؟ (المنتج التالف لا يُعاد للبيع)</summary>
    [Display(Name = "أُعيدت للمخزون")]
    public bool RestockedToInventory { get; set; } = true;

    /// <summary>
    /// المخزن الذي عادت إليه القطع — يُنسخ من مخزن الفاتورة الأصلية لا من
    /// مخزن المُنفِّذ. القطعة تعود من حيث خرجت، وإلا صار المرتجع تحويلًا
    /// مُقنَّعًا بين مخزنين بلا مستند تحويل ولا موافقة.
    /// </summary>
    [Display(Name = "المخزن")]
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    [Display(Name = "التاريخ والوقت")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();

    /// <summary>عدد القطع المُرجَعة</summary>
    public int TotalQuantity => Items?.Sum(i => i.Quantity) ?? 0;
}

/// <summary>سطر في المرتجع — يحفظ نسخة من بيانات المنتج وقت الإرجاع</summary>
public class ReturnItem
{
    public int Id { get; set; }

    public int ReturnId { get; set; }
    public Return? Return { get; set; }

    /// <summary>سطر الفاتورة الأصلي الذي جاء منه هذا الإرجاع</summary>
    public int InvoiceItemId { get; set; }
    public InvoiceItem? InvoiceItem { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    // ===== snapshot لبيانات المنتج =====
    [StringLength(200)]
    [Display(Name = "اسم المنتج")]
    public string ProductName { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "الباركود")]
    public string Barcode { get; set; } = string.Empty;

    /// <summary>سعر الوحدة من الفاتورة الأصلية — لا من المنتج الحالي</summary>
    [Display(Name = "سعر الوحدة")]
    public decimal UnitPrice { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "الكمية المُرجَعة يجب أن تكون 1 على الأقل")]
    [Display(Name = "الكمية المُرجَعة")]
    public int Quantity { get; set; }

    [Display(Name = "الإجمالي")]
    public decimal LineTotal { get; set; }
}

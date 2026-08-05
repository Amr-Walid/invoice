using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

public enum InvoiceStatus
{
    Completed = 1,
    Cancelled = 2,

    /// <summary>أُرجع بعض أصنافها وبقي الباقي مبيعًا</summary>
    PartiallyReturned = 3,

    /// <summary>أُرجعت كل أصنافها بالكامل</summary>
    FullyReturned = 4
}

/// <summary>
/// الفاتورة. تحتفظ بـ snapshot للأسعار ونسبة الخصم وقت البيع،
/// حتى لا تتأثر التقارير التاريخية بأي تعديل لاحق على المنتجات أو الكوبونات.
/// </summary>
public class Invoice
{
    public int Id { get; set; }

    /// <summary>الرقم المرجعي الفريد — INV-yyyyMMdd-0001</summary>
    [Display(Name = "رقم الفاتورة")]
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>الـ Agent الذي أصدر الفاتورة</summary>
    [Display(Name = "المندوب")]
    public string AgentId { get; set; } = string.Empty;
    public ApplicationUser? Agent { get; set; }

    /// <summary>
    /// المخزن الذي خرجت منه القطع. مخزَّن على الفاتورة لا مقروء من
    /// <c>Agent.WarehouseId</c> لحظة الحاجة، لسببين:
    ///  • نقل المندوب لمخزن آخر لاحقًا لا يجوز أن يُعيد كتابة تاريخ مبيعاته.
    ///  • المرتجع يجب أن يُرجِع القطع إلى المخزن الذي خرجت منه فعلًا،
    ///    لا إلى مخزن المندوب الحالي، وإلا تحرّك المخزون بين المخازن بلا تحويل.
    /// </summary>
    [Display(Name = "المخزن")]
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    /// <summary>
    /// العميل المرتبط. الربط يحدث تلقائيًا برقم الهاتف: إن كان الرقم
    /// موجودًا يُربط بالعميل نفسه، وإن كان جديدًا يُنشأ عميل جديد.
    /// </summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>snapshot لاسم العميل وقت البيع — يبقى كما كُتب في الفاتورة</summary>
    [StringLength(150)]
    [Display(Name = "اسم العميل")]
    public string? CustomerName { get; set; }

    [StringLength(30)]
    [Display(Name = "رقم العميل")]
    public string? CustomerPhone { get; set; }

    /// <summary>الإجمالي قبل الخصم</summary>
    [Display(Name = "الإجمالي الفرعي")]
    public decimal SubTotal { get; set; }

    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    /// <summary>snapshot لكود الخصم</summary>
    [StringLength(50)]
    [Display(Name = "كود الخصم")]
    public string? CouponCode { get; set; }

    /// <summary>snapshot لنسبة الخصم</summary>
    [Display(Name = "نسبة الخصم %")]
    public decimal DiscountPercentage { get; set; }

    [Display(Name = "قيمة الخصم")]
    public decimal DiscountAmount { get; set; }

    /// <summary>الصافي = SubTotal - DiscountAmount</summary>
    [Display(Name = "الإجمالي الكلي")]
    public decimal Total { get; set; }

    // ملاحظة: أُزيل TotalCost و Profit بطلب صريح — التقارير تركّز على
    // المبيعات والخصومات لا على التكلفة والأرباح.

    [Display(Name = "الحالة")]
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Completed;

    /// <summary>
    /// إجمالي ما رُدّ للعميل من هذه الفاتورة (تراكمي عبر عدة مرتجعات).
    /// مخزَّن لا محسوب، حتى تعمل التقارير باستعلام واحد بلا Join ثقيل.
    /// </summary>
    [Display(Name = "إجمالي المُرَد")]
    public decimal RefundedAmount { get; set; }

    [Display(Name = "التاريخ والوقت")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
    public ICollection<Return> Returns { get; set; } = new List<Return>();

    /// <summary>عدد القطع الكلي في الفاتورة</summary>
    public int TotalQuantity => Items?.Sum(i => i.Quantity) ?? 0;

    /// <summary>عدد القطع المُرجَعة من هذه الفاتورة</summary>
    public int ReturnedQuantity => Items?.Sum(i => i.ReturnedQuantity) ?? 0;

    /// <summary>الصافي بعد خصم المرتجعات — هذا ما دخل الخزنة فعلًا</summary>
    [Display(Name = "الصافي بعد المرتجعات")]
    public decimal NetAfterReturns => Total - RefundedAmount;

    /// <summary>هل يمكن إرجاع أي شيء آخر من هذه الفاتورة؟</summary>
    public bool HasReturnableItems =>
        Status != InvoiceStatus.Cancelled &&
        (Items?.Any(i => i.RemainingQuantity > 0) ?? false);

    public string StatusLabel => Status switch
    {
        InvoiceStatus.Completed => "مكتملة",
        InvoiceStatus.Cancelled => "ملغاة",
        InvoiceStatus.PartiallyReturned => "مرتجع جزئي",
        InvoiceStatus.FullyReturned => "مرتجعة بالكامل",
        _ => "غير معروف"
    };

    public string StatusBadgeClass => Status switch
    {
        InvoiceStatus.Completed => "badge-soft-green",
        InvoiceStatus.Cancelled => "badge-soft-red",
        InvoiceStatus.PartiallyReturned => "badge-soft-orange",
        InvoiceStatus.FullyReturned => "badge-soft-red",
        _ => "badge-soft-gray"
    };
}

/// <summary>سطر في الفاتورة — يحفظ نسخة من بيانات المنتج وقت البيع</summary>
public class InvoiceItem
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    // ===== snapshot لبيانات المنتج وقت البيع =====
    [StringLength(200)]
    [Display(Name = "اسم المنتج")]
    public string ProductName { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "الباركود")]
    public string Barcode { get; set; } = string.Empty;

    [Display(Name = "سعر الوحدة")]
    public decimal UnitPrice { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "الكمية يجب أن تكون 1 على الأقل")]
    [Display(Name = "الكمية")]
    public int Quantity { get; set; }

    /// <summary>UnitPrice × Quantity</summary>
    [Display(Name = "الإجمالي")]
    public decimal LineTotal { get; set; }

    /// <summary>
    /// الكمية المُرجَعة من هذا السطر. مخزَّنة على السطر نفسه لأن التحقق
    /// «هل تبقّى ما يمكن إرجاعه؟» يجري عند كل عملية إرجاع، ولا يصح أن
    /// يتطلب جمع كل المرتجعات السابقة في كل مرة.
    /// </summary>
    [Display(Name = "الكمية المُرجَعة")]
    public int ReturnedQuantity { get; set; }

    public ICollection<ReturnItem> ReturnItems { get; set; } = new List<ReturnItem>();

    /// <summary>ما تبقّى قابلًا للإرجاع من هذا السطر</summary>
    public int RemainingQuantity => Quantity - ReturnedQuantity;
}

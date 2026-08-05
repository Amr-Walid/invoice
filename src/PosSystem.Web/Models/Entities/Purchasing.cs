using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

// ============================================================
// المورد
// ============================================================

/// <summary>
/// مورد البضاعة.
///
/// <para>
/// الهاتف هو المُعرِّف الحقيقي لا الاسم: الأسماء تُكتب بصيغ مختلفة
/// («محمد للتجارة» / «شركة محمد») فينشأ موردان لجهة واحدة وتتفرّق حركاتها.
/// لذا نُطبّع الرقم ونضع عليه الفهرس الفريد — نفس منطق العملاء تمامًا،
/// ونُعيد استخدام <see cref="PhoneHelper"/> بلا منطق ثانٍ يختلف عنه.
/// </para>
/// </summary>
public class Supplier
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم المورد مطلوب")]
    [StringLength(150, ErrorMessage = "اسم المورد لا يزيد عن 150 حرف")]
    [Display(Name = "اسم المورد")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [StringLength(30, ErrorMessage = "رقم الهاتف لا يزيد عن 30 حرف")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    /// <summary>الرقم بعد التوحيد — عليه الفهرس الفريد، ولا يُحرَّر يدويًا</summary>
    [StringLength(30)]
    public string PhoneNormalized { get; set; } = string.Empty;

    [StringLength(150, ErrorMessage = "البريد لا يزيد عن 150 حرف")]
    [EmailAddress(ErrorMessage = "صيغة البريد غير صحيحة")]
    [Display(Name = "البريد الإلكتروني")]
    public string? Email { get; set; }

    [StringLength(200, ErrorMessage = "العنوان لا يزيد عن 200 حرف")]
    [Display(Name = "العنوان")]
    public string? Address { get; set; }

    /// <summary>مسؤول التواصل عند المورد — يُختصر وقت المتابعة</summary>
    [StringLength(150)]
    [Display(Name = "مسؤول التواصل")]
    public string? ContactPerson { get; set; }

    [StringLength(500, ErrorMessage = "الملاحظات لا تزيد عن 500 حرف")]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "تاريخ الإضافة")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}

// ============================================================
// حالة أمر الشراء
// ============================================================

/// <summary>
/// حالة أمر الشراء.
///
/// <para>
/// القيم لا تُعاد ترقيمها بعد النشر لأنها محفوظة في قاعدة البيانات،
/// والترتيب الرقمي يعكس تقدّم الأمر زمنيًا فيصحّ الترتيب والمقارنة به.
/// </para>
/// </summary>
public enum PurchaseStatus
{
    /// <summary>مسودة — قابلة للتعديل والحذف، لم تُرسَل للمورد بعد</summary>
    Draft = 1,

    /// <summary>مُعتمد ومُرسَل للمورد — لا يُعدَّل، وينتظر التوريد</summary>
    Confirmed = 2,

    /// <summary>وصلت دفعة ولم يكتمل الأمر</summary>
    PartiallyReceived = 3,

    /// <summary>استُلم بالكامل</summary>
    Received = 4,

    /// <summary>أُلغي</summary>
    Cancelled = 5
}

public static class PurchaseStatusLabel
{
    public static string Of(PurchaseStatus s) => s switch
    {
        PurchaseStatus.Draft => "مسودة",
        PurchaseStatus.Confirmed => "مُعتمد — بانتظار التوريد",
        PurchaseStatus.PartiallyReceived => "استلام جزئي",
        PurchaseStatus.Received => "مستلم بالكامل",
        PurchaseStatus.Cancelled => "ملغي",
        _ => s.ToString()
    };

    public static string Badge(PurchaseStatus s) => s switch
    {
        PurchaseStatus.Draft => "badge-soft-gray",
        PurchaseStatus.Confirmed => "badge-soft-blue",
        PurchaseStatus.PartiallyReceived => "badge-soft-orange",
        PurchaseStatus.Received => "badge-soft-green",
        PurchaseStatus.Cancelled => "badge-soft-red",
        _ => "badge-soft-gray"
    };
}

// ============================================================
// أمر الشراء
// ============================================================

/// <summary>
/// أمر شراء من مورد إلى مخزن.
///
/// <para><b>دورة الحياة:</b></para>
/// <code>
/// Draft → Confirmed → PartiallyReceived → Received
///   ↓         ↓              ↓
/// Cancelled Cancelled    Cancelled(ما استُلم يبقى)
/// </code>
///
/// <para>
/// <b>لا يُضاف للمخزون إلا عند الاستلام.</b> إنشاء الأمر نية شراء، والاعتماد
/// التزام تجاهها، وكلاهما لا يُدخل قطعة واحدة المخزن. لو أضفنا عند الاعتماد
/// لبِعنا قطعًا لم تصل بعد.
/// </para>
///
/// <para>
/// <b>المخزن يُحدَّد على الأمر لا على إذن الاستلام:</b> المورد يُوصّل لمكان
/// متفق عليه، وترك المخزن للاستلام يُتيح توزيع دفعات أمر واحد على مخازن
/// مختلفة فيصير «كم وصل لهذا المخزن؟» سؤالًا بلا جواب واحد.
/// </para>
/// </summary>
public class PurchaseOrder
{
    public int Id { get; set; }

    /// <summary>رقم مرجعي فريد بصيغة PO-yyyyMMdd-0001</summary>
    [StringLength(30)]
    [Display(Name = "رقم أمر الشراء")]
    public string PurchaseNumber { get; set; } = string.Empty;

    [Display(Name = "المورد")]
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>اسم المورد لحظة الأمر (snapshot) — لا يتغير بتعديل بيانات المورد لاحقًا</summary>
    [StringLength(150)]
    public string SupplierName { get; set; } = string.Empty;

    /// <summary>المخزن الذي ستدخل إليه البضاعة</summary>
    [Display(Name = "المخزن المستقبِل")]
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    [Display(Name = "الحالة")]
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

    // ===== المبالغ =====
    // تُحسب من السطور وتُخزَّن: إعادة حسابها من السطور لاحقًا تُعطي رقمًا
    // مختلفًا لو تغيّرت التكلفة، والمبلغ المتفق عليه مع المورد لا يتغير.

    [Display(Name = "الإجمالي قبل الخصم")]
    public decimal SubTotal { get; set; }

    [Range(0, 100, ErrorMessage = "نسبة الخصم بين 0 و 100")]
    [Display(Name = "نسبة الخصم %")]
    public decimal DiscountPercentage { get; set; }

    [Display(Name = "قيمة الخصم")]
    public decimal DiscountAmount { get; set; }

    [Display(Name = "الإجمالي")]
    public decimal Total { get; set; }

    [Display(Name = "تاريخ التوريد المتوقع")]
    [DataType(DataType.Date)]
    public DateTime? ExpectedDate { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [StringLength(300)]
    [Display(Name = "سبب الإلغاء")]
    public string? CancellationReason { get; set; }

    // ===== من فعل ماذا ومتى (snapshot للأسماء) =====

    [StringLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;
    [StringLength(150)]
    [Display(Name = "أنشأه")]
    public string CreatedByName { get; set; } = string.Empty;
    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [StringLength(450)]
    public string? ConfirmedByUserId { get; set; }
    [StringLength(150)]
    [Display(Name = "اعتمده")]
    public string? ConfirmedByName { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>تاريخ اكتمال الاستلام أو الإلغاء — لحظة إغلاق الأمر</summary>
    public DateTime? CompletedAt { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public ICollection<PurchaseReceipt> Receipts { get; set; } = new List<PurchaseReceipt>();

    // ===== خصائص محسوبة =====

    /// <summary>المسودة وحدها قابلة للتعديل — بعد الاعتماد صار الأمر التزامًا مع المورد</summary>
    public bool CanEdit => Status == PurchaseStatus.Draft;

    public bool CanConfirm => Status == PurchaseStatus.Draft;

    /// <summary>الاستلام متاح بعد الاعتماد وحتى اكتمال الكميات</summary>
    public bool CanReceive => Status is PurchaseStatus.Confirmed or PurchaseStatus.PartiallyReceived;

    /// <summary>
    /// الإلغاء متاح حتى مع وجود استلام جزئي: المورد قد يتوقف عن التوريد.
    /// ما استُلم يبقى في المخزون — لا يُسحب بالإلغاء، فهو موجود فعلًا.
    /// </summary>
    public bool CanCancel => Status is PurchaseStatus.Draft
        or PurchaseStatus.Confirmed or PurchaseStatus.PartiallyReceived;

    public bool IsClosed => Status is PurchaseStatus.Received or PurchaseStatus.Cancelled;

    public int TotalOrdered => Items.Sum(i => i.Quantity);
    public int TotalReceived => Items.Sum(i => i.ReceivedQuantity);
    public int TotalRemaining => Items.Sum(i => i.RemainingQuantity);

    /// <summary>هل كل السطور استُلمت بكمياتها كاملة؟</summary>
    public bool IsFullyReceived => Items.Count > 0 && Items.All(i => i.RemainingQuantity <= 0);
}

/// <summary>
/// سطر في أمر الشراء — صنف بتكلفته وكميته.
///
/// <para>
/// <b>التكلفة تُسجَّل هنا فقط</b> (قرار معتمد): تكلفة الشراء تختلف من أمر
/// لآخر، فوضعها على المنتج يجعل «التكلفة» رقمًا واحدًا يُطمس مع كل شراء
/// ويُفقد تاريخه. تسجيلها على السطر يحفظ ما دُفع فعلًا في كل مرة.
/// </para>
/// </summary>
public class PurchaseOrderItem
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>اسم المنتج وباركوده لحظة الأمر (snapshot)</summary>
    [StringLength(200)]
    [Display(Name = "المنتج")]
    public string ProductName { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "الباركود")]
    public string Barcode { get; set; } = string.Empty;

    /// <summary>سعر شراء الوحدة من هذا المورد في هذا الأمر</summary>
    [Range(0, 9999999, ErrorMessage = "التكلفة غير منطقية")]
    [Display(Name = "تكلفة الوحدة")]
    public decimal UnitCost { get; set; }

    [Range(1, 100000, ErrorMessage = "الكمية بين 1 و 100000")]
    [Display(Name = "الكمية")]
    public int Quantity { get; set; }

    /// <summary>ما وصل فعلًا من هذا السطر — يزيد مع كل إذن استلام</summary>
    [Range(0, 100000)]
    [Display(Name = "المستلم")]
    public int ReceivedQuantity { get; set; }

    [Display(Name = "إجمالي السطر")]
    public decimal LineTotal { get; set; }

    /// <summary>ما لم يصل بعد — أساس شاشة الاستلام</summary>
    public int RemainingQuantity => Quantity - ReceivedQuantity;

    public bool IsFullyReceived => RemainingQuantity <= 0;
}

// ============================================================
// إذن الاستلام
// ============================================================

/// <summary>
/// إذن استلام دفعة من أمر شراء.
///
/// <para>
/// <b>لماذا كيان منفصل لا مجرد تحديث للكمية على السطر؟</b> لأن المورد يُوصّل
/// على دفعات. بدون هذا الكيان نعرف «وصل 30 من 50» ولا نعرف متى وصلت كل
/// دفعة ولا من استلمها — وهي أول معلومة تُطلب عند أي خلاف مع مورد.
/// </para>
///
/// <para>
/// نفس منطق «القيد المعاكس» في المرتجعات: أمر الشراء لا يُعدَّل، والاستلام
/// حركة مستقلة مرتبطة به. الكمية على السطر كاش للمجموع لا مصدرًا وحيدًا.
/// </para>
/// </summary>
public class PurchaseReceipt
{
    public int Id { get; set; }

    /// <summary>رقم مرجعي فريد بصيغة RCV-yyyyMMdd-0001</summary>
    [StringLength(30)]
    [Display(Name = "رقم الإذن")]
    public string ReceiptNumber { get; set; } = string.Empty;

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    /// <summary>
    /// المخزن الذي دخلت إليه الدفعة. مُكرَّر من أمر الشراء عن قصد: لو نُقل
    /// الأمر لمخزن آخر لاحقًا يجب أن تبقى الدفعة القديمة مسجّلة على مخزنها.
    /// </summary>
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    [StringLength(450)]
    public string ReceivedByUserId { get; set; } = string.Empty;
    [StringLength(150)]
    [Display(Name = "استلمه")]
    public string ReceivedByName { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "تاريخ الاستلام")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<PurchaseReceiptItem> Items { get; set; } = new List<PurchaseReceiptItem>();

    public int TotalQuantity => Items.Sum(i => i.Quantity);
}

public class PurchaseReceiptItem
{
    public int Id { get; set; }

    public int PurchaseReceiptId { get; set; }
    public PurchaseReceipt? PurchaseReceipt { get; set; }

    /// <summary>السطر الذي تخصم منه هذه الدفعة — الرابط بين الإذن والأمر</summary>
    public int PurchaseOrderItemId { get; set; }
    public PurchaseOrderItem? PurchaseOrderItem { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [StringLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [Range(1, 100000)]
    [Display(Name = "الكمية المستلمة")]
    public int Quantity { get; set; }
}

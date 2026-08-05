using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

/// <summary>
/// حالة طلب التحويل بين المخازن.
///
/// <para>
/// الترتيب الرقمي يعكس تقدّم الطلب زمنيًا، فيصحّ الترتيب والمقارنة به.
/// القيم لا تُعاد ترقيمها بعد النشر لأنها محفوظة في قاعدة البيانات.
/// </para>
/// </summary>
public enum TransferStatus
{
    /// <summary>طُلب من المخزن المصدر وينتظر موافقته</summary>
    Pending = 1,

    /// <summary>وافق المصدر ولم يشحن بعد</summary>
    Approved = 2,

    /// <summary>خرجت القطع من المصدر ولم تُستلم بعد — «في الطريق»</summary>
    Shipped = 3,

    /// <summary>استُلمت بالكامل</summary>
    Received = 4,

    /// <summary>رفضه المصدر</summary>
    Rejected = 5,

    /// <summary>ألغاه الطالب قبل الشحن</summary>
    Cancelled = 6
}

public static class TransferStatusLabel
{
    public static string Of(TransferStatus s) => s switch
    {
        TransferStatus.Pending => "بانتظار الموافقة",
        TransferStatus.Approved => "معتمد — لم يُشحن",
        TransferStatus.Shipped => "في الطريق",
        TransferStatus.Received => "مستلم",
        TransferStatus.Rejected => "مرفوض",
        TransferStatus.Cancelled => "ملغي",
        _ => s.ToString()
    };

    /// <summary>لون الشارة في الواجهة</summary>
    public static string Badge(TransferStatus s) => s switch
    {
        TransferStatus.Pending => "badge-soft-orange",
        TransferStatus.Approved => "badge-soft-blue",
        TransferStatus.Shipped => "badge-soft-purple",
        TransferStatus.Received => "badge-soft-green",
        TransferStatus.Rejected => "badge-soft-red",
        TransferStatus.Cancelled => "badge-soft-gray",
        _ => "badge-soft-gray"
    };
}

/// <summary>
/// طلب تحويل قطع من مخزن إلى مخزن.
///
/// <para><b>دورة الحياة ولماذا هي بهذا الطول:</b></para>
/// <code>
/// Pending → Approved → Shipped → Received
///    ↓         ↓
/// Rejected  Cancelled
/// </code>
///
/// <para>
/// الخصم من المصدر يحدث عند <b>الشحن</b> لا عند الطلب ولا عند الاعتماد،
/// والإضافة للهدف عند <b>الاستلام</b>. لو خصمنا وأضفنا في اللحظة نفسها
/// لانتقلت القطع تليباثيًا وظهرت في الفرع قبل أن تصله، ولو خصمنا عند الطلب
/// لحُجزت قطع قد يُرفض طلبها.
/// </para>
///
/// <para>
/// الفارق <c>ShippedQuantity - ReceivedQuantity</c> على مستوى السطور هو
/// «القطع في الطريق»: ليست في المصدر لأنها خرجت، وليست في الهدف لأنها لم
/// تُسجَّل بعد. تُعرض من هذا الجدول مباشرة، ولم نُنشئ «مخزن ترانزيت وهمي»
/// لأنه يلوّث قائمة المخازن ويظهر في كل قائمة منسدلة في النظام.
/// </para>
/// </summary>
public class StockTransfer
{
    public int Id { get; set; }

    /// <summary>رقم مرجعي فريد بصيغة TRF-yyyyMMdd-0001</summary>
    [StringLength(30)]
    [Display(Name = "رقم التحويل")]
    public string TransferNumber { get; set; } = string.Empty;

    /// <summary>المخزن الذي تخرج منه القطع</summary>
    [Display(Name = "من مخزن")]
    public int FromWarehouseId { get; set; }
    public Warehouse? FromWarehouse { get; set; }

    /// <summary>المخزن الذي تدخل إليه القطع</summary>
    [Display(Name = "إلى مخزن")]
    public int ToWarehouseId { get; set; }
    public Warehouse? ToWarehouse { get; set; }

    [Display(Name = "الحالة")]
    public TransferStatus Status { get; set; } = TransferStatus.Pending;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>سبب الرفض — يُطلب إلزاميًا عند الرفض</summary>
    [StringLength(300)]
    [Display(Name = "سبب الرفض")]
    public string? RejectionReason { get; set; }

    // ===== من فعل ماذا ومتى =====
    // نحفظ الاسم إلى جانب المعرّف (snapshot) لأن حذف مستخدم أو تغيير اسمه
    // لا يجوز أن يمحو تاريخ من اعتمد التحويل أو شحنه.

    [StringLength(450)]
    public string RequestedByUserId { get; set; } = string.Empty;
    [StringLength(150)]
    [Display(Name = "طلبه")]
    public string RequestedByName { get; set; } = string.Empty;
    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; } = DateTime.Now;

    [StringLength(450)]
    public string? ApprovedByUserId { get; set; }
    [StringLength(150)]
    [Display(Name = "اعتمده")]
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }

    [StringLength(450)]
    public string? ShippedByUserId { get; set; }
    [StringLength(150)]
    [Display(Name = "شحنه")]
    public string? ShippedByName { get; set; }
    public DateTime? ShippedAt { get; set; }

    [StringLength(450)]
    public string? ReceivedByUserId { get; set; }
    [StringLength(150)]
    [Display(Name = "استلمه")]
    public string? ReceivedByName { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();

    // ===== خصائص محسوبة =====

    /// <summary>هل ما زال قابلًا للإلغاء من الطالب؟ (قبل خروج القطع فعلًا)</summary>
    public bool CanCancel => Status is TransferStatus.Pending or TransferStatus.Approved;

    /// <summary>هل ينتظر قرار المخزن المصدر؟</summary>
    public bool CanDecide => Status == TransferStatus.Pending;

    /// <summary>هل جاهز للشحن؟</summary>
    public bool CanShip => Status == TransferStatus.Approved;

    /// <summary>هل جاهز للاستلام؟</summary>
    public bool CanReceive => Status == TransferStatus.Shipped;

    /// <summary>هل انتهى الطلب (لا إجراء بعده)؟</summary>
    public bool IsClosed => Status is TransferStatus.Received
        or TransferStatus.Rejected or TransferStatus.Cancelled;

    public int TotalRequested => Items.Sum(i => i.RequestedQuantity);
    public int TotalShipped => Items.Sum(i => i.ShippedQuantity);
    public int TotalReceived => Items.Sum(i => i.ReceivedQuantity);

    /// <summary>
    /// القطع التي خرجت ولم تُستلم. تبقى موجبة بعد الاستلام الجزئي، وهذا
    /// مقصود: الفرق لا يتبخّر بل يظل ظاهرًا حتى يُسوّى بجرد صريح.
    /// </summary>
    public int TotalInTransit => TotalShipped - TotalReceived;
}

/// <summary>
/// سطر واحد في طلب التحويل — صنف واحد بكمياته الثلاث.
///
/// <para>
/// ثلاث كميات لا واحدة: <c>Requested</c> ما طُلب، <c>Shipped</c> ما خرج
/// فعلًا (قد يكون أقل لعدم توفّره)، <c>Received</c> ما وصل (قد يكون أقل
/// لتلف أو نقص). دمجها في رقم واحد يُخفي الفروق التي هي بالضبط ما يحتاج
/// المدير رؤيته.
/// </para>
/// </summary>
public class StockTransferItem
{
    public int Id { get; set; }

    public int StockTransferId { get; set; }
    public StockTransfer? StockTransfer { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>اسم المنتج وباركوده لحظة الطلب (snapshot) — لا يتأثر بتعديل المنتج لاحقًا</summary>
    [StringLength(200)]
    [Display(Name = "المنتج")]
    public string ProductName { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "الباركود")]
    public string Barcode { get; set; } = string.Empty;

    [Range(1, 100000, ErrorMessage = "الكمية المطلوبة بين 1 و 100000")]
    [Display(Name = "الكمية المطلوبة")]
    public int RequestedQuantity { get; set; }

    [Range(0, 100000)]
    [Display(Name = "الكمية المشحونة")]
    public int ShippedQuantity { get; set; }

    [Range(0, 100000)]
    [Display(Name = "الكمية المستلمة")]
    public int ReceivedQuantity { get; set; }

    /// <summary>القطع الخارجة من هذا السطر ولم تصل بعد</summary>
    public int InTransitQuantity => ShippedQuantity - ReceivedQuantity;

    /// <summary>هل شُحن أقل من المطلوب؟ (نقص في المصدر)</summary>
    public bool IsPartiallyShipped => ShippedQuantity < RequestedQuantity;

    /// <summary>هل وصل أقل من المشحون؟ (فرق يحتاج تفسيرًا)</summary>
    public bool HasShortage => ReceivedQuantity < ShippedQuantity;
}

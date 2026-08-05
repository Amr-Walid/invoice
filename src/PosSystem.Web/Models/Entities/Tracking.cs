using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

public enum StockMovementReason
{
    Sale = 1,
    ManualAdjustment = 2,
    Return = 3,
    InitialStock = 4,

    /// <summary>استلام مشتريات من مورد</summary>
    Purchase = 5,

    /// <summary>شحن تحويل — خروج من المخزن المُرسِل</summary>
    TransferOut = 6,

    /// <summary>استلام تحويل — دخول للمخزن المستقبِل</summary>
    TransferIn = 7,

    /// <summary>تسوية ناتجة عن جرد فعلي</summary>
    StockCount = 8,

    /// <summary>تالف أو فقد</summary>
    Damage = 9
}

/// <summary>الأسماء العربية لأسباب حركة المخزون — تُعرض في كارت الصنف</summary>
public static class StockMovementReasonLabels
{
    public static string Of(StockMovementReason reason) => reason switch
    {
        StockMovementReason.Sale => "بيع",
        StockMovementReason.ManualAdjustment => "تعديل يدوي",
        StockMovementReason.Return => "مرتجع",
        StockMovementReason.InitialStock => "رصيد افتتاحي",
        StockMovementReason.Purchase => "مشتريات",
        StockMovementReason.TransferOut => "تحويل صادر",
        StockMovementReason.TransferIn => "تحويل وارد",
        StockMovementReason.StockCount => "جرد",
        StockMovementReason.Damage => "تالف / فقد",
        _ => "غير معروف"
    };
}

/// <summary>حركة مخزون — سجل لكل تغيير في كمية المنتج</summary>
public class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>
    /// المخزن الذي وقعت فيه الحركة. الحركة بلا مخزن لا معنى لها في
    /// نطام متعدد المخازن — ولولاه لما أمكن تفسير فرق جرد في فرع معين.
    /// </summary>
    [Display(Name = "المخزن")]
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    /// <summary>مقدار التغيير — سالب في حالة البيع</summary>
    [Display(Name = "التغيير")]
    public int Change { get; set; }

    /// <summary>الكمية في هذا المخزن بعد التغيير</summary>
    [Display(Name = "الكمية بعد")]
    public int QuantityAfter { get; set; }

    [Display(Name = "السبب")]
    public StockMovementReason Reason { get; set; }

    public int? ReferenceInvoiceId { get; set; }

    /// <summary>المرتجع الذي سبّب هذه الحركة (في حالة الإرجاع للمخزون)</summary>
    public int? ReferenceReturnId { get; set; }

    /// <summary>إذن استلام المشتريات الذي سبّب الحركة</summary>
    public int? ReferencePurchaseReceiptId { get; set; }

    /// <summary>التحويل المخزني الذي سبّب الحركة (شحنًا أو استلامًا)</summary>
    public int? ReferenceTransferId { get; set; }

    /// <summary>التسوية / الجرد الذي سبّب الحركة</summary>
    public int? ReferenceAdjustmentId { get; set; }

    /// <summary>ملاحزة حرة توضّح سبب الحركة في كارت الصنف</summary>
    [StringLength(300)]
    [Display(Name = "ملاحزة")]
    public string? Note { get; set; }

    [StringLength(450)]
    public string? UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class AuditActions
{
    public const string Create = "إضافة";
    public const string Update = "تعديل";
    public const string Delete = "حذف";
    public const string Login = "تسجيل دخول";
    public const string Logout = "تسجيل خروج";
    public const string Sale = "بيع";
    public const string PrintBarcode = "طباعة باركود";
    public const string Return = "مرتجع";
    public const string Purchase = "أمر شراء";
    public const string Receive = "استلام";
    public const string Transfer = "تحويل مخزني";
    public const string Approve = "اعتماد";
    public const string Reject = "رفض";
    public const string Adjust = "تسوية مخزون";

    /// <summary>
    /// كل الأنواع المعروفة — تُستخدم لبناء قائمة الفلترة في «سجل العمليات»
    /// حتى لا يعتمد الفلتر على ما هو موجود فعلًا في قاعدة البيانات فقط
    /// (الفلتر المبني على DISTINCT يُخفي نوعًا لم يحدث بعد، وهذا مُحيّر للمدير).
    /// </summary>
    public static readonly string[] All =
    {
        Create, Update, Delete, Login, Logout, Sale, PrintBarcode, Return,
        Purchase, Receive, Transfer, Approve, Reject, Adjust
    };
}

/// <summary>سجل العمليات — لتتبع من فعل ماذا ومتى</summary>
public class AuditLog
{
    public int Id { get; set; }

    [StringLength(450)]
    public string? UserId { get; set; }

    [StringLength(150)]
    [Display(Name = "المستخدم")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "العملية")]
    public string Action { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "الكيان")]
    public string EntityName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? EntityId { get; set; }

    [Display(Name = "التفاصيل")]
    public string? Details { get; set; }

    [StringLength(64)]
    public string? IpAddress { get; set; }

    [Display(Name = "التاريخ والوقت")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

/// <summary>
/// المخزن — مكان مادي يحتفظ بأرصدة المنتجات.
///
/// النظام يدعم أكثر من مخزن، وكل مندوب مبيعات يُربط بمخزن واحد يبيع منه.
/// الكميات لا تُخزَّن هنا بل في <see cref="ProductStock"/> لأن الكمية صفة
/// للعلاقة (منتج × مخزن) لا صفة للمخزن ولا للمنتج منفردًا.
/// </summary>
public class Warehouse
{
    public int Id { get; set; }

    /// <summary>كود مختصر يُستخدم في أرقام الوثائق والتقارير — مثل MAIN أو BR01</summary>
    [Required(ErrorMessage = "كود المخزن مطلوب")]
    [StringLength(20, ErrorMessage = "كود المخزن لا يزيد عن 20 حرف")]
    [Display(Name = "كود المخزن")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم المخزن مطلوب")]
    [StringLength(150, ErrorMessage = "اسم المخزن لا يزيد عن 150 حرف")]
    [Display(Name = "اسم المخزن")]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "العنوان")]
    public string? Address { get; set; }

    [StringLength(30)]
    [Display(Name = "رقم الهاتف")]
    public string? Phone { get; set; }

    [StringLength(150)]
    [Display(Name = "مسؤول المخزن")]
    public string? ManagerName { get; set; }

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// المخزن الافتراضي — يُستخدم عند ترحيل البيانات القديمة وكاختيار مبدئي
    /// في الشاشات. مخزن واحد فقط يحمل هذه الصفة، ويضمنها WarehouseService.
    /// </summary>
    [Display(Name = "المخزن الافتراضي")]
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<ProductStock> Stocks { get; set; } = new List<ProductStock>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

    /// <summary>الاسم المعروض في القوائم — يجمع الكود والاسم</summary>
    public string DisplayName => $"{Name} ({Code})";
}

/// <summary>
/// رصيد منتج في مخزن معيّن — <b>مصدر الحقيقة الوحيد للكميات</b>.
///
/// <para>
/// حقل <c>Product.StockQuantity</c> يبقى موجودًا لكنه صار <b>كاشًا محسوبًا</b>
/// يساوي مجموع كل صفوف هذا الجدول للمنتج. الغرض من الكاش أن تظل القراءات
/// القديمة (نقطة البيع، تنبيه وشك النفاد، صفحة المنتجات) تعمل بلا تعديل.
/// </para>
///
/// <para>
/// ⚠️ لا يُكتب في هذا الجدول ولا في الكاش إلا من خلال
/// <c>IInventoryService</c>. أي تعديل مباشر في مكان آخر يجعل الكاش يختلف
/// عن الحقيقة وينتج فروق مخزون لا يمكن تفسيرها.
/// </para>
/// </summary>
public class ProductStock
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    [Display(Name = "الكمية")]
    public int Quantity { get; set; }

    /// <summary>
    /// حد التنبيه الخاص بهذا المخزن. عند <c>null</c> يُستخدم حد المنتج العام
    /// (<c>Product.LowStockThreshold</c>) — فرع صغير قد يكفيه 3 قطع بينما
    /// المستودع الرئيسي يحتاج 50، ولا يصح فرض رقم واحد على الاثنين.
    /// </summary>
    [Range(0, int.MaxValue)]
    [Display(Name = "حد التنبيه لهذا المخزن")]
    public int? LowStockThreshold { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>الحد الفعلي المُستخدم في حساب «وشك النفاد»</summary>
    public int EffectiveThreshold => LowStockThreshold ?? Product?.LowStockThreshold ?? 0;

    /// <summary>هل رصيد هذا المخزن منخفض؟</summary>
    public bool IsLowStock => Quantity <= EffectiveThreshold;
}

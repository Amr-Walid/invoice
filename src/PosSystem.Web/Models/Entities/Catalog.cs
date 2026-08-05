using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

/// <summary>البراند / الماركة التجارية</summary>
public class Brand
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم البراند مطلوب")]
    [StringLength(100, ErrorMessage = "اسم البراند لا يزيد عن 100 حرف")]
    [Display(Name = "اسم البراند")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

/// <summary>التصنيف — تابع لبراند معيّن</summary>
public class Category
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم التصنيف مطلوب")]
    [StringLength(100, ErrorMessage = "اسم التصنيف لا يزيد عن 100 حرف")]
    [Display(Name = "اسم التصنيف")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "البراند")]
    [Range(1, int.MaxValue, ErrorMessage = "يجب اختيار البراند")]
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

/// <summary>المنتج — الوحدة الأساسية للبيع، يُبحث عنه بالباركود</summary>
public class Product
{
    public int Id { get; set; }

    [Required(ErrorMessage = "كود الباركود مطلوب")]
    [StringLength(64, ErrorMessage = "الباركود لا يزيد عن 64 حرف")]
    [Display(Name = "كود الباركود")]
    public string Barcode { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم المنتج مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم المنتج")]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 9999999, ErrorMessage = "السعر يجب أن يكون أكبر من صفر")]
    [Display(Name = "سعر المنتج")]
    public decimal Price { get; set; }

    // ملاحظة: أُزيل حقل «سعر التكلفة» بطلب صريح — النظام لا يتعامل مع
    // المعاملات المالية للتكلفة ولا الأرباح، سعر المنتج وحده يكفي.

    /// <summary>
    /// ⚠️ <b>كاش لا مصدر حقيقة.</b> بعد تعدُّد المخازن صار الرصيد الحقيقي موزَّعًا
    /// في <see cref="ProductStock"/> لكل مخزن، وهذا الحقل مجموعها في كل المخازن.
    /// يُبقى للسرعة (قوائم المنتجات والتنبيهات تقرأه بلا JOIN) ويحدّثه
    /// <c>IInventoryService</c> وحده مع كل حركة. لا تكتب فيه مباشرة أبدًا.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    [Display(Name = "إجمالي الكمية (كل المخازن)")]
    public int StockQuantity { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "حد التنبيه للمخزون")]
    public int LowStockThreshold { get; set; } = 5;

    [Display(Name = "البراند")]
    [Range(1, int.MaxValue, ErrorMessage = "يجب اختيار البراند")]
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }

    [Display(Name = "التصنيف")]
    [Range(1, int.MaxValue, ErrorMessage = "يجب اختيار التصنيف")]
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    /// <summary>أرصدة هذا المنتج موزَّعة على المخازن — مصدر الحقيقة</summary>
    public ICollection<ProductStock> Stocks { get; set; } = new List<ProductStock>();

    /// <summary>هل المخزون منخفض؟ (خاصية محسوبة، غير مخزنة)</summary>
    public bool IsLowStock => StockQuantity <= LowStockThreshold;
}

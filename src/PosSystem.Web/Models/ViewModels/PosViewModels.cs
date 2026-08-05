using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.ViewModels;

// ==================== الدخول ====================
public class LoginVm
{
    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [Display(Name = "البريد الإلكتروني")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة المرور")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "تذكرني")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

// ==================== نتيجة البحث بالباركود ====================
public class ProductLookupResult
{
    public bool Found { get; set; }
    public string? Message { get; set; }
    public int Id { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
}

// ==================== التحقق من الكوبون ====================
public class CouponValidationResult
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal DiscountPercentage { get; set; }
    public int? CouponId { get; set; }
}

// ==================== طلب إنشاء فاتورة ====================
/// <summary>
/// ما يُرسله المتصفح فقط: المنتج والكمية + الكوبون + بيانات العميل.
/// لا تُرسل أسعار — السيرفر يقرأها من قاعدة البيانات دائمًا.
/// </summary>
public class CreateInvoiceRequest
{
    public List<InvoiceLineRequest> Items { get; set; } = new();

    public string? CouponCode { get; set; }

    [StringLength(150)]
    public string? CustomerName { get; set; }

    [StringLength(30)]
    public string? CustomerPhone { get; set; }
}

public class InvoiceLineRequest
{
    public int ProductId { get; set; }
    public string? Barcode { get; set; }

    [Range(1, 100000)]
    public int Quantity { get; set; }
}

// ==================== نتيجة إنشاء الفاتورة ====================
public class CreateInvoiceResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal Total { get; set; }

    public static CreateInvoiceResult Fail(string message) => new() { Success = false, Message = message };
}

// ==================== لوحة تحكم المندوب ====================
public class AgentDashboardVm
{
    public string AgentName { get; set; } = string.Empty;
    public int TodayInvoices { get; set; }
    public decimal TodaySales { get; set; }
    public int MonthInvoices { get; set; }
    public decimal MonthSales { get; set; }
}

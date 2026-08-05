using System.ComponentModel.DataAnnotations;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Models.ViewModels;

/// <summary>سطر قابل للإرجاع معروض في شاشة المرتجع</summary>
public class ReturnableItemVm
{
    public int InvoiceItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    /// <summary>الكمية الأصلية في الفاتورة</summary>
    public int SoldQuantity { get; set; }

    /// <summary>ما أُرجع سابقًا من هذا السطر</summary>
    public int AlreadyReturned { get; set; }

    /// <summary>ما تبقّى قابلًا للإرجاع</summary>
    public int Remaining => SoldQuantity - AlreadyReturned;

    /// <summary>الكمية التي اختار المستخدم إرجاعها الآن</summary>
    public int QuantityToReturn { get; set; }
}

/// <summary>شاشة تنفيذ المرتجع لفاتورة محددة</summary>
public class ReturnFormVm
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }

    public decimal InvoiceTotal { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal AlreadyRefunded { get; set; }
    public InvoiceStatus Status { get; set; }

    public List<ReturnableItemVm> Items { get; set; } = new();

    [Display(Name = "سبب الإرجاع")]
    public ReturnReason Reason { get; set; } = ReturnReason.Defective;

    [StringLength(500, ErrorMessage = "الملاحظات لا تزيد عن 500 حرف")]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>
    /// هل تُعاد الأصناف للمخزون؟ افتراضيًا نعم — وهو مطلب المستخدم الصريح:
    /// «الأجهزة اللي رجّعها تُحسب تاني في المخزون». لكن المنتج التالف
    /// لا يصلح للبيع، فتُتاح للمستخدم إمكانية إلغاء الإعادة.
    /// </summary>
    [Display(Name = "إعادة الأصناف إلى المخزون")]
    public bool RestockToInventory { get; set; } = true;

    public bool HasReturnableItems => Items.Any(i => i.Remaining > 0);
}

/// <summary>الطلب المُرسَل عند تأكيد الإرجاع</summary>
public class CreateReturnRequest
{
    public int InvoiceId { get; set; }
    public ReturnReason Reason { get; set; } = ReturnReason.Defective;
    public string? Notes { get; set; }
    public bool RestockToInventory { get; set; } = true;
    public List<ReturnLineDto> Items { get; set; } = new();
}

public class ReturnLineDto
{
    public int InvoiceItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateReturnResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public int RestockedQuantity { get; set; }
    public bool InvoiceFullyReturned { get; set; }

    public static CreateReturnResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>نتيجة البحث عن فاتورة لإرجاعها</summary>
public class ReturnSearchVm
{
    [Display(Name = "رقم الفاتورة أو رقم هاتف العميل")]
    public string? Query { get; set; }

    public bool Searched { get; set; }
    public List<InvoiceSearchRowVm> Results { get; set; } = new();
}

public class InvoiceSearchRowVm
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal RefundedAmount { get; set; }
    public InvoiceStatus Status { get; set; }
    public int TotalQuantity { get; set; }
    public int ReturnedQuantity { get; set; }

    public bool CanReturn => Status != InvoiceStatus.Cancelled && ReturnedQuantity < TotalQuantity;

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

/// <summary>قائمة المرتجعات مع التصفية</summary>
public class ReturnListVm
{
    public string? Query { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public List<Return> Returns { get; set; } = new();

    public int TotalItemsReturned { get; set; }
    public decimal TotalRefunded { get; set; }

    /// <summary>هل المستخدم مدير؟ (المندوب يرى مرتجعاته فقط)</summary>
    public bool IsAdmin { get; set; }
}

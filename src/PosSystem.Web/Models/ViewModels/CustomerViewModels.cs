using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Models.ViewModels;

public class CustomerRowVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int InvoiceCount { get; set; }
    public int ReturnCount { get; set; }
    public DateTime? LastPurchase { get; set; }

    /// <summary>ما دفعه العميل فعلًا = إجمالي الفواتير - المرتجعات</summary>
    public decimal TotalSpent { get; set; }
}

public class CustomerListVm
{
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 20;
    public List<CustomerRowVm> Customers { get; set; } = new();
}

public class CustomerDetailsVm
{
    public Customer Customer { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();
    public List<Return> Returns { get; set; } = new();

    /// <summary>إجمالي الفواتير قبل خصم المرتجعات</summary>
    public decimal GrossSpent { get; set; }
    public decimal TotalRefunded { get; set; }

    /// <summary>الصافي فعلًا = GrossSpent - TotalRefunded</summary>
    public decimal TotalSpent { get; set; }
    public int ItemsBought { get; set; }

    public int InvoiceCount => Invoices.Count;
    public int ReturnCount => Returns.Count;
}

/// <summary>اقتراح عميل في خانة الإكمال التلقائي داخل نافذة الفاتورة</summary>
public class CustomerSuggestionVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
}

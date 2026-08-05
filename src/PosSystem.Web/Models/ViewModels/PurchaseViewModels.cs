using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Models.ViewModels;

// ============================================================
//  الموردون
// ============================================================

public class SupplierRowVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>عدد الأوامر غير الملغاة — الملغي لم يُنفَّق عليه شيء</summary>
    public int OrderCount { get; set; }

    /// <summary>أوامر بانتظار التوريد — الرقم الذي يُتابَع يوميًا</summary>
    public int OpenOrderCount { get; set; }

    public decimal TotalPurchased { get; set; }
    public DateTime? LastOrderAt { get; set; }
}

public class SupplierListVm
{
    public string? Query { get; set; }
    public bool IncludeInactive { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 20;
    public List<SupplierRowVm> Suppliers { get; set; } = new();
}

/// <summary>كشف حساب المورد — أوامره ومجاميعها</summary>
public class SupplierStatementVm
{
    public Supplier Supplier { get; set; } = new();
    public List<PurchaseOrder> Orders { get; set; } = new();

    public decimal TotalPurchased { get; set; }
    public int OrderCount { get; set; }
    public int OpenOrderCount { get; set; }
    public int CancelledCount { get; set; }
    public int PiecesOrdered { get; set; }
    public int PiecesReceived { get; set; }

    /// <summary>ما لم يصل بعد من كل أوامره — التزام المورد القائم</summary>
    public int PiecesPending => Math.Max(0, PiecesOrdered - PiecesReceived);
}

// ============================================================
//  أوامر الشراء
// ============================================================

/// <summary>صف في قائمة أوامر الشراء</summary>
public class PurchaseOrderRowVm
{
    public int Id { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public PurchaseStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string CreatedByName { get; set; } = string.Empty;

    public int TotalOrdered { get; set; }
    public int TotalReceived { get; set; }
    public int ItemCount { get; set; }

    public int TotalRemaining => Math.Max(0, TotalOrdered - TotalReceived);

    /// <summary>تأخّر عن تاريخ التوريد المتوقع وما زال مفتوحًا</summary>
    public bool IsOverdue =>
        ExpectedDate.HasValue &&
        ExpectedDate.Value.Date < DateTime.Today &&
        (Status == PurchaseStatus.Confirmed || Status == PurchaseStatus.PartiallyReceived);

    public string StatusLabel => PurchaseStatusLabel.Of(Status);
    public string StatusBadge => PurchaseStatusLabel.Badge(Status);
}

public class PurchaseOrderListVm
{
    public string? Query { get; set; }
    public int? SupplierId { get; set; }
    public int? WarehouseId { get; set; }
    public PurchaseStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 20;

    public List<PurchaseOrderRowVm> Orders { get; set; } = new();

    // ===== مجاميع الصفحة الكاملة (لا الصفحة المعروضة) =====
    public decimal GrandTotal { get; set; }
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
    public int PiecesPending { get; set; }
}

/// <summary>صنف مقترح في شاشة إنشاء أمر الشراء</summary>
public class PurchaseProductRowVm
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal Price { get; set; }

    /// <summary>الرصيد الحالي في المخزن المستقبِل — يجيب «هل أشتري أصلًا؟»</summary>
    public int QuantityHere { get; set; }
    public int Threshold { get; set; }

    /// <summary>آخر تكلفة شراء لهذا الصنف — تُقترح تلقائيًا فلا يُبحث عنها يدويًا</summary>
    public decimal? LastCost { get; set; }

    public bool IsLowStock => QuantityHere <= Threshold;
}

/// <summary>سطر واحد يُرسله المتصفح عند إنشاء أمر الشراء</summary>
public class PurchaseLineInput
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

/// <summary>تفاصيل أمر الشراء + ما تحتاجه شاشة الاستلام</summary>
public class PurchaseDetailsVm
{
    public PurchaseOrder Order { get; set; } = new();

    /// <summary>أرصدة أصناف الأمر في المخزن المستقبِل — قبل/بعد الاستلام</summary>
    public Dictionary<int, int> StockHere { get; set; } = new();

    public bool CanManage { get; set; }
    public bool CanReceive { get; set; }
}

// ============================================================
//  تقرير المشتريات
// ============================================================

public class PurchaseReportRowVm
{
    public string Label { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public int Pieces { get; set; }
    public decimal Total { get; set; }
}

public class PurchaseReportVm
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? WarehouseId { get; set; }

    /// <summary>المشتريات بالمورد</summary>
    public List<PurchaseReportRowVm> BySupplier { get; set; } = new();

    /// <summary>المشتريات بالمخزن</summary>
    public List<PurchaseReportRowVm> ByWarehouse { get; set; } = new();

    /// <summary>المشتريات باليوم</summary>
    public List<PurchaseReportRowVm> ByDay { get; set; } = new();

    /// <summary>أكثر الأصناف شراءً</summary>
    public List<PurchaseReportRowVm> TopProducts { get; set; } = new();

    public decimal GrandTotal { get; set; }
    public int OrderCount { get; set; }
    public int PiecesOrdered { get; set; }
    public int PiecesReceived { get; set; }
    public int PiecesPending => Math.Max(0, PiecesOrdered - PiecesReceived);
}

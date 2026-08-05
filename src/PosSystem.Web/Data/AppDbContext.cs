using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Return> Returns => Set<Return>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptItem> PurchaseReceiptItems => Set<PurchaseReceiptItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ===================== Brand =====================
        builder.Entity<Brand>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        // ===================== Category =====================
        builder.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            // نفس اسم التصنيف مسموح لبراندات مختلفة، لكن ليس مرتين لنفس البراند
            e.HasIndex(x => new { x.BrandId, x.Name }).IsUnique();
            e.HasOne(x => x.Brand)
             .WithMany(b => b.Categories)
             .HasForeignKey(x => x.BrandId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== Product =====================
        builder.Entity<Product>(e =>
        {
            e.Property(x => x.Barcode).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            // decimal(18,2) — دقة مالية، لا نستخدم float أبدًا للمال
            e.Property(x => x.Price).HasPrecision(18, 2);

            // أهم فهرس في النظام: البحث الفوري بالباركود في الـ POS
            e.HasIndex(x => x.Barcode).IsUnique();
            e.HasIndex(x => new { x.BrandId, x.CategoryId });

            e.HasOne(x => x.Brand)
             .WithMany(b => b.Products)
             .HasForeignKey(x => x.BrandId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Category)
             .WithMany(c => c.Products)
             .HasForeignKey(x => x.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== Coupon =====================
        builder.Entity<Coupon>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.DiscountPercentage).HasPrecision(5, 2);
            e.HasIndex(x => x.Code).IsUnique();
        });

        // ===================== Customer =====================
        builder.Entity<Customer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            e.Property(x => x.PhoneNormalized).HasMaxLength(30).IsRequired();

            // رقم الهاتف الموحَّد هو المُعرّف الحقيقي للعميل — فهرس فريد
            // يمنع تكرار العميل نفسه على مستوى قاعدة البيانات لا التطبيق فقط.
            e.HasIndex(x => x.PhoneNormalized).IsUnique();
            e.HasIndex(x => x.Name);
        });

        // ===================== Invoice =====================
        builder.Entity<Invoice>(e =>
        {
            e.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.SubTotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercentage).HasPrecision(5, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.RefundedAmount).HasPrecision(18, 2);

            e.HasIndex(x => x.InvoiceNumber).IsUnique();
            e.HasIndex(x => x.CreatedAt);                        // كل التقارير الزمنية
            e.HasIndex(x => new { x.AgentId, x.CreatedAt });      // تقارير المندوب
            e.HasIndex(x => x.CustomerPhone);                     // البحث برقم العميل
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });   // فواتير العميل
            e.HasIndex(x => new { x.WarehouseId, x.CreatedAt });   // مبيعات كل مخزن

            // الفاتورة وثيقة مالية: لا يُحذف مخزن وله مبيعات
            e.HasOne(x => x.Warehouse)
             .WithMany()
             .HasForeignKey(x => x.WarehouseId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Agent)
             .WithMany(u => u.Invoices)
             .HasForeignKey(x => x.AgentId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Coupon)
             .WithMany(c => c.Invoices)
             .HasForeignKey(x => x.CouponId)
             .OnDelete(DeleteBehavior.SetNull);

            // حذف العميل لا يحذف فواتيره — الفاتورة وثيقة مالية تاريخية.
            // ومع snapshot للاسم والرقم تبقى الفاتورة مقروءة بعد الفكّ.
            e.HasOne(x => x.Customer)
             .WithMany(c => c.Invoices)
             .HasForeignKey(x => x.CustomerId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ===================== InvoiceItem =====================
        builder.Entity<InvoiceItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(200);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);

            e.HasIndex(x => x.InvoiceId);

            // الأصناف جزء لا ينفصل عن الفاتورة
            e.HasOne(x => x.Invoice)
             .WithMany(i => i.Items)
             .HasForeignKey(x => x.InvoiceId)
             .OnDelete(DeleteBehavior.Cascade);

            // منع حذف منتج له مبيعات — حفاظًا على البيانات التاريخية
            e.HasOne(x => x.Product)
             .WithMany(p => p.InvoiceItems)
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== Return =====================
        builder.Entity<Return>(e =>
        {
            e.Property(x => x.ReturnNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.ProcessedByName).HasMaxLength(150);
            e.Property(x => x.SubTotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercentage).HasPrecision(5, 2);
            e.Property(x => x.RefundAmount).HasPrecision(18, 2);

            e.HasIndex(x => x.ReturnNumber).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => new { x.ProcessedByUserId, x.CreatedAt });
            e.HasIndex(x => new { x.WarehouseId, x.CreatedAt });

            e.HasOne(x => x.Warehouse)
             .WithMany()
             .HasForeignKey(x => x.WarehouseId)
             .OnDelete(DeleteBehavior.Restrict);

            // المرتجع لا معنى له بدون فاتورته، لكن لا نسمح بحذف فاتورة لها مرتجعات
            e.HasOne(x => x.Invoice)
             .WithMany(i => i.Returns)
             .HasForeignKey(x => x.InvoiceId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ProcessedByUser)
             .WithMany()
             .HasForeignKey(x => x.ProcessedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Customer)
             .WithMany(c => c.Returns)
             .HasForeignKey(x => x.CustomerId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ===================== ReturnItem =====================
        builder.Entity<ReturnItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(200);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);

            e.HasIndex(x => x.ReturnId);
            e.HasIndex(x => x.InvoiceItemId);

            e.HasOne(x => x.Return)
             .WithMany(r => r.Items)
             .HasForeignKey(x => x.ReturnId)
             .OnDelete(DeleteBehavior.Cascade);

            // لا حذف لسطر فاتورة مرتبط بمرتجع — ولولا Restrict لأنتج
            // SQL Server مسارات cascade متعددة ورفض إنشاء القيد.
            e.HasOne(x => x.InvoiceItem)
             .WithMany(i => i.ReturnItems)
             .HasForeignKey(x => x.InvoiceItemId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Product)
             .WithMany()
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== Warehouse =====================
        builder.Entity<Warehouse>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.ManagerName).HasMaxLength(150);

            // كود المخزن هو مُعرّفه المقروء في المستندات (أوامر الشراء، التحويلات)
            // فلا يجوز تكراره ولو اختلف الاسم.
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.IsDefault);   // البحث عن المخزن الافتراضي يتكرر كثيرًا
        });

        // ===================== ProductStock =====================
        // ⭐ جدول الحقيقة للكميات. Product.StockQuantity كاش لمجموع صفوفه.
        builder.Entity<ProductStock>(e =>
        {
            // القيد الأهم في السيستم كله: صف واحد فقط لكل (منتج، مخزن).
            // بدونه يمكن أن يوجد رصيدان للصنف نفسه في المخزن نفسه فتكذب الأرقام
            // ولا ينفع أي إصلاح على مستوى التطبيق.
            e.HasIndex(x => new { x.ProductId, x.WarehouseId }).IsUnique();

            // فهرس تقارير المخزن: "كل أصناف مخزن كذا" و"الأصناف الناقصة فيه"
            e.HasIndex(x => new { x.WarehouseId, x.Quantity });

            // Restrict على الطرفين: لا يُحذف منتج له أرصدة ولا مخزن به أرصدة.
            // وأيضًا لأن Cascade من الجهتين ينتج مسارات cascade متعددة
            // يرفضها SQL Server (نفس سبب Restrict في ReturnItem → InvoiceItem).
            e.HasOne(x => x.Product)
             .WithMany(p => p.Stocks)
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Warehouse)
             .WithMany(w => w.Stocks)
             .HasForeignKey(x => x.WarehouseId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== StockMovement =====================
        builder.Entity<StockMovement>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(300);

            e.HasIndex(x => x.ProductId);
            e.HasIndex(x => x.CreatedAt);
            // كارت الصنف داخل مخزن معيّن — أهم شاشة تتبُّع في النظام
            e.HasIndex(x => new { x.WarehouseId, x.ProductId, x.CreatedAt });

            e.HasOne(x => x.Product)
             .WithMany()
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Cascade);

            // الحركة سِجل تاريخي: لا يُحذف مخزن وله حركات مسجَّلة
            e.HasOne(x => x.Warehouse)
             .WithMany()
             .HasForeignKey(x => x.WarehouseId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== StockTransfer =====================
        builder.Entity<StockTransfer>(e =>
        {
            e.Property(x => x.TransferNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionReason).HasMaxLength(300);
            e.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.RequestedByName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ApprovedByUserId).HasMaxLength(450);
            e.Property(x => x.ApprovedByName).HasMaxLength(150);
            e.Property(x => x.ShippedByUserId).HasMaxLength(450);
            e.Property(x => x.ShippedByName).HasMaxLength(150);
            e.Property(x => x.ReceivedByUserId).HasMaxLength(450);
            e.Property(x => x.ReceivedByName).HasMaxLength(150);

            e.HasIndex(x => x.TransferNumber).IsUnique();

            // شاشة «طلبات واردة للاعتماد»: كل الطلبات المعلّقة على مخزن مصدر
            e.HasIndex(x => new { x.FromWarehouseId, x.Status });
            // شاشة «طلباتي»: كل ما طلبه مخزني وحالته
            e.HasIndex(x => new { x.ToWarehouseId, x.Status });
            e.HasIndex(x => x.RequestedAt);

            // كلا الطرفين Restrict: التحويل سِجل تاريخي، وحذف مخزن طرف في
            // تحويل يمحو نصف قصة حركة القطع. الحذف يُمنع في WarehouseService
            // برسالة مفهومة قبل أن يصل الأمر إلى قيد قاعدة البيانات.
            e.HasOne(x => x.FromWarehouse)
             .WithMany()
             .HasForeignKey(x => x.FromWarehouseId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ToWarehouse)
             .WithMany()
             .HasForeignKey(x => x.ToWarehouseId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== StockTransferItem =====================
        builder.Entity<StockTransferItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Barcode).HasMaxLength(64).IsRequired();

            e.HasIndex(x => x.StockTransferId);
            e.HasIndex(x => x.ProductId);

            // حذف الطلب يحذف سطوره — السطر لا معنى له بلا رأسه
            e.HasOne(x => x.StockTransfer)
             .WithMany(t => t.Items)
             .HasForeignKey(x => x.StockTransferId)
             .OnDelete(DeleteBehavior.Cascade);

            // Restrict على المنتج: منتج له تحويلات لا يُحذف، ولمنع مسارات
            // cascade متعددة في SQL Server (نفس سبب ReturnItem → InvoiceItem)
            e.HasOne(x => x.Product)
             .WithMany()
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== Supplier =====================
        builder.Entity<Supplier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            e.Property(x => x.PhoneNormalized).HasMaxLength(30).IsRequired();
            e.Property(x => x.Email).HasMaxLength(150);
            e.Property(x => x.Address).HasMaxLength(200);
            e.Property(x => x.ContactPerson).HasMaxLength(150);
            e.Property(x => x.Notes).HasMaxLength(500);

            // الهاتف المُطبَّع هو المُعرِّف: فهرس فريد يمنع تكرار المورد نفسه
            // بأسماء مكتوبة بصيغ مختلفة فتتفرّق حركاته على سجلَّين
            e.HasIndex(x => x.PhoneNormalized).IsUnique();
            // البحث بالاسم في شاشة الموردين وفي قائمة اختيار المورد
            e.HasIndex(x => x.Name);
        });

        // ===================== PurchaseOrder =====================
        builder.Entity<PurchaseOrder>(e =>
        {
            e.Property(x => x.PurchaseNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.SupplierName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CancellationReason).HasMaxLength(300);
            e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.CreatedByName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ConfirmedByUserId).HasMaxLength(450);
            e.Property(x => x.ConfirmedByName).HasMaxLength(150);

            // decimal(18,2) لكل مبلغ — لا float للمال أبدًا
            e.Property(x => x.SubTotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercentage).HasPrecision(5, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);

            e.HasIndex(x => x.PurchaseNumber).IsUnique();
            // كشف حساب المورد: كل أوامره مرتّبة
            e.HasIndex(x => new { x.SupplierId, x.CreatedAt });
            // شاشة «أوامر بانتظار التوريد» لمخزن معيّن
            e.HasIndex(x => new { x.WarehouseId, x.Status });
            // تقرير المشتريات بالفترة
            e.HasIndex(x => x.CreatedAt);

            // Restrict على الطرفين: أمر الشراء سِجل مالي، وحذف مورد أو مخزن
            // يُفقد نصف القصة. المنع يقع في الخدمة برسالة عربية مفهومة قبل
            // أن يصل الأمر إلى قيد قاعدة البيانات
            e.HasOne(x => x.Supplier)
             .WithMany(s => s.PurchaseOrders)
             .HasForeignKey(x => x.SupplierId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Warehouse)
             .WithMany()
             .HasForeignKey(x => x.WarehouseId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== PurchaseOrderItem =====================
        builder.Entity<PurchaseOrderItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Barcode).HasMaxLength(64).IsRequired();
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);

            e.HasIndex(x => x.PurchaseOrderId);
            // «تاريخ تكلفة هذا المنتج» — كل سطوره عبر الأوامر
            e.HasIndex(x => x.ProductId);

            // حذف الأمر يحذف سطوره — السطر لا معنى له بلا رأسه
            e.HasOne(x => x.PurchaseOrder)
             .WithMany(o => o.Items)
             .HasForeignKey(x => x.PurchaseOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Product)
             .WithMany()
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== PurchaseReceipt =====================
        builder.Entity<PurchaseReceipt>(e =>
        {
            e.Property(x => x.ReceiptNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.ReceivedByUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.ReceivedByName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);

            e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.HasIndex(x => x.PurchaseOrderId);
            e.HasIndex(x => x.CreatedAt);

            // Cascade من الأمر: أذون استلام أمر محذوف لا معنى لها. عمليًا
            // الأمر الذي له استلام لا يُحذف (الخدمة تمنعه)، والقيد شبكة أمان
            e.HasOne(x => x.PurchaseOrder)
             .WithMany(o => o.Receipts)
             .HasForeignKey(x => x.PurchaseOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            // Restrict على المخزن، ولمنع مسار cascade ثانٍ في SQL Server
            e.HasOne(x => x.Warehouse)
             .WithMany()
             .HasForeignKey(x => x.WarehouseId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== PurchaseReceiptItem =====================
        builder.Entity<PurchaseReceiptItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();

            e.HasIndex(x => x.PurchaseReceiptId);
            e.HasIndex(x => x.PurchaseOrderItemId);
            e.HasIndex(x => x.ProductId);

            e.HasOne(x => x.PurchaseReceipt)
             .WithMany(r => r.Items)
             .HasForeignKey(x => x.PurchaseReceiptId)
             .OnDelete(DeleteBehavior.Cascade);

            // Restrict على سطر الأمر: هذا ثاني مسار من PurchaseOrder إلى هنا
            // (الأول عبر PurchaseReceipt) و SQL Server يرفض مساري cascade
            // لجدول واحد — نفس سبب ReturnItem → InvoiceItem
            e.HasOne(x => x.PurchaseOrderItem)
             .WithMany()
             .HasForeignKey(x => x.PurchaseOrderItemId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Product)
             .WithMany()
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ===================== AuditLog =====================
        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.UserId);
        });

        // أسماء جداول Identity تبقى الافتراضية (AspNetUsers ...)
        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();

            // ربط المستخدم بمخزنه. SetNull لا Restrict: حذف مخزن (لو خلا من
            // الأرصدة والحركات) يجب ألا يُعطِّل حساب المندوب — يبقى الحساب
            // موجودًا بلا مخزن، والنظام يمنعه من البيع برسالة واضحة
            // حتى يُسنِد له المدير مخزنًا جديدًا.
            e.HasOne(x => x.Warehouse)
             .WithMany(w => w.Users)
             .HasForeignKey(x => x.WarehouseId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.WarehouseId);
        });
    }
}

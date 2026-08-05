using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosSystem.Web.Data.Migrations
{
    /// <summary>
    /// إضافة المخازن المتعددة.
    ///
    /// <para>
    /// <b>تحذير للمُعدِّل:</b> هذا الترحيل مُعدَّل يدويًا ولا يجوز إعادة توليده
    /// بـ<c>ef migrations add</c> ثم قبول الناتج كما هو. المولِّد يُصدر
    /// أعمدة <c>WarehouseId</c> الإلزامية بالشكل التالي:
    /// <c>AddColumn(nullable: false, defaultValue: 0)</c> ثم
    /// <c>AddForeignKey(Restrict)</c> — وهذا <b>يفشل</b> على أي قاعدة بها صفوف،
    /// لأن كل صف قديم يحصل على <c>WarehouseId = 0</c> ولا يوجد مخزن بهذا المعرّف
    /// فيرفض السيرفر إنشاء القيد. الترتيب الصحيح المُنفَّذ هنا:
    /// عمود <b>nullable</b> ← إنشاء المخزن الرئيسي ← نقل البيانات بـSQL ←
    /// <c>AlterColumn</c> إلى NOT NULL ← الفهرس ← المفتاح الأجنبي.
    /// </para>
    /// </summary>
    public partial class AddWarehousesAndProductStock : Migration
    {
        /// <summary>
        /// كود المخزن الافتراضي. ثابت لأنه يُستخدم في ست عبارات SQL أدناه،
        /// وأي اختلاف حرف واحد بينها يجعل النقل يُسند صفوفًا لمخزن غير موجود.
        /// نفس القيمة موجودة في <c>DbSeeder</c> لأن SQLite يستخدم
        /// <c>EnsureCreatedAsync()</c> فيتخطى الترحيلات كليًا.
        /// </summary>
        private const string MainWarehouseCode = "MAIN";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "StockMovements",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceAdjustmentId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferencePurchaseReceiptId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceTransferId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            // الأعمدة الثلاثة الإلزامية تُضاف nullable مؤقتًا. لا يمكن إضافتها
            // NOT NULL هنا لأن الصفوف القديمة لا تعرف مخزنها بعد — المخزن
            // الرئيسي لم يُنشأ أصلًا (جدول Warehouses يُنشأ بعد قليل).
            // يُشدَّد كل عمود إلى NOT NULL في القسم (5) بعد ملئه.
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Returns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Invoices",
                type: "int",
                nullable: true);

            // هذا العمود nullable في المخطط نفسه: NULL عند الأدمن تعني
            // «كل المخازن» لا «بلا مخزن»، فلا يُشدَّد لاحقًا.
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ManagerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    LowStockThreshold = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductStocks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WarehouseId",
                table: "AspNetUsers",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_ProductId_WarehouseId",
                table: "ProductStocks",
                columns: new[] { "ProductId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_WarehouseId_Quantity",
                table: "ProductStocks",
                columns: new[] { "WarehouseId", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsDefault",
                table: "Warehouses",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Name",
                table: "Warehouses",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Warehouses_WarehouseId",
                table: "AspNetUsers",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ================================================================
            // نقل البيانات — الجزء الذي لا يولّده EF ولا يمكن الاستغناء عنه.
            // بدونه يظهر كل المخزون صفرًا وتفشل قيود المفاتيح الأجنبية.
            // ================================================================

            // (1) المخزن الرئيسي. شرط NOT EXISTS يجعل العبارة قابلة لإعادة
            // التنفيذ بأمان لو أُعيد تشغيل الترحيل على قاعدة نُقلت جزئيًا.
            migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM [Warehouses] WHERE [Code] = N'{MainWarehouseCode}')
BEGIN
    INSERT INTO [Warehouses] ([Code], [Name], [Address], [Phone], [ManagerName], [IsActive], [IsDefault], [CreatedAt])
    VALUES (N'{MainWarehouseCode}', N'المخزن الرئيسي', NULL, NULL, NULL, 1, 1, GETDATE());
END;");

            // (2) نقل رصيد كل منتج إلى المخزن الرئيسي.
            // نُدخل صفًا لكل منتج بما فيه أصحاب الرصيد صفر: صف الرصيد الصفري
            // يُوثِّق أن الصنف «مُخزَّن هنا وانتهى» وهو ما تعتمد عليه شاشة
            // مخزون المخزن وحد التنبيه الخاص، بخلاف غياب الصف الذي يعني
            // «الصنف لا يُخزَّن هنا أصلًا».
            migrationBuilder.Sql($@"
INSERT INTO [ProductStocks] ([ProductId], [WarehouseId], [Quantity], [LowStockThreshold], [UpdatedAt])
SELECT p.[Id], w.[Id], p.[StockQuantity], NULL, GETDATE()
FROM [Products] p
CROSS JOIN (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'{MainWarehouseCode}') w
WHERE NOT EXISTS (
    SELECT 1 FROM [ProductStocks] ps
    WHERE ps.[ProductId] = p.[Id] AND ps.[WarehouseId] = w.[Id]
);");

            // (3) إسناد الحركات والفواتير التاريخية إلى المخزن الرئيسي.
            // كل حركة وكل فاتورة سابقة حدثت فعلًا في المخزن الوحيد الذي كان
            // موجودًا، فهذا الإسناد وصف للواقع لا تخمين له.
            migrationBuilder.Sql($@"
UPDATE [StockMovements]
SET [WarehouseId] = (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'{MainWarehouseCode}')
WHERE [WarehouseId] IS NULL;");

            migrationBuilder.Sql($@"
UPDATE [Invoices]
SET [WarehouseId] = (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'{MainWarehouseCode}')
WHERE [WarehouseId] IS NULL;");

            // المرتجع يأخذ مخزن فاتورته لا المخزن الافتراضي: القاعدة في
            // ReturnService أن القطع ترجع للمخزن الذي خرجت منه. الاستنساخ من
            // الفاتورة يجعل البيانات التاريخية متسقة مع هذه القاعدة، ويظل
            // صحيحًا لو نُفِّذ الترحيل على قاعدة بها أكثر من مخزن فعلًا.
            migrationBuilder.Sql(@"
UPDATE r
SET r.[WarehouseId] = i.[WarehouseId]
FROM [Returns] r
JOIN [Invoices] i ON i.[Id] = r.[InvoiceId]
WHERE r.[WarehouseId] IS NULL;");

            // (4) ربط المستخدمين الحاليين بالمخزن الرئيسي.
            // بدون هذا يفقد كل مندوب قائم قدرته على البيع فور النشر، لأن
            // المندوب بلا مخزن مُمنوع من البيع. الأدمن يُترك NULL لأن NULL
            // عنده تعني «كل المخازن» لا «بلا مخزن».
            migrationBuilder.Sql($@"
UPDATE u
SET u.[WarehouseId] = (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'{MainWarehouseCode}')
FROM [AspNetUsers] u
WHERE u.[WarehouseId] IS NULL
  AND EXISTS (
      SELECT 1
      FROM [AspNetUserRoles] ur
      JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
      WHERE ur.[UserId] = u.[Id] AND r.[Name] <> N'Admin'
  );");

            // (5) تشديد الأعمدة بعد ملئها، ثم الفهارس، ثم المفاتيح الأجنبية.
            // الآن لا يوجد صف بقيمة NULL فالثلاثة تنجح. الفهرس يُنشأ بعد
            // التشديد لأن إنشاءه على عمود nullable ثم تعديل العمود يُجبر
            // SQL Server على إعادة بناء الفهرس بلا داعٍ.
            migrationBuilder.AlterColumn<int>(
                name: "WarehouseId",
                table: "StockMovements",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseId_ProductId_CreatedAt",
                table: "StockMovements",
                columns: new[] { "WarehouseId", "ProductId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Warehouses_WarehouseId",
                table: "StockMovements",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AlterColumn<int>(
                name: "WarehouseId",
                table: "Invoices",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WarehouseId_CreatedAt",
                table: "Invoices",
                columns: new[] { "WarehouseId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Warehouses_WarehouseId",
                table: "Invoices",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AlterColumn<int>(
                name: "WarehouseId",
                table: "Returns",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Returns_WarehouseId_CreatedAt",
                table: "Returns",
                columns: new[] { "WarehouseId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Returns_Warehouses_WarehouseId",
                table: "Returns",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Warehouses_WarehouseId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Warehouses_WarehouseId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Returns_Warehouses_WarehouseId",
                table: "Returns");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Warehouses_WarehouseId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ProductStocks");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_WarehouseId_ProductId_CreatedAt",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Returns_WarehouseId_CreatedAt",
                table: "Returns");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_WarehouseId_CreatedAt",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_WarehouseId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ReferenceAdjustmentId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ReferencePurchaseReceiptId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ReferenceTransferId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "AspNetUsers");
        }
    }
}

# معمارية النظام — Retail POS & Reporting System

نظام مبيعات (POS) وتقارير مبني على **ASP.NET Core 8 MVC + EF Core + SQL Server**، بواجهة عربية RTL كاملة.

---

## 1. نظرة عامة على الطبقات (Layered Architecture)

```
┌──────────────────────────────────────────────────────────────┐
│  Presentation — Views (Razor, RTL) + wwwroot (CSS/JS)        │
│  Areas: Admin / Agent   •   POS Modal   •   Print Views      │
├──────────────────────────────────────────────────────────────┤
│  Controllers — thin: model binding, authorization, redirects  │
│  + ApiControllers (JSON) لخدمة الـ POS بالباركود              │
├──────────────────────────────────────────────────────────────┤
│  Services (Business Logic)                                    │
│  PosService · CouponService · ReportService · AuditService     │
│  InvoiceNumberGenerator · SeedService                         │
├──────────────────────────────────────────────────────────────┤
│  Data — AppDbContext (EF Core) + Entities + Migrations        │
├──────────────────────────────────────────────────────────────┤
│  SQL Server  (LocalDB / SQL Express / Azure SQL)              │
└──────────────────────────────────────────────────────────────┘
```

**مبدأ أساسي:** كل منطق الأعمال (حساب الإجماليات، تطبيق الخصم، خصم المخزون) يقع في `Services`
داخل **transaction واحدة**. الـ Controller لا يحسب أسعارًا، والـ JavaScript لا يُوثق به أبدًا في الأسعار.

### قاعدة أمنية حاسمة
الأسعار والخصومات القادمة من المتصفح **تُتجاهل بالكامل**. الواجهة ترسل فقط:
`(Barcode أو ProductId, Quantity)` + `CouponCode` + `CustomerName`.
السيرفر يعيد قراءة الأسعار من قاعدة البيانات ويعيد الحساب. هذا يمنع تلاعب المستخدم بالسعر.

---

## 2. تصميم قاعدة البيانات (Database Schema)

### مخطط العلاقات (ERD)

```
AspNetUsers (ApplicationUser)          AspNetRoles ── AspNetUserRoles
      │  1                                 (Admin / Agent)
      │
      │ N
   Invoices ──1───N── InvoiceItems ──N───1── Products
      │ N                                        │ N
      │                                          ├──1── Brands
      ├──N──1── Coupons  (nullable)              └──1── Categories
      ├──N──1── Customers (nullable)                    │ N
      │              │ 1                                 └──1── Brands
      │ 1            │ N
      └──N── Returns ──1───N── ReturnItems ──N──1── Products
                                    │ N
                                    └──1── InvoiceItems (Restrict)

   AuditLogs (standalone, يشير للمستخدم)
   StockMovements ──N──1── Products   (ويشير اختياريًا لـ Invoice و Return)
```

**ملاحظة معمارية مهمة:** `Returns` لا تُعدّل `Invoices` ولا تحذفها —ـ
بل هي **قيد معاكس (Reverse Entry)** مرتبط بالفاتورة. والأرقام المجمّعة
(`Invoice.RefundedAmount` و `InvoiceItem.ReturnedQuantity`) تُحفظ ماديًا لا تُحسب
في كل استعلام، لأنها تُقرأ في كل صفحة تقرير وتحتاج قفلًا داخل المعاملة.

### الجداول بالتفصيل

#### `Brands` — البراندات
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK, Identity | |
| `Name` | nvarchar(100), **UNIQUE**, required | مثال: `Infinix` |
| `IsActive` | bit, default 1 | حذف منطقي/تعطيل |
| `CreatedAt` | datetime2 | |

#### `Categories` — التصنيفات
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `Name` | nvarchar(100), required | |
| `BrandId` | int, FK → Brands.Id | التصنيف **تابع** لبراند |
| `IsActive` | bit | |
| `CreatedAt` | datetime2 | |

- **UNIQUE index مركب** على `(BrandId, Name)` — نفس اسم التصنيف مسموح لبراندات مختلفة.
- `OnDelete: Restrict` — لا يمكن حذف براند به تصنيفات.

#### `Products` — المنتجات
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `Barcode` | nvarchar(64), **UNIQUE**, required | مفتاح البحث في الـ POS |
| `Name` | nvarchar(200), required | |
| `Price` | **decimal(18,2)** | سعر البيع |
| `StockQuantity` | int, default 0 | المخزون المتاح — يُخصم بالبيع و**يُزاد بالمرتجع** |
| `LowStockThreshold` | int, default 5 | تنبيه نقص المخزون |
| `BrandId` | int, FK → Brands | |
| `CategoryId` | int, FK → Categories | |
| `IsActive` | bit | |
| `CreatedAt` / `UpdatedAt` | datetime2 | |

- Index على `Barcode` (unique) وعلى `(BrandId, CategoryId)` للفلترة.
- **لماذا `decimal(18,2)` ولا `float`؟** لتجنب أخطاء التقريب في المال. `float` غير مقبول ماليًا.

#### `Coupons` — كوبونات الخصم
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `Code` | nvarchar(50), **UNIQUE**, required | يُخزن **Upper-case** للمقارنة |
| `DiscountPercentage` | decimal(5,2) | 0 → 100 |
| `IsActive` | bit | مفعل / غير مفعل |
| `StartDate` / `EndDate` | datetime2, nullable | صلاحية زمنية (توسعة) |
| `MaxUsageCount` | int, nullable | حد أقصى للاستخدام |
| `UsedCount` | int, default 0 | عدّاد الاستخدام |
| `CreatedAt` | datetime2 | |

#### `Invoices` — الفواتير
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `InvoiceNumber` | nvarchar(30), **UNIQUE** | صيغة: `INV-20260803-0001` |
| `AgentId` | nvarchar(450), FK → AspNetUsers | من باع الفاتورة |
| `CustomerName` | nvarchar(150), nullable | اختياري |
| `CustomerPhone` | nvarchar(30), nullable | اختياري |
| `SubTotal` | decimal(18,2) | الإجمالي قبل الخصم |
| `CouponId` | int, nullable, FK → Coupons | |
| `CouponCode` | nvarchar(50), nullable | **snapshot** |
| `DiscountPercentage` | decimal(5,2), default 0 | **snapshot** |
| `DiscountAmount` | decimal(18,2), default 0 | قيمة الخصم المحسوبة |
| `Total` | decimal(18,2) | الصافي = SubTotal − DiscountAmount |
| `CustomerId` | int, FK → Customers, nullable | **ربط الفاتورة برقم الهاتف** (SetNull) |
| `RefundedAmount` | decimal(18,2), default 0 | مجموع ما استُرد من هذه الفاتورة |
| `CreatedAt` | datetime2, indexed | **Index مهم جدًا للتقارير** |
| `Status` | int (enum) | Completed / PartiallyReturned / FullyReturned / Cancelled |

**مبدأ الـ Snapshot:** الفاتورة تحفظ نسخة من الأسعار ونسبة الخصم وقت البيع.
لو المدير عدّل سعر منتج غدًا، الفواتير القديمة **لا تتغير**. هذا شرط أساسي في أي نظام محاسبي.

#### `InvoiceItems` — أصناف الفاتورة
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `InvoiceId` | int, FK → Invoices, **Cascade** | |
| `ProductId` | int, FK → Products, **Restrict** | لا نحذف منتجًا مبيعًا |
| `ProductName` | nvarchar(200) | snapshot |
| `Barcode` | nvarchar(64) | snapshot |
| `UnitPrice` | decimal(18,2) | snapshot — **منه يُحسب مبلغ الاسترداد** |
| `Quantity` | int, > 0 | |
| `ReturnedQuantity` | int, default 0 | الكمية المُرجَعة من هذا السطر (يدعم الإرجاع الجزئي) |
| `LineTotal` | decimal(18,2) | UnitPrice × Quantity |

#### `Customers` — العملاء
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `Name` | nvarchar(200), nullable | اسم العميل |
| `Phone` | nvarchar(30), required | **كما كتبه المندوب** (للعرض) |
| `PhoneNormalized` | nvarchar(30), **UNIQUE** | **هوية العميل** — للمطابقة ومنع التكرار |
| `Notes` | nvarchar(500), nullable | |
| `CreatedAt` | datetime2 | |

#### `Returns` — المرتجعات (قيد معاكس)
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `ReturnNumber` | nvarchar(50), **UNIQUE** | `RET-yyyyMMdd-0001` |
| `InvoiceId` | int, FK → Invoices, **Restrict** | الفاتورة الأصلية — **لا تُعدّل** |
| `InvoiceNumber` | nvarchar(50) | snapshot |
| `ProcessedByUserId` / `ProcessedByName` | — | **مَن نفّذ الإرجاع** |
| `CustomerId` / `CustomerName` / `CustomerPhone` | — | snapshot |
| `Reason` | int (enum) | Defective / WrongItem / ChangedMind / Other |
| `Notes` | nvarchar(500), nullable | |
| `SubTotal` | decimal(18,2) | Σ LineTotal |
| `DiscountPercentage` / `DiscountAmount` | — | **منقولة من الفاتورة الأصلية** |
| `RefundAmount` | decimal(18,2) | SubTotal − DiscountAmount |
| `RestockedToInventory` | bit | هل أُعيدت الكمية للمخزون؟ |
| `CreatedAt` | datetime2, indexed | |

#### `ReturnItems` — أصناف المرتجع
| العمود | النوع | ملاحظات |
|---|---|---|
| `Id` | int, PK | |
| `ReturnId` | int, FK → Returns, **Cascade** | |
| `InvoiceItemId` | int, FK → InvoiceItems, **Restrict إلزامًا** | Cascade يُنشئ مسارات حذف متعددة ويرفضها SQL Server |
| `ProductId` | int, FK → Products, **Restrict** | |
| `ProductName` / `Barcode` / `UnitPrice` | — | snapshot |
| `Quantity` | int, > 0 | |
| `LineTotal` | decimal(18,2) | UnitPrice × Quantity |

#### `StockMovements` — حركة المخزون (تتبع)
`Id` · `ProductId` (FK) · `Change` (int، سالب للبيع و**موجب للمرتجع**) · `QuantityAfter` · `Reason` (enum: Sale/Manual/Return) · `ReferenceInvoiceId` (nullable) · **`ReferenceReturnId` (nullable)** · `UserId` · `CreatedAt`

#### `AuditLogs` — سجل العمليات
`Id` · `UserId` · `UserName` · `Action` (Create/Update/Delete/Login/Sale/**Return**) · `EntityName` · `EntityId` · `Details` (nvarchar(max)، JSON) · `IpAddress` · `CreatedAt` (indexed)

#### `AspNetUsers` (ApplicationUser) — تمديد Identity
حقول مضافة: `FullName` (nvarchar(150))، `IsActive` (bit)، `CreatedAt`.

### الفهارس المطلوبة (Indexes)
| الجدول | الفهرس | السبب |
|---|---|---|
| Products | `Barcode` UNIQUE | البحث الفوري في الـ POS — أهم فهرس في النظام |
| Invoices | `CreatedAt` | كل التقارير الزمنية |
| Invoices | `AgentId, CreatedAt` | تقارير الـ Agent |
| Invoices | `InvoiceNumber` UNIQUE | البحث بالرقم المرجعي |
| InvoiceItems | `InvoiceId` | تحميل تفاصيل الفاتورة |
| Coupons | `Code` UNIQUE | التحقق من الكوبون |
| AuditLogs | `CreatedAt` | التصفح الزمني |
| Customers | `PhoneNormalized` UNIQUE | **هوية العميل** — ربط الفواتير برقم الهاتف مع منع التكرار |
| Invoices | `CustomerId` | عرض كل فواتير العميل |
| Returns | `ReturnNumber` UNIQUE | البحث برقم المرتجع |
| Returns | `InvoiceId` | مرتجعات فاتورة معينة |
| Returns | `CreatedAt` | تقارير المرتجعات الزمنية |

### سلوك الحذف (Delete Behavior)
- `Invoice → InvoiceItems`: **Cascade** (الأصناف جزء من الفاتورة).
- `Product → InvoiceItems`: **Restrict** (منع فقدان بيانات تاريخية). الحذف في الـ UI = **Soft delete** (`IsActive = false`).
- `Brand → Categories`, `Category → Products`: **Restrict**.
- `Return → ReturnItems`: **Cascade** (أصناف المرتجع جزء منه).
- `Invoice → Returns`: **Restrict** (لا يُحذف بيع له مرتجعات).
- `Customer → Invoices`: **SetNull** (حذف العميل لا يُلغي فواتيره التاريخية).
- `InvoiceItem → ReturnItems`: **Restrict إلزامي**. لو كان `Cascade` لنشأت **مسارات
  حذف متعددة** (`Invoice → InvoiceItems → ReturnItems` و `Invoice → Returns → ReturnItems`)
  ويرفض SQL Server إنشاء القيد أصلًا.

---

## 3. البيانات الأولية (Seed Data)

| النوع | القيم |
|---|---|
| Roles | `Admin` · `Agent` |
| Admin | `admin@pos.local` / `Admin@123` |
| Agent | `agent@pos.local` / `Agent@123` |
| Brand | **`Infinix`** |
| Categories (تحت Infinix) | **`Smartwatches`** · **`Smartphones`** · **`Power banks`** |
| Products (تجريبية) | عيّنات بباركود حقيقي لكل تصنيف |
| Coupons | `WELCOME10` (10%) · `SAVE20` (20%) · `VIP25` (25%) |

الـ Seed **idempotent** — يعمل عند كل تشغيل بلا تكرار (يتحقق من الوجود قبل الإضافة).

---

## 4. منطق حساب الفاتورة (خطوة بخطوة، داخل Transaction)

```
1. BEGIN TRANSACTION
2. لكل سطر مُرسل:
     - اقرأ المنتج من DB بالـ Id/Barcode  (تجاهل أي سعر من العميل)
     - تحقق: المنتج موجود؟ IsActive؟ StockQuantity >= Quantity؟
     - LineTotal = Product.Price × Quantity
3. SubTotal = Σ LineTotal
4. إن وُجد CouponCode:
     - اقرأ الكوبون: IsActive؟ داخل النطاق الزمني؟ لم يتجاوز MaxUsage؟
     - DiscountPercentage = Coupon.DiscountPercentage
     - DiscountAmount = round(SubTotal × Pct / 100, 2)
5. Total = SubTotal − DiscountAmount
6. InvoiceNumber = INV-yyyyMMdd-#### (تسلسل يومي، داخل نفس الـ transaction)
7. إن وُجد رقم هاتف: طبّعه وابحث عن العميل أو أنشِئه، واربط Invoice.CustomerId
8. احفظ Invoice + InvoiceItems
9. اخصم StockQuantity لكل منتج + أضف StockMovement
10. Coupon.UsedCount++
11. اكتب AuditLog (Action = Sale)
12. COMMIT   (أي خطأ ⇒ ROLLBACK كامل)
```

> **ملاحظة نطاق:** أُزيلت حقول `CostPrice` و `TotalCost` و `Profit` من النطاق
> بطلب صريح — النظام يتعامل مع **سعر البيع والخصومات فقط**.

**التقريب:** يُطبَّق مرة واحدة على قيمة الخصم بـ `MidpointRounding.AwayFromZero`،
حتى لا تنشأ فروق قروش بين `SubTotal - Discount` و `Total`.

---

## 5. منطق المرتجعات وإعادة المخزون (داخل Transaction)

### المبدأ: قيد معاكس لا تعديل

الفاتورة الأصلية **لا تُمس أبدًا**. يُنشأ `Return` مستقل يحمل لقطة من كل
شيء (رقم الفاتورة، بيانات العميل، من نفّذ الإرجاع، أسماء المنتجات وأسعارها)
ليبقى الأثر المحاسبي قابلًا للمراجعة حتى لو تغيرت المنتجات لاحقًا.

```
1. BEGIN TRANSACTION
2. اقرأ الفاتورة وأصنافها **داخل** المعاملة (لا تعتمد على ما قرأته الواجهة)
3. تحقق من الملكية: هل هذه فاتورة هذا المندوب؟ (وإلا 404)
4. لكل سطر مطلوب إرجاعه:
     - Remaining = InvoiceItem.Quantity − InvoiceItem.ReturnedQuantity
     - إن كان Qty > Remaining  ⇒  رفض وROLLBACK
     - LineTotal = InvoiceItem.UnitPrice × Qty      ← سعر الفاتورة لا سعر المنتج الحالي
5. إن كان المجموع صفرًا  ⇒  رفض (لا مرتجع فارغ)
6. SubTotal       = Σ LineTotal
   DiscountAmount = round(SubTotal × Invoice.DiscountPercentage / 100, 2)
   RefundAmount   = SubTotal − DiscountAmount        ← خصم الفاتورة يُطبّق
7. تحقق أن (Invoice.RefundedAmount + RefundAmount) ≤ Invoice.Total
8. ReturnNumber = RET-yyyyMMdd-#### (تسلسل يومي)
9. احفظ Return + ReturnItems
10. إن RestockToInventory:
      - Product.StockQuantity += Qty            ← الأجهزة تُحسب في المخزون تاني
      - أضف StockMovement (Reason = Return, ReferenceInvoiceId, ReferenceReturnId)
11. InvoiceItem.ReturnedQuantity += Qty
    Invoice.RefundedAmount       += RefundAmount
12. Invoice.Status = كل الأصناف مُرجَعة؟ FullyReturned : PartiallyReturned
13. اكتب AuditLog (Action = Return)
14. COMMIT   (أي خطأ ⇒ ROLLBACK كامل — لا مرتجع ولا مخزون متغير)
```

### قرارات تصميمية حاسمة

| القرار | السبب |
|--------|--------|
| قراءة الكميات **داخل** المعاملة (خطوة 2) | يمنع الإرجاع المزدوج من نقرة مكررة أو تابين مفتوحين |
| السعر من `InvoiceItem.UnitPrice` لا من `Product.Price` | تغيير سعر المنتج لاحقًا لا يغير مبلغ الاسترداد |
| تطبيق `Invoice.DiscountPercentage` على الاسترداد | وإلا استرد العميل سعرًا كاملًا لصنف دفع فيه مخفّضًا — خسارة مباشرة |
| `ReturnedQuantity` محفوظ لا محسوب | يُقرأ في كل صفحة تقرير؛ حسابه بـ JOIN كل مرة مكلف ويمنع القفل |
| مفتاح `RestockToInventory` منفصل | الجهاز التالف يُسترد ثمنه دون أن يعود للبيع |
| الحالات أربع لا اثنتان | كل فلتر `Status == Completed` أصبح `Status != Cancelled`، وإلا **اختفت الفواتير المُرجَعة من كل التقارير بصمت** |

### تطبيع رقم الهاتف (ربط الفواتير بالعميل)

يُخزّن الرقم مرتين: `Phone` كما كُتب (للعرض)، و `PhoneNormalized` بفهرس فريد (للمطابقة):

```
أرقام عربية/فارسية → لاتينية   ؞   حذف كل ما ليس رقمًا
→ إسقاط بادئة 00 الدولية   ؞   تحويل كود مصر 20 → 0 محلي
```

لذلك `+201001234567` و `01001234567` و `0100 123 4567` و `٠١٠٠١٢٣٤٥٦٧`
كلها **عميل واحد**، والفهرس الفريد يمنع إنشاء نسخ مكررة منه.

---

## 6. التقارير (Reports)

| التقرير | المستخدم | المحتوى |
|---|---|---|
| اليوم | Admin + Agent | فواتير اليوم، الإجمالي، الخصومات، المرتجعات، الصافي |
| الأسبوع | Admin + Agent | آخر 7 أيام / الأسبوع الحالي |
| الشهر | Admin + Agent | الشهر الحالي |
| مخصص | Admin + Agent | من تاريخ → إلى تاريخ |
| شامل | Admin فقط | كل الفواتير + فلترة بالـ Agent |
| الأكثر مبيعًا | Admin | Top Products بالكمية والإيراد |
| أداء المناديب | Admin | مبيعات كل Agent |
| المخزون | Admin | المنتجات الناقصة (Low stock) |
| المرتجعات | Admin + Agent | كل عمليات الإرجاع، مَن نفّذها، وهل أُعيدت للمخزون |
| العملاء | Admin + Agent | الاسم والهاتف وعدد الفواتير وصافي المشتريات |

**أثر المرتجعات:** كل تقرير يعرض `TotalRefunded` و `NetAfterReturns` إن وُجدت
مرتجعات، والفواتير المُرجَعة **تبقى ظاهرة** بحالتها الجديدة لا تُحجب.

**عزل بيانات الـ Agent:** كل استعلامات تقارير الـ Agent مقيّدة بـ
`Where(i => i.AgentId == currentUserId)` على مستوى الـ Service، وليس على مستوى الواجهة فقط.
وينطبق المبدأ نفسه على **العملاء والمرتجعات**: المندوب يرى فقط فواتيره في صفحة
العميل، ويرى المرتجعات التي نفّذها **أو** الواقعة على فواتيره (حتى لو نفّذها مدير)
—ـ وإلا تغيرت أرقامه دون تفسير. كما أن `Details/{id}` تتحقق من الملكية وترجع `404`
لو حاول Agent فتح فاتورة غيره.

---

## 7. هيكلية المشروع (Code Structure)

```
/
├── PosSystem.sln
├── docs/
│   ├── ARCHITECTURE.md          ← هذا الملف
│   └── IMPLEMENTATION_PLAN.md   ← الخطة المرحلية
└── src/PosSystem.Web/
    ├── Program.cs                       (DI, Identity, Middleware, RTL Culture)
    ├── appsettings.json                 (ConnectionStrings)
    ├── Data/
    │   ├── AppDbContext.cs
    │   ├── DbSeeder.cs
    │   └── Migrations/
    ├── Models/
    │   ├── Entities/  (Brand, Category, Product, Coupon,
    │   │               Invoice, InvoiceItem, StockMovement,
    │   │               Customer, Return, ReturnItem,
    │   │               AuditLog, ApplicationUser)
    │   └── ViewModels/ (PosCartVm, CreateInvoiceRequest,
    │                    CustomerViewModels, ReturnViewModels,
    │                    ReportFilterVm, DashboardVm, ...)
    ├── Services/
    │   ├── IPosService.cs / PosService.cs
    │   ├── ICouponService.cs / CouponService.cs
    │   ├── IReportService.cs / ReportService.cs
    │   ├── ICustomerService.cs / CustomerService.cs   (تطبيع الهاتف + الربط)
    │   ├── IReturnService.cs / ReturnService.cs       (القيد المعاكس + المخزون)
    │   ├── IAuditService.cs / AuditService.cs
    │   └── IInvoiceNumberGenerator.cs / ...
    ├── Controllers/
    │   ├── AccountController.cs          (Login/Logout + توجيه بالـ Role)
    │   ├── HomeController.cs             (توزيع حسب الـ Role)
    │   ├── Agent/  PosController, AgentReportsController,
    │   │           CustomersController, ReturnsController
    │   ├── Admin/  BrandsController, CategoriesController,
    │   │           ProductsController, CouponsController,
    │   │           UsersController, ReportsController, AuditController
    │   └── Api/    PosApiController      (JSON: lookup barcode, validate coupon, checkout)
    ├── Views/
    │   ├── Shared/  _Layout.cshtml (RTL), _AdminSidebar, _Pagination
    │   ├── Pos/     Index.cshtml (3 مربعات) + _PosModal.cshtml + Receipt.cshtml (thermal)
    │   ├── Customers/ Index.cshtml + Details.cshtml
    │   ├── Returns/   Index.cshtml + Create.cshtml + List.cshtml + Details.cshtml
    │   └── ...
    └── wwwroot/
        ├── css/  site.css  (تصميم مخصص RTL + Print CSS 80mm)
        └── js/   pos.js    (Barcode listener + Cart state + AJAX + إكمال العميل)
```

**لماذا مشروع واحد وليس عدة مشاريع (Clean Architecture متعددة)؟**
حجم النظام متوسط، ومشروع واحد بمجلدات واضحة يظل مقروءًا وسريع البناء.
الفصل الفعلي محقَّق عبر **طبقة Services** — يمكن استخراجها لمشروع منفصل لاحقًا دون إعادة كتابة.

---

## 8. توصيات الـ UI/UX ومكتبات الواجهة

| الاحتياج | الاختيار | السبب |
|---|---|---|
| الشبكة و RTL | **Bootstrap 5.3 RTL** (`bootstrap.rtl.min.css`) | RTL رسمي مدمج، لا حاجة لـ patches |
| النوافذ المنبثقة | **Bootstrap Modal** (fullscreen على الموبايل) | لا اعتماد إضافي؛ متكامل مع الـ RTL |
| التنبيهات | **SweetAlert2** + Toasts | تأكيد الحذف وإتمام البيع بشكل واضح |
| الجداول | جداول سيرفر-سايد + Pagination | أسرع وأدق من DataTables على بيانات كبيرة |
| الخط العربي | **Cairo / Tajawal** (Google Fonts) | وضوح عالٍ للأرقام والعربية |
| الأيقونات | Bootstrap Icons | خفيفة، بلا JS |
| الطباعة | CSS `@media print` بعرض 80mm | يعمل مع الطابعات الحرارية مباشرة بلا مكتبة |
| PDF | طباعة المتصفح → PDF | أخف من QuestPDF؛ QuestPDF تُضاف لاحقًا لو لزم إرسال PDF بالبريد |

### مبادئ UX في الـ POS (نقاط حرجة للسرعة)
1. **التركيز التلقائي (Autofocus)** على حقل الباركود دائمًا، ويُعاد التركيز بعد كل عملية.
2. **مستمع الباركود العام:** يلتقط الإدخال السريع من الـ Scanner ولو لم يكن الحقل مُركّزًا
   (الـ Scanner يكتب بسرعة > بشرية وينتهي بـ `Enter` — نميّزه بفارق زمني بين الضغطات).
3. **اختصارات لوحة المفاتيح:** `F2` فاتورة جديدة · `F9` إتمام البيع · `Esc` إغلاق/إلغاء.
4. **تغذية راجعة فورية:** صوت نجاح/خطأ + وميض السطر المُضاف + إذا كان المنتج موجودًا تُزاد كميته بدل تكراره.
5. **بلا إعادة تحميل للصفحة:** كل السلة في الـ JS، والحساب النهائي على السيرفر.
6. **أزرار كبيرة (≥ 48px)** ملائمة لشاشات اللمس في الكاشير.
7. **حماية من الإرسال المزدوج:** تعطيل زر الإتمام أثناء الطلب + مفتاح Idempotency.

---

## 9. الأمان (Security Checklist)
- تشفير كلمات المرور عبر Identity (PBKDF2) + سياسة قوة كلمة المرور.
- `[Authorize(Roles = "Admin")]` على كل وحدات الإدارة، و `[Authorize]` على الـ POS.
- **مضاد CSRF** (`[ValidateAntiForgeryToken]`) على كل POST، بما فيها طلبات الـ AJAX (تُرسل التوكن في الهيدر).
- الأسعار **لا تُقرأ أبدًا** من العميل (مذكور في §1).
- التحقق من الملكية في `Reports/Details` قبل عرض أي فاتورة.
- الحماية من SQL Injection تلقائية عبر EF Core (parameterized).
- Cookie: `HttpOnly` + `SameSite=Lax` + `Secure` في الإنتاج.
- سلسلة الاتصال تُقرأ من User Secrets / متغيرات البيئة في الإنتاج، لا من الملف.

---

## 10. الاتصال بقاعدة البيانات (SQL Server)
```jsonc
"ConnectionStrings": {
  // Windows / LocalDB
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=PosSystemDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"

  // SQL Server / Docker / Linux
  // "DefaultConnection": "Server=localhost,1433;Database=PosSystemDb;User Id=sa;Password=Your_Pass123;TrustServerCertificate=True"
}
```
التهيئة: `EnableRetryOnFailure()` لمرونة الشبكة، و`MigrateAsync()` + Seed عند بدء التشغيل.

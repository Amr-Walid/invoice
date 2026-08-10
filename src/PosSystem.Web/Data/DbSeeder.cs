using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Data;

/// <summary>
/// تهيئة البيانات الأولية. العملية idempotent — تعمل عند كل تشغيل بلا تكرار.
/// </summary>
public static class DbSeeder
{
    public const string AdminEmail = "admin@pos.local";
    public const string AgentEmail = "agent@pos.local";
    public const string KeeperEmail = "keeper@pos.local";

    // كلمات سر التطوير فقط. في الإنتاج تُقرأ من الإعدادات/متغيّرات البيئة
    // (Seed:AdminPassword …)، ويرفض النظام الإقلاع لو تُركت الافتراضية.
    // انظر ResolvePassword أدناه.
    public const string DefaultAdminPassword = "Admin@123";
    public const string DefaultAgentPassword = "Agent@123";
    public const string DefaultKeeperPassword = "Keeper@123";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var env = sp.GetRequiredService<IHostEnvironment>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        // الهجرات (Migrations) مُولَّدة لـ SQL Server وهو المزوّد الأساسي للنظام.
        // في حالة Sqlite (بيئة تجربة بدون SQL Server) نُنشئ المخطط مباشرة،
        // لتجنّب الحاجة لمجموعتَي هجرات متعارضتين لنفس الـ DbContext.
        if (db.Database.IsSqlite())
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        // Seed:Enabled=false يوقف التهيئة كليًا بعد أول إقلاع ناجح في الإنتاج
        // إن رغب العميل — القاعدة عندها تكون قائمة بحساباتها الحقيقية.
        if (!config.GetValue("Seed:Enabled", true))
        {
            logger.LogInformation("التهيئة الأولية مُعطَّلة (Seed:Enabled=false).");
            return;
        }

        // البيانات التجريبية (منتجات Infinix والكوبونات) مفيدة للتجربة وكارثية
        // في الإنتاج: العميل يفتح النظام فيجد أصنافًا ليست له وكوبونات خصم
        // فعّالة يمكن استخدامها في بيع حقيقي. الافتراضي: مُفعَّلة خارج الإنتاج فقط.
        var demoData = config.GetValue("Seed:DemoData", !env.IsProduction());

        await SeedRolesAsync(roleManager);
        // المخزن قبل المستخدمين وقبل المنتجات: المندوب يُربط بمخزن
        // والمنتج يحتاج مخزنًا يودِع فيه رصيده الافتتاحي.
        var mainWarehouse = await SeedWarehouseAsync(db);
        await SeedUsersAsync(userManager, logger, config, env, mainWarehouse);

        if (demoData)
        {
            await SeedCatalogAsync(db);
            await SeedCouponsAsync(db);
        }
        else
        {
            // العلامة التجارية والتصنيفات مطلوبة في المتطلبات وليست «بيانات
            // تجريبية»: شاشة إضافة منتج تحتاج تصنيفًا موجودًا وإلا تعذّر على
            // العميل إدخال أول صنف له. أما المنتجات والكوبونات فلا تُنشأ.
            await SeedBrandAndCategoriesAsync(db);
            logger.LogInformation("وضع الإنتاج: لم تُدرج منتجات ولا كوبونات تجريبية.");
        }

        // بعد المنتجات: مزامنة أرصدة المخزن مع الكاش القديم
        await SeedProductStocksAsync(db, mainWarehouse, logger);

        await db.SaveChangesAsync();
        logger.LogInformation("تمت تهيئة البيانات الأولية بنجاح.");
    }

    /// <summary>
    /// يحدّد كلمة سر حساب التهيئة. في الإنتاج لا يُسمح بالافتراضية إطلاقًا:
    /// حساب مدير بكلمة سر منشورة في مستودع عام = النظام مفتوح للجميع.
    /// نُفشل الإقلاع بدل أن نكتب في السجل تحذيرًا لا يقرأه أحد.
    /// </summary>
    private static string ResolvePassword(
        IConfiguration config, IHostEnvironment env, string key, string devDefault)
    {
        var configured = config[$"Seed:{key}"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        if (env.IsProduction())
        {
            throw new InvalidOperationException(
                $"كلمة سر حساب التهيئة غير مضبوطة في الإنتاج. " +
                $"اضبط متغيّر البيئة Seed__{key} بكلمة سر قوية قبل التشغيل. " +
                $"(الافتراضية معروفة للجميع ولا يُسمح بها في الإنتاج.)");
        }

        return devDefault;
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    /// <summary>
    /// يضمن وجود مخزن افتراضي واحد. لازم هنا لا في الهجرة وحدها لأن
    /// مسار Sqlite يستخدم EnsureCreatedAsync فتُتجاوز الهجرات بالكامل،
    /// ولأن قاعدة جديدة تمامًا لا تمر على عبارات النقل ولا تحتاجها.
    /// </summary>
    private static async Task<Warehouse> SeedWarehouseAsync(AppDbContext db)
    {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Code == "MAIN");
        if (warehouse is null)
        {
            warehouse = new Warehouse
            {
                Code = "MAIN",
                Name = "المخزن الرئيسي",
                ManagerName = "مدير النظام",
                IsActive = true,
                IsDefault = true
            };
            db.Warehouses.Add(warehouse);
            await db.SaveChangesAsync();
        }

        // حراسة قاعدة «افتراضي واحد بالضبط» عند الإقلاع — لو أُفسدت العلامة
        // بتعديل مباشر على قاعدة البيانات يُصحَّح الوضع تلقائيًا لا أن يعمل
        // النظام بمرجع عشوائي.
        var defaults = await db.Warehouses.Where(w => w.IsDefault).ToListAsync();
        if (defaults.Count != 1 || defaults[0].Id != warehouse.Id)
        {
            foreach (var other in defaults.Where(w => w.Id != warehouse.Id))
                other.IsDefault = false;
            warehouse.IsDefault = true;
            await db.SaveChangesAsync();
        }

        return warehouse;
    }

    /// <summary>
    /// يضمن أن لكل منتج صف رصيد في المخزن الرئيسي، وأن الكاش
    /// <c>Product.StockQuantity</c> يساوي مجموع المخازن فعلًا.
    ///
    /// <para>يخدم حالتين: (1) قاعدة Sqlite جديدة أُنشئت بـ EnsureCreated فلم تمر
    /// على عبارات نقل الهجرة؛ (2) منتجات أُضيفت قبل وجود المخازن.</para>
    ///
    /// <para>المنتج الذي له أصلًا صفوف رصيد لا يُمس — لأن إعادة الإيداع
    /// عند كل إقلاع ستُضاعف المخزون من فراغ. ولذلك أيضًا لا تُسجَّل هنا
    /// حركة StockMovement: التهيئة ليست عملية تجارية تُتتبَّع.</para>
    /// </summary>
    private static async Task SeedProductStocksAsync(
        AppDbContext db, Warehouse warehouse, ILogger logger)
    {
        var productIdsWithStock = await db.ProductStocks
            .Select(s => s.ProductId)
            .Distinct()
            .ToListAsync();

        var missing = await db.Products
            .Where(p => !productIdsWithStock.Contains(p.Id))
            .Select(p => new { p.Id, p.StockQuantity })
            .ToListAsync();

        if (missing.Count > 0)
        {
            foreach (var product in missing)
            {
                db.ProductStocks.Add(new ProductStock
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    Quantity = product.StockQuantity,
                    UpdatedAt = DateTime.Now
                });
            }
            await db.SaveChangesAsync();

            logger.LogInformation(
                "تم إيداع رصيد {Count} منتج في مخزن {Warehouse}",
                missing.Count, warehouse.Name);
        }

        // مطابقة الكاش: أي انحراف يُصحَّح ويُسجَّل بتحذير، لأن انحرافًا
        // هنا يعني أن كودًا ما كتب في المخزون دون IInventoryService.
        var totals = await db.ProductStocks
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(x => x.Quantity) })
            .ToListAsync();

        var totalsMap = totals.ToDictionary(t => t.ProductId, t => t.Total);
        var drifted = 0;

        foreach (var product in await db.Products.ToListAsync())
        {
            var actual = totalsMap.TryGetValue(product.Id, out var total) ? total : 0;
            if (product.StockQuantity == actual) continue;

            product.StockQuantity = actual;
            drifted++;
        }

        if (drifted > 0)
        {
            await db.SaveChangesAsync();
            logger.LogWarning(
                "تم تصحيح كاش المخزون لـ {Count} منتج لعدم مطابقته مجموع المخازن", drifted);
        }
    }

    private static async Task SeedUsersAsync(
        UserManager<ApplicationUser> userManager, ILogger logger,
        IConfiguration config, IHostEnvironment env, Warehouse mainWarehouse)
    {
        // الأدمن بلا مخزن (null = كل المخازن)، والمندوب مربوط بالرئيسي
        // وإلا لما قدر على البيع.
        await EnsureUserAsync(userManager, logger, AdminEmail,
            ResolvePassword(config, env, "AdminPassword", DefaultAdminPassword),
            "مدير النظام", AppRoles.Admin, warehouseId: null);
        await EnsureUserAsync(userManager, logger, AgentEmail,
            ResolvePassword(config, env, "AgentPassword", DefaultAgentPassword),
            "مندوب المبيعات", AppRoles.Agent, warehouseId: mainWarehouse.Id);
        await EnsureUserAsync(userManager, logger, KeeperEmail,
            ResolvePassword(config, env, "KeeperPassword", DefaultKeeperPassword),
            "أمين المخزن", AppRoles.WarehouseKeeper, warehouseId: mainWarehouse.Id);
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager, ILogger logger,
        string email, string password, string fullName, string role, int? warehouseId)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
                WarehouseId = warehouseId
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("فشل إنشاء المستخدم {Email}: {Errors}", email,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
                return;
            }
        }
        else if (warehouseId is not null && user.WarehouseId is null)
        {
            // حساب قائم من قبل المخازن: نربطه مرة واحدة فقط.
            // لا نُعيد الربط لو نقله المدير لاحقًا لمخزن آخر، فقرار المدير
            // أعلى من التهيئة ولا يجوز للتهيئة أن تنقضه عند كل إقلاع.
            user.WarehouseId = warehouseId;
            await userManager.UpdateAsync(user);
        }

        if (!await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);
    }

    /// <summary>
    /// البيانات المطلوبة افتراضيًا: البراند Infinix
    /// والتصنيفات التابعة Smartwatches / Smartphones / Power banks
    /// </summary>
    /// <summary>
    /// العلامة «Infinix» وتصنيفاتها الثلاثة — مطلوبة في المتطلبات، ويحتاجها
    /// نموذج إضافة المنتج (لا يمكن حفظ صنف بلا تصنيف). لذلك تُنشأ حتى في
    /// الإنتاج حيث تُمنع المنتجات والكوبونات التجريبية.
    /// </summary>
    private static async Task<Brand> SeedBrandAndCategoriesAsync(AppDbContext db)
    {
        var infinix = await db.Brands.FirstOrDefaultAsync(b => b.Name == "Infinix");
        if (infinix is null)
        {
            infinix = new Brand { Name = "Infinix", IsActive = true };
            db.Brands.Add(infinix);
            await db.SaveChangesAsync();
        }

        var categoryNames = new[] { "Smartwatches", "Smartphones", "Power banks" };
        foreach (var name in categoryNames)
        {
            if (!await db.Categories.AnyAsync(c => c.BrandId == infinix.Id && c.Name == name))
                db.Categories.Add(new Category { Name = name, BrandId = infinix.Id, IsActive = true });
        }
        await db.SaveChangesAsync();

        return infinix;
    }

    private static async Task SeedCatalogAsync(AppDbContext db)
    {
        var infinix = await SeedBrandAndCategoriesAsync(db);

        // منتجات تجريبية — فقط إن كانت قاعدة البيانات فارغة من المنتجات
        if (!await db.Products.AnyAsync())
        {
            var watches = await db.Categories.FirstAsync(c => c.Name == "Smartwatches" && c.BrandId == infinix.Id);
            var phones = await db.Categories.FirstAsync(c => c.Name == "Smartphones" && c.BrandId == infinix.Id);
            var banks = await db.Categories.FirstAsync(c => c.Name == "Power banks" && c.BrandId == infinix.Id);

            db.Products.AddRange(
                NewProduct("6941238701234", "Infinix Smart 8 - 128GB", 4200m, 25, infinix.Id, phones.Id),
                NewProduct("6941238701241", "Infinix Hot 40i - 256GB", 6350m, 18, infinix.Id, phones.Id),
                NewProduct("6941238701258", "Infinix Note 40 Pro", 11900m, 10, infinix.Id, phones.Id),
                NewProduct("6941238702231", "Infinix Watch XW1", 1450m, 30, infinix.Id, watches.Id),
                NewProduct("6941238702248", "Infinix Smart Watch Pro", 2300m, 12, infinix.Id, watches.Id),
                NewProduct("6941238703221", "Infinix Power Bank 10000mAh", 650m, 40, infinix.Id, banks.Id),
                NewProduct("6941238703238", "Infinix Power Bank 20000mAh", 1150m, 22, infinix.Id, banks.Id),
                NewProduct("6941238703245", "Infinix Fast Charger 45W", 480m, 4, infinix.Id, banks.Id)
            );
            await db.SaveChangesAsync();
        }
    }

    private static Product NewProduct(string barcode, string name, decimal price,
        int stock, int brandId, int categoryId) => new()
        {
            Barcode = barcode,
            Name = name,
            Price = price,
            StockQuantity = stock,
            LowStockThreshold = 5,
            BrandId = brandId,
            CategoryId = categoryId,
            IsActive = true
        };

    private static async Task SeedCouponsAsync(AppDbContext db)
    {
        var coupons = new (string Code, decimal Pct)[]
        {
            ("WELCOME10", 10m),
            ("SAVE20", 20m),
            ("VIP25", 25m)
        };

        foreach (var (code, pct) in coupons)
        {
            if (!await db.Coupons.AnyAsync(c => c.Code == code))
                db.Coupons.Add(new Coupon { Code = code, DiscountPercentage = pct, IsActive = true });
        }
        await db.SaveChangesAsync();
    }
}

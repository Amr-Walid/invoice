using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// قاعدة البيانات
// المزوّد الافتراضي هو SQL Server كما في المتطلبات.
// يمكن التبديل إلى Sqlite عبر Database:Provider لتشغيل النظام
// في بيئات لا يتوفر بها SQL Server (تجربة / CI).
// ============================================================
var provider = builder.Configuration.GetValue<string>("Database:Provider") ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("سلسلة الاتصال 'DefaultConnection' غير مُعرّفة.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString, sql =>
            sql.MigrationsAssembly("PosSystem.Web"));
    }
    else
    {
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            sql.MigrationsAssembly("PosSystem.Web");
        });
    }
});

// ============================================================
// الهوية والأدوار
// ============================================================
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    // يُصدر مطالبة FullName حتى يظهر الاسم العربي الحقيقي في «سجل العمليات»
    // بدلًا من البريد الإلكتروني (انظر AppClaimsPrincipalFactory).
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ============================================================
// الخدمات
// ============================================================
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ICouponService, CouponService>();
builder.Services.AddScoped<IPosService, PosService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IReturnService, ReturnService>();
// ⭐ نقطة العبور الوحيدة لكل تغيير في المخزون — كل خدمة تحرّك كمية تعتمد عليها
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
// دورة حياة التحويل بين المخازن: طلب → اعتماد → شحن (خصم) → استلام (إضافة)
builder.Services.AddScoped<ITransferService, TransferService>();
// المشتريات: المورد، ثم دورة الأمر (مسودة → اعتماد → استلام بدفعات)
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
// مُولِّد باركود CODE-128 بصيغة SVG — بلا حالة، لذا Singleton
builder.Services.AddSingleton<IBarcodeService, BarcodeService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// ============================================================
// التوطين: عرض الأرقام والتواريخ بصيغة ثابتة (أرقام لاتينية)
// مع واجهة عربية RTL — أوضح للتعامل مع الأسعار والباركود.
// ============================================================
var culture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ============================================================
// تهيئة قاعدة البيانات والبيانات الأولية
// ============================================================
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await DbSeeder.SeedAsync(app.Services);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "فشل تهيئة قاعدة البيانات. تحقق من سلسلة الاتصال.");
    }
}

app.Run();

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PosSystem.Web.Data;

namespace PosSystem.Web.Services;

/// <summary>
/// فحص صحة قاعدة البيانات لنقطة <c>/health</c>.
///
/// <para>يتحقق من أن الاتصال بالقاعدة قائم فعلًا لا من أن العملية حيّة فقط:
/// تطبيق يعمل بقاعدة مقطوعة يبدو «سليمًا» لموازِن الأحمال فيستمر في استقبال
/// المستخدمين، ثم يفشل عند أول عملية بيع. الفحص هنا يجعل الانقطاع ظاهرًا.</para>
///
/// <para>كُتب يدويًا بدل حزمة <c>AspNetCore.HealthChecks.EntityFrameworkCore</c>
/// لتجنّب اعتماد خارجي من أجل استعلام واحد.</para>
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // CanConnectAsync يفتح اتصالًا حقيقيًا ويغلقه — أرخص من أي استعلام
            // على جدول، ولا يتأثر بكون القاعدة فارغة أو ممتلئة.
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("الاتصال بقاعدة البيانات سليم")
                : HealthCheckResult.Unhealthy("تعذّر الاتصال بقاعدة البيانات");
        }
        catch (Exception ex)
        {
            // نص الاستثناء قد يحوي سلسلة الاتصال، ونقطة /health مفتوحة بلا
            // مصادقة — لذا لا نُعيده للمستجيب. يُسجَّل داخليًا فقط عبر
            // الاستثناء المرفق الذي لا يظهر في الاستجابة الافتراضية.
            return HealthCheckResult.Unhealthy("تعذّر الاتصال بقاعدة البيانات", ex);
        }
    }
}

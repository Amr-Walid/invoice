using System.Security.Claims;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Services;

public interface ICurrentUserService
{
    string? UserId { get; }
    string DisplayName { get; }
    string? IpAddress { get; }
    bool IsAdmin { get; }
    bool IsAgent { get; }
    bool IsWarehouseKeeper { get; }
}

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// الاسم المعروض في «سجل العمليات». مصدره مطالبة FullName التي يُصدرها
    /// <see cref="AppClaimsPrincipalFactory"/>. نتجنّب استعلام قاعدة البيانات
    /// في كل عملية تدقيق لأن ذلك يُضيف رحلة I/O لكل طلب بلا فائدة.
    /// نُفلتر الفراغ صراحةً: FindFirstValue يُرجع "" (لا null) لمطالبة فارغة،
    /// و ?? وحده لا يلتقط ذلك فيُخزَّن اسم فارغ في السجل.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var fullName = User?.FindFirstValue(AppClaimsPrincipalFactory.FullNameClaim);
            if (!string.IsNullOrWhiteSpace(fullName)) return fullName;

            var name = User?.Identity?.Name;
            return string.IsNullOrWhiteSpace(name) ? "نظام" : name;
        }
    }

    public string? IpAddress => _accessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public bool IsAdmin => User?.IsInRole(AppRoles.Admin) ?? false;
    public bool IsAgent => User?.IsInRole(AppRoles.Agent) ?? false;
    public bool IsWarehouseKeeper => User?.IsInRole(AppRoles.WarehouseKeeper) ?? false;
}

using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Services;

/// <summary>
/// يضيف مطالبة (claim) باسم <c>FullName</c> إلى تذكرة الهوية عند تسجيل الدخول.
///
/// لماذا؟ ASP.NET Core Identity افتراضيًا لا يُصدر إلا
/// <c>NameIdentifier</c> و <c>Name</c> (= اسم المستخدم/البريد) والأدوار.
/// كان <see cref="CurrentUserService.DisplayName"/> يقرأ مطالبة "FullName"
/// غير موجودة أصلًا فيسقط على البريد الإلكتروني، فكان «سجل العمليات»
/// يعرض <c>agent@pos.local</c> بدلًا من الاسم العربي الحقيقي للمستخدم.
///
/// إصدار المطالبة هنا يجعل الاسم متاحًا من الكوكيز بلا أي استعلام قاعدة بيانات
/// في كل طلب — وهذا هو السبب الجوهري لاختيار المطالبات على القراءة المباشرة.
///
/// ملاحظة: تُحدَّث المطالبة عند تسجيل دخول جديد فقط. عند تعديل اسم مستخدم
/// نستدعي <c>SecurityStampValidator</c> ضمنيًا عبر تحديث SecurityStamp
/// (انظر UsersController) فتُعاد بناء التذكرة تلقائيًا.
/// </summary>
public class AppClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public const string FullNameClaim = "FullName";

    public AppClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrWhiteSpace(user.FullName))
            identity.AddClaim(new Claim(FullNameClaim, user.FullName));

        return identity;
    }
}

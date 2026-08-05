using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Models.ViewModels;

namespace PosSystem.Web.Services;

public interface ICouponService
{
    Task<CouponValidationResult> ValidateAsync(string? code);
    Task<Coupon?> GetValidCouponAsync(string? code);
}

public class CouponService : ICouponService
{
    private readonly AppDbContext _db;

    public CouponService(AppDbContext db) => _db = db;

    /// <summary>
    /// التحقق من الكوبون مع رسالة عربية واضحة لكل حالة رفض.
    /// المقارنة تتم بحروف كبيرة لتجاهل حالة الأحرف.
    /// </summary>
    public async Task<CouponValidationResult> ValidateAsync(string? code)
    {
        var normalized = Normalize(code);
        if (string.IsNullOrEmpty(normalized))
            return new CouponValidationResult { IsValid = false, Message = "من فضلك أدخل كود الخصم" };

        var coupon = await _db.Coupons.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == normalized);

        if (coupon is null)
            return new CouponValidationResult { IsValid = false, Message = "كود الخصم غير موجود" };

        if (!coupon.IsActive)
            return new CouponValidationResult { IsValid = false, Message = "كود الخصم غير مفعل" };

        var now = DateTime.Now;
        if (coupon.StartDate.HasValue && now.Date < coupon.StartDate.Value.Date)
            return new CouponValidationResult { IsValid = false, Message = "كود الخصم لم يبدأ بعد" };

        if (coupon.EndDate.HasValue && now.Date > coupon.EndDate.Value.Date)
            return new CouponValidationResult { IsValid = false, Message = "كود الخصم منتهي الصلاحية" };

        if (coupon.MaxUsageCount.HasValue && coupon.UsedCount >= coupon.MaxUsageCount.Value)
            return new CouponValidationResult { IsValid = false, Message = "تم استهلاك الحد الأقصى لاستخدام هذا الكود" };

        return new CouponValidationResult
        {
            IsValid = true,
            CouponId = coupon.Id,
            Code = coupon.Code,
            DiscountPercentage = coupon.DiscountPercentage,
            Message = $"تم تطبيق خصم {coupon.DiscountPercentage:0.##}%"
        };
    }

    /// <summary>يرجع الكوبون كـ tracked entity (لتحديث UsedCount) أو null إن كان غير صالح</summary>
    public async Task<Coupon?> GetValidCouponAsync(string? code)
    {
        var normalized = Normalize(code);
        if (string.IsNullOrEmpty(normalized)) return null;

        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == normalized);
        return coupon is not null && coupon.IsValidAt(DateTime.Now) ? coupon : null;
    }

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}

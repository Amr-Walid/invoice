using System.ComponentModel.DataAnnotations;

namespace PosSystem.Web.Models.Entities;

/// <summary>كوبون خصم بنسبة مئوية</summary>
public class Coupon
{
    public int Id { get; set; }

    [Required(ErrorMessage = "كود الخصم مطلوب")]
    [StringLength(50, ErrorMessage = "الكود لا يزيد عن 50 حرف")]
    [Display(Name = "كود الخصم")]
    public string Code { get; set; } = string.Empty;

    [Range(0.01, 100, ErrorMessage = "نسبة الخصم يجب أن تكون بين 0.01 و 100")]
    [Display(Name = "نسبة الخصم %")]
    public decimal DiscountPercentage { get; set; }

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    [DataType(DataType.Date)]
    [Display(Name = "تاريخ البداية")]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "تاريخ الانتهاء")]
    public DateTime? EndDate { get; set; }

    [Display(Name = "أقصى عدد استخدامات")]
    [Range(1, int.MaxValue, ErrorMessage = "عدد الاستخدامات يجب أن يكون أكبر من صفر")]
    public int? MaxUsageCount { get; set; }

    [Display(Name = "عدد مرات الاستخدام")]
    public int UsedCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    /// <summary>
    /// التحقق من صلاحية الكوبون في لحظة معينة.
    /// يُستخدم في العرض فقط؛ التحقق المُلزم يحدث في CouponService على السيرفر.
    /// </summary>
    public bool IsValidAt(DateTime moment)
    {
        if (!IsActive) return false;
        if (StartDate.HasValue && moment.Date < StartDate.Value.Date) return false;
        if (EndDate.HasValue && moment.Date > EndDate.Value.Date) return false;
        if (MaxUsageCount.HasValue && UsedCount >= MaxUsageCount.Value) return false;
        return true;
    }
}

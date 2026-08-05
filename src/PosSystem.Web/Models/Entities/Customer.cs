using System.ComponentModel.DataAnnotations;
using System.Text;

namespace PosSystem.Web.Models.Entities;

/// <summary>
/// العميل. رقم الهاتف هو المُعرِّف الحقيقي للعميل في النظام — فالعميل
/// قد يُدخل اسمه بأشكال مختلفة، لكن رقمه ثابت.
///
/// لذلك نحفظ الرقم مرتين:
///  • <see cref="Phone"/> كما كتبه المستخدم — للعرض والاتصال.
///  • <see cref="PhoneNormalized"/> بعد التوحيد — للمطابقة والفهرس الفريد.
/// هذا يمنع تكرار العميل نفسه لمجرد أنه كُتب «0100 123 4567» مرة
/// و «+201001234567» مرة أخرى.
/// </summary>
public class Customer
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم العميل مطلوب")]
    [StringLength(150, ErrorMessage = "اسم العميل لا يزيد عن 150 حرف")]
    [Display(Name = "اسم العميل")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [StringLength(30, ErrorMessage = "رقم الهاتف لا يزيد عن 30 حرف")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    /// <summary>الرقم بعد التوحيد — عليه الفهرس الفريد، ولا يُحرَّر يدويًا</summary>
    [StringLength(30)]
    public string PhoneNormalized { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "الملاحظات لا تزيد عن 500 حرف")]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [StringLength(200, ErrorMessage = "العنوان لا يزيد عن 200 حرف")]
    [Display(Name = "العنوان")]
    public string? Address { get; set; }

    [Display(Name = "مفعل")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "تاريخ الإضافة")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<Return> Returns { get; set; } = new List<Return>();
}

/// <summary>
/// توحيد أرقام الهواتف. الهدف أن يُطابق الرقم نفسه أيًّا كانت طريقة كتابته:
/// مسافات، شرطات، أقواس، صيغة دولية، أو أصفار بادئة للاتصال الدولي.
///
/// القواعد مُرتَّبة ومقصودة:
///  1. تُحوَّل الأرقام العربية (٠١٢…) إلى لاتينية — لوحات المفاتيح العربية شائعة.
///  2. يُحتفظ بالأرقام فقط (وتُهمل + و - و ( ) والمسافات).
///  3. بادئة الاتصال الدولي «00» تُحذف.
///  4. كود مصر «20» يُحوَّل إلى الصيغة المحلية بصفر بادئ،
///     فيتطابق «+201001234567» مع «01001234567» وهما رقم واحد فعلًا.
/// </summary>
public static class PhoneHelper
{
    private const string EgyptCountryCode = "20";

    /// <summary>يُعيد الصيغة الموحَّدة، أو نصًا فارغًا إن لم يكن هناك رقم صالح.</summary>
    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

        var sb = new StringBuilder(phone.Length);
        foreach (var ch in phone)
        {
            // الأرقام العربية والفارسية تُحوَّل لِما يقابلها لاتينيًا
            var c = ch switch
            {
                >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
                _ => ch
            };
            if (c is >= '0' and <= '9') sb.Append(c);
        }

        var digits = sb.ToString();
        if (digits.Length == 0) return string.Empty;

        // بادئة الاتصال الدولي
        while (digits.StartsWith("00", StringComparison.Ordinal) && digits.Length > 2)
            digits = digits[2..];

        // كود مصر → صيغة محلية بصفر بادئ
        if (digits.Length > EgyptCountryCode.Length &&
            digits.StartsWith(EgyptCountryCode, StringComparison.Ordinal) &&
            !digits.StartsWith("200", StringComparison.Ordinal))
        {
            var local = digits[EgyptCountryCode.Length..];
            digits = local.StartsWith('0') ? local : "0" + local;
        }

        // حدّ الحفظ في قاعدة البيانات
        return digits.Length > 30 ? digits[..30] : digits;
    }

    /// <summary>هل الرقم يحتوي عددًا معقولًا من الأرقام؟ (يمنع «123» أو مسافات فقط)</summary>
    public static bool IsPlausible(string? phone)
    {
        var normalized = Normalize(phone);
        return normalized.Length is >= 7 and <= 30;
    }
}

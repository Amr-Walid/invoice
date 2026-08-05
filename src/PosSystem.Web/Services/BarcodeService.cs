using System.Text;

namespace PosSystem.Web.Services;

/// <summary>
/// مُولِّد باركود CODE-128 بصيغة SVG — بلا أي مكتبة خارجية.
///
/// لماذا CODE-128 و لماذا SVG؟
///  • CODE-128 يقبل أي طول وأي حرف ASCII (أرقام/حروف/رموز)، بخلاف EAN-13
///    الذي يفرض 13 رقمًا مع خانة تحقّق صحيحة. باركودات المنتجات هنا قد تكون
///    أي نص، فـ CODE-128 هو الخيار العملي الوحيد.
///  • SVG متجهي: يُطبع بدقة الطابعة الحقيقية (600/1200dpi) لا بدقة الشاشة،
///    وهذا فرقٌ حاسم في قابلية القراءة بالماسح. صورة PNG بدقة الشاشة تُنتج
///    أشرطة مُهترئة (aliasing) يرفضها الماسح كثيرًا.
///
/// تنفيذ متوافق مع مواصفة CODE-128 (ISO/IEC 15417):
///   START (B أو C) + بيانات + خانة تحقّق (mod 103) + STOP.
/// نستخدم Code Set B افتراضيًا لأن Code Set C يُزاوج الأرقام
/// وقد يحتاج حرف حشو صفري في الأطوال الفردية.
/// </summary>
public interface IBarcodeService
{
    /// <summary>هل يمكن ترميز هذا النص بـ CODE-128 (كل محارفه ASCII 32..126)؟</summary>
    bool CanEncode(string? value);

    /// <summary>
    /// يُنتج عنصر SVG كامل للباركود.
    /// </summary>
    /// <param name="value">النص المُراد ترميزه (الباركود).</param>
    /// <param name="moduleWidth">عرض الوحدة الأصغر بالـ mm. 0.33mm ≈ معيار جيد للطابعات الحرارية.</param>
    /// <param name="heightMm">ارتفاع الأشرطة بالـ mm.</param>
    /// <param name="showText">هل نطبع النص المقروء بشريًا تحت الأشرطة؟</param>
    string ToSvg(string value, double moduleWidth = 0.33, double heightMm = 14, bool showText = true);
}

public class BarcodeService : IBarcodeService
{
    // أنماط الأشرطة لكل رمز من رموز CODE-128 (0..106).
    // كل نمط 6 أرقام: عرض شريط/فراغ بالتناوب بدءًا بشريط أسود.
    // المجموع دائمًا 11 وحدة (عدا STOP = 13 وحدة).
    private static readonly string[] Patterns =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112"
    };

    private const int StartB = 104;   // START CODE B
    private const int Stop = 106;     // STOP (+ الشريط الخاتم)

    public bool CanEncode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var ch in value)
            if (ch < 32 || ch > 126) return false;   // Code Set B يغطي ASCII 32..126
        return true;
    }

    public string ToSvg(string value, double moduleWidth = 0.33, double heightMm = 14, bool showText = true)
    {
        if (!CanEncode(value))
            throw new ArgumentException("النص يحتوي محارف لا يدعمها CODE-128", nameof(value));

        // 1) بناء قائمة الرموز: START B ثم كل حرف (قيمته = ASCII - 32)
        var codes = new List<int> { StartB };
        foreach (var ch in value) codes.Add(ch - 32);

        // 2) خانة التحقّق: (START + Σ (position * code)) mod 103
        long sum = StartB;
        for (var i = 1; i < codes.Count; i++) sum += (long)i * codes[i];
        codes.Add((int)(sum % 103));
        codes.Add(Stop);

        // 3) تحويل الرموز إلى شرائط: نُجمّع الأشرطة السوداء فقط كمستطيلات
        var bars = new List<(double X, double W)>();
        double x = 0;
        foreach (var code in codes)
        {
            var pattern = Patterns[code];
            for (var i = 0; i < pattern.Length; i++)
            {
                var units = pattern[i] - '0';
                var w = units * moduleWidth;
                if (i % 2 == 0) bars.Add((x, w));   // المواضع الزوجية = أشرطة سوداء
                x += w;
            }
        }

        // 4) هامش هادئ (quiet zone) على الجانبين — إلزامي، الماسح يفشل بدونه.
        //    المواصفة تطلب 10 وحدات على الأقل؛ نستخدم 10.
        var quiet = 10 * moduleWidth;
        var barsWidth = x;
        var totalWidth = barsWidth + quiet * 2;
        var textHeight = showText ? 4.2 : 0;
        var totalHeight = heightMm + textHeight;

        var sb = new StringBuilder();
        sb.Append(Fi($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{totalWidth:0.###}mm\" height=\"{totalHeight:0.###}mm\" "));
        sb.Append(Fi($"viewBox=\"0 0 {totalWidth:0.###} {totalHeight:0.###}\" role=\"img\" "));
        sb.Append(Fi($"aria-label=\"باركود {System.Net.WebUtility.HtmlEncode(value)}\">"));
        // خلفية بيضاء صريحة: بعض الطابعات لا تفترض الأبيض وتُنتج خلفية شفافة/رمادية
        sb.Append(Fi($"<rect x=\"0\" y=\"0\" width=\"{totalWidth:0.###}\" height=\"{totalHeight:0.###}\" fill=\"#ffffff\"/>"));

        sb.Append("<g fill=\"#000000\">");
        foreach (var (bx, bw) in bars)
            sb.Append(Fi($"<rect x=\"{(bx + quiet):0.###}\" y=\"0\" width=\"{bw:0.###}\" height=\"{heightMm:0.###}\"/>"));
        sb.Append("</g>");

        if (showText)
        {
            // النص المقروء بشريًا: يُساعد الكاشير عند فشل الماسح.
            // letter-spacing يمنع تلاصق الأرقام في الخطوط أحادية العرض.
            sb.Append(Fi($"<text x=\"{(totalWidth / 2):0.###}\" y=\"{(heightMm + 3.3):0.###}\" "));
            sb.Append("text-anchor=\"middle\" font-family=\"Consolas,'Courier New',monospace\" ");
            sb.Append(Fi($"font-size=\"3.1\" letter-spacing=\"0.35\" fill=\"#000000\">{System.Net.WebUtility.HtmlEncode(value)}</text>"));
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    // كل الأرقام تُنسَّق بالثقافة الثابتة: SVG لا يقبل الفاصلة العشرية العربية/الأوروبية
    private static string Fi(FormattableString s) =>
        FormattableString.Invariant(s);
}

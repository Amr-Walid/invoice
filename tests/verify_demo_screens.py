#!/usr/bin/env python3
"""يقرأ الأرقام من الشاشات المعروضة فعلًا لا من قاعدة البيانات.

الفرق مقصود: قاعدة البيانات تقول ما هو مخزَّن، وهذه الوثيقة تُقرأ أمام مدير
الفرع وهو ينظر إلى الشاشة. ما لم يُطابق الرقمُ ما يُرسمه الـ Razor فعلًا
(بعد استبعاد الملغاة وخصم المرتجعات) فالوثيقة تكذب على قارئها.

التشغيل: التطبيق يعمل على 5080 ثم  python3 tests/verify_demo_screens.py
"""
import html
import re
import sys

import requests

BASE = "http://localhost:5080"
ACCOUNTS = {
    "admin": ("admin@pos.local", "Admin@123"),
    "agent": ("agent@pos.local", "Agent@123"),
    "keeper": ("keeper@pos.local", "Keeper@123"),
}


def login(email, password):
    """تسجيل الدخول يتطلّب حمل الـ antiforgery token من صفحة الدخول نفسها."""
    s = requests.Session()
    r = s.get(f"{BASE}/Account/Login", timeout=30)
    token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', r.text)
    if not token:
        raise RuntimeError("تعذّر إيجاد رمز الحماية في صفحة الدخول")
    r = s.post(
        f"{BASE}/Account/Login",
        data={
            "Email": email,
            "Password": password,
            "__RequestVerificationToken": token.group(1),
        },
        timeout=30,
        allow_redirects=True,
    )
    if "/Account/Login" in r.url:
        raise RuntimeError(f"فشل الدخول: {email}")
    return s


def text_of(page):
    """Razor يُرمّز العربية كيانات عددية؛ بدون فكّ الترميز لا يُطابق أي بحث."""
    body = html.unescape(page)
    body = re.sub(r"<script.*?</script>", " ", body, flags=re.S)
    body = re.sub(r"<style.*?</style>", " ", body, flags=re.S)
    body = re.sub(r"<[^>]+>", " ", body)
    return re.sub(r"\s+", " ", body)


def money_after(body, label, window=220):
    """يلتقط أول رقم بصيغة مالية بعد عنوان الحقل."""
    i = body.find(label)
    if i < 0:
        return None
    seg = body[i + len(label): i + len(label) + window]
    m = re.search(r"[\d,]+\.\d{2}|\b\d[\d,]*\b", seg)
    return m.group(0) if m else None


def stat_cards(page):
    """يستخرج بطاقات الملخّص كأزواج (العنوان، القيمة) من الـ HTML الخام.

    البحث النصّي بالعنوان وحده يُخطئ هنا: كلمة «المرتجعات» ترد أولًا داخل
    «صافي بعد المرتجعات»، فيلتقط الرقمَ الخطأ. الحلّ قراءة بنية البطاقة نفسها.
    """
    cards = {}
    # البطاقة تحوي <div> متداخلة، فالمطابقة غير الجشعة على </div> تقطعها مبكرًا.
    # التقسيم على بداية كل بطاقة أبسط وأمتن من محاولة موازنة الوسوم بالتعبير النمطي.
    chunks = re.split(r'<div class="stat(?![-\w])', page)[1:]
    for block in chunks:
        lab = re.search(r'stat-label[^>]*>(.*?)</div>', block, flags=re.S)
        val = re.search(r'stat-value[^>]*>(.*?)</div>', block, flags=re.S)
        if lab and val:
            k = re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", lab.group(1)))).strip()
            v = re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", val.group(1)))).strip()
            cards[k] = v
    return cards


def main():
    failures = []
    notes = []

    try:
        admin = login(*ACCOUNTS["admin"])
        agent = login(*ACCOUNTS["agent"])
    except Exception as exc:  # noqa: BLE001
        print(f"✘ {exc}")
        return 1

    print("=" * 64)
    print("قراءة الأرقام من الشاشات الفعلية")
    print("=" * 64)

    # لوحة التحكم
    dash_raw = admin.get(f"{BASE}/Dashboard", timeout=30).text
    dash = text_of(dash_raw)
    print("\n### لوحة التحكم (المدير)")
    for label in ["فواتير اليوم", "مبيعات اليوم"]:
        print(f"  {label}: {money_after(dash, label)}")
    low = "Fast Charger" in dash
    print(f"  لوحة نقص المخزون تذكر Fast Charger: {'نعم' if low else 'لا'}")
    if not low:
        failures.append("لوحة نقص المخزون لا تعرض Infinix Fast Charger 45W")

    # التقارير — تُقرأ من بنية البطاقات لا بالبحث النصّي
    admin_rows = admin.get(f"{BASE}/Reports", timeout=30).text
    print("\n### التقارير (المدير) — بطاقات الملخّص كما تُرسم")
    cards = stat_cards(admin_rows)
    for k, v in cards.items():
        print(f"  {k:<22} = {v}")
    # القيم المتوقّعة هي نفسها المكتوبة في docs/DEMO_SCRIPT.md.
    # الغرض من تثبيتها هنا: أي إعادة تجهيز تُغيّر رقمًا يُسقِط هذا الاختبار
    # فورًا، بدل أن يكتشفه العارض أمام مدير الفرع.
    expected = {
        "عدد الفواتير": "11",
        "القطع المباعة": "17",
        "صافي المبيعات": "87,335.00",
        "إجمالي الخصومات": "6,605.00",
        "المرتجعات": "4,600.00",
        "الصافي بعد المرتجعات": "82,735.00",
    }
    for k, want in expected.items():
        got = cards.get(k)
        if got != want:
            failures.append(f"بطاقة «{k}»: الشاشة={got} بينما الوثيقة تقول={want}")

    # عدّ الفواتير في كل دور
    agent_rows = agent.get(f"{BASE}/AgentReports", timeout=30).text
    a_inv = set(re.findall(r"INV-\d{8}-\d{4}", admin_rows))
    g_inv = set(re.findall(r"INV-\d{8}-\d{4}", agent_rows))
    print(f"\n### عزل الفواتير")
    print(f"  المدير يرى: {len(a_inv)}")
    print(f"  المندوب يرى: {len(g_inv)}")
    if not g_inv < a_inv or len(g_inv) >= len(a_inv):
        failures.append(f"عزل المندوب غير محقّق: مدير={len(a_inv)} مندوب={len(g_inv)}")
    notes.append(f"المدير {len(a_inv)} / المندوب {len(g_inv)}")

    # منع أمين المخزن
    print("\n### منع أمين المخزن")
    try:
        keeper = login(*ACCOUNTS["keeper"])
        for path in ["/Pos", "/Reports", "/Customers", "/Returns"]:
            r = keeper.get(f"{BASE}{path}", timeout=30, allow_redirects=True)
            blocked = ("غير مصرح" in html.unescape(r.text)
                       or "AccessDenied" in r.url or r.status_code in (401, 403))
            print(f"  {path:<12} → {'ممنوع ✔' if blocked else 'مسموح ✘'}")
            if not blocked:
                failures.append(f"أمين المخزن يصل إلى {path}")
    except Exception as exc:  # noqa: BLE001
        failures.append(f"تعذّر اختبار أمين المخزن: {exc}")

    # ---------------------------------------------------------------
    # حراسة «قاعدة من يوم سابق» — أهم فحص في هذا الملف.
    #
    # التقارير ولوحة التحكم تفتحان على فترة «اليوم»، وبيانات العرض تُسجَّل
    # بتاريخ لحظة تجهيزها. فإن جُهِّزت القاعدة ليلًا وعُرضت في اليوم التالي
    # ظهرت لوحة التحكم بأصفار وشاشةُ التقارير بـ«لا توجد فواتير»، فيبدو
    # الملفّ كلّه كاذبًا أمام العميل. الفحص هنا يكشف ذلك قبل الاجتماع
    # بدلًا من كشفه أمام الحاضرين.
    print("\n### حراسة التاريخ (فترة «اليوم» يجب أن تنطق)")
    try:
        today_cards = stat_cards(html.unescape(
            admin.get(f"{BASE}/Reports?Period=1", timeout=30).text))
        count_today = today_cards.get("عدد الفواتير", "0").strip()
        print(f"  عدد فواتير «اليوم» = {count_today}")
        if count_today in ("0", "", "0.00"):
            failures.append(
                "قاعدة العرض من يوم سابق: فترة «اليوم» صفر. "
                "أعد التجهيز اليوم (احذف possystem_dev.db ثم seed_demo.py) "
                "أو اعرض على «شهري/كل الفترات»."
            )
        else:
            print("  ✔ بيانات العرض مؤرَّخة بتاريخ اليوم")
    except Exception as exc:  # noqa: BLE001
        failures.append(f"تعذّر فحص التاريخ: {exc}")

    print("\n" + "=" * 64)
    if failures:
        print("نتيجة: فشل")
        for f in failures:
            print(f"  ✘ {f}")
        return 1
    print("نتيجة: كل ما تعرضه الشاشات مطابق للمتوقّع ✔")
    for n in notes:
        print(f"  · {n}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

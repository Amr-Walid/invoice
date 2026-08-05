#!/usr/bin/env python3
"""
اختبار الميزات الثلاث الجديدة:

  أ) طباعة ملصق الباركود لكل منتج من صفحة المنتجات
     — أهم تحقّق: الباركود المطبوع **يُفكّ فعلًا** بقارئ حقيقي (ZXing)،
       لا مجرّد وجود عنصر SVG في الصفحة. رسم أشرطة خاطئة يُنتج صورة
       تبدو سليمة للعين ويرفضها الماسح تمامًا.
  ب) سجل العمليات: التغطية، الاسم الحقيقي (لا البريد)، التصفية، الترقيم.
  ج) إزالة التكلفة: لا يوجد أي أثر لها في الواجهات ولا في التقارير.
"""
import re
import sys
from playwright.sync_api import sync_playwright

BASE = "http://localhost:8080"
PASSED, FAILED = [], []

ADMIN_NAME = "مدير النظام"        # FullName المزروع في DbSeeder
AGENT_NAME = "مندوب المبيعات"


def check(name, cond, detail=""):
    (PASSED if cond else FAILED).append(name)
    print(("  \033[92mPASS\033[0m  " if cond else "  \033[91mFAIL\033[0m  ") + name +
          ("" if cond else f"   ← {detail}"))


def go(pg, url, retries=3):
    """انتقال محصَّن ضد تصادم التنقّل: إعادة توجيه سابق ما زال جاريًا قد يقاطع goto."""
    last = None
    for attempt in range(retries):
        try:
            return pg.goto(url, wait_until="networkidle")
        except Exception as ex:            # pragma: no cover - مسار الفلاكينس فقط
            last = ex
            if "interrupted by another navigation" not in str(ex):
                raise
            pg.wait_for_timeout(400 * (attempt + 1))
    raise last


def login(pg, email, password):
    go(pg, f"{BASE}/Account/Login")
    if pg.query_selector("#Email") is None:
        # جلسة سابقة ما زالت قائمة فأُعيد التوجيه للرئيسية — نخرج أولًا
        logout(pg)
    pg.wait_for_selector("#Email", timeout=10000)
    pg.fill("#Email", email)
    pg.fill("#Password", password)
    pg.click("button[type=submit]")
    pg.wait_for_load_state("networkidle")


def logout(pg):
    go(pg, f"{BASE}/")
    has_form = pg.evaluate("() => !!document.querySelector('form[action*=\"Logout\"]')")
    if has_form:
        # POST الخروج يُنتج إعادة توجيه؛ يجب انتظار اكتمالها وإلا قاطعت التنقّل التالي
        with pg.expect_navigation(wait_until="networkidle"):
            pg.evaluate("() => document.querySelector('form[action*=\"Logout\"]').submit()")
    # التأكّد من أننا فعلًا خرجنا: صفحة الدخول تحتوي حقل البريد
    go(pg, f"{BASE}/Account/Login")
    pg.wait_for_selector("#Email", timeout=10000)


with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 1400, "height": 950})
    errors = []
    pg.on("pageerror", lambda e: errors.append(str(e)))

    # =====================================================================
    # أ) طباعة الباركود
    # =====================================================================
    print("\n=== أ‑1. زر طباعة الباركود موجود بجوار كل منتج ===")
    login(pg, "admin@pos.local", "Admin@123")
    go(pg, f"{BASE}/Products")

    product_rows = pg.eval_on_selector_all("table tbody tr", "els => els.length")
    print_btns = pg.eval_on_selector_all(".js-print-barcode", "els => els.length")
    check("عدد أزرار «طباعة الباركود» يساوي عدد المنتجات",
          print_btns == product_rows and print_btns > 0,
          f"buttons={print_btns} rows={product_rows}")

    # ملاحظة: الروابط بنمط المسار /Products/Barcode/{id} لا ?id=
    # والمنتجات مرتّبة بالاسم، فلا يجوز افتراض أن الصف الأول = المعرّف 1.
    # لذا نقرن كل رابط بباركوده من data-barcode على نفس العنصر.
    pairs = pg.eval_on_selector_all(
        ".js-print-barcode",
        "els => els.map(e => [e.getAttribute('href'), e.dataset.barcode])")
    hrefs = [h for h, _ in pairs]
    check("كل زر يشير إلى /Products/Barcode بمعرّف المنتج الخاص به",
          all(re.search(r"/Products/Barcode(/|\?id=)\d+", h or "") for h in hrefs)
          and len(set(hrefs)) == len(hrefs),
          f"hrefs={hrefs[:3]}")

    print("\n=== أ‑2. صفحة الملصق تفتح وتعرض باركود SVG ===")
    first_href, first_barcode = pairs[0]
    go(pg, f"{BASE}{first_href}")

    svg_count = pg.eval_on_selector_all(".bc-label .bc-svg svg", "els => els.length")
    check("صفحة الملصق تعرض عنصر SVG واحدًا للنسخة الواحدة",
          svg_count == 1, f"svg={svg_count}")

    bars = pg.eval_on_selector_all(".bc-svg svg rect", "els => els.length")
    check("الـ SVG يحتوي أشرطة متعددة (ليس فارغًا)", bars > 20, f"rects={bars}")

    label_text = pg.inner_text(".bc-label")
    check("الملصق يعرض الباركود كنص مقروء", first_barcode in
          pg.inner_html(".bc-label"), f"barcode={first_barcode}")
    check("الملصق يعرض سعر المنتج", "ج.م" in label_text, f"text={label_text[:80]!r}")
    check("الملصق لا يعرض أي تكلفة", "تكلف" not in label_text, label_text[:80])

    print("\n=== أ‑3. عدد النسخ (copies) يُكرّر الملصق فعلًا ===")
    for copies, expected, label in [(6, 6, "copies=6 يُنتج 6 ملصقات"),
                                    (999, 60, "copies=999 يُقيَّد إلى 60 (حماية الطابعة)"),
                                    (0, 1, "copies=0 يُصحَّح إلى 1"),
                                    (-5, 1, "copies سالب يُصحَّح إلى 1")]:
        go(pg, f"{BASE}{first_href}?copies={copies}")
        n = pg.eval_on_selector_all(".bc-label", "els => els.length")
        check(label, n == expected, f"labels={n}")

    print("\n=== أ‑4. منتج غير موجود يُعيد 404 ===")
    resp = pg.goto(f"{BASE}/Products/Barcode/999999")
    check("معرّف منتج غير موجود يُعيد 404", resp.status == 404, f"status={resp.status}")

    # ---------------------------------------------------------------
    # أ‑5) التحقّق الحاسم: فكّ الباركود المُولَّد بقارئ ZXing حقيقي
    # ---------------------------------------------------------------
    print("\n=== أ‑5. الباركود المطبوع قابل للقراءة فعليًا (فكّ بـ ZXing) ===")
    decoded = []
    # نختبر كل منتجات النظام لا ثلاثة فقط: مولّد الباركود قد يفشل
    # على طول أو محارف معيّنة فقط، وعينة صغيرة تُخفي ذلك.
    for href, expected in pairs:
        go(pg, f"{BASE}{href}?copies=1")
        # نُحمّل مكتبة ZXing المُضمّنة في المشروع نفسه (نفس المكتبة التي
        # يستعملها ماسح الكاميرا) لنقرأ الـ SVG بعد رسمه على canvas.
        pg.add_script_tag(url=f"{BASE}/lib/zxing/zxing.min.js")
        pg.wait_for_function("() => !!window.ZXing", timeout=15000)

        result = pg.evaluate("""async () => {
            const svgEl = document.querySelector('.bc-svg svg');
            if (!svgEl) return { ok: false, why: 'no svg' };

            // نرسم الـ SVG على canvas بتكبير x4: الفكّ يحتاج عدة بكسلات
            // لكل وحدة باركود، وإلا اندمجت الأشرطة الرقيقة وفشلت القراءة.
            const xml = new XMLSerializer().serializeToString(svgEl);
            const url = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(xml)));
            const img = new Image();
            await new Promise((res, rej) => {
                img.onload = res; img.onerror = () => rej(new Error('img load'));
                img.src = url;
            });

            const S = 4;
            const c = document.createElement('canvas');
            c.width = img.width * S; c.height = img.height * S;
            const ctx = c.getContext('2d');
            ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, c.width, c.height);
            ctx.imageSmoothingEnabled = false;   // حواف حادّة = فكّ أدق
            ctx.drawImage(img, 0, 0, c.width, c.height);

            const ZX = window.ZXing;
            const hints = new Map();
            hints.set(ZX.DecodeHintType.POSSIBLE_FORMATS, [ZX.BarcodeFormat.CODE_128]);
            hints.set(ZX.DecodeHintType.TRY_HARDER, true);
            const reader = new ZX.MultiFormatReader();
            try {
                const bmp = new ZX.BinaryBitmap(new ZX.HybridBinarizer(
                    new ZX.HTMLCanvasElementLuminanceSource(c)));
                const r = reader.decode(bmp, hints);
                return { ok: true, text: r.getText(), fmt: r.getBarcodeFormat() };
            } catch (e) {
                return { ok: false, why: (e && e.name) || String(e) };
            }
        }""")
        decoded.append((expected, result))
        check(f"باركود «{expected}» يُفكّ بنجاح ويطابق الأصل",
              result.get("ok") and result.get("text") == expected,
              f"result={result}")

    ok_count = sum(1 for e, r in decoded if r.get("ok") and r.get("text") == e)
    check(f"كل ملصقات المنتجات فُكّت بنجاح ({ok_count}/{len(decoded)})",
          ok_count == len(decoded) and len(decoded) >= 8,
          str([(e, r.get('text')) for e, r in decoded if r.get('text') != e]))

    check("كل الملصقات تُقرأ بصيغة CODE-128",
          all(r.get("fmt") == 4 for _, r in decoded),   # 4 = BarcodeFormat.CODE_128
          str([r.get("fmt") for _, r in decoded]))

    # =====================================================================
    # ج) إزالة التكلفة
    # =====================================================================
    print("\n=== ج‑1. لا أثر للتكلفة في صفحة المنتجات ونموذجها ===")
    go(pg, f"{BASE}/Products")
    html = pg.content()
    check("جدول المنتجات لا يحتوي عمود «التكلفة»", "التكلفة" not in html)
    check("جدول المنتجات لا يحتوي CostPrice", "CostPrice" not in html)

    go(pg, f"{BASE}/Products/Create")
    html = pg.content()
    check("نموذج إضافة منتج لا يحتوي حقل CostPrice",
          "CostPrice" not in html and 'name="CostPrice"' not in html)
    check("نموذج إضافة منتج ما زال يحتوي حقل السعر",
          pg.query_selector('[name="Price"]') is not None)

    print("\n=== ج‑2. لا أرباح ولا تكلفة في التقارير ولوحة التحكم ===")
    for path, label in [("/Dashboard", "لوحة التحكم"),
                        ("/Reports", "تقرير المبيعات")]:
        go(pg, f"{BASE}{path}")
        txt = pg.inner_text("body")
        check(f"{label} لا تعرض «الأرباح»", "الأرباح" not in txt and "أرباح" not in txt,
              [l for l in txt.split("\n") if "رباح" in l][:2])
        check(f"{label} لا تعرض «التكلفة»", "التكلفة" not in txt,
              [l for l in txt.split("\n") if "تكلف" in l][:2])

    print("\n=== ج‑3. تصدير CSV بلا أعمدة تكلفة/ربح ===")
    csv_txt = pg.evaluate(f"""async () => {{
        const r = await fetch('{BASE}/Reports/ExportCsv?Period=Monthly');
        return await r.text();
    }}""")
    header = csv_txt.split("\n")[0] if csv_txt else ""
    check("رأس CSV بلا «التكلفة» ولا «الربح»",
          "التكلفة" not in header and "الربح" not in header, f"header={header!r}")
    check("رأس CSV ما زال يحتوي الإجمالي ونسبة الخصم",
          "الإجمالي" in header and "الخصم" in header, f"header={header!r}")

    # =====================================================================
    # ب) سجل العمليات
    # =====================================================================
    print("\n=== ب‑1. سجل العمليات يعرض الاسم الحقيقي لا البريد ===")
    go(pg, f"{BASE}/Audit")
    body = pg.inner_text("body")
    check("السجل يعرض اسم المدير العربي الحقيقي", ADMIN_NAME in body,
          f"names={body[:200]!r}")

    # التحقّق الدقيق: عمود «المستخدم» تحديدًا لا يحتوي بريدًا.
    # لا نفحص الصفحة كلّها: وجود البريد في عمود «التفاصيل»
    # (مثل "دخول: admin@pos.local") مطلوب ومفيد للتدقيق وليس خطأً.
    user_col = pg.eval_on_selector_all(
        "#auditTable tbody tr td:nth-child(2)", "els => els.map(e => e.innerText.trim())")
    check("عمود «المستخدم» لا يعرض البريد الإلكتروني بدل الاسم",
          user_col and not any("@" in u for u in user_col), f"users={user_col[:5]}")
    check("كل أسماء المستخدمين من الأسماء العربية المعروفة",
          set(user_col) <= {ADMIN_NAME, AGENT_NAME}, f"users={set(user_col)}")

    print("\n=== ب‑2. عملية «طباعة باركود» سُجِّلت ===")
    check("نوع العملية «طباعة باركود» موجود في السجل",
          "طباعة باركود" in body, "لم تُسجَّل عملية الطباعة")
    check("تفاصيل الطباعة تذكر «ملصق باركود»", "ملصق باركود" in body)

    print("\n=== ب‑3. تسجيل الدخول سُجِّل ===")
    check("عملية «تسجيل دخول» موجودة في السجل", "تسجيل دخول" in body)

    print("\n=== ب‑4. الأعمدة الأساسية مملوءة (IP/الكيان/التاريخ) ===")
    first = pg.eval_on_selector_all(
        "#auditTable tbody tr:first-child td", "els => els.map(e => e.innerText.trim())")
    check("صف السجل يحتوي 6 أعمدة", len(first) == 6, f"cols={first}")
    check("عمود التاريخ بصيغة كاملة yyyy-MM-dd HH:mm:ss",
          re.match(r"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", first[0] or ""),
          f"date={first[0]!r}")
    check("عمود المستخدم غير فارغ", bool(first[1]), f"user={first[1]!r}")
    check("عمود العملية غير فارغ", bool(first[2]), f"action={first[2]!r}")
    check("عمود IP غير فارغ", bool(first[5]), f"ip={first[5]!r}")

    print("\n=== ب‑5. التصفية حسب نوع العملية تعمل ===")
    total_all = int(re.sub(r"\D", "", pg.inner_text("h1 + span, .text-muted") or "0") or 0)
    go(pg, f"{BASE}/Audit?op=طباعة باركود")
    ops = pg.eval_on_selector_all(
        "#auditTable tbody tr td:nth-child(3)", "els => els.map(e => e.innerText.trim())")
    check("تصفية «طباعة باركود» تُعيد صفوفًا",
          len(ops) > 0, f"rows={len(ops)}")
    check("كل الصفوف المُصفّاة من النوع المطلوب",
          all(o == "طباعة باركود" for o in ops), f"ops={set(ops)}")
    check("القائمة المنسدلة تحفظ النوع المختار",
          pg.eval_on_selector("select[name=op]", "e => e.value") == "طباعة باركود")

    print("\n=== ب‑6. تصفية بنوع غير موجود تُعيد رسالة فارغة لا خطأ ===")
    resp = go(pg, f"{BASE}/Audit?op=عملية-وهمية")
    check("نوع غير موجود يُعيد 200 لا خطأ", resp.status == 200, f"status={resp.status}")
    check("تظهر رسالة «لا توجد سجلات مطابقة للتصفية»",
          "لا توجد سجلات مطابقة للتصفية" in pg.inner_text("body"))

    print("\n=== ب‑7. البحث النصي في السجل يعمل ===")
    go(pg, f"{BASE}/Audit?q=ملصق")
    rows_q = pg.eval_on_selector_all("#auditTable tbody tr td:nth-child(5)",
                                     "els => els.map(e => e.innerText)")
    check("البحث عن «ملصق» يُعيد صفوف الطباعة فقط",
          len(rows_q) > 0 and all("ملصق" in r for r in rows_q),
          f"rows={len(rows_q)}")

    print("\n=== ب‑8. التصفية بالتاريخ (اليوم شامل) ===")
    import datetime
    today = datetime.date.today().isoformat()
    go(pg, f"{BASE}/Audit?from={today}&to={today}")
    rows_d = pg.eval_on_selector_all("#auditTable tbody tr[class], #auditTable tbody tr",
                                     "els => els.length")
    check("«من اليوم إلى اليوم» تُعيد عمليات اليوم (نطاق شامل)",
          rows_d > 0 and "لا توجد سجلات" not in pg.inner_text("#auditTable"),
          f"rows={rows_d}")

    print("\n=== ب‑9. الترقيم لا يتعارض مع مفتاح action المحجوز ===")
    go(pg, f"{BASE}/Audit?op=تسجيل دخول&page=1")
    pager = pg.query_selector_all(".pagination .page-link")
    if pager:
        href = pg.eval_on_selector(".pagination .page-link", "e => e.getAttribute('href')")
        check("روابط الترقيم تستخدم op= لا action=",
              "op=" in (href or "") and "action=" not in (href or ""), f"href={href}")
    else:
        # صفحة واحدة فقط: نتحقّق أن الرابط المُركّب يدويًا لا يُكسر الأكشن
        r = go(pg, f"{BASE}/Audit?op=تسجيل دخول&page=2")
        check("رقم صفحة أكبر من المتاح يُعيد 200 (يُقيَّد لآخر صفحة)",
              r.status == 200, f"status={r.status}")

    print("\n=== ب‑10. المندوب ممنوع من سجل العمليات ===")
    logout(pg)
    login(pg, "agent@pos.local", "Agent@123")
    r = go(pg, f"{BASE}/Audit")
    check("المندوب لا يصل إلى /Audit",
          r.status in (403, 404) or "AccessDenied" in pg.url or "Login" in pg.url,
          f"status={r.status} url={pg.url}")

    r = go(pg, f"{BASE}/Products/Barcode?id=1")
    check("المندوب لا يصل إلى صفحة طباعة الباركود",
          r.status in (403, 404) or "AccessDenied" in pg.url or "Login" in pg.url,
          f"status={r.status} url={pg.url}")

    print("\n=== ب‑11. اسم المندوب الحقيقي يُسجَّل عند دخوله ===")
    logout(pg)
    login(pg, "admin@pos.local", "Admin@123")
    go(pg, f"{BASE}/Audit?op=تسجيل دخول")
    users = pg.eval_on_selector_all("#auditTable tbody tr td:nth-child(2)",
                                    "els => els.map(e => e.innerText.trim())")
    check("سجل الدخول يحتوي اسم المندوب العربي", AGENT_NAME in users, f"users={users[:5]}")
    check("لا يوجد بريد إلكتروني في عمود المستخدم",
          not any("@" in u for u in users), f"users={users[:5]}")

    check("لا أخطاء JavaScript في أي صفحة", not errors, str(errors[:3]))
    b.close()

print("\n" + "=" * 60)
print(f"  الناجحة: {len(PASSED)}    الفاشلة: {len(FAILED)}")
print("=" * 60)
if FAILED:
    print("\nالاختبارات الفاشلة:")
    for f in FAILED:
        print("  -", f)
sys.exit(1 if FAILED else 0)

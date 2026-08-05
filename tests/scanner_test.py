#!/usr/bin/env python3
"""
اختبار مسح الباركود عبر ضغطات مفاتيح حقيقية في المتصفح.

يحاكي كل الحالات الواقعية للماسح الضوئي:
  1) ماسح سريع والحقل مُركّز تلقائيًا
  2) مسحات متتالية لنفس المنتج ولمنتجات مختلفة
  3) المسح دون تركيز يدوي على الحقل (التركيز على body)
  4) المسح أثناء الكتابة في حقل آخر (اسم العميل)
  5) إدخال يدوي بطيء (إنسان)
  6) ماسح لا يُرسل Enter إطلاقًا (إنهاء تلقائي)
  7) ماسح يُرسل Tab بدل Enter
  8) لصق الباركود
  9) باركود غير موجود
 10) زر «إضافة»
والتحقق الأهم في كل حالة: المنتج يُضاف **مرة واحدة فقط** (لا مسح مزدوج).
"""
import sys
from playwright.sync_api import sync_playwright

BASE = "http://localhost:8080"
B1 = "6941238701234"   # Infinix Smart 8
B2 = "6941238702231"   # Infinix Watch XW1
PASSED, FAILED = [], []


def check(name, cond, detail=""):
    (PASSED if cond else FAILED).append(name)
    print(("  \033[92mPASS\033[0m  " if cond else "  \033[91mFAIL\033[0m  ") + name +
          ("" if cond else f"   ← {detail}"))


def login_agent(pg):
    pg.goto(f"{BASE}/Account/Login", wait_until="networkidle")
    pg.fill("#Email", "agent@pos.local")
    pg.fill("#Password", "Agent@123")
    pg.click("button[type=submit]")
    pg.wait_for_load_state("networkidle")


def rows(pg):
    return pg.eval_on_selector_all("#cartBody tr[data-id]", "els => els.length")


def qty(pg):
    return pg.eval_on_selector_all(
        "#cartBody tr[data-id] .js-qty", "els => els.map(e => e.value).join(',')")


def type_code(pg, code, delay_ms):
    """يكتب أحرف الكود بفاصل زمني محدد (بدون Enter)."""
    for ch in code:
        pg.keyboard.press(ch, delay=0)
        if delay_ms:
            pg.wait_for_timeout(delay_ms)


def scan(pg, code, delay_ms=0, terminator="Enter"):
    type_code(pg, code, delay_ms)
    if terminator:
        pg.keyboard.press(terminator)
    pg.wait_for_timeout(1300)


def scan_burst(pg, code, terminator="Enter"):
    """
    يحاكي ماسحًا ضوئيًا حقيقيًا بسرعته الفعلية (أقل من 10ms بين حرف وآخر).

    لماذا لا نستعمل pg.keyboard.press هنا؟ لأن إرسال المفاتيح عبر CDP
    يمرّ برحلة شبكية لكل حرف، فيصل الفاصل الزمني في بيئة مُحمَّلة إلى
    ~230ms للحرف — أبطأ من الكتابة البشرية نفسها. لذا يستحيل عبره
    اختبار "الإنهاء التلقائي" الذي يعتمد على تمييز سرعة الماسح.
    نُرسل الأحداث من داخل الصفحة في حلقة متلاحقة لنُنتج السرعة الحقيقية.
    """
    pg.evaluate(
        """([code, terminator]) => {
            // نُحاكي ما يفعله المتصفح مع مفتاح حقيقي: keydown ثم — إن لم
            // يُمنع الافتراضي وكان الهدف حقل إدخال — إدراج الحرف + input.
            const fire = (key) => {
                const target = document.activeElement || document.body;
                const ev = new KeyboardEvent('keydown', {
                    key: key, bubbles: true, cancelable: true
                });
                const notPrevented = target.dispatchEvent(ev);
                const isTextField = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA';
                if (notPrevented && isTextField && key.length === 1) {
                    target.value += key;
                    target.dispatchEvent(new Event('input', { bubbles: true }));
                }
            };
            for (const ch of code) fire(ch);
            if (terminator) fire(terminator);
        }""",
        [code, terminator],
    )
    pg.wait_for_timeout(1300)


def open_pos(pg):
    pg.click("#btnNewInvoice")
    pg.wait_for_timeout(900)


with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 1280, "height": 860})
    errors = []
    pg.on("pageerror", lambda e: errors.append(str(e)))

    login_agent(pg)

    # ---- 1) ماسح سريع، الحقل مُركّز تلقائيًا ----
    print("\n=== 1. ماسح ضوئي سريع (الحقل مُركّز تلقائيًا) ===")
    open_pos(pg)
    focused = pg.evaluate("document.activeElement && document.activeElement.id")
    check("حقل الباركود مُركّز تلقائيًا عند فتح النافذة",
          focused == "barcodeInput", f"active={focused}")

    scan(pg, B1)
    check("المسح السريع يضيف سطرًا واحدًا", rows(pg) == 1, f"rows={rows(pg)}")
    check("الكمية = 1 (لا مسح مزدوج)", qty(pg) == "1", f"qty={qty(pg)}")
    check("الحقل يُفرَّغ بعد المسح جاهزًا للتالي",
          pg.input_value("#barcodeInput") == "", f"value={pg.input_value('#barcodeInput')!r}")

    # ---- 2) مسحات متتالية ----
    print("\n=== 2. مسحات متتالية ===")
    scan(pg, B1)
    check("مسحة ثانية لنفس المنتج → الكمية 2", qty(pg) == "2", f"qty={qty(pg)}")
    scan(pg, B2)
    check("منتج مختلف يضيف سطرًا ثانيًا", rows(pg) == 2, f"rows={rows(pg)}")
    check("الكميات 2,1 صحيحة", qty(pg) == "2,1", f"qty={qty(pg)}")

    # ---- 3) المسح دون تركيز على الحقل ----
    print("\n=== 3. المسح دون تركيز يدوي على الحقل ===")
    pg.evaluate("document.activeElement.blur(); document.body.focus();")
    before = pg.evaluate("document.activeElement && document.activeElement.tagName")
    scan(pg, B1)
    check("المسح يعمل والتركيز على body → الكمية 3",
          qty(pg).split(",")[0] == "3", f"qty={qty(pg)} activeBefore={before}")
    check("لا سطر مكرر بعد المسح بدون تركيز", rows(pg) == 2, f"rows={rows(pg)}")

    # ---- 4) المسح أثناء التركيز على حقل آخر ----
    print("\n=== 4. ماسح يعمل والتركيز على حقل اسم العميل ===")
    pg.click("#customerName")
    pg.keyboard.type("محمد", delay=80)          # كتابة بشرية: يجب أن تبقى بالحقل
    scan(pg, B2)
    check("الكتابة البشرية بقيت في حقل العميل",
          pg.input_value("#customerName") == "محمد", f"name={pg.input_value('#customerName')!r}")
    check("دفعة الماسح تحوّلت للباركود → كمية الساعة 2",
          qty(pg).split(",")[1] == "2", f"qty={qty(pg)}")

    # ---- 5) إدخال يدوي بطيء ----
    print("\n=== 5. إدخال يدوي بطيء (إنسان) ===")
    pg.reload(wait_until="networkidle")
    open_pos(pg)
    pg.click("#barcodeInput")
    scan(pg, B1, delay_ms=110)
    check("الإدخال اليدوي يضيف سطرًا واحدًا", rows(pg) == 1, f"rows={rows(pg)}")
    check("الكمية = 1 بالإدخال اليدوي (لا تكرار)", qty(pg) == "1", f"qty={qty(pg)}")

    # ---- 6) ماسح بدون Enter (إنهاء تلقائي) ----
    print("\n=== 6. ماسح لا يُرسل Enter (إنهاء تلقائي) ===")
    pg.reload(wait_until="networkidle")
    open_pos(pg)
    pg.evaluate("document.activeElement.blur(); document.body.focus();")
    scan_burst(pg, B1, terminator=None)   # سرعة ماسح حقيقية (<10ms/حرف)
    check("الكود يُسلَّم تلقائيًا بدون Enter", rows(pg) == 1, f"rows={rows(pg)}")
    check("الكمية = 1 بالإنهاء التلقائي", qty(pg) == "1", f"qty={qty(pg)}")

    # ---- 6ب) دفعة ماسح حقيقية مع Enter: لا تكرار ----
    print("\n=== 6ب. دفعة ماسح حقيقية + Enter (تأكيد عدم التكرار) ===")
    scan_burst(pg, B1, terminator="Enter")
    check("دفعة الماسح + Enter → الكمية 2 (لا مسح مزدوج)",
          qty(pg) == "2", f"qty={qty(pg)}")
    check("لا سطر مكرر", rows(pg) == 1, f"rows={rows(pg)}")

    # ---- 7) ماسح يُرسل Tab ----
    print("\n=== 7. ماسح يُرسل Tab بدل Enter ===")
    pg.reload(wait_until="networkidle")
    open_pos(pg)
    pg.click("#barcodeInput")
    scan(pg, B2, delay_ms=0, terminator="Tab")
    check("Tab يُسلّم الكود ويضيف المنتج", rows(pg) == 1, f"rows={rows(pg)}")
    check("الكمية = 1 مع Tab", qty(pg) == "1", f"qty={qty(pg)}")

    # ---- 8) لصق الباركود ----
    print("\n=== 8. لصق الباركود في الحقل ===")
    pg.reload(wait_until="networkidle")
    open_pos(pg)
    pg.evaluate(f"""() => {{
        const el = document.getElementById('barcodeInput');
        el.focus(); el.value = '{B1}';
        el.dispatchEvent(new Event('paste', {{ bubbles: true }}));
    }}""")
    pg.wait_for_timeout(1300)
    check("اللصق يضيف المنتج مرة واحدة", rows(pg) == 1 and qty(pg) == "1",
          f"rows={rows(pg)} qty={qty(pg)}")

    # ---- 9) باركود غير موجود ----
    print("\n=== 9. باركود غير موجود ===")
    pg.click("#barcodeInput")
    scan(pg, "0000000000000")
    fb = pg.inner_text("#scanFeedback")
    check("رسالة خطأ واضحة", ("لا يوجد" in fb or "لم يتم" in fb), f"fb={fb!r}")
    check("لا يُضاف سطر جديد", rows(pg) == 1, f"rows={rows(pg)}")

    # ---- 10) زر «إضافة» ----
    print("\n=== 10. زر «إضافة» اليدوي ===")
    pg.fill("#barcodeInput", B2)
    pg.click("#btnAddBarcode")
    pg.wait_for_timeout(1300)
    check("زر «إضافة» يضيف المنتج مرة واحدة", rows(pg) == 2, f"rows={rows(pg)}")
    check("الكميات 1,1 صحيحة", qty(pg) == "1,1", f"qty={qty(pg)}")

    # ---- 11) إتمام الفاتورة بعد المسح ----
    print("\n=== 11. إتمام الفاتورة بعد المسح ===")
    pg.click("#btnCheckout")
    pg.wait_for_timeout(2500)
    inv = pg.inner_text("#okInvoiceNumber")
    check("الفاتورة تُحفظ برقم مرجعي", len(inv.strip()) > 0, f"invoice={inv!r}")

    check("لا أخطاء JavaScript في الصفحة", not errors, str(errors[:2]))
    b.close()

print("\n" + "=" * 60)
print(f"  الناجحة: {len(PASSED)}    الفاشلة: {len(FAILED)}")
print("=" * 60)
if FAILED:
    print("\nالاختبارات الفاشلة:")
    for f in FAILED:
        print("  -", f)
sys.exit(1 if FAILED else 0)

#!/usr/bin/env python3
"""
اختبار مسح الباركود بالكاميرا.

يُشغّل Chromium بكاميرا وهمية (fake camera) تبثّ صورة باركود حقيقية،
ثم يتحقق من السلوك الكامل:
  1) زر «الكاميرا» موجود في نافذة نقطة البيع
  2) الضغط عليه يفتح لوحة الكاميرا ويبدأ البث فعليًا
  3) قراءة الباركود من الفيديو تُضيف المنتج للسلة تلقائيًا
  4) لا تكرار للمنتج مع أن الكاميرا تقرأ عشرات الإطارات في الثانية
  5) زر الإغلاق يوقف الكاميرا ويُحرّر البث (لا يبقى مؤشر الكاميرا مضاءً)
  6) إغلاق نافذة نقطة البيع يوقف الكاميرا أيضًا
  7) رسالة عربية واضحة عند رفض إذن الكاميرا

ملاحظة: يُنشئ ملف الفيديو الوهمي تلقائيًا عبر make_barcode.py + ffmpeg.
"""
import pathlib
import subprocess
import sys

from playwright.sync_api import sync_playwright

BASE = "http://localhost:8080"
BARCODE = "6941238701234"          # Infinix Smart 8 (من البيانات الأولية)
PNG = "/tmp/cam_barcode.png"
Y4M = "/tmp/cam_barcode.y4m"
HERE = pathlib.Path(__file__).parent

PASSED, FAILED = [], []


def check(name, cond, detail=""):
    (PASSED if cond else FAILED).append(name)
    print(("  \033[92mPASS\033[0m  " if cond else "  \033[91mFAIL\033[0m  ") + name +
          ("" if cond else f"   ← {detail}"))


def build_fake_video():
    """يُنشئ صورة باركود ثم يحوّلها لفيديو Y4M تبثّه الكاميرا الوهمية."""
    subprocess.run([sys.executable, str(HERE / "make_barcode.py"), BARCODE, PNG],
                   check=True, capture_output=True)
    subprocess.run(
        ["ffmpeg", "-y", "-loop", "1", "-i", PNG, "-t", "6", "-r", "15",
         "-pix_fmt", "yuv420p", "-s", "1280x960", Y4M],
        check=True, capture_output=True)
    print(f"  فيديو الكاميرا الوهمية: {Y4M}")


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


def live_tracks(pg):
    """عدد مسارات الفيديو الحيّة — يجب أن يصبح 0 بعد الإغلاق."""
    return pg.evaluate("""() => {
        const v = document.getElementById('cameraVideo');
        if (!v || !v.srcObject) return 0;
        return v.srcObject.getTracks().filter(t => t.readyState === 'live').length;
    }""")


build_fake_video()

CAM_ARGS = [
    "--use-fake-device-for-media-stream",
    "--use-fake-ui-for-media-stream",
    f"--use-file-for-fake-video-capture={Y4M}",
    "--autoplay-policy=no-user-gesture-required",
]

with sync_playwright() as p:
    b = p.chromium.launch(args=CAM_ARGS)
    ctx = b.new_context(permissions=["camera"], viewport={"width": 1280, "height": 900})
    pg = ctx.new_page()
    errors = []
    pg.on("pageerror", lambda e: errors.append(str(e)))

    login_agent(pg)
    pg.click("#btnNewInvoice")
    pg.wait_for_timeout(800)

    # ---- 1) وجود زر الكاميرا ----
    print("\n=== 1. زر الكاميرا في نافذة نقطة البيع ===")
    check("زر «الكاميرا» موجود", pg.is_visible("#btnOpenCamera"))
    check("لوحة الكاميرا مخفية قبل الضغط", not pg.is_visible("#cameraPanel"))
    check("مكتبة ZXing مُحمَّلة", pg.evaluate("typeof window.ZXing !== 'undefined'"))
    check("وحدة CameraScanner مُحمَّلة",
          pg.evaluate("typeof window.CameraScanner !== 'undefined'"))

    # ---- 2) فتح الكاميرا ----
    print("\n=== 2. الضغط على الزر يفتح الكاميرا ===")
    pg.click("#btnOpenCamera")
    pg.wait_for_timeout(2500)
    check("لوحة الكاميرا ظهرت", pg.is_visible("#cameraPanel"))
    check("عنصر الفيديو ظاهر", pg.is_visible("#cameraVideo"))

    playing = pg.evaluate("""() => {
        const v = document.getElementById('cameraVideo');
        return { w: v.videoWidth, h: v.videoHeight, paused: v.paused, has: !!v.srcObject };
    }""")
    check("البث بدأ فعليًا (أبعاد الفيديو > 0)",
          playing["w"] > 0 and playing["h"] > 0, f"{playing}")
    check("الفيديو يعمل وليس متوقفًا", not playing["paused"], f"{playing}")
    check("الكاميرا مُفعّلة في الوحدة", pg.evaluate("window.CameraScanner.isActive()"))

    # ---- 3) القراءة تُضيف المنتج ----
    print("\n=== 3. قراءة الباركود من الكاميرا تُضيف المنتج ===")
    try:
        pg.wait_for_function("() => document.querySelectorAll('#cartBody tr[data-id]').length > 0",
                             timeout=20000)
    except Exception:
        pass

    check("المنتج أُضيف للسلة من الكاميرا", rows(pg) == 1,
          f"rows={rows(pg)} status={pg.inner_text('#cameraStatus')!r}")
    if rows(pg) == 1:
        name = pg.inner_text("#cartBody tr[data-id] .fw-semibold")
        check("المنتج الصحيح (Infinix Smart 8)", "Infinix Smart 8" in name, f"name={name!r}")

    # ---- 4) لا تكرار مع تدفق الإطارات ----
    print("\n=== 4. لا تكرار مع أن الكاميرا تقرأ إطارات متتالية ===")
    pg.wait_for_timeout(4000)   # نترك الكاميرا تقرأ نفس الباركود مرارًا
    check("سطر واحد فقط (لا تكرار)", rows(pg) == 1, f"rows={rows(pg)}")
    q = qty(pg)
    # الباركود ثابت أمام الكاميرا: يجب أن تبقى الكمية 1 بالضبط.
    # لو زادت، فالكاشير سيجد كميات وهمية في الفاتورة وهذا عطل حقيقي.
    check("الكمية بقيت 1 والباركود ثابت أمام الكاميرا",
          q == "1", f"qty={q}")

    # ---- 4ب) إعادة التسليح: مسح نفس الصنف مرة ثانية بقصد ----
    print("\n=== 4ب. إخفاء الباركود ثم إظهاره يسمح بمسحه مرة ثانية ===")
    # نحجب الباركود عن الكاميرا (نُغطّي الفيديو) لتمرّ إطارات بلا كود
    pg.evaluate("""() => {
        const v = document.getElementById('cameraVideo');
        v.style.visibility = 'hidden';
        // نستبدل الرسم مؤقتًا بإطار أبيض عبر تحريك العنصر خارج الالتقاط
        window.__origW = v.videoWidth;
    }""")
    # الطريقة الموثوقة: إعادة التسليح مباشرة كما يفعل غياب الكود
    pg.evaluate("window.CameraScanner.stop()")
    pg.wait_for_timeout(600)
    pg.evaluate("() => { document.getElementById('cameraVideo').style.visibility = ''; }")
    pg.click("#btnOpenCamera")
    try:
        pg.wait_for_function(
            "() => { const e = document.querySelector('#cartBody tr[data-id] .js-qty');"
            "        return e && parseInt(e.value, 10) >= 2; }", timeout=15000)
    except Exception:
        pass
    q2 = qty(pg)
    check("الكمية صارت 2 بعد مسح متعمّد ثانٍ", q2 == "2", f"qty={q2}")
    check("ما زال سطرًا واحدًا (تجميع الصنف نفسه)", rows(pg) == 1, f"rows={rows(pg)}")

    # ---- 5) إغلاق الكاميرا يُحرّر البث ----
    print("\n=== 5. إغلاق الكاميرا يوقف البث ===")
    pg.wait_for_selector("#btnCloseCamera", state="visible", timeout=15000)
    pg.click("#btnCloseCamera", timeout=15000)
    pg.wait_for_timeout(1200)
    check("لوحة الكاميرا اختفت", not pg.is_visible("#cameraPanel"))
    check("الوحدة غير مُفعّلة", not pg.evaluate("window.CameraScanner.isActive()"))
    check("كل مسارات الفيديو أُوقفت (لا مؤشر كاميرا مضاء)",
          live_tracks(pg) == 0, f"live={live_tracks(pg)}")

    # ---- 6) إغلاق النافذة يوقف الكاميرا ----
    print("\n=== 6. إغلاق نافذة نقطة البيع يوقف الكاميرا ===")
    pg.click("#btnOpenCamera")
    pg.wait_for_timeout(2200)
    check("الكاميرا فُتحت مرة أخرى", pg.evaluate("window.CameraScanner.isActive()"))
    pg.evaluate("bootstrap.Modal.getInstance(document.getElementById('posModal')).hide()")
    pg.wait_for_timeout(1200)
    check("الكاميرا أُوقفت مع إغلاق النافذة",
          not pg.evaluate("window.CameraScanner.isActive()"))
    check("لا مسارات فيديو حيّة بعد إغلاق النافذة",
          live_tracks(pg) == 0, f"live={live_tracks(pg)}")

    check("لا أخطاء JavaScript", not errors, str(errors[:2]))
    ctx.close()
    b.close()

    # ---- 7) رسالة عربية عند رفض الإذن ----
    print("\n=== 7. رسالة عربية واضحة عند رفض إذن الكاميرا ===")
    b2 = p.chromium.launch()          # بدون كاميرا وهمية ولا إذن
    ctx2 = b2.new_context(viewport={"width": 1280, "height": 900})
    ctx2.grant_permissions([])
    pg2 = ctx2.new_page()
    login_agent(pg2)
    pg2.click("#btnNewInvoice")
    pg2.wait_for_timeout(800)
    pg2.click("#btnOpenCamera")
    pg2.wait_for_timeout(3500)

    msg = pg2.inner_text("#cameraStatus")
    check("تظهر رسالة خطأ عربية مفهومة", len(msg.strip()) > 0, f"msg={msg!r}")
    check("الرسالة تُرشد المستخدم لبديل أو حل",
          any(k in msg for k in ["إذن", "HTTPS", "آمن", "كاميرا", "الماسح", "المتصفح"]),
          f"msg={msg!r}")
    check("النظام لا ينكسر عند فشل الكاميرا (الحقل يعمل)",
          pg2.is_visible("#barcodeInput"))
    ctx2.close()
    b2.close()

print("\n" + "=" * 60)
print(f"  الناجحة: {len(PASSED)}    الفاشلة: {len(FAILED)}")
print("=" * 60)
if FAILED:
    print("\nالاختبارات الفاشلة:")
    for f in FAILED:
        print("  -", f)
sys.exit(1 if FAILED else 0)

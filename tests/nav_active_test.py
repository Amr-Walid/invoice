#!/usr/bin/env python3
"""اختبار تعليم التبويب الحالي في الشريط الجانبي.

المشكلة التي وُلد منها: التعليم كان يقارن المتحكم وحده، فمتحكم يخدم عدة
تبويبات (Purchases) يُعلِّم أولها دائمًا — فتقف على «أذون الاستلام» والشريط
يقول إنك في «بانتظار التوريد». وتبويبات أخرى لم تُقارَن أصلًا فلم تُعلَّم قط.

لماذا اختبار آلي لمسألة «شكلية»: التعليم الكاذب أسوأ من غيابه، لأن المستخدم
يبني عليه قراره فينتقل خطأً. والفحص اليدوي لثلاثين رابطًا × ثلاثة أدوار لا
يُكرَّر مع كل تعديل، فيعود العيب بعد أول رابط جديد.

القاعدة المُختبَرة: لكل شاشة رابط واحد معلَّم بالضبط، وهو الرابط الصحيح.
"""
import html
import re
import sys
import urllib.parse

import requests

BASE = "http://localhost:5080"
PASS = FAIL = 0


def chk(ok, label, extra=""):
    global PASS, FAIL
    if ok:
        PASS += 1
        print(f"  ✅ {label}")
    else:
        FAIL += 1
        print(f"  ❌ {label} {extra}")


def login(email, password):
    s = requests.Session()
    r = s.get(f"{BASE}/Account/Login", timeout=30)
    token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', r.text).group(1)
    r = s.post(
        f"{BASE}/Account/Login",
        data={"Email": email, "Password": password, "__RequestVerificationToken": token},
        timeout=30,
        allow_redirects=True,
    )
    assert "/Account/Login" not in r.url, f"فشل الدخول بـ {email}"
    return s


def active_links(session, path):
    """يعيد (رمز الحالة، أسماء الروابط المعلَّمة).

    نستخرج الاسم من <span> داخل الرابط لأنه النص الذي يقرأه المستخدم فعلًا،
    وهو ما يجب أن يطابق عنوان الصفحة في ذهنه.
    """
    r = session.get(f"{BASE}{path}", timeout=30, allow_redirects=False)
    if r.status_code != 200:
        return r.status_code, []
    body = html.unescape(r.text)
    # الرابط المعلَّم: class يحتوي nav-link و active معًا
    names = []
    for m in re.finditer(r'<a class="nav-link[^"]*\bactive\b[^"]*"(.*?)</a>', body, re.S):
        span = re.search(r"<span>(.*?)</span>", m.group(1), re.S)
        names.append(span.group(1).strip() if span else "؟")
    return 200, names


def expect(session, path, expected, role):
    code, names = active_links(session, path)
    if code != 200:
        chk(False, f"[{role}] {path} → {expected}", f"(HTTP {code})")
        return
    chk(len(names) == 1, f"[{role}] {path}: رابط معلَّم واحد", f"(المعلَّم: {names})")
    chk(
        names == [expected],
        f"[{role}] {path} يُعلِّم «{expected}»",
        f"(الفعلي: {names})",
    )


def first_id(session, path, pattern):
    """يستخرج أول معرّف من قائمة، لاختبار الشاشات الفرعية بمعرّف حقيقي."""
    r = session.get(f"{BASE}{path}", timeout=30)
    m = re.search(pattern, r.text)
    return m.group(1) if m else None


print("\n=== 1) المدير: كل تبويب يُعلِّم نفسه ===")
admin = login("admin@pos.local", "Admin@123")
for path, name in [
    ("/Dashboard", "لوحة التحكم"),
    ("/Pos", "نقطة البيع"),
    ("/Customers", "العملاء"),
    ("/Returns", "مرتجع جديد"),
    ("/Returns/List", "سجل المرتجعات"),
    ("/Warehouses", "المخازن"),
    ("/Transfers/All", "التحويلات"),
    ("/Transfers/InTransit", "في الطريق"),
    ("/Suppliers", "الموردون"),
    ("/Purchases", "أوامر الشراء"),
    ("/Purchases/Open", "بانتظار التوريد"),
    ("/Purchases/Receipts", "أذون الاستلام"),
    ("/Products", "المنتجات"),
    ("/Brands", "البراندات"),
    ("/Categories", "التصنيفات"),
    ("/Coupons", "أكواد الخصم"),
    ("/Reports", "تقرير المبيعات"),
    ("/Reports/TopProducts", "الأكثر مبيعًا"),
    ("/Purchases/Report", "تقرير المشتريات"),
    ("/Users", "المستخدمون"),
    ("/Audit", "سجل العمليات"),
]:
    expect(admin, path, name, "مدير")

print("\n=== 2) المدير: الشاشات الفرعية تُعلِّم قائمتها الأم ===")
for path, name in [
    ("/Suppliers/Create", "الموردون"),
    ("/Purchases/Create", "أوامر الشراء"),
    ("/Warehouses/Create", "المخازن"),
    ("/Products/Create", "المنتجات"),
    ("/Brands/Create", "البراندات"),
    ("/Categories/Create", "التصنيفات"),
    ("/Coupons/Create", "أكواد الخصم"),
    ("/Transfers/Create", "التحويلات"),
]:
    expect(admin, path, name, "مدير")

# بمعرّفات حقيقية: التفاصيل هي أكثر الشاشات زيارةً، وكانت بلا تعليم صحيح
pid = first_id(admin, "/Purchases", r"/Purchases/Details/(\d+)")
if pid:
    expect(admin, f"/Purchases/Details/{pid}", "أوامر الشراء", "مدير")
else:
    print("  ⚠️  لا أوامر شراء لاختبار التفاصيل")

sid = first_id(admin, "/Suppliers", r"/Suppliers/Details/(\d+)")
if sid:
    expect(admin, f"/Suppliers/Details/{sid}", "الموردون", "مدير")

wid = first_id(admin, "/Warehouses", r"/Warehouses/Stock/(\d+)")
if wid:
    expect(admin, f"/Warehouses/Stock/{wid}", "المخازن", "مدير")

rid = first_id(admin, "/Returns/List", r"/Returns/Details/(\d+)")
if rid:
    # تفاصيل المرتجع تنتسب للسجل لا لشاشة «مرتجع جديد»: من السجل جاء
    expect(admin, f"/Returns/Details/{rid}", "سجل المرتجعات", "مدير")

recid = first_id(admin, "/Purchases/Receipts", r"/Purchases/Receipt/(\d+)")
if recid:
    # إذن الاستلام صفحة طباعة تخفي الشريط، فنتحقق فقط أنها لا تُعلِّم خطأً
    code, names = active_links(admin, f"/Purchases/Receipt/{recid}")
    chk(
        names in ([], ["أذون الاستلام"]),
        "[مدير] إذن الاستلام لا يُعلِّم رابطًا خاطئًا",
        f"(الفعلي: {names})",
    )

print("\n=== 3) أمين المخزن: تبويباته السبعة ===")
keeper = login("keeper@pos.local", "Keeper@123")
for path, name in [
    ("/Warehouses", "مخازني"),
    ("/Transfers/Incoming", "طلبات واردة"),
    ("/Transfers", "طلباتي"),
    ("/Transfers/InTransit", "في الطريق"),
    ("/Purchases/Open", "بانتظار التوريد"),
    ("/Purchases/Receipts", "أذون الاستلام"),
    ("/Suppliers", "الموردون"),
]:
    expect(keeper, path, name, "أمين")

# العيب الأصلي بعينه: أمين المخزن على أذون الاستلام كان يُعلَّم له
# «بانتظار التوريد». نثبّته باختبار صريح لا يُحذف.
code, names = active_links(keeper, "/Purchases/Receipts")
chk(
    "بانتظار التوريد" not in names,
    "[أمين] /Purchases/Receipts لا يُعلِّم «بانتظار التوريد» (العيب الأصلي)",
    f"(الفعلي: {names})",
)

# تفاصيل أمر الشراء عند الأمين تنتسب لـ «بانتظار التوريد» لأنه لا يرى قائمة أوامر
kpid = first_id(keeper, "/Purchases/Open", r"/Purchases/Details/(\d+)")
if kpid:
    expect(keeper, f"/Purchases/Details/{kpid}", "بانتظار التوريد", "أمين")

print("\n=== 4) المندوب: تبويباته الخمسة ===")
agent = login("agent@pos.local", "Agent@123")
for path, name in [
    ("/Pos", "الرئيسية"),
    ("/AgentReports", "تقاريري"),
    ("/Returns", "مرتجع"),
    ("/Customers", "العملاء"),
    ("/Transfers", "طلب قطع من مخزن"),
]:
    expect(agent, path, name, "مندوب")

# المندوب لا يملك «سجل المرتجعات»، فتفاصيل مرتجعه تنتسب لـ «مرتجع»
agid = first_id(agent, "/Customers", r"/Customers/Details/(\d+)")
if agid:
    expect(agent, f"/Customers/Details/{agid}", "العملاء", "مندوب")

print(f"\n{'='*46}\nنجح {PASS} · فشل {FAIL}\n{'='*46}")
sys.exit(1 if FAIL else 0)

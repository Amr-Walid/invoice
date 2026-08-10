#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""حدود الأدوار: من يرى ماذا — ومن لا يراه.

هذا الملف يحرس ثلاثة أشياء اكتُشف خللها فعلًا في مراجعة ما قبل العرض:

١) **تسريب التكاليف**: كان أمين المخزن يرى تكلفة الوحدة وإجمالي أمر الشراء
   وإجمالي التعامل مع المورد. التكلفة معلومة تعاقدية بين المدير والمورد،
   وعمل أمين المخزن أن يعدّ القطع ويوقّع الاستلام. (وهو نفس القرار الذي
   بُني عليه غياب تقارير الربح.)

٢) **تسريب المخازن**: كانت شاشة /Purchases تعرض لأمين المخزن أوامر المخازن
   كلها، فيرى مشتريات فروع ليست شغله بمجرد فتح الرابط.

٣) **الشاشات المحظورة**: كل شاشة إدارية يجب أن ترفض من ليس مديرًا، ولا
   يكفي أن تُخفى من القائمة الجانبية — الرابط يُكتب باليد.

القياس هنا على النص المعروض لا على رمز الحالة وحده: صفحة تعيد ٢٠٠ وفيها
رقم التكلفة هي تسريب، مهما كان رمزها.
"""
import re
import sys
import requests

B = "http://localhost:8080"

ok = 0
bad = []


def chk(cond, name, detail=""):
    global ok
    if cond:
        ok += 1
        print(f"  ✅ {name}")
    else:
        bad.append(f"{name} — {detail}")
        print(f"  ❌ {name}  {detail}")


def token(s, path):
    r = s.get(f"{B}{path}", timeout=30)
    m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', r.text)
    return m.group(1) if m else ""


def login(email, password):
    s = requests.Session()
    s.post(f"{B}/Account/Login",
           data={"Email": email, "Password": password,
                 "__RequestVerificationToken": token(s, "/Account/Login")},
           timeout=30)
    return s


def body(s, path):
    r = s.get(f"{B}{path}", timeout=30, allow_redirects=True)
    return r.status_code, r.text, r.url


def status(s, path):
    r = s.get(f"{B}{path}", timeout=30, allow_redirects=False)
    return r.status_code


admin = login("admin@pos.local", "Admin@123")
keeper = login("keeper@pos.local", "Keeper@123")
agent = login("agent@pos.local", "Agent@123")

# مصطلحات المال في شاشات الشراء: وجودها لغير المدير تسريب
COST_TERMS = ["تكلفة الوحدة", "إجمالي المشتريات", "إجمالي التعامل", "إجمالي السطر"]


print("\n=== ١) التكاليف محجوبة عن أمين المخزن ===")
for path in ["/Purchases", "/Purchases/Open", "/Purchases/Receipts", "/Suppliers"]:
    code, html, _ = body(keeper, path)
    if code != 200:
        chk(False, f"{path} تُفتح لأمين المخزن", f"code={code}")
        continue
    leaked = [t for t in COST_TERMS if t in html]
    chk(not leaked, f"{path} بلا تكاليف لأمين المخزن", f"ظهر: {leaked}")

# تفاصيل أمر شراء بعينه: أدق موضع كان يسرّب الأرقام
code, html, _ = body(keeper, "/Purchases/Open")
oid = re.search(r'/Purchases/Details/(\d+)', html)
if oid:
    code, d, _ = body(keeper, f"/Purchases/Details/{oid.group(1)}")
    leaked = [t for t in COST_TERMS if t in d]
    chk(code == 200 and not leaked,
        f"/Purchases/Details/{oid.group(1)} بلا تكاليف لأمين المخزن",
        f"code={code} ظهر: {leaked}")
else:
    chk(False, "أمين المخزن عنده أمر مفتوح ليُفحَص", "لا أوامر")


print("\n=== ٢) المدير يرى التكاليف (الحجب ليس تعطيلًا) ===")
# الحجب الصحيح لا يعني إخفاء الرقم عن صاحبه: لو اختفى عن المدير أيضًا
# لكان «الإصلاح» تعطيلًا للميزة لا ضبطًا للصلاحية.
code, html, _ = body(admin, "/Purchases")
chk(code == 200 and "إجمالي المشتريات" in html,
    "/Purchases تُظهر إجمالي المشتريات للمدير", f"code={code}")

oid = re.search(r'/Purchases/Details/(\d+)', html)
if oid:
    code, d, _ = body(admin, f"/Purchases/Details/{oid.group(1)}")
    chk(code == 200 and "تكلفة الوحدة" in d,
        "تفاصيل الأمر تُظهر تكلفة الوحدة للمدير", f"code={code}")

code, html, _ = body(admin, "/Suppliers")
chk(code == 200 and "إجمالي التعامل" in html,
    "/Suppliers تُظهر إجمالي التعامل للمدير", f"code={code}")


print("\n=== ٣) أوامر الشراء مقيَّدة بمخزن أمين المخزن ===")
# نقرأ مخزن الأمين من شاشته، ثم نتأكد أن كل أمر يراه على مخزنه وحده.
code, html, _ = body(keeper, "/Purchases")
chk(code == 200, "/Purchases تُفتح لأمين المخزن", f"code={code}")
wh_names = set(re.findall(r'badge badge-soft-\w+[^>]*>\s*([^<]+?)\s*<', html))
# المخازن الظاهرة في الجدول يجب أن تكون مخزنًا واحدًا على الأكثر
mentioned = [n for n in wh_names if "مخزن" in n or "معرض" in n or "فرع" in n]
chk(len(set(mentioned)) <= 1,
    "لا تظهر لأمين المخزن أوامر أكثر من مخزن", f"ظهرت: {sorted(set(mentioned))}")

# المخزن مقفول: التصفية بمخزن آخر لا تفتح له بيانات غيره
code, other, _ = body(keeper, "/Purchases?warehouseId=1")
mentioned2 = [n for n in set(re.findall(r'badge badge-soft-\w+[^>]*>\s*([^<]+?)\s*<', other))
              if "مخزن" in n or "معرض" in n or "فرع" in n]
chk(sorted(set(mentioned2)) == sorted(set(mentioned)),
    "تغيير warehouseId يدويًا لا يفتح مخزنًا آخر لأمين المخزن",
    f"قبل={sorted(set(mentioned))} بعد={sorted(set(mentioned2))}")


print("\n=== ٤) الشاشات الإدارية ترفض غير المدير ===")
ADMIN_ONLY = ["/Users", "/Products/Create", "/Brands", "/Categories",
              "/Coupons", "/Audit", "/Transfers/All"]
# المسار يُثبَّت بفحص المدير أولًا: لو كُتب مسارٌ لا وجود له لعاد ٤٠٤ للجميع
# فمرّت أسطر «ممنوع» كلها بلا أن تفحص شيئًا — منعٌ كاذب أخطر من تسريب معروف.
for path in ADMIN_ONLY:
    chk(status(admin, path) == 200, f"[مدير] {path} مسموح (المسار موجود)",
        f"code={status(admin, path)}")
for path in ADMIN_ONLY:
    for who, s in (("أمين", keeper), ("مندوب", agent)):
        c = status(s, path)
        chk(c in (302, 403, 404), f"[{who}] {path} ممنوع", f"code={c}")


print("\n=== ٥) المندوب لا يرى إلا فواتيره ===")
code, html, _ = body(agent, "/AgentReports")
mine = set(re.findall(r'/AgentReports/Details/(\d+)', html))
chk(code == 200 and len(mine) > 0, "المندوب يرى فواتيره", f"code={code} عدد={len(mine)}")

code, allrep, _ = body(admin, "/Reports")
everything = set(re.findall(r'/Reports/Details/(\d+)', allrep))
chk(len(everything) > len(mine),
    "المدير يرى فواتير أكثر من المندوب (فيها فواتير غيره)",
    f"مدير={len(everything)} مندوب={len(mine)}")

# فاتورة ليست للمندوب: لا تُفتح له
not_mine = sorted(everything - mine)
if not_mine:
    c = status(agent, f"/AgentReports/Details/{not_mine[0]}")
    chk(c in (403, 404, 302), "المندوب لا يفتح فاتورة غيره", f"code={c}")
else:
    chk(False, "توجد فاتورة لغير المندوب لفحص العزل", "لا توجد")

# ==================== أمين المخزن ليس بائعًا ====================
# كان هنا تعليقٌ يَعِد بهذا الفحص ولم يُكتب، فمرّ الخلل: PosController كان
# يحمل [Authorize] المجرّدة (أي مستخدم مسجَّل)، فكان أمين المخزن يفتح نقطة
# البيع ويُصدر فاتورة كاملة تُخصم من رصيد مخزنه. القائمة الجانبية لا تُظهر
# له الرابط، لكن إخفاء الرابط ليس صلاحية.
print("\n=== أمين المخزن لا يبيع ولا يُرجع ولا يرى العملاء ===")

for path in ["/Pos", "/Returns", "/Returns/List", "/Customers", "/AgentReports"]:
    _, html_, url = body(keeper, path)
    denied = "AccessDenied" in url or "غير مصرح" in html_
    chk(denied, f"[أمين مخزن] {path} ممنوع", f"url={url}")

# الأهمّ: منْعُ الشاشة لا يكفي — الطلب المباشر على الـ endpoint هو ما أنشأ
# فاتورةً فعلًا. نقيس الأثر: لا فاتورة جديدة تُنسب لأمين المخزن.
before = len(set(re.findall(r'/Reports/Details/(\d+)', body(admin, "/Reports")[1])))
r = keeper.post(f"{B}/Pos/Checkout",
                json={"items": [{"productId": 3, "quantity": 1}]},
                headers={"RequestVerificationToken": token(keeper, "/Warehouses")},
                timeout=30, allow_redirects=True)
sold = '"success":true' in r.text.replace(" ", "")
after = len(set(re.findall(r'/Reports/Details/(\d+)', body(admin, "/Reports")[1])))
chk(not sold and after == before,
    "[أمين مخزن] POST /Pos/Checkout لا يُنشئ فاتورة",
    f"sold={sold} قبل={before} بعد={after}")

# وعمله الأصلي سليم: التقييد لا يجوز أن يشلّ الدور
for path in ["/Warehouses", "/Transfers/Incoming", "/Transfers/InTransit",
             "/Purchases/Open", "/Purchases/Receipts", "/Suppliers"]:
    _, _, url = body(keeper, path)
    chk("AccessDenied" not in url, f"[أمين مخزن] {path} ما زال يعمل", f"url={url}")

# والبائعان يبيعان كما كان — لا انحدار
chk(status(agent, "/Pos") == 200, "[مندوب] /Pos مسموح")
chk(status(admin, "/Pos") == 200, "[مدير] /Pos مسموح")
for path in ["/Returns", "/Customers"]:
    _, _, url = body(agent, path)
    chk("AccessDenied" not in url, f"[مندوب] {path} مسموح", f"url={url}")


print("\n" + "=" * 54)
print(f"نجح {ok} · فشل {len(bad)}")
print("=" * 54)
if bad:
    print("\nالفاشل:")
    for b in bad:
        print("  -", b)
sys.exit(1 if bad else 0)

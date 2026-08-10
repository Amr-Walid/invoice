#!/usr/bin/env python3
"""تجهيز قاعدة بيانات العرض ببيانات واقعية.

لماذا عبر الواجهة لا بحقن SQL: البيانات المحقونة تتجاوز قواعد العمل، فتظهر
في العرض حالات لا يمكن أن تنشأ من التشغيل الحقيقي (رصيد بلا حركة، أمر مستلم
بلا إذن). ما يُبنى بالواجهة يكون قد مرّ بكل ما يمرّ به المستخدم — فإن نجح
التجهيز فقد اختبرنا المسار كاملًا مرةً أخرى.

الاستخدام: احذف possystem_dev.db، شغّل التطبيق، ثم شغّل هذا الملف.
"""
import html
import re
import sys

import requests

B = "http://localhost:5080"


def login(email, password):
    s = requests.Session()
    r = s.get(f"{B}/Account/Login", timeout=30)
    t = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', r.text).group(1)
    r = s.post(
        f"{B}/Account/Login",
        data={"Email": email, "Password": password, "__RequestVerificationToken": t},
        timeout=30,
    )
    assert "/Account/Login" not in r.url, f"فشل الدخول: {email}"
    return s


def token(s, path):
    r = s.get(f"{B}{path}", timeout=30)
    m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', r.text)
    return m.group(1) if m else None


def post(s, path, data, form_path=None):
    t = token(s, form_path or path)
    if t:
        data = dict(data, __RequestVerificationToken=t)
    return s.post(f"{B}{path}", data=data, timeout=60, allow_redirects=True)


admin = login("admin@pos.local", "Admin@123")
print("دخل المدير")

# ---------- مخازن الفرع ----------
# ثلاثة مخازن تحكي قصة: معرض يبيع، مخزن يخزّن، وفرع ثانٍ يُطلب منه.
for code, name, mgr, phone, addr in [
    ("SHOW", "معرض المعادي", "محمود عبد الرحمن", "0225190001", "١٥ شارع النصر، المعادي"),
    ("STORE", "مخزن الأمين المركزي", "سعيد الجندي", "0225190002", "المنطقة الصناعية، طرة"),
    ("BR-NASR", "فرع مدينة نصر", "هشام فؤاد", "0224010003", "٧ شارع عباس العقاد"),
]:
    r = post(
        admin,
        "/Warehouses/Create",
        {"Code": code, "Name": name, "ManagerName": mgr, "Phone": phone,
         "Address": addr, "IsActive": "true"},
    )
    print(f"  مخزن {name}: {r.status_code}")

# ---------- عملاء واقعيون ----------
# العميل يُنشأ من نقطة البيع في التشغيل الحقيقي، فننشئه بفواتيره لاحقًا.

# ---------- ربط الموظفين بمخازنهم ----------
# بلا هذا الربط يرى أمين المخزن شاشات فارغة: عمله كله مقيَّد بمخزنه، فإن بقي
# على المخزن الافتراضي بينما الحركة على المخازن الجديدة ظهر النظام معطّلًا
# أمام من يعرضه — وهو أسوأ ما يمكن أن يحدث في عرض.
def bind_user(email, warehouse_id):
    """يربط مستخدمًا بمخزن عبر شاشة المستخدمين.

    المعرّف يُقرأ من زر «تعطيل/تفعيل» داخل صف الجدول نفسه، لا من نموذج
    ChangeWarehouse: نوافذ المخزن كلها تُرسم بعد الجدول في كتلة واحدة، فأول
    نموذج تجده في «صف» المستخدم هو في الحقيقة نافذة مستخدمٍ آخر — وهذا ما
    جعل ربط المندوب ينقل أمين المخزن بدلًا منه (نجاحٌ كاذب يُبلّغ ✓).
    """
    body = admin.get(f"{B}/Users", timeout=30).text
    # نقصّ الجدول وحده حتى لا تتسرّب النوافذ إلى آخر صف
    table = body.split("</tbody>")[0]
    for row in table.split("<tr")[1:]:
        if email not in row:
            continue
        m = re.search(r'action="/Users/ToggleActive/([^"]+)"', row)
        if not m:
            return False
        uid = m.group(1)
        res = admin.post(f"{B}/Users/ChangeWarehouse/{uid}",
                         data={"warehouseId": warehouse_id,
                               "__RequestVerificationToken": token(admin, "/Users")},
                         timeout=60)
        if res.status_code >= 400:
            return False
        # التحقق من الأثر لا من رمز الحالة: ChangeWarehouse يعيد تحويلًا
        # ناجحًا (302 إلى Index) حتى حين يرفض الطلب ويكتب سببَ الرفض في
        # TempData — فالرمز وحده لا يقول إن المخزن تغيّر فعلًا.
        after = admin.get(f"{B}/Users", timeout=30).text
        block = after.split(f'id="whModal-{uid}"')
        if len(block) < 2:
            return False
        sel = block[1].split("</select>")[0]
        return re.search(rf'value="{warehouse_id}" selected', sel) is not None
    return False


# ---------- موردون ----------
for name, phone, contact, email, addr in [
    ("شركة النيل لتوزيع الموبايلات", "01001234567", "أ. كريم منصور",
     "sales@nile-mobiles.com", "٤٢ شارع عبد العزيز، وسط البلد"),
    ("الشرق الأوسط للإلكترونيات", "01109876543", "أ. داليا سمير",
     "info@me-electronics.com", "برج الصفا، مدينة نصر"),
    ("مؤسسة الفتح للإكسسوارات", "01277001122", "أ. طارق الديب",
     None, "سوق الموبايلات، العتبة"),
]:
    data = {"Name": name, "Phone": phone, "ContactPerson": contact,
            "Address": addr, "IsActive": "true"}
    if email:
        data["Email"] = email
    r = post(admin, "/Suppliers/Create", data)
    print(f"  مورد {name}: {r.status_code}")

print("\nتم تجهيز المخازن والموردين. الخطوة التالية تُدار من ملف المراجعة.")

# ================== الشراء: بضاعة تدخل بمسارها الكامل ==================
import json

# الكميات ثابتة لا عشوائية: كانت random.choice بلا بذرة، فكل إعادة تجهيز
# تُغيّر إجماليات أوامر الشراء — وسكربت العرض يذكر أرقامًا بعينها، فيقف
# العارض أمام مدير الفرع يقرأ رقمًا لا يطابق الشاشة. الأرقام مختلفة بين
# الأسطر (فتبدو واقعية) لكنها هي نفسها في كل تجهيز (فالسكربت يبقى صادقًا).
PO_QTY = [10, 8, 12, 15, 20]

def ids(s, path, pattern):
    r = s.get(f"{B}{path}", timeout=30)
    return [int(x) for x in dict.fromkeys(re.findall(pattern, r.text))]

sup_ids = ids(admin, "/Suppliers", r"/Suppliers/Details/(\d+)")

# المخازن تُعرَّف بالكود لا بترتيبها في الصفحة: تشغيل الاختبارات قبل التجهيز
# يترك مخازن أخرى في القائمة، فالاعتماد على الترتيب ربط المندوب بـ«مخزن فرع
# الهرم» (مخزن اختبار) بدل المعرض — والبضاعة كلها دخلت المخزن الخطأ.
wh_by_code = {}
r = admin.get(f"{B}/Warehouses", timeout=30)
for row in html.unescape(r.text).split("<tr")[1:]:
    m = re.search(r'/Warehouses/Stock/(\d+)', row)
    code = re.search(r'badge badge-soft-gray num[^>]*>\s*([A-Za-z0-9\-]+)\s*<', row)
    if m and code:
        wh_by_code[code.group(1)] = int(m.group(1))

prod_ids = ids(admin, "/Products", r"/Products/Edit/(\d+)")
print(f"\nموردون={sup_ids} مخازن={wh_by_code} منتجات={len(prod_ids)}")

missing = [c for c in ("SHOW", "STORE", "BR-NASR") if c not in wh_by_code]
if missing:
    print(f"❌ مخازن العرض غير موجودة: {missing}")
    raise SystemExit(1)

SHOW, STORE, BRANCH = wh_by_code["SHOW"], wh_by_code["STORE"], wh_by_code["BR-NASR"]

# الموردون كذلك بالاسم لا بالترتيب، لنفس السبب
def sup_by_name(part):
    # النص يُفكَّك من ترميز HTML أولًا: Razor يكتب العربية كرموز رقمية
    # (&#x634;...) فالبحث عن اسم عربي في النص الخام لا يجد شيئًا أبدًا.
    body = html.unescape(admin.get(f"{B}/Suppliers", timeout=30).text)
    for row in body.split("<tr")[1:]:
        if part in row:
            m = re.search(r'/Suppliers/Details/(\d+)', row)
            if m:
                return int(m.group(1))
    return None

sup_ids = [sup_by_name("النيل"), sup_by_name("الشرق الأوسط"), sup_by_name("الفتح")]
if not all(sup_ids):
    print(f"❌ موردو العرض غير موجودين: {sup_ids}")
    raise SystemExit(1)

# أمين المخزن على المخزن المركزي (منه تُشحن التحويلات وإليه يُورَّد)،
# والمندوب على المعرض (منه يبيع). هذا ما يجعل شاشاتهما مأهولة بعمل حقيقي.
print(f"  ربط أمين المخزن بالمخزن المركزي: {'✓' if bind_user('keeper@pos.local', STORE) else '✗'}")
print(f"  ربط المندوب بالمعرض: {'✓' if bind_user('agent@pos.local', SHOW) else '✗'}")

def create_po(supplier, warehouse, lines, discount=0, expected=None, notes=None):
    """lines = [(productId, qty, unitCost)]"""
    data = {"supplierId": supplier, "warehouseId": warehouse,
            "discountPercentage": discount}
    if expected: data["expectedDate"] = expected
    if notes: data["notes"] = notes
    for pid, qty, cost in lines:
        data.setdefault("productIds", []).append(pid)
        data.setdefault("quantities", []).append(qty)
        data.setdefault("unitCosts", []).append(cost)
    r = post(admin, "/Purchases/Create", data, form_path=f"/Purchases/Create?warehouseId={warehouse}")
    m = re.search(r'/Purchases/Details/(\d+)', r.url)
    return int(m.group(1)) if m else None

def confirm_po(oid):
    return post(admin, f"/Purchases/Confirm/{oid}", {"id": oid},
                form_path=f"/Purchases/Details/{oid}")

def receive_po(session, oid, portion=1.0):
    """يستلم نسبة من المتبقّي في كل سطر — لتظهر حالات جزئية واقعية."""
    r = session.get(f"{B}/Purchases/Details/{oid}", timeout=30)
    # المتبقّي يُقرأ من max في حقل الكمية: هو الحد الذي تفرضه الشاشة نفسها،
    # فلا نخترع رقمًا قد يرفضه السيرفر ويترك القاعدة ناقصة بلا إشعار.
    item_ids = re.findall(r'name="itemIds" value="(\d+)"', r.text)
    maxes = re.findall(r'name="quantities"[^>]*?max="(\d+)"', r.text, re.S)
    if not item_ids:
        return None
    data = {"id": oid, "itemIds": [], "quantities": []}
    for iid, rem in zip(item_ids, maxes):
        rem = int(rem)
        q = max(1, int(rem * portion)) if rem else 0
        data["itemIds"].append(iid)
        data["quantities"].append(min(q, rem))
    return post(session, f"/Purchases/Receive/{oid}", data,
                form_path=f"/Purchases/Details/{oid}")

# ---------- قصة المشتريات ----------
# نبني حالات يراها مدير الفرع كما تحدث فعلًا: مكتمل، جزئي، بانتظار، وملغى.
P = prod_ids
COST = {}   # تكلفة تقديرية = ٧٥٪ من سعر البيع، وهي نسبة معقولة في التجزئة
r = admin.get(f"{B}/Products", timeout=30).text
for pid, price in zip(P, re.findall(r'<td class="num[^"]*">([\d,]+\.\d\d)</td>', r)[:len(P)]):
    COST[pid] = round(float(price.replace(",", "")) * 0.75, 2)
for pid in P:
    COST.setdefault(pid, 500.0)

# الأصناف تُختار بالباركود لا بموقعها في الصفحة.
# صفحة المنتجات مرتّبة بالاسم لا بالرقم، فكان P[3:5] يعني «الباور بانك» بينما
# الملاحظة المكتوبة على الأمر تقول «ساعات ذكية». الأثر لم يكن تجميليًا: مدير
# الفرع يفتح الأمر أثناء العرض فيجد عنوانًا يناقض محتواه، وهي أسرع طريقة
# لفقد الثقة في البيانات كلها. الباركود هوية ثابتة لا يغيّرها ترتيب العرض.
BC = {}
for row in html.unescape(r).split("<tr")[1:]:
    m = re.search(r'/Products/Edit/(\d+)', row)
    b = re.search(r'(\d{13})', row)
    if m and b:
        BC[b.group(1)] = int(m.group(1))

SMART8, HOT40I, NOTE40 = BC["6941238701234"], BC["6941238701241"], BC["6941238701258"]
WATCH_XW1, WATCH_PRO = BC["6941238702231"], BC["6941238702248"]
PB10, PB20, CHARGER = BC["6941238703221"], BC["6941238703238"], BC["6941238703245"]

PHONES = [SMART8, HOT40I, NOTE40]
WATCHES = [WATCH_XW1, WATCH_PRO]
ACCESSORIES = [PB10, PB20, CHARGER]

missing_bc = [k for k in ("6941238701234", "6941238702231", "6941238703245") if k not in BC]
if missing_bc:
    print(f"❌ باركودات العرض غير موجودة: {missing_bc}")
    raise SystemExit(1)

stories = [
    # (مورد, مخزن, أصناف, خصم, ملاحظة, حالة مطلوبة)
    (sup_ids[0], STORE, PHONES, 3, "توريد الشهر — دفعة الموبايلات", "received"),
    (sup_ids[1], STORE, WATCHES, 0, "ساعات ذكية للمعرض", "partial"),
    (sup_ids[2], SHOW, ACCESSORIES, 5, "إكسسوارات ومحوّلات", "received"),
    (sup_ids[0], BRANCH, [SMART8, HOT40I], 0, "تجهيز فرع مدينة نصر", "confirmed"),
    (sup_ids[1], SHOW, [NOTE40, WATCH_XW1], 0, "طلب أُلغي — المورد رفع السعر", "cancelled"),
]
created = []
for sup, whid, items, disc, note, want in stories:
    lines = [(pid, PO_QTY[i % len(PO_QTY)], COST[pid]) for i, pid in enumerate(items)]
    oid = create_po(sup, whid, lines, discount=disc, notes=note)
    if not oid:
        print(f"  ⚠️ فشل إنشاء أمر: {note}")
        continue
    created.append((oid, want))
    confirm_po(oid)
    if want == "received":
        receive_po(admin, oid, 1.0)
    elif want == "partial":
        receive_po(admin, oid, 0.5)
    elif want == "cancelled":
        post(admin, f"/Purchases/Cancel/{oid}",
             {"id": oid, "reason": "المورد رفع السعر بعد الاعتماد"},
             form_path=f"/Purchases/Details/{oid}")
    print(f"  أمر #{oid} ({want}): {note}")

# ================== أدوات التحويل ==================
def create_transfer(session, to_wh, from_wh, lines, notes=None):
    data = {"warehouseId": to_wh, "fromWarehouseId": from_wh,
            "productIds": [p for p, _ in lines],
            "quantities": [q for _, q in lines]}
    if notes: data["notes"] = notes
    r = post(session, "/Transfers/Create", data,
             form_path=f"/Transfers/Create?warehouseId={to_wh}")
    m = re.search(r'/Transfers/Details/(\d+)', r.url)
    return int(m.group(1)) if m else None

def tr_action(session, action, tid, extra=None):
    data = {"id": tid}
    if extra: data.update(extra)
    return post(session, f"/Transfers/{action}/{tid}", data,
                form_path=f"/Transfers/Details/{tid}")

def available_at(session, from_wh, to_wh):
    """أرصدة المخزن المصدر كما تعرضها شاشة التحويل نفسها: {صنف: كمية}.

    الشاشة تكتب أرصدة كل المخازن في data-stock لكل صف، فنقرأ منها بدل
    تخمين كمية. التخمين هو ما جعل الشحن يُرفض كاملًا («الكمية غير كافية»)
    فوصل المعرض فارغًا ورُفضت فواتير المندوب.
    """
    r = session.get(f"{B}/Transfers/Create?warehouseId={to_wh}", timeout=30)
    out = {}
    for chunk in r.text.split('class="cand-row"')[1:]:
        ds = re.search(r'data-stock="([^"]*)"', chunk)
        pid = re.search(r'name="productIds" value="(\d+)"', chunk)
        if not pid:
            continue
        qty = 0
        if ds:
            for pair in ds.group(1).split(","):
                if ":" in pair:
                    w, q = pair.split(":")
                    if int(w) == from_wh:
                        qty = int(q)
        out[int(pid.group(1))] = qty
    return out

def tr_move(session, action, tid):
    """الشحن والاستلام يقرآن الكميات من max الذي تفرضه الشاشة نفسها."""
    r = session.get(f"{B}/Transfers/Details/{tid}", timeout=30)
    pids = re.findall(r'name="productIds" value="(\d+)"', r.text)
    maxes = re.findall(r'name="quantities"[^>]*?max="(\d+)"', r.text, re.S)
    if not pids: return None
    return post(session, f"/Transfers/{action}/{tid}",
                {"id": tid, "productIds": pids, "quantities": maxes[:len(pids)]},
                form_path=f"/Transfers/Details/{tid}")

# ================== توزيع البضاعة على المعرض ==================
# البضاعة تدخل المخزن المركزي بالشراء، ثم تُوزَّع على المعرض بالتحويل: هذه
# هي الدورة الحقيقية. وبدونها يبيع المندوب من معرضٍ فارغ فتُرفض فواتيره —
# وهو ما حدث فعلًا: «لا يوجد رصيد للمنتج … في مخزن معرض المعادي».
avail = available_at(admin, STORE, SHOW)
# نصف المتاح لكل صنف: يبقى رصيدٌ في المخزن المركزي فتبدو الشاشتان عاملتين،
# ولا نطلب أكثر من الموجود فيُرفض الشحن كله.
supply = [(pid, max(1, avail.get(pid, 0) // 2)) for pid in P if avail.get(pid, 0) > 0]
t0 = create_transfer(admin, SHOW, STORE, supply,
                     "تزويد المعرض بالبضاعة الواردة من المخزن المركزي") if supply else None
if t0:
    tr_action(admin, "Approve", t0)
    tr_move(admin, "Ship", t0)
    tr_move(admin, "Receive", t0)
    print(f"  تحويل التزويد #{t0}: وصل المعرض {len(supply)} صنفًا")
else:
    print("  ⚠️ فشل تحويل تزويد المعرض — الفواتير ستُرفض")

# ================== المبيعات: فواتير بعملاء حقيقيين ==================
# البيع من نقطة البيع بالمندوب — فتُنسب الفواتير لمن يبيع فعلًا، ويعمل
# «المندوب يرى فواتيره وحده» على بيانات لها معنى.
agent = login("agent@pos.local", "Agent@123")
print("\nدخل المندوب")

CUSTOMERS = [
    ("محمد السيد عبد الله", "01011223344"),
    ("فاطمة أحمد حسن", "01122334455"),
    ("أحمد مصطفى كامل", "01233445566"),
    ("نورهان إبراهيم", "01099887711"),
    ("خالد عبد المنعم", "01555667788"),
    (None, None),   # بيع نقدي بلا عميل — الحالة الأغلب في التجزئة
]

def checkout(session, lines, coupon=None, cust=None):
    """lines = [(productId, qty)]"""
    t = token(session, "/Pos")
    payload = {"items": [{"productId": p, "quantity": q} for p, q in lines]}
    if coupon: payload["couponCode"] = coupon
    if cust and cust[0]:
        payload["customerName"], payload["customerPhone"] = cust
    r = session.post(f"{B}/Pos/Checkout", json=payload,
                     headers={"RequestVerificationToken": t or ""}, timeout=60)
    try: return r.json()
    except Exception: return {"success": False, "message": r.text[:200]}

# الأصناف تُختار من رصيد المعرض الفعلي: البيع من صنفٍ ليس في مخزن المندوب
# يُرفض بحق، فبناء الفواتير على أسماء ثابتة يُفرغ شاشة المبيعات كلها.
shop = available_at(admin, SHOW, STORE)  # رصيد المعرض بعد التزويد
S = [pid for pid in P if shop.get(pid, 0) >= 4]
if len(S) < 4:
    S = [pid for pid in P if shop.get(pid, 0) >= 1] or list(P)
def pk(i):
    return S[i % len(S)]

# الفاتورة الأخيرة (بكود VIP25) هي التي تُرتجع لاحقًا كـ«تالف»، وعليها يقوم
# أهمّ شرح في فصل المرتجعات: «النظام يرد ما دفعه العميل بعد الخصم لا سعر
# الكتالوج». لذلك تُثبَّت على صنفٍ غالٍ بالباركود: تركها على اختيارٍ متغيّر
# جعلها مرّة شاحنًا بـ٤٨٧ جنيهًا، والفرق بين المُسترد وسعر الكتالوج يصير
# ١٦٢ جنيهًا لا يلفت نظر أحد — فتضيع النقطة كلها في العرض.
sales = [
    ([(pk(0), 1)], None, CUSTOMERS[0]),
    ([(pk(1), 1), (pk(2), 2)], "WELCOME10", CUSTOMERS[1]),
    ([(pk(3), 1)], None, CUSTOMERS[2]),
    ([(pk(0), 1), (pk(2), 1)], None, CUSTOMERS[3]),
    ([(pk(1), 2)], "SAVE20", CUSTOMERS[4]),
    ([(pk(3), 2)], None, CUSTOMERS[5]),
    ([(pk(0), 1), (pk(1), 1)], None, CUSTOMERS[0]),   # عميل متكرر: كشف حسابه يمتلئ
    ([(pk(2), 1)], None, CUSTOMERS[1]),
    ([(SMART8, 1)], "VIP25", CUSTOMERS[2]),           # تُرتجع كتالف: 4,200 ← 3,150
]
ok = 0
for lines, coup, cust in sales:
    res = checkout(agent, lines, coup, cust)
    if res.get("success"): ok += 1
    else: print(f"  ⚠️ فاتورة مرفوضة: {res.get('message')}")
print(f"  فواتير ناجحة: {ok}/{len(sales)}")

# فاتورتان بالمدير: ليرى مدير الفرع أن المدير يرى الكل والمندوب يرى نفسه
for lines, cust in [([(NOTE40, 1)], CUSTOMERS[3]), ([(WATCH_XW1, 1)], CUSTOMERS[4])]:
    checkout(admin, lines, None, cust)

# ================== التحويلات: قصة كاملة بين المخازن ==================
# نبني حالات في كل مرحلة، فيرى مدير الفرع الدورة لا نقطة منها.
keeper = login("keeper@pos.local", "Keeper@123")
print("\nدخل أمين المخزن")

# 1) دورة مكتملة: المعرض طلب من المخزن، ووصلت البضاعة
# الكميات تُقرأ من الرصيد الفعلي في المصدر: طلب صنفٍ ليس عنده يجعل الشحن
# يُرفض كاملًا، فيظهر تحويلٌ «موافق عليه» عالقًا بلا قطعة واحدة.
av1 = available_at(admin, STORE, SHOW)
pick1 = [(pid, min(2, av1[pid])) for pid in P if av1.get(pid, 0) > 0][:2]
t1 = create_transfer(admin, SHOW, STORE, pick1,
                     "نقص في المعرض قبل عطلة نهاية الأسبوع") if pick1 else None
if t1:
    tr_action(admin, "Approve", t1)
    tr_move(admin, "Ship", t1)
    tr_move(admin, "Receive", t1)
    print(f"  تحويل #{t1}: مكتمل")

# 2) في الطريق: شُحن ولم يُستلم — الحالة التي تحتاج متابعة
av2 = available_at(admin, STORE, BRANCH)
pick2 = [(pid, min(3, av2[pid])) for pid in P if av2.get(pid, 0) > 0][:1]
t2 = create_transfer(admin, BRANCH, STORE, pick2, "تجهيز فرع مدينة نصر") if pick2 else None
if t2:
    tr_action(admin, "Approve", t2)
    tr_move(admin, "Ship", t2)
    print(f"  تحويل #{t2}: في الطريق")

# 3) بانتظار الموافقة: طلب جديد على مخزن الأمين
# الصنف بالباركود لا بالموقع: «طلب ساعات للعرض» كان يحمل باور بانك،
# فيفتحه مدير الفرع أثناء العرض ويجد العنوان يناقض المحتوى.
t3 = create_transfer(admin, SHOW, STORE, [(WATCH_PRO, 2)], "طلب ساعات للعرض")
if t3: print(f"  تحويل #{t3}: بانتظار الموافقة")

# 4) مرفوض بسبب: يُظهر أن الرفض يُوثَّق لا يُخفى
t4 = create_transfer(admin, SHOW, BRANCH, [(HOT40I, 10)], "طلب كمية كبيرة")
if t4:
    tr_action(admin, "Reject", t4, {"reason": "الرصيد لا يكفي في الفرع المطلوب منه"})
    print(f"  تحويل #{t4}: مرفوض")

# ================== المرتجعات ==================
# مرتجعان: واحد يُعاد للمخزن (صالح) وآخر لا (تالف) — الفرق جوهري في الرصيد.
inv_ids = [int(x) for x in dict.fromkeys(
    re.findall(r'/Reports/Details/(\d+)', admin.get(f"{B}/Reports", timeout=30).text))]

def make_return(inv_id, reason, restock, notes):
    """المرتجع يُرسل كنموذج لا كـ JSON: Confirm يستقبل CreateReturnRequest
    بالربط النموذجي، فإرسال JSON يعطي 200 وقائمةَ أسطرٍ فارغة — نجاحٌ كاذب."""
    r = admin.get(f"{B}/Returns/Create?invoiceId={inv_id}", timeout=30)
    item_ids = re.findall(r'name="Items\[\d+\]\.InvoiceItemId" value="(\d+)"', r.text)
    if not item_ids:
        return False
    t = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', r.text).group(1)
    data = [
        ("__RequestVerificationToken", t),
        ("InvoiceId", inv_id),
        ("Reason", reason),
        ("Notes", notes),
        ("Items[0].InvoiceItemId", item_ids[0]),
        ("Items[0].Quantity", 1),
    ]
    # نُحاكي المتصفّح بدقّة: الشاشة فيها مفتاحٌ ثم حقلٌ مخفي بنفس الاسم.
    # عند التحديد يُرسل الاثنان بالترتيب، وعند الإلغاء يُرسل المخفي وحده.
    # إرسال "لا شيء" عند الإلغاء (كما كان) لا يُحاكي المتصفّح بل يخفي عيبًا:
    # الخاصية افتراضها true فيمرّ التالف إلى الرصيد ونحن نطبع ✓.
    if restock:
        data.append(("RestockToInventory", "true"))
    data.append(("RestockToInventory", "false"))
    res = admin.post(f"{B}/Returns/Confirm", data=data, timeout=60)
    # النجاح يُقاس بالوصول لصفحة تفاصيل مرتجع، لا برمز 200
    return "/Returns/Details/" in res.url

def invoice_with(marker):
    """يجد معرّف فاتورة من سجلّ التقارير بعلامةٍ فيها (كود خصم مثلًا).

    الاختيار بالمحتوى لا بالترتيب: الاعتماد على inv_ids[2] يربط «المرتجع
    التالف» بأي فاتورة تصادف ذلك الموضع، فينهار الشرح المبني على خصم VIP25
    بمجرّد تغيير سطر واحد في قائمة المبيعات.
    """
    body = html.unescape(admin.get(f"{B}/Reports", timeout=30).text)
    for row in body.split("<tr")[1:]:
        if marker in row:
            m = re.search(r'/Reports/Details/(\d+)', row)
            if m:
                return int(m.group(1))
    return None


if len(inv_ids) >= 2:
    # التالف: فاتورة كود VIP25 — عليها يقوم شرح «نرد المدفوع لا سعر الكتالوج»
    damaged = invoice_with("VIP25") or inv_ids[2]
    restocked = next((i for i in inv_ids if i != damaged), inv_ids[1])
    # السبب يجب أن يوافق نصّ الملاحظة حرفيًا: شاشة تفاصيل المرتجع تعرض
    # «السبب» من القائمة الثابتة، فلو أرسلنا WrongItem=2 والملاحظة تقول
    # «العميل غيّر رأيه» تظهر الشاشة مناقضةً للشرح المنطوق أمام العميل.
    # ChangedMind=3 · Defective=1  (انظر ReturnReason في Return.cs)
    r1 = make_return(restocked, 3, True, "العميل غيّر رأيه — الجهاز بحالته وكامل ملحقاته")
    r2 = make_return(damaged, 1, False,
                     "شاشة بها عيب صناعة — لا يُعاد للرصيد ويُرسل للمورد")
    print(f"  مرتجع صالح يُعاد للرصيد: {'✓' if r1 else '✗'}")
    print(f"  مرتجع تالف لا يُعاد:     {'✓' if r2 else '✗'}")

    # الأثر يُقاس لا يُفترض: «تم الحفظ» لا يعني أن الرصيد تحرّك كما يجب.
    # نقرأ الشاشتين ونتأكّد أن المرتجعين اختلفا فعلًا في إعادة المخزون.
    # نعتمد على title الشارة لا على نصّها: «أُعيد» و«لا» كلمتان قصيرتان
    # تتكرّران في الصفحة لأسباب أخرى، أمّا الـ title فمرتبط بالشرط وحده.
    lst = html.unescape(admin.get(f"{B}/Returns/List", timeout=30).text)
    yes = lst.count("أُعيدت الأصناف للمخزون")
    no = lst.count("لم تُعَد للمخزون (تالف)")
    print(f"  تحقّق الأثر: أُعيد للمخزون={yes} / لم يُعد (تالف)={no}")
    if not (yes >= 1 and no >= 1):
        print("  ❌ مفتاح «إعادة للمخزون» لا يُفرِّق بين الصالح والتالف")
        sys.exit(1)

    # السبب المعروض يجب أن يوافق الملاحظة المكتوبة: العارض يقرأ الملاحظة
    # بصوته والعميل يقرأ «السبب» بعينه، فأي اختلاف بينهما يبدو ارتباكًا.
    reasons_ok = True
    for rid, want in ((1, "العميل غيّر رأيه"), (2, "عيب في المنتج")):
        body = html.unescape(admin.get(f"{B}/Returns/Details/{rid}", timeout=30).text)
        if want not in body:
            print(f"  ❌ المرتجع #{rid}: السبب المعروض لا يطابق الملاحظة ({want})")
            reasons_ok = False
    if not reasons_ok:
        sys.exit(1)
    print("  ✅ سبب كل مرتجع مطابق لنصّ ملاحظته")

# ================== حدّ التنبيه: لوحة النقص يجب أن تنطق ==================
# «منتجات وشك النفاد» كانت تظهر «كل المنتجات بمخزون كافٍ» لأن الحدّ المزروع
# ٥ لكل صنف وأقلّ رصيد ٨. سكربت العرض يوجّه العارض ليقول «النظام بينبّهني
# على اللي قرّب يخلص» فيشير إلى لوحة فارغة — وهي أسوأ لحظة ممكنة: ميزةٌ
# قائمة تبدو معطّلة. الحل ليس تزييف رصيد بل ضبط حدٍّ واقعي على صنفٍ واحد:
# الشاحن أقلّ الأصناف رصيدًا وأسرعها دورانًا، فحدُّ إعادة طلبه أعلى بطبعه.
print("\n--- ضبط حدّ التنبيه ليُثبت لوحة النقص ---")

def stock_of(barcode):
    """رصيد الصنف كما تعرضه شاشة المنتجات (مجموع كل المخازن).

    الرصيد يُقرأ من شارة المخزون تحديدًا (stock-badge) لا من «أول رقم في
    الصف»: أول خلية في الصف هي رقم التسلسل، فالقراءة العامة أعادت ١ بدل
    الرصيد الحقيقي فضُبط حدُّ التنبيه تحت الرصيد وصمتت لوحة النقص.
    """
    body = html.unescape(admin.get(f"{B}/Products", timeout=30).text)
    for row in body.split("<tr")[1:]:
        if barcode in row:
            m = re.search(r'stock-badge[^>]*>\s*(\d+)\s*<', row)
            if m:
                return int(m.group(1))
    return None


def set_threshold(barcode, threshold):
    """يعدّل حدّ التنبيه من شاشة تعديل المنتج نفسها.

    التعديل يمرّ بالشاشة لا بحقن SQL: نموذج Edit يرفض تغيير المخزون
    (الرقم مجموع مخازن) ويعيد عرض النموذج، فإرسال حقلٍ واحد يُفقد الباقي
    ويُفسد الصنف. لذا نقرأ قيم النموذج المرسومة ونعيدها كما هي ولا نغيّر
    إلا الحدّ — وهذا أيضًا يختبر أن الشاشة تحفظ فعلًا.
    """
    body = html.unescape(admin.get(f"{B}/Products", timeout=30).text)
    pid = None
    for row in body.split("<tr")[1:]:
        if barcode in row:
            m = re.search(r'/Products/Edit/(\d+)', row)
            if m:
                pid = m.group(1)
            break
    if not pid:
        return False
    form = admin.get(f"{B}/Products/Edit/{pid}", timeout=30).text
    def val(name):
        m = re.search(rf'name="{name}"[^>]*value="([^"]*)"', form)
        return m.group(1) if m else ""
    def sel(name):
        """يقرأ الخيار المُختار من قائمة منسدلة.

        ترتيب السمات لا يُفترض: Razor يكتب `selected="selected"` قبل
        `value`، فالبحث عن «value ثم selected» لا يجد شيئًا فيُرسل البراند
        فارغًا، فيسقط التحقق ويُعاد النموذج بالرمز 200 — تعديلٌ لم يحدث
        بلا أي إشعار. نلتقط الخيار كاملًا ثم نستخرج قيمته منه.
        """
        block = re.search(rf'name="{name}".*?</select>', form, re.S)
        if not block:
            return ""
        opt = re.search(r'<option[^>]*\bselected[^>]*>', block.group(0))
        if not opt:
            return ""
        m = re.search(r'value="(\d+)"', opt.group(0))
        return m.group(1) if m else ""
    data = {
        "Id": pid, "Name": val("Name"), "Barcode": val("Barcode"),
        "Price": val("Price"), "StockQuantity": val("StockQuantity"),
        "LowStockThreshold": threshold,
        "BrandId": sel("BrandId"), "CategoryId": sel("CategoryId"),
        "IsActive": "true",
    }
    r = post(admin, f"/Products/Edit/{pid}", data, form_path=f"/Products/Edit/{pid}")
    # الأثر يُقاس من القائمة: صفحة التعديل تُعاد بنفس الرمز 200 عند الرفض
    after = html.unescape(admin.get(f"{B}/Products?lowStock=true", timeout=30).text)
    return barcode in after

# الشاحن السريع: أقلّ الأصناف رصيدًا وأسرعها دورانًا، فحدُّ إعادة طلبه أعلى بطبعه.
# الحدّ يُحسب من الرصيد الفعلي لا يُكتب رقمًا ثابتًا: أي تعديل في كميات الشراء
# أو البيع يُغيّر الرصيد، فرقمٌ مثبَّت (١٢) يصير أقلّ من الرصيد فتصمت اللوحة
# ويشير العارض إلى «تنبيه» غير موجود. القاعدة «الحدّ فوق الرصيد بقليل» تبقى
# صحيحة مهما تغيّرت الأرقام، وتظلّ واقعية: صنفٌ نزل تحت حدّ إعادة الطلب.
charger_stock = stock_of("6941238703245")
low_threshold = (charger_stock + 4) if charger_stock is not None else 12
ok_low = set_threshold("6941238703245", low_threshold)
print(f"  رصيد الشاحن={charger_stock} وحدّ التنبيه المضبوط={low_threshold}")
print(f"  الشاحن السريع 45W ضمن «وشك النفاد»: {'✓' if ok_low else '✗'}")

print("\n" + "=" * 50)
print("تم تجهيز قاعدة العرض.")
print("=" * 50)

# ================== تحقّق ذاتي ==================
# السكربت يتحقق من نتيجته بنفسه: طلبٌ يعيد 200 قد يكون قد رُفض بهدوء
# ================== تنظيف مخلّفات الاختبارات ==================
# ملفات الاختبار تُنشئ «مخزن اختبار التحويل» و«مورد اختبار المشتريات» وتتركها،
# فتظهر في الشاشات أمام مدير الفرع. تشغيل الاختبارات بعد التجهيز أمرٌ متوقَّع،
# فالتنظيف يجري هنا ليكون التجهيز صالحًا مهما كان ترتيب التشغيل — لا مرة واحدة
# على قاعدة نظيفة. ما لا يُحذف (لارتباطه بحركة) يُعطَّل ليخرج من الشاشات.
print("\n--- تنظيف مخلّفات الاختبارات ---")

def purge(list_path, id_pattern, marker="اختبار"):
    """يحذف كل سطر يحمل كلمة «اختبار» من شاشةٍ ما، ويعطّله إن رفض الحذف."""
    removed = 0
    body = admin.get(f"{B}{list_path}", timeout=30).text
    for row in body.split("<tr")[1:]:
        if marker not in row:
            continue
        m = re.search(id_pattern, row)
        if not m:
            continue
        eid = m.group(1)
        base = list_path.split("?")[0]
        r = post(admin, f"{base}/Delete/{eid}", {"id": eid}, form_path=list_path)
        after = admin.get(f"{B}{list_path}", timeout=30).text
        still = any(marker in rr and re.search(id_pattern, rr or "")
                    and re.search(id_pattern, rr).group(1) == eid
                    for rr in after.split("<tr")[1:])
        if still:
            # مرتبط بحركة فلا يُحذف: التعطيل يُخرجه من الشاشات بلا تشويه للسجل
            post(admin, f"{base}/ToggleActive/{eid}", {"id": eid}, form_path=list_path)
        removed += 1
    return removed

print(f"  مخازن اختبار مُزالة: {purge('/Warehouses', r'/Warehouses/Stock/(\\d+)')}")
print(f"  موردو اختبار مُزالون: {purge('/Suppliers', r'/Suppliers/Details/(\\d+)')}")

# ================== تحقّق ==================
# (حدث فعلًا مع المرتجعات)، فلا يُعتمد على رمز الحالة وحده.
print("\n--- تحقّق ---")
fails = []

def must(cond, label):
    print(f"  {'✅' if cond else '❌'} {label}")
    if not cond:
        fails.append(label)

def count_in(session, path, pattern):
    return len(set(re.findall(pattern, session.get(f"{B}{path}", timeout=30).text)))

must(count_in(admin, "/Warehouses", r"/Warehouses/Stock/(\d+)") >= 4, "أربعة مخازن على الأقل")
must(count_in(admin, "/Suppliers", r"/Suppliers/Details/(\d+)") >= 3, "ثلاثة موردين")
must(count_in(admin, "/Purchases", r"/Purchases/Details/(\d+)") >= 5, "خمسة أوامر شراء")
must(count_in(admin, "/Purchases/Receipts", r"/Purchases/Receipt/(\d+)") >= 3, "أذون استلام مسجَّلة")
must(count_in(admin, "/Transfers/All", r"/Transfers/Details/(\d+)") >= 4, "أربعة تحويلات")
must(count_in(admin, "/Reports", r"/Reports/Details/(\d+)") >= 10, "عشر فواتير على الأقل")
must(count_in(admin, "/Customers", r"/Customers/Details/(\d+)") >= 5, "خمسة عملاء")
must(count_in(admin, "/Returns/List", r"/Returns/Details/(\d+)") >= 2, "مرتجعان مسجَّلان")

# شاشات أمين المخزن يجب أن تكون مأهولة: شاشة فارغة أمام من يعرض النظام
# تُقرأ «لا يعمل»، لا «لا بيانات».
keeper2 = login("keeper@pos.local", "Keeper@123")
must(count_in(keeper2, "/Purchases/Open", r"/Purchases/Details/(\d+)") >= 1,
     "أمين المخزن يرى أوامر بانتظار التوريد على مخزنه")
must(count_in(keeper2, "/Purchases/Receipts", r"/Purchases/Receipt/(\d+)") >= 1,
     "أمين المخزن يرى أذون استلام مخزنه")
must(count_in(keeper2, "/Transfers/Incoming", r"/Transfers/Details/(\d+)") >= 1,
     "أمين المخزن يرى طلبًا واردًا على مخزنه")

# التكاليف محجوبة عنه: قاعدة عمل لا تفصيل شكلي
kbody = keeper2.get(f"{B}/Purchases/Open", timeout=30).text
must("تكلفة الوحدة" not in kbody and "إجمالي المشتريات" not in kbody,
     "التكاليف محجوبة عن أمين المخزن")

# والمندوب يرى فواتيره وحده
agent2 = login("agent@pos.local", "Agent@123")
must(count_in(agent2, "/AgentReports", r"/AgentReports/Details/(\d+)") >= 5,
     "المندوب يرى فواتيره في تقاريره")

# لا اسم فيه كلمة «اختبار»: بيانات الاختبار أمام مدير الفرع تُسقط المصداقية
for path in ["/Customers", "/Suppliers", "/Warehouses"]:
    body = admin.get(f"{B}{path}", timeout=30).text
    must("اختبار" not in body and "test" not in body.lower().replace("latest", ""),
         f"{path} بلا بيانات اختبار")

# الرصيد المجمّع = مجموع أرصدة المخازن: الفصل بينهما يعني عدّادًا كاذبًا
r = admin.get(f"{B}/Products", timeout=30).text
must("<td" in r, "شاشة المنتجات تعمل")

# لوحة «منتجات وشك النفاد» في لوحة التحكم ليست فارغة: سكربت العرض يوجّه
# العارض للإشارة إليها وقول «النظام بينبّهني لوحده»، فلوحةٌ فارغة تُسقط
# الميزة أمام مدير الفرع. نتحقّق من اللوحة نفسها لا من قيمة في القاعدة.
dash = html.unescape(admin.get(f"{B}/", timeout=30).text)
must("كل المنتجات بمخزون كافٍ" not in dash, "لوحة نقص المخزون ليست فارغة")

print()
if fails:
    print(f"❌ فشل {len(fails)} تحقّق:")
    for f in fails:
        print(f"   - {f}")
    sys.exit(1)
print("✅ قاعدة العرض جاهزة وموثوقة.")

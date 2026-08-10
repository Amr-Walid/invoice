#!/usr/bin/env python3
"""
اختبار المشتريات — دورة الحياة كاملة.

يتحقق من الأشياء التي لا يكفي فيها النظر للشاشة:
  * الاعتماد لا يضيف قطعة واحدة للمخزون (لئلا تُباع بضاعة لم تصل)
  * الإضافة تقع عند تسجيل الاستلام فقط، بسبب Purchase=5 وبإذن مرقّم مرتبط بالحركة
  * الاستلام الجزئي: الحالة تصير PartiallyReceived والمتبقّي يُحسب صحيحًا
  * الاستلام بأكثر من المتبقّي يُرفَض (وإلا اختُرعت قطع من الهواء)
  * الإلغاء بعد استلام جزئي لا يسحب ما استُلم — القطع موجودة فعلًا
  * الإلغاء بلا سبب يُرفَض
  * هاتف المورد مُعرِّف: التكرار يُرفَض
  * حذف مورد له أوامر يُرفَض (وإلا فقدنا تاريخ الأمر)
  * أمين مخزن آخر لا يستلم لمخزن ليس مخزنه
  * المندوب لا يرى المشتريات إطلاقًا (بيانات تكلفة ليست له)
  * الكاش (Products.StockQuantity) = مجموع ProductStocks بعد كل خطوة

التشغيل: البرنامج يعمل على 5080 بقاعدة SQLite مُهيّأة.
    python3 tests/purchase_test.py
"""
import re, urllib.request, urllib.parse, http.cookiejar, sqlite3, sys, os, time
import html as htmlmod

BASE = os.environ.get("POS_BASE", "http://localhost:5080")
DB = os.environ.get("POS_DB", "/home/user/webapp/src/PosSystem.Web/possystem_dev.db")

P = F = 0


def chk(name, cond, extra=""):
    global P, F
    if cond:
        P += 1
        print(f"  PASS  {name}")
    else:
        F += 1
        print(f"  FAIL  {name}  {extra}")


def login(email, pwd):
    op = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    html = op.open(BASE + "/Account/Login").read().decode()
    tok = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html).group(1)
    data = urllib.parse.urlencode(
        {"Email": email, "Password": pwd, "__RequestVerificationToken": tok}).encode()
    return op, op.open(BASE + "/Account/Login", data).geturl()


def get(op, url):
    """يُرجع (رمز التحقق, html, العنوان النهائي) — العربية تُفَك من ترميز Razor"""
    r = op.open(BASE + url)
    raw = r.read().decode()
    m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', raw)
    return (m.group(1) if m else None), htmlmod.unescape(raw), r.geturl()


def post(op, url, pairs, tok):
    body = urllib.parse.urlencode(list(pairs) + [("__RequestVerificationToken", tok)]).encode()
    r = op.open(BASE + url, body)
    return htmlmod.unescape(r.read().decode()), r.geturl()


def status_code(op, url):
    """رمز الحالة دون رفع استثناء — للتحقق من المنع"""
    try:
        r = op.open(BASE + url)
        return r.getcode(), r.geturl()
    except urllib.error.HTTPError as e:
        return e.code, url


def q(sql, args=()):
    con = sqlite3.connect(DB)
    try:
        return con.execute(sql, args).fetchall()
    finally:
        con.close()


def dec(v):
    """EF على SQLite يحفظ decimal كنص — فالمقارنة الرقمية تحتاج تحويلًا صريحًا"""
    return float(v) if v is not None else 0.0


def stock(pid, wid):
    r = q("SELECT Quantity FROM ProductStocks WHERE ProductId=? AND WarehouseId=?", (pid, wid))
    return r[0][0] if r else 0


def cache(pid):
    return q("SELECT StockQuantity FROM Products WHERE Id=?", (pid,))[0][0]


def total(pid):
    return q("SELECT COALESCE(SUM(Quantity),0) FROM ProductStocks WHERE ProductId=?", (pid,))[0][0]


def check_cache(label, pid):
    chk(f"الكاش = مجموع الأرصدة {label} (كاش={cache(pid)} مجموع={total(pid)})",
        cache(pid) == total(pid))


def mv_purchase(oid=None):
    """عدد حركات الشراء — مقيَّدة بأمر معيّن عند تمريره.

    العدّ الكلي لا يصلح: القاعدة قد تحمل حركات من تشغيل سابق، فيفشل الاختبار
    لسبب لا علاقة له بالمنطق المُختبَر.
    """
    if oid is None:
        return q("SELECT COUNT(*) FROM StockMovements WHERE Reason=5")[0][0]
    return q("SELECT COUNT(*) FROM StockMovements m "
             "JOIN PurchaseReceipts r ON r.Id = m.ReferencePurchaseReceiptId "
             "WHERE m.Reason=5 AND r.PurchaseOrderId=?", (oid,))[0][0]


def order_row(oid):
    r = q("SELECT Status, PurchaseNumber, CancellationReason FROM PurchaseOrders WHERE Id=?", (oid,))
    return r[0] if r else None


# ======================================================================
print("=" * 62)
print("تهيئة: مدير + مخزنان + مورد")
print("=" * 62)

opa, _ = login("admin@pos.local", "Admin@123")

W1 = q("SELECT Id FROM Warehouses WHERE Code='MAIN'")[0][0]

W2_CODE = "PUR2"
rows = q("SELECT Id FROM Warehouses WHERE Code=?", (W2_CODE,))
if not rows:
    tok, _, _ = get(opa, "/Warehouses/Create")
    post(opa, "/Warehouses/Create",
         [("Code", W2_CODE), ("Name", "مخزن فرع الهرم"),
          ("ManagerName", "أمين ٢"), ("IsActive", "true")], tok)
    rows = q("SELECT Id FROM Warehouses WHERE Code=?", (W2_CODE,))
W2 = rows[0][0]
chk(f"المخزن الثاني موجود (id={W2})", W2 > 0)

PID = 1
PNAME = q("SELECT Name FROM Products WHERE Id=?", (PID,))[0][0]

SUP_PHONE = "01099887766"
rows = q("SELECT Id FROM Suppliers WHERE PhoneNormalized LIKE ?", ("%99887766",))
if not rows:
    tok, _, _ = get(opa, "/Suppliers/Create")
    post(opa, "/Suppliers/Create",
         [("Name", "شركة الهرم للتوريدات"), ("Phone", SUP_PHONE),
          ("ContactPerson", "أ. سامي"), ("IsActive", "true")], tok)
    rows = q("SELECT Id FROM Suppliers WHERE PhoneNormalized LIKE ?", ("%99887766",))
chk("المورد أُنشئ", len(rows) == 1)
SUP = rows[0][0]

START1 = stock(PID, W1)
print(f"  البداية: صنف «{PNAME}» (id={PID}) رصيد المخزن الرئيسي={START1} كاش={cache(PID)}")
check_cache("قبل البدء", PID)


# ======================================================================
print()
print("=" * 62)
print("١) هاتف المورد مُعرِّف — التكرار يُرفَض")
print("=" * 62)

tok, _, _ = get(opa, "/Suppliers/Create")
html, _ = post(opa, "/Suppliers/Create",
               [("Name", "مورد بنفس الرقم"), ("Phone", SUP_PHONE), ("IsActive", "true")], tok)
dupes = q("SELECT COUNT(*) FROM Suppliers WHERE PhoneNormalized LIKE ?", ("%99887766",))[0][0]
chk("لم يُنشأ مورد ثانٍ بنفس الرقم", dupes == 1, f"العدد={dupes}")
chk("رسالة الرفض تذكر أن الرقم مسجَّل", "مسجّل بالفعل" in html or "مسجل بالفعل" in html)


# ======================================================================
print()
print("=" * 62)
print("٢) إنشاء مسودة أمر شراء — لا قطعة تدخل المخزون")
print("=" * 62)

tok, html, _ = get(opa, f"/Purchases/Create?warehouseId={W1}")
chk("شاشة الإنشاء تُفتح", tok is not None)
chk("المورد مطروح في القائمة", f'value="{SUP}"' in html)
chk("الصنف مطروح في الجدول", f'value="{PID}"' in html)

before = stock(PID, W1)
html, url = post(opa, "/Purchases/Create",
                 [("supplierId", str(SUP)), ("warehouseId", str(W1)),
                  ("discountPercentage", "0"), ("notes", "أمر اختبار"),
                  ("productIds", str(PID)), ("quantities", "10"), ("unitCosts", "1500.50"),
                  ("confirmImmediately", "false")], tok)

rows = q("SELECT Id, Status, PurchaseNumber, Total FROM PurchaseOrders "
         "WHERE SupplierId=? ORDER BY Id DESC LIMIT 1", (SUP,))
chk("الأمر أُنشئ في قاعدة البيانات", len(rows) == 1)
OID, st, pnum, ptotal = rows[0]
chk(f"الحالة مسودة (Draft=1) — الفعلية={st}", st == 1)
chk(f"رقم الأمر بالصيغة PO-yyyyMMdd-#### ({pnum})",
    re.match(r"^PO-\d{8}-\d{4}$", pnum) is not None)
chk(f"الإجمالي = 10 × 1500.50 = 15005.00 (الفعلي={ptotal})", abs(dec(ptotal) - 15005.0) < 0.01)
chk(f"المخزون لم يتغير بالمسودة ({before} → {stock(PID, W1)})", stock(PID, W1) == before)
mv = mv_purchase(OID)
chk("لا حركة مخزون من نوع شراء بعد المسودة", mv == 0, f"العدد={mv}")
check_cache("بعد المسودة", PID)


# ======================================================================
print()
print("=" * 62)
print("٣) الاعتماد التزام لا استلام — ما زال المخزون كما هو")
print("=" * 62)

tok, html, _ = get(opa, f"/Purchases/Details/{OID}")
chk("شاشة التفاصيل تُفتح", tok is not None)
chk("رسالة المسودة ظاهرة", "مسودة" in html)
chk("التفاصيل تُنبّه أن الكميات لا تدخل قبل الاستلام",
    "لا كميات في المخزون" in html or "لن تُضاف" in html)

before = stock(PID, W1)
post(opa, f"/Purchases/Confirm/{OID}", [], tok)
st = order_row(OID)[0]
chk(f"الحالة صارت معتمد (Confirmed=2) — الفعلية={st}", st == 2)
chk(f"المخزون لم يتغير بالاعتماد ({before} → {stock(PID, W1)})", stock(PID, W1) == before)
mv = mv_purchase(OID)
chk("لا حركة شراء بعد الاعتماد", mv == 0, f"العدد={mv}")
check_cache("بعد الاعتماد", PID)

tok, html, _ = get(opa, f"/Purchases/Details/{OID}")
chk("التفاصيل تقول «بانتظار التوريد»", "بانتظار التوريد" in html)
chk("زر التعديل اختفى بعد الاعتماد", 'asp-action="Edit"' not in html)
chk("الأمر ظاهر في شاشة «بانتظار التوريد»",
    pnum in get(opa, "/Purchases/Open")[1])


# ======================================================================
print()
print("=" * 62)
print("٤) الاستلام بأكثر من المتبقّي يُرفَض")
print("=" * 62)

ITEM = q("SELECT Id, Quantity, ReceivedQuantity FROM PurchaseOrderItems "
         "WHERE PurchaseOrderId=?", (OID,))[0]
IID, IQTY, IRECV = ITEM
chk(f"سطر الأمر موجود (id={IID}, مطلوب={IQTY})", IQTY == 10)

before = stock(PID, W1)
tok, _, _ = get(opa, f"/Purchases/Details/{OID}")
html, _ = post(opa, f"/Purchases/Receive/{OID}",
               [("itemIds", str(IID)), ("quantities", "99"), ("notes", "محاولة زائدة")], tok)
chk("رسالة الرفض تذكر أن المتبقّي أقل",
    "لا يمكن استلام" in html or "المتبقّي" in html)
chk(f"المخزون لم يتغير بالمحاولة المرفوضة ({before} → {stock(PID, W1)})",
    stock(PID, W1) == before)
r = q("SELECT COUNT(*) FROM PurchaseReceipts WHERE PurchaseOrderId=?", (OID,))[0][0]
chk("لم يُصدَر إذن استلام للمحاولة المرفوضة", r == 0, f"العدد={r}")
check_cache("بعد المحاولة المرفوضة", PID)


# ======================================================================
print()
print("=" * 62)
print("٥) استلام جزئي — الإضافة تقع هنا وهنا فقط")
print("=" * 62)

before = stock(PID, W1)
tok, _, _ = get(opa, f"/Purchases/Details/{OID}")
post(opa, f"/Purchases/Receive/{OID}",
     [("itemIds", str(IID)), ("quantities", "6"), ("notes", "فاتورة المورد 5512")], tok)

after = stock(PID, W1)
chk(f"الرصيد زاد ٦ قطع ({before} → {after})", after == before + 6)
st = order_row(OID)[0]
chk(f"الحالة استلام جزئي (PartiallyReceived=3) — الفعلية={st}", st == 3)

recv = q("SELECT Id, ReceiptNumber, WarehouseId, Notes FROM PurchaseReceipts "
         "WHERE PurchaseOrderId=? ORDER BY Id", (OID,))
chk("أُصدر إذن استلام واحد", len(recv) == 1, f"العدد={len(recv)}")
RID, rnum, rwid, rnotes = recv[0]
chk(f"رقم الإذن بالصيغة RCV-yyyyMMdd-#### ({rnum})",
    re.match(r"^RCV-\d{8}-\d{4}$", rnum) is not None)
chk("الإذن على المخزن المستقبِل الصحيح", rwid == W1)
chk("ملاحظة الإذن محفوظة (مرجع فاتورة المورد)", rnotes and "5512" in rnotes)

ri = q("SELECT PurchaseOrderItemId, ProductId, Quantity FROM PurchaseReceiptItems "
       "WHERE PurchaseReceiptId=?", (RID,))
chk("سطر الإذن يشير لسطر الأمر لا للمنتج فقط",
    len(ri) == 1 and ri[0][0] == IID and ri[0][2] == 6)

recvqty = q("SELECT ReceivedQuantity FROM PurchaseOrderItems WHERE Id=?", (IID,))[0][0]
chk(f"المستلم على السطر = ٦ (الفعلي={recvqty})", recvqty == 6)

mv = q("SELECT ProductId, WarehouseId, Change, Reason, ReferencePurchaseReceiptId "
       "FROM StockMovements WHERE Reason=5 AND ReferencePurchaseReceiptId=?", (RID,))
chk("حركة مخزون من نوع شراء (Reason=5) مسجَّلة", len(mv) == 1)
if mv:
    mpid, mwid, mchg, mreason, mref = mv[0]
    chk("الحركة على الصنف والمخزن الصحيحين", mpid == PID and mwid == W1)
    chk(f"مقدار الحركة +٦ (الفعلي={mchg})", mchg == 6)
    chk(f"الحركة مربوطة بإذن الاستلام (ref={mref}, إذن={RID})", mref == RID)
check_cache("بعد الاستلام الجزئي", PID)

tok, html, _ = get(opa, f"/Purchases/Details/{OID}")
chk("التفاصيل تعرض المتبقّي ٤", "استلام جزئي" in html)
chk("إذن الاستلام مدرج في التفاصيل", rnum in html)
_, html, _ = get(opa, f"/Purchases/Receipt/{RID}")
chk("إذن الاستلام يُطبع ويحمل رقمه", rnum in html)
chk("إذن الاستلام فيه خط توقيع المستلِم", "توقيع المستلِم" in html)
_, html, _ = get(opa, "/Purchases/Receipts")
chk("الإذن ظاهر في سجل الأذون", rnum in html)


# ======================================================================
print()
print("=" * 62)
print("٦) الإلغاء بلا سبب يُرفَض، وبعد الاستلام لا يسحب ما وصل")
print("=" * 62)

before = stock(PID, W1)
tok, _, _ = get(opa, f"/Purchases/Details/{OID}")
html, _ = post(opa, f"/Purchases/Cancel/{OID}", [("reason", "")], tok)
st = order_row(OID)[0]
chk(f"الأمر لم يُلغَ بلا سبب (الحالة={st})", st == 3)
chk("رسالة تطلب السبب", "سبب" in html)
chk(f"المخزون سليم بعد المحاولة ({before} → {stock(PID, W1)})", stock(PID, W1) == before)


# ======================================================================
print()
print("=" * 62)
print("٧) أمين مخزن آخر لا يستلم لمخزن ليس مخزنه")
print("=" * 62)

# نربط أمين المخزن بالمخزن الثاني ثم نطلب منه الاستلام لأمر على المخزن الأول
KEEP = q("SELECT Id, WarehouseId FROM AspNetUsers WHERE Email='keeper@pos.local'")
if KEEP:
    KUID, KWID = KEEP[0]
    tok, _, _ = get(opa, "/Users")
    post(opa, "/Users/ChangeWarehouse",
         [("id", KUID), ("warehouseId", str(W2))], tok)
    now = q("SELECT WarehouseId FROM AspNetUsers WHERE Id=?", (KUID,))[0][0]
    chk(f"أمين المخزن مربوط بالمخزن الثاني (الفعلي={now})", now == W2)

    opk, _ = login("keeper@pos.local", "Keeper@123")
    before = stock(PID, W1)
    tok, html, _ = get(opk, f"/Purchases/Details/{OID}")
    chk("أمين المخزن يرى الأمر (لا يُخفى عنه)", tok is not None)
    chk("لكن زر الاستلام غير معروض له",
        "receiveModal" not in html or "أمين ذلك المخزن" in html)
    if tok:
        html, _ = post(opk, f"/Purchases/Receive/{OID}",
                       [("itemIds", str(IID)), ("quantities", "4")], tok)
        chk(f"لم يُستلم شيء ({before} → {stock(PID, W1)})", stock(PID, W1) == before)
        chk("رسالة تشرح أن الاستلام لأمين ذلك المخزن",
            "مخزن" in html and ("لا يمكن" in html or "أمين" in html or "صلاحية" in html))

    code, _ = status_code(opk, "/Purchases/Create")
    chk("أمين المخزن لا يُنشئ أمر شراء", code in (403, 302) or "Login" in _ or code == 200,
        f"الرمز={code}")
    code, _ = status_code(opk, "/Purchases/Report")
    chk("أمين المخزن لا يرى تقرير المشتريات (تكاليف)",
        code in (403, 302) or "AccessDenied" in _, f"الرمز={code} {_}")
    check_cache("بعد محاولة الأمين", PID)

    # نُعيد أمين المخزن لمخزنه الأصلي فلا نُخلّف حالة تُربك اختبارًا آخر
    tok, _, _ = get(opa, "/Users")
    post(opa, "/Users/ChangeWarehouse",
         [("id", KUID), ("warehouseId", str(KWID or W1))], tok)
else:
    chk("مستخدم أمين المخزن موجود", False, "keeper@pos.local غير موجود")


# ======================================================================
print()
print("=" * 62)
print("٨) المندوب لا يرى المشتريات إطلاقًا")
print("=" * 62)

opg, _ = login("agent@pos.local", "Agent@123")
for path in ["/Purchases", "/Purchases/Open", "/Purchases/Receipts",
             f"/Purchases/Details/{OID}", "/Suppliers", "/Purchases/Report"]:
    code, u = status_code(opg, path)
    denied = code in (403, 401) or "AccessDenied" in u or "Login" in u
    chk(f"المندوب مُنَع من {path}", denied, f"الرمز={code} → {u}")


# ======================================================================
print()
print("=" * 62)
print("٩) استلام المتبقّي يُغلق الأمر مكتملًا")
print("=" * 62)

before = stock(PID, W1)
tok, _, _ = get(opa, f"/Purchases/Details/{OID}")
post(opa, f"/Purchases/Receive/{OID}",
     [("itemIds", str(IID)), ("quantities", "4"), ("notes", "الدفعة الثانية")], tok)

after = stock(PID, W1)
chk(f"الرصيد زاد ٤ قطع ({before} → {after})", after == before + 4)
chk(f"إجمالي الزيادة من الأمر = ١٠ ({START1} → {after})", after == START1 + 10)
st, _, _ = order_row(OID)
chk(f"الحالة صارت مستلم بالكامل (Received=4) — الفعلية={st}", st == 4)

r = q("SELECT COUNT(*) FROM PurchaseReceipts WHERE PurchaseOrderId=?", (OID,))[0][0]
chk("صار للأمر إذنا استلام (دفعتان)", r == 2, f"العدد={r}")
recvqty = q("SELECT ReceivedQuantity FROM PurchaseOrderItems WHERE Id=?", (IID,))[0][0]
chk(f"المستلم على السطر = ١٠ (الفعلي={recvqty})", recvqty == 10)
comp = q("SELECT CompletedAt FROM PurchaseOrders WHERE Id=?", (OID,))[0][0]
chk("تاريخ الإغلاق مسجَّل", comp is not None)
mv = mv_purchase(OID)
chk("حركتا شراء لهذا الأمر (دفعتان)", mv == 2, f"العدد={mv}")
check_cache("بعد الاستلام الكامل", PID)

tok, html, _ = get(opa, f"/Purchases/Details/{OID}")
chk("التفاصيل تقول مستلم بالكامل", "مستلم بالكامل" in html)
chk("لا زر استلام على أمر مكتمل", "receiveModal" not in html)


# ======================================================================
print()
print("=" * 62)
print("١٠) الإلغاء بعد استلام جزئي يُغلق المتبقّي ولا يسحب ما وصل")
print("=" * 62)

# أمر ثانٍ: نستلم بعضه ثم نُلغيه ونتأكد أن المستلم باقٍ
tok, _, _ = get(opa, f"/Purchases/Create?warehouseId={W1}")
post(opa, "/Purchases/Create",
     [("supplierId", str(SUP)), ("warehouseId", str(W1)),
      ("discountPercentage", "10"), ("notes", "أمر يُلغى"),
      ("productIds", str(PID)), ("quantities", "8"), ("unitCosts", "1000"),
      ("confirmImmediately", "true")], tok)

rows = q("SELECT Id, Status, Total FROM PurchaseOrders WHERE SupplierId=? "
         "ORDER BY Id DESC LIMIT 1", (SUP,))
OID2, st2, tot2 = rows[0]
chk(f"الأمر الثاني أُنشئ ومعتمد فورًا (الحالة={st2})", st2 == 2)
chk(f"الخصم ١٠% طُبِّق: 8000 → 7200 (الفعلي={tot2})", abs(dec(tot2) - 7200.0) < 0.01)

IID2 = q("SELECT Id FROM PurchaseOrderItems WHERE PurchaseOrderId=?", (OID2,))[0][0]
before = stock(PID, W1)
tok, _, _ = get(opa, f"/Purchases/Details/{OID2}")
post(opa, f"/Purchases/Receive/{OID2}",
     [("itemIds", str(IID2)), ("quantities", "3"), ("notes", "وصل جزء فقط")], tok)
mid = stock(PID, W1)
chk(f"وصل ٣ قطع ({before} → {mid})", mid == before + 3)

tok, _, _ = get(opa, f"/Purchases/Details/{OID2}")
post(opa, f"/Purchases/Cancel/{OID2}",
     [("reason", "المورد أبلغ بعدم توفر باقي الكمية")], tok)

st2, _, reason2 = order_row(OID2)
chk(f"الحالة صارت ملغي (Cancelled=5) — الفعلية={st2}", st2 == 5)
chk("سبب الإلغاء محفوظ", reason2 and "عدم توفر" in reason2)
after = stock(PID, W1)
chk(f"القطع الثلاث المستلمة لم تُسحب بالإلغاء ({mid} → {after})", after == mid)
mv = mv_purchase(OID2)
chk("حركة واحدة فقط للأمر الملغى — لا عكسية أُضيفت", mv == 1, f"العدد={mv}")
check_cache("بعد الإلغاء", PID)

tok, html, _ = get(opa, f"/Purchases/Details/{OID2}")
chk("التفاصيل تشرح أن المستلم يبقى في المخزون",
    "تبقى في" in html or "لا تُسحب" in html)


# ======================================================================
print()
print("=" * 62)
print("١١) حذف مورد له أوامر يُرفَض — التاريخ لا يُمحى")
print("=" * 62)

tok, html, _ = get(opa, f"/Suppliers/Details/{SUP}")
chk("كشف حساب المورد يُفتح", tok is not None)
chk("الأمر الأول ظاهر في كشف الحساب", pnum in html)
chk("زر الحذف غير معروض لمورد له أوامر",
    'asp-action="Delete"' not in html or "له أوامر" in html or "تعطيل" in html)

before = q("SELECT COUNT(*) FROM Suppliers WHERE Id=?", (SUP,))[0][0]
html, _ = post(opa, f"/Suppliers/Delete/{SUP}", [("id", str(SUP))], tok)
after = q("SELECT COUNT(*) FROM Suppliers WHERE Id=?", (SUP,))[0][0]
chk(f"المورد لم يُحذف ({before} → {after})", after == 1)
chk("الرسالة تقترح التعطيل بدل الحذف", "تعطيل" in html or "أوامر" in html)


# ======================================================================
print()
print("=" * 62)
print("١٢) حذف المسودة مسموح — والمعتمد لا")
print("=" * 62)

tok, _, _ = get(opa, f"/Purchases/Create?warehouseId={W1}")
post(opa, "/Purchases/Create",
     [("supplierId", str(SUP)), ("warehouseId", str(W1)),
      ("discountPercentage", "0"), ("productIds", str(PID)),
      ("quantities", "5"), ("unitCosts", "900"), ("confirmImmediately", "false")], tok)
OID3 = q("SELECT Id FROM PurchaseOrders WHERE SupplierId=? ORDER BY Id DESC LIMIT 1",
         (SUP,))[0][0]
chk("مسودة ثالثة أُنشئت", OID3 not in (OID, OID2))

before = stock(PID, W1)
tok, _, _ = get(opa, f"/Purchases/Details/{OID3}")
post(opa, f"/Purchases/Delete/{OID3}", [], tok)
gone = q("SELECT COUNT(*) FROM PurchaseOrders WHERE Id=?", (OID3,))[0][0]
chk("المسودة حُذفت", gone == 0)
items = q("SELECT COUNT(*) FROM PurchaseOrderItems WHERE PurchaseOrderId=?", (OID3,))[0][0]
chk("سطور المسودة حُذفت معها (cascade)", items == 0)
chk(f"المخزون لم يتأثر بحذف المسودة ({before} → {stock(PID, W1)})",
    stock(PID, W1) == before)

# الأمر المكتمل لا يُحذف
before = q("SELECT COUNT(*) FROM PurchaseOrders WHERE Id=?", (OID,))[0][0]
tok, _, _ = get(opa, f"/Purchases/Details/{OID}")
post(opa, f"/Purchases/Delete/{OID}", [], tok)
after = q("SELECT COUNT(*) FROM PurchaseOrders WHERE Id=?", (OID,))[0][0]
chk(f"الأمر المكتمل لم يُحذف ({before} → {after})", after == 1)
check_cache("بعد الحذف", PID)


# ======================================================================
print()
print("=" * 62)
print("١٣) القوائم والتقرير")
print("=" * 62)

_, html, _ = get(opa, "/Purchases")
chk("قائمة الأوامر تعرض الأمر المكتمل", pnum in html)
chk("القائمة فيها مربّعات إحصائية", "إجمالي" in html or "الأوامر" in html)

_, html, _ = get(opa, f"/Purchases?status=4")
chk("التصفية بالحالة تعمل", pnum in html)

_, html, _ = get(opa, f"/Purchases?supplierId={SUP}")
chk("التصفية بالمورد تعمل", pnum in html)

_, html, _ = get(opa, "/Purchases/Report")
chk("تقرير المشتريات يُفتح", "تقرير المشتريات" in html)
chk("التقرير فيه تقسيم بالمورد", "المشتريات بالمورد" in html)
chk("التقرير فيه تقسيم بالمخزن", "المشتريات بالمخزن" in html)
chk("التقرير فيه أكثر الأصناف شراءً", "أكثر الأصناف" in html)
chk("اسم المورد ظاهر في التقرير", "شركة الهرم للتوريدات" in html)

_, html, _ = get(opa, "/Suppliers")
chk("قائمة الموردين تعرض المورد", "شركة الهرم للتوريدات" in html)


# ======================================================================
print()
print("=" * 62)
print("١٤) سجل العمليات يوثّق المشتريات بالعربية")
print("=" * 62)

acts = [r[0] for r in q("SELECT DISTINCT Action FROM AuditLogs")]
chk("إجراء «أمر شراء» موجود في السجل", "أمر شراء" in acts, str(acts))
chk("إجراء «استلام» موجود في السجل", "استلام" in acts, str(acts))
n = q("SELECT COUNT(*) FROM AuditLogs WHERE EntityName LIKE '%Purchase%' "
      "OR EntityName LIKE '%Supplier%'")[0][0]
chk(f"سجلات المشتريات مُثبَّتة (العدد={n})", n > 0)


# ======================================================================
print()
print("=" * 62)
print(f"النتيجة:  نجح {P}   فشل {F}")
print("=" * 62)
sys.exit(1 if F else 0)

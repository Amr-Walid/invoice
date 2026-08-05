#!/usr/bin/env python3
"""
اختبار التحويلات بين المخازن — دورة الحياة كاملة.

يتحقق من الأشياء التي لا يكفي فيها النظر للشاشة:
  * الخصم يقع عند الشحن لا عند الطلب ولا عند الاعتماد
  * الإضافة تقع عند الاستلام لا عند الشحن
  * «في الطريق» = مشحون - مستلم، والقطع فعلًا ليست في رصيد أي مخزن بينهما
  * الشحن الجزئي والاستلام الجزئي (النقص) لا يخترعان قطعًا ولا يبخّرانها
  * تكرار الشحن (زر مضغوط مرتين) لا يخصم ضِعفين
  * كل مخزن يقرر في جهته فقط: المصدر يعتمد/يشحن، الطالب يستلم/يلغي
  * الكاش (Products.StockQuantity) = مجموع ProductStocks بعد كل خطوة

التشغيل: البرنامج يعمل على 5080 بقاعدة SQLite مُهيّأة.
    python3 tests/transfer_test.py
"""
import re, urllib.request, urllib.parse, http.cookiejar, sqlite3, sys, os
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
    """يُرجع (رمز التحقق, html, العنوان النهائي بعد التحويلات)

    Razor يُرمّز العربية القادمة من المتغيرات إلى &#x627; ونحوه، فلو بحثنا عن
    النص كما حُفِظ في قاعدة البيانات لما وجدناه. نفكّ الترميز مرة واحدة هنا لتكون
    كل المقارنات بعدها على نص طبيعي.
    """
    r = op.open(BASE + url)
    raw = r.read().decode()
    m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', raw)
    return (m.group(1) if m else None), htmlmod.unescape(raw), r.geturl()


def post(op, url, pairs, tok):
    body = urllib.parse.urlencode(list(pairs) + [("__RequestVerificationToken", tok)]).encode()
    r = op.open(BASE + url, body)
    return r.read().decode(), r.geturl()


def q(sql, args=()):
    con = sqlite3.connect(DB)
    try:
        return con.execute(sql, args).fetchall()
    finally:
        con.close()


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


# ======================================================================
print("=" * 62)
print("تهيئة: مخزن ثانٍ + رصيد للتحويل")
print("=" * 62)

opa, _ = login("admin@pos.local", "Admin@123")

W2_CODE = "TRF2"
rows = q("SELECT Id FROM Warehouses WHERE Code=?", (W2_CODE,))
if not rows:
    tok, _, _ = get(opa, "/Warehouses/Create")
    post(opa, "/Warehouses/Create",
         [("Code", W2_CODE), ("Name", "مخزن اختبار التحويل"),
          ("ManagerName", "أمين"), ("IsActive", "true")], tok)
    rows = q("SELECT Id FROM Warehouses WHERE Code=?", (W2_CODE,))

W2 = rows[0][0]
W1 = q("SELECT Id FROM Warehouses WHERE Code='MAIN'")[0][0]
chk(f"المخزن الثاني موجود (id={W2})", W2 > 0)
chk("المخزنان مختلفان", W1 != W2)

PID = 1
# نضمن رصيدًا كافيًا في المصدر (W1) قبل البدء
tok, _, _ = get(opa, f"/Warehouses/Stock/{W1}")
if stock(PID, W1) < 20:
    post(opa, "/Warehouses/Adjust",
         [("id", str(W1)), ("productId", str(PID)),
          ("change", str(20 - stock(PID, W1))), ("note", "تهيئة اختبار التحويل")], tok)

SRC0 = stock(PID, W1)
DST0 = stock(PID, W2)
print(f"  البداية: المصدر({W1})={SRC0}  الهدف({W2})={DST0}  الكاش={cache(PID)}")
check_cache("قبل البدء", PID)


# ======================================================================
print()
print("=" * 62)
print("١) شاشة «اطلب من مخزن آخر» تعرض أرصدة المخازن الأخرى")
print("=" * 62)

tok, html, _ = get(opa, f"/Transfers/Create?warehouseId={W2}")
chk("الشاشة تُفتح", tok is not None)
chk("قائمة المخزن المصدر معروضة", 'id="srcSelect"' in html)
chk("المخزن الرئيسي مطروح كمصدر", f'value="{W1}"' in html)
chk("رصيد المخزن الآخر ظاهر في الجدول", 'data-stock=' in html)
chk("المخزن العامل لا يُطرح كمصدر لنفسه",
    f'<option value="{W2}">' not in html)


# ======================================================================
print()
print("=" * 62)
print("٢) الطلب لا يخصم شيئًا — الطلب نية لا حجز")
print("=" * 62)

REQ = 10
_, redirect = post(opa, f"/Transfers/Create?warehouseId={W2}",
                   [("fromWarehouseId", str(W1)), ("notes", "اختبار آلي"),
                    ("productIds", str(PID)), ("quantities", str(REQ))], tok)

tr = q("SELECT Id,TransferNumber,Status,FromWarehouseId,ToWarehouseId "
       "FROM StockTransfers ORDER BY Id DESC LIMIT 1")
chk("طلب التحويل أُنشئ", len(tr) == 1)
TID, TNUM, TSTATUS, TFROM, TTO = tr[0]
chk(f"رقم التحويل بالصيغة TRF-yyyyMMdd-nnnn ({TNUM})",
    re.fullmatch(r"TRF-\d{8}-\d{4}", TNUM) is not None, TNUM)
chk("الحالة = Pending(1)", TSTATUS == 1, TSTATUS)
chk(f"المصدر={W1} والهدف={W2} كما طُلب", TFROM == W1 and TTO == W2, (TFROM, TTO))
chk("الهدف مأخوذ من مخزن المستخدم لا من النموذج", TTO == W2)

item = q("SELECT ProductId,ProductName,Barcode,RequestedQuantity,ShippedQuantity,"
         "ReceivedQuantity FROM StockTransferItems WHERE StockTransferId=?", (TID,))
chk("سطر واحد بالكمية المطلوبة", len(item) == 1 and item[0][3] == REQ, item)
chk("اسم المنتج والباركود محفوظان snapshot",
    bool(item[0][1]) and bool(item[0][2]), item[0][1:3])
chk("المشحون والمستلم صفر عند الطلب", item[0][4] == 0 and item[0][5] == 0, item[0][4:])

chk(f"رصيد المصدر لم يتغير بالطلب ({stock(PID, W1)})", stock(PID, W1) == SRC0)
chk(f"رصيد الهدف لم يتغير بالطلب ({stock(PID, W2)})", stock(PID, W2) == DST0)
chk("لم تُسجَّل حركة مخزون للطلب",
    q("SELECT COUNT(*) FROM StockMovements WHERE ReferenceTransferId=?", (TID,))[0][0] == 0)
check_cache("بعد الطلب", PID)

chk("التوجيه لصفحة التفاصيل", f"/Transfers/Details/{TID}" in redirect, redirect)


# ======================================================================
print()
print("=" * 62)
print("٣) التحويل من المخزن لنفسه مرفوض")
print("=" * 62)

tok, _, _ = get(opa, f"/Transfers/Create?warehouseId={W2}")
n_before = q("SELECT COUNT(*) FROM StockTransfers")[0][0]
post(opa, f"/Transfers/Create?warehouseId={W2}",
     [("fromWarehouseId", str(W2)), ("productIds", str(PID)), ("quantities", "3")], tok)
chk("لم يُنشأ طلب من المخزن لنفسه",
    q("SELECT COUNT(*) FROM StockTransfers")[0][0] == n_before)


# ======================================================================
print()
print("=" * 62)
print("٤) الاعتماد لا يخصم — الخصم عند الشحن فقط")
print("=" * 62)

tok, html, _ = get(opa, f"/Transfers/Details/{TID}")
chk("زر الاعتماد ظاهر للمصدر", "اعتماد الطلب" in html)
chk("زر الشحن غير ظاهر قبل الاعتماد", "شحن القطع" not in html)

post(opa, f"/Transfers/Approve/{TID}", [], tok)
st = q("SELECT Status,ApprovedByName,ApprovedAt FROM StockTransfers WHERE Id=?", (TID,))[0]
chk("الحالة = Approved(2)", st[0] == 2, st[0])
chk(f"اسم المعتمِد محفوظ ({st[1]})", bool(st[1]))
chk("تاريخ الاعتماد محفوظ", st[2] is not None)
chk(f"رصيد المصدر لم يتغير بالاعتماد ({stock(PID, W1)})", stock(PID, W1) == SRC0)
chk(f"رصيد الهدف لم يتغير بالاعتماد ({stock(PID, W2)})", stock(PID, W2) == DST0)
check_cache("بعد الاعتماد", PID)


# ======================================================================
print()
print("=" * 62)
print("٥) الشحن الجزئي: يخصم من المصدر فقط، والفرق ظاهر")
print("=" * 62)

SHIP = 6   # أقل من المطلوب 10 — شحن جزئي مقصود
tok, html, _ = get(opa, f"/Transfers/Details/{TID}")
chk("زر الشحن ظهر بعد الاعتماد", "شحن القطع" in html)

post(opa, f"/Transfers/Ship/{TID}",
     [("productIds", str(PID)), ("quantities", str(SHIP))], tok)

st = q("SELECT Status,ShippedByName,ShippedAt FROM StockTransfers WHERE Id=?", (TID,))[0]
chk("الحالة = Shipped(3)", st[0] == 3, st[0])
chk(f"اسم الشاحن محفوظ ({st[1]})", bool(st[1]))

src1, dst1 = stock(PID, W1), stock(PID, W2)
chk(f"المصدر خُصم {SHIP} ({SRC0} → {src1})", src1 == SRC0 - SHIP)
chk(f"الهدف لم يُضَف له شيء بالشحن ({dst1})", dst1 == DST0)

it = q("SELECT RequestedQuantity,ShippedQuantity,ReceivedQuantity "
       "FROM StockTransferItems WHERE StockTransferId=?", (TID,))[0]
chk(f"المشحون = {SHIP} والمطلوب {REQ} (شحن جزئي)", it[1] == SHIP and it[0] == REQ, it)
chk("المستلم لا يزال صفرًا", it[2] == 0)

mv = q("SELECT Change,Reason,WarehouseId,ReferenceTransferId FROM StockMovements "
       "WHERE ReferenceTransferId=? ORDER BY Id DESC", (TID,))
chk("حركة خروج واحدة مسجّلة", len(mv) == 1, mv)
chk(f"الحركة سالبة بمقدار الشحن ({mv[0][0]})", mv[0][0] == -SHIP)
chk("سبب الحركة = TransferOut(6)", mv[0][1] == 6, mv[0][1])
chk(f"الحركة على المخزن المصدر ({mv[0][2]})", mv[0][2] == W1)
chk("الحركة مربوطة برقم التحويل", mv[0][3] == TID)

# القطع في الطريق: خرجت من المصدر ولم تدخل الهدف — ليست في أي رصيد
chk(f"القطع في الطريق = {SHIP} (ليست في أي مخزن)",
    total(PID) == (SRC0 + DST0) - SHIP,
    f"مجموع الأرصدة={total(PID)} المتوقع={(SRC0 + DST0) - SHIP}")
check_cache("بعد الشحن", PID)

_, html, _ = get(opa, f"/Transfers/Details/{TID}")
chk("الشاشة تُظهر «في الطريق»", "في الطريق" in html)
chk("الشاشة تُظهر نقص الشحن عن المطلوب", "نقص" in html)


# ======================================================================
print()
print("=" * 62)
print("٦) إعادة الشحن (زر مضغوط مرتين) لا تخصم مرتين")
print("=" * 62)

tok, _, _ = get(opa, f"/Transfers/Details/{TID}")
post(opa, f"/Transfers/Ship/{TID}", [("productIds", str(PID)), ("quantities", "3")], tok)
chk(f"رصيد المصدر لم يُخصم مرة ثانية ({stock(PID, W1)})", stock(PID, W1) == src1)
chk("عدد حركات الخروج ما زال 1",
    q("SELECT COUNT(*) FROM StockMovements WHERE ReferenceTransferId=? AND Change<0",
      (TID,))[0][0] == 1)
chk("الحالة ما زالت Shipped(3)",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (TID,))[0][0] == 3)


# ======================================================================
print()
print("=" * 62)
print("٧) الإلغاء بعد الشحن مرفوض — القطع خرجت فعلًا")
print("=" * 62)

tok, _, _ = get(opa, f"/Transfers/Details/{TID}")
post(opa, f"/Transfers/Cancel/{TID}", [], tok)
chk("الحالة لم تصبح Cancelled",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (TID,))[0][0] == 3)
chk(f"الأرصدة لم تتغير بمحاولة الإلغاء ({stock(PID, W1)}, {stock(PID, W2)})",
    stock(PID, W1) == src1 and stock(PID, W2) == dst1)


# ======================================================================
print()
print("=" * 62)
print("٨) استلام أكثر من المشحون مرفوض — لا خلق قطع من العدم")
print("=" * 62)

tok, _, _ = get(opa, f"/Transfers/Details/{TID}")
post(opa, f"/Transfers/Receive/{TID}",
     [("productIds", str(PID)), ("quantities", str(SHIP + 5))], tok)
chk(f"رصيد الهدف لم يزد ({stock(PID, W2)})", stock(PID, W2) == dst1)
chk("الحالة ما زالت Shipped(3)",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (TID,))[0][0] == 3)


# ======================================================================
print()
print("=" * 62)
print("٩) الاستلام الجزئي: يضيف للهدف والنقص يبقى ظاهرًا")
print("=" * 62)

RECV = 5   # أقل من المشحون 6 — نقص مقصود
tok, html, _ = get(opa, f"/Transfers/Details/{TID}")
chk("زر الاستلام ظاهر للطالب", "تسجيل الاستلام" in html)

post(opa, f"/Transfers/Receive/{TID}",
     [("productIds", str(PID)), ("quantities", str(RECV))], tok)

st = q("SELECT Status,ReceivedByName,ReceivedAt FROM StockTransfers WHERE Id=?", (TID,))[0]
chk("الحالة = Received(4)", st[0] == 4, st[0])
chk(f"اسم المستلم محفوظ ({st[1]})", bool(st[1]))

src2, dst2 = stock(PID, W1), stock(PID, W2)
chk(f"الهدف أُضيف له {RECV} ({dst1} → {dst2})", dst2 == dst1 + RECV)
chk(f"المصدر لم يتغير بالاستلام ({src2})", src2 == src1)

it = q("SELECT ShippedQuantity,ReceivedQuantity FROM StockTransferItems "
       "WHERE StockTransferId=?", (TID,))[0]
chk(f"المستلم={RECV} والمشحون={SHIP} (النقص محفوظ لا مُصفّى)",
    it[1] == RECV and it[0] == SHIP, it)

mv_in = q("SELECT Change,Reason,WarehouseId FROM StockMovements "
          "WHERE ReferenceTransferId=? AND Change>0", (TID,))
chk("حركة دخول واحدة مسجّلة", len(mv_in) == 1, mv_in)
chk(f"الحركة موجبة بمقدار المستلم ({mv_in[0][0]})", mv_in[0][0] == RECV)
chk("سبب الحركة = TransferIn(7)", mv_in[0][1] == 7, mv_in[0][1])
chk(f"الحركة على المخزن الهدف ({mv_in[0][2]})", mv_in[0][2] == W2)

# الفرق بين المشحون والمستلم يبقى «في الطريق» حتى يُسوّى بجرد صريح
chk(f"فرق النقص ({SHIP - RECV}) لم يتبخّر ولم يُخترع",
    total(PID) == (SRC0 + DST0) - (SHIP - RECV),
    f"مجموع الأرصدة={total(PID)} المتوقع={(SRC0 + DST0) - (SHIP - RECV)}")
check_cache("بعد الاستلام", PID)


# ======================================================================
print()
print("=" * 62)
print("١٠) الطلب المنتهي لا إجراء بعده")
print("=" * 62)

tok, html, _ = get(opa, f"/Transfers/Details/{TID}")
chk("الشاشة تُعلن انتهاء الطلب", "الطلب منتهٍ" in html)
chk("لا زر اعتماد", "اعتماد الطلب" not in html)
chk("لا زر شحن", "شحن القطع" not in html)
chk("لا زر استلام", "تسجيل الاستلام" not in html)

for act in ("Approve", "Ship", "Receive", "Cancel"):
    tok2, _, _ = get(opa, f"/Transfers/Details/{TID}")
    post(opa, f"/Transfers/{act}/{TID}",
         [("productIds", str(PID)), ("quantities", "2")], tok2)
chk("الحالة ما زالت Received(4) بعد كل المحاولات",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (TID,))[0][0] == 4)
chk(f"الأرصدة لم تتغير ({stock(PID, W1)}, {stock(PID, W2)})",
    stock(PID, W1) == src2 and stock(PID, W2) == dst2)


# ======================================================================
print()
print("=" * 62)
print("١١) الرفض يحتاج سببًا ولا يمس المخزون")
print("=" * 62)

tok, _, _ = get(opa, f"/Transfers/Create?warehouseId={W2}")
post(opa, f"/Transfers/Create?warehouseId={W2}",
     [("fromWarehouseId", str(W1)), ("productIds", str(PID)), ("quantities", "4")], tok)
RID = q("SELECT Id FROM StockTransfers ORDER BY Id DESC LIMIT 1")[0][0]

s_before, d_before = stock(PID, W1), stock(PID, W2)

tok, _, _ = get(opa, f"/Transfers/Details/{RID}")
post(opa, f"/Transfers/Reject/{RID}", [("reason", "")], tok)
chk("الرفض بلا سبب لم يُنفَّذ",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (RID,))[0][0] == 1)

tok, _, _ = get(opa, f"/Transfers/Details/{RID}")
post(opa, f"/Transfers/Reject/{RID}", [("reason", "الكمية مطلوبة لمبيعاتنا")], tok)
st = q("SELECT Status,RejectionReason FROM StockTransfers WHERE Id=?", (RID,))[0]
chk("الحالة = Rejected(5)", st[0] == 5, st[0])
chk(f"سبب الرفض محفوظ ({st[1]})", st[1] == "الكمية مطلوبة لمبيعاتنا")
chk(f"الأرصدة لم تتغير بالرفض ({stock(PID, W1)}, {stock(PID, W2)})",
    stock(PID, W1) == s_before and stock(PID, W2) == d_before)
chk("لا حركة مخزون للطلب المرفوض",
    q("SELECT COUNT(*) FROM StockMovements WHERE ReferenceTransferId=?", (RID,))[0][0] == 0)
check_cache("بعد الرفض", PID)

_, html, _ = get(opa, f"/Transfers/Details/{RID}")
chk("سبب الرفض معروض للطالب", "الكمية مطلوبة لمبيعاتنا" in html)


# ======================================================================
print()
print("=" * 62)
print("١٢) الإلغاء قبل الشحن يعمل ولا يمس المخزون")
print("=" * 62)

tok, _, _ = get(opa, f"/Transfers/Create?warehouseId={W2}")
post(opa, f"/Transfers/Create?warehouseId={W2}",
     [("fromWarehouseId", str(W1)), ("productIds", str(PID)), ("quantities", "2")], tok)
CID = q("SELECT Id FROM StockTransfers ORDER BY Id DESC LIMIT 1")[0][0]

s_before, d_before = stock(PID, W1), stock(PID, W2)
tok, _, _ = get(opa, f"/Transfers/Details/{CID}")
post(opa, f"/Transfers/Cancel/{CID}", [], tok)
chk("الحالة = Cancelled(6)",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (CID,))[0][0] == 6)
chk(f"الأرصدة لم تتغير بالإلغاء ({stock(PID, W1)}, {stock(PID, W2)})",
    stock(PID, W1) == s_before and stock(PID, W2) == d_before)


# ======================================================================
print()
print("=" * 62)
print("١٣) تقرير «في الطريق» يعرض ما خرج ولم يُستلم")
print("=" * 62)

# تحويل جديد يُشحن ولا يُستلم — ليبقى معلّقًا في التقرير
tok, _, _ = get(opa, f"/Transfers/Create?warehouseId={W2}")
post(opa, f"/Transfers/Create?warehouseId={W2}",
     [("fromWarehouseId", str(W1)), ("productIds", str(PID)), ("quantities", "3")], tok)
XID = q("SELECT Id FROM StockTransfers ORDER BY Id DESC LIMIT 1")[0][0]
XNUM = q("SELECT TransferNumber FROM StockTransfers WHERE Id=?", (XID,))[0][0]

tok, _, _ = get(opa, f"/Transfers/Details/{XID}")
post(opa, f"/Transfers/Approve/{XID}", [], tok)
tok, _, _ = get(opa, f"/Transfers/Details/{XID}")
post(opa, f"/Transfers/Ship/{XID}", [("productIds", str(PID)), ("quantities", "3")], tok)

_, html, _ = get(opa, "/Transfers/InTransit")
chk(f"التحويل المشحون ظاهر في «في الطريق» ({XNUM})", XNUM in html, XNUM)
chk("التحويل المستلم غير ظاهر فيه", TNUM not in html, TNUM)
chk("التحويل المرفوض غير ظاهر فيه",
    q("SELECT TransferNumber FROM StockTransfers WHERE Id=?", (RID,))[0][0] not in html)


# ======================================================================
print()
print("=" * 62)
print("١٤) الصلاحيات: كل جهة تقرر في ناحيتها فقط")
print("=" * 62)

# نربط المندوب بالمخزن الهدف ليكون هو الطالب
q_users = q("SELECT Id,WarehouseId FROM AspNetUsers WHERE Email='agent@pos.local'")
AGENT_ID, AGENT_W = q_users[0]

tok, _, _ = get(opa, "/Users")
post(opa, "/Users/ChangeWarehouse",
     [("id", AGENT_ID), ("warehouseId", str(W2))], tok)
chk(f"المندوب مربوط بالمخزن الهدف ({W2})",
    q("SELECT WarehouseId FROM AspNetUsers WHERE Id=?", (AGENT_ID,))[0][0] == W2)

opag, _ = login("agent@pos.local", "Agent@123")

# المندوب في المخزن الطالب: يطلب ويستلم، ولا يعتمد ولا يشحن
tok, html, _ = get(opag, "/Transfers/Create")
chk("المندوب يستطيع فتح شاشة الطلب", tok is not None and 'id="srcSelect"' in html)

post(opag, "/Transfers/Create",
     [("fromWarehouseId", str(W1)), ("productIds", str(PID)), ("quantities", "2")], tok)
AID = q("SELECT Id,RequestedByUserId,ToWarehouseId FROM StockTransfers "
        "ORDER BY Id DESC LIMIT 1")[0]
chk("طلب المندوب أُنشئ", AID[1] == AGENT_ID, AID)
chk(f"الطلب على مخزن المندوب لا غيره ({AID[2]})", AID[2] == W2)
AID = AID[0]

# محاولة الطلب باسم مخزن آخر عبر warehouseId مُرسل — يجب أن يتجاهله الخادم
tok, _, _ = get(opag, f"/Transfers/Create?warehouseId={W1}")
post(opag, f"/Transfers/Create?warehouseId={W1}",
     [("fromWarehouseId", str(W2)), ("productIds", str(PID)), ("quantities", "1")], tok)
last = q("SELECT ToWarehouseId,FromWarehouseId FROM StockTransfers ORDER BY Id DESC LIMIT 1")[0]
chk(f"warehouseId المُرسل لم يُبدّل مخزن المندوب ({last})", last[0] == W2, last)

# المندوب لا يعتمد طلبه (ليس أمين المخزن المصدر)
tok, _, _ = get(opag, f"/Transfers/Details/{AID}")
try:
    post(opag, f"/Transfers/Approve/{AID}", [], tok)
except urllib.error.HTTPError as e:
    pass
chk("المندوب لم يستطع اعتماد الطلب",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (AID,))[0][0] == 1)

# «طلبات واردة» ممنوعة على المندوب — القرار للمصدر
_, _, url = get(opag, "/Transfers/Incoming")
chk(f"شاشة «واردة» ممنوعة على المندوب ({url.split('?')[0]})", "AccessDenied" in url, url)

_, _, url = get(opag, "/Transfers/InTransit")
chk("تقرير «في الطريق» ممنوع على المندوب", "AccessDenied" in url, url)

_, _, url = get(opag, "/Transfers/All")
chk("سجل التحويلات الكامل ممنوع على المندوب", "AccessDenied" in url, url)

# أمين المخزن يقرر في مخزنه فقط
opk, _ = login("keeper@pos.local", "Keeper@123")
KW = q("SELECT WarehouseId FROM AspNetUsers WHERE Email='keeper@pos.local'")[0][0]
_, html, url = get(opk, "/Transfers/Incoming")
chk(f"أمين المخزن يفتح «واردة» على مخزنه ({KW})", "AccessDenied" not in url, url)

_, _, url = get(opk, "/Transfers/All")
chk("سجل التحويلات الكامل للمدير وحده", "AccessDenied" in url, url)

# الاستلام حق الطالب: المندوب (في W2) يستلم طلبه بعد اعتماده وشحنه من الإدارة
tok, _, _ = get(opa, f"/Transfers/Details/{AID}")
post(opa, f"/Transfers/Approve/{AID}", [], tok)
tok, _, _ = get(opa, f"/Transfers/Details/{AID}")
post(opa, f"/Transfers/Ship/{AID}", [("productIds", str(PID)), ("quantities", "2")], tok)

d_pre = stock(PID, W2)
tok, _, _ = get(opag, f"/Transfers/Details/{AID}")
post(opag, f"/Transfers/Receive/{AID}", [("productIds", str(PID)), ("quantities", "2")], tok)
chk(f"المندوب استلم لمخزنه ({d_pre} → {stock(PID, W2)})", stock(PID, W2) == d_pre + 2)
chk("الحالة = Received(4)",
    q("SELECT Status FROM StockTransfers WHERE Id=?", (AID,))[0][0] == 4)
check_cache("بعد استلام المندوب", PID)

# نُعيد المندوب لمخزنه الأصلي حتى لا تتأثر اختبارات أخرى
tok, _, _ = get(opa, "/Users")
post(opa, "/Users/ChangeWarehouse",
     [("id", AGENT_ID), ("warehouseId", str(AGENT_W or W1))], tok)


# ======================================================================
print()
print("=" * 62)
print("١٥) سجل العمليات يسجّل كل خطوة")
print("=" * 62)

logs = q("SELECT Action,EntityName,EntityId FROM AuditLogs "
         "WHERE EntityName='StockTransfer' ORDER BY Id DESC LIMIT 40")
actions = {r[0] for r in logs}
# قيم AuditActions عربية لا إنجليزية — هي ما يراه المدير في شاشة السجل مباشرة.
chk("الطلب مسجّل (تحويل مخزني)", "تحويل مخزني" in actions, actions)
chk("الاعتماد مسجّل (اعتماد)", "اعتماد" in actions, actions)
chk("الرفض مسجّل (رفض)", "رفض" in actions, actions)
chk("الاستلام مسجّل (استلام)", "استلام" in actions, actions)
chk("رقم التحويل هو معرّف السجل", any(r[2] == TNUM for r in logs), TNUM)


# ======================================================================
print()
print("=" * 62)
print(f"النتيجة النهائية:  PASS={P}   FAIL={F}")
print("=" * 62)
sys.exit(1 if F else 0)

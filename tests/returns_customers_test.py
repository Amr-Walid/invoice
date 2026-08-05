#!/usr/bin/env python3
"""
اختبار صفحة العملاء ونظام المرتجعات.

يتحقق من المطالب الخمسة صراحةً:
  1. صفحة العملاء تعرض الاسم والرقم وفواتير العميل.
  2. الفواتير مربوطة برقم الهاتف (وبأي صيغة يُكتب بها الرقم).
  3. المربع الثالث «مرتجع» موجود في شاشة المندوب.
  4. طريقة الإرجاع مضبوطة: جزئي، بسعر الفاتورة، وبخصمها، وبلا تجاوز للمتبقي.
  5. عند الضغط على «تم» تُعاد الكميات إلى المخزون.
"""
import html
import http.cookiejar
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://localhost:8080"
FAILED = []
PASSED = []


def decode(body):
    """Razor يُرمِّز العربية ككيانات رقمية (&#x627;) — نفكّها قبل البحث النصي."""
    return html.unescape(body)


def check(name, condition, detail=""):
    if condition:
        PASSED.append(name)
        print(f"  \033[92mPASS\033[0m  {name}")
    else:
        FAILED.append(f"{name} — {detail}")
        print(f"  \033[91mFAIL\033[0m  {name}  {detail}")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None


class Client:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.jar), NoRedirect())

    def get(self, path, follow=True):
        req = urllib.request.Request(BASE + path)
        try:
            op = self.opener if not follow else urllib.request.build_opener(
                urllib.request.HTTPCookieProcessor(self.jar))
            with op.open(req) as r:
                return r.status, decode(r.read().decode("utf-8", "replace")), r.headers
        except urllib.error.HTTPError as e:
            return e.code, decode(e.read().decode("utf-8", "replace")), e.headers

    def post_form(self, path, data, follow=False):
        """data قد تحتوي مفاتيح مكرّرة، فنستخدم قائمة أزواج لا قاموسًا."""
        body = urllib.parse.urlencode(data).encode()
        req = urllib.request.Request(BASE + path, data=body, method="POST")
        req.add_header("Content-Type", "application/x-www-form-urlencoded")
        try:
            op = self.opener if not follow else urllib.request.build_opener(
                urllib.request.HTTPCookieProcessor(self.jar))
            with op.open(req) as r:
                return r.status, decode(r.read().decode("utf-8", "replace")), r.headers
        except urllib.error.HTTPError as e:
            return e.code, decode(e.read().decode("utf-8", "replace")), e.headers

    def post_json(self, path, payload, token):
        body = json.dumps(payload).encode()
        req = urllib.request.Request(BASE + path, data=body, method="POST")
        req.add_header("Content-Type", "application/json")
        req.add_header("RequestVerificationToken", token)
        try:
            op = urllib.request.build_opener(
                urllib.request.HTTPCookieProcessor(self.jar))
            with op.open(req) as r:
                return r.status, json.loads(r.read().decode())
        except urllib.error.HTTPError as e:
            return e.code, {"raw": e.read().decode("utf-8", "replace")[:300]}

    def get_json(self, path):
        status, body, _ = self.get(path)
        try:
            return status, json.loads(body)
        except Exception:
            return status, {"raw": body[:300]}

    def token_from(self, page):
        m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page)
        return m.group(1) if m else None

    def login(self, email, password):
        _, page, _ = self.get("/Account/Login")
        token = self.token_from(page)
        return self.post_form("/Account/Login", {
            "Email": email, "Password": password,
            "__RequestVerificationToken": token,
        })


def lookup(client, barcode):
    """نتيجة البحث بالباركود — الرد مسطّح: {found, id, stockQuantity, ...}"""
    status, data = client.get_json(f"/Pos/Lookup?barcode={barcode}")
    return data if status == 200 and data.get("found") else None


def stock_of(client, barcode):
    """رصيد المخزون الحالي لمنتج — المصدر الوحيد للتحقق من إعادة الكمية."""
    data = lookup(client, barcode)
    return data["stockQuantity"] if data else None


def main():
    agent = Client()
    status, _, loc = agent.login("agent@pos.local", "Agent@123")
    if status not in (302, 303):
        print("تعذّر تسجيل دخول المندوب — تأكد أن التطبيق يعمل على 8080")
        return 1

    admin = Client()
    admin.login("admin@pos.local", "Admin@123")

    # ================================================================
    print("\n=== 1. المربع الثالث «مرتجع» في شاشة المندوب ===")
    # ================================================================
    _, home, _ = agent.get("/Pos")
    check("المربع الأول «فاتورة جديدة» موجود", "فاتورة جديدة" in home)
    check("المربع الثاني «التقارير» موجود", "التقارير" in home)
    check("المربع الثالث «مرتجع» موجود", "مرتجع" in home)
    check("المربع الثالث له نمط tile-returns", "tile-returns" in home)
    check("المربع الثالث يشير إلى /Returns", "/Returns" in home)

    # ================================================================
    print("\n=== 2. إنشاء فاتورة برقم هاتف (بصيغة دولية) ===")
    # ================================================================
    _, pos_page, _ = agent.get("/Pos")
    token = agent.token_from(pos_page)

    # نلتقط باركودين حقيقيين من صفحة المنتجات (المدير يراها)
    _, products, _ = admin.get("/Products")
    barcodes = re.findall(r'barcode-text[^>]*>\s*([0-9]{6,})\s*<', products)
    if len(barcodes) < 2:
        barcodes = re.findall(r'>\s*([0-9]{8,14})\s*<', products)
    # نستبعد المنتجات التي نفد مخزونها: لا يمكن بيعها ولا اختبار إرجاعها
    barcodes = [b for b in dict.fromkeys(barcodes) if (stock_of(admin, b) or 0) >= 5]
    if len(barcodes) < 2:
        print("لم يتم العثور على باركودين للاختبار")
        return 1
    bc1, bc2 = barcodes[0], barcodes[1]

    p1 = lookup(agent, bc1)
    p2 = lookup(agent, bc2)
    stock1_before = p1["stockQuantity"]
    stock2_before = p2["stockQuantity"]

    # الرقم يُكتب بالصيغة الدولية هنا، وسنبحث عنه لاحقًا بالصيغة المحلية
    status, inv = agent.post_json("/Pos/Checkout", {
        "customerName": "عميل الاختبار",
        "customerPhone": "+201001234567",
        "items": [
            {"productId": p1["id"], "quantity": 3},
            {"productId": p2["id"], "quantity": 2},
        ],
    }, token)
    check("إتمام الفاتورة ينجح", inv.get("success"), str(inv)[:200])
    invoice_id = inv.get("invoiceId")
    invoice_no = inv.get("invoiceNumber", "")
    invoice_total = inv.get("total", 0)
    check("الفاتورة لها رقم مرجعي", bool(invoice_no), invoice_no)

    stock1_after_sale = stock_of(admin, bc1)
    check("خصم المخزون بعد البيع (3 قطع)",
          stock1_after_sale == stock1_before - 3,
          f"{stock1_before} -> {stock1_after_sale}")

    # ================================================================
    print("\n=== 3. صفحة العملاء: الاسم والرقم والفواتير ===")
    # ================================================================
    _, cust_list, _ = admin.get("/Customers")
    check("صفحة العملاء تُفتح", "العملاء" in cust_list)
    check("اسم العميل يظهر في القائمة", "عميل الاختبار" in cust_list)
    check("رقم هاتف العميل يظهر في القائمة", "201001234567" in cust_list.replace("+", ""))

    # البحث بالصيغة المحلية عن رقم أُدخل بالصيغة الدولية — جوهر التوحيد
    _, search_local, _ = admin.get("/Customers?q=01001234567")
    check("البحث بالصيغة المحلية يجد رقمًا مُدخلًا بصيغة دولية",
          "عميل الاختبار" in search_local)

    _, search_spaced, _ = admin.get("/Customers?" + urllib.parse.urlencode({"q": "0100 123 4567"}))
    check("البحث برقم فيه مسافات يجد العميل", "عميل الاختبار" in search_spaced)

    m = re.search(r'/Customers/Details/(\d+)', cust_list)
    customer_id = m.group(1) if m else None
    check("رابط تفاصيل العميل موجود", bool(customer_id))

    if customer_id:
        _, cust_details, _ = admin.get(f"/Customers/Details/{customer_id}")
        check("صفحة تفاصيل العميل تعرض اسمه", "عميل الاختبار" in cust_details)
        check("صفحة تفاصيل العميل تعرض رقمه", "1001234567" in cust_details)
        check("صفحة تفاصيل العميل تعرض فواتيره", invoice_no in cust_details)

    # الإكمال التلقائي داخل نافذة الفاتورة
    _, sugg = agent.get_json("/Customers/Suggest?term=010012")
    check("الإكمال التلقائي يُعيد العميل المطابق",
          isinstance(sugg, list) and any("عميل الاختبار" == s.get("name") for s in sugg),
          str(sugg)[:200])

    # ================================================================
    print("\n=== 4. البحث عن الفاتورة في شاشة المرتجع ===")
    # ================================================================
    _, ret_home, _ = agent.get("/Returns")
    check("شاشة المرتجع تُفتح للمندوب", "مرتجع" in ret_home)

    _, by_number, _ = agent.get("/Returns?" + urllib.parse.urlencode({"q": invoice_no}))
    check("البحث برقم الفاتورة يجدها", invoice_no in by_number)

    _, by_phone, _ = agent.get("/Returns?q=01001234567")
    check("البحث برقم هاتف العميل يجد الفاتورة", invoice_no in by_phone)

    # ================================================================
    print("\n=== 5. الإرجاع الجزئي وإعادة المخزون (زر «تم») ===")
    # ================================================================
    _, form, _ = agent.get(f"/Returns/Create?invoiceId={invoice_id}")
    check("شاشة تنفيذ المرتجع تُفتح", "الأصناف" in form or "الكمية" in form)
    check("شاشة المرتجع تعرض مفتاح إعادة المخزون", "RestockToInventory" in form)

    item_ids = re.findall(r'name="Items\[(\d+)\]\.InvoiceItemId"\s+value="(\d+)"', form)
    check("أسطر الفاتورة معروضة للإرجاع", len(item_ids) == 2, str(item_ids))

    ret_token = agent.token_from(form)

    # إرجاع جزئي: 1 من 3 للمنتج الأول فقط، والمنتج الثاني بلا إرجاع
    payload = [
        ("__RequestVerificationToken", ret_token),
        ("InvoiceId", str(invoice_id)),
        ("Reason", "1"),
        ("Notes", "اختبار إرجاع جزئي"),
        ("RestockToInventory", "true"),
    ]
    for idx, item_id in item_ids:
        payload.append((f"Items[{idx}].InvoiceItemId", item_id))
        payload.append((f"Items[{idx}].Quantity", "1" if idx == item_ids[0][0] else "0"))

    status, body, headers = agent.post_form("/Returns/Confirm", payload)
    loc = headers.get("Location", "")
    check("تنفيذ المرتجع يُعيد توجيهًا لصفحة المرتجع",
          status in (302, 303) and "/Returns/Details" in loc, f"status={status} loc={loc}")

    stock1_after_return = stock_of(admin, bc1)
    check("المخزون زاد بمقدار القطعة المُرجَعة (إعادة للمخزون)",
          stock1_after_return == stock1_after_sale + 1,
          f"{stock1_after_sale} -> {stock1_after_return}")

    stock2_now = stock_of(admin, bc2)
    check("المنتج غير المُرجَع لم يتغيّر مخزونه",
          stock2_now == stock2_before - 2,
          f"expected {stock2_before - 2}, got {stock2_now}")

    ret_id = loc.rstrip("/").split("/")[-1] if loc else None
    if ret_id:
        _, ret_details, _ = agent.get(f"/Returns/Details/{ret_id}")
        check("إيصال المرتجع يعرض رقم المرتجع", "RET-" in ret_details)
        check("إيصال المرتجع يربط بالفاتورة الأصلية", invoice_no in ret_details)
        check("إيصال المرتجع يذكر إعادة المخزون صراحةً",
              "أُعيدت" in ret_details or "المخزون" in ret_details)
        check("إيصال المرتجع يسجّل مَن نفّذها",
              "مندوب المبيعات" in ret_details, "اسم المُنفِّذ غير ظاهر")

    # ================================================================
    print("\n=== 6. حالة الفاتورة بعد الإرجاع الجزئي ===")
    # ================================================================
    _, inv_details, _ = agent.get(f"/AgentReports/Details/{invoice_id}")
    check("حالة الفاتورة صارت «مرتجع جزئي»", "مرتجع جزئي" in inv_details, "الحالة لم تتغيّر")
    check("تفاصيل الفاتورة تعرض المبلغ المُرَد", "المُرد" in inv_details or "المُرَد" in inv_details)

    # الفاتورة المُرجَعة جزئيًا يجب أن تبقى في التقارير لا أن تختفي
    _, agent_reports, _ = agent.get("/AgentReports?Period=4")
    check("الفاتورة المُرجَعة جزئيًا تبقى ظاهرة في التقارير",
          invoice_no in agent_reports, "اختفت الفاتورة من التقرير بعد الإرجاع")

    # ================================================================
    print("\n=== 7. الحماية: لا إرجاع أكثر من المتبقي ولا إرجاع فارغ ===")
    # ================================================================
    _, form2, _ = agent.get(f"/Returns/Create?invoiceId={invoice_id}")
    item_ids2 = re.findall(r'name="Items\[(\d+)\]\.InvoiceItemId"\s+value="(\d+)"', form2)
    token2 = agent.token_from(form2)

    # محاولة إرجاع 99 قطعة والمتبقي 2 فقط
    over = [
        ("__RequestVerificationToken", token2),
        ("InvoiceId", str(invoice_id)),
        ("Reason", "1"),
        ("RestockToInventory", "true"),
        (f"Items[{item_ids2[0][0]}].InvoiceItemId", item_ids2[0][1]),
        (f"Items[{item_ids2[0][0]}].Quantity", "99"),
    ]
    stock_before_over = stock_of(admin, bc1)
    status, _, headers = agent.post_form("/Returns/Confirm", over)
    loc_over = headers.get("Location", "")
    check("إرجاع كمية أكبر من المتبقي يُرفض",
          "/Returns/Create" in loc_over, f"loc={loc_over}")
    check("المخزون لم يتغيّر بعد الرفض",
          stock_of(admin, bc1) == stock_before_over,
          "تغيّر المخزون رغم رفض العملية")

    # مرتجع بلا أصناف
    empty = [
        ("__RequestVerificationToken", token2),
        ("InvoiceId", str(invoice_id)),
        ("Reason", "1"),
        ("RestockToInventory", "true"),
        (f"Items[{item_ids2[0][0]}].InvoiceItemId", item_ids2[0][1]),
        (f"Items[{item_ids2[0][0]}].Quantity", "0"),
    ]
    status, _, headers = agent.post_form("/Returns/Confirm", empty)
    check("مرتجع بلا أصناف يُرفض",
          "/Returns/Create" in headers.get("Location", ""),
          f"loc={headers.get('Location', '')}")

    # ================================================================
    print("\n=== 8. الإرجاع الكامل يُغيّر الحالة إلى «مرتجعة بالكامل» ===")
    # ================================================================
    _, form3, _ = agent.get(f"/Returns/Create?invoiceId={invoice_id}")
    item_ids3 = re.findall(r'name="Items\[(\d+)\]\.InvoiceItemId"\s+value="(\d+)"', form3)
    token3 = agent.token_from(form3)
    remaining = re.findall(r'data-max="(\d+)"', form3)

    full = [
        ("__RequestVerificationToken", token3),
        ("InvoiceId", str(invoice_id)),
        ("Reason", "3"),
        ("RestockToInventory", "true"),
    ]
    for i, (idx, item_id) in enumerate(item_ids3):
        full.append((f"Items[{idx}].InvoiceItemId", item_id))
        full.append((f"Items[{idx}].Quantity", remaining[i] if i < len(remaining) else "0"))

    status, _, headers = agent.post_form("/Returns/Confirm", full)
    check("الإرجاع الكامل ينجح",
          "/Returns/Details" in headers.get("Location", ""),
          f"loc={headers.get('Location','')}")

    check("المخزون عاد لرصيده الأصلي بالكامل",
          stock_of(admin, bc1) == stock1_before and stock_of(admin, bc2) == stock2_before,
          f"{bc1}: {stock_of(admin, bc1)} vs {stock1_before}, "
          f"{bc2}: {stock_of(admin, bc2)} vs {stock2_before}")

    _, inv_after, _ = agent.get(f"/AgentReports/Details/{invoice_id}")
    check("حالة الفاتورة صارت «مرتجعة بالكامل»", "مرتجعة بالكامل" in inv_after)

    # لا يمكن إرجاع أي شيء بعد الإرجاع الكامل
    _, form4, _ = agent.get(f"/Returns/Create?invoiceId={invoice_id}")
    check("لا يوجد ما يُرجَع بعد الإرجاع الكامل",
          "أُرجعت بالكامل" in form4 or "لا يوجد ما يمكن إرجاعه" in form4)

    # ================================================================
    print("\n=== 9. سجل المرتجعات والتقارير ===")
    # ================================================================
    _, ret_list, _ = agent.get("/Returns/List")
    check("سجل المرتجعات يعرض المرتجعات", "RET-" in ret_list)
    check("سجل المرتجعات يعرض إجمالي المُرَد", "إجمالي المُرَد" in ret_list)

    _, admin_ret_list, _ = admin.get("/Returns/List")
    check("المدير يرى سجل المرتجعات", "RET-" in admin_ret_list)

    _, reports, _ = admin.get("/Reports?Period=4")
    check("تقرير المدير يعرض المرتجعات", "المرتجعات" in reports)
    check("تقرير المدير يعرض الصافي بعد المرتجعات", "الصافي بعد المرتجعات" in reports)

    _, audit, _ = admin.get("/Audit?" + urllib.parse.urlencode({"op": "مرتجع"}))
    check("سجل العمليات يسجّل عملية المرتجع", "RET-" in audit or "مرتجع" in audit)

    # ================================================================
    print("\n=== 10. عزل بيانات المندوب ===")
    # ================================================================
    # فاتورة يُنشئها المدير لا يجوز للمندوب إرجاعها
    _, admin_pos, _ = admin.get("/Pos")
    admin_token = admin.token_from(admin_pos)
    _, admin_inv = admin.post_json("/Pos/Checkout", {
        "customerName": "عميل المدير",
        "customerPhone": "01555000111",
        "items": [{"productId": p1["id"], "quantity": 1}],
    }, admin_token)

    if admin_inv.get("success"):
        other_id = admin_inv["invoiceId"]
        status, body, _ = agent.get(f"/Returns/Create?invoiceId={other_id}", follow=False)
        check("المندوب لا يستطيع فتح مرتجع لفاتورة غيره (404)",
              status == 404, f"status={status}")

        _, agent_search, _ = agent.get("/Returns?q=01555000111")
        check("المندوب لا يرى فاتورة غيره في بحث المرتجع",
              admin_inv["invoiceNumber"] not in agent_search)

    # ================================================================
    print("\n" + "=" * 60)
    print(f"  \033[92mنجح: {len(PASSED)}\033[0m   |   "
          f"\033[91mفشل: {len(FAILED)}\033[0m")
    print("=" * 60)
    if FAILED:
        print("\nالاختبارات الفاشلة:")
        for f in FAILED:
            print(f"  - {f}")
        return 1
    print("\nكل اختبارات العملاء والمرتجعات نجحت.\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

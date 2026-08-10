#!/usr/bin/env python3
"""
اختبار دخان (Smoke Test) لنظام المبيعات.
يتحقق من: الدخول، التوجيه بالـ Role، البحث بالباركود، الكوبون،
إتمام الفاتورة، خصم المخزون، عزل بيانات المندوب، وحماية صفحات الإدارة.
"""
import html
import http.cookiejar
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request


def decode(body):
    """Razor يُرمِّز العربية ككيانات رقمية (&#x627;) — نفكّها قبل البحث النصي."""
    return html.unescape(body)

BASE = "http://localhost:8080"
FAILED = []
PASSED = []


def check(name, condition, detail=""):
    if condition:
        PASSED.append(name)
        print(f"  \033[92mPASS\033[0m  {name}")
    else:
        FAILED.append(f"{name} — {detail}")
        print(f"  \033[91mFAIL\033[0m  {name}  {detail}")


class Client:
    """عميل HTTP يحفظ الكوكيز ويستخرج توكن مضاد CSRF."""

    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.jar),
            NoRedirect(),
        )

    def get(self, path, follow=True):
        req = urllib.request.Request(BASE + path)
        try:
            op = self.opener if not follow else urllib.request.build_opener(
                urllib.request.HTTPCookieProcessor(self.jar))
            with op.open(req) as r:
                return r.status, decode(r.read().decode("utf-8", "replace")), r.headers
        except urllib.error.HTTPError as e:
            return e.code, decode(e.read().decode("utf-8", "replace")), e.headers

    def post_form(self, path, data):
        body = urllib.parse.urlencode(data).encode()
        req = urllib.request.Request(BASE + path, data=body, method="POST")
        req.add_header("Content-Type", "application/x-www-form-urlencoded")
        try:
            with self.opener.open(req) as r:
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
            return e.code, {"raw": e.read().decode("utf-8", "replace")[:200]}

    def get_json(self, path):
        status, body, _ = self.get(path)
        try:
            return status, json.loads(body)
        except Exception:
            return status, {"raw": body[:200]}

    def token_from(self, html):
        m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html)
        return m.group(1) if m else None

    def login(self, email, password):
        _, html, _ = self.get("/Account/Login")
        token = self.token_from(html)
        status, body, headers = self.post_form("/Account/Login", {
            "Email": email, "Password": password,
            "__RequestVerificationToken": token,
        })
        return status, headers.get("Location", ""), body


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None


def main():
    print("\n=== 1. صفحة الدخول والتوجيه حسب الـ Role ===")

    agent = Client()
    status, loc, _ = agent.login("agent@pos.local", "Agent@123")
    check("دخول المندوب ينجح", status in (302, 303), f"status={status}")
    check("المندوب يُوجَّه إلى نقطة البيع (Pos)", "/Pos" in loc, f"location={loc}")

    admin = Client()
    status, loc, _ = admin.login("admin@pos.local", "Admin@123")
    check("دخول المدير ينجح", status in (302, 303), f"status={status}")
    check("المدير يُوجَّه إلى لوحة التحكم (Dashboard)", "/Dashboard" in loc, f"location={loc}")

    bad = Client()
    status, loc, body = bad.login("agent@pos.local", "WrongPass1")
    check("كلمة مرور خاطئة تُرفض", status == 200 and "غير صحيحة" in body, f"status={status}")

    print("\n=== 2. صفحة المندوب: المربعان الكبيران ===")
    status, html, _ = agent.get("/Pos")
    check("صفحة المندوب تُفتح", status == 200, f"status={status}")
    check("مربع «فاتورة جديدة» موجود", "فاتورة جديدة" in html)
    check("مربع «التقارير» موجود", "التقارير" in html)
    check("الصفحة RTL وبالعربية", 'dir="rtl"' in html and 'lang="ar"' in html)
    check("نافذة POS المنبثقة محمّلة", 'id="posModal"' in html)
    check("حقل الباركود موجود", 'id="barcodeInput"' in html)

    print("\n=== 3. البحث بالباركود ===")
    status, data = agent.get_json("/Pos/Lookup?barcode=6941238701234")
    check("باركود صحيح يُرجع المنتج", data.get("found") is True, str(data)[:120])
    check("السعر يعود من قاعدة البيانات", data.get("price") == 4200.0, f"price={data.get('price')}")
    stock_before = data.get("stockQuantity")
    check("الكمية المتاحة تعود", isinstance(stock_before, int) and stock_before >= 2,
          f"stock={stock_before}")

    status, data = agent.get_json("/Pos/Lookup?barcode=0000000000000")
    check("باركود غير موجود يُرفض برسالة عربية",
          data.get("found") is False and "لا يوجد" in (data.get("message") or ""), str(data)[:120])

    print("\n=== 4. التحقق من كوبون الخصم ===")
    _, html, _ = agent.get("/Pos")
    token = agent.token_from(html)
    check("توكن مضاد CSRF موجود في الصفحة", token is not None)

    status, data = agent.post_json("/Pos/ValidateCoupon", {"code": "save20"}, token)
    check("كوبون صحيح يُقبل (وحالة الأحرف لا تهم)", data.get("isValid") is True, str(data)[:120])
    check("نسبة الخصم = 20%", data.get("discountPercentage") == 20.0, str(data.get("discountPercentage")))

    status, data = agent.post_json("/Pos/ValidateCoupon", {"code": "NOPE123"}, token)
    check("كوبون غير موجود يُرفض", data.get("isValid") is False, str(data)[:120])

    print("\n=== 5. إتمام فاتورة (منتجان + خصم 20%) ===")
    # 2 × 4200 = 8400 ، 1 × 650 = 650 ، الفرعي = 9050
    # الخصم 20% = 1810 ، الإجمالي = 7240
    payload = {
        "items": [
            {"productId": 1, "barcode": "6941238701234", "quantity": 2},
            {"productId": 6, "barcode": "6941238703221", "quantity": 1},
        ],
        "couponCode": "SAVE20",
        "customerName": "سمير عبد اللطيف",
        "customerPhone": "01000000000",
    }
    status, data = agent.post_json("/Pos/Checkout", payload, token)
    check("الفاتورة تُحفظ بنجاح", data.get("success") is True, str(data)[:200])
    check("الإجمالي الفرعي = 9050.00", float(data.get("subTotal", 0)) == 9050.0, str(data.get("subTotal")))
    check("قيمة الخصم = 1810.00", float(data.get("discountAmount", 0)) == 1810.0, str(data.get("discountAmount")))
    check("الإجمالي بعد الخصم = 7240.00", float(data.get("total", 0)) == 7240.0, str(data.get("total")))
    check("رقم مرجعي بصيغة INV-yyyyMMdd-####",
          re.match(r"^INV-\d{8}-\d{4}$", data.get("invoiceNumber", "") or "") is not None,
          data.get("invoiceNumber"))
    invoice_id = data.get("invoiceId")
    invoice_no = data.get("invoiceNumber")

    print("\n=== 6. خصم المخزون تلقائيًا ===")
    status, data = agent.get_json("/Pos/Lookup?barcode=6941238701234")
    check("المخزون انخفض بمقدار الكمية المبيعة (2)",
          data.get("stockQuantity") == stock_before - 2,
          f"before={stock_before} after={data.get('stockQuantity')}")

    print("\n=== 7. منع البيع عند عدم كفاية الكمية ===")
    _, html, _ = agent.get("/Pos")
    token = agent.token_from(html)
    status, data = agent.post_json("/Pos/Checkout", {
        "items": [{"productId": 1, "barcode": "6941238701234", "quantity": 9999}],
        "couponCode": None, "customerName": None, "customerPhone": None,
    }, token)
    check("الكمية الزائدة تُرفض", data.get("success") is False, str(data)[:150])
    check("رسالة الرفض عربية وواضحة", "الكمية غير كافية" in (data.get("message") or ""), str(data.get("message")))

    status, data = agent.get_json("/Pos/Lookup?barcode=6941238701234")
    check("المخزون لم يتغير بعد الرفض (rollback سليم)",
          data.get("stockQuantity") == stock_before - 2,
          f"stock={data.get('stockQuantity')}")

    print("\n=== 8. رفض الفاتورة الفارغة ===")
    status, data = agent.post_json("/Pos/Checkout", {"items": []}, token)
    check("فاتورة فارغة تُرفض", data.get("success") is False, str(data)[:150])

    print("\n=== 9. تقارير المندوب وتفاصيل الفاتورة ===")
    status, html, _ = agent.get("/AgentReports")
    check("صفحة تقارير المندوب تُفتح", status == 200, f"status={status}")
    check("الفاتورة تظهر في تقاريره", invoice_no in html, f"invoice={invoice_no}")

    status, html, _ = agent.get(f"/AgentReports/Details/{invoice_id}")
    check("تفاصيل الفاتورة تُفتح للمندوب", status == 200, f"status={status}")
    check("اسم العميل يظهر", "سمير عبد اللطيف" in html)
    check("كود الخصم يظهر", "SAVE20" in html)
    check("المندوب لا يرى الأرباح", "الربح" not in html)

    print("\n=== 10. طباعة الفاتورة (إيصال حراري) ===")
    status, html, _ = agent.get(f"/Pos/Receipt/{invoice_id}")
    check("صفحة الإيصال تُفتح", status == 200, f"status={status}")
    check("الإيصال يحتوي رقم الفاتورة", invoice_no in html)
    check("الإيصال يحتوي الإجمالي", "7,240.00" in html or "7240.00" in html)

    print("\n=== 11. صلاحيات: المندوب ممنوع من صفحات الإدارة ===")
    for path in ["/Products", "/Brands", "/Categories", "/Coupons", "/Reports", "/Users", "/Audit", "/Dashboard"]:
        status, body, headers = agent.get(path, follow=False)
        loc = headers.get("Location", "") if headers else ""
        blocked = status in (302, 303, 401, 403) and ("AccessDenied" in loc or "Login" in loc) or status == 403
        check(f"المندوب ممنوع من {path}", blocked, f"status={status} loc={loc}")

    print("\n=== 12. صفحات الإدارة تعمل للمدير ===")
    for path, marker in [
        ("/Dashboard", "لوحة التحكم"),
        ("/Products", "المنتجات"),
        ("/Brands", "Infinix"),
        ("/Categories", "Smartphones"),
        ("/Coupons", "SAVE20"),
        ("/Reports", "تقرير المبيعات"),
        ("/Reports/TopProducts", "الأكثر مبيعًا"),
        ("/Users", "المستخدمون"),
        ("/Audit", "سجل العمليات"),
    ]:
        status, html, _ = admin.get(path)
        check(f"{path} تعمل للمدير", status == 200 and marker in html, f"status={status}")

    print("\n=== 13. المدير يرى تفاصيل الفاتورة (بلا تكلفة ولا أرباح) ===")
    status, html, _ = admin.get(f"/Reports/Details/{invoice_id}")
    check("المدير يفتح تفاصيل فاتورة المندوب", status == 200, f"status={status}")
    check("اسم المندوب البائع يظهر", "مندوب المبيعات" in html)
    # النظام لا يتعامل مع التكلفة ولا الأرباح بطلب صريح: سعر المنتج وحده يكفي.
    check("لا يظهر «الربح» في تفاصيل الفاتورة", "الربح" not in html)
    check("لا يظهر «إجمالي التكلفة» في تفاصيل الفاتورة", "إجمالي التكلفة" not in html)
    check("لا يظهر «تكلفة الوحدة» في أصناف الفاتورة", "تكلفة الوحدة" not in html)
    check("تفاصيل الفاتورة ما زالت تعرض الإجمالي الكلي", "الإجمالي الكلي" in html)

    print("\n=== 14. سجل العمليات (Audit Log) ===")
    status, html, _ = admin.get("/Audit")
    check("عملية البيع مسجّلة", "بيع" in html)
    check("تسجيل الدخول مسجّل", "تسجيل دخول" in html)

    print("\n=== 15. عزل بيانات المندوب ===")
    # ننشئ فاتورة بحساب المدير ثم نتأكد أن المندوب لا يستطيع رؤيتها
    _, html, _ = admin.get("/Pos")
    atoken = admin.token_from(html)
    status, adata = admin.post_json("/Pos/Checkout", {
        "items": [{"productId": 4, "barcode": "6941238702231", "quantity": 1}],
        "couponCode": None, "customerName": "عميل المدير", "customerPhone": None,
    }, atoken)
    check("المدير يستطيع إنشاء فاتورة", adata.get("success") is True, str(adata)[:150])
    admin_invoice_id = adata.get("invoiceId")

    status, body, _ = agent.get(f"/AgentReports/Details/{admin_invoice_id}")
    check("المندوب لا يستطيع فتح فاتورة غيره (404)", status == 404, f"status={status}")

    status, body, _ = agent.get(f"/Pos/Receipt/{admin_invoice_id}")
    check("المندوب لا يستطيع طباعة إيصال غيره (404)", status == 404, f"status={status}")

    print("\n=== 16. تصدير CSV ===")
    status, body, headers = admin.get("/Reports/ExportCsv?Period=4")
    check("تصدير CSV يعمل", status == 200, f"status={status}")
    check("CSV يحتوي بيانات الفاتورة", invoice_no in body, "invoice not in csv")

    print("\n=== 17. حماية الصفحات من غير المسجلين ===")
    anon = Client()
    for path in ["/Pos", "/Dashboard", "/Products", "/Reports"]:
        status, body, headers = anon.get(path, follow=False)
        loc = headers.get("Location", "") if headers else ""
        check(f"زائر غير مسجّل يُحوَّل من {path} للدخول",
              status in (302, 303) and "Login" in loc, f"status={status} loc={loc}")

    # ==================== النتيجة ====================
    print("\n" + "=" * 60)
    print(f"  الناجحة: {len(PASSED)}   الفاشلة: {len(FAILED)}")
    print("=" * 60)
    if FAILED:
        print("\nالاختبارات الفاشلة:")
        for f in FAILED:
            print("  -", f)
        return 1
    print("\n\033[92mكل الاختبارات نجحت.\033[0m\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

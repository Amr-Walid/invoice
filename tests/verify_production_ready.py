#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
يتحقّق من جاهزية النظام للنشر على الإنتاج — فحص ثابت لا يحتاج SQL Server.

الغرض: التقاط الأخطاء التي لا تظهر إلا على خادم العميل، حيث اكتشافها
يكلّف يوم عمل بدل دقيقة. يُشغَّل قبل كل نشر.

    python3 tests/verify_production_ready.py
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WEB = os.path.join(ROOT, "src", "PosSystem.Web")

failures: list[str] = []
notes: list[str] = []


def read(*parts: str) -> str:
    path = os.path.join(*parts)
    if not os.path.exists(path):
        failures.append(f"ملف مفقود: {os.path.relpath(path, ROOT)}")
        return ""
    with open(path, encoding="utf-8-sig") as handle:
        return handle.read()


def check(label: str, ok: bool, detail: str = "") -> None:
    if ok:
        print(f"  ✔ {label}")
    else:
        print(f"  ✘ {label}")
        failures.append(detail or label)


print("=" * 66)
print("فحص جاهزية الإنتاج")
print("=" * 66)

# ---------------------------------------------------------------
print("\n### ملفات النشر")
schema = read(ROOT, "deploy", "sql", "01_create_schema.sql")
create_db = read(ROOT, "deploy", "sql", "00_create_database.sql")
check("سكربت إنشاء القاعدة موجود", bool(create_db))
check("سكربت المخطط موجود", bool(schema))
check("دليل النشر موجود", bool(read(ROOT, "docs", "DEPLOYMENT.md")))

if schema:
    tables = set(re.findall(r"CREATE TABLE \[(\w+)\]", schema))
    # الجداول التي لا يعمل النظام بدونها فعليًا
    required = {
        "AspNetUsers", "AspNetRoles", "AspNetUserRoles",
        "Products", "Brands", "Categories", "Coupons",
        "Invoices", "InvoiceItems", "Customers",
        "Returns", "ReturnItems",
        "Warehouses", "ProductStocks", "StockMovements",
        "StockTransfers", "StockTransferItems",
        "Suppliers", "PurchaseOrders", "PurchaseOrderItems",
        "PurchaseReceipts", "PurchaseReceiptItems",
        "AuditLogs",
    }
    missing = sorted(required - tables)
    check(f"كل الجداول المطلوبة موجودة ({len(tables)} جدول)",
          not missing,
          f"جداول ناقصة في سكربت المخطط: {missing}")

    # بدون هذا يفشل تطبيق السكربت على قاعدة سبق تحديثها جزئيًا
    check("السكربت قابل لإعادة التشغيل (idempotent)",
          "IF NOT EXISTS" in schema and "__EFMigrationsHistory" in schema,
          "سكربت المخطط ليس idempotent — أعد توليده بـ --idempotent")

    # varchar يفسد العربية إلى علامات استفهام
    bad_varchar = re.findall(r"\[\w+\] varchar\(", schema)
    check("كل الأعمدة النصية Unicode (nvarchar)",
          not bad_varchar,
          f"أعمدة varchar تفسد العربية: {bad_varchar[:5]}")

# ---------------------------------------------------------------
print("\n### إعدادات الإنتاج")
prod = read(WEB, "appsettings.Production.json")
base = read(WEB, "appsettings.json")

check("ملف appsettings.Production.json موجود", bool(prod))
if prod:
    # نبحث عن قيمة فعلية لا عن ذكر الاسم: التعليق التوضيحي داخل الملف
    # يذكر اسم المتغيّر عمدًا لإرشاد من ينشر، وهذا ليس تسريبًا.
    check("لا يحتوي سلسلة اتصال مكتوبة",
          re.search(r'"DefaultConnection"\s*:\s*"[^"]+"', prod) is None,
          "appsettings.Production.json يحوي سلسلة اتصال — انقلها لمتغيّرات البيئة")
    check("لا يحتوي كلمات سر",
          not re.search(r'"(Password|Seed__\w*Password)"\s*:\s*"[^"]+"', prod),
          "appsettings.Production.json يحوي كلمة سر مكتوبة")
    check("البيانات التجريبية معطّلة",
          re.search(r'"DemoData"\s*:\s*false', prod) is not None,
          "Seed:DemoData ليست false في إعدادات الإنتاج")
    check("المزوّد SqlServer",
          re.search(r'"Provider"\s*:\s*"SqlServer"', prod) is not None,
          "مزوّد قاعدة البيانات ليس SqlServer في الإنتاج")

# ---------------------------------------------------------------
print("\n### إحكام الكود")
program = read(WEB, "Program.cs")
seeder = read(WEB, "Data", "DbSeeder.cs")

check("تحويل HTTPS مفعّل خارج التطوير",
      "UseHttpsRedirection" in program,
      "UseHttpsRedirection غير مستدعى — الجلسات تُرسل بلا تشفير")
check("ترويسات العاكس مقروءة",
      "UseForwardedHeaders" in program,
      "UseForwardedHeaders غير مستدعى — حلقة إعادة توجيه خلف Nginx/IIS")
check("نقطة فحص الصحة /health",
      "MapHealthChecks" in program,
      "لا توجد نقطة /health للمراقبة")
check("فشل القاعدة يوقف الإقلاع في الإنتاج",
      re.search(r"IsProduction\(\)\s*\)?\s*\n?\s*throw", program) is not None,
      "التطبيق يكمل رغم فشل تهيئة القاعدة — سيفشل عند أول عملية بيع")

check("كلمات السر الافتراضية مرفوضة في الإنتاج",
      "ResolvePassword" in seeder and "IsProduction" in seeder,
      "لا حراسة على كلمات السر الافتراضية في الإنتاج")
check("البيانات التجريبية مشروطة",
      "Seed:DemoData" in seeder,
      "البيانات التجريبية تُدرج دائمًا — ستظهر في قاعدة العميل")

# ---------------------------------------------------------------
print("\n### تسريب أسرار")
# كلمات السر الافتراضية مسموحة داخل DbSeeder (تُستخدم للتطوير فقط)
# لكن وجودها في ملفات الإعدادات يعني نشرها مع التطبيق.
for name in ("appsettings.json", "appsettings.Production.json"):
    body = read(WEB, name)
    if not body:
        continue
    leaked = [p for p in ("Admin@123", "Agent@123", "Keeper@123") if p in body]
    check(f"{name} بلا كلمات سر افتراضية",
          not leaked,
          f"{name} يحوي {leaked}")

if base:
    # الافتراضي في appsettings.json هو LocalDB — مناسب للتطوير على ويندوز،
    # وغير صالح للإنتاج. ليس خطأً لأن الإنتاج يتجاوزه بمتغيّر البيئة.
    if "(localdb)" in base.lower():
        notes.append(
            "appsettings.json يشير إلى LocalDB (للتطوير). "
            "في الإنتاج تتجاوزه ConnectionStrings__DefaultConnection.")

# ---------------------------------------------------------------
print("\n" + "=" * 66)
for note in notes:
    print(f"ملحوظة: {note}")

if failures:
    print(f"\nنتيجة: فشل — {len(failures)} مشكلة")
    for item in failures:
        print(f"  ✘ {item}")
    sys.exit(1)

print("\nنتيجة: النظام جاهز للنشر على الإنتاج ✔")

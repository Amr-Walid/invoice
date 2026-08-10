#!/usr/bin/env python3
"""يتحقّق أن كل رقم مذكور في docs/DEMO_SCRIPT.md مطابق لقاعدة العرض.

السبب: السكربت يُقرأ أمام مدير الفرع، ورقم واحد لا يطابق الشاشة يُفقد الثقة
في العرض كلّه. هذا الملف يقرأ القاعدة مباشرة ويطبع الحقيقة، فتُصحَّح الوثيقة
من المصدر لا من الذاكرة.

التشغيل:  python3 tests/verify_demo_numbers.py
"""
import sqlite3
import sys
from decimal import Decimal
from pathlib import Path

DB = Path(__file__).resolve().parent.parent / "src" / "PosSystem.Web" / "possystem_dev.db"

TRANSFER_STATUS = {1: "معلّق", 2: "معتمد", 3: "مشحون", 4: "مستلم", 5: "مرفوض", 6: "ملغى"}
PURCHASE_STATUS = {1: "مسوّدة", 2: "معتمد", 3: "مستلم جزئيًا", 4: "مستلم", 5: "ملغى"}
# مطابق لـ Models/Entities/Invoice.cs — الترتيب هنا ليس بديهيًا:
# الإلغاء رقم ٢ لا ٤، والمرتجع بالكامل ٤. الخلط بينهما يقلب قراءة التقارير
# لأن التقارير تستبعد «الملغاة» وحدها وتُبقي المرتجعة مع خصم قيمة الإرجاع.
INVOICE_STATUS = {1: "مكتملة", 2: "ملغاة", 3: "مرتجع جزئي", 4: "مرتجعة بالكامل"}


def dec(v):
    """أعمدة المبالغ مخزّنة نصًّا في SQLite، والمقارنة الرقمية تحتاج تحويلًا."""
    return Decimal(str(v or 0))


def money(v):
    return f"{dec(v):,.2f}"


def main():
    if not DB.exists():
        print(f"✘ قاعدة العرض غير موجودة: {DB}")
        return 1

    con = sqlite3.connect(DB)
    con.row_factory = sqlite3.Row
    q = con.execute
    out = []
    p = out.append

    p("=" * 66)
    p("أرقام قاعدة العرض الفعلية — مصدر الحقيقة لوثيقة السكربت")
    p("=" * 66)

    p("\n### المخازن")
    for r in q("select Code,Name from Warehouses order by Id"):
        p(f"  {r['Code']:<9} {r['Name']}")

    p("\n### أوامر الشراء")
    for r in q(
        "select po.PurchaseNumber n,po.SupplierName s,w.Name wh,po.Status st,po.Total t "
        "from PurchaseOrders po join Warehouses w on w.Id=po.WarehouseId order by po.Id"
    ):
        p(f"  {r['n']} | {r['s']} | {r['wh']} | "
          f"{PURCHASE_STATUS.get(r['st'], r['st'])} | {money(r['t'])}")

    p("\n### بنود الأمر الجزئي (0002)")
    for r in q(
        "select i.ProductName p,i.Quantity q,i.ReceivedQuantity rq,i.UnitCost uc "
        "from PurchaseOrderItems i join PurchaseOrders po on po.Id=i.PurchaseOrderId "
        "where po.PurchaseNumber like '%0002'"
    ):
        p(f"  {r['p']:<26} مطلوب={r['q']:<3} مستلم={r['rq']:<3} "
          f"متبقٍّ={r['q'] - r['rq']:<3} تكلفة={money(r['uc'])}")

    p("\n### بنود التكلفة الأعلى (لإبراز مثال التكلفة)")
    for r in q(
        "select po.PurchaseNumber n,i.ProductName p,i.Quantity q,i.UnitCost uc,i.LineTotal lt "
        "from PurchaseOrderItems i join PurchaseOrders po on po.Id=i.PurchaseOrderId "
        "order by cast(i.LineTotal as real) desc limit 4"
    ):
        p(f"  {r['n']} | {r['p']:<24} ك={r['q']:<3} تكلفة={money(r['uc'])} "
          f"إجمالي={money(r['lt'])}")

    p("\n### أذون الاستلام")
    for r in q("select ReceiptNumber from PurchaseReceipts order by Id"):
        p(f"  {r['ReceiptNumber']}")

    p("\n### التحويلات")
    for r in q(
        "select t.TransferNumber n,f.Name frm,d.Name dst,t.Status st "
        "from StockTransfers t join Warehouses f on f.Id=t.FromWarehouseId "
        "join Warehouses d on d.Id=t.ToWarehouseId order by t.Id"
    ):
        p(f"  {r['n']} | {r['frm']} ← {r['dst']} | {TRANSFER_STATUS.get(r['st'], r['st'])}")

    p("\n### بنود التحويلات (المطلوب / المشحون / المستلم)")
    for r in q(
        "select t.TransferNumber n,i.ProductName p,i.RequestedQuantity rq,"
        "i.ShippedQuantity sq,i.ReceivedQuantity vq "
        "from StockTransferItems i join StockTransfers t on t.Id=i.StockTransferId "
        "order by t.Id"
    ):
        p(f"  {r['n']} | {r['p']:<26} مطلوب={r['rq']:<3} "
          f"مشحون={r['sq']:<3} مستلم={r['vq']}")

    p("\n### الفواتير")
    for r in q(
        "select i.InvoiceNumber n,u.Email ag,w.Name wh,i.CustomerName cn,"
        "i.CouponCode cc,i.Total t,i.Status st from Invoices i "
        "join AspNetUsers u on u.Id=i.AgentId join Warehouses w on w.Id=i.WarehouseId "
        "order by i.Id"
    ):
        p(f"  {r['n']} | {r['ag']:<17} | {r['wh']:<20} | "
          f"{(r['cn'] or '-'):<18} | {(r['cc'] or '-'):<10} | {money(r['t']):>12} | "
          f"{INVOICE_STATUS.get(r['st'], r['st'])}")

    agent_n = q("select count(*) c from Invoices i join AspNetUsers u on u.Id=i.AgentId "
                "where u.Email='agent@pos.local'").fetchone()["c"]
    total_n = q("select count(*) c from Invoices").fetchone()["c"]
    p(f"\n  إجمالي الفواتير = {total_n}   ·   فواتير المندوب = {agent_n}")

    p("\n### المرتجعات")
    for r in q("select ReturnNumber n,InvoiceNumber inv,RefundAmount amt,"
               "RestockedToInventory rs,Reason rsn from Returns order by Id"):
        p(f"  {r['n']} | من {r['inv']} | {money(r['amt'])} | "
          f"أُعيد للمخزون={'نعم' if r['rs'] else 'لا'} | {r['rsn']}")

    p("\n### ملخّص التقارير — كما تحسبه ReportService بالضبط")
    p("  (القاعدة: تُستبعد الفواتير الملغاة وحدها Status=2،")
    p("   والمرتجعة تبقى محسوبة ويُخصم منها RefundedAmount)")
    rows = list(q("select SubTotal,DiscountAmount,Total,RefundedAmount "
                  "from Invoices where Status <> 2"))
    gross = sum(dec(r["SubTotal"]) for r in rows)
    disc = sum(dec(r["DiscountAmount"]) for r in rows)
    net = sum(dec(r["Total"]) for r in rows)
    refunded = sum(dec(r["RefundedAmount"]) for r in rows)
    items_sold = q("select coalesce(sum(it.Quantity),0) c from InvoiceItems it "
                   "join Invoices i on i.Id=it.InvoiceId where i.Status <> 2").fetchone()["c"]
    items_ret = q("select coalesce(sum(it.ReturnedQuantity),0) c from InvoiceItems it "
                  "join Invoices i on i.Id=it.InvoiceId where i.Status <> 2").fetchone()["c"]
    p(f"  عدد الفواتير (بالتقرير) = {len(rows)}")
    p(f"  إجمالي المبيعات GrossSales   = {gross:,.2f}")
    p(f"  الخصومات      TotalDiscount  = {disc:,.2f}")
    p(f"  صافي المبيعات NetSales       = {net:,.2f}")
    p(f"  المرتجعات     TotalRefunded  = {refunded:,.2f}")
    p(f"  الصافي بعد الإرجاع (المعروض) = {net - refunded:,.2f}")
    p(f"  القطع المباعة = {items_sold}   ·   المرتجعة = {items_ret}")

    p("\n### كل الفواتير بلا استثناء (للمراجعة فقط)")
    all_total = sum(dec(r["Total"]) for r in q("select Total from Invoices"))
    ret_amt = sum(dec(r["RefundAmount"]) for r in q("select RefundAmount from Returns"))
    p(f"  مجموع الفواتير كلها = {all_total:,.2f}")
    p(f"  مجموع المرتجعات من جدول Returns = {ret_amt:,.2f}")

    p("\n### أعداد عامة")
    for label, sql in [
        ("المنتجات", "select count(*) c from Products"),
        ("الموردون", "select count(*) c from Suppliers"),
        ("العملاء", "select count(*) c from Customers"),
        ("أرصدة المخازن", "select count(*) c from ProductStocks"),
        ("حركات المخزون", "select count(*) c from StockMovements"),
        ("سجل العمليات", "select count(*) c from AuditLogs"),
    ]:
        p(f"  {label:<16} = {q(sql).fetchone()['c']}")

    p("\n### أرصدة المنتجات (الإجمالي عبر كل المخازن)")
    for r in q(
        "select p.Name n,p.Barcode b,p.StockQuantity sq,p.LowStockThreshold lt,"
        "(select coalesce(sum(s.Quantity),0) from ProductStocks s where s.ProductId=p.Id) tot "
        "from Products p order by p.Id"
    ):
        flag = "  ← منخفض" if r["tot"] <= r["lt"] else ""
        warn = "  ‼ الكاش لا يطابق المجموع" if r["sq"] != r["tot"] else ""
        p(f"  {r['n']:<26} {r['b']} | مجموع={r['tot']:<4} كاش={r['sq']:<4} "
          f"حدّ={r['lt']}{flag}{warn}")

    p("\n### رصيد معرض المعادي (شاشة رصيد المخزن)")
    for r in q(
        "select p.Name n,s.Quantity q from ProductStocks s join Products p on p.Id=s.ProductId "
        "join Warehouses w on w.Id=s.WarehouseId where w.Code='SHOW' order by p.Id"
    ):
        p(f"  {r['n']:<26} {r['q']}")

    text = "\n".join(out)
    print(text)
    Path(__file__).with_name("demo_numbers_actual.txt").write_text(text + "\n", encoding="utf-8")
    print(f"\n(حُفظت النتيجة في tests/demo_numbers_actual.txt)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

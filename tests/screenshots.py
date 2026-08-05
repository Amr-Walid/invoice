#!/usr/bin/env python3
"""يأخذ لقطات شاشة للواجهات الرئيسية للتحقق البصري من التصميم العربي RTL."""
import pathlib
from playwright.sync_api import sync_playwright

BASE = "http://localhost:8080"
OUT = pathlib.Path(__file__).parent / "shots"
OUT.mkdir(exist_ok=True)


def login(page, email, password):
    page.goto(f"{BASE}/Account/Login", wait_until="networkidle")
    page.fill("#Email", email)
    page.fill("#Password", password)
    page.click("button[type=submit]")
    page.wait_for_load_state("networkidle")


def shot(page, name):
    page.screenshot(path=str(OUT / f"{name}.png"), full_page=True)
    print("saved", name)


with sync_playwright() as p:
    b = p.chromium.launch()

    # 1) صفحة الدخول
    pg = b.new_page(viewport={"width": 1280, "height": 860})
    pg.goto(f"{BASE}/Account/Login", wait_until="networkidle")
    shot(pg, "01_login")

    # 2) صفحة المندوب — المربعان الكبيران
    login(pg, "agent@pos.local", "Agent@123")
    shot(pg, "02_agent_home")

    # 3) نافذة نقطة البيع مع منتجات في السلة
    pg.click("#btnNewInvoice")
    pg.wait_for_timeout(700)
    for code in ["6941238701234", "6941238703221"]:
        pg.fill("#barcodeInput", code)
        pg.press("#barcodeInput", "Enter")
        pg.wait_for_timeout(900)
    pg.fill("#customerName", "أحمد محمد")
    pg.fill("#couponInput", "SAVE20")
    pg.click("#btnApplyCoupon")
    pg.wait_for_timeout(1200)
    shot(pg, "03_pos_modal")

    # 4) تقارير المندوب
    pg.goto(f"{BASE}/AgentReports", wait_until="networkidle")
    shot(pg, "04_agent_reports")
    pg.close()

    # 5) لوحة تحكم المدير
    pg = b.new_page(viewport={"width": 1280, "height": 1000})
    login(pg, "admin@pos.local", "Admin@123")
    shot(pg, "05_admin_dashboard")

    for path, name in [
        ("/Reports", "06_admin_reports"),
        ("/Products", "07_products"),
        ("/Brands", "08_brands"),
        ("/Coupons", "09_coupons"),
        ("/Audit", "10_audit"),
    ]:
        pg.goto(BASE + path, wait_until="networkidle")
        shot(pg, name)

    # 11) تفاصيل فاتورة + الإيصال الحراري
    pg.goto(f"{BASE}/Reports", wait_until="networkidle")
    link = pg.query_selector("table tbody tr a")
    if link:
        link.click()
        pg.wait_for_load_state("networkidle")
        shot(pg, "11_invoice_details")

    pg2 = b.new_page(viewport={"width": 420, "height": 900})
    login(pg2, "admin@pos.local", "Admin@123")
    pg2.goto(f"{BASE}/Pos/Receipt/1", wait_until="networkidle")
    shot(pg2, "12_receipt")

    b.close()
print("done")

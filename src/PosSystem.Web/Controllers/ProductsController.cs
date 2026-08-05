using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;
using PosSystem.Web.Services;

namespace PosSystem.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class ProductsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IBarcodeService _barcode;
    private readonly IInventoryService _inventory;
    private readonly IWarehouseService _warehouses;

    public ProductsController(AppDbContext db, IAuditService audit, IBarcodeService barcode,
        IInventoryService inventory, IWarehouseService warehouses)
    {
        _db = db;
        _audit = audit;
        _barcode = barcode;
        _inventory = inventory;
        _warehouses = warehouses;
    }

    public async Task<IActionResult> Index(string? search, int? brandId, int? categoryId, bool lowStock = false)
    {
        var query = _db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Barcode.Contains(term));
        }

        if (brandId.HasValue) query = query.Where(p => p.BrandId == brandId.Value);
        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);
        if (lowStock) query = query.Where(p => p.StockQuantity <= p.LowStockThreshold);

        ViewBag.Search = search;
        ViewBag.BrandId = brandId;
        ViewBag.CategoryId = categoryId;
        ViewBag.LowStock = lowStock;
        await LoadListsAsync(brandId, categoryId);

        var products = await query.OrderBy(p => p.Name).ToListAsync();
        return View(products);
    }

    public async Task<IActionResult> Create()
    {
        await LoadListsAsync(null, null);
        return View(new Product { LowStockThreshold = 5 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product model)
    {
        await ValidateProductAsync(model, null);

        if (!ModelState.IsValid)
        {
            await LoadListsAsync(model.BrandId, model.CategoryId);
            return View(model);
        }

        // المخزن الذي يُودَع فيه الرصيد الافتتاحي: ما اختاره المدير،
        // وإلا الافتراضي. لا يجوز إنشاء منتج برصيد معلّق بلا مخزن.
        var openingWarehouseId = model.StockQuantity > 0
            ? (WarehouseIdFromForm() ?? (await _warehouses.GetDefaultAsync())?.Id)
            : null;

        if (model.StockQuantity > 0 && openingWarehouseId is null)
        {
            ModelState.AddModelError(string.Empty,
                "لا يوجد مخزن نشط لإيداع الرصيد الافتتاحي — أنشِئ مخزنًا أولًا");
            await LoadListsAsync(model.BrandId, model.CategoryId);
            return View(model);
        }

        model.Barcode = model.Barcode.Trim();
        model.CreatedAt = DateTime.Now;

        // الرصيد يُودَع عبر IInventoryService لا من النموذج مباشرة،
        // وإلا وُلِد المنتج بكاش يقول 20 ومخازن فارغة تمامًا — رقم يمنع
        // البيع رغم أن القائمة تقول إن البضاعة موجودة.
        var openingQuantity = model.StockQuantity;
        model.StockQuantity = 0;

        _db.Products.Add(model);
        await _db.SaveChangesAsync();

        if (openingQuantity > 0 && openingWarehouseId is int depotId)
        {
            var result = await _inventory.ApplyAsync(new StockChangeRequest
            {
                ProductId = model.Id,
                WarehouseId = depotId,
                Change = openingQuantity,
                Reason = StockMovementReason.InitialStock,
                Note = "رصيد افتتاحي عند إنشاء المنتج"
            });

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message);
                await LoadListsAsync(model.BrandId, model.CategoryId);
                return View(model);
            }
        }

        _audit.Track(AuditActions.Create, nameof(Product), model.Id.ToString(),
            $"منتج جديد: {model.Name} — باركود {model.Barcode} — سعر {model.Price:0.00}");
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم إضافة المنتج «{model.Name}» بنجاح";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>معرّف المخزن المرسل من النموذج (اختياري)</summary>
    private int? WarehouseIdFromForm() =>
        int.TryParse(Request.Form["OpeningWarehouseId"], out var id) && id > 0 ? id : null;

    public async Task<IActionResult> Edit(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();

        await LoadListsAsync(product.BrandId, product.CategoryId);
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product model)
    {
        if (id != model.Id) return BadRequest();

        await ValidateProductAsync(model, id);

        if (!ModelState.IsValid)
        {
            await LoadListsAsync(model.BrandId, model.CategoryId);
            return View(model);
        }

        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();

        var oldStock = product.StockQuantity;

        product.Barcode = model.Barcode.Trim();
        product.Name = model.Name;
        product.Price = model.Price;
        product.LowStockThreshold = model.LowStockThreshold;
        product.BrandId = model.BrandId;
        product.CategoryId = model.CategoryId;
        product.IsActive = model.IsActive;
        product.UpdatedAt = DateTime.Now;

        // ===== تعديل المخزون يدويًا =====
        // الرقم المعروض مجموع كل المخازن، والفرق لا يُمكن توزيعه على
        // المخازن توزيعًا معقولًا من حقل واحد: فرق قدره −٥ من أي مخزن
        // يُخصم؟ لذلك نمنع التعديل من هنا ونُوجّه للأداة المختصة، بدل
        // أن نخمّن مخزنًا ونُفسد جرده بلا أن يعرف المدير.
        if (oldStock != model.StockQuantity)
        {
            ModelState.AddModelError(nameof(Product.StockQuantity),
                "لا يُعدّل المخزون من هنا بعد تعدد المخازن، لأن الرقم المعروض " +
                "مجموع كل المخازن. استخدم شاشة مخزون المخزن لتعديل رصيد مخزن محدّد");

            await LoadListsAsync(model.BrandId, model.CategoryId);
            return View(model);
        }

        _audit.Track(AuditActions.Update, nameof(Product), id.ToString(),
            $"تعديل منتج: {product.Name} — السعر {product.Price:0.00}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم تحديث المنتج بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products
            .Include(p => p.InvoiceItems)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null) return NotFound();

        // منتج له مبيعات لا يُحذف نهائيًا — يُعطَّل فقط (soft delete)
        if (product.InvoiceItems.Any())
        {
            product.IsActive = false;
            product.UpdatedAt = DateTime.Now;
            _audit.Track(AuditActions.Update, nameof(Product), id.ToString(),
                $"تعطيل منتج (له مبيعات سابقة): {product.Name}");
            await _db.SaveChangesAsync();

            TempData["Success"] = $"تم تعطيل «{product.Name}» لأن له مبيعات مسجلة (لا يمكن حذفه نهائيًا للحفاظ على التقارير)";
            return RedirectToAction(nameof(Index));
        }

        _db.Products.Remove(product);
        _audit.Track(AuditActions.Delete, nameof(Product), id.ToString(), $"حذف منتج: {product.Name}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم حذف المنتج بنجاح";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// صفحة ملصقات الباركود الجاهزة للطباعة لمنتج واحد.
    /// <paramref name="copies"/> يسمح بطباعة عدة ملصقات في ورقة واحدة
    /// (الحالة الواقعية: وصلت دفعة من نفس المنتج ونريد ملصقًا لكل قطعة).
    /// </summary>
    public async Task<IActionResult> Barcode(int id, int copies = 1)
    {
        var product = await _db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null) return NotFound();

        // نحمي الطابعة من طلب عبثي بألف ملصق: الحد 60 ملصقًا (حوالي ورقتي A4)
        copies = Math.Clamp(copies, 1, 60);

        if (!_barcode.CanEncode(product.Barcode))
        {
            TempData["Error"] = $"باركود المنتج «{product.Name}» يحتوي محارف لا يدعمها CODE-128 ولا يمكن طباعته";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Copies = copies;
        // نمرر الـ SVG مرة واحدة ونكرره في العرض: توليده لكل نسخة هدر بلا معنى
        ViewBag.BarcodeSvg = _barcode.ToSvg(product.Barcode);

        _audit.Track(AuditActions.PrintBarcode, nameof(Product), product.Id.ToString(),
            $"ملصق باركود: {product.Name} — {product.Barcode} — عدد النسخ {copies}");
        await _db.SaveChangesAsync();

        return View(product);
    }

    private async Task ValidateProductAsync(Product model, int? excludeId)
    {
        var barcode = (model.Barcode ?? string.Empty).Trim();

        if (!string.IsNullOrEmpty(barcode) &&
            await _db.Products.AnyAsync(p => p.Barcode == barcode && (!excludeId.HasValue || p.Id != excludeId.Value)))
        {
            ModelState.AddModelError(nameof(model.Barcode), "هذا الباركود مستخدم بالفعل لمنتج آخر");
        }

        // التصنيف يجب أن يكون تابعًا للبراند المختار
        if (model.BrandId > 0 && model.CategoryId > 0)
        {
            var belongs = await _db.Categories
                .AnyAsync(c => c.Id == model.CategoryId && c.BrandId == model.BrandId);
            if (!belongs)
                ModelState.AddModelError(nameof(model.CategoryId), "التصنيف المختار لا يتبع البراند المختار");
        }
    }

    private async Task LoadListsAsync(int? brandId, int? categoryId)
    {
        var brands = await _db.Brands.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();

        var categoriesQuery = _db.Categories.AsNoTracking().Where(c => c.IsActive);
        if (brandId.HasValue)
            categoriesQuery = categoriesQuery.Where(c => c.BrandId == brandId.Value);

        var categories = await categoriesQuery.OrderBy(c => c.Name).ToListAsync();

        ViewBag.Brands = new SelectList(brands, "Id", "Name", brandId);
        ViewBag.Categories = new SelectList(categories, "Id", "Name", categoryId);

        // المخازن النشطة فقط — لا يُودَع رصيد افتتاحي في مخزن معطَّل.
        // الافتراضي مُختار مسبقًا ليكون المسار الأسرع هو الصحيح.
        var warehouses = await _warehouses.ListActiveAsync();
        ViewBag.Warehouses = new SelectList(
            warehouses.Select(w => new { w.Id, Label = w.DisplayName }),
            "Id", "Label", warehouses.FirstOrDefault(w => w.IsDefault)?.Id);
    }
}

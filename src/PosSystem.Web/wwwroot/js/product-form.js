/* ============================================================
   نموذج المنتج — تحديث قائمة التصنيفات حسب البراند المختار
   
   التحقق النهائي من أن التصنيف يتبع البراند يحدث على السيرفر
   في ProductsController.ValidateProductAsync — هذا للراحة فقط.
   ============================================================ */
(function () {
    'use strict';

    var brand = document.getElementById('brandSelect');
    var category = document.getElementById('categorySelect');
    if (!brand || !category) return;

    brand.addEventListener('change', function () {
        var id = brand.value;
        category.innerHTML = '<option value="">— اختر التصنيف —</option>';
        if (!id) return;

        fetch('/Categories/ByBrand?brandId=' + encodeURIComponent(id))
            .then(function (r) { return r.json(); })
            .then(function (list) {
                list.forEach(function (c) {
                    var opt = document.createElement('option');
                    opt.value = c.id;
                    opt.textContent = c.name;
                    category.appendChild(opt);
                });
            })
            .catch(function () {
                /* في حال فشل الطلب تبقى القائمة فارغة، والتحقق يحدث على السيرفر */
            });
    });
})();

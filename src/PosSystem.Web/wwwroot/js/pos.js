/* ============================================================
   نقطة البيع — منطق الواجهة
   
   ملاحظة مهمة: كل الأسعار المعروضة هنا للعرض الفوري فقط.
   السيرفر يعيد حساب كل شيء من قاعدة البيانات عند الحفظ،
   لذا لا يمكن التلاعب بالأسعار من المتصفح.
   ============================================================ */
(function () {
    'use strict';

    // ==================== الحالة ====================
    var cart = [];                 // [{ productId, barcode, name, price, quantity, stock }]
    var coupon = null;             // { code, pct }
    var isSubmitting = false;

    // ==================== عناصر DOM ====================
    var posModalEl = document.getElementById('posModal');
    if (!posModalEl) return;

    var posModal = new bootstrap.Modal(posModalEl);
    var successModal = new bootstrap.Modal(document.getElementById('successModal'));

    var barcodeInput = document.getElementById('barcodeInput');
    var scanFeedback = document.getElementById('scanFeedback');
    var cartBody = document.getElementById('cartBody');
    var couponInput = document.getElementById('couponInput');
    var couponFeedback = document.getElementById('couponFeedback');
    var btnCheckout = document.getElementById('btnCheckout');

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function money(n) {
        return (Math.round(n * 100) / 100).toFixed(2);
    }

    function esc(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    // ==================== تغذية راجعة ====================
    function feedback(el, message, type) {
        var icons = { ok: 'check-circle-fill', err: 'exclamation-triangle-fill', info: 'info-circle-fill' };
        var colors = { ok: 'success', err: 'danger', info: 'secondary' };
        el.innerHTML = '<span class="text-' + colors[type] + ' fw-semibold">' +
            '<i class="bi bi-' + icons[type] + '"></i> ' + esc(message) + '</span>';
    }

    /* نغمة قصيرة للتأكيد الصوتي — مفيدة جدًا للكاشير السريع */
    function beep(ok) {
        try {
            var Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            var ctx = new Ctx();
            var osc = ctx.createOscillator();
            var gain = ctx.createGain();
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.frequency.value = ok ? 1100 : 320;
            osc.type = 'sine';
            gain.gain.setValueAtTime(0.06, ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.14);
            osc.start();
            osc.stop(ctx.currentTime + 0.15);
            setTimeout(function () { ctx.close(); }, 300);
        } catch (e) { /* الصوت ميزة إضافية، لا نُفشل العملية بسببه */ }
    }

    /* نُركّز الحقل مع وضع المؤشر في النهاية (بدون تحديد النص).
       مهم جدًا: select() يجعل الحرف التالي من الماسح يستبدل الكود بالكامل. */
    function focusBarcode() {
        if (!barcodeInput) return;
        barcodeInput.focus();
        try {
            var end = barcodeInput.value.length;
            barcodeInput.setSelectionRange(end, end);
        } catch (e) { /* بعض المتصفحات لا تدعمها على أنواع معيّنة */ }
    }

    // ==================== إدارة السلة ====================
    function findLine(productId) {
        for (var i = 0; i < cart.length; i++) {
            if (cart[i].productId === productId) return cart[i];
        }
        return null;
    }

    function addProduct(p) {
        var line = findLine(p.id);

        if (line) {
            // المنتج موجود بالسلة: نزيد الكمية بدل تكرار السطر
            if (line.quantity + 1 > line.stock) {
                feedback(scanFeedback, 'الكمية المتاحة من «' + p.name + '» هي ' + line.stock + ' فقط', 'err');
                beep(false);
                return;
            }
            line.quantity += 1;
            feedback(scanFeedback, 'تم زيادة كمية «' + p.name + '» إلى ' + line.quantity, 'ok');
        } else {
            cart.push({
                productId: p.id,
                barcode: p.barcode,
                name: p.name,
                price: p.price,
                quantity: 1,
                stock: p.stockQuantity
            });
            feedback(scanFeedback, 'تم إضافة «' + p.name + '»', 'ok');
        }

        beep(true);
        render(p.id);
    }

    function setQuantity(productId, qty) {
        var line = findLine(productId);
        if (!line) return;

        qty = parseInt(qty, 10);
        if (isNaN(qty) || qty < 1) qty = 1;

        if (qty > line.stock) {
            qty = line.stock;
            feedback(scanFeedback, 'الحد الأقصى المتاح من «' + line.name + '» هو ' + line.stock, 'err');
            beep(false);
        }

        line.quantity = qty;
        render();
    }

    function removeLine(productId) {
        cart = cart.filter(function (l) { return l.productId !== productId; });
        render();
        focusBarcode();
    }

    function clearCart() {
        cart = [];
        coupon = null;
        couponInput.value = '';
        couponFeedback.innerHTML = '';
        scanFeedback.innerHTML = '';
        document.getElementById('customerName').value = '';
        document.getElementById('customerPhone').value = '';
        render();
        focusBarcode();
    }

    // ==================== العرض ====================
    function render(flashProductId) {
        if (cart.length === 0) {
            cartBody.innerHTML =
                '<tr id="emptyCartRow"><td colspan="6" class="empty-state">' +
                '<i class="bi bi-cart-x"></i>السلة فارغة — ابدأ بمسح باركود المنتج</td></tr>';
        } else {
            var html = '';
            cart.forEach(function (l, idx) {
                var lineTotal = l.price * l.quantity;
                html +=
                    '<tr data-id="' + l.productId + '"' +
                    (flashProductId === l.productId ? ' class="flash-row"' : '') + '>' +
                    '<td class="num text-muted">' + (idx + 1) + '</td>' +
                    '<td><div class="fw-semibold">' + esc(l.name) + '</div>' +
                    '<small class="text-muted num barcode-text">' + esc(l.barcode) + '</small></td>' +
                    '<td class="num">' + money(l.price) + '</td>' +
                    '<td class="text-center">' +
                    '<div class="qty-group">' +
                    '<button type="button" class="btn btn-outline-secondary btn-sm js-dec">−</button>' +
                    '<input type="number" class="form-control form-control-sm js-qty" value="' + l.quantity +
                    '" min="1" max="' + l.stock + '" />' +
                    '<button type="button" class="btn btn-outline-secondary btn-sm js-inc">+</button>' +
                    '</div></td>' +
                    '<td class="num fw-bold">' + money(lineTotal) + '</td>' +
                    '<td><button type="button" class="btn btn-sm btn-outline-danger js-del" title="حذف">' +
                    '<i class="bi bi-x-lg"></i></button></td>' +
                    '</tr>';
            });
            cartBody.innerHTML = html;
        }

        renderTotals();
    }

    function renderTotals() {
        var subTotal = 0, qty = 0;
        cart.forEach(function (l) {
            subTotal += l.price * l.quantity;
            qty += l.quantity;
        });

        var pct = coupon ? coupon.pct : 0;
        var discount = Math.round(subTotal * pct) / 100;
        // تقريب مطابق لمنطق السيرفر
        discount = Math.round(subTotal * pct / 100 * 100) / 100;
        var total = subTotal - discount;

        document.getElementById('sumCount').textContent = cart.length;
        document.getElementById('sumQty').textContent = qty;
        document.getElementById('sumSubTotal').textContent = money(subTotal);
        document.getElementById('sumTotal').textContent = money(total);

        var row = document.getElementById('discountRow');
        if (coupon) {
            row.style.display = 'flex';
            document.getElementById('sumPct').textContent = pct;
            document.getElementById('sumDiscount').textContent = money(discount);
        } else {
            row.style.display = 'none';
        }

        btnCheckout.disabled = cart.length === 0 || isSubmitting;
    }

    // ==================== البحث بالباركود ====================

    /* ------------------------------------------------------------
       مسار وحيد لتسليم الباركود.
       كل المصادر (Enter في الحقل، زر «إضافة»، الماسح الضوئي،
       اللصق، الإنهاء التلقائي) تنادي هذه الدالة فقط — وهي تحمي من
       المسح المزدوج: أي كود مطابق يصل خلال 250ms يُهمَل، لأن ذلك
       يعني أن نفس المسحة وصلت من أكثر من مصدر (وليس مسحة جديدة).
       ------------------------------------------------------------ */
    var lastSubmittedCode = '';
    var lastSubmittedAt = 0;
    var DUPLICATE_WINDOW = 250; // ms

    function submitBarcode(code) {
        cancelAutoSubmit();

        code = (code || '').trim();
        if (!code) {
            feedback(scanFeedback, 'من فضلك امسح الباركود أو أدخله يدويًا', 'info');
            focusBarcode();
            return;
        }

        var now = Date.now();
        if (code === lastSubmittedCode && (now - lastSubmittedAt) < DUPLICATE_WINDOW) {
            // نفس المسحة وصلت مرتين — نُفرّغ الحقل فقط دون تكرار الإضافة
            barcodeInput.value = '';
            focusBarcode();
            return;
        }

        lastSubmittedCode = code;
        lastSubmittedAt = now;

        // نُفرّغ الحقل فورًا حتى يكون جاهزًا للمسحة التالية بدون انتظار الشبكة
        barcodeInput.value = '';
        lastFieldLength = 0;
        resetCodeTiming();
        pendingLeak = null;
        focusBarcode();

        lookup(code);
    }

    function lookup(code) {
        if (!code) return;

        fetch('/Pos/Lookup?barcode=' + encodeURIComponent(code), {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.found) {
                    addProduct(data);
                } else {
                    feedback(scanFeedback, data.message || 'لم يتم العثور على المنتج', 'err');
                    beep(false);
                }
                focusBarcode();
            })
            .catch(function () {
                feedback(scanFeedback, 'تعذر الاتصال بالسيرفر، حاول مرة أخرى', 'err');
                beep(false);
                // نسمح بإعادة محاولة نفس الكود مباشرة بعد فشل الاتصال
                lastSubmittedCode = '';
                focusBarcode();
            });
    }

    // ==================== الكوبون ====================
    function applyCoupon() {
        var code = (couponInput.value || '').trim();
        if (!code) {
            coupon = null;
            couponFeedback.innerHTML = '';
            renderTotals();
            return;
        }

        fetch('/Pos/ValidateCoupon', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token()
            },
            body: JSON.stringify({ code: code })
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.isValid) {
                    coupon = { code: data.code, pct: data.discountPercentage };
                    feedback(couponFeedback, data.message, 'ok');
                    beep(true);
                } else {
                    coupon = null;
                    feedback(couponFeedback, data.message || 'كود غير صالح', 'err');
                    beep(false);
                }
                renderTotals();
            })
            .catch(function () {
                feedback(couponFeedback, 'تعذر التحقق من الكود', 'err');
            });
    }

    // ==================== إتمام الفاتورة ====================
    function checkout() {
        if (cart.length === 0 || isSubmitting) return;

        isSubmitting = true;
        btnCheckout.disabled = true;
        btnCheckout.innerHTML = '<span class="spinner-border spinner-border-sm"></span> جاري الحفظ...';

        var payload = {
            items: cart.map(function (l) {
                // نرسل المنتج والكمية فقط — الأسعار من DB على السيرفر
                return { productId: l.productId, barcode: l.barcode, quantity: l.quantity };
            }),
            couponCode: coupon ? coupon.code : null,
            customerName: document.getElementById('customerName').value || null,
            customerPhone: document.getElementById('customerPhone').value || null
        };

        fetch('/Pos/Checkout', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token()
            },
            body: JSON.stringify(payload)
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.success) {
                    document.getElementById('okInvoiceNumber').textContent = data.invoiceNumber;
                    document.getElementById('okTotal').textContent = money(data.total);
                    document.getElementById('okPrintLink').href = '/Pos/Receipt/' + data.invoiceId;

                    posModal.hide();
                    successModal.show();
                    clearCart();
                    beep(true);
                } else {
                    feedback(scanFeedback, data.message || 'تعذر حفظ الفاتورة', 'err');
                    beep(false);
                }
            })
            .catch(function () {
                feedback(scanFeedback, 'خطأ في الاتصال، لم يتم حفظ الفاتورة', 'err');
                beep(false);
            })
            .finally(function () {
                isSubmitting = false;
                btnCheckout.innerHTML =
                    '<i class="bi bi-check2-circle"></i> إتمام وحفظ الفاتورة' +
                    '<small class="d-block" style="font-size:.72rem;opacity:.85">F9</small>';
                renderTotals();
            });
    }

    /* ============================================================
       حالة مستمع الباركود السريع
       ------------------------------------------------------------
       قارئ الباركود يعمل كلوحة مفاتيح: يُرسل الأحرف بسرعة تتجاوز
       قدرة الإنسان ثم (عادةً) Enter. نستخدم فارق الزمن بين الضغطات
       للتمييز بين الماسح والكتابة اليدوية.
       ============================================================ */
    var SCANNER_MAX_GAP = 60;        // ms — فاصل يدل على دفعة ماسح (لتحويل الضغطات للحقل)
    var SCANNER_AVG_GAP = 55;        // ms — متوسط الفاصل الذي يُميّز الماسح عن الإنسان
    var MIN_CODE_LENGTH = 4;         // أقصر كود مقبول عند التسليم الصريح
    var AUTO_SUBMIT_MIN_LENGTH = 8;  // أقصر كود يُقبل للإنهاء التلقائي
    var AUTO_SUBMIT_IDLE = 400;      // ms — سكون بعده نُسلّم الكود تلقائيًا

    var lastFieldLength = 0;
    var autoSubmitTimer = null;

    /* ------------------------------------------------------------
       تصنيف مصدر الإدخال: ماسح ضوئي أم إنسان؟

       نعتمد على **متوسط** الفاصل الزمني بين أحرف الكود الحالي، وليس
       على أكبر فاصل. السبب: أي تأخّر لحظي من نظام التشغيل قد يُنتج
       فاصلًا كبيرًا واحدًا وسط دفعة الماسح، فلو اعتمدنا على الحد
       الأقصى لصنّفنا الماسح خطأً كإنسان وتوقّف الإنهاء التلقائي.
       المتوسط يمتصّ هذه التأخّرات العارضة.
       ------------------------------------------------------------ */
    var codeStartAt = 0;   // زمن أول حرف في الكود الحالي
    var codeLastAt = 0;    // زمن آخر حرف
    var codeChars = 0;     // عدد الأحرف المتراكمة

    function resetCodeTiming() {
        codeStartAt = 0;
        codeLastAt = 0;
        codeChars = 0;
    }

    /* يُسجّل توقيت كل حرف يدخل ضمن الكود الحالي */
    function noteCodeChar() {
        var now = Date.now();
        if (codeChars === 0 || (now - codeLastAt) > 500) {
            codeStartAt = now;
            codeChars = 1;
        } else {
            codeChars++;
        }
        codeLastAt = now;
    }

    function looksLikeScanner() {
        if (codeChars < MIN_CODE_LENGTH) return false;
        var avg = (codeLastAt - codeStartAt) / (codeChars - 1);
        return avg <= SCANNER_AVG_GAP;
    }

    /* الحرف الأول من دفعة الماسح لا يمكن تمييزه لحظيًا (الفاصل الزمني
       يُقاس مقابل ضغطة بشرية سابقة)، فقد يتسرّب إلى حقل آخر. نحفظه هنا
       لنستعيده رجعيًا بمجرد أن يُثبت الحرف التالي أن المصدر ماسح ضوئي. */
    var pendingLeak = null;   // { el, valueBefore, ch }

    function rememberLeak(el, ch) {
        if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA')) {
            pendingLeak = { el: el, valueBefore: el.value, ch: ch };
        } else {
            pendingLeak = null;
        }
    }

    /* يُلغي الحرف المتسرّب من الحقل الآخر ويُعيده كبادئة للباركود */
    function reclaimLeak(active) {
        if (!pendingLeak || pendingLeak.el !== active) {
            pendingLeak = null;
            return '';
        }
        var ch = pendingLeak.ch;
        try { pendingLeak.el.value = pendingLeak.valueBefore; } catch (e) { /* تجاهل */ }
        pendingLeak = null;
        return ch;
    }

    function cancelAutoSubmit() {
        if (autoSubmitTimer) {
            clearTimeout(autoSubmitTimer);
            autoSubmitTimer = null;
        }
    }

    /* خطة بديلة للماسحات التي لا تُرسل Enter: بعد سكون قصير نُسلّم الكود.
       يُعاد جدولة المؤقّت مع كل حرف، فلا يُسلَّم إلا بعد توقف الإدخال فعلًا. */
    function scheduleAutoSubmit(delay, minLength) {
        cancelAutoSubmit();
        var floor = minLength || AUTO_SUBMIT_MIN_LENGTH;
        autoSubmitTimer = setTimeout(function () {
            autoSubmitTimer = null;
            // إدخال بشري: ننتظر Enter أو زر «إضافة» حتى لا نُسلّم كودًا ناقصًا
            if (!looksLikeScanner()) return;
            var code = barcodeInput.value.trim();
            if (code.length >= floor) submitBarcode(code);
        }, delay);
    }

    // ==================== ربط الأحداث ====================
    var btnNew = document.getElementById('btnNewInvoice');
    if (btnNew) {
        btnNew.addEventListener('click', function () { posModal.show(); });
    }

    posModalEl.addEventListener('shown.bs.modal', focusBarcode);

    /* Enter داخل الحقل = تسليم الكود (المصدر الأساسي للماسح والإدخال اليدوي).
       نستخدم keydown ونمنع الافتراضي حتى لا يُرسل أي form محتمل. */
    barcodeInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.keyCode === 13) {
            e.preventDefault();
            e.stopPropagation();
            submitBarcode(barcodeInput.value);
        }
    });

    /* بعض الماسحات لا ترسل Enter إطلاقًا (أو تُرسل Tab).
       نتعامل مع Tab كتسليم، ونستخدم مؤقتًا للإنهاء التلقائي كخطة بديلة. */
    barcodeInput.addEventListener('keydown', function (e) {
        if (e.key === 'Tab' && barcodeInput.value.trim().length >= MIN_CODE_LENGTH) {
            e.preventDefault();
            submitBarcode(barcodeInput.value);
        }
    });

    /* الإدخال في الحقل: نجدول إنهاءً تلقائيًا للماسحات التي لا ترسل Enter.
       الكتابة البشرية لا تُفعّله لأن الشرط يعتمد على سرعة الإدخال. */
    barcodeInput.addEventListener('input', function () {
        var raw = barcodeInput.value;
        // بعض الماسحات تُدخل الكود كاملًا في حدث واحد (لصق / HID سريع)
        var jumped = raw.length - lastFieldLength >= 4;
        lastFieldLength = raw.length;

        if (raw.trim().length === 0) {
            cancelAutoSubmit();
            return;
        }

        if (jumped && raw.trim().length >= MIN_CODE_LENGTH) {
            submitBarcode(raw);
            return;
        }

        if (raw.trim().length >= AUTO_SUBMIT_MIN_LENGTH) {
            scheduleAutoSubmit(AUTO_SUBMIT_IDLE);
        }
    });

    barcodeInput.addEventListener('paste', function () {
        setTimeout(function () {
            if (barcodeInput.value.trim().length >= MIN_CODE_LENGTH) submitBarcode(barcodeInput.value);
        }, 30);
    });

    document.getElementById('btnAddBarcode').addEventListener('click', function () {
        submitBarcode(barcodeInput.value);
    });

    document.getElementById('btnApplyCoupon').addEventListener('click', applyCoupon);

    couponInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            applyCoupon();
        }
    });

    btnCheckout.addEventListener('click', checkout);
    document.getElementById('btnClearCart').addEventListener('click', clearCart);

    document.getElementById('okNewInvoice').addEventListener('click', function () {
        successModal.hide();
        setTimeout(function () { posModal.show(); }, 300);
    });

    // تفويض أحداث السلة (الصفوف تُبنى ديناميكيًا)
    cartBody.addEventListener('click', function (e) {
        var tr = e.target.closest('tr[data-id]');
        if (!tr) return;
        var id = parseInt(tr.getAttribute('data-id'), 10);
        var line = findLine(id);
        if (!line) return;

        if (e.target.closest('.js-inc')) setQuantity(id, line.quantity + 1);
        else if (e.target.closest('.js-dec')) setQuantity(id, line.quantity - 1);
        else if (e.target.closest('.js-del')) removeLine(id);
    });

    cartBody.addEventListener('change', function (e) {
        if (!e.target.classList.contains('js-qty')) return;
        var tr = e.target.closest('tr[data-id]');
        if (tr) setQuantity(parseInt(tr.getAttribute('data-id'), 10), e.target.value);
    });

    /* ============================================================
       مستمع الباركود السريع (Barcode Keyboard Listener)
       ------------------------------------------------------------
       الهدف: أن يعمل الماسح الضوئي دون الحاجة للنقر على الحقل.

       المبدأ الحاسم لتجنّب المسح المزدوج: هذا المستمع لا ينادي
       submitBarcode أبدًا عندما يكون حقل الباركود مُركّزًا — في هذه
       الحالة يتولّى مستمع الحقل نفسه المهمة. مهمته هنا تنحصر في
       "تحويل" ضغطات الماسح إلى الحقل عندما يكون التركيز في مكان آخر.
       ============================================================ */
    var lastKeyTime = 0;

    document.addEventListener('keydown', function (e) {
        // ---------- اختصارات عامة ----------
        if (e.key === 'F2') {
            e.preventDefault();
            posModal.show();
            return;
        }

        var modalOpen = posModalEl.classList.contains('show');

        // F3 = شاشة المرتجع. لا يعمل والنافذة مفتوحة حتى لا تُفقَد سلة قائمة
        // بالانتقال إلى صفحة أخرى بضغطة واحدة.
        if (e.key === 'F3' && !modalOpen) {
            var returnsTile = document.querySelector('.tile-returns');
            if (returnsTile) {
                e.preventDefault();
                window.location.href = returnsTile.getAttribute('href');
            }
            return;
        }

        if (e.key === 'F9' && modalOpen) {
            e.preventDefault();
            checkout();
            return;
        }

        if (!modalOpen) return;

        var now = Date.now();
        var gap = now - lastKeyTime;
        lastKeyTime = now;

        var active = document.activeElement;

        // الحقل مُركّز: مستمع الحقل هو المسؤول عن التسليم — لا نُسلّم هنا
        // (منع التكرار)، لكن نُسجّل التوقيت لتصنيف المصدر.
        if (active === barcodeInput) {
            if (e.key.length === 1) noteCodeChar();
            pendingLeak = null;
            return;
        }

        // المستخدم يكتب في حقل آخر (العميل / الكوبون / الكمية)
        var inOtherField = active &&
            (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.tagName === 'SELECT');

        // دفعة سريعة جدًا = ماسح ضوئي؛ نحوّلها للحقل حتى لو كان التركيز في حقل آخر
        var burst = e.key.length === 1 && gap < SCANNER_MAX_GAP;

        if (e.key === 'Enter' || e.keyCode === 13) {
            pendingLeak = null;
            // Enter قادم من الماسح بعد تحويل الأحرف إلى الحقل
            if (!inOtherField && barcodeInput.value.trim().length >= MIN_CODE_LENGTH) {
                e.preventDefault();
                submitBarcode(barcodeInput.value);
            }
            return;
        }

        if (e.key.length !== 1) return;

        if (inOtherField && !burst) {
            // قد تكون هذه أول ضغطة من دفعة ماسح: نحفظها لاستعادتها لاحقًا
            rememberLeak(active, e.key);
            return;   // كتابة بشرية في حقل آخر: نتركها كما هي
        }

        e.preventDefault();  // نمنع كتابة الحرف في العنصر الحالي

        // نستعيد الحرف الأول لو كان قد تسرّب إلى الحقل الآخر
        var reclaimed = inOtherField ? reclaimLeak(active) : '';

        // فاصل كبير = بداية كود جديد
        if (gap > 500 || reclaimed) {
            barcodeInput.value = '';
            resetCodeTiming();
        }

        noteCodeChar();
        if (reclaimed) codeChars++;   // نحسب الحرف المستعاد ضمن الكود

        barcodeInput.value += reclaimed + e.key;
        focusBarcode();
        lastFieldLength = barcodeInput.value.length;

        // خطة بديلة للماسحات التي لا ترسل Enter
        if (barcodeInput.value.trim().length >= AUTO_SUBMIT_MIN_LENGTH) {
            scheduleAutoSubmit(AUTO_SUBMIT_IDLE);
        }
    });

    /* عند إغلاق النافذة: تنظيف كامل للحالة حتى لا تتسرب مسحة قديمة */
    posModalEl.addEventListener('hidden.bs.modal', function () {
        cancelAutoSubmit();
        resetCodeTiming();
        pendingLeak = null;
        lastSubmittedCode = '';
        lastFieldLength = 0;

        // إيقاف الكاميرا إلزامي عند الإغلاق، وإلا بقيت تعمل في الخلفية
        if (window.CameraScanner) window.CameraScanner.stop();
    });

    /* ============================================================
       ربط ماسح الكاميرا
       الكاميرا مصدر إدخال إضافي يمرّ بنفس مسار التسليم الوحيد،
       فتستفيد تلقائيًا من الحماية ضد المسح المزدوج.
       ============================================================ */
    if (window.CameraScanner) {
        window.CameraScanner.init({
            onDetected: function (code) {
                submitBarcode(code);
            }
        });
    }

    /* ============================================================
       إكمال تلقائي لبيانات العميل

       رقم الهاتف هو مُعرِّف العميل في النظام، فبمجرد كتابة أول أرقامه
       نعرض العملاء المطابقين. اختيار عميل يملأ الاسم والرقم معًا، فلا
       يُكتب الاسم بصيغتين مختلفتين لنفس الرقم.

       المطابقة النهائية والربط يحدثان على السيرفر بالرقم الموحَّد، لذا
       هذه الشاشة تسهيل للإدخال لا مصدرًا للحقيقة.
       ============================================================ */
    (function initCustomerLookup() {
        var phoneInput = document.getElementById('customerPhone');
        var nameInput = document.getElementById('customerName');
        var box = document.getElementById('customerSuggestions');
        var hint = document.getElementById('customerHint');
        if (!phoneInput || !nameInput || !box) return;

        var debounceTimer = null;
        var lastTerm = '';
        var activeController = null;

        function hideBox() {
            box.classList.add('d-none');
            box.innerHTML = '';
        }

        function showHint(html, cls) {
            if (!hint) return;
            hint.className = 'mt-2 ' + cls;
            hint.innerHTML = html;
        }

        function clearHint() {
            if (!hint) return;
            hint.className = 'mt-2 d-none';
            hint.innerHTML = '';
        }

        function pick(customer) {
            phoneInput.value = customer.phone || '';
            nameInput.value = customer.name || '';
            hideBox();
            showHint(
                '<i class="bi bi-person-check-fill"></i> عميل مسجَّل — ' +
                'لديه <strong>' + customer.invoiceCount + '</strong> فاتورة سابقة',
                'text-success fw-semibold'
            );
        }

        function renderSuggestions(list) {
            if (!list || list.length === 0) {
                hideBox();
                // رقم جديد ليس خطأ — نطمئن المستخدم أن العميل سيُسجَّل تلقائيًا
                if (phoneInput.value.replace(/\D/g, '').length >= 7) {
                    showHint(
                        '<i class="bi bi-person-plus"></i> عميل جديد — سيُضاف تلقائيًا عند حفظ الفاتورة',
                        'text-secondary'
                    );
                } else {
                    clearHint();
                }
                return;
            }

            clearHint();
            box.innerHTML = '';
            list.forEach(function (c) {
                var item = document.createElement('button');
                item.type = 'button';
                item.className = 'customer-suggestion';
                item.innerHTML =
                    '<span class="cs-name">' + esc(c.name) + '</span>' +
                    '<span class="cs-phone num">' + esc(c.phone) + '</span>' +
                    '<span class="badge badge-soft-blue num">' + c.invoiceCount + '</span>';
                item.addEventListener('click', function () { pick(c); });
                box.appendChild(item);
            });
            box.classList.remove('d-none');
        }

        function lookup(term) {
            if (term === lastTerm) return;
            lastTerm = term;

            if (!term || term.trim().length < 2) {
                hideBox();
                clearHint();
                return;
            }

            // نُلغي الطلب السابق حتى لا تصل نتيجة قديمة بعد الأحدث فتُظهر
            // اقتراحات لا تُطابق ما يكتبه المستخدم الآن.
            if (activeController) activeController.abort();
            activeController = typeof AbortController !== 'undefined' ? new AbortController() : null;

            fetch('/Customers/Suggest?term=' + encodeURIComponent(term),
                  activeController ? { signal: activeController.signal } : undefined)
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(renderSuggestions)
                .catch(function () { /* الإكمال التلقائي ميزة مساعدة — لا نُزعج المستخدم بفشلها */ });
        }

        function schedule(term) {
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(function () { lookup(term); }, 250);
        }

        phoneInput.addEventListener('input', function () { schedule(phoneInput.value); });
        nameInput.addEventListener('input', function () { schedule(nameInput.value); });

        // إخفاء القائمة عند النقر خارجها
        document.addEventListener('click', function (e) {
            if (!box.contains(e.target) && e.target !== phoneInput && e.target !== nameInput) {
                hideBox();
            }
        });

        phoneInput.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') hideBox();
        });

        // تنظيف عند إغلاق النافذة — نفس منطق clearCart
        posModalEl.addEventListener('hidden.bs.modal', function () {
            hideBox();
            clearHint();
            lastTerm = '';
        });
    })();

    render();
})();

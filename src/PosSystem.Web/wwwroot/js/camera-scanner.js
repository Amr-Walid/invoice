/* ============================================================
   ماسح الباركود بالكاميرا (Camera Barcode Scanner)

   يفتح كاميرا الجهاز ويقرأ الباركود بصريًا عبر مكتبة ZXing،
   فيعمل النظام على الهاتف أو اللابتوب **بدون ماسح أجهزة**.

   ملاحظات مهمة:
   - getUserMedia لا يعمل إلا على HTTPS أو localhost (قيد أمني في
     المتصفحات)، ولهذا نعرض رسالة عربية واضحة عند الرفض.
   - يجب إيقاف البث (stream) عند الإغلاق، وإلا بقي مؤشر الكاميرا
     مضاءً واستمر استهلاك البطارية.
   - نستدعي دالة onDetected التي يُمرّرها pos.js، ولا نتعامل مع
     السلة هنا إطلاقًا — فصل واضح للمسؤوليات.
   ============================================================ */
window.CameraScanner = (function () {
    'use strict';

    /* ------------------------------------------------------------
       منع التكرار: الكاميرا ترى نفس الباركود في كل إطار، فلو اعتمدنا
       على مؤقّت زمني فقط لزادت الكمية تلقائيًا كل ثانية بينما الكاشير
       يوجّه الكاميرا نحو صنف واحد. القاعدة الصحيحة:
       لا نقبل نفس الكود مرة أخرى إلا بعد أن **يخرج من إطار الكاميرا**
       (عدة إطارات متتالية بلا قراءته) — كما تعمل ماسحات نقاط البيع.
       ------------------------------------------------------------ */
    var COOLDOWN_MS = 900;        // أدنى فاصل زمني بين قراءتين لنفس الكود
    var ABSENT_FRAMES_TO_REARM = 6;   // ~1.5 ثانية بلا رؤية الكود => أُعيد تسليحه
    var DECODE_MAX_W = 640;       // نُصغّر الإطار قبل الفك: أسرع بكثير ودقته كافية
    var DECODE_EVERY_MS = 250;    // فاصل مريح للمعالج ويبقى فوريًا للمستخدم

    var videoEl, panelEl, statusEl, stageEl, selectEl, openBtn;
    var reader = null;        // ZXing.MultiFormatReader
    var hints = null;         // تُمرَّر صراحةً في كل نداء decode (انظر decodeFrame)
    var workCanvas = null, workCtx = null;
    var loopTimer = null;
    var stream = null;
    var active = false;
    var onDetected = null;
    var lastCode = '';
    var lastAt = 0;
    var absentFrames = 0;     // عدد الإطارات المتتالية التي لم يظهر فيها lastCode
    var starting = false;

    function status(msg, isError) {
        if (!statusEl) return;
        statusEl.innerHTML = msg
            ? '<span style="color:' + (isError ? '#fca5a5' : '#e2e8f0') + '">' + msg + '</span>'
            : '';
    }

    /* هل المتصفح يدعم الكاميرا في هذا السياق؟ */
    function isSupported() {
        var ZX = window.ZXing;
        return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia) &&
               !!ZX && !!ZX.MultiFormatReader && !!ZX.BinaryBitmap &&
               !!ZX.HybridBinarizer && !!ZX.HTMLCanvasElementLuminanceSource;
    }

    /* رسالة عربية مفهومة لكل سبب فشل محتمل */
    function explainError(err) {
        var name = err && err.name ? err.name : '';

        if (!window.isSecureContext) {
            return 'الكاميرا تحتاج اتصالًا آمنًا (HTTPS). افتح النظام عبر https ثم أعد المحاولة.';
        }
        if (name === 'NotAllowedError' || name === 'PermissionDeniedError') {
            return 'تم رفض إذن الكاميرا. اسمح بالوصول للكاميرا من إعدادات المتصفح ثم أعد المحاولة.';
        }
        if (name === 'NotFoundError' || name === 'DevicesNotFoundError') {
            return 'لم يتم العثور على كاميرا في هذا الجهاز. استخدم ماسح الباركود أو الإدخال اليدوي.';
        }
        if (name === 'NotReadableError' || name === 'TrackStartError') {
            return 'الكاميرا مستخدمة من تطبيق آخر. أغلق التطبيقات الأخرى ثم أعد المحاولة.';
        }
        if (name === 'OverconstrainedError') {
            return 'الكاميرا المحددة غير متاحة. اختر كاميرا أخرى من القائمة.';
        }
        return 'تعذر تشغيل الكاميرا. استخدم ماسح الباركود أو الإدخال اليدوي.';
    }

    /* ------------------------------------------------------------
       أنواع الباركود المدعومة — نحصرها لتسريع القراءة ودقتها.

       تنبيه مهم (سبب عطل حقيقي):
       لا نستخدم BrowserMultiFormatReader لأن حلقته الداخلية تنادي
       MultiFormatReader.decode(bitmap) **بلا تلميحات**، وهذه الدالة
       تستدعي setHints(undefined) داخليًا فتمحو تلميحات الباني —
       فتضيع POSSIBLE_FORMATS و TRY_HARDER ولا يُقرأ أي باركود.
       لذلك نُدير الحلقة بأنفسنا ونمرّر hints صراحةً في كل نداء.
       ------------------------------------------------------------ */
    function buildReader() {
        var ZX = window.ZXing;
        var fmt = ZX.BarcodeFormat;

        hints = new Map();
        hints.set(ZX.DecodeHintType.POSSIBLE_FORMATS, [
            fmt.EAN_13, fmt.EAN_8,
            fmt.UPC_A, fmt.UPC_E,
            fmt.CODE_128, fmt.CODE_39,
            fmt.ITF, fmt.CODABAR,
            fmt.QR_CODE
        ]);
        // بلا TRY_HARDER: أثقل بأضعاف على الإطارات الحيّة وقد يُجمّد الصفحة،
        // والقراءة تنجح بدونه لأن الكاميرا تُعطينا إطارات متعددة كل ثانية.

        workCanvas = document.createElement('canvas');
        workCtx = workCanvas.getContext('2d', { willReadFrequently: true });

        return new ZX.MultiFormatReader();
    }

    /* فك شيفرة إطار واحد — يُرجع النص أو null */
    function decodeFrame() {
        if (!videoEl || !videoEl.videoWidth || videoEl.readyState < 2) return null;

        var ZX = window.ZXing;
        // التصغير ضروري: الفك على 1280×960 بطيء جدًا وقد يُسقط التبويب
        var scale = Math.min(1, DECODE_MAX_W / videoEl.videoWidth);
        workCanvas.width = Math.round(videoEl.videoWidth * scale);
        workCanvas.height = Math.round(videoEl.videoHeight * scale);
        workCtx.drawImage(videoEl, 0, 0, workCanvas.width, workCanvas.height);

        try {
            var bmp = new ZX.BinaryBitmap(new ZX.HybridBinarizer(
                new ZX.HTMLCanvasElementLuminanceSource(workCanvas)));
            // تمرير hints صراحةً — هذا هو جوهر الإصلاح
            return reader.decode(bmp, hints).getText();
        } catch (e) {
            return null;   // NotFoundException طبيعي جدًا لإطار بلا باركود
        } finally {
            try { reader.reset(); } catch (e) { /* تجاهل */ }
        }
    }

    function loopTick() {
        if (!active) return;

        var text = decodeFrame();
        if (text) {
            handleResult(text);
        } else if (lastCode) {
            // لا باركود في الإطار: بعد غياب كافٍ نُعيد تسليح الكود الأخير
            // ليتمكّن الكاشير من مسح **نفس الصنف** مرة ثانية بتوجيه الكاميرا إليه.
            absentFrames++;
            if (absentFrames >= ABSENT_FRAMES_TO_REARM) {
                lastCode = '';
                absentFrames = 0;
                status('وجّه الكاميرا نحو الباركود داخل الإطار');
            }
        }

        if (active) loopTimer = setTimeout(loopTick, DECODE_EVERY_MS);
    }

    /* تعبئة قائمة الكاميرات ليختار المستخدم (أمامية/خلفية) */
    function listCameras() {
        if (!navigator.mediaDevices.enumerateDevices) return Promise.resolve([]);
        return navigator.mediaDevices.enumerateDevices()
            .then(function (devices) {
                return devices.filter(function (d) { return d.kind === 'videoinput'; });
            })
            .catch(function () { return []; });
    }

    function fillCameraList(cams, currentId) {
        if (!selectEl) return;

        if (cams.length < 2) {
            selectEl.style.display = 'none';
            return;
        }

        selectEl.innerHTML = '';
        cams.forEach(function (cam, i) {
            var opt = document.createElement('option');
            opt.value = cam.deviceId;
            opt.textContent = cam.label || ('كاميرا ' + (i + 1));
            if (cam.deviceId === currentId) opt.selected = true;
            selectEl.appendChild(opt);
        });
        selectEl.style.display = '';
    }

    /* وميض أخضر + نغمة: تأكيد فوري أن القراءة نجحت */
    function flashHit() {
        if (!stageEl) return;
        stageEl.classList.remove('hit');
        // إعادة تشغيل الأنيميشن تتطلب reflow
        void stageEl.offsetWidth;
        stageEl.classList.add('hit');
        setTimeout(function () { stageEl.classList.remove('hit'); }, 500);
    }

    function handleResult(text) {
        var code = (text || '').trim();
        if (!code) return;

        var now = Date.now();
        // نفس الكود ما زال أمام الكاميرا => تجاهل تام (لا زيادة كمية تلقائية)
        if (code === lastCode) {
            absentFrames = 0;
            if ((now - lastAt) < COOLDOWN_MS) return;
            return;   // لن يُقبل مجددًا إلا بعد خروجه من الإطار (rearm في loopTick)
        }

        lastCode = code;
        lastAt = now;
        absentFrames = 0;

        flashHit();
        status('تم قراءة الكود: ' + code);

        if (typeof onDetected === 'function') onDetected(code);
    }

    /* تشغيل البث وربطه بعنصر الفيديو، ثم بدء حلقة الفك */
    function decodeFrom(deviceId) {
        var constraints = deviceId
            ? { video: { deviceId: { exact: deviceId } }, audio: false }
            // facingMode: 'environment' تفضّل الكاميرا الخلفية على الهاتف
            : { video: { facingMode: { ideal: 'environment' } }, audio: false };

        return navigator.mediaDevices.getUserMedia(constraints)
            .then(function (s) {
                stream = s;
                videoEl.srcObject = s;
                videoEl.setAttribute('playsinline', 'true');
                videoEl.muted = true;
                return videoEl.play().catch(function () { /* بعض المتصفحات ترفض بصمت */ });
            })
            .then(function () {
                // ننتظر وصول أبعاد الفيديو فعلًا قبل أول محاولة فك
                return new Promise(function (resolve) {
                    if (videoEl.videoWidth > 0) return resolve();
                    var done = false;
                    var finish = function () { if (!done) { done = true; resolve(); } };
                    videoEl.addEventListener('loadeddata', finish, { once: true });
                    setTimeout(finish, 3000);
                });
            });
    }

    function start(deviceId) {
        if (active || starting) return Promise.resolve();

        if (!isSupported()) {
            panelEl.style.display = '';
            status(!window.isSecureContext
                ? 'الكاميرا تحتاج اتصالًا آمنًا (HTTPS) للعمل.'
                : 'متصفحك لا يدعم قراءة الباركود بالكاميرا. استخدم الماسح أو الإدخال اليدوي.', true);
            return Promise.resolve();
        }

        starting = true;
        panelEl.style.display = '';
        status('جاري تشغيل الكاميرا…');
        if (openBtn) openBtn.classList.add('active');

        try {
            if (!reader) reader = buildReader();
        } catch (e) {
            starting = false;
            status('تعذر تهيئة قارئ الباركود.', true);
            return Promise.resolve();
        }

        return decodeFrom(deviceId)
            .then(function () {
                active = true;
                starting = false;
                status('وجّه الكاميرا نحو الباركود داخل الإطار');
                loopTick();                 // بدء حلقة الفك التي نُديرها بأنفسنا
                return listCameras();
            })
            .then(function (cams) {
                var currentId = deviceId;
                if (!currentId && stream) {
                    var track = stream.getVideoTracks()[0];
                    if (track && track.getSettings) currentId = track.getSettings().deviceId;
                }
                fillCameraList(cams, currentId);
            })
            .catch(function (err) {
                starting = false;
                active = false;
                if (openBtn) openBtn.classList.remove('active');
                status(explainError(err), true);
            });
    }

    function stop() {
        active = false;
        starting = false;
        lastCode = '';
        absentFrames = 0;

        // إيقاف حلقة الفك أولًا حتى لا تعمل على بثّ مُغلق
        if (loopTimer) { clearTimeout(loopTimer); loopTimer = null; }

        if (reader) {
            try { reader.reset(); } catch (e) { /* تجاهل */ }
        }

        // إيقاف كل المسارات صراحةً — وإلا بقي مؤشر الكاميرا مضاءً
        if (stream) {
            stream.getTracks().forEach(function (t) {
                try { t.stop(); } catch (e) { /* تجاهل */ }
            });
            stream = null;
        }
        if (videoEl) {
            try { videoEl.pause(); } catch (e) { /* تجاهل */ }
            videoEl.srcObject = null;
        }

        if (panelEl) panelEl.style.display = 'none';
        if (openBtn) openBtn.classList.remove('active');
        status('');
    }

    function toggle() {
        if (active || starting) stop();
        else start(selectEl && selectEl.value ? selectEl.value : null);
    }

    /* ==================== التهيئة ==================== */
    function init(opts) {
        opts = opts || {};
        onDetected = opts.onDetected;

        videoEl = document.getElementById('cameraVideo');
        panelEl = document.getElementById('cameraPanel');
        statusEl = document.getElementById('cameraStatus');
        stageEl = panelEl ? panelEl.querySelector('.camera-stage') : null;
        selectEl = document.getElementById('cameraSelect');
        openBtn = document.getElementById('btnOpenCamera');

        if (!videoEl || !panelEl || !openBtn) return;

        openBtn.addEventListener('click', toggle);

        var closeBtn = document.getElementById('btnCloseCamera');
        if (closeBtn) closeBtn.addEventListener('click', stop);

        // تبديل الكاميرا: نُوقف الحالية ثم نبدأ بالمُختارة
        if (selectEl) {
            selectEl.addEventListener('change', function () {
                var id = selectEl.value;
                stop();
                setTimeout(function () { start(id); }, 150);
            });
        }
    }

    return {
        init: init,
        start: start,
        stop: stop,
        toggle: toggle,
        isActive: function () { return active; },
        isSupported: isSupported
    };
})();

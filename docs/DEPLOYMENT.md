# دليل نشر نظام المبيعات على SQL Server

> موجّه لمن سينشر النظام على خادم الشركة. كل أمر هنا مُجرَّب، والترتيب مقصود.
> اقرأ **قسم الأخطاء الشائعة** في آخر الملف قبل أن تبدأ — يوفّر عليك ساعات.

---

## ملخّص سريع (لمن يعرف ما يفعل)

```powershell
# 1) القاعدة
sqlcmd -S localhost -E -i deploy\sql\00_create_database.sql
sqlcmd -S localhost -E -d PosSystemDb -i deploy\sql\01_create_schema.sql

# 2) النشر
dotnet publish src\PosSystem.Web\PosSystem.Web.csproj -c Release -o C:\inetpub\PosSystem

# 3) الأسرار (لا تكتبها في أي ملف)
setx ConnectionStrings__DefaultConnection "Server=localhost;Database=PosSystemDb;Trusted_Connection=True;TrustServerCertificate=True" /M
setx Seed__AdminPassword  "<كلمة سر قوية>" /M
setx Seed__AgentPassword  "<كلمة سر قوية>" /M
setx Seed__KeeperPassword "<كلمة سر قوية>" /M
setx ASPNETCORE_ENVIRONMENT "Production" /M

# 4) تحقّق
curl http://localhost/health     # يجب أن يطبع: Healthy
```

---

## ١. المتطلبات

| المكوّن | الإصدار | ملاحظة |
|---|---|---|
| Windows Server | 2016 فأحدث | أو Linux مع Nginx |
| SQL Server | 2017 فأحدث | Express يكفي (سقف ١٠ جيجا للقاعدة) |
| .NET Hosting Bundle | **8.0** | ⚠️ «Hosting Bundle» لا «Runtime» وحده — بدونه IIS لا يعرف كيف يشغّل التطبيق |
| IIS | مع ASP.NET Core Module V2 | يأتي مع الـHosting Bundle |

**تنزيل الـHosting Bundle:** <https://dotnet.microsoft.com/download/dotnet/8.0> → قسم "Windows" → **ASP.NET Core Runtime 8.x → Hosting Bundle**.

> بعد تثبيت الـHosting Bundle **أعد تشغيل IIS**: `iisreset`
> بدون ذلك يظهر خطأ 500.19 أو 502.5 ويضيع وقتك في البحث عن سبب غير موجود.

---

## ٢. إنشاء قاعدة البيانات

### ٢.١ إنشاء القاعدة نفسها

```powershell
sqlcmd -S localhost -E -i deploy\sql\00_create_database.sql
```

`-E` تعني مصادقة Windows. لو تستخدم حساب SQL:
```powershell
sqlcmd -S localhost -U sa -P <كلمة السر> -i deploy\sql\00_create_database.sql
```

السكربت **آمن لإعادة التشغيل** — لن يمسّ قاعدة موجودة.

### ٢.٢ إنشاء الجداول

```powershell
sqlcmd -S localhost -E -d PosSystemDb -i deploy\sql\01_create_schema.sql
```

ينشئ **٢٧ جدولًا** + جدول تتبّع الهجرات. كل عبارة محاطة بـ`IF NOT EXISTS`،
فإعادة التشغيل لا تُكرّر شيئًا ولا تحذف بيانات.

### ٢.٣ تحقّق

```sql
USE PosSystemDb;
SELECT COUNT(*) AS الجداول FROM sys.tables;            -- المتوقع: 28
SELECT MigrationId FROM __EFMigrationsHistory;          -- المتوقع: 5 صفوف
```

> **بديل:** يمكنك تخطّي ٢.٢ تمامًا وترك التطبيق يطبّق الهجرات بنفسه عند أول
> إقلاع (هذا سلوكه الافتراضي). لكن ذلك يتطلب منح حساب التطبيق صلاحية تعديل
> المخطط، وهو ما لا نوصي به في الإنتاج — راجع التعليق في `00_create_database.sql`.

---

## ٣. سلسلة الاتصال

**اختر واحدة** حسب نوع المصادقة:

```
# مصادقة Windows (الأفضل — بلا كلمة سر في أي مكان)
Server=localhost;Database=PosSystemDb;Trusted_Connection=True;TrustServerCertificate=True

# SQL Express
Server=localhost\SQLEXPRESS;Database=PosSystemDb;Trusted_Connection=True;TrustServerCertificate=True

# مصادقة SQL (خادم غير منضمّ للدومين)
Server=localhost,1433;Database=PosSystemDb;User Id=pos_app;Password=<كلمة السر>;TrustServerCertificate=True
```

> **`TrustServerCertificate=True`** ضرورية إن كانت شهادة SQL Server ذاتية التوقيع
> (الوضع الافتراضي). بدونها يفشل الاتصال برسالة عن سلسلة الثقة. لو لديك شهادة
> موقّعة من جهة معتمدة فاحذفها — فهي تُعطّل التحقق من هوية الخادم.

### أين تُوضع؟

**🔴 لا تضعها في `appsettings.json`.** الملف يُرفع مع التطبيق ويمكن قراءته،
وسلسلة الاتصال قد تحوي كلمة سر. استخدم متغيّرات البيئة:

```powershell
setx ConnectionStrings__DefaultConnection "Server=localhost;Database=PosSystemDb;Trusted_Connection=True;TrustServerCertificate=True" /M
```

الشرطتان السفليتان `__` هما فاصل المستويات في .NET
(`ConnectionStrings:DefaultConnection`). `/M` تجعله على مستوى الجهاز لا المستخدم.

---

## ٤. كلمات سر الحسابات الأولى

النظام ينشئ ثلاثة حسابات عند أول إقلاع. **في الإنتاج يرفض النظام الإقلاع
إن لم تضبط كلمات سرها** — بدل أن يعمل بكلمات سر منشورة في المستودع:

```powershell
setx Seed__AdminPassword  "<كلمة سر قوية>" /M
setx Seed__AgentPassword  "<كلمة سر قوية>" /M
setx Seed__KeeperPassword "<كلمة سر قوية>" /M
```

**متطلبات كلمة السر:** ٦ أحرف على الأقل وتحوي رقمًا واحدًا على الأقل.
(معرّفة في `Program.cs`؛ ارفعها هناك إن أرادت سياسة الشركة ذلك.)

| الحساب | البريد | الدور |
|---|---|---|
| مدير النظام | `admin@pos.local` | Admin — كل الصلاحيات |
| مندوب المبيعات | `agent@pos.local` | Agent — البيع وفواتيره هو فقط |
| أمين المخزن | `keeper@pos.local` | WarehouseKeeper — المخازن والاستلام |

> **بعد أول دخول:** غيّر البُرد الإلكترونية إلى بُرد الموظفين الحقيقيين من شاشة
> المستخدمين. الحسابات الافتراضية معروفة لكل من قرأ هذا الملف.

---

## ٥. النشر

```powershell
dotnet publish src\PosSystem.Web\PosSystem.Web.csproj -c Release -o C:\inetpub\PosSystem
```

### إعداد IIS

1. **Application Pool** جديد باسم `PosSystem`:
   - `.NET CLR Version` = **No Managed Code** (التطبيق يعمل خارج CLR الخاص بـIIS)
   - `Identity` = `ApplicationPoolIdentity` (أو حساب دومين إن كنت تستخدم مصادقة Windows للقاعدة)
2. **Website / Application** يشير إلى `C:\inetpub\PosSystem`
3. اربطه بالـApp Pool أعلاه

**صلاحيات مصادقة Windows للقاعدة:** أعطِ هوية الـApp Pool حق الوصول:

```sql
USE [master];
CREATE LOGIN [IIS APPPOOL\PosSystem] FROM WINDOWS;
GO
USE [PosSystemDb];
CREATE USER [IIS APPPOOL\PosSystem] FOR LOGIN [IIS APPPOOL\PosSystem];
ALTER ROLE [db_datareader] ADD MEMBER [IIS APPPOOL\PosSystem];
ALTER ROLE [db_datawriter] ADD MEMBER [IIS APPPOOL\PosSystem];
GO
```

> إن تركت التطبيق يطبّق الهجرات بنفسه فأضف كذلك:
> `ALTER ROLE [db_ddladmin] ADD MEMBER [IIS APPPOOL\PosSystem];`

---

## ٦. التحقق بعد النشر

```powershell
curl http://localhost/health
```

| النتيجة | المعنى |
|---|---|
| `Healthy` | الاتصال بالقاعدة سليم — النظام جاهز |
| `Unhealthy` | التطبيق يعمل لكن القاعدة مقطوعة — راجع سلسلة الاتصال |
| لا استجابة | التطبيق لم يُقلع — راجع سجل الأحداث (Event Viewer) |

ثم افتح المتصفح على عنوان الموقع، وسجّل الدخول بحساب المدير، وتأكد من:
- ظهور شاشة لوحة التحكم بالعربية من اليمين لليسار
- `المنتجات` فارغة (طبيعي — الإنتاج يبدأ بلا بيانات تجريبية)
- `المخازن` فيها «المخزن الرئيسي»
- `العلامات` فيها `Infinix` و`التصنيفات` فيها الثلاثة

---

## ٧. ما الذي يختلف في الإنتاج؟

النظام يتصرّف بشكل مختلف حين `ASPNETCORE_ENVIRONMENT=Production`:

| السلوك | التطوير | الإنتاج |
|---|---|---|
| منتجات وكوبونات تجريبية | تُنشأ | **لا تُنشأ** |
| كلمات السر الافتراضية | مسموحة | **مرفوضة — لا يُقلع النظام** |
| فشل الاتصال بالقاعدة | يُسجَّل ويكمل | **يوقف الإقلاع** |
| تحويل HTTPS | معطّل | **مفعّل** |
| صفحة الخطأ | تفاصيل الاستثناء | صفحة عامة |

> **لماذا يوقف فشل القاعدة الإقلاع؟** تطبيق يعمل بقاعدة غير مهيّأة يبدو سليمًا
> لموازِن الأحمال، فيستقبل المستخدمين ثم ينهار عند أول عملية بيع. الفشل الصريح
> عند الإقلاع أرخص كثيرًا من فشل صامت أثناء العمل.

**لإبقاء البيانات التجريبية في بيئة اختبار شبيهة بالإنتاج:**
`Seed__DemoData=true`

**لإيقاف التهيئة كليًا بعد استقرار النظام:** `Seed__Enabled=false`

---

## ٨. النسخ الاحتياطي

القاعدة مضبوطة على `RECOVERY FULL`، فيمكن الاستعادة إلى أي لحظة.
**هذا يعني أن سجل المعاملات ينمو حتى تأخذ نسخة منه** — بدون ذلك يمتلئ القرص.

```sql
-- نسخة كاملة يومية
BACKUP DATABASE [PosSystemDb]
  TO DISK = N'D:\Backup\PosSystemDb_full.bak'
  WITH INIT, COMPRESSION;

-- نسخة من سجل المعاملات كل ساعة (تُقلّص السجل)
BACKUP LOG [PosSystemDb]
  TO DISK = N'D:\Backup\PosSystemDb_log.trn';
```

اجعلهما مهمّتين في SQL Server Agent. لو استخدمت Express (بلا Agent) فاستخدم
Task Scheduler مع `sqlcmd`.

> **لا تستخدم `RECOVERY SIMPLE` لتتفادى نمو السجل** — ستفقد كل المعاملات منذ
> آخر نسخة كاملة. في نظام مبيعات هذا يعني ضياع فواتير يوم كامل.

---

## ٩. التحديثات اللاحقة

عند وصول إصدار جديد فيه تغيير في القاعدة:

```powershell
# 1) أوقف الموقع من IIS
# 2) خذ نسخة احتياطية كاملة  ← لا تتخطَّ هذه
# 3) ولّد سكربت الترقية وطبّقه
dotnet ef migrations script --idempotent -o upgrade.sql
sqlcmd -S localhost -E -d PosSystemDb -i upgrade.sql
# 4) انشر الملفات الجديدة
# 5) شغّل الموقع وتحقّق من /health
```

السكربت `--idempotent` يطبّق الناقص فقط ويتخطّى المُطبَّق — آمن حتى لو
شككت في أي إصدار كانت القاعدة.

---

## ١٠. أخطاء شائعة وحلولها

| العرض | السبب | الحل |
|---|---|---|
| **500.19** أو **502.5** عند فتح الموقع | الـHosting Bundle غير مثبّت أو IIS لم يُعد تشغيله | ثبّته ثم `iisreset` |
| `A network-related or instance-specific error` | اسم الخادم خطأ أو TCP/IP معطّل | فعّله من SQL Server Configuration Manager ثم أعد تشغيل الخدمة |
| `The certificate chain was issued by an authority that is not trusted` | شهادة SQL ذاتية التوقيع | أضف `TrustServerCertificate=True` |
| `Login failed for user 'IIS APPPOOL\PosSystem'` | هوية الـApp Pool بلا صلاحية | نفّذ سكربت الصلاحيات في القسم ٥ |
| **النظام لا يُقلع** ورسالة عن كلمة سر التهيئة | `Seed__AdminPassword` غير مضبوط في الإنتاج | اضبط المتغيّرات الثلاثة ثم `iisreset` |
| القاعدة صحيحة لكن `/health` يقول `Unhealthy` | التطبيق يقرأ سلسلة اتصال أخرى | تأكد أن `setx` استُخدم مع `/M` وأنك أعدت تشغيل IIS بعده |
| العربية تظهر `?????` | عمود `varchar` بدل `nvarchar` | لا يحدث مع سكربتاتنا — كل الأعمدة `nvarchar` |
| التقارير تظهر صفرًا رغم وجود فواتير | فلتر الفترة على «اليوم» والفواتير من أيام سابقة | اضغط «شهري» أو «كل الفترات» — ليس عطلًا |

### متغيّرات البيئة لا تُقرأ؟

`setx` يؤثّر على العمليات **الجديدة** فقط. بعد ضبطها:
```powershell
iisreset
```
وللتحقق من قراءتها فعلًا:
```powershell
[System.Environment]::GetEnvironmentVariable("ConnectionStrings__DefaultConnection","Machine")
```

---

## ملحق: النشر على Linux

```bash
# نشر
dotnet publish src/PosSystem.Web/PosSystem.Web.csproj -c Release -o /var/www/possystem

# الأسرار في وحدة systemd (وليست في ملف داخل مجلد الموقع)
sudo tee /etc/systemd/system/possystem.service >/dev/null <<'EOF'
[Unit]
Description=POS System
After=network.target

[Service]
WorkingDirectory=/var/www/possystem
ExecStart=/usr/bin/dotnet /var/www/possystem/PosSystem.Web.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=ConnectionStrings__DefaultConnection=Server=...;Database=PosSystemDb;User Id=pos_app;Password=...;TrustServerCertificate=True
Environment=Seed__AdminPassword=...
Environment=Seed__AgentPassword=...
Environment=Seed__KeeperPassword=...

[Install]
WantedBy=multi-user.target
EOF

sudo chmod 600 /etc/systemd/system/possystem.service   # الأسرار داخله
sudo systemctl daemon-reload
sudo systemctl enable --now possystem
curl http://127.0.0.1:5000/health
```

ثم ضع Nginx أمامه كعاكس عكسي مع تمرير الترويسات:

```nginx
location / {
    proxy_pass         http://127.0.0.1:5000;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade $http_upgrade;
    proxy_set_header   Connection keep-alive;
    proxy_set_header   Host $host;
    proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;   # ← بدونها تحدث حلقة إعادة توجيه
}
```

> `X-Forwarded-Proto` ليست اختيارية: التطبيق يُحوّل إلى HTTPS في الإنتاج،
> وبدون هذه الترويسة يرى كل طلب على أنه `http` فيُعيد التوجيه إلى ما لا نهاية.
> التطبيق مُعدّ لقراءتها عبر `UseForwardedHeaders`.

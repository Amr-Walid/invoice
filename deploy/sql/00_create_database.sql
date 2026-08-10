/* ============================================================
   إنشاء قاعدة بيانات نظام المبيعات — SQL Server
   ------------------------------------------------------------
   يُشغَّل مرة واحدة قبل 01_create_schema.sql
   قابل لإعادة التشغيل بأمان (لا يمسّ قاعدة قائمة).

   التشغيل:
     sqlcmd -S <SERVER> -E -i 00_create_database.sql
   أو من SSMS: افتح الملف واضغط Execute.
   ============================================================ */

IF DB_ID(N'PosSystemDb') IS NULL
BEGIN
    CREATE DATABASE [PosSystemDb];
    PRINT N'تم إنشاء قاعدة البيانات PosSystemDb';
END
ELSE
    PRINT N'قاعدة البيانات PosSystemDb موجودة — لم يُغيَّر شيء';
GO

ALTER DATABASE [PosSystemDb] SET RECOVERY FULL;
GO

/* ------------------------------------------------------------
   الترتيب (Collation) — مهم للنصوص العربية
   ------------------------------------------------------------
   الأعمدة كلها nvarchar (Unicode) فالتخزين سليم مع أي ترتيب،
   لكن الترتيب يحدّد نتيجة المقارنة والفرز:
     • CI = غير حسّاس لحالة الأحرف (Case Insensitive)
     • AS = حسّاس للتشكيل (Accent Sensitive)
   الترتيب أدناه يجعل البحث عن «محمد» يطابق «محمد» بصرف النظر
   عن حالة الحروف اللاتينية في أسماء المنتجات (Infinix/INFINIX).

   ⚠️ لا يمكن تغيير الترتيب بعد إدخال البيانات بسهولة — اضبطه الآن.
   ------------------------------------------------------------ */
IF NOT EXISTS (
    SELECT 1 FROM sys.databases
    WHERE name = N'PosSystemDb'
      AND collation_name = N'Arabic_CI_AS'
)
BEGIN
    BEGIN TRY
        ALTER DATABASE [PosSystemDb] COLLATE Arabic_CI_AS;
        PRINT N'تم ضبط الترتيب على Arabic_CI_AS';
    END TRY
    BEGIN CATCH
        -- يفشل لو كانت هناك اتصالات مفتوحة أو بيانات تمنع التحويل.
        -- ليس عائقًا: الأعمدة nvarchar فالعربية تُخزَّن وتُعرض سليمة
        -- على أي ترتيب، والفرق يظهر في ترتيب الفرز فقط.
        PRINT N'تعذّر ضبط الترتيب (غير حرج) — ' + ERROR_MESSAGE();
    END CATCH
END
GO

/* ------------------------------------------------------------
   حساب التطبيق — مصادقة SQL
   ------------------------------------------------------------
   الأفضل أمنيًا أن يعمل التطبيق بحساب Windows (Trusted_Connection)
   بلا كلمة سر في سلسلة الاتصال إطلاقًا. استخدم القسم التالي فقط
   إن كان التطبيق على خادم غير منضمّ للدومين.

   🔴 غيّر كلمة السر أدناه قبل التشغيل، ولا تحفظ الملف بعدها.
   ------------------------------------------------------------ */
/*
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'pos_app')
BEGIN
    CREATE LOGIN [pos_app]
        WITH PASSWORD = N'ضع_كلمة_سر_قوية_هنا',
             CHECK_POLICY = ON;
END
GO

USE [PosSystemDb];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pos_app')
BEGIN
    CREATE USER [pos_app] FOR LOGIN [pos_app];
END
GO

-- أقل صلاحية كافية: قراءة وكتابة البيانات فقط.
-- لا db_owner: حساب التطبيق لا يحتاج تعديل المخطط بعد النشر،
-- وإعطاؤه إياه يعني أن أي ثغرة حقن SQL تستطيع حذف الجداول.
ALTER ROLE [db_datareader] ADD MEMBER [pos_app];
ALTER ROLE [db_datawriter] ADD MEMBER [pos_app];
GO

-- ملاحظة: إن اخترت أن يطبّق التطبيق الهجرات بنفسه عند الإقلاع
-- (السلوك الافتراضي) فسيحتاج صلاحية تعديل المخطط أيضًا:
--   ALTER ROLE [db_ddladmin] ADD MEMBER [pos_app];
-- الأنظف: طبّق 01_create_schema.sql يدويًا بحساب مدير،
-- وأبقِ حساب التطبيق على القراءة/الكتابة فقط.
*/

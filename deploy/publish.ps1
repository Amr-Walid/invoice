<#
.SYNOPSIS
    ينشر نظام المبيعات إلى مجلد جاهز للرفع على IIS.

.DESCRIPTION
    يغلّف الخطوات المتكرّرة في أمر واحد. لا يلمس قاعدة البيانات ولا
    متغيّرات البيئة — راجع docs/DEPLOYMENT.md لهما.

.EXAMPLE
    .\deploy\publish.ps1
    .\deploy\publish.ps1 -OutputPath "D:\Sites\Pos" -SkipBackup
#>

[CmdletBinding()]
param(
    [string] $OutputPath = "C:\inetpub\PosSystem",
    [string] $SiteName   = "PosSystem",
    [switch] $SkipBackup
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "..\src\PosSystem.Web\PosSystem.Web.csproj"
if (-not (Test-Path $projectPath)) {
    throw "لم يُعثر على ملف المشروع: $projectPath"
}

Write-Host "`n=== نشر نظام المبيعات ===" -ForegroundColor Cyan
Write-Host "المصدر : $projectPath"
Write-Host "الوجهة : $OutputPath`n"

# ------------------------------------------------------------
# نسخة احتياطية من النشر السابق
# الاستعادة السريعة أهم من توفير الوقت: لو ظهر عطل بعد النشر
# فاسترجاع المجلد أسرع من إعادة بناء إصدار قديم.
# ------------------------------------------------------------
if ((Test-Path $OutputPath) -and -not $SkipBackup) {
    $stamp  = Get-Date -Format "yyyy-MM-dd_HHmmss"
    $backup = "${OutputPath}_backup_$stamp"
    Write-Host "نسخ احتياطي للنشر السابق → $backup" -ForegroundColor Yellow
    Copy-Item $OutputPath $backup -Recurse -Force
}

# ------------------------------------------------------------
# إيقاف الموقع قبل الكتابة
# ملفات .dll تكون مقفولة أثناء العمل، والنشر فوقها يفشل جزئيًا
# فيترك المجلد بخليط من إصدارين — أسوأ من الفشل الكامل.
# ------------------------------------------------------------
$iisAvailable = $null -ne (Get-Module -ListAvailable -Name WebAdministration)
if ($iisAvailable) {
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    if (Test-Path "IIS:\Sites\$SiteName") {
        Write-Host "إيقاف الموقع $SiteName ..." -ForegroundColor Yellow
        Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}
else {
    Write-Host "وحدة WebAdministration غير متاحة — تخطّي إيقاف/تشغيل الموقع." -ForegroundColor DarkGray
}

try {
    Write-Host "`nبناء ونشر (Release) ..." -ForegroundColor Cyan
    dotnet publish $projectPath -c Release -o $OutputPath --nologo
    if ($LASTEXITCODE -ne 0) { throw "فشل أمر النشر (رمز $LASTEXITCODE)." }
    Write-Host "`n✔ اكتمل النشر إلى $OutputPath" -ForegroundColor Green
}
finally {
    # يعمل حتى لو فشل النشر: ترك الموقع متوقفًا بصمت أسوأ من نشر ناقص.
    if ($iisAvailable -and (Test-Path "IIS:\Sites\$SiteName")) {
        Write-Host "تشغيل الموقع $SiteName ..." -ForegroundColor Yellow
        Start-Website -Name $SiteName -ErrorAction SilentlyContinue
    }
}

Write-Host @"

الخطوات التالية:
  1) تأكد من ضبط متغيّرات البيئة (مرة واحدة فقط):
       ConnectionStrings__DefaultConnection
       Seed__AdminPassword / Seed__AgentPassword / Seed__KeeperPassword
       ASPNETCORE_ENVIRONMENT = Production
  2) iisreset      ← ضروري بعد أي تغيير في متغيّرات البيئة
  3) تحقّق:  curl http://localhost/health   ← يجب أن يطبع Healthy

التفاصيل في docs/DEPLOYMENT.md
"@ -ForegroundColor Cyan

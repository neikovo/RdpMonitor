#Requires -RunAsAdministrator
<#
    Включва в Windows записването на събитията, от които агентът чете одита:
      - "Process Creation" (събитие 4688) + команден ред в него   -> за одита на процеси
      - "File System" (събитие 4663) + одит запис на избраните папки -> за одита на файлове ("кой и кога")

    Папките се четат от appsettings.json (Audit:FileFolders), в същата папка като този скрипт.

    Какво променя (показва го и пита за потвърждение):
      1. Политика за одит:  auditpol  (Process Creation, File System - само успешни)
      2. Регистър:          ProcessCreationIncludeCmdLine_Enabled = 1
      3. Одит запис (SACL) за Everyone върху всяка избрана папка: само запис на промяна/изтриване.
         НЕ променя правата за достъп (DACL) на папките.
    Ако машината е в домейн и политиката за одит се задава от Group Policy, GPO може да презапише точка 1.

    Използване:
        .\setup-audit.ps1            # настройва
        .\setup-audit.ps1 -Remove    # маха одит записите от папките (политиката за одит се оставя - виж края)
        .\setup-audit.ps1 -Yes       # без въпрос за потвърждение
#>

param(
    [switch]$Remove,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

$cfgPath = Join-Path $PSScriptRoot "appsettings.json"
if (-not (Test-Path $cfgPath)) { throw "Не намирам $cfgPath" }
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
$folders = @($cfg.Audit.FileFolders | Where-Object { $_ })
$procAudit = [bool]$cfg.Audit.MonitorProcesses

$guidProcess = "{0CCE922B-69AE-11D9-BED3-505054503030}"   # Process Creation
$guidFile    = "{0CCE921D-69AE-11D9-BED3-505054503030}"   # File System
$rights = [System.Security.AccessControl.FileSystemRights]"CreateFiles, WriteData, AppendData, Delete, DeleteSubdirectoriesAndFiles"
$everyone = New-Object System.Security.Principal.SecurityIdentifier("S-1-1-0")

function New-AuditRule {
    New-Object System.Security.AccessControl.FileSystemAuditRule(
        $everyone, $rights,
        [System.Security.AccessControl.InheritanceFlags]"ContainerInherit, ObjectInherit",
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AuditFlags]::Success)
}

if ($Remove) {
    Write-Host "Ще бъдат махнати одит записите (за Everyone) от папките:" -ForegroundColor Yellow
    $folders | ForEach-Object { Write-Host "  $_" }
    if (-not $Yes -and (Read-Host "Продължаваш? (y/n)") -ne "y") { return }
    foreach ($f in $folders) {
        if (-not (Test-Path $f)) { Write-Warning "Липсва: $f"; continue }
        $acl = Get-Acl -Path $f -Audit
        $acl.RemoveAuditRuleAll((New-AuditRule))
        Set-Acl -Path $f -AclObject $acl
        Write-Host "Махнат одит запис: $f"
    }
    Write-Host "`nПолитиката за одит (auditpol) НЕ е променена, защото други инструменти може да я ползват."
    Write-Host "За да я изключиш ръчно: auditpol /set /subcategory:`"$guidFile`" /success:disable"
    return
}

Write-Host "Ще бъдат направени следните промени на тази машина:" -ForegroundColor Yellow
if ($procAudit) {
    Write-Host "  1. auditpol: включва одит 'Process Creation' (успешни) и записване на командния ред в събитие 4688"
}
if ($folders.Count -gt 0) {
    Write-Host "  2. auditpol: включва одит 'File System' (успешни)"
    Write-Host "  3. Одит запис (само запис на събития, без промяна на правата) върху папките:"
    $folders | ForEach-Object { Write-Host "       $_" }
}
if (-not $procAudit -and $folders.Count -eq 0) {
    Write-Host "Нищо за настройване: Audit:MonitorProcesses е false и Audit:FileFolders е празен в appsettings.json." -ForegroundColor Yellow
    return
}
if (-not $Yes -and (Read-Host "Продължаваш? (y/n)") -ne "y") { Write-Host "Отказано."; return }

if ($procAudit) {
    auditpol /set /subcategory:"$guidProcess" /success:enable | Out-Null
    $key = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit"
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    New-ItemProperty -Path $key -Name "ProcessCreationIncludeCmdLine_Enabled" -Value 1 -PropertyType DWord -Force | Out-Null
    Write-Host "OK: одит на процесите + команден ред" -ForegroundColor Green
}

if ($folders.Count -gt 0) {
    auditpol /set /subcategory:"$guidFile" /success:enable | Out-Null
    foreach ($f in $folders) {
        if (-not (Test-Path $f)) { Write-Warning "Липсва, прескачам: $f"; continue }
        $acl = Get-Acl -Path $f -Audit
        $acl.AddAuditRule((New-AuditRule))
        Set-Acl -Path $f -AclObject $acl
        Write-Host "OK: одит запис на $f" -ForegroundColor Green
    }
}

Write-Host "`nГотово. Рестартирай агента, за да прочете настройките:  Restart-Service RdpMonitorAgent"

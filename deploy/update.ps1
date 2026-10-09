<#
    Обновява вече инсталиран RDP Monitor Agent или Server с тази нова версия.
    Запазва твоя appsettings.json (и добавя само новите настройки, които липсват в него).
    Не пипа базата данни, сертификата и идентичността на агента (те са в C:\ProgramData\RdpMonitor).

    Как се ползва:
      1. Разархивирай новия zip в НОВА папка (напр. C:\update\RdpMonitor.Agent).
      2. От тази нова папка, в PowerShell като администратор:
            .\update.ps1 -Target C:\RdpMonitor.Agent
         (за сървъра: -Target C:\RdpMonitor.Server)

    Какво прави:  спира услугата -> прави копие на appsettings.json -> копира новите файлове ->
                  добавя липсващите настройки -> сваля "изтеглено отвън" етикета -> стартира услугата.
#>

param(
    [Parameter(Mandatory = $true)][string]$Target,
    [switch]$NoService   # само за тест: не пипа услугата
)

$ErrorActionPreference = "Stop"
$src = $PSScriptRoot

if (-not $NoService) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw "Пусни скрипта в PowerShell като администратор." }
}

if (Test-Path (Join-Path $src "RdpMonitorAgent.exe"))      { $service = "RdpMonitorAgent";  $exe = "RdpMonitorAgent.exe" }
elseif (Test-Path (Join-Path $src "RdpMonitorServer.exe")) { $service = "RdpMonitorServer"; $exe = "RdpMonitorServer.exe" }
else { throw "В $src няма RdpMonitorAgent.exe или RdpMonitorServer.exe - пусни скрипта от разархивираната НОВА папка." }

if (-not (Test-Path (Join-Path $Target $exe))) {
    throw "В $Target не намирам $exe. Провери -Target (папката на ВЕЧЕ инсталираната версия)."
}
if ((Resolve-Path $src).Path -eq (Resolve-Path $Target).Path) { throw "Новата и старата папка не могат да са една и съща." }

function Merge-Missing($old, $new) {
    # добавя в $old само ключовете, които ги няма; стойностите на потребителя не се пипат
    foreach ($prop in $new.PSObject.Properties) {
        if ($null -eq $old.PSObject.Properties[$prop.Name]) {
            $old | Add-Member -NotePropertyName $prop.Name -NotePropertyValue $prop.Value
            Write-Host "  + нова настройка: $($prop.Name)"
        }
        elseif ($prop.Value -is [pscustomobject] -and $old.($prop.Name) -is [pscustomobject]) {
            Merge-Missing $old.($prop.Name) $prop.Value
        }
    }
}

$cfgTarget = Join-Path $Target "appsettings.json"
$cfgNew    = Join-Path $src "appsettings.json"
$stamp     = Get-Date -Format yyyyMMdd-HHmmss

if (-not $NoService) {
    $svc = Get-Service -Name $service -ErrorAction SilentlyContinue
    if ($svc) { Write-Host "Спирам $service ..."; Stop-Service -Name $service -Force; $svc.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30)) }
    else { Write-Warning "Услугата $service не е инсталирана. Файловете ще се обновят; после пусни install.ps1." }
}

if (Test-Path $cfgTarget) {
    $backup = Join-Path $Target "appsettings.backup-$stamp.json"
    Copy-Item $cfgTarget $backup
    Write-Host "Копие на настройките: $backup"
}

Write-Host "Копирам новите файлове ..."
robocopy $src $Target /E /XF appsettings.json "appsettings.backup-*.json" /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy се провали (код $LASTEXITCODE)." }

if ((Test-Path $cfgTarget) -and (Test-Path $cfgNew)) {
    $old = Get-Content $cfgTarget -Raw | ConvertFrom-Json
    $new = Get-Content $cfgNew -Raw | ConvertFrom-Json
    Merge-Missing $old $new
    $old | ConvertTo-Json -Depth 10 | Set-Content $cfgTarget -Encoding UTF8
}
elseif (-not (Test-Path $cfgTarget)) {
    Copy-Item $cfgNew $cfgTarget
    Write-Warning "Нямаше appsettings.json - сложен е стандартният. Попълни го!"
}

Get-ChildItem $Target -Recurse -File | Unblock-File

if (-not $NoService) {
    if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
        Start-Service -Name $service
        Write-Host ""
        Get-Service -Name $service | Format-Table Name, Status -AutoSize
    }
}
$global:LASTEXITCODE = 0   # robocopy връща 1-7 при успех; да не излиза скриптът с "грешка"
Write-Host "Обновяването приключи." -ForegroundColor Green

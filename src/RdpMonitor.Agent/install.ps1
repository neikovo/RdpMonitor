#Requires -RunAsAdministrator
<#
    Инсталира RDP Monitor Agent като Windows Service.
    Преди да пуснеш това, редактирай appsettings.json в тази папка:
      - Agent:ServerUrl          -> адресът на централния сървър (https://...:5443)
      - Agent:EnrollmentToken    -> трябва да съвпада с Agent:EnrollmentToken в appsettings.json на сървъра
      - Agent:AllowInsecureTls   -> постави true, ако сървърът използва самоподписан сертификат
#>

$ErrorActionPreference = "Stop"
$serviceName = "RdpMonitorAgent"
$exePath = Join-Path $PSScriptRoot "RdpMonitorAgent.exe"

if (-not (Test-Path $exePath)) {
    throw "Не намирам $exePath - изпълни install.ps1 от папката, в която е публикуван агентът."
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Услугата $serviceName вече съществува - спирам я преди преинсталация..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $serviceName `
    -BinaryPathName "`"$exePath`"" `
    -DisplayName "RDP Monitor Agent" `
    -Description "Следи за RDP сесии на този сървър и изпраща информация към централния RDP Monitor сървър." `
    -StartupType Automatic | Out-Null

# Рестартирай автоматично при срив (например ако сървърът временно е недостъпен при старт)
sc.exe failure $serviceName reset=86400 actions=restart/60000/restart/60000/restart/60000 | Out-Null

Start-Service -Name $serviceName

Write-Host ""
Write-Host "RDP Monitor Agent е инсталиран и стартиран (услуга: $serviceName)." -ForegroundColor Green
Write-Host "Логове: Event Viewer -> Windows Logs -> Application (source: 'RdpMonitor Agent')"

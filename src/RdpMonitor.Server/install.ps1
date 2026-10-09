#Requires -RunAsAdministrator
<#
    Инсталира RDP Monitor Server като Windows Service и отваря нужния firewall порт.
    Преди да пуснеш това, редактирай appsettings.json в тази папка:
      - Admin:Username / Admin:Password   -> вход в уеб таблото
      - Agent:EnrollmentToken             -> споделен токен, който агентите трябва да ползват при регистрация
      - Smtp:*                            -> настройки на пощенския сървър за изпращане на аларми
      - Kestrel:Endpoints:Https:Url       -> адрес/порт (по подразбиране https://0.0.0.0:5443)
#>

param(
    [int]$FirewallPort = 5443
)

$ErrorActionPreference = "Stop"
$serviceName = "RdpMonitorServer"
$exePath = Join-Path $PSScriptRoot "RdpMonitorServer.exe"

if (-not (Test-Path $exePath)) {
    throw "Не намирам $exePath - изпълни install.ps1 от папката, в която е публикуван сървърът."
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
    -DisplayName "RDP Monitor Server" `
    -Description "Централен сървър - приема данни от RDP Monitor агентите, изпраща имейл аларми и показва уеб табло." `
    -StartupType Automatic | Out-Null

sc.exe failure $serviceName reset=86400 actions=restart/60000/restart/60000/restart/60000 | Out-Null

$ruleName = "RDP Monitor Server ($FirewallPort/TCP)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $FirewallPort -Action Allow | Out-Null
    Write-Host "Отворен firewall порт $FirewallPort/TCP за входящи връзки от агентите."
}

Start-Service -Name $serviceName

Write-Host ""
Write-Host "RDP Monitor Server е инсталиран и стартиран (услуга: $serviceName)." -ForegroundColor Green
Write-Host "Уеб табло: https://<този-сървър>:$FirewallPort/"
Write-Host "Логове: Event Viewer -> Windows Logs -> Application, или конзолен изход при 'dotnet run' в режим за разработка."

#Requires -RunAsAdministrator
<#
    Премахва RDP Monitor Server (спира и изтрива Windows Service + firewall правилото).
    Не трие базата данни в ProgramData\RdpMonitor - изтрий я ръчно, ако искаш чисто премахване.
#>

param(
    [int]$FirewallPort = 5443
)

$ErrorActionPreference = "Stop"
$serviceName = "RdpMonitorServer"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}
else {
    Write-Host "Услугата $serviceName не е инсталирана."
}

$ruleName = "RDP Monitor Server ($FirewallPort/TCP)"
Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

Write-Host "RDP Monitor Server е премахнат." -ForegroundColor Green
Write-Host "Базата данни (ProgramData\RdpMonitor\rdpmonitor.db) е запазена - изтрий я ръчно при нужда."

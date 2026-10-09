#Requires -RunAsAdministrator
<#
    Removes RDP Monitor Server (stops and deletes the Windows service and the firewall rule).
    Does not delete the database in ProgramData\RdpMonitor - delete it manually for a full cleanup.
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
    Write-Host "Service $serviceName is not installed."
}

$ruleName = "RDP Monitor Server ($FirewallPort/TCP)"
Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

Write-Host "RDP Monitor Server has been removed." -ForegroundColor Green
Write-Host "The database (ProgramData\RdpMonitor\rdpmonitor.db) was kept - delete it manually if needed."

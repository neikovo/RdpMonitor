#Requires -RunAsAdministrator
<#
    Removes RDP Monitor Agent (stops and deletes the Windows service).
    Does not delete appsettings.json or the agent's local state in ProgramData\RdpMonitor.
#>

$ErrorActionPreference = "Stop"
$serviceName = "RdpMonitorAgent"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service $serviceName is not installed - nothing to remove."
    exit 0
}

Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
sc.exe delete $serviceName | Out-Null

Write-Host "RDP Monitor Agent has been removed." -ForegroundColor Green

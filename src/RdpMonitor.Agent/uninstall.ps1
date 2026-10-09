#Requires -RunAsAdministrator
<#
    Премахва RDP Monitor Agent (спира и изтрива Windows Service).
    Не трие appsettings.json нито локалната опашка от събития в ProgramData\RdpMonitor.
#>

$ErrorActionPreference = "Stop"
$serviceName = "RdpMonitorAgent"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Услугата $serviceName не е инсталирана - няма какво да се премахне."
    exit 0
}

Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
sc.exe delete $serviceName | Out-Null

Write-Host "RDP Monitor Agent е премахнат." -ForegroundColor Green

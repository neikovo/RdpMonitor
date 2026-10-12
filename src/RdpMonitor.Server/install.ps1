#Requires -RunAsAdministrator
<#
    Installs RDP Monitor Server as a Windows service and opens the required firewall port.
    Before running this, edit appsettings.json in this folder:
      - Admin:Username / Admin:Password   -> web dashboard login
      - Agent:EnrollmentToken             -> shared token the agents use to register
      - Smtp:*                            -> mail server used to send alerts
      - Kestrel:Endpoints:Https:Url       -> address/port (default https://0.0.0.0:5443)
#>

param(
    [int]$FirewallPort = 5443,
    [switch]$NoFolderLock   # do not restrict the program folder to SYSTEM and Administrators
)

$ErrorActionPreference = "Stop"
$serviceName = "RdpMonitorServer"
$exePath = Join-Path $PSScriptRoot "RdpMonitorServer.exe"

if (-not (Test-Path $exePath)) {
    throw "Cannot find $exePath - run install.ps1 from the folder the server was extracted to."
}

# Lock the program folder. A folder created directly under C:\ is writable by every signed-in user by default;
# that would let a normal user replace the program (it runs as SYSTEM) or read appsettings.json (passwords/tokens).
# Only SYSTEM and Administrators get access.
if (-not $NoFolderLock) {
    icacls $PSScriptRoot /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
    Write-Host "Folder permissions locked to SYSTEM and Administrators: $PSScriptRoot"
}
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service $serviceName already exists - stopping and removing it before reinstalling..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $serviceName `
    -BinaryPathName "`"$exePath`"" `
    -DisplayName "RDP Monitor Server" `
    -Description "Central server - receives data from RDP Monitor agents, sends e-mail alerts and serves the web dashboard." `
    -StartupType Automatic | Out-Null

sc.exe failure $serviceName reset=86400 actions=restart/60000/restart/60000/restart/60000 | Out-Null

$ruleName = "RDP Monitor Server ($FirewallPort/TCP)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $FirewallPort -Action Allow | Out-Null
    Write-Host "Opened firewall port $FirewallPort/TCP for incoming agent connections."
}

Start-Service -Name $serviceName

Write-Host ""
Write-Host "RDP Monitor Server is installed and running (service: $serviceName)." -ForegroundColor Green
Write-Host "Web dashboard: https://<this-server>:$FirewallPort/"
Write-Host "Logs: Event Viewer -> Windows Logs -> Application."

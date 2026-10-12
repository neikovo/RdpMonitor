#Requires -RunAsAdministrator
<#
    Installs RDP Monitor Agent as a Windows service.
    Before running this, edit appsettings.json in this folder:
      - Agent:ServerUrl          -> address of the central server (https://...:5443)
      - Agent:EnrollmentToken    -> must match Agent:EnrollmentToken in the server's appsettings.json
      - Agent:AllowInsecureTls   -> set to true if the server uses a self-signed certificate
#>

param(
    [switch]$NoFolderLock   # do not restrict the program folder to SYSTEM and Administrators
)

$ErrorActionPreference = "Stop"
$serviceName = "RdpMonitorAgent"
$exePath = Join-Path $PSScriptRoot "RdpMonitorAgent.exe"

if (-not (Test-Path $exePath)) {
    throw "Cannot find $exePath - run install.ps1 from the folder the agent was extracted to."
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
    -DisplayName "RDP Monitor Agent" `
    -Description "Watches RDP sessions and suspicious processes on this server and reports them to the central RDP Monitor server." `
    -StartupType Automatic | Out-Null

# Restart automatically on failure (for example if the server is temporarily unreachable at startup)
sc.exe failure $serviceName reset=86400 actions=restart/60000/restart/60000/restart/60000 | Out-Null

Start-Service -Name $serviceName

Write-Host ""
Write-Host "RDP Monitor Agent is installed and running (service: $serviceName)." -ForegroundColor Green
Write-Host "Logs: Event Viewer -> Windows Logs -> Application (source: 'RdpMonitor Agent')"

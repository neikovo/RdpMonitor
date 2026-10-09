<#
    Updates an already installed RDP Monitor Agent or Server with this newer version.
    Keeps your appsettings.json (and only adds new settings that are missing from it).
    Does not touch the database, the certificate or the agent identity (they live in C:\ProgramData\RdpMonitor).

    How to use:
      1. Extract the new zip into a NEW folder (e.g. C:\update\RdpMonitor.Agent).
      2. From that new folder, in PowerShell as administrator:
            .\update.ps1 -Target C:\RdpMonitor.Agent
         (for the server: -Target C:\RdpMonitor.Server)

    What it does:  stops the service -> backs up appsettings.json -> copies the new files ->
                   adds missing settings -> removes the "downloaded from the internet" mark -> starts the service.
#>

param(
    [Parameter(Mandatory = $true)][string]$Target,
    [switch]$NoService   # test only: does not touch the service
)

$ErrorActionPreference = "Stop"
$src = $PSScriptRoot

if (-not $NoService) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw "Run this script in PowerShell as administrator." }
}

if (Test-Path (Join-Path $src "RdpMonitorAgent.exe"))      { $service = "RdpMonitorAgent";  $exe = "RdpMonitorAgent.exe" }
elseif (Test-Path (Join-Path $src "RdpMonitorServer.exe")) { $service = "RdpMonitorServer"; $exe = "RdpMonitorServer.exe" }
else { throw "There is no RdpMonitorAgent.exe or RdpMonitorServer.exe in $src - run the script from the extracted NEW folder." }

if (-not (Test-Path (Join-Path $Target $exe))) {
    throw "Cannot find $exe in $Target. Check -Target (the folder of the ALREADY installed version)."
}
if ((Resolve-Path $src).Path -eq (Resolve-Path $Target).Path) { throw "The new and the old folder cannot be the same." }

function Merge-Missing($old, $new) {
    # adds to $old only the keys it does not have; the user's values are never changed
    foreach ($prop in $new.PSObject.Properties) {
        if ($null -eq $old.PSObject.Properties[$prop.Name]) {
            $old | Add-Member -NotePropertyName $prop.Name -NotePropertyValue $prop.Value
            Write-Host "  + new setting: $($prop.Name)"
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
    if ($svc) { Write-Host "Stopping $service ..."; Stop-Service -Name $service -Force; $svc.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30)) }
    else { Write-Warning "Service $service is not installed. The files will be updated; run install.ps1 afterwards." }
}

if (Test-Path $cfgTarget) {
    $backup = Join-Path $Target "appsettings.backup-$stamp.json"
    Copy-Item $cfgTarget $backup
    Write-Host "Settings backup: $backup"
}

Write-Host "Copying the new files ..."
robocopy $src $Target /E /XF appsettings.json "appsettings.backup-*.json" /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed (code $LASTEXITCODE)." }

if ((Test-Path $cfgTarget) -and (Test-Path $cfgNew)) {
    $old = Get-Content $cfgTarget -Raw | ConvertFrom-Json
    $new = Get-Content $cfgNew -Raw | ConvertFrom-Json
    Merge-Missing $old $new
    $old | ConvertTo-Json -Depth 10 | Set-Content $cfgTarget -Encoding UTF8
}
elseif (-not (Test-Path $cfgTarget)) {
    Copy-Item $cfgNew $cfgTarget
    Write-Warning "There was no appsettings.json - the default one was copied. Fill it in!"
}

Get-ChildItem $Target -Recurse -File | Unblock-File

if (-not $NoService) {
    if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
        Start-Service -Name $service
        Write-Host ""
        Get-Service -Name $service | Format-Table Name, Status -AutoSize
    }
}
$global:LASTEXITCODE = 0   # robocopy returns 1-7 on success; do not make the script look like it failed
Write-Host "Update finished." -ForegroundColor Green

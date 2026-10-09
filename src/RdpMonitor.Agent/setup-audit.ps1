#Requires -RunAsAdministrator
<#
    Turns on the Windows auditing that the agent reads its audit events from:
      - "Process Creation" (event 4688) + command line in it  -> process audit
      - "File System" (event 4663) + audit entry on the chosen folders -> file audit ("who and when")

    Folders are read from appsettings.json (Audit:FileFolders), located next to this script.

    What it changes (it shows the list and asks for confirmation):
      1. Audit policy:  auditpol  (Process Creation, File System - success only)
      2. Registry:      ProcessCreationIncludeCmdLine_Enabled = 1
      3. An audit entry (SACL) for Everyone on each chosen folder: only records changes/deletes.
         It does NOT change the access permissions (DACL) of the folders.
    If the machine is in a domain and audit policy is set by Group Policy, a GPO may overwrite item 1.

    Usage:
        .\setup-audit.ps1            # configure
        .\setup-audit.ps1 -Remove    # remove the audit entries from the folders (audit policy is left as is - see the end)
        .\setup-audit.ps1 -Yes       # no confirmation prompt
#>

param(
    [switch]$Remove,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

$cfgPath = Join-Path $PSScriptRoot "appsettings.json"
if (-not (Test-Path $cfgPath)) { throw "Cannot find $cfgPath" }
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
$folders = @($cfg.Audit.FileFolders | Where-Object { $_ })
$procAudit = [bool]$cfg.Audit.MonitorProcesses

$guidProcess = "{0CCE922B-69AE-11D9-BED3-505054503030}"   # Process Creation
$guidFile    = "{0CCE921D-69AE-11D9-BED3-505054503030}"   # File System
$rights = [System.Security.AccessControl.FileSystemRights]"CreateFiles, WriteData, AppendData, Delete, DeleteSubdirectoriesAndFiles"
$everyone = New-Object System.Security.Principal.SecurityIdentifier("S-1-1-0")

function New-AuditRule {
    New-Object System.Security.AccessControl.FileSystemAuditRule(
        $everyone, $rights,
        [System.Security.AccessControl.InheritanceFlags]"ContainerInherit, ObjectInherit",
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AuditFlags]::Success)
}

if ($Remove) {
    Write-Host "The audit entries (for Everyone) will be removed from these folders:" -ForegroundColor Yellow
    $folders | ForEach-Object { Write-Host "  $_" }
    if (-not $Yes -and (Read-Host "Continue? (y/n)") -ne "y") { return }
    foreach ($f in $folders) {
        if (-not (Test-Path $f)) { Write-Warning "Missing: $f"; continue }
        $acl = Get-Acl -Path $f -Audit
        $acl.RemoveAuditRuleAll((New-AuditRule))
        Set-Acl -Path $f -AclObject $acl
        Write-Host "Audit entry removed: $f"
    }
    Write-Host "`nThe audit policy (auditpol) was NOT changed, because other tools may rely on it."
    Write-Host "To turn it off manually: auditpol /set /subcategory:`"$guidFile`" /success:disable"
    return
}

Write-Host "The following changes will be made on this machine:" -ForegroundColor Yellow
if ($procAudit) {
    Write-Host "  1. auditpol: enable 'Process Creation' auditing (success) and put the command line into event 4688"
}
if ($folders.Count -gt 0) {
    Write-Host "  2. auditpol: enable 'File System' auditing (success)"
    Write-Host "  3. Audit entry (records events only, does not change permissions) on the folders:"
    $folders | ForEach-Object { Write-Host "       $_" }
}
if (-not $procAudit -and $folders.Count -eq 0) {
    Write-Host "Nothing to configure: Audit:MonitorProcesses is false and Audit:FileFolders is empty in appsettings.json." -ForegroundColor Yellow
    return
}
if (-not $Yes -and (Read-Host "Continue? (y/n)") -ne "y") { Write-Host "Cancelled."; return }

if ($procAudit) {
    auditpol /set /subcategory:"$guidProcess" /success:enable | Out-Null
    $key = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit"
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    New-ItemProperty -Path $key -Name "ProcessCreationIncludeCmdLine_Enabled" -Value 1 -PropertyType DWord -Force | Out-Null
    Write-Host "OK: process auditing + command line" -ForegroundColor Green
}

if ($folders.Count -gt 0) {
    auditpol /set /subcategory:"$guidFile" /success:enable | Out-Null
    foreach ($f in $folders) {
        if (-not (Test-Path $f)) { Write-Warning "Missing, skipping: $f"; continue }
        $acl = Get-Acl -Path $f -Audit
        $acl.AddAuditRule((New-AuditRule))
        Set-Acl -Path $f -AclObject $acl
        Write-Host "OK: audit entry on $f" -ForegroundColor Green
    }
}

Write-Host "`nDone. Restart the agent so it reads the settings:  Restart-Service RdpMonitorAgent"

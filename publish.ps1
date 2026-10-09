<#
    Builds RDP Monitor into ready-to-distribute packages in the dist\ folder:
        dist\RdpMonitor.Server.zip   (for the central server)
        dist\RdpMonitor.Agent.zip    (for every monitored Windows Server)
        dist\INSTALL.md              (instructions)
    The packages are self-contained (no .NET needed on the target machine) and are NOT a single-file exe:
    the single file turned out to be slow/blocked by the protection software on some servers.

    Run from a machine with the .NET 8 SDK:   .\publish.ps1
#>

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

foreach ($name in "Server", "Agent") {
    Write-Host "`nPublishing RdpMonitor.$name ..." -ForegroundColor Cyan
    $out = Join-Path $dist "RdpMonitor.$name"
    dotnet publish (Join-Path $root "src\RdpMonitor.$name\RdpMonitor.$name.csproj") `
        -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "Publishing $name failed." }
    Compress-Archive -Path $out -DestinationPath (Join-Path $dist "RdpMonitor.$name.zip") -Force
    Remove-Item $out -Recurse -Force
}

Copy-Item (Join-Path $root "INSTALL.md") $dist -ErrorAction SilentlyContinue

Write-Host "`nDone:" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize | Out-Host
Write-Host "Follow dist\INSTALL.md."

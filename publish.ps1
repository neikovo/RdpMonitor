<#
    Пакетира RDP Monitor в готови за разпространение пакети в папка dist\ :
        dist\RdpMonitor.Server.zip   (за централния сървър)
        dist\RdpMonitor.Agent.zip    (за всеки наблюдаван Windows Server)
        dist\ИНСТАЛАЦИЯ.md           (инструкции)
    Пакетите са self-contained (не изискват .NET на целевата машина) и са БЕЗ единичен .exe -
    единичният файл се оказа бавен/блокиран от защитата на някои сървъри.

    Пусни от машина с .NET 8 SDK:   .\publish.ps1
#>

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

foreach ($name in "Server", "Agent") {
    Write-Host "`nПубликувам RdpMonitor.$name ..." -ForegroundColor Cyan
    $out = Join-Path $dist "RdpMonitor.$name"
    dotnet publish (Join-Path $root "src\RdpMonitor.$name\RdpMonitor.$name.csproj") `
        -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "Публикуването на $name се провали." }
    Compress-Archive -Path $out -DestinationPath (Join-Path $dist "RdpMonitor.$name.zip") -Force
    Remove-Item $out -Recurse -Force
}

Copy-Item (Join-Path $root "ИНСТАЛАЦИЯ.md") $dist -ErrorAction SilentlyContinue

Write-Host "`nГотово:" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
Write-Host "Следвай dist\ИНСТАЛАЦИЯ.md."

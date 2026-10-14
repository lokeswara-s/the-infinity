# Publishes a self-contained win-x64 build, a zip of that folder, and a per-user MSI.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist\win-x64"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish (Join-Path $root "src\TheInfinity.App\TheInfinity.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $dist
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

dotnet build (Join-Path $PSScriptRoot "TheInfinity.Installer.wixproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "The MSI build failed." }
Write-Host "Per-user installer: dist\TheInfinity-1.0.0.msi"

$zip = Join-Path $root "dist\TheInfinity-1.0.0-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$dist\*" -DestinationPath $zip
Write-Host "Zip: dist\TheInfinity-1.0.0-win-x64.zip"

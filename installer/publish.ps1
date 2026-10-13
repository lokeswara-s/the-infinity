# Publishes a self-contained win-x64 build and, when the Windows SDK is present,
# an unsigned MSIX. Install the unpackaged build with installer\Install.ps1.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist\win-x64"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish (Join-Path $root "src\TheInfinity.App\TheInfinity.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $dist

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($makeappx) {
    $layout = Join-Path $root "dist\msix-layout"
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    New-Item -ItemType Directory -Path $layout | Out-Null
    Copy-Item (Join-Path $root "packaging\Package.appxmanifest") (Join-Path $layout "AppxManifest.xml")
    Copy-Item "$dist\*" $layout -Recurse
    $logos = Join-Path $root "packaging\Assets"
    if (-not (Test-Path $logos)) {
        Write-Host "packaging\Assets is missing, so the MSIX was not built. Add the logos named in Package.appxmanifest."
        return
    }
    Copy-Item $logos (Join-Path $layout "Assets") -Recurse
    & $makeappx.FullName pack /d $layout /p (Join-Path $root "dist\TheInfinity.msix") /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE" }
    Write-Host "Unsigned package: dist\TheInfinity.msix"
    Write-Host "Install requires a signing certificate or Developer Mode sideload."
} else {
    Write-Host "Windows SDK makeappx.exe was not found. Unpackaged build is in dist\win-x64."
}

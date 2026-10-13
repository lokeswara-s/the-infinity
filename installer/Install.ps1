# Copies the Release build into LocalAppData and adds a Start Menu shortcut.
# Run installer\publish.ps1 first for a self-contained folder, or build Release and pass -FromBuild.
param([switch]$FromBuild)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = if ($FromBuild) {
    Join-Path $root "src\TheInfinity.App\bin\Release\net10.0-windows"
} else {
    Join-Path $root "dist\win-x64"
}
if (-not (Test-Path (Join-Path $source "TheInfinity.exe"))) {
    throw "TheInfinity.exe was not found in $source. Build Release or run publish.ps1."
}
$dest = Join-Path $env:LOCALAPPDATA "TheInfinity\app"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item "$source\*" $dest -Recurse -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\The Infinity.lnk"))
$shortcut.TargetPath = Join-Path $dest "TheInfinity.exe"
$shortcut.WorkingDirectory = $dest
$shortcut.Description = "Spatial window workspace"
$shortcut.IconLocation = (Join-Path $dest "TheInfinity.exe") + ",0"
$shortcut.Save()
Write-Host "Installed to $dest"

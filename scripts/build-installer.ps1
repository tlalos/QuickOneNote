# Build the QuickOneNote setup.exe with Inno Setup.
#   1. publish the self-contained x86 app into dist\
#   2. export the app icon (installer\app.ico)
#   3. compile installer\QuickOneNote.iss  ->  dist\QuickOneNote-Setup-<version>.exe
#
# Requires Inno Setup 6 (ISCC.exe). Install with:  winget install JRSoftware.InnoSetup
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
param([string] $Dotnet = "dotnet")

$ErrorActionPreference = "Stop"
$root   = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root "QuickOneNote.csproj"
$ver    = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $ver) { throw "Could not read <Version> from $csproj" }

$dist = Join-Path $root "dist"
$iss  = Join-Path $root "installer\QuickOneNote.iss"
$ico  = Join-Path $root "installer\app.ico"

# 1. self-contained publish
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
& $Dotnet publish $csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 2. export the icon from the freshly built exe
$exe = Join-Path $dist "QuickOneNote.exe"
& $exe "--exporticon" $ico | Out-Null
if (-not (Test-Path $ico)) { throw "icon export failed" }

# 3. find ISCC and compile
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup (ISCC.exe) not found. Install with: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$ver" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Join-Path $dist "QuickOneNote-Setup-$ver.exe"
Write-Host ""
Write-Host "Installer: $setup"
Write-Host "Attach to the release:  gh release upload v$ver `"$setup`" --repo tlalos/QuickOneNote"

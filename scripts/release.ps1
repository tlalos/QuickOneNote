# One-shot release builder: produces BOTH release assets into dist\ —
#   quickonenote-update-<version>.zip   (auto-update payload; files at zip root)
#   QuickOneNote-Setup-<version>.exe     (per-user installer)
#
# Requires Inno Setup 6 (ISCC.exe): winget install JRSoftware.InnoSetup
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\release.ps1
param([string] $Dotnet = "dotnet")

$ErrorActionPreference = "Stop"
$root   = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root "QuickOneNote.csproj"
$ver    = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $ver) { throw "Could not read <Version> from $csproj" }

$dist = Join-Path $root "dist"
$ico  = Join-Path $root "installer\app.ico"
$iss  = Join-Path $root "installer\QuickOneNote.iss"

# 1. self-contained publish
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
& $Dotnet publish $csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

@"
QuickOneNote - standalone build (self-contained x86, no .NET install needed).
Run QuickOneNote.exe. For a PC without desktop OneNote, use Cloud mode in Settings.
Data is stored per-user in %APPDATA%\QuickOneNote.
"@ | Out-File (Join-Path $dist "README.txt") -Encoding utf8

# 2. update zip (contents at root) — built BEFORE the installer so it holds only the app payload
$zip = Join-Path $dist "quickonenote-update-$ver.zip"
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -Force

# 3. export the icon, then compile the installer (its [Files] excludes *.zip and the setup exe)
& (Join-Path $dist "QuickOneNote.exe") "--exporticon" $ico | Out-Null
if (-not (Test-Path $ico)) { throw "icon export failed" }
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
Write-Host "Version: $ver"
Write-Host "  update zip: $zip"
Write-Host "  installer:  $setup"
Write-Host ""
Write-Host "Release:  gh release create v$ver `"$zip`" `"$setup`" --repo tlalos/QuickOneNote --title v$ver --notes-file <notes>"

# Build the self-contained x86 update package for QuickOneNote and zip its contents (files at the
# zip root) so the auto-updater extracts them flat over the install dir.
#
# Usage:  pwsh scripts/package-update.ps1        (or: powershell -File scripts\package-update.ps1)
# Output: dist\quickonenote-update-<version>.zip
param([string] $Dotnet = "dotnet")

$ErrorActionPreference = "Stop"
$root   = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root "QuickOneNote.csproj"
$ver    = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $ver) { throw "Could not read <Version> from $csproj" }

$dist = Join-Path $root "dist"
$out  = $dist                                   # the existing dist folder is the self-contained build
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

& $Dotnet publish $csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

@"
QuickOneNote - standalone build (self-contained x86, no .NET install needed).
Run QuickOneNote.exe. For a PC without desktop OneNote, use Cloud mode in Settings.
Data is stored per-user in %APPDATA%\QuickOneNote.
"@ | Out-File (Join-Path $out "README.txt") -Encoding utf8

$zip = Join-Path $dist "quickonenote-update-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force   # contents at zip root

Write-Host "Version:      $ver"
Write-Host "Update asset: $zip"
Write-Host ""
Write-Host "Next: gh release create v$ver `"$zip`" --repo tlalos/QuickOneNote --title v$ver --notes `"What changed`""

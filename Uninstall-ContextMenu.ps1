<#
    Removes the "Add to OneNote" right-click entry created by Install-ContextMenu.ps1.
#>
$ErrorActionPreference = 'SilentlyContinue'

$targets = @('image', '.txt')
foreach ($t in $targets) {
    $base = "HKCU:\Software\Classes\SystemFileAssociations\$t\shell\QuickOneNote"
    if (Test-Path $base) {
        Remove-Item -Path $base -Recurse -Force
    }
}

Write-Host "Removed the 'Add to OneNote' right-click entry." -ForegroundColor Green

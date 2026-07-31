<#
    Adds an "Add to OneNote" entry to the right-click menu for image files and .txt files.
    This is per-user (HKCU) and needs no admin rights.

    Usage:
        ./Install-ContextMenu.ps1                 # uses the built Release/Debug exe
        ./Install-ContextMenu.ps1 -ExePath "C:\path\to\QuickOneNote.exe"
#>
param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

if (-not $ExePath) {
    $candidates = @(
        "$PSScriptRoot\bin\Release\net9.0-windows\QuickOneNote.exe",
        "$PSScriptRoot\bin\Debug\net9.0-windows\QuickOneNote.exe"
    )
    $ExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $ExePath -or -not (Test-Path $ExePath)) {
    Write-Error "Could not find QuickOneNote.exe. Build the project first, or pass -ExePath."
}

$ExePath = (Resolve-Path $ExePath).Path
$command = "`"$ExePath`" `"%1`""

# 'image' is a perceived-type group that covers .png/.jpg/.bmp/.gif etc.; also handle .txt.
$targets = @('image', '.txt')

foreach ($t in $targets) {
    $base = "HKCU:\Software\Classes\SystemFileAssociations\$t\shell\QuickOneNote"
    New-Item -Path $base -Force | Out-Null
    Set-ItemProperty -Path $base -Name '(default)' -Value 'Add to OneNote'
    Set-ItemProperty -Path $base -Name 'Icon' -Value $ExePath
    $cmd = "$base\command"
    New-Item -Path $cmd -Force | Out-Null
    Set-ItemProperty -Path $cmd -Name '(default)' -Value $command
}

Write-Host "Installed 'Add to OneNote' right-click entry for images and .txt files." -ForegroundColor Green
Write-Host "Target: $ExePath"

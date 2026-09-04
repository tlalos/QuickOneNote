; Inno Setup script for QuickOneNote — a per-user install (no admin) so the built-in auto-updater
; can overwrite files in place. Build with scripts\build-installer.ps1 (passes /DAppVersion=...).

#ifndef AppVersion
  #define AppVersion "1.4.0"
#endif

#define AppName "QuickOneNote"
#define AppExe "QuickOneNote.exe"
#define AppPublisher "Anastasios Lalos"
#define AppUrl "https://github.com/tlalos/QuickOneNote"

[Setup]
; A stable AppId keeps upgrades/uninstall linked across versions — do not change it.
AppId={{8F2A1C74-9B3E-4D5A-B6C1-2E7F0A9D4C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases

; Per-user install: no admin rights, and the install dir stays writable so auto-update works.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

; The app is 32-bit, but the installer itself runs fine on 64-bit Windows.
OutputDir=..\dist
OutputBaseFilename=QuickOneNote-Setup-{#AppVersion}
SetupIconFile=app.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; (No ArchitecturesAllowed — the 32-bit app installs and runs on x86/x64/arm64.)

; Close a running QuickOneNote (via Restart Manager) before copying, and don't auto-restart it —
; the [Run] section relaunches a single instance instead.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; The self-contained publish output (produced into dist\ by the build script). Files land flat in
; the install dir, matching what the auto-updater expects.
Source: "..\dist\*"; DestDir: "{app}"; Excludes: "*.zip,QuickOneNote-Setup-*.exe,update.log"; \
  Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

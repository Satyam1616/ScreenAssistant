; Inno Setup script for ScreenAssistant.
; Produces a single click-to-run installer: ScreenAssistant-Setup.exe
;
; Build order (see SHARING.md):
;   1) dotnet publish -c Release -r win-x64 --self-contained true \
;        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
;        -o dist-selfcontained
;   2) iscc installer\ScreenAssistant.iss
;
; The published exe bundles .NET, so recipients need no runtime. The first
; launch shows the setup screen (Free Cloud vs Local Ollama).

#define AppName "ScreenAssistant"
#define AppVersion "1.0.0"
#define AppExe "ScreenAssistant.exe"
#define AppPublisher "ScreenAssistant"

[Setup]
; A stable AppId keeps upgrades/uninstall clean. Replace if you fork this.
AppId={{B7B2F4E1-9C4A-4E5D-9A0F-2C7E1D3A6F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=ScreenAssistant-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Per-user install: no admin/UAC prompt for recipients, and the per-user
; autostart + desktop shortcut below then apply to the right user. {autopf}
; resolves to %LocalAppData%\Programs in this mode.
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
SetupIconFile=..\appicon.ico
UninstallDisplayIcon={app}\{#AppExe}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"
Name: "startup"; Description: "Start ScreenAssistant when Windows starts"; GroupDescription: "Startup:"

[Files]
; Copy everything the self-contained publish produced (usually just the exe).
Source: "..\dist-selfcontained\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Per-user, visible autostart (HKCU Run) — no stealth.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "ScreenAssistant"; \
    ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Launch after install → triggers the first-run setup screen.
Filename: "{app}\{#AppExe}"; Description: "Launch ScreenAssistant"; \
    Flags: nowait postinstall skipifsilent

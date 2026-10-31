#ifndef AppVersion
  #define AppVersion "1.4.0"
#endif
#define AppName "HISA Quản lý kho"
#define AppExeName "HISA Kho Vat Tu.exe"

[Setup]
AppId={{5A40F56E-F5D4-4E66-8C9B-00EC0C11C083}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=HISA
DefaultDirName={autopf}\HISA Kho Vat Tu
DefaultGroupName=HISA Kho Vat Tu
OutputDir=..\installer-output
OutputBaseFilename=HISA-Kho-Vat-Tu-{#AppVersion}-Setup
SetupIconFile=..\src\Hisa.App\Assets\hisa.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=yes
DisableProgramGroupPage=yes
ChangesAssociations=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Trình cài đặt HISA Quản lý kho

[Languages]
Name: "vietnamese"; MessagesFile: "Vietnamese.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình Desktop"; GroupDescription: "Tùy chọn:"; Flags: checkedonce

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\HISA Quản lý kho"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\HISA Quản lý kho"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Mở HISA Quản lý kho"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: ShouldRelaunchAfterUpgrade

[Code]
function ShouldRelaunchAfterUpgrade(): Boolean;
begin
  { Silent CI installation must not launch a desktop app. Only user-approved
    HISA in-app upgrades supply /HISAUPGRADE=1 and relaunch on completion. }
  Result := WizardSilent and (ExpandConstant('{param:HISAUPGRADE|0}') = '1');
end;

// No [UninstallDelete] for Documents: application data and backups survive upgrades/uninstall.

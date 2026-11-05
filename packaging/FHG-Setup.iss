; FHG Windows x64 installer — per-user installation, explicit directory picker.
; Build using ISCC.exe on a Windows GitHub Actions runner after stage is ready.
; Never copy sources, build scripts, .py or setup input/credentials into app dir.
#define FhgVersion "0.6.0-preview"
[Setup]
AppId={{5BD4AA38-6319-4C59-B0D8-4706B994208F}
AppName=FHG Browser
AppVersion={#FhgVersion}
AppVerName=FHG Browser {#FhgVersion}
AppPublisher=FHG Community
AppPublisherURL=https://github.com/sayyedalimrj/FHG
SourceDir=..
DefaultDirName={localappdata}\Programs\FHG Browser
DefaultGroupName=FHG Browser
UninstallDisplayIcon={app}\FHG.exe
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
AllowNoIcons=no
WizardStyle=modern
WizardSizePercent=110
ShowLanguageDialog=no
Compression=lzma2
SolidCompression=yes
OutputDir=installer-output
OutputBaseFilename=FHG-Setup-Windows-x64-{#FhgVersion}
SetupIconFile=native\original-ui\Assets\FHG.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableWelcomePage=no
DisableReadyMemo=no
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "stage\FHG.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "stage\FHGGuard.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "stage\FHGAccountManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "stage\FHGNativeHost.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "stage\Firefox\*"; DestDir: "{app}\Firefox"; Excludes: "*.py,*.pyc,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "stage\Hiddify\*"; DestDir: "{app}\Hiddify"; Excludes: "*.py,*.pyc,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "stage\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userdesktop}\FHG Firefox"; Filename: "{app}\FHG.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Firefox\firefox.exe"
Name: "{userdesktop}\FHG Proxy"; Filename: "{app}\FHG.exe"; Parameters: "--proxy-only"; WorkingDir: "{app}"; IconFilename: "{app}\Hiddify\FHGProxy.exe"
Name: "{userdesktop}\FHG Accounts"; Filename: "{app}\FHGAccountManager.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FHGAccountManager.exe"
Name: "{userprograms}\FHG Firefox"; Filename: "{app}\FHG.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Firefox\firefox.exe"
Name: "{userprograms}\FHG Proxy"; Filename: "{app}\FHG.exe"; Parameters: "--proxy-only"; WorkingDir: "{app}"; IconFilename: "{app}\Hiddify\FHGProxy.exe"
Name: "{userprograms}\FHG Accounts"; Filename: "{app}\FHGAccountManager.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\FHGAccountManager.exe"; Description: "Open FHG Accounts and complete your initial setup"; Flags: nowait postinstall skipifsilent

; Register only a properly signed FHG extension's native messaging host.
; The manifest contains no credentials. A prior Mozilla extension's data is not deleted.
[Code]
const
  HostRegistry = 'Software\Mozilla\NativeMessagingHosts\fhg.authenticator';

procedure CurStepChanged(CurStep: TSetupStep);
var
  InstallPath, HostExe, ManifestPath, Payload, SignedAddon: String;
begin
  if CurStep <> ssPostInstall then Exit;
  InstallPath := ExpandConstant('{app}');
  SignedAddon := AddBackslash(InstallPath) + 'Firefox\distribution\fhg-auto-authenticator.xpi';
  HostExe := AddBackslash(InstallPath) + 'FHGNativeHost.exe';
  if not FileExists(SignedAddon) then Exit;
  if not FileExists(HostExe) then
    RaiseException('Signed FHG Authenticator included without native host');
  ManifestPath := AddBackslash(InstallPath) + 'FHGNativeHost.json';
  Payload := '{"name":"fhg.authenticator","description":"Local FHG DPAPI vault bridge","path":"' +
    StringChangeEx(HostExe, '\', '\\', True) +
    '","type":"stdio","allowed_extensions":["fhg-authenticator@fhg.local"]}';
  if not SaveStringToFile(ManifestPath, Payload, False) then
    RaiseException('Cannot create FHG Authenticator native messaging manifest');
  if not RegWriteStringValue(HKCU, HostRegistry, '', ManifestPath) then
    RaiseException('Cannot register FHG Firefox native messaging host');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Prior: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  if RegQueryStringValue(HKCU, HostRegistry, '', Prior) then
    if CompareText(Prior, ExpandConstant('{app}\FHGNativeHost.json')) = 0 then
      RegDeleteKeyIncludingSubkeys(HKCU, HostRegistry);
end;

[UninstallDelete]
Type: files; Name: "{app}\FHGNativeHost.json"

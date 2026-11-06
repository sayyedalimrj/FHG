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
; Highest Inno Setup 6 built-in LZMA2 level.
; A 256 MiB dictionary needs roughly 3 GiB at build-time and
; 256 MiB at installation; keep within hosted Windows runner RAM.
Compression=lzma2/ultra64
SolidCompression=yes
LZMADictionarySize=262144
LZMANumFastBytes=273
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=1
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
Source: "stage\FHGAuthenticatorSync.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "stage\FHGLogin.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "stage\WebDriver\geckodriver.exe"; DestDir: "{app}\WebDriver"; Flags: ignoreversion
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

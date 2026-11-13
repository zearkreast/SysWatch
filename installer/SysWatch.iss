; Inno Setup 6 script for the SysWatch installer. Built by scripts/build-release.ps1, which passes
; AppVersion and the folder holding the published SysWatch.exe (PublishDir).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "SysWatch"
#define AppExe "SysWatch.exe"
#define AppUrl "https://github.com/zearkreast/SysWatch"
; Evergreen link to the latest .NET 8 Desktop Runtime installer; the release workflow checks it still resolves.
#define RuntimeUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
; Never change AppId: upgrades and the uninstaller find the installed copy by it.
AppId={{6F2C8B4E-3D1A-4E7B-9C55-2A9D0B7E51C3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=zearkreast
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; SysWatch itself runs elevated (sensor access, /kill, /shutdown), so it is installed for all users.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=SysWatch_x64-setup
SetupIconFile=..\Assets\syswatch.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard-large-164.bmp,wizard-large-328.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-110.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
tr.StartWithWindows=Windows açılınca SysWatch'ı başlat
en.StartWithWindows=Start SysWatch when Windows starts
tr.LaunchApp=SysWatch'ı şimdi başlat
en.LaunchApp=Start SysWatch now
tr.RuntimeTitle=.NET 8 Masaüstü Çalışma Zamanı
en.RuntimeTitle=.NET 8 Desktop Runtime
tr.RuntimeDesc=SysWatch'ın çalışması için Microsoft'un .NET 8 Masaüstü Çalışma Zamanı indirilip kuruluyor.
en.RuntimeDesc=SysWatch needs Microsoft's .NET 8 Desktop Runtime; it is being downloaded and installed.
tr.RuntimeFailed=.NET 8 Masaüstü Çalışma Zamanı kurulamadı (%1). SysWatch kurulacak, ancak çalışması için bu bileşeni https://dotnet.microsoft.com/download/dotnet/8.0 adresinden elle kurmanız gerekir.
en.RuntimeFailed=The .NET 8 Desktop Runtime could not be installed (%1). SysWatch will be installed, but it needs that component: install it from https://dotnet.microsoft.com/download/dotnet/8.0
tr.RemoveData=Ayarlar, geçmiş ve günlük de silinsin mi?%n%n%1
en.RemoveData=Also delete settings, history and the log?%n%n%1

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--enable-startup"; Flags: runhidden waituntilterminated; Tasks: startup
; runascurrentuser: the app requires elevation and the installer already has it, so no second UAC prompt.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN SysWatch /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function RuntimeInstalled: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
      begin
        Result := True;
        Exit;
      end;
end;

// The tray app holds its exe open; an upgrade or uninstall stops it first.
procedure StopSysWatch;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM SysWatch.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(CustomMessage('RuntimeTitle'), CustomMessage('RuntimeDesc'), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or RuntimeInstalled then Exit;
  DownloadPage.Clear;
  DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime-8-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime-8-x64.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Code)
        or ((Code <> 0) and (Code <> 3010) and (Code <> 1638)) then
        MsgBox(FmtMessage(CustomMessage('RuntimeFailed'), [IntToStr(Code)]), mbError, MB_OK);
    except
      MsgBox(FmtMessage(CustomMessage('RuntimeFailed'), [GetExceptionMessage]), mbError, MB_OK);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopSysWatch;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  StopSysWatch;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Data := ExpandConstant('{userappdata}\SysWatch');
  if DirExists(Data) and not UninstallSilent then
    if MsgBox(FmtMessage(CustomMessage('RemoveData'), [Data]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Data, True, True, True);
end;

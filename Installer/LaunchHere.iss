; Build with build-installer.ps1. Keep AppId stable for future upgrades.
#ifndef AppName
  #define AppName "LaunchHere"
#endif
#ifndef InstallerAppId
  #define InstallerAppId "{6BA2C7B6-6790-4311-B7ED-3C1CFAE67A87}"
#endif
#ifndef AppMutexName
  #define AppMutexName "Local\TaskbarMonitor.Utility"
#endif
#ifndef StartupValueName
  #define StartupValueName "LaunchHere"
#endif
#ifndef OutputFolder
  #define OutputFolder "..\.."
#endif
#ifndef AppFilesFolder
  #define AppFilesFolder ".."
#endif
#define AppVersion GetVersionNumbersString(AddBackslash(AppFilesFolder) + "LaunchHere.exe")
#define ReleaseVersion AppVersion
#if Copy(ReleaseVersion, Len(ReleaseVersion) - 1, 2) == ".0"
  #define ReleaseVersion Copy(ReleaseVersion, 1, RPos(".", ReleaseVersion) - 1)
  #if Copy(ReleaseVersion, Len(ReleaseVersion) - 1, 2) == ".0"
    #define ReleaseVersion Copy(ReleaseVersion, 1, RPos(".", ReleaseVersion) - 1)
  #endif
#endif

[Setup]
AppId={{#InstallerAppId}
AppName={#AppName}
AppVersion={#ReleaseVersion}
AppPublisher=jaxels1
AppPublisherURL=https://github.com/jaxels1/LaunchHere
AppSupportURL=https://github.com/jaxels1/LaunchHere/issues
AppUpdatesURL=https://github.com/jaxels1/LaunchHere/releases/latest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
AppMutex={#AppMutexName}
SetupMutex=Local\LaunchHere.Setup
UninstallDisplayIcon={app}\LaunchHere.exe
UninstallDisplayName={#AppName}
Uninstallable=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
DisableWelcomePage=no
SetupIconFile=..\LaunchHere.ico
OutputDir={#OutputFolder}
OutputBaseFilename=LaunchHere-{#ReleaseVersion}-Setup-x64
VersionInfoVersion={#AppVersion}
VersionInfoProductName=LaunchHere
VersionInfoDescription=LaunchHere Setup
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nOpen apps on the display where you click their taskbar icon.
SetupAppRunningError=LaunchHere is running in the system tray.%n%nRight-click its tray icon and choose Exit, then click OK to continue. Closing the settings window does not quit the app.
UninstallAppRunningError=LaunchHere is running in the system tray.%n%nRight-click its tray icon and choose Exit, then click OK to uninstall.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#AppFilesFolder}\LaunchHere.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppFilesFolder}\LaunchHere.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppFilesFolder}\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\LaunchHere.exe"; Parameters: "--settings"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\LaunchHere.exe"; Parameters: "--settings"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LaunchHere.exe"; Parameters: "--settings"; Description: "Open LaunchHere"; Flags: nowait postinstall skipifsilent

[Code]
const
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('LaunchHere needs .NET Framework 4.8 or newer.' + #13#10 + #13#10 +
      'Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48, then run this installer again.',
      mbCriticalError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Command: String;
begin
  if CurStep = ssPostInstall then begin
    // If startup already pointed at a portable LaunchHere copy, move that
    // registration to the installed copy. Never enable startup by default.
    if RegQueryStringValue(HKCU, StartupKey, '{#StartupValueName}', Command) then
      if CompareText(ExtractFileName(RemoveQuotes(Trim(Command))), 'LaunchHere.exe') = 0 then
        RegWriteStringValue(HKCU, StartupKey, '{#StartupValueName}',
          '"' + ExpandConstant('{app}\LaunchHere.exe') + '"');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usPostUninstall then begin
    // The GUI can enable startup after installation. Remove only a startup
    // entry pointing at this installation, leaving any other copy alone.
    if RegQueryStringValue(HKCU, StartupKey, '{#StartupValueName}', Command) then
      if CompareText(Trim(Command), '"' + ExpandConstant('{app}\LaunchHere.exe') + '"') = 0 then
        RegDeleteValue(HKCU, StartupKey, '{#StartupValueName}');
  end;
  // Preferences in LocalAppData\LaunchHere deliberately survive uninstall.
end;

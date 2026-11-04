#ifndef PayloadDir
  #error PayloadDir must point to the self-contained publish folder
#endif
#ifndef OutputDir
  #define OutputDir "..\installers"
#endif
#ifndef AppVersion
  #define AppVersion "0.8.6"
#endif
#ifndef ExtensionId
  #error ExtensionId must match the packaged browser extension key
#endif

[Setup]
AppId={{F14B83CB-285D-459F-9E24-57D4E44F6D56}
AppName=Flux
AppVersion={#AppVersion}
AppPublisher=Flux
DefaultDirName={localappdata}\Programs\Flux
DefaultGroupName=Flux
DisableProgramGroupPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
UninstallDisplayIcon={app}\Flux.exe
SetupIconFile=..\SwiftFetch\app.ico
OutputDir={#OutputDir}
OutputBaseFilename=Flux-Setup-{#AppVersion}-win-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
AppMutex=Local\ApexDownloader.Running
InfoAfterFile=INSTALLATION.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopTask}"
Name: "browserintegration"; Description: "{cm:BrowserTask}"
Name: "startup"; Description: "{cm:StartupTask}"; Flags: unchecked

[CustomMessages]
english.DesktopTask=Create a desktop shortcut
english.BrowserTask=Connect Flux to Chrome, Edge and Brave (extension setup still required)
english.StartupTask=Start Flux in the system tray when I sign in to Windows
english.BrowserGuide=Set up the Chrome and Brave extension
english.LaunchApp=Launch Flux
french.DesktopTask=Créer un raccourci sur le Bureau
french.BrowserTask=Connecter Flux à Chrome, Edge et Brave (extension à charger dans le navigateur)
french.StartupTask=Démarrer Flux dans la zone de notification à l'ouverture de session Windows
french.BrowserGuide=Configurer l'extension Chrome et Brave
french.LaunchApp=Lancer Flux

[Messages]
english.SetupAppRunningError=Flux is still running, possibly in the system tray.%n%nChoose Tasks > Exit or Exit from the tray menu, then click OK. Downloads will be preserved.
english.UninstallAppRunningError=Flux is still running, possibly in the system tray.%n%nChoose Tasks > Exit or Exit from the tray menu, then click OK. Downloads will be preserved.
french.SetupAppRunningError=Flux est encore ouvert, peut-être dans la zone de notification.%n%nChoisissez Tasks > Exit ou Quitter dans le menu de l'icône, puis cliquez sur OK. Vos téléchargements seront conservés.
french.UninstallAppRunningError=Flux est encore ouvert, peut-être dans la zone de notification.%n%nChoisissez Tasks > Exit ou Quitter dans le menu de l'icône, puis cliquez sur OK. Vos téléchargements seront conservés.

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Upgrades from Apex Downloader: the program file and shortcuts were renamed to Flux.
Type: files; Name: "{app}\ApexDownloader.exe"
Type: files; Name: "{group}\Apex Downloader.lnk"
Type: files; Name: "{userdesktop}\Apex Downloader.lnk"

[Icons]
Name: "{group}\Flux"; Filename: "{app}\Flux.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:BrowserGuide}"; Filename: "{app}\Browser-Setup.html"
Name: "{userdesktop}\Flux"; Filename: "{app}\Flux.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU32; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.swiftfetch.bridge"; ValueType: string; ValueData: "{app}\com.swiftfetch.bridge.json"; Tasks: browserintegration
Root: HKCU64; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.swiftfetch.bridge"; ValueType: string; ValueData: "{app}\com.swiftfetch.bridge.json"; Tasks: browserintegration
Root: HKCU32; Subkey: "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.swiftfetch.bridge"; ValueType: string; ValueData: "{app}\com.swiftfetch.bridge.json"; Tasks: browserintegration
Root: HKCU64; Subkey: "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.swiftfetch.bridge"; ValueType: string; ValueData: "{app}\com.swiftfetch.bridge.json"; Tasks: browserintegration
Root: HKCU32; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.swiftfetch.bridge"; ValueType: string; ValueData: "{app}\com.swiftfetch.bridge.json"; Tasks: browserintegration
Root: HKCU64; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.swiftfetch.bridge"; ValueType: string; ValueData: "{app}\com.swiftfetch.bridge.json"; Tasks: browserintegration
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ApexDownloader"; ValueData: """{app}\Flux.exe"" --background"; Tasks: startup

[Run]
Filename: "{app}\Flux.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
; In-app updates run silently; relaunch the updated app afterwards.
Filename: "{app}\Flux.exe"; Flags: nowait; Check: WizardSilent
Filename: "{app}\Browser-Setup.html"; Description: "{cm:BrowserGuide}"; Flags: shellexec postinstall skipifsilent unchecked; Tasks: browserintegration

[UninstallDelete]
Type: files; Name: "{app}\com.swiftfetch.bridge.json"

[Code]
function JsonString(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  Result := '"' + Value + '"';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Manifest, Json, PreviousFolder: String;
  ExitCode: Integer;
begin
  Result := '';
  // Remove only the previous installer's tracked files when changing from the
  // old multi-file runtime layout. Its uninstaller preserves history/user files.
  if FileExists(ExpandConstant('{app}\SwiftFetch.NativeHost.exe')) and
     FileExists(ExpandConstant('{app}\SwiftFetch.runtimeconfig.json')) and
     FileExists(ExpandConstant('{app}\unins000.exe')) and
     RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F14B83CB-285D-459F-9E24-57D4E44F6D56}_is1', 'InstallLocation', PreviousFolder) and
     (CompareText(AddBackslash(PreviousFolder), AddBackslash(ExpandConstant('{app}'))) = 0) then begin
    if not Exec(ExpandConstant('{app}\unins000.exe'), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then begin
      Result := 'Could not remove the previous Flux files. Quit Flux from its tray menu and retry. Your downloads and history are retained.';
      Exit;
    end;
  end;
  // Build the machine-specific manifest before any browser registry entry changes.
  if not ForceDirectories(ExpandConstant('{app}')) then begin
    Result := 'Could not create the Flux installation folder.';
    Exit;
  end;
  Manifest := ExpandConstant('{app}\com.swiftfetch.bridge.json');
  Json := '{"name":"com.swiftfetch.bridge","description":"Flux browser bridge",' +
    '"path":' + JsonString(ExpandConstant('{app}\Flux.exe')) +
    ',"type":"stdio","allowed_origins":["chrome-extension://{#ExtensionId}/"]}';
  if not SaveStringToFile(Manifest, Utf8Encode(Json), False) then
    Result := 'Could not create the browser integration manifest.';
end;

procedure RemoveOwnedBridge(Root: Integer; Browser: String);
var
  Key, Existing: String;
begin
  Key := Browser + '\NativeMessagingHosts\com.swiftfetch.bridge';
  // A later installation may have taken ownership. Do not unregister that copy.
  if RegQueryStringValue(Root, Key, '', Existing) and
     (CompareText(Existing, ExpandConstant('{app}\com.swiftfetch.bridge.json')) = 0) then
    RegDeleteKeyIncludingSubkeys(Root, Key);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Existing: String;
begin
  if CurUninstallStep = usUninstall then begin
    RemoveOwnedBridge(HKCU32, 'Software\Google\Chrome');
    RemoveOwnedBridge(HKCU64, 'Software\Google\Chrome');
    RemoveOwnedBridge(HKCU32, 'Software\BraveSoftware\Brave-Browser');
    RemoveOwnedBridge(HKCU64, 'Software\BraveSoftware\Brave-Browser');
    RemoveOwnedBridge(HKCU32, 'Software\Microsoft\Edge');
    RemoveOwnedBridge(HKCU64, 'Software\Microsoft\Edge');
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ApexDownloader', Existing) and
       ((CompareText(Existing, ExpandConstant('"{app}\Flux.exe" --background')) = 0) or
        (CompareText(Existing, ExpandConstant('"{app}\SwiftFetch.exe" --background')) = 0) or
        (CompareText(Existing, ExpandConstant('"{app}\ApexDownloader.exe" --background')) = 0)) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ApexDownloader');
  end;
  // No user history, preferences, downloads or temporary download parts are deleted.
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Existing: String;
begin
  if CurStep = ssPostInstall then begin
    if not WizardIsTaskSelected('browserintegration') then begin
      RemoveOwnedBridge(HKCU32, 'Software\Google\Chrome');
      RemoveOwnedBridge(HKCU64, 'Software\Google\Chrome');
      RemoveOwnedBridge(HKCU32, 'Software\BraveSoftware\Brave-Browser');
      RemoveOwnedBridge(HKCU64, 'Software\BraveSoftware\Brave-Browser');
      RemoveOwnedBridge(HKCU32, 'Software\Microsoft\Edge');
      RemoveOwnedBridge(HKCU64, 'Software\Microsoft\Edge');
    RemoveOwnedBridge(HKCU32, 'Software\Microsoft\Edge');
    RemoveOwnedBridge(HKCU64, 'Software\Microsoft\Edge');
    end;
    if not WizardIsTaskSelected('startup') then
      if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ApexDownloader', Existing) and
         ((CompareText(Existing, ExpandConstant('"{app}\Flux.exe" --background')) = 0) or
          (CompareText(Existing, ExpandConstant('"{app}\SwiftFetch.exe" --background')) = 0) or
        (CompareText(Existing, ExpandConstant('"{app}\ApexDownloader.exe" --background')) = 0)) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ApexDownloader');
  end;
end;

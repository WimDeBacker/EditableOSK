; ═══════════════════════════════════════════════════════════════════════════
;  Editable On-Screen Keyboard  —  Inno Setup script
;
;  Prerequisites:
;    • Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;    • Release build must be run first:
;        dotnet build -c Release
;      This produces bin\Release\net10.0-windows\ with all required files.
;    • worddb_EN.wfq is optional (excluded from source control).
;      Place it in bin\Release\net10.0-windows\ before compiling this script
;      if you want English word prediction in the installer.
;
;  To build the installer:
;    Open this file in the Inno Setup IDE and press F9  (or Compile),
;    or run from the command line:
;        iscc installer\setup.iss
;  Output: installer\Output\EditableOSK-1.0.0-Setup.exe
; ═══════════════════════════════════════════════════════════════════════════

#define AppName      "Editable On-Screen Keyboard"
#define AppShortName "EditableOSK"
#define AppVersion   "1.0.0"
#define AppPublisher "Wim De Backer"
#define AppURL       "https://github.com/WimDeBacker/EditableOSK"
#define AppExeName   "OnScreenKeyboard.exe"
#define BuildDir     "..\bin\Release\net10.0-windows"

; ── [Setup] ─────────────────────────────────────────────────────────────────

[Setup]
AppId                    = {{A3B8C2D4-E5F6-47A8-B9C0-D1E2F3A4B5C6}
AppName                  = {#AppName}
AppVersion               = {#AppVersion}
AppVerName               = {#AppName} {#AppVersion}
AppPublisher             = {#AppPublisher}
AppPublisherURL          = {#AppURL}
AppSupportURL            = {#AppURL}
AppUpdatesURL            = {#AppURL}

; Install into "C:\Program Files\EditableOSK" by default
DefaultDirName           = {autopf}\{#AppShortName}
DefaultGroupName         = {#AppName}

; The program is 64-bit (AnyCPU on a 64-bit Windows): install it in "C:\Program Files", not in "Program Files (x86)", and write the
; registry keys of the 64-bit view
ArchitecturesAllowed            = x64compatible
ArchitecturesInstallIn64BitMode = x64compatible

; One installer per machine, not per user (requires elevation)
PrivilegesRequired       = admin

; The .kbl file type is registered (task "kblassoc"): tell Explorer to refresh its associations afterwards
ChangesAssociations      = yes

; Output
OutputDir                = Output
OutputBaseFilename       = {#AppShortName}-{#AppVersion}-Setup
SetupIconFile            = {#BuildDir}\icons\onscreenkeyboard.ico

; The uninstaller (unins000.exe, made by Inno Setup) is listed in Settings > Apps with the program's icon
UninstallDisplayIcon     = {app}\icons\onscreenkeyboard.ico

; Compression
Compression              = lzma2/ultra64
SolidCompression         = yes

; Appearance
WizardStyle              = modern


; Minimum Windows version: Windows 10
MinVersion               = 10.0

; After install: offer to launch the app
DisableFinishedPage      = no

; ── [Languages] ─────────────────────────────────────────────────────────────

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "dutch";   MessagesFile: "compiler:Languages\Dutch.isl"

; ── [Tasks] ─────────────────────────────────────────────────────────────────

[Tasks]
; Desktop shortcut — checked by default
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
    GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; Run at Windows startup — unchecked by default
Name: "startup"; Description: "{cm:StartupTask}"; \
    GroupDescription: "{cm:StartupGroup}"; Flags: unchecked

; Open .kbl layout files with this program (double-click) — checked by default
Name: "kblassoc"; Description: "{cm:KblAssocTask,{#AppName}}"; \
    GroupDescription: "{cm:KblAssocGroup}"

; ── [Registry] ──────────────────────────────────────────────────────────────
; The file type .kbl → program id EditableOSK.Layout → "OnScreenKeyboard.exe" "<file>". HKA is the machine (HKLM) classes here, since
; the installer runs as administrator. uninsdeletekey removes the keys again when the program is uninstalled.

[Registry]
Root: HKA; Subkey: "Software\Classes\.kbl"; ValueType: string; ValueName: ""; ValueData: "EditableOSK.Layout"; \
    Flags: uninsdeletevalue; Tasks: kblassoc
Root: HKA; Subkey: "Software\Classes\EditableOSK.Layout"; ValueType: string; ValueName: ""; ValueData: "{cm:KblTypeName}"; \
    Flags: uninsdeletekey; Tasks: kblassoc
Root: HKA; Subkey: "Software\Classes\EditableOSK.Layout\DefaultIcon"; ValueType: string; ValueName: ""; \
    ValueData: "{app}\icons\onscreenkeyboard.ico"; Tasks: kblassoc
Root: HKA; Subkey: "Software\Classes\EditableOSK.Layout\shell\open\command"; ValueType: string; ValueName: ""; \
    ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: kblassoc

; ── [Files] ─────────────────────────────────────────────────────────────────

[Files]
; ── Core application ─────────────────────────────────────────────────────
Source: "{#BuildDir}\OnScreenKeyboard.exe";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\OnScreenKeyboard.dll";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\OnScreenKeyboard.deps.json";     DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\OnScreenKeyboard.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; ── Third-party DLLs ─────────────────────────────────────────────────────
Source: "{#BuildDir}\Svg.dll";                        DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\ExCSS.dll";                      DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\runtime.osx.10.10-x64.CoreCompat.System.Drawing.dll"; \
                                                      DestDir: "{app}"; Flags: ignoreversion

; ── Icons ────────────────────────────────────────────────────────────────
Source: "{#BuildDir}\icons\*"; DestDir: "{app}\icons"; Flags: ignoreversion recursesubdirs

; ── Keyboard layouts ─────────────────────────────────────────────────────
Source: "{#BuildDir}\azerty.kbl";                     DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\azertycolor.kbl";                DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\qwerty.kbl";                     DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\math.kbl";                       DestDir: "{app}"; Flags: ignoreversion

; ── Word prediction databases ────────────────────────────────────────────
; Dutch — always included
Source: "{#BuildDir}\worddb_NL.wfq";                  DestDir: "{app}"; Flags: ignoreversion

; English — optional: only packed if the file exists next to this script.
; Place worddb_EN.wfq in bin\Release\net10.0-windows\ to include it.
Source: "{#BuildDir}\worddb_EN.wfq";  DestDir: "{app}"; \
    Flags: ignoreversion skipifsourcedoesntexist

; ── Translation ──────────────────────────────────────────────────────────
Source: "{#BuildDir}\lang_nl.xml";                    DestDir: "{app}"; Flags: ignoreversion

; ── Fonts ────────────────────────────────────────────────────────────────
; SchoolKX_New — used by azertycolor.kbl. Installed into the system Fonts folder so
; the bundled colourful theme renders correctly out of the box on a fresh machine.
; onlyifdoesntexist: don't reinstall/overwrite if the user already has it.
; uninsneveruninstall: a shared system font may be in use by other apps by the time
; this app is uninstalled — never remove it automatically.
Source: "fonts\SchoolKX_new.ttf"; DestDir: "{autofonts}"; FontInstall: "SchoolKX_New"; \
    Flags: onlyifdoesntexist uninsneveruninstall

; ── [Icons] ─────────────────────────────────────────────────────────────────

[Icons]
; Start Menu
Name: "{group}\{#AppName}";         Filename: "{app}\{#AppExeName}"; \
    IconFilename: "{app}\icons\onscreenkeyboard.ico"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

; Desktop (optional task)
Name: "{autodesktop}\{#AppName}";   Filename: "{app}\{#AppExeName}"; \
    IconFilename: "{app}\icons\onscreenkeyboard.ico"; \
    Tasks: desktopicon

; Startup folder (optional task)
Name: "{commonstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
    Tasks: startup

; ── [Run] ────────────────────────────────────────────────────────────────────

[Run]
; Offer to launch the app after installation
Filename: "{app}\{#AppExeName}"; \
    Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser

; ── [UninstallDelete] ────────────────────────────────────────────────────────

[UninstallDelete]
; Settings, learned words and the error log are kept per user in %AppData%\EditableOSK (a folder of each user, not removed here:
; learned words survive a reinstall). These two are only left behind by versions before that change.
Type: files; Name: "{app}\settings.xml"
Type: files; Name: "{app}\settings.xml.bak"
; Remove any personal word databases the user created
Type: files; Name: "{app}\*_personal.wfq"

; ── [CustomMessages] ────────────────────────────────────────────────────────

[CustomMessages]
english.StartupTask=Start automatically with Windows
dutch.StartupTask=Automatisch starten met Windows
english.StartupGroup=Startup:
dutch.StartupGroup=Opstarten:
english.KblAssocTask=Open keyboard layout files (.kbl) with %1
dutch.KblAssocTask=Toetsenbordbestanden (.kbl) openen met %1
english.KblAssocGroup=File type:
dutch.KblAssocGroup=Bestandstype:
english.KblTypeName=Keyboard layout
dutch.KblTypeName=Toetsenbordindeling
english.DotNetMissing=%1 requires the .NET 10 Desktop Runtime, which was not found on this computer.%n%nDownload it free from:%nhttps://dotnet.microsoft.com/download/dotnet/10.0%n%nOpen the download page now?
dutch.DotNetMissing=%1 heeft de .NET 10 Desktop Runtime nodig, die op deze computer niet gevonden werd.%n%nDownload ze gratis via:%nhttps://dotnet.microsoft.com/download/dotnet/10.0%n%nDe downloadpagina nu openen?
english.RemoveUserDataPrompt=Also remove all your data of {#AppName}?%n%nThis deletes your settings, the layout that was open and all learned words (the folder %1), and cannot be undone. Choose No to keep them for a later installation.
dutch.RemoveUserDataPrompt=Ook al uw gegevens van {#AppName} verwijderen?%n%nDit verwijdert uw instellingen, het geopende toetsenbord en alle geleerde woorden (de map %1) en kan niet ongedaan worden gemaakt. Kies Nee om ze te bewaren voor een latere installatie.

; ── [Code] ─────────────────────────────────────────────────────────────────
; Checks for .NET 10 Desktop Runtime before starting the install.
; Uses the registry (no subprocess, no temp files).
; If not found, offers to open the download page and then aborts.

[Code]

function IsDotNet10DesktopInstalled(): Boolean;
{ Ask dotnet itself — works whether .NET was installed via SDK or Runtime.
  Runs: dotnet --list-runtimes, looks for "Microsoft.WindowsDesktop.App 10." }
var
  TempFile:   String;
  Lines:      TArrayOfString;
  ResultCode: Integer;
  i:          Integer;
begin
  Result   := False;
  TempFile := ExpandConstant('{tmp}\dotnet_runtimes.txt');
  Exec('cmd.exe',
       '/c dotnet --list-runtimes > "' + TempFile + '" 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if LoadStringsFromFile(TempFile, Lines) then
  begin
    for i := 0 to GetArrayLength(Lines) - 1 do
    begin
      if Pos('Microsoft.WindowsDesktop.App 10.', Lines[i]) > 0 then
      begin
        Result := True;
        Break;
      end;
    end;
  end;
  DeleteFile(TempFile);
end;

function InitializeSetup(): Boolean;
var
  ErrCode: Integer;
  NL:      String;
  Msg:     String;
begin
  Result := True;
  NL     := Chr(13) + Chr(10);

  if not IsDotNet10DesktopInstalled() then
  begin
    Msg := CustomMessage('DotNetMissing');
    StringChangeEx(Msg, '%1', '{#AppName}', True);

    if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0',
                '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
    end;

    Result := False;
  end;
end;

{ ── Uninstall: the choice to remove all user data ─────────────────────────
  The program keeps its settings, learned words and error log per user in %AppData%\EditableOSK. By default an uninstall leaves
  them (a reinstall then continues where the user stopped). The uninstaller asks whether to remove them too, so that the next
  installation is a completely new one. A silent uninstall keeps them unless it is started with /DELETEUSERDATA=1.
  Only the data of the user who runs the uninstaller can be removed; other Windows users on the PC keep theirs. }

procedure RemoveUserData();
var
  UserDir: String;
begin
  UserDir := ExpandConstant('{userappdata}\{#AppShortName}');
  DelTree(UserDir, True, True, True);
  { files an earlier version wrote next to the exe }
  DelTree(ExpandConstant('{app}\*.learned.wfq'), False, True, False);
  DelTree(ExpandConstant('{app}\OnScreenKeyboard_error.log*'), False, True, False);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Remove: Boolean;
  Msg:    String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;

  { (a line of this section must not start with a square bracket: Inno Setup would read it as a section tag) }
  if UninstallSilent() then
    Remove := ExpandConstant('{param:DELETEUSERDATA|0}') = '1'
  else
  begin
    Msg := CustomMessage('RemoveUserDataPrompt');
    StringChangeEx(Msg, '%1', ExpandConstant('{userappdata}\{#AppShortName}'), True);
    Remove := MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;

  if Remove then RemoveUserData();
end;

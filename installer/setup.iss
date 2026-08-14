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

; One installer per machine, not per user (requires elevation)
PrivilegesRequired       = admin

; Output
OutputDir                = Output
OutputBaseFilename       = {#AppShortName}-{#AppVersion}-Setup
SetupIconFile            = {#BuildDir}\icons\onscreenkeyboard.ico

; Compression
Compression              = lzma2/ultra64
SolidCompression         = yes
LZMAUseSeparateProcess   = yes

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
Name: "startup"; Description: "Start automatically with Windows"; \
    GroupDescription: "Startup:"; Flags: unchecked

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
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

; Desktop (optional task)
Name: "{autodesktop}\{#AppName}";   Filename: "{app}\{#AppExeName}"; \
    IconFilename: "{app}\icons\onscreenkeyboard.ico"; \
    Tasks: desktopicon

; Startup folder (optional task)
Name: "{userstartup}\{#AppName}";   Filename: "{app}\{#AppExeName}"; \
    Tasks: startup

; ── [Run] ────────────────────────────────────────────────────────────────────

[Run]
; Offer to launch the app after installation
Filename: "{app}\{#AppExeName}"; \
    Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent

; ── [UninstallDelete] ────────────────────────────────────────────────────────

[UninstallDelete]
; Remove user settings file created at runtime (if present)
Type: files; Name: "{app}\settings.xml"
Type: files; Name: "{app}\settings.xml.bak"
; Remove any personal word databases the user created
Type: files; Name: "{app}\*_personal.wfq"

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
    Msg := 'Editable On-Screen Keyboard requires the .NET 10 Desktop Runtime,' +
           ' which was not found on this computer.' + NL + NL +
           'Download it free from:' + NL +
           'https://dotnet.microsoft.com/download/dotnet/10.0' + NL + NL +
           'Open the download page now?';

    if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0',
                '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
    end;

    Result := False;
  end;
end;

# EditableOSK (OnScreenKeyboard, WinForms, .NET 10)

## Build and test
- Build: `dotnet build -nologo -v q` in this folder.
- Tests: the app is a WinExe, so console output is not captured by a pipe. Run it with output redirected to a file:
  `Start-Process bin\Debug\net10.0-windows\OnScreenKeyboard.exe -ArgumentList "--test" -RedirectStandardOutput $env:TEMP\t_out.txt -Wait -PassThru -NoNewWindow`
  Exit code 0 = all passed. Failed tests are listed in `bin\Debug\net10.0-windows\test_failures.txt`.
- Never run the test mode from Bash: it can open a hidden console and wait for a key.
- Gallery screenshots of the dialogs: `OnScreenKeyboard.exe --gallery <folder>`.
- If the build fails because OnScreenKeyboard.exe is running: ask the user to close it. Do not kill it and do not build in another folder.

## Working rules
- Change files with the Edit/Write tools, not with Python scripts.
- A fix is not done until the build is clean and the full test suite passes.
- For visual fixes, render the gallery and look at it in English and Dutch; a passing test is not enough.
- Every new UI string goes in `LanguageManager.cs` (English) and `lang_nl.xml` (Dutch).
- Throwaway preview pages go in the project folder (not the scratchpad) and must inline their images.

## Where things are written down
- `todo.md`: numbered priorities and steps (P / 2.4.3 / 2.4.3.1). Update it when work is done, and move finished items to Completed.
- `formdesign.md`: design flaws and lessons from the touch-friendly redesign. Read it before building or changing any dialog, and add new findings to it.

## UI conventions
- Touch targets are at least 44 x 44 px (`Touch.Target`); the standard is WCAG 2.1 AAA, light and dark theme.
- New dialogs use `FluentDialogBase` (content-sized, `BuildFrame` / `AddSection` / `AddRow`) and the `Touch*` controls, not hand-positioned controls.
- Borders and rings: `Fluent.CrispBorderRect`, `FluentPainter.DrawRoundedRing`, `Fluent.DrawSquareRing`.
- Icons are SVGs in `icons\`, 24 px grid, stroke `#1a1a1c`, red accent `#bf534e`, loaded with `SvgIconLoader`.

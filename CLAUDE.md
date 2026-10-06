# EditableOSK (OnScreenKeyboard, WinForms, .NET 10)

## Build and test
- Build: `dotnet build -nologo -v q` in this folder.
- Tests: the app is a WinExe, so console output is not captured by a pipe. Run it with output redirected to a file:
  `Start-Process bin\Debug\net10.0-windows\OnScreenKeyboard.exe -ArgumentList "--test" -RedirectStandardOutput $env:TEMP\t_out.txt -Wait -PassThru -NoNewWindow`
  Exit code 0 = all passed. Failed tests are listed in `bin\Debug\net10.0-windows\test_failures.txt`.
- **Work with a filter, finish with the full suite.** `--test <term>[,<term>]` runs only the groups whose name contains a term (`--test Wizard`); add `--quick` to run the layout guards and `T_NarrowDialogs` for English and +80 % text only (the layout guards run in the light theme only in every mode: they measure geometry, and contrast has its own test). A partial run says `PARTIAL RUN` and is for iterating; the full run (about 2 minutes on mains power, more than twice that on battery; no arguments; see `test_timing.txt`) is the only one that counts, and runs once before a commit. Every run prints its slowest groups and writes all timings to `test_timing.txt`.
- **Only run tests when the change calls for them.** A change to comments, docs or non-UI logic needs at most a build or a filtered run; the full suite (and the slow layout guards) only after changes to dialogs, layout or shared code.
- Run the tests in the background (`run_in_background`) and read the output file when the notification arrives; do not poll or sleep.
- Never run the test mode from Bash: it can open a hidden console and wait for a key.
- Gallery screenshots of the dialogs: `OnScreenKeyboard.exe --gallery <folder>`.
- If the build fails because OnScreenKeyboard.exe is running: ask the user to close it. Do not kill it and do not build in another folder.

## Working rules
- Change files with the Edit/Write tools, not with Python scripts.
- A fix is not done until the build is clean and the full test suite passes.
- Layout work: read the whole list of guard failures and look at the gallery pictures before changing anything, then fix every cause in one go. One guess per full run is the slowest way to work. A change that made things worse is reverted at once, not built upon.
- Stress-test strings (`xxxx` appended) cannot wrap inside a word: keep each translated line short (under about 70 characters) instead of making the layout wider.
- For visual fixes, render the gallery and look at it in English and Dutch; a passing test is not enough.
- Every new UI string goes in `LanguageManager.cs` (English) and `lang_nl.xml` (Dutch).
- Throwaway preview pages go in the project folder (not the scratchpad) and must inline their images.

## Where things are written down
- `todo.md`: numbered priorities and steps (P / 2.4.3 / 2.4.3.1). Update it when work is done, and move finished items to Completed.
- `formdesign.md`: design flaws and lessons from the touch-friendly redesign. Read it before building or changing any dialog, and add new findings to it.

## UI conventions
- Touch targets are at least 44 x 44 px (`Touch.Target`); the standard is WCAG 2.1 AAA, light and dark theme.
- New dialogs use `FluentDialogBase` (content-sized, `BuildFrame` / `AddSection` / `AddRow`) and the `Touch*` controls, not hand-positioned controls.
- Dialogs are responsive down to 480 design px (`responsive_spec.md`, section 10): no fixed widths for text or rows. Text that may wrap is registered with `Wrap()`; rows and grids that must move use `InlineRow`, `ButtonRow` or `AdaptiveTable` variants (the arrangement is chosen by the measured width); a new dialog is added to `NarrowDialogs()` in `NarrowWidthTests.cs`. Check it with `--test NarrowDialogs` and look at the `narrow_*` gallery pictures.
- Borders and rings: `Fluent.CrispBorderRect`, `FluentPainter.DrawRoundedRing`, `Fluent.DrawSquareRing`.
- Icons are SVGs in `icons\`, 24 px grid, stroke `#1a1a1c`, red accent `#bf534e`, loaded with `SvgIconLoader`.

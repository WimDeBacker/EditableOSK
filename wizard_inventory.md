# New Keyboard Wizard: inventory (todo 2.4.5.3)

Written 2026-10-04 from reading `NewKeyboardWizard.cs` (1,282 lines), `WizardKeyParser.cs`, `KeyboardForm.OpenNewWizard` and the tests. Nothing was run or photographed for this file. Companion: `wizard_spec.md`, `mockups/wizard.html`.

## 1. What the wizard does

Four pages in a sizable 880 x 820 window (minimum 720 x 580), hand-positioned controls, a custom dark-navy navigation bar at the bottom (Back / Next / Create, "Step n of 4" on the left). It writes a new `.kbl` file and returns its path in `CreatedFilePath`; `KeyboardForm.OpenNewWizard` then clears the undo history and loads the file. Nothing else leaves the dialog.

| Page | Title key | Controls | Hidden state it sets |
|---|---|---|---|
| 1 Start | `wiz: p1 title` | three radio buttons (`_rbBlank` default, `_rbPaste`, `_rbCopy`); copy row (label, `_txtCopyFile`, browse `...`) visible only for Copy; language combo `_cmbLanguage` (English / Nederlands) | start mode; copy path; language (used for the layout's `Language` **and** for parsing pasted labels) |
| 2 Grid | `wiz: p2 title` | Copy: one info label. Paste: section label, hint, multi-line `_txtPaste`. Blank and Paste: rows/columns number boxes (1-30, 1-60, default 4 x 8), preview panel (grid of key rectangles + the gear cell top right) | `_gridRows`, `_gridCols` (preview only) |
| 3 Theme | `wiz: p4 title` | four preset buttons (Dark, Light, High Contrast, Colorful) + "From file..." button, a check-mark label under each, from-file row (`_txtThemeFile`, browse), preview strip with ten sample keys (one per group) | `_selectedPreset` (0-3, or -1 = file) |
| 4 Save | `wiz: p5 title` | `_txtFileName` (default "new keyboard"), `_txtFolder` + browse, error label, summary panel (rows x columns, theme, language, always on top) | none |

Result (`BuildLayoutData`): layout from the copied file, the parsed paste (one extra column, gear cell at row 0 / last column) or a blank grid (`cols + 1`); theme from a preset (`ApplyPreset` restyles or adds the standard group and the six groups Klinkers, Medeklinkers, Cijfers, Besturing, Leestekens, Woord) or from a file (global theme copied, each of its groups overwrites a same-named group or is added); paste layouts are auto-classified into those six groups (`ClassifyKey`); window size from the primary screen working area; `AlwaysOnTop = true`, `HideTitlebar = false`, `GearRow 0`, `GearCol -1`.

Other public/internal surface: `ThemePreset`, `Presets` (internal, read by tests), `ClassifyKey` (internal static), `WizardThemeValidator.ContrastRatio` (internal, **not used by the wizard** since the "validate imported theme" feature was dropped).

## 2. Defects found in the old wizard

| # | Defect | Where |
|---|---|---|
| W1 | Does not fit a 1366 x 768 screen: 820 px client + title bar. The nav bar can be below the screen edge. | ctor `new Size(880, 820)` |
| W2 | Touch targets: the browse buttons are 30 x 25, text boxes 25, number boxes 25, radio buttons 28 (the target is 44). | all pages |
| W3 | All validation is a modal `MessageBox` ("Please select a layout file...") instead of an inline message next to the field. | `ValidatePage` |
| W4 | In Paste mode the rows / columns boxes are live but do nothing: the file uses the parsed rows, the boxes only resize the preview. Editing them misleads. | `_nudRows`, `BuildLayoutData` |
| W5 | Create silently overwrites an existing file of that name. | `TryCreate` |
| W6 | File name is not checked for invalid characters (`/`, `:`, ...); the exception text from the file system is shown instead. | `TryCreate` |
| W7 | No Cancel button (only the window X), and Enter does nothing on pages 1-3 because `AcceptButton` is the hidden Create button. | `BuildUI` |
| W8 | Selection is shown by colour only (Primary vs Neutral button) plus a small check-mark label under the tile. | `SelectPreset` |
| W9 | Fonts are hard-coded (Arial 16 bold title, Arial 9 section labels); colours are hard-coded RGB values; custom paint for the dark nav bar. | `AddPageTitle`, `ApplyTheme` |
| W10 | Preview and summary are hand-painted with `DrawString`, no accessible name or text (screen readers see an empty panel). | `OnPreviewPaint`, `OnSummaryPaint` |
| W11 | The summary says "Always on top: Yes" with a hard-coded value; there is no way to change it, and no mention of the title bar. | `OnSummaryPaint` |
| W12 | Switching the language combo does not change the wizard's own language (only the layout's `Language`). Confusing next to the page title. | page 1 |
| W13 | Unused strings: `wiz: p3 title`, `wiz: p3 sub`, `wiz: tip always on top`, `wiz: tip hide title bar`, `wiz: Validate with`, `wiz: suite contrast`, `wiz: suite focus`, `wiz: validation title`, `wiz: validation header`, `wiz: fail contrast`, `wiz: fail focus`. To remove in 2.4.7 (or reuse, see decision D9). | `LanguageManager.cs`, `lang_nl.xml` |
| W14 | Colorful preset is below AA (Klinkers 3.4 : 1, Leestekens 3.8 : 1): already todo 2.5.4, a design decision for the keyboard, not for the dialog. | `Presets` |

## 3. Tests that touch the wizard

| Test | What it needs | Effect of the rebuild |
|---|---|---|
| `T_WizardKeyParser`, `ClassifyKey` tests (OnScreenKeyboardTests.cs ~4824-5200) | `WizardKeyParser`, `NewKeyboardWizard.ClassifyKey` | none while both stay as they are |
| `T_WizardThemePresets` (~5209-5280) | `NewKeyboardWizard.Presets` | none |
| `T_WizardBuildLayoutData_ThemeFileMerge` (~5290) | reflection: fields `_rbBlank`, `_rbPaste`, `_rbCopy` (cast to `RadioButton`), `_txtCopyFile`, `_txtPaste`, `_txtThemeFile` (cast to `TextBox`), `_selectedPreset`, method `BuildLayoutData` | adapt the casts if the new controls do not derive from the WinForms types; keep the field names where possible |
| `T_WizardThemePage_GroupSwatchColors` (~5370) | `_txtThemeFile`, `_selectedPreset`, `GetGroupKeyColor` | same |

No test builds the wizard for layout, and the gallery has no wizard picture. New: `WizardTests.cs` (guards for each page in en / nl / +40 % / +80 %, light and dark, 1366 x 768 fit; navigation; validation messages; Enter; overwrite; invalid name) and `DevGallery.SaveWizard`.

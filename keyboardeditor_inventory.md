# Keyboard Editor: inventory of the old dialog (`KeyboardEditorForm.cs`, commit 27aa301)

Written 2026-10-02 from reading the code only. Nothing was built, run or photographed. The new design is in `keyboardeditor_spec.md`.

> **Status, 2026-10-06:** this file describes the **old** dialog (the 1,130-line `KeyboardEditorForm` that was replaced on 2026-10-04). Every defect listed here is fixed in the new one, except where `keyboardeditor_spec.md` ("Current state" at the top) says otherwise. It is kept as the record of what the old dialog did and which hidden state had to survive the rebuild.

Legend: EN / NL = English / Dutch text, with the accelerator letter in brackets. "built-in &" means the `&` is inside the translation string; "prefixed" means the code writes `"&" + Lang.T(...)`, so the accelerator is the first letter of the translation and differs per language.

## 1. Public API and passthrough

| Item | What it does |
|---|---|
| Constructor `(VisualTheme theme, WindowState window, LayoutMeta meta, Form owner, Action onSave, Action onSaveAs, Action onLoad, List<KeyGroup> groups, Func<List<KeyGroup>> getGroups, Func<(VisualTheme, WindowState, LayoutMeta)> getSettings)` | Clones theme / window / meta into `Result*`, clones the groups. `base(new Size(940, 560))`, old hand-positioned form. |
| `ResultTheme`, `ResultWindow`, `ResultMeta`, `ResultGroups` | Read by `KeyboardForm.OpenKeyboardEditor` after `DialogResult.OK`; copied into the live `_theme` / `_window` / `_meta` with `CopyFrom`. |
| Only 4 arguments are used by tests / gallery | `(theme, window, meta, owner)`. Only `KeyboardForm` passes the callbacks and `groups`. |
| Passthrough in `Apply` | `VisualTheme`: FontName, FontSize, FontColor, KeyColor, BorderColor, BorderThickness (from `_srcTheme`). `WindowState`: WindowWidth, WindowHeight. `LayoutMeta`: Language, LastFile, GearRow, GearCol. Apply builds NEW objects, so a field added to a model class later is silently reset on every Apply unless someone adds it here by hand (see section 5, H9). |

## 2. Controls and settings

Model column: `T` = `VisualTheme`, `W` = `WindowState`, `M` = `LayoutMeta`.

| # | Field | Old control | Edits | Model property | Loaded (`PopulateFields` unless noted) | Written back (`Apply`) | Keys: EN [accel] / NL [accel] | Dependencies, side effects |
|---|---|---|---|---|---|---|---|---|
| 1 | `_cmbLanguage` | `ComboBox` DropDownList, 26 px | Interface language | none in the dialog (`M.Language` is written by `KeyboardForm`'s `Lang.LanguageChanged` handler) | In `BuildUI`, not in `PopulateFields`: items from `Lang.GetAvailable()`, selects `Lang.CurrentCode`, else item 0 | Not written: `M.Language = _srcMeta.Language` (passthrough) | "Language" / "Taal", no accelerator; card header only; no tooltip (the key `tip: Language` exists and is unused here) | **Live:** `SelectedIndexChanged` calls `Lang.Load(code)` at once, so the whole app switches language and `KeyboardForm` sets `_meta.Language`. **Cancel does not undo it.** Not refreshed after the Load button (stale selection if the loaded file has another language). Initial focus (`ActiveControl`). |
| 2 | `_trkOpacity` | `TrackBar` 0..80, tick 10, small 5, large 10, 45 px high | Window transparency | `T.Opacity` | `slider = clamp(round((1 - clamp(op, .2, 1)) * 100), 0, 80)` | `Opacity = clamp((100 - slider) / 100, .2, 1)` | label `AddFieldLabel` prefixed: "Transparency" [T] / "Transparantie" [T]; tip `tip: Opacity` | Lossy: an opacity such as 0.755 is rewritten as 0.76 on every Apply, touched or not. The slider thumb is a few pixels wide (not a 44 px target). `ValueChanged` handler is empty ("reserved for future live preview"). Key `Opacity` = "Transparency". |
| 3 | `_pnlBgColor` | `AddColorRow`: hex `TextBox` (courier) + 32 x 26 `ColorSwatchButton`; the hex box hangs off `swatch.Tag` | Window background colour | `T.BackgroundColor` | `SetSwatchHex(Hex(color))` | `ParseColor(hex, _srcTheme.BackgroundColor)` (an invalid code can never reach this: Apply refuses first) | prefixed "Background" [B] / "Achtergrond" [A]; tips `tip: Hex color`, `tip: Color swatch` | An invalid hex sets `_err` ("err: invalid hex") and `HasPendingErrors()` blocks Apply. `_suppressOnChanged` (hidden flag in the base class) silences the handler during `SetSwatchHex`. Swatch opens the standard `ColorDialog`. |
| 4 | `_chkAlwaysOnTop` | `CheckBox`, `AutoSize`, about 17 px tick | Window stays on top | `W.AlwaysOnTop` | `Checked = ws.AlwaysOnTop` | copied | built-in & "&Always on top" [A] / "Altijd &bovenaan" [B]; **no tooltip** | `KeyboardForm.ForceTopMost()` after OK. |
| 5 | `_chkHideTitlebar` | `CheckBox` | Hide the Windows title bar | `W.HideTitlebar` | `Checked` | copied | built-in & "H&ide title bar" [I] / "Titelbalk &verbergen" [V]; no tooltip | `KeyboardForm.ApplyTitlebarState()` after OK. |
| 6 | `_cmbToolbarTheme` | `ComboBox` DropDownList, 3 items | Toolbar colours | `M.ToolbarTheme` | `SelectedIndex = (int)m.ToolbarTheme` (set in `BuildUI` and again in `PopulateFields`) | `(ToolbarTheme)SelectedIndex` | built-in & "T&oolbar theme" [O] / "&Werkbalkthema" [W]; items "Dark/Donker", "Light/Licht", "System default/Systeemstandaard"; no tooltip | The list order must equal the enum order (`Dark, Light, System`): hidden coupling by index. Items are translated once, at construction, and **not** refreshed on a language change. The dialog's own theme follows `ToolbarButton.IsLightTheme` at construction only: changing this value does not re-theme the open dialog. |
| 7 | `_btnSaveFile` | `FluentButton` Neutral, 42 px | Save the layout file | none | n/a | Click: `if (Apply()) _onSave()` | prefixed "Save" [S] / "Opslaan" [O]; no tooltip | See section 4, B1 and B2 (stale save; the dialog closes). |
| 8 | `_btnSaveAsFile` | `FluentButton`, 42 px | Save under a new name | none | n/a | Click: `if (Apply()) _onSaveAs()` | built-in & "Sa&ve As…" [V] / "Op&slaan als…" [S] | same |
| 9 | `_btnLoadFile` | `FluentButton`, 42 px | Load another layout file | none | Click: `_onLoad()`, then re-reads `_getSettings()` into `_srcTheme/_srcWindow/_srcMeta`, `_groups = _getGroups()` clones, then `PopulateFields(...)` | none | prefixed "Load…" [L] / "Laden…" [L] | See section 4, B3 (edits lost even when the file dialog is cancelled; not undone by Cancel). |
| 10 | `_chkStickyMods` | `CheckBox` | Modifiers stay on until the next key | `M.StickyModifiers` (default true) | `Checked` | `StickyModifiers` | built-in & "Stic&ky modifiers" [K] / "Plaktoetsen (Sticky &Keys)" [K]; **no tooltip** | |
| 11 | `_chkHoldToEdit` | `CheckBox` | Long press needed to open the key editor | `M.HoldToEdit` (default false) | `Checked` | `HoldToEdit` | built-in & "&Hold to enter edit mode" [H] / "&Ingedrukt houden voor bewerkingsmodus" [I]; **no tooltip** | |
| 12 | `_chkSlowKeys` + `_nudSlowKeys` | `CheckBox` + `NumericUpDown` 75 x 26, 100..3000 step 50, default 300 | Slow keys: a key registers after N ms | `M.SlowKeysMs` (0 = off) | `if (SlowKeysMs > 0) { nud = clamp(ms, 100, 3000); chk = true } else chk = false`; `nud.Enabled = chk.Checked` | `chk.Checked ? (int)nud.Value : 0` | built-in & "Slow k&eys" [E] / "La&ngzame toetsen" [N]; tip `tip: Slow keys` on both controls; nud `AccessibleName` = stripped label | Mutually exclusive with Dwell (row 13). Enables the nud and the animation check box. A loaded value outside 100..3000 is silently clamped on Apply. The nud keeps its last value while off, but Apply writes 0 (the value is lost on the next load). |
| 13 | `_chkDwell` + `_nudDwell` | same, 100..5000 step 100, default 1000 | Dwell click: hover N ms fires the key | `M.DwellMs` (0 = off) | same pattern | same pattern | built-in & "&Dwell click" [D] / "Automatis&che klik" [C]; tip `tip: Dwell click` | **Load order quirk:** `PopulateFields` ticks Slow first, then Dwell. Each tick runs the exclusion handler, so a file with BOTH values above 0 ends up as Dwell only, and Apply then writes `SlowKeysMs = 0`. |
| 14 | `_chkTimingAnimation` | `CheckBox`, constructed `Checked = true` | Fill animation on keys while slow keys / dwell counts down | `M.ShowTimingAnimation` (default true) | `Enabled = SlowKeysMs > 0 || DwellMs > 0` (and by the handlers of 12 and 13); `Checked = value` even while disabled | `Checked` is written even while disabled | built-in & "Sho&w timing animation" [W] / "Voo&rtgangsanimatie tonen" [R]; tip `tip: Show timing animation` | Disabled with no visible reason. |
| 15 | `_chkCornerLabels` | `CheckBox`, constructed `Checked = true` | Show the small Shift / AltGr labels on the keys | `M.ShowCornerLabels` (default true; saved only when off) | `Checked` | `ShowCornerLabels` | no & "Show Shift and AltGr labels" / "Shift- en AltGr-labels tonen"; tip `tip: Show Shift and AltGr labels` | `KeyboardForm` invalidates all keys after OK. Read by `CornerLabelTests` by field name. |
| 16 | `_chkWPLearning` | `CheckBox`, constructed `Checked = true` | Remember typed words | `M.WordLearningEnabled` (default true) | `Checked` | `WordLearningEnabled` | prefixed "wp: Remember typed words": "Remember typed words" [R] / "Getypte woorden onthouden" [G]; tip `wp: tip remember` | **Live:** `CheckedChanged` sets the static `WordDatabase.LearningEnabled`, refreshes the info label and the candidate controls. **Cancel does not undo it** (the engine stays switched until the next `LoadWordDatabase`). Loading the dialog with `false` also sets it. Gates row 20 to 22. |
| 17 | `_cmbWPDatabase` | `ComboBox` DropDownList; items `WPDbAutoItem` and one `WPDbItem` per `LanguageRegistry.All` ("[NL]  worddb_NL") | Which word database file | `M.WordDatabase` (file name, "" = auto) | `PopulateWPDatabaseCombo(name)`: matches by file name, else falls back to "(auto)" | file name of the selected item, "" for auto | built-in none; `AddFieldLabel` "Database" / "Database"; "wp: Auto" = "(auto)" / "(automatisch)"; tip `wp: tip database` | A stored name that is not found is **silently rewritten to ""** on Apply. `_suppressWPChanged` guards the programmatic fill. There is **no revert on Cancel in this code** (the file comment says "no revert logic"; the selection only takes effect when `KeyboardForm.LoadWordDatabase` runs after OK). The registry is rescanned from disk on every fill and on every export-enabled check. |
| 18 | `_lblWPInfo` | `Label`, 20 px, hint font | Shows language, word count, learning state | none | `UpdateWPInfoLabel` on fill, on selection, on the check box, on `WordDatabase.Loaded` | n/a | keys "wp: learning on" / "wp: learning off", "wp: words", "wp: No database loaded" | Auto: language and count of the LOADED database. An explicit file: language and learning state only (no count). Not focusable; a screen reader never reaches it. |
| 19 | `_btnWPExport` | `FluentButton` full width | Copy the overlay file (learned words) elsewhere | none | `Enabled = File.Exists(GetOverlayPath(SelectedOrLoadedBasePath()))` | n/a | "wp: Export…" = "Export…" / "Exporteren…", no accelerator; tip `wp: tip export`; dialog title "wp: Export learned words" | `SaveFileDialog` with an untranslated filter string. Failure uses a system `MessageBox` ("wp: Export failed"). The overlay on disk may lack the last words typed (the app saves every 30 s and at exit): unverified whether `SaveIfDirty` should run first. "(auto)" resolves through `_srcMeta.Language`, not the loaded database. |
| 20 | `_lstWPCandidates` | `ListBox` 70 px, strings "word (count)" | Unknown words seen while typing | none (reads `WordDatabase.GetCandidates()`) | `PopulateWPCandidates()`; refreshed on `WordDatabase.Loaded`; selection kept by word | n/a | "wp: Candidates" = "Candidates" / "Kandidaten"; tip `wp: tip candidates` | `Enabled = learning`. Always shows the candidates of the **loaded** database, even when another file is selected in row 17. |
| 21 | `_btnWPPromote` | `FluentButton` half width | Add the selected candidate to the word list now | none | n/a | Click: `WordDatabase.PromoteCandidate(word)` | "wp: Promote" = "Promote" / "Toevoegen"; tip `wp: tip promote` | **Immediate and permanent** (saved by the 30 s timer or at exit). Cancel does not undo it. Enabled when learning and a row is selected. |
| 22 | `_btnWPReject` | `FluentButton` half width | Discard the selected candidate | none | n/a | Click: `WordDatabase.RemoveCandidate(word)` | "wp: Reject" = "Reject" / "Verwijderen"; tip `wp: tip reject` | Immediate, as row 21. |
| 23 | `_btnApply` | `FluentButton` 44 px (`MakeActionBtn`) | Commit | n/a | n/a | `Apply()` | built-in & "A&pply" [P] / "Toe&passen" [P] | `AcceptButton`. Refuses while `HasPendingErrors()`. Sets `DialogResult.OK` and closes. |
| 24 | `_btnCancel` | `FluentButton` 44 px | Close without applying | n/a | n/a | `DialogResult.Cancel` | built-in & "&Cancel" [C] / "Ann&uleren" [U] | `CancelButton`. Does not undo rows 1, 9, 16, 21, 22. |
| 25 | Card headers (`AddGroup` x5, painted) | painted panels | Titles | n/a | n/a | n/a | "Language", "Window", "Layout file", "Accessibility", "wp: Word prediction" (NL: Taal, Venster, Lay-outbestand, Toegankelijkheid, Woordvoorspelling) | Re-painted from `_transGroups` on a language change. |
| 26 | Window title | `Text` | | n/a | n/a | n/a | "Edit Keyboard" / "Toetsenbord bewerken" | |

Accelerators in the old dialog: EN T, B, A, I, O, K, H, E, D, W, S, V, L, R, P, C (16, all different). NL T, A, B, V, W, K, I, N, C, R, O, S, L, G, P, U (16, all different). `T_Accelerators` requires at least 10.

## 3. Behaviour a new version must preserve

1. `Result*` objects are copies; the caller's objects are never touched before OK. Passthrough fields survive (section 1).
2. Slow keys and Dwell click are mutually exclusive; each stores 0 when off; either one enables "Show timing animation"; the animation value is stored even when its check box is disabled (it must not be reset to a default just because it is disabled).
3. An invalid value must block Apply and show where (the colour hex was the only case; the touch chip removes it, but the wiring `ShowFirstSectionWithError` must stay for any future field).
4. The Load button re-reads theme / window / meta / groups from the caller and shows them (`getSettings`, `getGroups`).
5. The language chooser changes the UI language at once (the dialog re-translates itself while open).
6. The database chooser offers "(auto)" plus every base `.wfq` file; "(auto)" is stored as "".
7. Candidates are listed most-seen first, disabled while learning is off, Promote / Reject need a selection, the list refreshes when `WordDatabase.Loaded` fires while the dialog is open, and the selection is kept by word after a refresh.
8. Export copies the overlay (`.learned.wfq`) and is only possible when that file exists.
9. The word-count / language info refreshes when a background database load finishes.
10. After OK, `KeyboardForm` re-reads everything (`CopyFrom`, titlebar, topmost, background, opacity, toolbar theme, `LoadWordDatabase`, `ApplyWPTags`, invalidates the keys, `AutoSave`). The new dialog must not change that contract except where the spec says so (FileAction).
11. Enter = Apply, Esc = Cancel. Initial focus on the first control.
12. `ShowCornerLabels` default on, written only when off (model code, unchanged).
13. `WordDatabase.Loaded` is unsubscribed when the dialog closes.
14. Dutch and English text, `tip:` keys registered and translated (tests check this structurally).

## 4. Defects found by reading (to fix in the redesign; none was confirmed by running)

| ID | Defect |
|---|---|
| B1 | **Save and Save As save the OLD values.** `Apply()` closes the dialog and returns, then `_onSave()` runs `KeyboardForm.SaveSettings`, which writes `_theme / _window / _meta`. Those are only updated from `dlg.ResultTheme` etc. AFTER `ShowDialog` returns. So what the user just changed in the dialog is not in the file (it reaches only the auto-save file, afterwards). The XML doc of the old constructor claims the opposite ("Apply() is called first so ResultTheme is up to date"). |
| B2 | Save / Save As close the dialog (it is `Apply()`), though the buttons look like file actions. With no current file, a `SaveFileDialog` opens while the editor is closing. |
| B3 | Load replaces the keyboard immediately (it mutates the live `_theme / _window / _meta` and `_layout` in `KeyboardForm`), so Cancel cannot undo it. If the user cancels the file dialog, `PopulateFields` still runs and **overwrites every unsaved edit in the dialog** with the live values. No confirmation. |
| B4 | The language change survives Cancel (rows 1). |
| B5 | `WordDatabase.LearningEnabled` stays flipped after Cancel (row 16), until the next database (re)load. |
| B6 | Promote / Reject survive Cancel (rows 21, 22) and are not mentioned on screen. |
| B7 | Toolbar theme items and the "(auto)" entry are not re-translated when the language changes while the dialog is open. |
| B8 | A stored database name that is not found is replaced by "(auto)" without a word. |
| B9 | Loading both `SlowKeysMs` and `DwellMs` above 0 leaves only Dwell (row 13); loaded values outside the steppers' ranges are clamped on Apply (rows 2, 12, 13). |
| B10 | Candidates list shows the loaded database, the chooser may show another (rows 17, 20). |
| B11 | Several controls have no tooltip: Sticky modifiers, Hold to edit, Always on top, Hide title bar, the toolbar theme, the language chooser and the three file buttons (the Language tooltip key exists but is unused). |
| B12 | Controls are 26 to 45 px high, the tick box is about 17 px, the stepper arrows a few pixels, the trackbar thumb a few pixels. `UiGuardTests` reports the form as baseline only (`ui_guard_report.txt`). Fixed size 940 x 560, then `ClientSize` grown by hand and wrapped in a scroll panel; it does not fit a 1366 x 768 screen (todo 9.4). |
| B13 | `Apply` builds new `LayoutMeta` / `WindowState` / `VisualTheme` objects field by field (see section 1). |
| B14 | The word-prediction database list and the registry are scanned from disk on the UI thread every time the info label is refreshed (export-enabled check) and on every fill. Small, but it repeats. |
| B15 | `WordDatabase.Loaded` is unsubscribed in `FormClosed` only; a form built and disposed without being shown (every test does that) keeps the handler. (The base class handles its own events in `Dispose`; this one is the form's own.) |

## 5. Hidden flags and hidden state in the old code (formdesign.md: hidden state is a defect)

| ID | Where | What | New design |
|---|---|---|---|
| H1 | `_suppressWPChanged` | re-entrancy guard around the combo fill | One `_loading` flag, as in the Group Editor, set only while the code fills controls; never read by Apply. |
| H2 | `FluentDialogBase._suppressOnChanged` | silences the hex handler during `SetSwatchHex` | Gone (no hex box). |
| H3 | `_srcTheme`, `_srcWindow`, `_srcMeta` | replaced on Load; the passthrough values come from them | Kept as the object that Apply clones, but named `_sourceMeta` etc. and re-pointed in one method (`AdoptSource`), covered by a test. |
| H4 | Check box `Enabled` for the timing animation | derived from two other controls, no visible reason | Visible hint text; the state is a pure function `TimingAvailable()`. |
| H5 | NUD value while its feature is off | kept in the control but never stored | The stepper stays visible and disabled with its last value; a hint says that off stores 0. |
| H6 | `_cmbToolbarTheme.SelectedIndex = (int)enum` | order of the items equals the enum order | Items carry their enum value explicitly. |
| H7 | `_cmbLanguage` | selection tied to `Lang.CurrentCode` at construction only | Re-synced after Load and after any `LanguageChanged`. |
| H8 | `WordDatabase.LearningEnabled` (static) set by a checkbox | engine state changed before OK | Only on Apply (through `KeyboardForm.LoadWordDatabase`, which already does it). |
| H9 | `new LayoutMeta { ... }` in Apply | any field not listed is reset | Apply clones the source and overwrites the edited fields. |
| H10 | `Lang.Load` in a change handler | global state changed before OK | Restored on Cancel (`_languageOnOpen`, explicit, tested). |
| H11 | Opacity rounding | a value is rewritten even when untouched | The source value is kept unless the stepper differs from the rounded source (a pure comparison, not a flag). |

## 6. Tests that touch the old dialog

Read private members of `KeyboardEditorForm` by name (these need adapting):

| Test | File:line | What it reads |
|---|---|---|
| `T_CornerLabels` | `CornerLabelTests.cs:61` | field `_chkCornerLabels` (cast to `CheckBox`) |
| `T_CornerLabels` | `CornerLabelTests.cs:64` | private method `Apply` (returns `bool`) |
| `T_ValidationBlocksApply` | `OnScreenKeyboardTests.cs:4582` | field `_pnlBgColor` (a `Button` whose `Tag` is the hex `TextBox`) |
| `T_ValidationBlocksApply` | `OnScreenKeyboardTests.cs:4585` | private method `Apply` |
| `T_ValidationBlocksApply` | `OnScreenKeyboardTests.cs:4542-4545` | base method `HasPendingErrors` by reflection (still exists) |
| `CheckTooltipsRegistered` | `OnScreenKeyboardTests.cs:4370` | base field `_transTooltips` (still exists) |

Depend on the old structure without reading its names:

| Test | File:line | Dependency |
|---|---|---|
| layout sanity | `OnScreenKeyboardTests.cs:1690-1701` | a direct child `Panel` with `AutoScroll` (the old scroll wrapper); the Group Editor version of the same test already uses `UiGuard.All(...)` |
| AutoScaleMode | `OnScreenKeyboardTests.cs:4201-4214` | none (still true) |
| `T_AccessibilityControls` | `OnScreenKeyboardTests.cs:4407-4427` | counts `NumericUpDown` with an `AccessibleName` (at least 2); the new form has none: count `TouchStepper` instead (3) and use `CheckNudHasDescription` as the Group Editor does |
| `T_Accelerators` | `OnScreenKeyboardTests.cs:4872-4905` | builds the form in en / nl, minimum 10 accelerators, no duplicates (keep; the new form has 16 or 17) |
| `T_UiGuardBaseline` | `UiGuardTests.cs:1016-1036` | lists `KeyboardEditorForm` as the only baseline dialog; it moves to the strict list and the baseline test can be deleted |
| gallery | `DevGallery.cs:192` | constructs the form with 4 arguments |
| construction only | `OnScreenKeyboardTests.cs:1695`, `4204`, `4410`, `4581`, `UiGuardTests.cs:1021` | the 4-argument constructor (keep) |

No other test reads a `KeyboardEditorForm` member. `KeyboardForm` is never constructed in the suite, so the `OpenKeyboardEditor` hand-off (copy-back, FileAction) has no automated test today.

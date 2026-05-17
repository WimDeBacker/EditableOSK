# OnScreenKeyboard – Todo List

---

## Pending — UI / UX improvements

- [x] **Group editor — hex fields and field order** — `GroupEditorForm` colour rows previously showed only a wide swatch (no hex input). Replaced `AddColorSwatch` with `AddColorRow` returning `(Panel swatch, TextBox hexBox)`: 32 px swatch + `vw - 37 px` hex box, matching `KeyEditorForm.AddColorRow`. `SetSwatchColor` now writes the hex box and sets `BackColor`; `GetSwatchColor` reads back via `TryParseHex`. `_loading` flag suppresses circular `TextChanged` updates during `LoadDetail`. Field order rewritten to match `KeyEditorForm` Appearance column: Name → Font → Font size → Font color → Key color → Border color → Border thickness. ✓

- [x] **Emoji in dialog title bars** — Root cause: `KeyEditorForm` title includes `[{props.Label}]`, and when the key label is an emoji (⚙, 📌, …) the title bar showed a replacement box. `KeyboardEditorForm` and `GroupEditorForm` titles contain no user content and were already clean. Fix: `TitleSafeLabel` helper strips surrogate pairs (U+10000+ emoji) and BMP symbol blocks (U+2600–U+27BF, U+2B00–U+2BFF); `BuildTitle` omits the brackets entirely if nothing printable remains. ✓

- [x] **"+" button next to group dropdown in key editor** — `FluentButton` (Neutral, 32×26 px) added to the right of `_cmbGroup` in `KeyEditorForm`. Combo width narrowed from `svw` → `svw - 37` to mirror the color-row text-box/swatch ratio, keeping all labels unclipped. Button `Left` = `svx + svw - 32`, exactly matching the color swatch column above. Click opens `GroupEditorForm`; on OK, `_groups` is updated in-place (`.Clear()` + `.AddRange()`), `_cmbGroup` is rebuilt via `RebuildGroupCombo` which restores the previous selection by name. ✓

- [ ] **Windows accessibility** — Full audit and implementation across all three editor dialogs (`KeyEditorForm`, `KeyboardEditorForm`, `GroupEditorForm`) and the shared `FluentButton` control. Items grouped by effort.

  #### Quick wins (trivial — 1–3 lines each)
  - [x] **1. AcceptButton / CancelButton on main forms** — `AcceptButton = _btnApply; CancelButton = _btnCancel` on `KeyEditorForm` and `KeyboardEditorForm`; `AcceptButton = _btnOK; CancelButton = _btnCancel` on `GroupEditorForm`. Enter and Escape currently do nothing on all three forms. ✓
  - [x] **2. TabStop on action buttons** — `FluentButton` constructor sets `TabStop = false` globally. Added `TabStop = true` in `MakeActionBtn` (KeyEditorForm, KeyboardEditorForm) and `MakeBigBtn` (GroupEditorForm) so Apply, Cancel, and OK are reachable by Tab. ✓
  - [x] **12. CancelButton on sub-dialogs** — Added `dlg.CancelButton = cn` to `GroupEditorForm.GetNewName` and `dlg.CancelButton = btnCancel3` to the import-resolution dialog. Escape now closes both. ✓
  - [x] **18. Focus restoration after sub-dialog closes** — `_cmbGroup.Focus()` after `GroupEditorForm` closes from the `+` button in `KeyEditorForm`; `_btnManageGroups.Focus()` after `GroupEditorForm` closes from `KeyboardEditorForm`. ✓
  - [x] **19. Initial focus on dialog open** — `ActiveControl = _txtLabel` (KeyEditorForm), `ActiveControl = _cmbFont` (KeyboardEditorForm), `ActiveControl = _txtName` (GroupEditorForm). ✓

  #### Screen reader basics
  - [ ] **3. Focus ring on FluentButton** — `FlatAppearance.BorderSize = 0` suppresses the default focus rectangle and nothing replaces it. In `FluentButton.OnPaint`, after all painting: `if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width-7, Height-7));`. `ShowFocusCues` returns false for mouse users so the ring only appears during keyboard navigation.
  - [ ] **4. AccessibleName on every interactive control** — Every `TextBox`, `ComboBox`, `NumericUpDown`, `CheckBox`, and `Panel` used as input needs `AccessibleName` set to the text of its visible label (wrapped in `Lang.T(...)`). Controls currently have no name so Narrator announces them as "edit" or "combo box" with no context.
  - [ ] **9. Preview panel not described** — `_pnlPreview` changes label, colour, and font as the user edits. Set `AccessibleName` and `AccessibleDescription` on the panel, or add a hidden read-only text field that mirrors the preview state in words ("Preview: key 'A', blue background, Arial 14 pt") updated alongside `Refresh2()`.
  - [ ] **24. Tooltips on nearly all controls** — Only one `ToolTip` exists in the entire codebase. Every non-obvious control needs one: colour swatches ("Click to pick colour; type hex directly in the field"), `+` group button ("Add or edit groups"), Record button, Browse button, mode selector buttons, span spinners, border-thickness spinner. Screen readers expose tooltip text as `HelpText`, announced when the control receives focus.
  - [ ] **25. Sentinel values in AccessibleDescription** — The meaning of `-1 = inherit global` (border-thickness) and `0 = auto / inherit` (font size) is conveyed only by small adjacent hint labels. Add the same explanation to each spinner's `AccessibleDescription` so screen readers announce it on focus: `_nudBorderThickness.AccessibleDescription = "-1 inherits the global border thickness"`.
  - [ ] **26. Field-level validation feedback** — Entering an invalid hex colour results in the swatch keeping its old colour silently. Add an `ErrorProvider` to each form; call `errorProvider.SetError(txtHex, "Enter a valid hex colour (#RRGGBB)")` when parsing fails and clear it when valid. This provides a visual error icon and an accessible error announcement.

  #### Keyboard completeness
  - [ ] **7. Keyboard accelerators (& labels)** — No `Label` uses `&` prefixes. Add them to the most-used field labels so Alt+letter moves focus to the associated input (requires `TabIndex` on labels to be one less than the input). Example: `"&Label"`, `"&Send"`, `"F&ont"`, `"Font &size"`, `"Font c&olor"`, `"&Key color"`, `"G&roup"`.
  - [ ] **11. Colour swatch panels are mouse-only** — `Panel` controls with `Click` handlers cannot receive keyboard focus or be activated from the keyboard. Replace or wrap each swatch `Panel` with a `Button` subclass (or `FluentButton`) so it participates in Tab order and responds to Space/Enter. The adjacent hex TextBox already allows direct colour entry; the picker button just needs to be keyboard-reachable.
  - [ ] **13. Mode selector buttons (KeyEditorForm) keyboard-inaccessible** — The five send-mode buttons (Text, Key sequence, Modifier, Word prediction, Layout) all have `TabStop = false`. Add `TabStop = true` and a `KeyDown` handler that moves selection with Left/Right arrow keys, matching the standard radio-group keyboard pattern.
  - [ ] **14. Record key button not keyboard-reachable** — `_btnRecord` has `TabStop = false`. Set `TabStop = true`.
  - [ ] **15. Browse / file-picker buttons not keyboard-reachable** — `_btnBrowseLayout` and similar buttons have `TabStop = false`. Set `TabStop = true`.
  - [ ] **16. GroupEditorForm list-action buttons not keyboard-reachable** — Add Group, Delete, Rename, Import buttons all have `TabStop = false`. Set `TabStop = true`. Also consider keyboard shortcuts: Delete key removes the selected group, F2 renames it.

  #### High-contrast mode
  - [ ] **5. High-contrast support — dialogs** — `SystemInformation.HighContrast` is never checked. When true, set `BackColor = SystemColors.Control` and `ForeColor = SystemColors.ControlText` in the form constructors; standard child controls (`TextBox`, `ComboBox`, `NumericUpDown`, `CheckBox`) handle themselves automatically in high-contrast.
  - [ ] **20. WCAG 2.1 AA colour contrast audit** — Several Fluent palette combinations likely fail the 4.5 : 1 normal-text ratio: `Fluent.TextHint` (`#888`) on `Fluent.BgPage` (`#F3F3F3`) is approximately 3.5 : 1 (fails); the red `_lblWPDuplicate` text on the card background may also fail. Audit every text/background pair and adjust the palette where needed.
  - [ ] **21. Disabled state contrast** — `FluentButton` disabled state applies a white 39%-opacity wash that can nearly erase Neutral-variant button labels. Verify `#ABABAB` on white and `#868686` on dark meet the 3 : 1 minimum for UI components and adjust if needed.
  - [ ] **High-contrast support — FluentButton / FluentPainter** — `PaintLight` and `PaintDark` use entirely hardcoded colours. When `SystemInformation.HighContrast` is true both methods should take an early-exit path: fill with `SystemColors.Control`, draw a `SystemColors.ControlText` border via `ControlPaint.DrawBorder`, render text in `SystemColors.ControlText`, and draw a focus rectangle when focused. Subscribe to `SystemEvents.UserPreferenceChanged` to repaint open dialogs if the user switches high-contrast mode while they are open.

  #### Tab order and focus
  - [ ] **6. Explicit TabIndex in logical reading order** — No form sets `TabIndex` explicitly; WinForms uses control-creation order which does not match visual reading order. Assign sequential `TabIndex` values in `BuildUI()` in top-to-bottom, left-to-right order for each form. Required before accelerator keys (item 7) can work, since `Label` + buddy control must be consecutive in tab order.

  #### Scaling and resize
  - [ ] **22. Fixed-size forms clip at high DPI / large fonts** — All three forms use `FormBorderStyle.FixedSingle` with no scroll. If DPI scaling or large system fonts cause controls to overflow their hardcoded positions, the clipped content is unreachable. Either make forms resizable (`FormBorderStyle.Sizable` with minimum size) or wrap each content column in a `Panel` with `AutoScroll = true`.
  - [ ] **23. Validate layout at 125%, 150%, 200% DPI** — `AutoScaleMode.Dpi` scales control sizes proportionally, but the form-height calculations in `BuildUI()` use hardcoded pixel arithmetic. Test at non-100% DPI settings and fix any controls that overlap or get clipped.

  #### Informational / future
  - [ ] **10. Form UI culture for screen reader voice selection** — Screen readers switch speech voice based on the current UI language. Set `Thread.CurrentThread.CurrentUICulture` on startup when a non-English language file is loaded (e.g. `new CultureInfo("nl")` for Dutch) so Narrator selects the correct voice.
  - [ ] **27. Group dropdown: no way to preview group appearance** — The group combo shows raw names ("Red", "Blue"). A screen reader user cannot discover what a group looks like without selecting it. Consider adding a read-only summary label below the combo that updates on selection ("Red group: key #CC2222, font white, Arial 12 pt"), or set `AccessibleDescription` on the combo with a summary of the currently selected group.
  - [ ] **28. DataGridView row descriptions in import dialog** — The DataGridView in `GroupEditorForm`'s import dialog has a Status column ("Conflict" / "New"), which is good. However, row-level `AccessibleObject.Name` is not overridden, so screen readers announce raw cell values. Override `DataGridView.Rows[i].AccessibleObject.Name` to produce a full sentence: "Group Arrows: Conflict — choose Overwrite, Add as new, or Skip".
  - [ ] **29. Touch target sizes** — The `+` group-edit button and colour swatches are 32 × 26 px. Windows and WCAG 2.5.5 recommend 44 × 44 px minimum for touch targets. Not a current concern (mouse/keyboard app), but worth noting if the app is ever used on a tablet.
  - [ ] **30. Right-to-left layout** — `RightToLeft` is not set on any form. The absolute-positioned layouts would mirror incorrectly if RTL translations (Arabic, Hebrew) were ever added. Document this constraint so it is considered before adding new languages.

## Pending — gear button styling (Option D + standard group)

Make the gear button's appearance fully editable by merging the "Default Key Style"
into a protected "standard" group that is the root of the resolution chain.
The gear button always belongs to the standard group; editing it styles the gear.

### Step 1 — Introduce the standard group in the data model and layout files
- Add constant `StandardGroupName = "standard"` to the codebase
- On load: if no group named "standard" exists, create one from the Theme appearance
  fields (FontName, FontSize, FontColor, KeyColor, BorderColor, BorderThickness)
- On save: always write the standard group as a regular `<Group Name="standard" …/>` element
- Migrate all four layout files to include an explicit standard group (values from
  current `<Theme>` style attributes); auto-creation on load becomes a safety net only
- Theme element keeps BackgroundColor and Opacity; its appearance attributes remain
  for backward compatibility but are no longer the source of truth
- **Translations:** no new UI strings in this step; verify existing group-related keys still resolve correctly
- **Tests:** add round-trip tests: load file without standard group → standard group created from Theme values;
  load file with standard group → values preserved; save → standard group written; delete deprecated
  tests that rely on `_theme` appearance fields as the final fallback
- **Run tests:** `dotnet run -- --test`
- **Manual check:** open each of the four layout files; confirm they load without errors and all keys
  look identical to before; save each file and verify a `<Group Name="standard" …/>` element appears in the XML

### Step 2 — Standard group replaces VisualTheme as the resolution root
- Change `ResolveColor`, `ResolveThickness`, and `ApplyPropsToButton` so the final
  fallback is the standard group instead of `_theme` appearance fields
- Keys with `GroupName = ""` fall through to the standard group (same as keys
  explicitly assigned to it)
- Named groups with "inherit" fields resolve to the standard group, not to `_theme`
- Strip appearance fields from `_theme` / `VisualTheme`; it becomes a pure
  window-settings object (BackgroundColor, Opacity, layout metadata only)
- **Translations:** the hint label "(inherit global)" in `GroupEditorForm` stays unchanged for now
  (updated in Step 4); no new strings
- **Tests:** add resolution-chain tests: no-group key resolves to standard group colors; named-group key
  with empty fields resolves through group then standard group; per-key override wins over group and
  standard group; delete or update tests that referenced `_theme.FontColor` / `_theme.KeyColor` etc.
  as the expected fallback value
- **Run tests:** `dotnet run -- --test`
- **Manual check:** open azerty.xml and azertycolor.xml; compare that all keys still show the same
  colors as before this step; open "Edit Keyboard" and confirm the keyboard background color and
  opacity fields still work correctly (those stay in `_theme`)

### Step 3 — Gear button styled by the standard group
- Remove hardcoded `_gearNormalBg` / `_gearNormalFg` color constants
- `ApplyModeIndicators` resolves gear normal-mode colors from the standard group
  (KeyColor → BackColor, FontColor → Tag foreground) via the same path as any key
- Font name taken from standard group; font size stays auto-calculated from cell
  height (fits the overlay cell) until the gear is made fully editable later
- **Translations:** no new strings; verify existing mode-indicator strings (✏, 📌) still display correctly
- **Tests:** add tests that verify the gear Tag foreground color matches the standard group's FontColor
  after `ApplyModeIndicators`; verify that changing the standard group's KeyColor changes the gear's
  BackColor; delete tests that asserted the old hardcoded `_gearNormalBg` / `_gearNormalFg` values
- **Run tests:** `dotnet run -- --test`
- **Manual check:** load azerty.xml (dark teal theme) — gear button should match the teal key color
  instead of the old dark blue-purple; load azertycolor.xml (light grey theme) — gear should match
  the grey background keys; switch to Edit mode and confirm the amber/pencil indicator still appears

### Step 4 — GroupEditorForm: protect standard group and update inherit labels
- Standard group always shown first in the group list (visual indicator, e.g. lock)
- Delete button disabled when standard group is selected
- Rename field disabled / hidden when standard group is selected
- All "inherit" options hidden or disabled for standard group fields (it is the
  root — nothing above it to inherit from)
- "(inherit global)" label changed to "(inherit standard)" for all other groups
- **Translations:** add Dutch translation for any new label or tooltip (e.g. lock indicator tooltip,
  updated "(inherit standard)" hint text); update `lang_nl.xml`
- **Tests:** add tests that verify the standard group cannot be deleted (delete action is blocked);
  verify the standard group cannot be renamed; verify that a non-standard group's "inherit" fields
  resolve to the standard group's values (covered by Step 2 tests, but confirm here with UI-level
  assertions if possible)
- **Run tests:** `dotnet run -- --test`
- **Manual check:** open "Manage Groups" in the keyboard editor; confirm "standard" appears at the top
  with a visual lock indicator; confirm the Delete and Rename buttons are greyed out or hidden when
  "standard" is selected; confirm you can still edit all color and font fields for "standard"; confirm
  that for another group (e.g. "Besturing") the "(inherit standard)" hint appears next to empty fields

### Step 5 — KeyboardEditorForm: retire the "Default Key Style" section
- Remove the "Default Key Style" group box from the keyboard editor
- Add a clearly labelled button "Edit standard group style…" (or repurpose
  "Manage Groups…") that opens `GroupEditorForm` with the standard group pre-selected
- Confirm that "Apply to all keys" iterates `_layout.Cells` only and never touches
  the gear; add a comment to document the exclusion
- **Translations:** add Dutch translations for any new button label or tooltip introduced;
  remove Dutch translations for labels that are removed; update `lang_nl.xml`
- **Tests:** add a test that verifies "Apply to all keys" does not change the gear button's
  appearance (i.e. the standard group's values are not overwritten by the per-key apply action);
  delete tests that referenced the old "Default Key Style" controls directly if any existed
- **Run tests:** `dotnet run -- --test`
- **Manual check:** open "Edit Keyboard"; confirm the old "Default Key Style" section is gone;
  click the new "Edit standard group style…" button and confirm `GroupEditorForm` opens with
  "standard" already selected; change a color in the standard group, click OK, and verify the
  gear button and all ungrouped keys update immediately

### Step 6 — Reserved name enforcement and import handling
- Creating a new group: block the name "standard" with an error message
- Renaming: block "standard" as both source and target
- Import: if the imported file contains a group named "standard", offer it as
  "Update standard group style" with its own conflict option, separate from the
  normal new / conflict / skip flow for regular groups
- **Translations:** add Dutch translations for the new error message (name blocked) and the
  import conflict option ("Update standard group style"); update `lang_nl.xml`
- **Tests:** add tests for: creating a group named "standard" is rejected; renaming any group
  to "standard" is rejected; renaming "standard" to anything else is rejected; importing a file
  with a group named "standard" presents the special update option and applies it correctly
- **Run tests:** `dotnet run -- --test`
- **Manual check:** open "Manage Groups"; try to add a new group named "standard" — confirm the
  error message appears and no group is added; try to rename an existing group to "standard" —
  confirm it is blocked; open a second layout file that has a custom "standard" group with
  different colors, use Import Groups, and confirm the special "Update standard group style"
  option appears and works correctly when selected

---

## Pending — features, ordered by robustness impact

- [ ] **Word prediction — learn new words and word pairs** *(structural — high)* — While the user types (via the keyboard or prediction cells), record new words and word-pair frequencies so the prediction engine improves over time. Store learned data separately from the bundled word database so it survives layout reloads.

- [ ] **Slow keys** *(accessibility — high for target users)* — Add a configurable delay between key press and key activation, so accidental brushes are ignored. Keys must be held for the full duration before the send action fires. Configurable threshold in keyboard settings.

- [ ] **Dwell click** *(accessibility — high for target users)* — Allow a key to be activated by hovering over it for a configurable dwell time, without a physical click. Useful for users with limited motor control. Configurable dwell duration in keyboard settings; visual progress indicator on the key.

- [ ] **Scanning** *(accessibility — medium, consider after dwell + slow keys)* — Auto-advance a highlight through keys/rows at a fixed interval; the user confirms with a single switch input. More complex to implement. Revisit after dwell and slow keys are done.

- [ ] **New keyboard wizard** *(UI/UX — medium)* — When creating a new layout file, ask for the number of rows and columns instead of starting with a blank default. Optionally: include a multiline text field where the user can type or paste the key labels row by row (e.g. `q w e r t y` on one line, `a s d f g h` on the next) and the wizard generates the full grid automatically — each word becomes a key label and send value.

- [ ] **Multi-cell selection for formatting** *(UI/UX — low robustness impact)* — Allow selecting multiple keys at once (e.g. drag-select or Ctrl+click) and applying formatting to all of them in one action. Affects only style (color, font, group) — label and send are untouched.

---

## Completed

### Robustness / code quality ✓

- [x] **Keyboard hook not always uninstalled** — Added `Deactivate` handler that calls `StopRecording()` if the user switches away while recording. Added hook-install failure check in `StartRecording()` — aborts cleanly with a message instead of leaving the UI stuck in "recording" state. `FormClosed` safety net was already in place. ✓
- [x] **Layout save — non-atomic write** — Write to `.tmp` first, then `File.Replace()` (existing file) or `File.Move()` (first save) to swap atomically. `.bak` is now created as part of the atomic operation rather than before the write. 10 automated tests added and passing. ✓
- [x] **Grid validation before file is opened for writing** — `SettingsManager.SaveSettings` now calls `layout.IsValid()` before creating the `.tmp` file and throws `InvalidOperationException` if invalid, so no file is ever touched. Added `try/catch` to delete the `.tmp` on any mid-write failure. 3 automated tests added and passing. ✓
- [x] **Word prediction — graceful failure** — `GetPredictions` split into public try/catch wrapper + `GetPredictionsCore`; `RefreshPredictions` wrapped in try/catch; `ApplyWPTags` split into safe wrapper + `ApplyWPTagsCore`. Keyboard continues in degraded mode (prediction cells blank) on any DB failure. 9 automated tests added and passing. ✓
- [x] **Font disposal in dialogs** — `FontCourier` and `FontPreviewKey` added as shared statics in `FluentTheme` (process lifetime, never disposed). All `new Font("Courier New", 12f)` in colour-row TextBoxes and all `new Font("Arial", 13f, Bold)` for initial preview labels replaced with shared statics. `KeyEditorForm` aligned with `KeyboardEditorForm`'s `_previewFont` field pattern — last dynamic preview font disposed in `FormClosed`. ✓
- [x] **Undo/redo stack — no size cap** — Already implemented: `LinkedList` with `AddFirst` + `RemoveLast` (both O(1)), cap at 50. 3 new assertions added to `T_UndoRedo` verifying count, newest, and oldest entries after 60 pushes. ✓
- [x] **SendKeys stripping must not feed back into send** — Code audit confirmed no bug: `StripSendBraces` is called in exactly 3 places, all inside `DrawChipSection` (toolbar chip renderer), return values used only for display labels, never written back. `KeyProps.Send/ShiftSend/AltGrSend` are plain auto-properties. `EscapeForSend` preserves existing `{KEY}` tokens (no double-escaping). `ToHuman`/`FromHuman` round-trip correctly for prefix sequences. 69 automated tests added. ✓
- [x] **DPI scaling** — `AutoScaleMode = AutoScaleMode.Dpi` and `AutoScaleDimensions = new SizeF(96f, 96f)` added to all three editor dialog constructors (`KeyEditorForm`, `KeyboardEditorForm`, `GroupEditorForm`). 6 automated tests added and passing. ✓
- [x] **ColorDialog / OpenFileDialog not created in Paint or Resize** — Code audit complete. `OnButtonPaint` and `DrawChipSection` allocate `Pen`/`SolidBrush` inside paint handlers but all use `using var` — disposed immediately after each call, no GDI leaks. No dialogs or `Font` allocations found in any high-frequency handler. 2 documentation tests added recording the finding. ✓
- [x] **Language file XML — future-proof against remote sources** — Private `LoadLangXml()` helper added to `LanguageManager`. Both `Load()` and `GetAvailable()` now use it. Protections: (1) 512 KB size gate before parsing; (2) `DtdProcessing.Prohibit` blocks billion-laughs and XXE; (3) `XmlResolver = null` on reader and document; (4) root-element check rejects non-`<Language>` files. `Load()` gained a `try/catch` so corrupt files fall back to English silently. 12 automated tests added. ✓

### UI / UX improvements ✓

- [x] **Application icon** — `icons/onscreenkeyboard.svg` exported to a PNG-in-ICO at 16/32/48/256 px using Inkscape CLI + a Python ICO assembler. Set as `<ApplicationIcon>` in the `.csproj` (embedded in the assembly) and loaded at runtime via `Form.Icon` in `KeyboardForm`. Icon appears in the title bar, taskbar, Alt+Tab switcher, and Windows Explorer. ✓

### Fluent Design / UI overhaul ✓

- [x] **Nicer button visuals — rounded corners and subtle shadow** — `FluentButton` and `ToolbarButton` owner-drawn with rounded corners, hover/press states, and MDL2 icon glyphs. Full Fluent Design overhaul across all dialogs and toolbar. ✓
- [x] **WinUI 3 / Fluent Design aesthetics** — `FluentTheme.cs` with shared color palette, fonts, radii, and icon codepoints (`FIcon`). Light theme for dialogs, dark theme for toolbar. `FluentButton` owner-drawn with Primary / Neutral / Danger / Success variants, hover/press states, rounded corners. `ToolbarButton` owner-drawn with MDL2 icon glyph + text label, dark palette. ✓
- [x] **Key editor redesign** — `KeyEditorForm` rebuilt: 980×560, two-column layout, `ShowIcon = false`, all action buttons Neutral variant, send-mode selector spans full panel width (fits "Woordvoorspelling"), hint text lines removed. ✓
- [x] **Keyboard editor redesign** — `KeyboardEditorForm` rebuilt: 840×560, equal two-column layout, `ShowIcon = false`, all buttons Neutral, file buttons (Save / Save As… / Load…) as clean-keyed `Lang.T()` calls. ✓
- [x] **Group editor redesign** — `GroupEditorForm` rebuilt: 880×610, `ShowIcon = false`, all buttons Neutral. ✓
- [x] **Ghost-paint fix** — Picker panels (`_pnlKeyPicker`, `_pnlLayoutPicker`, `_pnlModPicker`) use `Fluent.BgCard` (white) so they match the card interior painted by `PaintCard`; suppressed `OnPaintBackground` in `FluentButton`/`ToolbarButton` + `g.Clear(parentBg)` to stop white buildup on dark toolbar. ✓
- [x] **Mode buttons unified color** — All five send-mode buttons use `Variant.Primary` (blue) when selected. ✓
- [x] **`ShowIcon = false` on all dialogs** — Removes the default .NET app icon from `FormBorderStyle.FixedSingle` title bars. ✓

### Translation system ✓

- [x] **Toolbar buttons translatable** — All 22 `MakeBtn()` calls in `KeyboardForm.cs` switched from hardcoded English strings to `Lang.T("tb: ...")` keys. `RefreshToolbarButtonLabels()` now updates both button text and tooltips on language change. ✓
- [x] **Dialog button keys fixed** — `Lang.T("Apply")`, `Lang.T("Cancel")`, `Lang.T("Save")`, `Lang.T("Save As…")`, `Lang.T("Load…")`, `Lang.T("Import")` added as clean entries to `_en` dict in `LanguageManager.cs`; previously code called clean keys but dict only had emoji-prefixed versions (`"✔ Apply"`, `"💾 Save"`, etc.). ✓
- [x] **`lang_nl.xml` cleaned up** — 33 deprecated entries removed (old emoji-keyed toolbar labels, old emoji Apply/Cancel, hint text lines, removed grid context menu, old Import buttons, duplicate `"Preview"`). 8 missing translations added (`"Layout"`, `"Record key / shortcut"`, `"Press key now…"`, `"Browse (Send/Shift-send/AltGr-send)"`, `"Press Record to …"`). ✓

### UI / UX improvements

- [x] **Better cursor in format-painter mode** — Windows hand cursor (`Cursors.Hand`) used for both format-painter and key-copy paint modes. ✓
- [x] **Gear button: open toolbar, not dropdown** — Left-click toggles Edit mode; right-click shows minimal menu with "Move gear button…" only. Edit Keyboard moved to toolbar. ✓
- [x] **Gear button hold-to-edit (optional)** — 1-second hold required when enabled; button darkens while held; toggleable in Edit Keyboard → Accessibility. Saved in XML. ✓
- [x] **Remove right-click / button dropdowns in edit mode** — Single click selects, double click opens editor, toolbar handles all actions. ✓
- [x] **Remove delete confirmations in key editor** — All 7 confirmation dialogs removed; undo covers recovery. ✓
- [x] **Format-painter copy-paste (click-to-apply, no extra button)** — "Paste fmt" button removed; "Copy fmt" enters paint mode (blue highlight, crosshair cursor); clicking any key applies formatting; Escape or second click cancels. ✓
- [x] **Redraw performance — eliminate slow/hesitant repaints** — Four fixes: `WS_EX_COMPOSITED` batches child-window paints; `SuspendLayout`/`ResumeLayout` in `LayoutButtons` and `RefreshAllButtons`; `skipFontCalc` parameter skips redundant `TextRenderer.MeasureText` pass when fonts were already set by the preceding `LayoutButtons()` call; `UpdateCornerTag` deduplication removes double `Invalidate` per button. ✓

### Structural improvements

#### Word prediction slots ✓
- [x] **Word prediction slot — auto-assign next free number** — When adding a prediction cell, instead of warning that a slot number is already in use, automatically assign the next available slot number. Slot NUD hidden; no manual choice needed. ✓
- [x] **Word prediction slot — renumber on remove** — When a prediction cell is removed or copy-pasted, renumber all WP cells in left-to-right, top-to-bottom grid order so slots are always contiguous starting at 0. ✓

#### Layout switching from a key ✓
- [x] New "Layout" send mode in key editor. Primary/Shift/AltGr sends each independently load a file (`layout:math.xml`). Path resolved relative to current layout dir, then app dir, then absolute. Flash red on missing file. Undo stack cleared on switch. ✓

### Security / robustness

#### XML file tampering ✓
- ~~Negative or zero GridRows/GridCols~~ — clamped ≥1, default 2, max 50 ✓
- ~~ColSpan/RowSpan = 0 or negative~~ — `Math.Clamp(rs,1,gridRows)` ✓
- ~~Malformed color values~~ — `ParseColor` already has try/catch, returns fallback ✓
- ~~Extremely large ColSpan/RowSpan~~ — same `Math.Clamp` covers this ✓
- ~~FontSize = 0 or negative per key~~ — `Math.Clamp(fs,0,72)` on key load; 0 = inherit is valid ✓
- ~~Invalid grid coordinates~~ — `Debug.WriteLine` warning + `continue` ✓
- ~~Overlapping cells~~ — second key at same position is skipped; `Debug.WriteLine` warning ✓
- ~~Missing Row/Col attribute~~ — detected explicitly; `Debug.WriteLine` warning ✓
- ~~Global/group FontSize unclamped~~ — both clamped to [0, 200] ✓
- ~~ColSpan overflow past right edge~~ — span clamped to `gridCols - c` after bounds check ✓
- ~~WindowWidth/Height no ceiling~~ — max 7680 × 4320; out-of-range values ignored ✓
- ~~Duplicate group names~~ — second entry silently skipped; `Debug.WriteLine` warning ✓
- ~~`doc.Load()` not wrapped in try/catch~~ — all callers already wrap in try/catch ✓
- ~~`Send` field intentionally unsanitized~~ — documented in README (EN + NL) ✓
- ~~Safe mode load flag~~ — decided against; README advises manual inspection instead ✓

### Structural improvements

#### E. Import groups from another layout ✓
- [x] `Import...` button added to `GroupEditorForm` list panel
- [x] File picker → `SettingsManager.LoadGroupsFromFile()` parses `<Group>` elements (new and old XML format)
- [x] DataGridView conflict table: new groups (green) / conflicting groups (orange) with per-row action ComboBox
- [x] Actions: Overwrite / Add as new (auto-numbered) / Skip
- [x] Dutch translations added to `lang_nl.xml`
- [x] All 1144 tests pass

#### D. XML readability — `<Theme>` / `<Layout>` sections ✓
- [x] `SettingsManager.SaveSettings`: writes `<Theme>` (visual + groups) and `<Layout>` (structure + keys)
- [x] `SettingsManager.LoadSettings`: reads new format; backward-compatible fallback for old `<Global>` format
- [x] All four layout files converted: azerty.xml, azertycolor.xml, qwerty.xml, math.xml

#### A. GlobalSettings split ✓
Split `GlobalSettings` into `VisualTheme`, `WindowState`, and `LayoutMeta`.
- [x] Create the three new classes in separate files
- [x] Replace `_global` in KeyboardForm, KeyboardEditorForm, KeyEditorForm, SettingsManager
- [x] SettingsManager: read/write still uses single `<Global>` XML element (no file format change)
- [x] KeyboardEditorForm: UI sections split to match the three classes
- [x] All existing XML files load and save correctly

#### B. Sparse XML format ✓
- [x] `SettingsManager.SaveSettings`: skip pure spacers (no label, no send, no style overrides, 1×1)
- [x] `SettingsManager.LoadSettings`: auto-fill any grid positions not covered by an explicit `<Key>` element

#### C. Style groups (named key groups) ✓
Inheritance chain: Global → Group → Per-key.
- [x] New class `KeyGroup`: Name, KeyColor, FontColor, BorderColor, BorderThickness, FontName, FontSize
- [x] `KeyProps` gets `GroupName` field; `GridLayout` gets `List<KeyGroup> Groups`
- [x] SettingsManager reads/writes `<Group>` elements and `GroupName` attribute
- [x] `KeyboardForm` rendering resolves colors through group layer
- [x] `KeyEditorForm`: group selector dropdown
- [x] `GroupEditorForm`: full group editor (add/remove/rename, color pickers)
- [x] `KeyboardEditorForm`: Manage Groups button + load-refresh callback
- [x] Dutch translations for all group-related strings
- [x] 26 unit tests added
- [x] Groups added to all layout files: azerty.xml, azertycolor.xml, qwerty.xml, math.xml

### Toolbar implementation

- [x] **Step 1** — Toolbar shell (Panel docked top, visible in Edit/QuickEdit only, keys reflow)
- [x] **Step 2** — Mode toggle buttons (Edit, Quick, Exit)
- [x] **Step 3** — Load and Save buttons + filename display label
- [x] **Step 4** — Selected key concept (`_selectedCell`, highlight, label in toolbar)
- [x] **Step 5** — Key action buttons (Edit, Remove, Copy fmt, Copy key)
- [x] **Step 6** — Grid action buttons (add/remove row/col, merge, split)
- [x] **Step 7** — Undo/Redo infrastructure (snapshot stack, PushUndo before every destructive edit)
- [x] **Step 8** — Undo/Redo buttons (in toolbar, greyed when stack empty)
- [x] **Step 9** — Full key copy/paste (content + formatting)
- [x] **Step 10** — Zoom presets (not needed)

### Bug fixes
- [x] Fix dead cells after span shrink in SwapCells (drag)
- [x] Fix dead cells when resizing a cell via key editor
- [x] Fix dead cells when expanding a cell span (AbsorbCoveredCells)
- [x] Fix: adding extra word prediction cell does not work
- [x] Add "Remove key" option to edit mode menu
- [x] Add copy & paste formatting (font, colors, border) per key
- [x] Fix: azertycolor.xml missing/duplicate cells
- [x] Fix: azerty.xml duplicate cell at Row 3 Col 13 and missing Row 4 Col 13
- [x] Validate layout on save (block save if IsValid() fails)
- [x] Fix: groups from one layout persisting after loading a layout with no groups
- [x] Rebuild and test all recent fixes
- [x] **Fix: group changes via + button not applied to other keys in the group** — `OpenEditor` only called `ApplyPropsToButton` for the single edited cell; other keys in the modified group were never repainted. Added `ResultGroupsChanged` property to `KeyEditorForm` (set to `true` in `_btnGroupEdit.Click` when the group editor returns OK); `OpenEditor` now calls `RefreshAllButtons(skipFontCalc: true)` when groups were changed, so all keys pick up the new group colours. ✓
- [x] **Fix: test run opens a new console window since WinExe change** — With `OutputType=WinExe` the process no longer inherits the parent terminal's console, so `AllocConsole()` was creating a new window every time. Replaced with `AttachConsole(ATTACH_PARENT_PROCESS)` to reuse `dotnet run`'s existing terminal; `AllocConsole()` kept as fallback when launched without a parent console (e.g. double-click), in which case `Console.ReadKey()` still pauses before the window closes. ✓
- [x] **Fix: WP duplicate warning was dead code; replaced with "all slots full" warning** — `CheckWPDuplicate` / `_lblWPDuplicate` removed (slot NUD is always hidden; auto-assignment made duplicates impossible via normal use). `RebuildAllButtons` now calls `NormaliseWPSlots()` first so hand-edited XML with duplicate or out-of-order WP slots is silently corrected on load. If a layout has more than 10 WP cells after renumbering, a `Debug.WriteLine` warning is logged (slots ≥ 10 are silently non-functional). In `KeyEditorForm`, switching to WP mode when all 10 slots (0–9) are already taken by other keys now shows a red warning label: "All 10 word-prediction slots are already in use — this key will not function" (Dutch: "Alle 10 voorspellingsslots zijn al in gebruik — deze toets werkt niet"). ✓

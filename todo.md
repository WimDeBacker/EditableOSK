# OnScreenKeyboard – Todo List

---

## Completed — bug fixes

- [x] **Window size inflated after closing in Edit mode** — `ResizeEnd` and `FormClosing` now both save `Height - ToolbarHeightForMode(_mode)` to `WindowState.WindowHeight`, so the stored value is always the Normal-mode equivalent regardless of which mode is active when the user resizes or closes. ✓

---

## Pending — UI / UX improvements

---

## Pending — accessibility improvements

  #### Priority 1 — Keyboard reachability (WCAG 2.1 A §2.1.1) ✓
  - [x] **13. Mode selector buttons (KeyEditorForm) keyboard-inaccessible** — `TabStop = true` set in `MakeModeBtn`; Left/Right arrow key handler wired to all five buttons in `AddOption3ModeSelector` (radio-group keyboard pattern). ✓
  - [x] **14. Record key button not keyboard-reachable** — `TabStop = true` set on `_btnRecord`. ✓
  - [x] **15. Browse / file-picker buttons not keyboard-reachable** — `TabStop = true` set on `_btnBrowseLayout` (KeyEditorForm) and in `MakeFileBtn` (KeyboardEditorForm). ✓
  - [x] **16. GroupEditorForm list-action buttons not keyboard-reachable** — `TabStop = true` set in `MakeSmallBtn` (covers Add, Delete, Import). `KeyDown` handler on `_lstGroups`: Delete removes the selected group; F2 moves focus to `_txtName` with SelectAll for inline rename. ✓
  - [x] **11. Colour swatch panels are mouse-only** — All three `AddColorRow` helpers (KeyEditorForm, GroupEditorForm, KeyboardEditorForm) now create a `Button` (`FlatStyle.Flat`, `TabStop = true`) instead of a `Panel`. `Button` participates in Tab order and activates the colour picker on Space/Enter natively. Field types and helper method signatures updated to `Button`/`Control` accordingly. ✓

  #### Priority 2 — Focus visibility (WCAG 2.1 AA §2.4.7) ✓
  - [x] **3. Focus ring on FluentButton** — `OnGotFocus`/`OnLostFocus` added to trigger repaints. `OnPaint` now draws a 2 px solid focus ring after `PaintLight`: accent blue on Neutral (grey) buttons, white on coloured variants (Primary/Danger/Success) — matches WinUI 3 focus style. `ShowFocusCues` gates it so the ring only appears during Tab/keyboard navigation, never on mouse click. New `ColorSwatchButton` class added to `FluentButton.cs`: replaces the plain `Button` in all three `AddColorRow` helpers; draws a two-tone white-outer/dark-inner ring visible against any swatch colour. `ApplyDialogTheme` swatch-detection updated to `ColorSwatchButton`. ✓

  #### Priority 3 — Tab order + keyboard accelerators ✓
  - [x] **6. Explicit TabIndex in logical reading order** — `AddFieldLabel` now returns `Label`; `AddColorRow` accepts `ref int ti`; `AddOption3ModeSelector` and `AddOption3PickerRow` accept `ref int ti`. All three forms (KeyEditorForm, GroupEditorForm, KeyboardEditorForm) assign sequential `TabIndex` values in `BuildUI()` in top-to-bottom, left-to-right order. Card panels get form-level TabIndex (left before right); action buttons come last. ✓
  - [x] **7. Keyboard accelerators (& labels)** — Key labels updated with `&` mnemonics throughout all three forms. KeyEditorForm: `&Label` (L), `&Send` (S), `&AltGr label` (A), `&Key width` (K), `Key h&eight` (E), `&Font` (F), `Font &size` (S), `Font c&olor` (O), `&Key color` (K), `&Border color` (B), `Border &thickness` (T), `&Group` (G). GroupEditorForm: `&Name`, `&Font`, `Font si&ze`, `Font c&olor`, `&Key color`, `&Border color`, `Border &thickness`. KeyboardEditorForm: `&Opacity`, `&Background`, `&Always on top`, `H&ide title bar`, `&Toolbar theme`, `&Save`, `Save &As…`, `&Load…`, `&Sticky modifiers`, `H&old to edit`. ✓

  #### Priority 4 — Screen-reader annotations ✓
  - [x] **4. AccessibleName on every interactive control** — `FluentButton.Text` setter override auto-syncs `AccessibleName` (strips `&`). `AddFieldLabel` sets `_pendingAccessibleName`; `AddInput` and `AddColorRow` consume it automatically. All remaining inline controls (`ComboBox`, `NumericUpDown`, `TrackBar`, `ListBox`, `TextBox`) receive explicit `AccessibleName` at construction. `_txtSend.AccessibleName` is kept in sync with its dynamic label in `SetSendMode`. ✓
  - [x] **24. Tooltips on nearly all controls** — `ToolTip _tip` added to all three editor forms; `SetTip(ctrl, lambda)` helper registers and refreshes on language change. New `tip:` lang keys added for: colour swatches, hex boxes, font-size / border-thickness / key-width / row-span spinners, Record button, Browse layout button, all five mode buttons, Add/Delete/Import group buttons, Opacity trackbar, Manage Groups button, Language combo, WP slot spinner. Dutch translations added to `lang_nl.xml`. ✓
  - [x] **25. Sentinel values in AccessibleDescription** — `_nudFontSize.AccessibleDescription = Lang.T("0 = auto / inherit")` and `_nudBorderThickness.AccessibleDescription = Lang.T("-1 = inherit standard")` set in both `KeyEditorForm` and `GroupEditorForm`. Screen readers now announce the sentinel meaning when the spinner receives focus. ✓

  #### Priority 5 — Error announcements ✓
  - [x] **26. Field-level validation feedback** — `ErrorProvider _err` added to `KeyEditorForm`, `GroupEditorForm`, and `KeyboardEditorForm`. `_err.SetError(txtHex, Lang.T("err: invalid hex"))` called in each `AddColorRow` `TextChanged` handler when the text is non-empty and unparseable; cleared on valid or empty input. Lang key `"err: invalid hex"` added in English and Dutch. 4 automated tests added, 1372/1372 passing. ✓

  #### Priority 6 — Colour contrast & high-contrast mode ✓
  - [x] **20. WCAG 2.1 AA colour contrast audit** — `Fluent.TextHint` was `#A0A0A0` (2.62:1 on white — fails). Darkened to `#646464` (5.93:1 on white, 5.34:1 on `#F3F3F3` — passes AA). `Danger` on `BgCard` = 5.2:1 ✓. `TextPrimary`/`TextSecondary` already passed. `_lblWPDuplicate` was removed in an earlier session. 8 automated contrast-ratio tests added, 1380/1380 passing. ✓
  - [x] **21. Disabled state contrast** — WCAG 1.4.3 and 1.4.11 explicitly exempt inactive UI components. No code change required; documented. ✓
  - [x] **5. High-contrast support — dialogs** — `FluentPainter.ApplyDialogTheme` now checks `SystemInformation.HighContrast`; when true, applies `SystemColors.*` throughout the control tree via new `ApplyHCChildren` helper. All three forms (`KeyEditorForm`, `GroupEditorForm`, `KeyboardEditorForm`) subscribe to `SystemEvents.UserPreferenceChanged` → re-call `ApplyDialogTheme` via `BeginInvoke` so open dialogs re-theme immediately when HC mode is toggled live. Unsubscribe in `FormClosed`. ✓
  - [x] **High-contrast support — FluentButton / FluentPainter** — `PaintLight` and `PaintDark` take an early-exit path when HC: flat `SystemColors.Control` fill, `ControlText` border via `ControlPaint.DrawBorder`, `ControlText`/`GrayText` text, `Highlight`/`HighlightText` for active toolbar buttons. `PaintCard` returns immediately in HC so panels show their system-colour `BackColor`. Focus rings use `ControlPaint.DrawFocusRectangle` in HC mode. `FluentButton` and `ToolbarButton` subscribe to `UserPreferenceChanged` → `BeginInvoke(Invalidate)`; unsubscribe in `Dispose`. ✓

  #### Priority 7 — Rich widget descriptions ✓
  - [x] **9. Preview panel not described** — `_pnlPreview.AccessibleName` is now updated on every `Refresh2()` call to a full sentence e.g. `"Preview: key 'A', key colour #2D2D4A, font colour #E0E0FF, Arial 13 pt"`. Lang key `"preview: key '{0}', …"` added in English and Dutch. ✓
  - [ ] **27. Group dropdown: no way to preview group appearance** — The group combo shows raw names ("Red", "Blue"). A screen reader user cannot discover what a group looks like without selecting it. Consider setting `AccessibleDescription` on the combo with a summary of the currently selected group (without a visible label, which clutters the UI).
  - [x] **28. DataGridView row descriptions in import dialog** — New `DescribedRow` class (in `GroupEditorForm.cs`) subclasses `DataGridViewRow` and overrides `CreateAccessibilityInstance()` to return a custom `AccessibleObject` whose `Name` is the full sentence: `"Group Arrows: Conflict — choose Overwrite, Add as new, or Skip"` / `"…: New — will be added"` / `"…: Protected — choose Update or Skip"`. All import rows now use `DescribedRow`. Lang keys added in English and Dutch. 16 new tests added, 1396/1396 passing. ✓

  #### Priority 8 — DPI scaling ✓
  - [x] **22. Fixed-size forms clip at high DPI / large fonts** — All three forms changed from `FormBorderStyle.FixedSingle` to `Sizable`. A `Load` handler clamps the form to `Screen.WorkingArea - 10px` so it always fits on screen at any DPI. All content (card panels + buttons) lives in a single `DockStyle.Fill` `Panel` with `AutoScroll = true` and `AutoScrollMinSize` set to the full designed form size. Scrollbars appear the moment the form is 1 px smaller than the designed layout in either dimension; both scrollbars appear simultaneously when needed. `MinimumSize` set to `(min(Width,480), min(Height,320))` after the Load clamp so the user can always resize smaller. 1398/1398 tests passing. ✓
  - [x] **23. Validate layout at 125%, 150%, 200% DPI** — `AutoScaleMode.Dpi` + `AutoScaleDimensions = new SizeF(96f, 96f)` was already in place on all three forms; combined with the `Sizable` + `DockStyle.Fill` scroll panel + `Load`-time screen-clamp, controls scale proportionally and remain reachable at any DPI via scrolling. Manual verification protocol: open each dialog at 125%, 150%, 200% Windows display scale and confirm no controls are clipped and scrollbars appear as expected. ✓

  #### Future / informational
  - [ ] **10. Form UI culture for screen reader voice selection** — Screen readers switch speech voice based on the current UI language. Set `Thread.CurrentThread.CurrentUICulture` on startup when a non-English language file is loaded (e.g. `new CultureInfo("nl")` for Dutch) so Narrator selects the correct voice.
  - [ ] **29. Touch target sizes** — The `+` group-edit button and colour swatches are 32 × 26 px. Windows and WCAG 2.5.5 recommend 44 × 44 px minimum for touch targets. Not a current concern (mouse/keyboard app), but worth noting if the app is ever used on a tablet.
  - [ ] **30. Right-to-left layout** — `RightToLeft` is not set on any form. The absolute-positioned layouts would mirror incorrectly if RTL translations (Arabic, Hebrew) were ever added. Document this constraint so it is considered before adding new languages.

---

## Pending — code quality

- [ ] **Audit duplicate code across the three editor forms** — `KeyEditorForm`, `GroupEditorForm`, and `KeyboardEditorForm` each contain their own copies of `AddColorRow`, `AddFieldLabel` / `AddLabel`, `SetTip`, `_pendingAccessibleName`, `_transLabels`, `_transTooltips`, `ParseColor` / `TryParseHex`, `AddGroup` / `AddPanel`, `MakeActionBtn` / `MakeBigBtn` / `MakeSmallBtn` / `MakeFileBtn`, and `FluentPainter.ApplyDialogTheme` calls. Some of these are near-identical (all three `AddColorRow` implementations differ only in minor details). Consider extracting shared logic into a base class `FluentDialogBase` or a static `DialogBuilder` helper, so future accessibility or styling changes only need to be made in one place.

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

### Gear button styling (Option D + standard group) ✓

- [x] **Step 1** — Standard group introduced in data model and layout files; `StandardGroupName = "standard"` constant; auto-created with neutral defaults on load if missing; written as `<Group Name="standard" …/>` on save; all four layout files migrated. ✓
- [x] **Step 2** — Standard group replaces VisualTheme as resolution root; `ResolveColor`, `ResolveThickness`, `ResolveFontName` fall back to standard group via `Std*` helpers; `VisualTheme` stripped to window-settings only. ✓
- [x] **Step 3** — Gear button styled by standard group; hardcoded `_gearNormalBg`/`_gearNormalFg` removed; `ApplyModeIndicators` reads `StdKeyColor`/`StdFontColor`; stopEditing.svg icon (30 px) used in Edit mode. ✓
- [x] **Step 4** — GroupEditorForm protects standard group: 🔒 prefix in listbox, name field and delete button locked, border Minimum=0, hint hidden, font item 0 = "(none / auto)", colour clear menus = "Clear"; non-standard groups show "(inherit standard)" throughout. Dutch translations added. ✓
- [x] **Step 5** — KeyboardEditorForm: Default Key Style section removed; Key Groups card removed entirely (groups are a per-key concern, not keyboard-level); layout reorganized to Left=Language+Window, Right=Layout file+Accessibility; KeyEditorForm: `+` button replaced with full-width "Manage Groups…" button below the group combo (pre-selects the active group, opens GroupEditorForm); 6 new tests, 1314/1314 passing. ✓
- [x] **Step 6** — Reserved name enforcement: "standard" blocked in Add (inline error label in New Group dialog) and Rename (live inline error label below Name field; `CommitTo` safety net; `SaveCurrentName` deferred-write fix so partial typing never corrupts the data model); import gets a special light-blue "Protected" row with "Update standard group style" / "Skip" options; `ApplyImportDecisions` internal method; `CommitToResult`, `TryAddGroup`, `TryRenameCurrentGroup` test-seam methods; Dutch translations; 22 new tests, 1334/1334 passing. ✓
- [x] **Code review & fixes** — Three `ContextMenuStrip` instances in `GroupEditorForm` now disposed in `FormClosed`; group edits made via "Manage Groups…" inside `KeyEditorForm` now correctly repaint the keyboard even when the key edit is cancelled (`RebuildGroupDict` + `RefreshAllButtons` on cancel if `ResultGroupsChanged`); XML doc comments added to `BuildTitle`, `RefreshAppearanceFromGroupCore` (KeyEditorForm). ✓

### UI / UX improvements ✓

- [x] **Grow window height when entering edit mode** — `ToolbarHeightForMode()` computes the toolbar-height delta per mode transition; `Height += delta` is applied before `ApplyModeIndicators()`. A `_inModeTransition` flag suppresses the spurious `SizeChanged → LayoutButtons()` call during the programmatic resize so only one layout pass runs — the one inside `ApplyModeIndicators()` — at which point both the new height and the new toolbar visibility are already set. Result: key sizes stay constant; transition is a single-frame jump with no intermediate paint. 9 tests added, 1343/1343 passing. ✓
- [x] **Group editor — hex fields and field order** — `GroupEditorForm` colour rows now show a 32 px swatch + hex text box, matching `KeyEditorForm`. `SetSwatchColor` writes the hex box; `GetSwatchColor` reads back via `TryParseHex`. Field order: Name → Font → Font size → Font color → Key color → Border color → Border thickness. ✓
- [x] **Emoji in dialog title bars** — `TitleSafeLabel` helper strips surrogate pairs and BMP symbol blocks; `BuildTitle` omits brackets when nothing printable remains. ✓
- [x] **"+" button → "Manage Groups…" button in key editor** — Full-width button below the group combo replaces the small `+`; pre-selects the active group when opening `GroupEditorForm`. ✓

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

### Accessibility quick wins ✓

- [x] **1. AcceptButton / CancelButton on main forms** — `AcceptButton = _btnApply; CancelButton = _btnCancel` on `KeyEditorForm` and `KeyboardEditorForm`; `AcceptButton = _btnOK; CancelButton = _btnCancel` on `GroupEditorForm`. Enter and Escape now work on all three forms. ✓
- [x] **2. TabStop on action buttons** — `FluentButton` constructor sets `TabStop = false` globally. Added `TabStop = true` in `MakeActionBtn` (KeyEditorForm, KeyboardEditorForm) and `MakeBigBtn` (GroupEditorForm) so Apply, Cancel, and OK are reachable by Tab. ✓
- [x] **12. CancelButton on sub-dialogs** — Added `dlg.CancelButton = cn` to `GroupEditorForm.GetNewName` and `dlg.CancelButton = btnCancel3` to the import-resolution dialog. Escape now closes both. ✓
- [x] **18. Focus restoration after sub-dialog closes** — `_cmbGroup.Focus()` after `GroupEditorForm` closes from the `+` button in `KeyEditorForm`; `_btnManageGroups.Focus()` after `GroupEditorForm` closes from `KeyboardEditorForm`. ✓
- [x] **19. Initial focus on dialog open** — `ActiveControl = _txtLabel` (KeyEditorForm), `ActiveControl = _cmbFont` (KeyboardEditorForm), `ActiveControl = _txtName` (GroupEditorForm). ✓

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

### UI / UX improvements ✓

- [x] **Better cursor in format-painter mode** — Windows hand cursor (`Cursors.Hand`) used for both format-painter and key-copy paint modes. ✓
- [x] **Gear button: open toolbar, not dropdown** — Left-click toggles Edit mode; right-click shows minimal menu with "Move gear button…" only. Edit Keyboard moved to toolbar. ✓
- [x] **Gear button hold-to-edit (optional)** — 1-second hold required when enabled; button darkens while held; toggleable in Edit Keyboard → Accessibility. Saved in XML. ✓
- [x] **Remove right-click / button dropdowns in edit mode** — Single click selects, double click opens editor, toolbar handles all actions. ✓
- [x] **Remove delete confirmations in key editor** — All 7 confirmation dialogs removed; undo covers recovery. ✓
- [x] **Format-painter copy-paste (click-to-apply, no extra button)** — "Paste fmt" button removed; "Copy fmt" enters paint mode (blue highlight, crosshair cursor); clicking any key applies formatting; Escape or second click cancels. ✓
- [x] **Redraw performance — eliminate slow/hesitant repaints** — Four fixes: `WS_EX_COMPOSITED` batches child-window paints; `SuspendLayout`/`ResumeLayout` in `LayoutButtons` and `RefreshAllButtons`; `skipFontCalc` parameter skips redundant `TextRenderer.MeasureText` pass when fonts were already set by the preceding `LayoutButtons()` call; `UpdateCornerTag` deduplication removes double `Invalidate` per button. ✓

### Structural improvements ✓

#### Word prediction slots ✓
- [x] **Word prediction slot — auto-assign next free number** — When adding a prediction cell, instead of warning that a slot number is already in use, automatically assign the next available slot number. Slot NUD hidden; no manual choice needed. ✓
- [x] **Word prediction slot — renumber on remove** — When a prediction cell is removed or copy-pasted, renumber all WP cells in left-to-right, top-to-bottom grid order so slots are always contiguous starting at 0. ✓

#### Layout switching from a key ✓
- [x] New "Layout" send mode in key editor. Primary/Shift/AltGr sends each independently load a file (`layout:math.xml`). Path resolved relative to current layout dir, then app dir, then absolute. Flash red on missing file. Undo stack cleared on switch. ✓

### Security / robustness ✓

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

### Toolbar implementation ✓

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

### Bug fixes ✓
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

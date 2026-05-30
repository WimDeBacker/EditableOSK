# OnScreenKeyboard – Todo List

## Pending — features, ordered by priority

- [x] **Priority 1 — New keyboard wizard** ✓ *(UI/UX — medium)* — When creating a new layout file, ask for the number of rows and columns instead of starting with a blank default. Optionally: include a multiline text field where the user can type or paste the key labels row by row (e.g. `q w e r t y` on one line, `a s d f g h` on the next) and the wizard generates the full grid automatically — each word becomes a key label and send value.

- [ ] **Priority 2 — Word prediction: learn new words and word pairs** *(structural — high)* — While the user types (via the keyboard or prediction cells), record new words and word-pair frequencies so the prediction engine improves over time. Store learned data separately from the bundled word database so it survives layout reloads.

- [ ] **Priority 2b — English word prediction** *(content)* — Add an English word list / frequency database so word prediction works out of the box for English layouts, not just Dutch.

- [ ] **Priority 2c — Shrink and review the test suite** *(maintenance)* — The test program has grown large and many tests cover the same paths multiple times. Audit which tests are still load-bearing (catch real regressions), which are redundant duplicates, and which are deprecated (testing code that no longer exists or behaviour that has changed). Goal: smaller, faster, easier to maintain suite without losing meaningful coverage.

- [ ] **Priority 3 — Touch target sizes** *(accessibility — low)* — The `+` group-edit button and colour swatches are 32 × 26 px. Windows and WCAG 2.5.5 recommend 44 × 44 px minimum for touch targets. Worth addressing if the app is used on a tablet.

- [ ] **Priority 4 — Scanning** *(accessibility — medium)* — Auto-advance a highlight through keys/rows at a fixed interval; the user confirms with a single switch input. More complex to implement.

- [ ] **Priority 5 — Bundled keyboard layouts** *(content)* — Ship ready-made `.xml` layout files for the four wizard themes (Dark, Light, High Contrast, Colorful) × two key arrangements (AZERTY Dutch, QWERTY English) = 8 files. Users can load one immediately without running the wizard. Layouts should include the standard group colours from each theme preset and a word-prediction row.

---

## Completed

### Accessibility ✓

#### Slow keys & dwell click ✓
- [x] **Slow keys** — Key must be held for a configurable duration (100–3000 ms) before it fires. Amber bottom-up fill animation grows on the key while held. Checkbox + NUD in Edit Keyboard → Accessibility. Saved in `LayoutMeta.SlowKeysMs`. 1503/1503 tests passing. ✓
- [x] **Dwell click (automatische klik)** — Key auto-fires after hovering for a configurable duration (100–5000 ms) without clicking. Same bottom-up fill animation. Mutually exclusive with slow keys — checking one unchecks the other; both NUDs are disabled/greyed when unchecked. Saved in `LayoutMeta.DwellMs`. ✓
- [x] **Accessibility control tests** — `T_AccessibilityControls` verifies `AcceptButton`/`CancelButton`, `TabStop`, `TabIndex` uniqueness, `AccessibleName`, `AccessibleDescription`, and tooltip registration on all three editor forms. Caught a real bug: `AcceptButton`/`CancelButton` were silently cleared by `WrapInScrollPanel` on all three forms — fixed. ✓

#### Priority 1 — Keyboard reachability (WCAG 2.1 A §2.1.1) ✓
- [x] **Mode selector buttons (KeyEditorForm) keyboard-inaccessible** — `TabStop = true` set in `MakeModeBtn`; Left/Right arrow key handler wired to all five buttons in `AddOption3ModeSelector` (radio-group keyboard pattern). ✓
- [x] **Record key button not keyboard-reachable** — `TabStop = true` set on `_btnRecord`. ✓
- [x] **Browse / file-picker buttons not keyboard-reachable** — `TabStop = true` set on `_btnBrowseLayout` (KeyEditorForm) and in `MakeFileBtn` (KeyboardEditorForm). ✓
- [x] **GroupEditorForm list-action buttons not keyboard-reachable** — `TabStop = true` set in `MakeSmallBtn` (covers Add, Delete, Import). `KeyDown` handler on `_lstGroups`: Delete removes the selected group; F2 moves focus to `_txtName` with SelectAll for inline rename. ✓
- [x] **Colour swatch panels are mouse-only** — All three `AddColorRow` helpers now create a `Button` (`FlatStyle.Flat`, `TabStop = true`) instead of a `Panel`. `Button` participates in Tab order and activates the colour picker on Space/Enter natively. ✓

#### Priority 2 — Focus visibility (WCAG 2.1 AA §2.4.7) ✓
- [x] **Focus ring on FluentButton** — `OnGotFocus`/`OnLostFocus` added to trigger repaints. `OnPaint` now draws a 2 px solid focus ring after `PaintLight`: accent blue on Neutral buttons, white on coloured variants. `ShowFocusCues` gates it so the ring only appears during Tab/keyboard navigation. New `ColorSwatchButton` class draws a two-tone white-outer/dark-inner ring visible against any swatch colour. ✓

#### Priority 3 — Tab order + keyboard accelerators ✓
- [x] **Explicit TabIndex in logical reading order** — All three forms assign sequential `TabIndex` values in `BuildUI()` in top-to-bottom, left-to-right order. ✓
- [x] **Keyboard accelerators (& labels)** — Key labels updated with `&` mnemonics throughout all three forms. ✓

#### Priority 4 — Screen-reader annotations ✓
- [x] **AccessibleName on every interactive control** — `FluentButton.Text` setter auto-syncs `AccessibleName`. `AddFieldLabel` sets `_pendingAccessibleName`; `AddInput` and `AddColorRow` consume it automatically. ✓
- [x] **Tooltips on nearly all controls** — `ToolTip _tip` added to all three editor forms; `SetTip(ctrl, lambda)` helper registers and refreshes on language change. Dutch translations added. ✓
- [x] **Sentinel values in AccessibleDescription** — `_nudFontSize.AccessibleDescription` and `_nudBorderThickness.AccessibleDescription` set in both `KeyEditorForm` and `GroupEditorForm`. ✓

#### Priority 5 — Error announcements ✓
- [x] **Field-level validation feedback** — `ErrorProvider _err` added to all three forms. `_err.SetError(txtHex, …)` called in each `AddColorRow` `TextChanged` handler when unparseable. 4 automated tests added, 1372/1372 passing. ✓

#### Priority 6 — Colour contrast & high-contrast mode ✓
- [x] **WCAG 2.1 AA colour contrast audit** — `Fluent.TextHint` darkened from `#A0A0A0` (2.62:1 — fails) to `#646464` (5.93:1 — passes AA). 8 contrast-ratio tests added, 1380/1380 passing. ✓
- [x] **Disabled state contrast** — WCAG 1.4.3 and 1.4.11 explicitly exempt inactive UI components. No code change required; documented. ✓
- [x] **High-contrast support — dialogs** — `ApplyDialogTheme` applies `SystemColors.*` throughout the control tree when HC is active. All three forms subscribe to `SystemEvents.UserPreferenceChanged` to re-theme live. ✓
- [x] **High-contrast support — FluentButton / FluentPainter** — `PaintLight`/`PaintDark` take an early-exit path when HC: flat system-colour fill, `ControlPaint.DrawBorder`, `Highlight`/`HighlightText` for active toolbar buttons. Focus rings use `ControlPaint.DrawFocusRectangle`. ✓

#### Priority 7 — Rich widget descriptions ✓
- [x] **Preview panel not described** — `_pnlPreview.AccessibleName` updated on every `Refresh2()` call to a full sentence, e.g. `"Preview: key 'A', key colour #2D2D4A, font colour #E0E0FF, Arial 13 pt"`. Lang keys added in English and Dutch. ✓
- [x] **DataGridView row descriptions in import dialog** — `DescribedRow` class overrides `CreateAccessibilityInstance()` to return a full-sentence description per row. 16 new tests added, 1396/1396 passing. ✓

#### Priority 8 — DPI scaling ✓
- [x] **Fixed-size forms clip at high DPI / large fonts** — All three forms changed to `Sizable`. A `Load` handler clamps to `Screen.WorkingArea - 10px`. Content lives in a `DockStyle.Fill` panel with `AutoScroll = true`. 1398/1398 tests passing. ✓
- [x] **Validate layout at 125%, 150%, 200% DPI** — `AutoScaleMode.Dpi` + `AutoScaleDimensions = new SizeF(96f, 96f)` already in place; combined with `Sizable` + scroll panel + Load-time screen-clamp, controls scale proportionally. ✓

#### Accessibility quick wins ✓
- [x] **AcceptButton / CancelButton on main forms** — Enter and Escape now work on all three forms. ✓
- [x] **TabStop on action buttons** — Apply, Cancel, and OK are reachable by Tab. ✓
- [x] **CancelButton on sub-dialogs** — Escape closes the New Group dialog and the import-resolution dialog. ✓
- [x] **Focus restoration after sub-dialog closes** — Focus returns to the triggering control when GroupEditorForm closes. ✓
- [x] **Initial focus on dialog open** — `ActiveControl` set to the first logical field in each form. ✓

---

### Bug fixes ✓
- [x] **Window size inflated after closing in Edit mode** — `ResizeEnd` and `FormClosing` both save `Height - ToolbarHeightForMode(_mode)` to `WindowState.WindowHeight`. ✓
- [x] Fix dead cells after span shrink in SwapCells (drag) ✓
- [x] Fix dead cells when resizing a cell via key editor ✓
- [x] Fix dead cells when expanding a cell span (AbsorbCoveredCells) ✓
- [x] Fix: adding extra word prediction cell does not work ✓
- [x] Add "Remove key" option to edit mode menu ✓
- [x] Add copy & paste formatting (font, colors, border) per key ✓
- [x] Fix: azertycolor.xml missing/duplicate cells ✓
- [x] Fix: azerty.xml duplicate cell at Row 3 Col 13 and missing Row 4 Col 13 ✓
- [x] Validate layout on save (block save if IsValid() fails) ✓
- [x] Fix: groups from one layout persisting after loading a layout with no groups ✓
- [x] **Fix: group changes via + button not applied to other keys in the group** — Added `ResultGroupsChanged` property to `KeyEditorForm`; `OpenEditor` now calls `RefreshAllButtons` when groups were changed. ✓
- [x] **Fix: test run opens a new console window since WinExe change** — Replaced `AllocConsole()` with `AttachConsole(ATTACH_PARENT_PROCESS)` to reuse the parent terminal. ✓
- [x] **Fix: WP duplicate warning was dead code** — Replaced with "all slots full" warning label; `NormaliseWPSlots()` called on load. ✓

---

### Gear button styling (Option D + standard group) ✓

- [x] **Step 1** — Standard group introduced in data model and layout files; `StandardGroupName = "standard"` constant; auto-created with neutral defaults on load if missing. ✓
- [x] **Step 2** — Standard group replaces VisualTheme as resolution root; `ResolveColor`, `ResolveThickness`, `ResolveFontName` fall back to standard group; `VisualTheme` stripped to window-settings only. ✓
- [x] **Step 3** — Gear button styled by standard group; hardcoded colours removed; `stopEditing.svg` icon used in Edit mode. ✓
- [x] **Step 4** — GroupEditorForm protects standard group: 🔒 prefix, name/delete locked, special colour menus. ✓
- [x] **Step 5** — KeyboardEditorForm reorganised; `+` button replaced with full-width "Manage Groups…" button in KeyEditorForm. ✓
- [x] **Step 6** — Reserved name enforcement: "standard" blocked in Add and Rename; import gets a "Protected" row. ✓
- [x] **Code review & fixes** — Three `ContextMenuStrip` instances disposed in `FormClosed`; group edits via "Manage Groups…" correctly repaint on cancel. ✓

---

### UI / UX improvements ✓

- [x] **Grow window height when entering edit mode** — `ToolbarHeightForMode()` computes the delta; `_inModeTransition` flag suppresses spurious layout passes. ✓
- [x] **Group editor — hex fields and field order** — Colour rows now show swatch + hex box. Field order: Name → Font → Font size → Font color → Key color → Border color → Border thickness. ✓
- [x] **Emoji in dialog title bars** — `TitleSafeLabel` strips surrogate pairs and BMP symbol blocks. ✓
- [x] **"+" button → "Manage Groups…" button in key editor** ✓
- [x] **Better cursor in format-painter mode** — `Cursors.Hand` for both format-painter and key-copy paint modes. ✓
- [x] **Gear button: open toolbar, not dropdown** — Left-click toggles Edit mode; right-click shows minimal menu. ✓
- [x] **Gear button hold-to-edit (optional)** — 1-second hold required when enabled; toggleable in Edit Keyboard → Accessibility. ✓
- [x] **Remove right-click / button dropdowns in edit mode** ✓
- [x] **Remove delete confirmations in key editor** — All 7 confirmation dialogs removed; undo covers recovery. ✓
- [x] **Format-painter copy-paste (click-to-apply, no extra button)** ✓
- [x] **Redraw performance** — `WS_EX_COMPOSITED`, `SuspendLayout`/`ResumeLayout`, `skipFontCalc` parameter, `UpdateCornerTag` deduplication. ✓
- [x] **Application icon** — PNG-in-ICO at 16/32/48/256 px; set as `<ApplicationIcon>` and loaded at runtime. ✓

---

### New keyboard wizard ✓

- [x] **Wizard built and shipped** — 4-page wizard: starting point (blank / paste / copy), grid & labels, theme picker, save. Auto-classifies pasted keys into groups (Klinkers, Medeklinkers, Cijfers, Besturing, Leestekens, Woord). Four built-in themes with per-group colours. Language selector on page 1. ✓
- [x] **Wizard dark-theme fixes** — `ApplyTheme()` override wired; `RadioButton` and `Button` added to `ApplyThemeChildren`/`ApplyHCChildren`; `OnSummaryPaint` uses `_dark`-conditional brush; checkmark labels restored to accent colour after theme walk. ✓
- [x] **Besturing contrast** — Dark preset `9090A8` → `B0B0C8` (1.92:1 → ~8:1); Light preset `505050` → `3C3C3C` (3.1:1 → ~9:1). Both now pass WCAG AA while remaining visually quieter than letter keys. ✓
- [x] **Nav button positioning** — `PositionNavButtons()` called from `ApplyTheme()` (= Load) so buttons are right-aligned from first paint, not only after the first user resize. ✓
- [x] **Font-dispose crash on second open** — `using var fnt = Fluent.FontLabel` in `OnSummaryPaint` was disposing the shared static font; changed to plain `var`. ✓

---

### FluentDialogBase — full form inheritance ✓

- [x] **GroupEditorForm, KeyEditorForm, KeyboardEditorForm** all migrated from `Form` to `FluentDialogBase`. Removed ~300 lines of duplicated infrastructure (dark field, tip, err, pref-changed handler, trans-lists, AddGroup, AddColorRow, SetTip, MakeActionBtn, ParseColor, WrapInScrollPanel, screen-clamp Load handler). ✓
- [x] **Code-review findings fixed** — `GroupEditorForm._lblNameError` red colour restored in `ApplyTheme()` override; `SetSwatchHex` sets `_suppressOnChanged` flag so three colour-swatch loads no longer fire three spurious `Refresh2()` calls; `DataGridView` column headers themed under HC mode; stale `<see cref="RelabelUI"/>` comment corrected. ✓
- [x] **Bug fixes (pre-refactor)** — BUG 1 ToolTip leak, BUG 2 Load discards edits, BUG 3 dual timers, BUG 4 `ShowTimingAnimation` flag, BUG 5 animation checkbox enabled state, BUG 6 dead test variable — all resolved. ✓

---

### Code quality ✓

- [x] **FluentDialogBase refactoring** — Extracted `AddColorRow`, `AddFieldLabel`, `SetTip`, `_pendingAccessibleName`, `_transLabels`/`_transTooltips`/`_transGroups`, `ParseColor`, `AddGroup`, `MakeActionBtn`, `WrapInScrollPanel`, DPI scaling, screen-clamp, HC live-update, and language-change refresh into `FluentDialogBase`. All four dialog forms (KeyEditorForm, GroupEditorForm, KeyboardEditorForm, NewKeyboardWizard) inherit it. ✓

---

### Robustness / code quality ✓

- [x] **Keyboard hook not always uninstalled** — `Deactivate` handler calls `StopRecording()`; hook-install failure aborts cleanly. ✓
- [x] **Layout save — non-atomic write** — Write to `.tmp` first, then `File.Replace()` / `File.Move()` to swap atomically. ✓
- [x] **Grid validation before file is opened for writing** — `layout.IsValid()` checked before creating `.tmp`. ✓
- [x] **Word prediction — graceful failure** — Keyboard continues in degraded mode on any DB failure. ✓
- [x] **Font disposal in dialogs** — `FontCourier` and `FontPreviewKey` added as shared statics in `FluentTheme`. ✓
- [x] **Undo/redo stack — no size cap** — `LinkedList` with cap at 50; O(1) add and trim. ✓
- [x] **SendKeys stripping must not feed back into send** — Code audit confirmed no bug; 69 automated tests added. ✓
- [x] **DPI scaling** — `AutoScaleMode.Dpi` + `AutoScaleDimensions` added to all three editor dialog constructors. ✓
- [x] **ColorDialog / OpenFileDialog not created in Paint or Resize** — Code audit complete; no GDI leaks found. ✓
- [x] **Language file XML — future-proof against remote sources** — `DtdProcessing.Prohibit`, `XmlResolver = null`, 512 KB size gate, root-element check. 12 automated tests added. ✓

---

### Translation system ✓

- [x] **Toolbar buttons translatable** — All 22 `MakeBtn()` calls switched to `Lang.T("tb: ...")` keys. ✓
- [x] **Dialog button keys fixed** — Clean `Lang.T()` keys added for Apply, Cancel, Save, Save As…, Load…, Import. ✓
- [x] **`lang_nl.xml` cleaned up** — 33 deprecated entries removed; 8 missing translations added. ✓

---

### Fluent Design / UI overhaul ✓

- [x] **FluentButton and ToolbarButton** — Owner-drawn with rounded corners, hover/press states, MDL2 icon glyphs. ✓
- [x] **Key editor redesign** — 980×560, two-column layout. ✓
- [x] **Keyboard editor redesign** — 840×560, equal two-column layout. ✓
- [x] **Group editor redesign** — 880×610. ✓
- [x] **Ghost-paint fix** — Picker panels use `Fluent.BgCard`; suppressed `OnPaintBackground` in buttons. ✓
- [x] **Mode buttons unified color** — All five send-mode buttons use `Variant.Primary` when selected. ✓

---

### Structural improvements ✓

- [x] **Word prediction slot — auto-assign next free number** ✓
- [x] **Word prediction slot — renumber on remove** ✓
- [x] **"Layout" send mode** — Primary/Shift/AltGr sends each load a file (`layout:math.xml`). Flash red on missing file. Undo stack cleared on switch. ✓
- [x] **XML file tampering hardening** — GridRows/Cols, ColSpan/RowSpan, FontSize, coordinates, window size all clamped; duplicate entries skipped; DTD/XXE blocked. ✓
- [x] **Import groups from another layout** — Conflict table with Overwrite / Add as new / Skip per row. ✓
- [x] **XML readability — `<Theme>` / `<Layout>` sections** — Backward-compatible with old `<Global>` format. ✓
- [x] **GlobalSettings split** — Into `VisualTheme`, `WindowState`, and `LayoutMeta`. ✓
- [x] **Sparse XML format** — Pure spacers skipped on save; auto-filled on load. ✓
- [x] **Style groups (named key groups)** — Inheritance chain: Global → Group → Per-key. 26 unit tests added. ✓
- [x] **Toolbar implementation** — All 10 steps complete (shell, mode toggles, load/save, selection, key actions, grid actions, undo/redo, copy/paste). ✓

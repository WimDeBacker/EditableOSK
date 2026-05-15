# OnScreenKeyboard – Todo List

---

## Pending — UI / UX improvements

- [ ] **Multi-cell selection for formatting** — Allow selecting multiple keys at once (e.g. drag-select or Ctrl+click) and applying formatting to all of them in one action. Affects only style (color, font, group) — label and send are untouched.

---

## Pending — Accessibility

- [ ] **Dwell click** — Allow a key to be activated by hovering over it for a configurable dwell time, without a physical click. Useful for users with limited motor control. Configurable dwell duration in keyboard settings; visual progress indicator on the key.

- [ ] **Slow keys** — Add a configurable delay between key press and key activation, so accidental brushes are ignored. Keys must be held for the full duration before the send action fires. Configurable threshold in keyboard settings.

- [ ] **Scanning (consider)** — Auto-advance a highlight through keys/rows at a fixed interval; the user confirms with a single switch input. More complex to implement. Revisit after dwell and slow keys are done.

---

## Pending — Structural improvements

*(none)*

---

## Completed

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

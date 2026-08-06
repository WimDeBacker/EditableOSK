# OnScreenKeyboard – Todo List

## Pending

- [ ] **Priority 2b — Word prediction: learning engine** *(new feature — high)* — While the user types, record word and word-pair frequencies. Known words get their frequency incremented immediately. Unknown words go into a `<Candidates>` buffer and are promoted to the main list only when count > 2 (filters typos). Personal `.wfq` file updated in place; writes batched every 30 s or on app close. Candidate list visible and manageable in the Edit Keyboard → Word Prediction panel.

- [ ] **Priority 2c — Shrink and review the test suite** *(maintenance)* — The test program has grown large. Audit which tests are load-bearing, which are redundant duplicates, and which are deprecated. Goal: smaller, faster, easier to maintain suite without losing meaningful coverage.

- [ ] **Priority 3 — Touch target sizes** *(accessibility)* — The `+` group-edit button and colour swatches are 32 × 26 px. WCAG 2.5.5 recommends 44 × 44 px minimum. Worth addressing if the app is used on a tablet.

- [ ] **Priority 4 — Scanning** *(accessibility)* — Auto-advance a highlight through keys/rows at a fixed interval; the user confirms with a single switch input.

- [ ] **Priority 5 — Bundled keyboard layouts** *(content)* — Ship 8 ready-made `.kbl` files: 4 wizard themes × 2 key arrangements (AZERTY Dutch, QWERTY English). Users can load one immediately without running the wizard.

---

## Completed

### Word prediction — multi-language & database ✓

- [x] **Step 1 — New file extensions** ✓ — Layouts `.xml` → `.kbl`; databases `.xml` → `.wfq`. All file pickers, csproj, tests, and physical files updated.
- [x] **Step 2 — `.wfq` format** ✓ — Root element gets `version`, `language`, `isPersonal` attributes and `<Candidates />` section. Build-time converter `tools/ConvertWordDb.ps1` produces `.wfq` from source `.xml`. `WordDatabase` exposes `Language` and `IsPersonal`.
- [x] **Step 3 — LanguageRegistry** ✓ — Scans app folder for `.wfq` files via fast `XmlReader` root-element peek; groups by language; sorts base before personal alphabetically.
- [x] **Step 4 — Layout → database link** ✓ — `LayoutMeta.WordDatabase` stores optional filename override. `LoadWordDatabase()` auto-selects: explicit file → language-matched base → any base.
- [x] **Step 5 — Database management UI** ✓ — Word Prediction card in Edit Keyboard: dropdown of all `.wfq` files; base files (`→`) trigger personal-copy dialog with revert-on-cancel; personal files (`★`) are the only selectable targets; `● unsaved` indicator when selection differs from saved value.
- [x] **Step 6 — Tests** ✓ — 1613/1613 passing. Covers: `.kbl`/`.wfq` round-trip, `.wfq` metadata, `WordCount`, `IsLoading`/`Loaded` event state, sequential reload, concurrent load stability (10-task stress), Dutch regression. UI combo and English regression not unit-testable (SaveFileDialog / worddb_EN.xml excluded from repo).

### Word prediction — fast background loading ✓

- [x] **XmlReader + background load** ✓ — `XmlDocument` replaced by forward-only `XmlReader` (~3–5× faster, ~1.5 GB → ~200 MB peak). `LoadWordDatabase()` uses `Task.Run`; keyboard opens immediately with blank WP cells. `WordDatabase.Loaded` event marshals back via `BeginInvoke` → `ApplyWPTags()`. `_lastLoadedDbPath` guard skips redundant reloads on settings-only Apply. Thread-safety: immutable `DbSnapshot` published via a single `volatile` write — no locking on read path. `worddb_EN.xml` excluded from git; size driven by depth (up to 1000 `<Next>` entries per word by design) and managed by the external database builder.

### Code review (ultra) — 8 findings fixed ✓

- [x] BeginInvoke TOCTOU: `try { BeginInvoke(…) } catch (InvalidOperationException)` in both `Loaded` handlers.
- [x] Concurrent Load() race: generation counter (`Interlocked.Increment` + `Volatile.Read`) — superseded loads abort before publishing.
- [x] `grpWP` missing from `WrapInScrollPanel` — added, card now scrolls correctly.
- [x] Partial `.wfq` file on copy failure — deleted in catch block before showing error.
- [x] `Loaded` subscriber exception swallowed — `Loaded?.Invoke()` wrapped in `try/catch`.
- [x] Editor info label stale during async reload — `KeyboardEditorForm` subscribes to `WordDatabase.Loaded` and refreshes `UpdateWPInfoLabel()` on completion.
- [x] Unnecessary reload on every Apply — `_lastLoadedDbPath` guard skips `Task.Run` when path unchanged.
- [x] `IsLoaded=true && IsLoading=true` window — `_isLoaded = true` moved after the `finally` block.

### New keyboard wizard ✓

- [x] 4-page wizard: starting point (blank / paste / copy), grid & labels, theme picker, save. Auto-classifies pasted keys into groups. Four built-in themes. Language selector on page 1. Dark-theme and high-contrast fixes. Nav button positioning. Font-dispose crash fixed.

### Gear button styling (standard group) ✓

- [x] Standard group as style resolution root; gear button styled by it; protected in GroupEditorForm (🔒); reserved name enforcement; "Manage Groups…" button replaces `+`.

### Accessibility ✓

- [x] **Slow keys & dwell click** — Hold-to-register (100–3000 ms) and hover-to-click (100–5000 ms). Amber bottom-up fill animation. Mutually exclusive, saved in `LayoutMeta`.
- [x] **WCAG 2.1 A — Keyboard reachability** — Tab order, arrow-key radio groups, record/browse/list-action buttons all reachable by keyboard.
- [x] **WCAG 2.1 AA — Focus visibility** — 2 px focus ring on all `FluentButton` variants; `ColorSwatchButton` two-tone ring.
- [x] **WCAG 2.1 AA — Colour contrast** — `TextHint` darkened to `#646464` (5.93:1). High-contrast mode support in all dialogs and `FluentButton`/`FluentPainter`.
- [x] **Screen-reader annotations** — `AccessibleName` on every control; tooltips on all three editor forms; `AccessibleDescription` sentinels; preview panel and DataGridView rows fully described.
- [x] **DPI scaling** — All forms `Sizable` + `AutoScroll`; `AutoScaleMode.Dpi`; screen-clamp on load.

### FluentDialogBase ✓

- [x] Shared infrastructure (`AddColorRow`, `AddFieldLabel`, `SetTip`, `AddGroup`, `MakeActionBtn`, `WrapInScrollPanel`, HC live-update, language-change refresh) extracted into `FluentDialogBase`. All four dialog forms inherit it (~300 lines removed).

### Fluent Design / UI overhaul ✓

- [x] `FluentButton` and `ToolbarButton` — owner-drawn, rounded corners, hover/press states, MDL2 icons.
- [x] All three editor forms redesigned (two-column Fluent layout).
- [x] Toolbar: 22 translatable buttons, SVG icons, dark/light/system theme, mode toggles, undo/redo, copy/paste, grid actions.

### Structural & robustness ✓

- [x] `VisualTheme` / `WindowState` / `LayoutMeta` split from `GlobalSettings`.
- [x] Style groups with inheritance chain (Global → Group → Per-key).
- [x] Atomic layout save (`.tmp` + `File.Replace`). Grid validation before write.
- [x] Sparse XML format; DTD/XXE hardening; out-of-bounds clamping.
- [x] Import groups from another layout (conflict table).
- [x] Word prediction: slot auto-assign, renumber on remove, graceful DB failure.
- [x] `Layout` send mode; undo stack (capped at 50).
- [x] Translation system: all toolbar/dialog strings via `Lang.T()`; `lang_nl.xml` complete.

### Bug fixes ✓

- [x] Dead cells after span shrink, resize, expand (3 fixes).
- [x] Window height inflated after closing in Edit mode.
- [x] Groups from one layout persisting after loading another.
- [x] Group style changes via `+` not applied to sibling keys.
- [x] WP duplicate-warning dead code replaced; `NormaliseWPSlots()` on load.
- [x] Test runner console window on WinExe (`AttachConsole` instead of `AllocConsole`).
- [x] `azertycolor.kbl` not typing — `SlowKeysMs` was 300, set to 0.

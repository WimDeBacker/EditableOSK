# OnScreenKeyboard – Todo List

## Pending

- [ ] **Priority 2c — Shrink and review the test suite** *(maintenance)* — The test program has grown large. Audit which tests are load-bearing, which are redundant duplicates, and which are deprecated. Goal: smaller, faster, easier to maintain suite without losing meaningful coverage.

- [ ] **Priority 3 — Touch target sizes** *(accessibility)* — The `+` group-edit button and colour swatches are 32 × 26 px. WCAG 2.5.5 recommends 44 × 44 px minimum. Worth addressing if the app is used on a tablet.

- [ ] **Priority 4 — Scanning** *(accessibility)* — Auto-advance a highlight through keys/rows at a fixed interval; the user confirms with a single switch input.

- [ ] **Priority 5 — Bundled keyboard layouts** *(content)* — Ship 8 ready-made `.kbl` files: 4 wizard themes × 2 key arrangements (AZERTY Dutch, QWERTY English). Users can load one immediately without running the wizard.

---

## Completed

### Word prediction — learning engine (overlay redesign) ✓

First cut used a full "personal copy" of the base `.wfq` (manual dropdown +
copy step, learned frequency mixed into the base corpus frequency). Replaced
after review — too much setup friction, and a +1 frequency bump is invisible
against base corpus frequencies in the thousands/millions. Redesigned around
a small overlay file and a separate personal-use ranking tier:

- [x] **Overlay file, not a full copy** ✓ — `worddb_NL.wfq` → `worddb_NL.learned.wfq` (same dir, `WordDatabase.DeriveOverlayPath`/`GetOverlayPath`). Holds only what changed: `<PersonalUse>`/`<PairUse>` deltas for base words/pairs, `<NewWord>`/`<NewPair>` for promoted candidates (with a `<NewWord>`'s own `<Next>` children nested inside it), and `<Candidates>`. `WordDatabase.Load` merges it onto the freshly-parsed base snapshot before publishing (`ApplyOverlay`), reusing the same mutation primitives `RecordWord` uses. `LanguageRegistry` excludes `*.learned.wfq` from its database scan.
- [x] **`RecordWord`** ✓ — Called from `WordPredictor.CompleteWord` on every finished word (typed or WP-clicked). Known words/pairs: `WordEntry`/`NextEntry.PersonalUseCount` incremented — kept separate from `Frequency` (the base corpus value), never blended. Unknown words go into a candidate buffer, promoted to a real word once seen more than twice. No-op unless `WordDatabase.LearningEnabled` (default **on**; no personal-file setup needed at all).
- [x] **Personal-use ranking tier** ✓ — `GetPredictionsCore` gained "Step 1.5": words/pairs with `PersonalUseCount > 0` are ranked ahead of raw frequency, capped at `PersonalCap(count)` (≈3/4 of the slots, always leaving ≥1 for a normal suggestion) so personal usage reliably surfaces without ever fully crowding out the base corpus.
- [x] **Sentence-start normalisation** ✓ — A word is only auto-capitalised because it opened a sentence (display artefact); `WordPredictor` tracks this per-word and learns it lowercase, while a genuine mid-sentence Shift press (proper noun) keeps its capital.
- [x] **Batched, crash-safe persistence** ✓ — `SaveIfDirty()`/`SaveNow()` (parameterless — always target the overlay paired with whatever base is loaded) build a plain-data copy on the caller's (UI) thread — so a background write never races the UI thread's in-place `RecordWord` mutations — then write via `Task.Run` using the same `.tmp` + `File.Replace` pattern as `SettingsManager`. `KeyboardForm` calls it from a 30 s timer and does a final synchronous `SaveNow` on close.
- [x] **"Remember typed words" toggle** ✓ — `LayoutMeta.WordLearningEnabled` (default true) replaces the old database-choosing dropdown/copy-dialog UI in Edit Keyboard → Word Prediction with a single checkbox; the database dropdown now only chooses between base language files (no personal/copy concept). Candidate list + Promote/Reject buttons gated on the toggle. Export now exports the (small) overlay file.
- [x] **Tests** ✓ — 1648/1648 passing: personal-use vs. frequency increments kept separate, candidate buffering and auto-promotion at count > 2, manual promote/reject, overlay merge-at-load (`<PersonalUse>`/`<NewWord>`/`<PairUse>`/`<NewPair>`/`<Candidates>`), personal-use ranking cap, save targets the overlay (never the base file), no-op when `LearningEnabled = false`, `WordPredictor` sentence-start-vs-proper-noun case normalisation, and `LanguageRegistry` excluding overlay files from its scan.

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

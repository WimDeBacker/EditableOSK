# Review suggestions: changes since 12 August (d6723c1..HEAD)

Scope: 34 commits, 64 files. Reviewed in depth: ModifierLatch, UndoHistory, SendKeysHelper (Win key), SettingsManager,
KeyboardForm, WordDatabase, WizardKeyParser, KeyEditorForm (recording and send conversion), KeyboardEditorForm (Apply and
Load), GroupEditorForm and GroupDialogs, TouchStepper, Accelerators, Program, ConvertWordDb.ps1, setup.iss. Dialog layout
code (FluentDialogBase, AdaptiveTable, Touch* painting) and the test files were only skimmed. Nothing was built or run.
Ranked most severe first.

1. KeyEditorForm.cs:1302 (BuildSend, layer 0, KeySequence): `FromHuman(text)` is always applied, with no "layer touched"
   guard. `ToHuman` drops grouping parentheses ("lossy by design"), so opening a key whose Send is `^(ab)` and pressing Apply
   rewrites it to `^ab` (Ctrl applies to one key only). Layers 1 and 2 are protected by `_layerTouched` / `_origSend`; layer 0
   is not. Fix: keep `p.Send` for layer 0 as well while the value box is untouched.

2. AutoSave persists an invalid layout over the user's good files. KeyboardForm.cs:3815 passes `allowInvalid: true` for both
   the current file and `SettingsManager.DefaultPath`. After a transiently invalid layout (mid resize or merge) the next
   autosave or the FormClosing autosave overwrites the last good file. On restart `LoadSettings` rejects it
   (`!loaded.IsValid()`), so the keyboard silently falls back to the default. Suggest writing invalid in-memory state to a
   separate recovery file, not over the named file.

3. SendKeysHelper.cs:685 and KeyEditorForm.cs:1146. The recorder produces `win:` + prefix + key for any key, but
   `WinKeyPayloadToVk` only knows letters, digits, arrows, Home/End, PgUp/PgDn, Enter, Tab, Esc, Delete and F1-F12. Recorded
   Win+Space (stored as `win: `), Win+. / Win+, (stored as `{BE}` / `{BC}`), Win+Backspace, Win+PrtSc and Win+Insert return
   vk 0 and are silently dropped. The new comment "a lone '+' ... is the key itself" is also dead: `win:+` leaves key "+",
   which maps to 0, so Win++ (Magnifier) still does nothing. Extend the table (`SPACE` as " ", OEM keys, BACKSPACE, PRTSC,
   INSERT) or reject unsendable recordings in the editor.

4. WizardKeyParser.cs:92-100: modifier keys get Send `^`, `%`, `win:`, `{CAPSLOCK}`. The comment says these are what the stock
   layouts use, but qwerty.kbl has `Label="Ctrl" Send=""` and `Label="Win" Send=""`. `KeyEditorForm.DetectSendMode` treats a
   key as Modifier only when Send is empty, so a wizard-made Ctrl, Alt or Win key opens in the key editor as a KeySequence
   key, and Apply stores that send. Use "" for modifier keys (the engine already recognises them by label) and fix the comment.

5. KeyboardForm.cs:3920 (WarnIfFontsMissing, called from ApplyLoadedSettings and LoadSettings): an owner-less modal
   `MessageBox.Show` from an always-on-top, no-activate window. It can open behind the topmost keyboard. It also fires on
   every `layout:` key press that loads a file with a missing font, and during startup before the form is shown. Pass
   `this` as owner or use `TouchMessage`; show it once per file, and not from the layout-switch key path.

6. KeyEditorForm.cs:~1002 (LowLevelHookCallback): every key-down while recording is suppressed and queued with `BeginInvoke`
   until the UI thread runs `CompleteRecording` and unhooks. A second key, or auto-repeat of the first, queues further
   `CompleteRecording` calls. They run after recording stopped and overwrite the recorded value, mode and label with the
   later key. Set `_recording = false` (or a "done" flag) inside the callback and ignore events after the first Record.

7. Program.cs:105-125: `SetUnhandledExceptionMode(CatchException)` with a handler that appends to
   OnScreenKeyboard_error.log with no cap. An exception thrown in a paint handler repeats on every repaint, so the log
   grows without bound and the window stays broken. Add a size cap or rate-limit by message. Also,
   `Environment.Exit(0)` right after `Application.Run` skips finalizers and `using` disposal. Acceptable if intended, but
   anything created after `Run` (a pending background `WordDatabase.SaveIfDirty` write) is cut off.

8. WordDatabase.cs, SaveNow (FormClosing, KeyboardForm.cs:~395) versus the background write in SaveIfDirty. Closing the
   keyboard within a periodic save calls `WriteSaveData` to the same overlay path from two threads. `SaveNow` ignores
   `_saving`. The new `try { WordDatabase.SaveNow(); } catch { }` hides the resulting IOException, so the last learned
   words are lost without a trace, or the file is interleaved. Wait for `_saving` (or take a lock) in `SaveNow`.
   Related: `Load()` sets `_dirty = false` after publishing the snapshot, so a `RecordWord` on the UI thread in that window
   is marked clean and never saved.

9. GroupEditorForm.cs:537 (CommitTo) and 612 (RemoveSelectedGroup): renaming or deleting a group does not update
   `Props.GroupName` of the keys that use it. Those keys point at a name that no longer exists, so they lose the group look
   and the next group-editor open shows them as ungrouped. Rename should rewrite the cells' GroupName; delete should
   clear it. (Existed before this range, but the rewritten dialog still lacks it.)

10. GroupDialogs.cs, ImportDialog / GroupEditorForm.ApplyImportDecisions (`ImportAction.Add`): the status ("New") is
    computed only against existing local groups. Two groups with the same name inside the imported file are both "New"
    and both added, giving duplicate group names (the dictionary lookup then shadows one). Include earlier imported
    names in the conflict check, or run `GetUniqueName` for Add as well.

11. TouchControls.cs:~641 and 662 (TouchStepper): `BeforeChange` is only asked for buttons, arrows and a few keys.
    Paste from the context menu, Shift+Insert, drag and drop and Ctrl+Z reach `OnTyped` without asking, so a value
    that needs confirmation (leaving a group) changes unasked. `OnTyped` also ignores a typed number below `Minimum`
    while typing, which is intended, but it never reports it, so the box shows a value the model does not hold until Leave.

12. KeyEditorForm.cs:~1105 (BuildSendFromHook) and the Ctrl/Alt state read in the hook: modifiers come from
    `Control.ModifierKeys` inside a low-level hook callback. That reads the async key state of the UI thread, which can
    lag the event being processed. A Ctrl+key typed quickly may be recorded without Ctrl. Track modifier state from the
    hook's own key-down and key-up events (as is done for Win with `_winHeld`).

13. installer/setup.iss and .gitignore: .gitignore says the font archive is "private ... not ours to publish: once pushed
    by mistake", but installer/fonts/SchoolKX_new.ttf is committed and now redistributed system-wide by the installer
    (`{autofonts}`). Confirm the licence allows redistribution. `FontInstall: "SchoolKX_New"` must also equal the
    font's internal family name exactly, or the registration silently fails.

14. KeyboardForm.cs, ModifierLatch usage (`ToggleModifier` / `StateOf`): the state is derived from two HashSets keyed by
    cell, and `SetState` is applied to every cell with the same label. The state of the tapped cell is read once
    and applied to all, which is correct. But `Caps` is excluded from `AfterKey` clearing only by label in
    `ClearModifiers`, and a Locked Caps follows the sticky rules too (non-sticky: second tap -> Off, first tap -> Latched
    -> cleared by the next key unless excluded). Check that Caps with `StickyModifiers = false` still behaves as a lock;
    the new `Toggle` makes it latch-then-off like Shift. Covered by tests only for Shift/Ctrl.

Not reported (checked and found fine): UndoHistory ordering and cap, the `ShowCornerLabels` default in old files,
`SaveIfDirty` dirty-flag ordering, the ConvertWordDb header checks, `Accel` parsing of `&&`, `DrawSelectionRing` colour
comparison (named colours only).

---

# Suggested fixes

Nothing below was built or run; the snippets are written against the code as read and need a build and the usual test
runs (CLAUDE.md: filtered run while iterating, full suite before a commit). Each fix names the test to add. Strings
that are new go in `LanguageManager.cs` and `lang_nl.xml`.

Suggested order: 2, 8, 1, 6 (data loss or wrong data) -> 3, 4, 12, 14 (wrong behaviour) -> 5, 9, 10, 11 -> 7 -> 13 (a
decision for the owner).

## Fix 1: layer 0 key sequence is rewritten lossy (KeyEditorForm.BuildSend)

**Status: applied** (commit 4761280, test `T_KeySequenceLossless`). Built and tested on Windows (2026-10-09, full suite green). Not covered by a
test: switching the action type away and back without typing (see the second bullet below).

Treat layer 0 like layers 1 and 2: while the value box is untouched, return what was stored.

```csharp
case SendMode.KeySequence:
    if (!_layerTouched[0] && _origSend[0] != null) return _origSend[0];   // the readable form is lossy: keep the stored one
    return FromHuman(text);
```

- Check first that `_origSend[0]` is filled where `_origSend[1..2]` are (it is probably only used for the shifted layers)
  and that typing in `_values[0]` sets `_layerTouched[0]` (it must, or an edit is lost). `CompleteRecording` already sets it.
- The same must hold when the mode is switched away and back: a mode change should also set `_layerTouched[0]`.
- Better root fix, if wanted later: make `ToHuman` / `FromHuman` round-trip parentheses (`{Ctrl}(ab)`), then the guard
  is only a safety net.
- Test: a key with Send `^(ab)` -> open editor, Apply with no edit -> `Send == "^(ab)"`. Same after editing the label only.

## Fix 2: AutoSave overwrites good files with an invalid layout (KeyboardForm.AutoSave)

**Status: applied**, as `SettingsManager.AutoSave(..., path, defaultPath)` (testable; `KeyboardForm.AutoSave` calls it)
with assertions added to `T_SettingsManager_RoundTrip`. The startup offer to open a recovery file is not done. Built and tested
on Windows (2026-10-09, full suite green; the test's clashing variable name `dir` was renamed to `autoDir` to make it compile).

Keep `allowInvalid: true` (so the work is not lost), but never onto the named file or the default file. Write an invalid
layout to a recovery file next to it.

```csharp
private void AutoSave()
{
    string path  = _currentFilePath ?? SettingsManager.DefaultPath;
    bool   valid = _layout.IsValid();
    try
    {
        if (valid)
        {
            _meta.LastFile = path;
            SettingsManager.SaveSettings(_layout, _theme, _window, _meta, path);
            if (path != SettingsManager.DefaultPath)
                SettingsManager.SaveSettings(_layout, _theme, _window, _meta, SettingsManager.DefaultPath);
        }
        else
        {
            // Never replace a good file by one LoadSettings will reject: keep the work in a recovery file.
            SettingsManager.SaveSettings(_layout, _theme, _window, _meta, path + ".recovery", allowInvalid: true);
        }
    }
    catch { }
}
```

- On a normal successful autosave delete `path + ".recovery"` so it never goes stale.
- Optional: at startup, if a `.recovery` file is newer than the file, offer to open it (one extra string).
- Do not change `_meta.LastFile` for the invalid case: it would point the next start at a file that does not load.
- Test (in the SettingsManager tests): invalid layout -> AutoSave -> original file unchanged, `.recovery` exists.

## Fix 3: recorded Win shortcuts that cannot be sent (SendKeysHelper.WinKeyPayloadToVk)

**Status: applied** (2026-10-09), built and tested on Windows (filtered run: `KeyEditorRoundTrip`, SendKeys groups). The table follows the proposal
below (Space, `+`, BACKSPACE, INSERT, PRTSC, BREAK, CAPSLOCK, NUMLOCK, SCROLLLOCK, NUMPAD0-9, and `{hex}` only inside braces); a test walks
every key 1-254 through the recorder and the sender and checks they agree (0xF1-0xFA excepted: the recorder writes them as `{F1}`..`{FA}`,
the name of F1..F10). The "cannot be combined with Win" hint was not added: nothing the recorder writes is unknown any more.
Added after testing by the owner: Space is written as `win:{SPACE}` and shown as `{Win}{Space}` (a lone trailing space was invisible);
`{Space}` and the modifier words are case-insensitive in the editor; the recorded label says "Space". The owner confirmed Win+M and
Ctrl+Space work; Win+Space (input-language switcher) needs Win *held* and could not be tested: see todo Ideas I.3.

Teach the sender every payload the recorder can produce. The recorder writes `" "` for Space, `{BACKSPACE}`, `{INSERT}`,
`{PRTSC}`, `{BREAK}`, `{CAPSLOCK}`, `{NUMLOCK}`, `{SCROLLLOCK}`, `{NUMPAD0..9}`, and `{XX}` (two hex digits) for any other key.

```csharp
// before the {KEY} handling in WinKeyPayloadToVk:
if (key == " ") return 0x20;                 // Space is a literal space in the recorder's output
if (key == "+") return 0xBB;                 // VK_OEM_PLUS: "win:+" is Win and the plus key (Magnifier)

// in the switch: add
"BACKSPACE" => 0x08, "INSERT" => 0x2D, "PRTSC" => 0x2C, "BREAK" => 0x13,
"CAPSLOCK" => 0x14, "NUMLOCK" => 0x90, "SCROLLLOCK" => 0x91,
"NUMPAD0" => 0x60, ... "NUMPAD9" => 0x69,    // or: k.StartsWith("NUMPAD") && int.TryParse(...)

// default branch (replace "_ => 0"): the recorder's {hex} fallback, e.g. {BE} = Win+. and {BC} = Win+,
_ when k.Length == 2 && byte.TryParse(k, System.Globalization.NumberStyles.AllowHexSpecifier,
                                      System.Globalization.CultureInfo.InvariantCulture, out byte hex) => hex,
_ => 0,
```

- The F1-F12 cases are matched by the switch before the hex default, so `{F1}` is never read as 0xF1.
- Make `WinKeyPayloadToVk` `internal` and add a round-trip test: for every vk the recorder offers (all of
  `VkCodeToSendKeys`'s outputs, with `win:` in front) `WinKeyPayloadToVk(payload) != 0`. This keeps the two tables in step.
- Also fix the comment at line 682: with the `+` case above it is true. Keep it, and add a test for `win:+` and `win:^`
  (`^` has no virtual key of its own; if wanted, `0xDC`/layout dependent, so leave it unsupported and let the recorder
  refuse it).
- In `CompleteRecording`, when `win` is set and the payload maps to 0, show a hint ("This key cannot be combined with
  Win") instead of storing a key that does nothing.

## Fix 4: wizard modifier keys have a non-empty Send (WizardKeyParser)

**Status: applied** (2026-10-09), built and tested on Windows (filtered run: all Wizard groups, 712 checks). Ctrl, Alt, Win (and Shift, AltGr)
have `Send = ""`; the comment is corrected. Caps too, a step further than proposed (found by the owner: Caps was the only key that still opened
as Key/Shortcut): `{CAPSLOCK}` is never sent, Caps Lock is a latch decided by the label (`ToggleModifier`), so the wizard writes `""`, and
`KeyEditorForm.DetectSendMode` shows a Caps key with the stock send text `{CAPSLOCK}` (qwerty.kbl and the other stock layouts) as a Modifier. `WizardTests` now expect `""` and open each such key in a
`KeyEditorForm`, which must show it as a Modifier. Checked first: `qwerty.kbl` indeed has `Send=""` for Ctrl, Alt and Win, and the
keyboard decides "modifier" by label only (`KeyLayout.ModifierLabels`), so the old send text did nothing at run time; only the Key Editor was
affected. Layouts that an earlier wizard already saved keep their `^` / `%` / `win:` until the key is opened and set to Modifier in the Key Editor
(no migration).

The stock layouts use `Send=""` for Ctrl, Alt, Win and AltGr (the engine recognises them by label). Only Caps keeps
`{CAPSLOCK}`.

```csharp
["ctrl"]     = ("",           "Ctrl", "Ctrl"),
["control"]  = ("",           "Ctrl", "Ctrl"),
["alt"]      = ("",           "Alt",  "Alt"),
["win"]      = ("",           "Win",  "Win"),
["windows"]  = ("",           "Win",  "Win"),
```

- Fix the comment above the table: "Shift, Ctrl, Alt, Win and AltGr send nothing themselves; Caps sends {CAPSLOCK}".
- Update `WizardTests` (around line 67): assert `Send == ""` for those tokens, and add: the key the wizard makes opens in
  `KeyEditorForm` as `SendMode.Modifier` (via `DetectSendMode`).
- Check `KeyboardForm` for any place that reads `Send == "^"` / `"%"` / `"win:"` from a modifier cell (grep `"win:"`),
  in case the wizard output relied on it.

## Fix 5: font warning dialog (KeyboardForm.WarnIfFontsMissing)

**Status: applied** (2026-10-09), built and tested on Windows (filtered run: the font tests, 69 checks). As proposed: the box has `this` as owner (so has the
invalid-file box, `ShowFileError`), it waits for `Shown` at start-up (`TryAutoLoad` runs inside the constructor), and it is shown once per file;
`ApplyLoadedSettings(path, fromLayoutKey)` is called with `true` from the `layout:` key, which never warns (and does not use up the one warning of
that file). `KeyboardForm` cannot be built in the suite, so the rule was extracted as `KeyboardForm.ShouldWarnAboutFonts` and is tested
directly; the owner, the start-up wait and the key path are tested by hand only.

Give the box an owner, never show it before the form is visible, and show it once per file.

```csharp
private readonly HashSet<string> _fontWarnedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

private void WarnIfFontsMissing(VisualTheme theme, GridLayout layout, bool fromLayoutKey = false)
{
    var missing = GetMissingFonts(theme, layout);
    if (missing.Count == 0) return;
    string key = _currentFilePath ?? "";
    if (fromLayoutKey || !_fontWarnedFiles.Add(key)) return;          // once per file, not on every layout: switch

    void Show() => MessageBox.Show(this,
        string.Format(Lang.T("font missing msg"), string.Join(", ", missing)),
        Lang.T("font missing title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

    if (IsHandleCreated && Visible) Show();
    else Shown += (s, e) => BeginInvoke((Action)Show);                 // startup: after the window is up
}
```

- `ApplyLoadedSettings` is shared by the open dialog and the `layout:` key; give it a `fromLayoutKey` parameter (or a
  field) so only a user-chosen open warns. A layout key press shows nothing (the font substitution is the same as
  before).
- `this` as owner is enough to keep the box in front of the always-on-top window; `TouchMessage` would match the new
  dialogs better but is a larger change.
- Test: `GetMissingFonts` already has one. Add: the warn path is called twice for the same file -> one call (inject the
  `Show` as a delegate field so the test does not open a modal box).

## Fix 6: queued recordings overwrite each other (KeyEditorForm hook)

**Status: applied** (2026-10-09), built and tested on Windows (filtered run: `KeyEditorRoundTrip`, `KeyEditorGuards`). Done as proposed, with the hook's
queueing extracted as `KeyEditorForm.QueueRecording` (internal) so a test can drive it: `_recordPending` drops a second key or an
auto-repeat, and the queued lambda checks `_recording`. The new test is in `T_KeyEditorRoundTrip`. With only the `_recording` check in the lambda
the overwrite is already prevented; the flag additionally makes the dropped key explicit (and testable). Not tested with a real hook.

The low-level hook callback runs on the UI thread, so a plain flag is enough.

```csharp
private bool _recordPending;                         // a key was captured, CompleteRecording is queued

// StartRecording and StopRecording: _recordPending = false;

// in LowLevelHookCallback, replacing the last lines:
if (_recordPending) return (IntPtr)1;                // second key or auto-repeat: swallow, record nothing
_recordPending = true;
...
BeginInvoke((Action)(() => CompleteRecording(layer, vk, ctrl, alt, shift, win)));
return (IntPtr)1;
```

- Do not put the guard inside `CompleteRecording` itself: the tests call it directly without a running recording.
  If a guard there is wanted, use `if (!_recording && !_testing) return;`, but the flag above is enough.
- Because `StopRecording` can also run from the stop button or on leaving the window while a `BeginInvoke` is queued,
  have the queued lambda check `if (!_recording) return;` (the lambda, not `CompleteRecording`).
- Test: call the callback path twice before the queue is pumped (extract `OnHookKey(vk, ...)` as internal so a test can
  drive it) -> one recording, the first key.

## Fix 7: error log without a cap, and Environment.Exit (Program.cs)

```csharp
private const long MaxLogBytes = 256 * 1024;
private static string _lastError; private static DateTime _lastErrorAt;

private static void LogError(string where, Exception ex)
{
    try
    {
        string text = $"[{where}] {ex}";
        // The same error again within a few seconds (a paint handler that throws on every repaint): count it, do not log it.
        if (text == _lastError && (DateTime.Now - _lastErrorAt).TotalSeconds < 5) return;
        _lastError = text; _lastErrorAt = DateTime.Now;

        string file = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OnScreenKeyboard_error.log");
        if (System.IO.File.Exists(file) && new System.IO.FileInfo(file).Length > MaxLogBytes)
            System.IO.File.Move(file, file + ".old", overwrite: true);      // keep one generation
        System.IO.File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}{Environment.NewLine}");
    }
    catch { }
}
```

- `Environment.Exit(0)`: keep it (the comment explains why), but make sure everything that must be flushed is flushed
  before it. With Fix 8 `WordDatabase.SaveNow` waits for a running background write, so the only thing that can be cut
  off is gone. Optionally call `WordDatabase.SaveNow()` once more right before the `Exit` (cheap when not dirty).
- Make `LogError` `internal` so `FormClosing`'s `catch { }` around `SaveNow` can log through it instead of hiding the error.

## Fix 8: SaveNow races with the background save; Load clears the dirty flag too late (WordDatabase)

**Status: applied** (2026-10-09), built and tested on Windows (full suite green). The race was reproduced first: with the lock taken out of
`SaveNow` the new test `close while saving` fails (`SaveNow` throws, because the background save has already moved the shared
".tmp" file; the exception was swallowed by the `catch { }` in `FormClosing`). Done as proposed below: `_writeLock` around every
`WriteSaveData`, `SaveNow` clears the dirty flag before the copy and sets it again on failure, `Load` clears the flag before it publishes
the snapshot. `WriteSaveData` already wrote through a ".tmp" file and `File.Replace`. The test hook is `WordDatabase.WriteDelayMsForTest`
(a sleep between the temp file and the replace). Not tested separately: the order change in `Load`.

One lock for every write of the overlay file:

```csharp
private static readonly object _writeLock = new object();

public static void SaveNow()
{
    var snap = _snapshot;
    if (!_isLoaded || snap.OverlayPath == null) return;
    _dirty = false;                                   // before the copy, as SaveIfDirty does
    SaveData data;
    try   { data = BuildSaveData(snap); }
    catch { _dirty = true; throw; }
    try
    {
        lock (_writeLock) WriteSaveData(data, snap.OverlayPath);   // waits for a background write, and writes the newer data after it
    }
    catch { _dirty = true; throw; }
}

// SaveIfDirty, in the Task.Run body:
try   { lock (_writeLock) WriteSaveData(data, path); }
```

- `SaveNow` builds its data after the background copy was taken, so when it gets the lock second, the newer data wins.
- Load: publish after clearing, so a `RecordWord` right after the publish is not lost:

```csharp
_dirty = false;                // the previous file's changes are not ours any more
_snapshot = snap;              // publish; any RecordWord from here on sets _dirty again
LoadError = null;
```

- Also write the file atomically (`path + ".tmp"` then `File.Replace`/`Move(overwrite)`) inside `WriteSaveData`, if it does
  not already; a crash mid-write then cannot truncate the learned words.
- Test: start `SaveIfDirty` with a slow writer (inject a delay hook), call `SaveNow` -> no exception, final file holds
  the later data.

## Fix 9: renaming or deleting a group leaves keys pointing at it (GroupEditorForm)

The dialog edits a copy (`_groups`), so the keys must be fixed by whoever applies the result. Track by group object, not
by name:

```csharp
private readonly Dictionary<KeyGroup, string> _origName = new Dictionary<KeyGroup, string>();   // filled when the list is loaded

/// <summary>Old name -> new name for renamed groups, and the names of deleted groups; valid after OK.</summary>
internal Dictionary<string, string> Renames { get; private set; }
internal HashSet<string>            Deleted { get; private set; }

private void ComputeChanges()
{
    Renames = _groups.Where(g => _origName.TryGetValue(g, out var o) && o != g.Name).ToDictionary(g => _origName[g], g => g.Name);
    Deleted = new HashSet<string>(_origName.Where(kv => !_groups.Contains(kv.Key)).Select(kv => kv.Value));
}
```

- Call `ComputeChanges()` when the dialog closes with OK (after the last `CommitCurrent()`).
- In the caller (`KeyboardForm`/`KeyboardEditorForm`, where the edited groups are copied back), do **one** pass over the
  cells so a swap (A -> B, B -> A) works:

```csharp
foreach (var c in _layout.Cells)
{
    string n = c.Props.GroupName;
    if (n != null && dlg.Renames.TryGetValue(n, out var nn)) c.Props.GroupName = nn;
    else if (n != null && dlg.Deleted.Contains(n))           c.Props.GroupName = SettingsManager.StandardGroupName;   // or "": whatever "ungrouped" is in FindGroup
}
```

- A group that is deleted and re-added under the same name in one session has the same `Name`, but is a new object, so
  it lands in `Deleted` and the keys are reset. If that is not wanted, only list a name in `Deleted` when no remaining
  group has that name.
- Imported `Overwrite` keeps the local name (see `ApplyImportDecisions`), so it needs nothing.
- Test: rename "Nav" -> "Arrows" with two keys in it -> both keys read "Arrows"; delete "Nav" -> keys read standard;
  swap two names -> keys follow.

## Fix 10: duplicate group names inside one import file (ImportDialog and ApplyImportDecisions)

Both places: treat a name already seen in the file as a conflict, and make `Add` safe by itself.

```csharp
// ImportDialog constructor, before the loop:
var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
// in the loop:
bool dupInFile = !std && !seenInFile.Add(g.Name);
bool conflict  = !std && (existing.Contains(g.Name) || dupInFile);
// anyConflict at the top must use the same rule.

// GroupEditorForm.ApplyImportDecisions, case Add:
var clone = group.Clone();
clone.Name = GetUniqueName(group.Name, usedNames);      // safe even if the dialog did not flag it
_groups.Add(clone);
usedNames.Add(clone.Name);
```

- With the dialog change a duplicate shows "Conflict" with Overwrite / Add as new / Skip, default Skip. Overwrite of a
  group added earlier in the same batch overwrites that one; if that is confusing, offer only "Add as new" and "Skip" for
  `dupInFile` (`defaultIdx` 0 = Add as new, since both groups probably matter to the user).
- Test: file with two groups "Fun" and an existing "Fun" -> 3 rows; apply with Add-as-new for both -> "Fun", "Fun 2", "Fun 3".

## Fix 11: TouchStepper skips BeforeChange for paste, undo, drop (TouchControls)

Gate the edit-control messages instead of listing keys. A small subclass of `TextBox` for the value box:

```csharp
private sealed class GatedTextBox : TextBox
{
    public Func<bool> Gate;                       // false: refuse the edit
    protected override void WndProc(ref Message m)
    {
        const int WM_CUT = 0x300, WM_PASTE = 0x302, WM_CLEAR = 0x303, WM_UNDO = 0x304, EM_UNDO = 0xC7;
        if ((m.Msg == WM_CUT || m.Msg == WM_PASTE || m.Msg == WM_CLEAR || m.Msg == WM_UNDO || m.Msg == EM_UNDO)
            && Gate != null && !Gate())
            return;                               // the context menu, Shift+Insert, Ctrl+Z and the like all end up here
        base.WndProc(ref m);
    }
}
```

- Set `AllowDrop = false` on the box (drag and drop of text then does nothing), and in `KeyDown` add Shift+Insert,
  Shift+Delete, Ctrl+Z and Ctrl+Y to the `edits` list as a first line of defence (they would otherwise ask twice if both
  paths ask; the WndProc gate is the one that must hold, so drop the key-list entries for V and X if the gate asks).
- Ask only once per action: `Allowed()` can open a window. Keep a short `_askedAt` tick count (or a flag cleared on
  `KeyUp`/`MouseUp`) so a paste that arrives as WM_PASTE after KeyDown asked does not ask again.
- Second part (typed value below Minimum is not reported): the model keeps the last valid value while the box shows the
  half-typed one until Leave. Add `internal void Commit() => SyncText();` and call it from `FluentDialogBase` before OK is
  accepted (`ValidateChildren()` plus `Validating += (s, e) => SyncText()` on the box), so Apply never reads a value the
  user does not see. Do not clamp while typing: "1" on the way to "15" with Minimum 8 must stay possible.
- Test (UI guard style): context-menu paste with `BeforeChange` returning false -> value unchanged; same with
  `SendMessage(WM_PASTE)`.

## Fix 12: recorder reads Ctrl, Alt and Shift from the async key state (KeyEditorForm hook)

**Status: applied** (2026-10-09), built and tested on Windows (filtered run: `KeyEditorRoundTrip`). The lag itself was not reproduced (it needs a real hook
and fast typing); the change is the defensive one proposed below: `_heldMods` follows the hook's own key-down / key-up events (left and
right keys separately, a generic code releases both), is seeded with `GetAsyncKeyState` when recording starts and cleared when it stops, and the
callback reads Ctrl / Alt / Shift from it instead of `Control.ModifierKeys`. The tests drive `TrackModifierKey` / `HeldModifiers` (extracted as
internal) instead of an `OnHookKey`; `ClassifyHookKey` is unchanged. The owner tested it by hand (Ctrl+C fast, Alt+Shift+F4: fine).
Added after that test: Ctrl+Alt+Delete is not recorded (`IsSecureAttentionSequence`; the keys pass to Windows, the recording goes on).
Windows does not let a program send it (it is the Secure Attention Sequence), so a key with it could never work; `SendSAS` would need a
policy setting and a signed UI-Access install, and was left out.

`GetAsyncKeyState` and `Control.ModifierKeys` are not reliable inside a low-level hook: the state is updated after the
hook returns. Track the modifiers from the hook's own events, as is done for Win:

```csharp
private int _modMask;                              // 1 = Ctrl, 2 = Alt, 4 = Shift, per physical key so left and right release separately
private readonly HashSet<uint> _heldMods = new HashSet<uint>();

private static int ModBit(uint vk) =>
    vk is 0x11 or 0xA2 or 0xA3 ? 1 : vk is 0x12 or 0xA4 or 0xA5 ? 2 : vk is 0x10 or 0xA0 or 0xA1 ? 4 : 0;

// in LowLevelHookCallback, before the switch (modifier keys always pass through):
if (ModBit(kbd.vkCode) != 0)
{
    if (isDown) _heldMods.Add(kbd.vkCode); else if (isUp) _heldMods.Remove(kbd.vkCode);
    return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
}
bool ctrl  = _heldMods.Any(v => ModBit(v) == 1);
bool alt   = _heldMods.Any(v => ModBit(v) == 2);
bool shift = _heldMods.Any(v => ModBit(v) == 4);
```

- Seed `_heldMods` in `StartRecording` from `GetAsyncKeyState` (outside the hook it is accurate) so keys already held
  when the user clicks Record count; clear it in `StopRecording`.
- Windows sends the generic vk (0x11) or the left/right vk (0xA2/0xA3) depending on the source; the sets above handle both.
  Alt arrives as WM_SYSKEYDOWN: `isDown` already covers it.
- `ClassifyHookKey` can stay as it is (it is pure and tested); the new block runs before it.
- Test: feed the extracted `OnHookKey` the sequence Ctrl-down, C-down -> recorded `^c`; Ctrl-up then C -> `c`.

## Fix 13: font redistribution in the installer (installer/setup.iss, .gitignore)

This is a decision, not a code change. The licence of SchoolKX / SchoolKX_New is not something I can see.

- If redistribution is allowed: remove the "private, not ours to publish" wording from `.gitignore` (it contradicts the
  repo), and add a `LICENSE`/readme line next to `installer/fonts` naming the licence.
- If it is not allowed: `git rm installer/fonts/SchoolKX_new.ttf installer/fonts/SKXnew.inf`, delete the `[Files]` font
  line, and let `azertycolor.kbl` fall back (the missing-font warning of Fix 5 then names the font). The file stays in
  git history; if that matters, a history rewrite is a separate, explicit decision.
- Check the registered name either way. From PowerShell:
  `Add-Type -AssemblyName PresentationCore; (New-Object Windows.Media.GlyphTypeface (Resolve-Path installer\fonts\SchoolKX_new.ttf)).Win32FamilyNames.Values`
  The result must equal the `FontInstall:` value (`SchoolKX_New`) and the `FontName` in `azertycolor.kbl`. The
  `FontInstall` value is the name shown in the Fonts list, which should match the family name.
- After an install test on a clean machine, run the app with the layout and confirm `Fluent.IsFontAvailable("SchoolKX_New")`.

## Fix 14: Caps with sticky modifiers (ModifierLatch, KeyboardForm.ToggleModifier)

**Status: applied** (2026-10-09) as proposed below (`lockOnly`), tested in `T_StickyModifiers` (27 checks, filtered run). `KeyboardForm` cannot be built in the
suite, so the wiring in `ToggleModifier` / `ClearModifiers` is tested by hand only. Visible change: Caps is now drawn in the "locked" colour (amber)
when on, also with the option off (before it was drawn as a one-shot latch although it behaved as a lock). The Sticky modifiers tooltip does not mention Caps and is unchanged.

Correction to finding 14 after reading `ClearModifiers`: with `StickyModifiers = false` Caps does still act as a lock,
because `ClearModifiers` skips it by label (Off <-> Latched, never cleared by a key). The real defect is with
`StickyModifiers = true`: Caps goes Off -> Latched -> Locked -> Off, so it takes three taps, and the first tap looks
the same as the second (a Latched Caps is never cleared).

Make "lock only" part of the rule instead of a label test in two places:

```csharp
// ModifierLatch
public static ModifierState Toggle(ModifierState current, bool sticky, bool lockOnly = false)
{
    if (lockOnly)                                   // Caps: like the Caps Lock key, one tap on, one tap off
        return current == ModifierState.Off ? ModifierState.Locked : ModifierState.Off;
    ...unchanged...
}

// KeyboardForm.ToggleModifier
var next = ModifierLatch.Toggle(StateOf(cell), _meta.StickyModifiers, lockOnly: cell.Props.Label == "Caps");

// ClearModifiers: the label test is no longer needed, a Locked key stays through AfterKey
_latchedMods.RemoveWhere(c => ModifierLatch.AfterKey(StateOf(c)) == ModifierState.Off);
```

- Update the tooltip text of the Sticky modifiers option if it mentions Caps, and the `ModifierLatch.cs` header comment.
- Tests in `StickyModifierTests`: Caps with sticky on and off -> Off, Locked, Off; `AfterKey(Locked)` stays Locked;
  Shift/Ctrl unchanged (the existing cases).

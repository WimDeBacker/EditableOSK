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

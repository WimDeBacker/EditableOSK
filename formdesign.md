# Form design: points of attention and design flaws

Notes from redesigning the Key Editor (`KeyEditorForm`) on touch-friendly components, written 2026-10-02.
They are meant to be reused for the other windows (Group Editor, Keyboard Editor, wizard): the same flaws will be there.

Status tags: **fixed**, **open**, **unverified** (not tested).

## 1. Flaws in the original design

| Flaw | Status |
|---|---|
| Fixed pixel layout and a constant window size (1080 px wide, about 770 px tall). It did not fit small or scaled screens. | fixed (content-sized, two sections) |
| Controls were 26–34 px tall; 29 of 36 pointer controls were under 44 px. The `NumericUpDown` arrows were only a few pixels wide. | fixed |
| One shared "Browse" button filled whichever send field was focused last. That is hidden state, and the button label changed to say which field it served. | fixed (a Browse/Record button per layer) |
| A hidden flag (`_shiftSendIsLayout`) re-added the `layout:` prefix on Apply. | fixed |
| Five mode buttons took three rows and did not explain themselves. | fixed (one chooser with descriptions) |
| The label field was as wide as a text field, though key labels are short. | fixed |
| Lots of empty space was reserved for longer translations. | fixed (table layout plus a +40% / +80% test) |
| The preview sat bottom left and was not labelled as an example. | fixed (labelled card, top right) |
| The colour dialog has no hex box; Windows' `ColorDialog` only has H/S/L and R/G/B. | fixed (own flyout; "More colours…" still opens the standard dialog) |

### 1b. Flaws in the original Group Editor (found when it was migrated, 2026-10-02)

| Flaw | Status |
|---|---|
| "Inherit" was a hidden value: an empty colour, a thickness of -1, a size of 0, shown as a grey swatch and a hint like "-1 = inherit standard". | fixed (explicit: dashed chip showing the real inherited colour + an *Inherit* button in the flyout; an *Inherit* check box for the border; *Auto* for the size) |
| Clearing a colour needed a right-click context menu on a 32 × 26 swatch: no touch, no keyboard. | fixed (a button in the colour flyout) |
| The list was a plain `ListBox` (rows about 18 px), the Add / Delete / Import buttons 34 px, a hex box and a tiny swatch per colour. | fixed (`TouchList` with 44 px rows, 44 px buttons, chips) |
| `MessageBox` for "delete?" and "no groups found": small system buttons, and the default answer was Yes. | fixed (`TouchMessage`; the default of a question is No) |
| The import dialog was a `DataGridView` with a combo box per row, fixed 580 px wide, rows coloured green / amber / blue (colour alone says nothing to a screen reader or a colour-blind user). | fixed (`ImportDialog`: one row per group with its status in words and a touch chooser) |
| Fixed pixel layout and constant size (880 × 610); labels at a fixed x; no preview. | fixed (content-sized, table layout, labelled preview) |
| A new group was a copy of the standard group "so the user sees concrete values". Kept: it avoids a row of inherit markers that mean nothing before the inheritance model is understood. | kept on purpose |

### 1d. Flaws in the original wizard and lessons from rebuilding it (2026-10-04; full list W1–W14 in `wizard_inventory.md`)

| Flaw or lesson | Status |
|---|---|
| 820 px tall window (did not fit 1366 × 768), 25 px text boxes and number boxes, 30 × 25 browse buttons, message boxes for every error, no Cancel button, Enter did nothing on pages 1–3, selection shown by colour only. | fixed |
| In paste mode the rows / columns boxes did nothing (the file used the parsed rows). Create overwrote an existing file silently; file names with `/ : ? *` gave a raw system error. | fixed (size shown as text; Yes / No question; clear message) |
| **Lesson: a `TableLayoutPanel` row takes the control's own height, not its `GetPreferredSize`, unless the control is `AutoSize`.** A custom control (`WizardGridPreview`) therefore set its own `Height` / `MinimumSize`. | learned |
| **Lesson: a wrapping `Label` must have a `MaximumSize` that is no larger than the real room.** One that was wider than its container (or wider than the window) kept a one-line height and clipped; one narrower than the column broke the "stacked controls share one width" rule. Use constants derived from `ContentMaxWidth` (`PageTextWidth`, `GroupTextWidth`). | learned |
| **Lesson: a stress test that appends `xxxx` to a string cannot wrap it.** A hint of 130 characters grows an unbreakable run wider than the window at +80 %; keep each hint line short (three short lines instead of one long). | learned |
| **Lesson: a page that shows different things per mode must not hold all of them at once.** The three variants of page 2 as separate host sections keep the window as tall as the tallest instead of their sum. | learned |
| A hard-coded "Always on top: Yes" in the summary; the preview used the colours of preset 0 whatever was chosen. | fixed (summary text states the defaults; the preview uses neutral dialog colours) |

### 1e. Responsive layout: flaws found and lessons (2026-10-05; see `responsive_spec.md`, section 10)

| Flaw or lesson | Status |
|---|---|
| The dialogs were as wide as their content and cut off what did not fit: the Key Editor's Action row lost its value box and button at 175 % scaling (811 px), with no scroll bar. The old overflow guard skipped docked controls and never saw it. | fixed (responsive tables; `UiGuard.CutOff`) |
| Fixed pixel widths stood in for layout: hint lines capped at 836 px, a 280 px language chooser, 240 px minimum text boxes, five tiles in a row, three help columns in a row. | fixed (wrapping text, choosers 160–280 px, tiles and columns chosen by width) |
| **Lesson: pick the arrangement from the dialog's real width, not from the nearest container.** That container is usually sized by its content, i.e. by the table: the answer depended on itself and the table flipped between arrangements (a layout loop that hung the test run). | learned |
| **Lesson: the layout engine's own preferred width is not reliable right after margins change**, and it proposes all kinds of widths while it measures (a `GetPreferredSize` that picked an arrangement per proposal made the dialog 180 px too tall). Work the width out from the controls; let measuring answer for what is on show. | learned |
| **Lesson: remember measured widths, but forget them** when the form is scaled, the language or font changes, or a child appears, disappears or changes text (deferred, once: deciding after every one of hundreds of text changes made the dialogs 4–5× slower). | learned |
| **Lesson: a section that is not on show gets no layout**, so its tables keep the choice made at the small starting size and the window is measured too tall (`ReselectAll`). The window must also be made as wide as it may become before it is measured. | learned |
| **Lesson: a table that changes arrangement must have its container laid out again**, after the container's own pass has ended (inside it the request is ignored): otherwise the last row was cut off 10 px. | learned |
| **Lesson: a `MaximumSize` with height 0 on a single control** (a stepper) left it with no height while the row was measured; only containers and text controls get a width limit. | learned |
| **Lesson: a stress text must be breakable.** An unbroken run of x's as long as a sentence made wrapping labels impossible to fit; `Lang.Pseudo` now adds words. | learned |
| The suite got slower (104 s → 216 s): the narrow-width test and slower dialog guards. | open (todo 2.4.6.12) |

### 1c. Flaws in the original Keyboard Editor (found when it was migrated, 2026-10-04; full list in `keyboardeditor_inventory.md`)

| Flaw | Status |
|---|---|
| **Save and Save As saved the previous values** (read by reading the code, not reproduced): the dialog applied and closed, then the main form saved its live objects, which were only updated after the dialog had returned. | fixed (Save applies first and sets `FileAction`; the caller copies the results and then saves) |
| Load wiped every unsaved edit in the dialog even when the file dialog was cancelled, and nothing warned that Cancel cannot undo a load. | fixed (asks first; a cancelled file dialog changes nothing) |
| Cancel did not undo the live language change, the flipped learning engine or Promote / Reject. | fixed (language restored on Cancel; learning only on Apply; Promote / Reject say on screen that they are immediate) |
| Hidden state and silent rewrites: `Apply` built new model objects field by field (a field added later is reset), an untouched opacity 0.755 became 0.76, slow / dwell values outside the stepper range were clamped, a stored database that was not found became "(auto)", a file with both slow keys and dwell kept only dwell by accident of the load order, the toolbar theme was stored by list index. | fixed (clone and overwrite, keep untouched values, widen ranges, keep the missing database as a row, explicit radio group, explicit enum order) |
| A track bar (thumb a few pixels), 17 px check boxes, 26 px combo boxes, a hex text box and a 32 × 26 swatch; fixed 940 px window that did not fit a laptop. | fixed |
| Two similar controls that stand under each other differed slightly in width, so no right edge lined up. | fixed (alignment rule, section 3b) |
| Slow keys and Dwell click were two exclusive check boxes with no word of explanation, and a disabled "Show timing animation" with no visible reason. | fixed (radio group "Timing aid"; the animation check box belongs to the group and is greyed while Off is chosen) |

## 2. Flaws in the proposals during the design

- **Separate Shift/AltGr tab (first proposal):** there was no way to set a layout jump in Shift send. This led to the layer-based design.
- **Option A:** many components, functions unclear at first sight. **Option B:** tallest, and the hardest to build and measure. **Option C:** the dropdown was under 44 px, the rows too wide, and "Send" was an unclear name.
- Every design question took several rounds. Layout, naming, preview position and the placement of key width/height were each corrected after seeing a mock-up. Lesson: show a mock-up earlier.

## 3. Bugs in the implementation (found by the user or by tests)

- **Borders:** a 1 px pen on whole coordinates made some edges thin and others wide. This was in the shared button painter, so it affected the whole app. Text boxes also used a near-black system border and the colour chips a flat 2 px border. Fixed, with a pixel test (`T_ControlBorders`, `Fluent.CrispBorderRect`).
- **Focus ring:** it was a square over rounded buttons, in every button and in the toolbar. Fixed, with a corner test (`FluentPainter.DrawRoundedRing`).
- **Dark theme:** the AAA accent blue is dark, so as a focus colour it showed at only 2.3:1 on the toolbar and the Group Editor list. Fixed there.
- **"Auto" check box:** the 44 px click area existed, but only a small tick box was drawn. Fixed.
- **Layout measuring:** a table ignores the content of a stretching row, so the window opened too short; a wrapping row inside a nested table doubled a field's height. Fixed by measuring the frame explicitly and building flat.
- **Text:** a stepper value was off-centre (the edit control resets its text rectangle on resize); the colour chips clipped long captions; at +80% text the value field shrank to 1 px; the "Auto" check box showed "A…". All fixed.
- **`ToHuman("{(}")` returned `{}`.** This bug predates the redesign: editing a `(` key silently wrote an invalid send. Fixed; `{TOKEN}`s are now copied whole.
- **A flag shared by all layers:** the "value shows the prediction explanation" flag meant a loaded word prediction key, switched to Text, would have typed "Prediction slot 1…". Fixed; the explanation is also shorter.
- **Disabled controls:** a translucent white wash left them light, so on the dark theme a disabled dropdown looked enabled. A disabled text box also showed faint system text. Fixed with a grey palette per theme (`FluentPainter.DisabledPalette`). This changes every disabled button in the app.
- **Action type kept the old value (Priority 5):** switching a layer from Key/Shortcut, Layout or Modifier to Text left the value in the box, so the key would type `azerty.kbl` or `{Ctrl}c`. The cause was a one-way reset: the value was cleared when entering Key or Layout, never when leaving them. Fixed by remembering the previous type per layer. Lesson: a state that is reset on entry must also be reset on exit, on every layer.
- **Shortcut recording lost modifiers (Priority 6):** Ctrl+Shift+A was stored as Ctrl+A; Shift alone on a letter lost its Shift; Shift+digit lost its Shift; Win+Shift+key could not be stored at all, because the Win path carried no modifiers. Fixed in `BuildSendFromHook` and in `SendKeysHelper.SendWinKey` (a `^ % +` prefix after `win:`). Sending Win+Shift through the real `SendInput` is not tested.
- **The Record button grew over the value box.** Its text changed to "Press key now…" while recording, and the button is in an auto-sized column next to a value box that spans columns. A first fix (reserve the width of the longest text) still overlapped slightly in English and badly in Dutch. Lesson: a control whose label changes with state or language must not sit in an auto-sized table column next to a spanning control. Fixed by making the button icon-only, a fixed 44 × 44 square; the words live in the accessible name and the tooltip.
- **A second recording kept the first recording's label.** The label was only filled when empty. Fixed with `ApplyRecordedLabel`: a label that a recording put there is replaced, a label the user typed is kept.
- **Escape cancelled the recording, so Ctrl+Esc and similar shortcuts could not be recorded.** Escape is now recorded like any other key; recording is cancelled by clicking the stop button (less accessible for keyboard users, a deliberate trade-off) or by leaving the window.
- **Record and stop icons were too alike.** A ring with a red dot and a ring with a red square differ by a few pixels at 28 px. Fixed: record is an outline ring with a dot, stop is a solid red disc with a white square (outline against filled, plus colour). The state change must be visible at the real size, not at the size of the design sheet.
- **Red on red:** the first design turned the button red while recording, which would hide a red stop icon. The button stays neutral; the icon carries the state.
- **A table that is much taller than its content (Group Editor):** the style table showed a gap of 120 px before its last row, and the window needed 753 px (the screen has 728). Cause: `FlowLayoutPanel`s that wrap, inside a table inside another table, are measured at a narrow width first (three wrapped lines of colour chips), the table's *preferred* height was inflated, and the table then handed the surplus to its last row. Also, a flow panel under-measured its own width, so a long caption overflowed it. Fixed with a small non-wrapping `TableLayoutPanel` per row (`InlineRow`) and by anchoring instead of docking the table. Lesson: do not nest wrapping flows in tables; print `GetRowHeights()` when spacing looks wrong (`GroupEditorForm.DiagnoseLayout`).
- **A hidden label is not part of the window's size (NameDialog):** the error label was invisible at first, so the window was sized without it and the message was cut off when it appeared. Fixed by keeping the label always present (empty) with a reserved height. Same in the Group Editor's Name field.
- **A long control next to a stepper (Group Editor):** the check box "Inherit from standard" did not fit beside the stepper at +80 % text. Fixed by a short visible text ("Inherit") with the full phrase as accessible name and tooltip. Lesson: the text on a control that sits in a row must be short; put the explanation in the accessible name.
- **Pseudo-localisation finds unbreakable runs:** the padding "xxxx…" of the test language is one unbreakable word, so a message label that wrapped fine at +40 % was clipped at +80 %. The label got more room (three label widths); a real long compound word behaves the same.
- **`Control.Visible` is false in a form that was never shown.** A test that asserted "this check box is hidden for the standard group" through `Visible` was true by accident (and a state check `Visible && Checked` inside the form logic was wrong for the same reason). Use `Enabled` or an explicit field instead; the form's own logic must not depend on `Visible`.
- **Idle hints named a button that no longer had a name:** "Press Record…" while the button was icon-only. Reworded to "record button". Lesson: when a control loses its text, search for the texts that refer to it.
- **A long user-supplied text in a chooser (Keyboard Editor).** A stored database name such as `old_db.wfq` plus the suffix "(not found)" ran past the chooser's width and the suffix was the part that got cut off. Put the status first and the user's text last, so an ellipsis cuts the less important end. A label beside a chooser also steals its width at +80 % text: put the label above the chooser when the value can be long.
- **A guard that checks the stress case finds layout decisions the normal case hides.** The Word prediction section looked fine at +0 % and +40 %, and failed only at +80 % because the "Database" label grew; the same guard found the 753 px height of the Group Editor earlier. Keep the +80 % case in every dialog guard.
- **Tests on a form that was never shown.** `Visible` is false, `DrawToBitmap` needs a handle, a docked control has no size until a layout ran: the helper `HostFor` (a hidden form with a handle) and `DevGallery.Show` exist for this. Calls to protected handlers go through reflection (`Key`, `Send`), and the data behind a dialog goes through a seam (`WordPredictionBackend`) so no test touches a real database.

## 3b. Layout rules (from the owner's reviews)

- **Neighbouring controls share one width (2026-10-04).** The old Keyboard Editor looked untidy because controls that stand under each other differed a little in width (Background, Always on top, Hide title bar; Sticky modifiers and Hold to enter edit mode; Slow keys and Dwell click), so no right edge lined up. Rules: (a) controls stacked in one column get the width of the widest of them; (b) buttons side by side (Save / Save As / Load, Promote / Reject) get one common width; (c) choosers under each other get one width; (d) a stepper next to a control sits in its own column so the steppers line up too. Built with `OptionStack` and `ButtonRow` (2026-10-04, spec D23) and checked by `UiGuard.StackedEdges`. The Group and Key Editors have no stacked check boxes, so they were not affected; check them against this rule when touched again.
- **"Exactly one of these" is a radio group, not two check boxes with an explanatory sentence (2026-10-04).** A group is a frame with a caption (the caption is its accessible name), button-shaped rows with a round mark, a 2 px accent border on the selected row (not only the dot), one tab stop with arrow keys inside, and the values that belong to an option (steppers) enabled only for the selected option. Components `TouchRadioButton` and `TouchGroup` (built 2026-10-04, spec D24).

## 4. Accessibility (WCAG AAA)

- The whole palette was below AAA: white on the blue, green and red buttons (4.5–5.7:1), hint and secondary text (5.3–6.5:1), the neutral border (1.3:1), and dark-theme text (1.9–5:1). Fixed. The blue, green and red buttons are now darker throughout the app, which is visible.
- **Coloured buttons on dark:** at AAA depth the coloured buttons would vanish into a dark background, so they get a light outline there.
- **Not audited for AAA:** the main toolbar, the wizard's own pages, the old editors' hand-typed colours.
- **Colorful wizard preset:** two groups are below AA (3.4:1 and 3.8:1); this is a design decision for the keyboard itself, not for the dialogs.
- **Focus appearance (WCAG 2.2, 2.4.13)** is not tested. **High contrast mode** is handled in the new controls but was not run.

## 5. Design decisions with side effects

- **Word prediction is Normal-only.** On such a key the label and the Shift/AltGr rows are emptied and disabled. This is safe because the click handler returns before reading Shift/AltGr, but an existing key loses those dead values the first time it is applied.
- **Modifier stays listed on Shift/AltGr** as unavailable. It is the only type still shown unavailable there. Whether it should go too is undecided.
- **An untouched Shift/AltGr layer is stored byte for byte**, because the readable form of a shortcut is lossy (grouping parentheses are dropped).
- **Colour chips always hold a concrete colour.** The old "empty = inherit from the group" state is not modelled. Group inheritance still works by comparing against the group's values.
- **The row of an action type differs per layer** (five on Normal, four on Shift/AltGr). The tests choose types by name for that reason.
- **Weakened guard:** the minimum accelerator count for the Key Editor was lowered from 10 to 6, because the flyout items have none. The accelerators still have to be unique.

## 6. Open items

- **Priority 5 and 6 (`todo.md`) are fixed** (see section 3). Remaining: Modifier → Text clears the value but the label stays "Shift" by design (the user may want it); undecided.
- **Record / Browse icons:** the Browse icon is the toolbar's `load.svg`; whether a folder icon reads as "pick a layout file" has not been tested with users. The record ring looks a little small in its button.
- **Priority 7:** borders and focus rings on the keys of the keyboard itself were never checked.
- **Still on the old layout:** the New Keyboard Wizard only. (The Group Editor was migrated on 2026-10-02, the Keyboard Editor on 2026-10-04.)
- **Group Editor:** the list's scroll bar is the native thin one; the Name field keeps one empty line for its error message; "More colours…" still opens the standard Windows colour dialog.
- **Narrow screens:** the Action row has five columns and fits 760 px; below that a stacked layout is not built.
- **Left over for cleanup:** `KeyEditorMockups.cs`, the old `AddColorRow`, `ROW_H` / `HDR_H`, `WrapInScrollPanel`.

## 7. What was not verified

- The app was never run and the Key Editor never used with mouse or touch. Everything is from tests and rendered screenshots (`DrawToBitmap`), not the real screen.
- **Group Editor (2026-10-02):** the whole dialog has only been built, tested and photographed (`DrawToBitmap`); it was never used with a mouse or a touch screen. Not tested: the Delete and F2 keys on the list, the real click on Add / Delete / Import (the dialogs they open are tested directly), "More colours…", and 125 % / 150 % scaling.
- **Now tested (2026-10-02):** keyboard and mouse handling of the chooser flyout and the colour flyout (`TouchChoiceColorTests.cs`), which found a real bug: Home / End did nothing when the first / last rows were unavailable. Still untested: opening them from a real control (placement, focus return) and the standard colour dialog behind "More colours…".
- **Not tested at all:** recording a shortcut through the real hook (only the label and send logic are unit-tested, and the key injection of Win+Shift), the stop icon on screen (the gallery cannot show the recording state), 125% and 150% display scaling, a real touch screen.
- The flyout closes when it loses focus; the case of the Windows colour dialog opening from it is handled, other focus changes are not.
- **Keyboard Editor (2026-10-04):** built, tested and photographed only; never used with a mouse or touch screen. Not verified: that the old dialog really saved the previous values (inventory B1), the whole Save / Save As / Load hand-off through the real `KeyboardForm` (never built in the suite), the Export file dialog, "More colours…" from the background chip, 125 % / 150 % scaling.

## 8. Process

- Two ways of working were rejected and saved as preferences: patching files with Python scripts, and working around a locked executable (a separate build folder, killing the app). When the build fails because the app is open, the user is asked to close it.
- Throwaway preview pages go in the project folder, not the scratchpad: the browser pane could not open a file in the scratchpad, and it shows files as static snapshots, so a preview must inline its images (relative links stay empty).
- A fix is not finished when the test passes: the width fix for the Record button passed its test and was still wrong on screen. For visual problems, render the gallery and look, in both languages.
- Several fixes came from the user's own use of the screenshots, not from the checks: the borders, the focus rings and the disabled look. A manual walk-through by the user remains the real test.

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
- **Still on the old layout:** the Keyboard Editor and the wizard. (The Group Editor was migrated on 2026-10-02.)
- **Group Editor:** the list's scroll bar is the native thin one; the Name field keeps one empty line for its error message; "More colours…" still opens the standard Windows colour dialog.
- **Narrow screens:** the Action row has five columns and fits 760 px; below that a stacked layout is not built.
- **Left over for cleanup:** `KeyEditorMockups.cs`, the old `AddColorRow`, `ROW_H` / `HDR_H`, `WrapInScrollPanel`.

## 7. What was not verified

- The app was never run and the Key Editor never used with mouse or touch. Everything is from tests and rendered screenshots (`DrawToBitmap`), not the real screen.
- **Group Editor (2026-10-02):** the whole dialog has only been built, tested and photographed (`DrawToBitmap`); it was never used with a mouse or a touch screen. Not tested: the Delete and F2 keys on the list, the real click on Add / Delete / Import (the dialogs they open are tested directly), "More colours…", and 125 % / 150 % scaling.
- **Now tested (2026-10-02):** keyboard and mouse handling of the chooser flyout and the colour flyout (`TouchChoiceColorTests.cs`), which found a real bug: Home / End did nothing when the first / last rows were unavailable. Still untested: opening them from a real control (placement, focus return) and the standard colour dialog behind "More colours…".
- **Not tested at all:** recording a shortcut through the real hook (only the label and send logic are unit-tested, and the key injection of Win+Shift), the stop icon on screen (the gallery cannot show the recording state), 125% and 150% display scaling, a real touch screen.
- The flyout closes when it loses focus; the case of the Windows colour dialog opening from it is handled, other focus changes are not.

## 8. Process

- Two ways of working were rejected and saved as preferences: patching files with Python scripts, and working around a locked executable (a separate build folder, killing the app). When the build fails because the app is open, the user is asked to close it.
- Throwaway preview pages go in the project folder, not the scratchpad: the browser pane could not open a file in the scratchpad, and it shows files as static snapshots, so a preview must inline its images (relative links stay empty).
- A fix is not finished when the test passes: the width fix for the Record button passed its test and was still wrong on screen. For visual problems, render the gallery and look, in both languages.
- Several fixes came from the user's own use of the screenshots, not from the checks: the borders, the focus rings and the disabled look. A manual walk-through by the user remains the real test.

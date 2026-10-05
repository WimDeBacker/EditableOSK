# Responsive dialog layout: specification (todo 2.4.6)

Status: **proposal, 2026-10-05, awaiting owner review.** Written from reading the code and from the width measurement in `NarrowWidthTests.cs`; nothing of this is built. It replaces the earlier idea of "stack the columns below N pixels in every dialog".

## 1. Goal and rule

Every dialog must stay fully usable when the window is narrower than its natural width: a small tablet, or 150 % to 200 % scaling on a 1366-px laptop (683 to 910 design pixels). Elements **move** (to a new line, under their label, into fewer columns) instead of being cut off; only vertical scrolling is allowed, never sideways.

**The rule that keeps the code free of pixel numbers:** a breakpoint is *derived from the measured width of the content* ("do these items fit side by side?"), never typed in. The only numbers that remain are design tokens that mean something by themselves (`Touch.Target` 44, `Touch.Gap` 8, a text box's minimum useful width), each defined once in `Touch`, like `min-width` in CSS.

## 2. Where we are (facts)

- `FluentDialogBase` + `FluentTheme` + the `Touch*` controls give look, DPI scaling, 44 px targets and **content-sized** windows (`BuildFrame`, `AddSection`, `AddRow`, `MeasureContent`, `FitToContent`). There is no reflow: the window is as wide as its content, capped at the screen, and what does not fit is cut off.
- Hard-coded widths that stand in for reflow: `ContentMaxWidth` (760 / 900 / 920 / 960 / 1040 per dialog), maximum text widths (`PageTextWidth` 836, `TextWidth` 860, `LabelMaxWidth` 260), an absolute label column in the Key Editor, minimum widths of 150 / 240, the Group Editor's two columns, five wizard tiles in one row, three help columns in one row.
- `FlowLayoutPanel` (the one WinForms container that wraps) measured wrongly inside tables, so the editors avoid it (`InlineRow`). That is why the wrapping rows in the Key Editor ("Key width / Key height") are the only ones that reflow.
- `SectionHost` scrolls vertically; content wider than the window is silently clipped by its table cell. The old overflow guard could not see this (docked controls); the new `UiGuard.CutOff` can.
- Measured (design px at 100 %, English / +40 % text), the width from which every section still passes the guards: Key Editor 676 / 740, Keyboard Editor 604 / 732, Group Editor 788 / 852, Wizard 844 / 692, special-keys window 892 / 892. The owner's screenshot (Key Editor, 175 %, about 463 design px) shows the Action row's value box and picker missing.

## 3. The contract that makes reflow possible in WinForms

A container can only wrap if its parent asks it "how tall are you at this width?" and gets the right answer. So every responsive piece below follows one contract:

- `GetPreferredSize(proposed)` honours `proposed.Width` when it is above zero (returns the size it needs *at that width*), and reports its natural width when it is zero.
- `OnLayout` and the preferred size use the **same** placement function, so what is measured is what is drawn (a unit test compares them).
- After a resize the container invalidates its own layout and its parent's, so rows below it move (the stale-row-height problem met in the wizard).
- A container is never docked inside an auto-sized cell whose width is unknown; the section host passes the real width down (`MeasureContent(width)` already exists).

## 4. Components

### 4.1 `WrapLabel`, and check boxes / radio buttons that wrap
Text that breaks onto more lines at the width it is given. Replaces every `Label` with a hand-set `MaximumSize` (hints, error lines, summary lines, intro lines) and lets `TouchCheckBox` / `TouchRadioButton` take two lines (the row stays one button-shaped 44 px target, taller when needed). No width constant: the width comes from the parent.

### 4.2 `ReflowPanel` (flex-wrap)
A panel that places its children in order, left to right, and starts a new line when the next child does not fit. Two modes:
- **Flow:** each child keeps its own preferred size (the Key width / Key height steppers, the "Font size + Auto" row, button groups, the help-window groups as cards).
- **Grid:** all children get one width, the widest; the number per line is as many as fit, the cells then stretch to fill the line (wizard theme tiles: 5, or 3 + 2, or 1 per line; help-window groups).
Properties: `Gap`, `LineGap`, `Mode`. Tab order and accessibility order stay the order of the children, whatever line they are on.

### 4.3 `AdaptiveTable` (layout variants)
For structured rows where the order of the cells matters. It holds one table and an ordered list of **variants** (each variant says which control goes in which cell and how much width it needs, measured from the controls). It shows the first variant whose measured width fits, and re-evaluates on resize. It moves controls between cells (`SetColumn`, `SetRow`, `SetColumnSpan`) rather than between parents.
Used for:
- the Key Editor's Action row (4.4);
- the Group Editor's top level: form and preview side by side, else the preview above, compact;
- **every `label | input` table built by `AddRow`, in the dialog base:** when the label column and an input of its minimum useful width do not fit side by side, each label goes above its input, as on a phone form. This single rule fixes most sections of every dialog without touching them.

### 4.4 The Action row of the Key Editor
Cells per layer: layer name, label box, action type, value box, picker button.
- Wide variant (today): name | label | type | value | picker on one line, with the column headers.
- Narrow variant: line 1: name, label box, type; line 2: value box and picker, starting under the label box and filling the width; the headers "Label" and "Action" stay above their cells.
- The three layers share the breakpoint (they are in one `AdaptiveTable`), so they switch together and the columns stay aligned.

## 5. Dialog by dialog

| Dialog | Change |
|---|---|
| Key Editor | Action row as 4.4; Appearance rows through the `AddRow` rule; Font size + Auto as a `ReflowPanel` |
| Keyboard Editor | check boxes and radio buttons wrap (4.1); two columns of the Accessibility section as a `ReflowPanel`; word-prediction candidate list keeps its minimum height |
| Group Editor | top level as an `AdaptiveTable` (preview beside or above); colour chips under their label via the `AddRow` rule; the inherit check box wraps |
| Wizard | hint and error lines become `WrapLabel`; theme tiles as a Grid-mode `ReflowPanel`; browse rows keep the box filling and the button at 44 px; footer buttons wrap onto two lines if needed (Cancel, Back, Next) |
| Special-keys window | groups as cards in a Grid-mode `ReflowPanel` (3, 2 or 1 columns) |
| Small dialogs (name, import, message) | `WrapLabel` only |

## 6. Window sizing on a narrow screen

`FitToContent` already caps the window at the screen. With the contract in section 3 it measures the height at the capped width, so the window opens as tall as the wrapped content needs (up to the screen) and scrolls vertically beyond that. `MinimumSize` is the smaller of the content's minimum and the screen. Dragging the window narrower reflows live.

## 7. Tests

- **Width sweep (permanent):** each dialog, each section, in English / Dutch / +40 % / +80 %, light and dark, at the supported minimum width, and every 40 px up to its natural width: no control cut off (`CutOff`), nothing sticks out, no clipped text, all targets at least 44 px, and the dialog is not wider than the width it was given. `NarrowWidthTests.cs` becomes this test; the supported minimum is the one number the owner chooses (decision D1).
- **Container unit tests:** `ReflowPanel` places N children as expected for a series of widths (Flow and Grid), measured height equals laid-out height, tab order unchanged; `AdaptiveTable` picks the right variant at the boundary width and one pixel either side, and moves nothing else; `WrapLabel` height for a width equals what is drawn; the `AddRow` rule stacks exactly when label plus minimum input do not fit.
- **Pictures:** the gallery saves each dialog at the minimum width and at 683 px, so the look can be judged.
- The existing guards keep running at the natural width, so nothing gets worse for wide screens.

## 8. Decisions for the owner

| # | Question | Proposal |
|---|---|---|
| D1 | The narrowest supported window width (design px at 100 %) | **480.** It covers the screenshot (463 is just under it; the Action row's second line fits there) and 200 % scaling on a laptop. Below it, vertical scrolling and anything that fits, no guarantee |
| D2 | Sideways scrolling | Never. Only vertical |
| D3 | Labels above inputs when side by side does not fit (rule in `AddRow`) | Yes, everywhere at once |
| D4 | Action row narrow layout (4.4) | Name, label, type on line 1; value and picker on line 2 |
| D5 | Group Editor preview when narrow | Above the form, small |
| D6 | Wizard tiles and help-window groups | Equal-width grid, as many per line as fit |
| D7 | Footer buttons that do not fit on one line | Wrap onto a second line, all one width |
| D8 | Order of work | R1 `WrapLabel` and wrapping check boxes (fixes the Keyboard Editor and wizard hints) → R2 `ReflowPanel` → R3 `AdaptiveTable` and the `AddRow` rule → R4 Key Editor Action row → R5 Group Editor → R6 wizard → R7 help window → R8 the permanent width sweep and the docs |

## 9. Risks and what is not verified

- The reflow containers must give the right height at a given width inside WinForms tables; this is where the wizard showed stale row heights. Each container gets its own tests before any dialog uses it (R1 to R3 end with a clean suite).
- Moving controls between cells while a control has the focus must not lose the focus (tested).
- Narrator reading order after a reflow is not checked yet.
- At the minimum width the Group Editor's list and the Keyboard Editor's candidate list need a minimum height, or they would shrink to nothing; the exact values are to be judged in the pictures.
- Cost: this touches every dialog; the suite stays the safety net, and the work is done in the steps of D8, each ending with a clean build and the full suite (during a step, `--test <group> --quick`).

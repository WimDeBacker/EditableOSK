# New Keyboard Wizard redesign: specification (todo 2.4.5.3)

Status: **approved by the owner on 2026-10-04: D1-D10 as proposed (D3 = read-only grid size text), plus: the preview shows the key labels live** (section 2, page 2). Written from reading the code only; nothing is built or run yet. Inventory and defects (W1-W14): `wizard_inventory.md`.
## Current state (2026-10-06)

Built on 2026-10-04/05 as `NewKeyboardWizard : FluentDialogBase` (todo 2.4.5.3). The proposal below was approved; this is what the wizard is now and where it differs:

- **Pages and sections.** Four pages (Start, Grid, Theme, Save) shown in six host sections: page 2 is one of three sections (blank grid: rows / columns steppers and a preview; pasted labels: the box, a size line and a preview; copy from file: one line), chosen from page 1, so the window is as tall as the tallest section, not the sum.
- **Footer** (standard, adaptive): "Step n of 4", Cancel, Back, Next / Create, all one width; the step text goes above the buttons when they do not fit together. Enter = Next / Create, Escape = Cancel.
- **Resizable** (changed on 2026-10-06; D8 said "not resizable"): `Sizable`, not narrower than 480 design px. Everything reflows: hint and error lines wrap (`Wrap`), the five theme tiles are `Tiles(5, 3, 2, 1)` (a tile is as wide as its longest word needs), the ten sample keys are controls on one line, else two of five, the language chooser is 160–280 px wide, browse text boxes keep 120 px at least. The permanent test (`T_NarrowDialogs`) requires it to fit from 480 px (520 at +40 % text); measured on 2026-10-05 it fitted from 400 / 444 / 456 px.
- **Validation** is inline under the field (no message boxes); an existing file is only replaced after a Yes / No question (default No); invalid file-name characters are refused with a clear reason.
- **Pasted labels** support more than letters: `[shift] [ctrl] [alt] [altgr] [win] [caps]`, `[home] [end] [pageup] [pagedown] [insert] [printscreen] [numlock] [scrolllock] [pause]`, `[f1]` … `[f16]` and dead keys (`[dead:^]` or `[circumflex] [umlaut] [tilde] [grave] [acute]`), any case, with a few English / Dutch aliases. A **? button** next to the special-keys hint opens the full list (`SpecialKeysDialog`, `WizardKeyHelp.cs`, built from `WizardKeyParser.Help`; a test checks that the list and the parser agree).
- **Deviations from the proposal:** the overwrite question is a plain Yes / No, not "Replace / Choose another name"; the descriptions under the start options are single short lines; the paste page has no separate hint line for "one row per line" (it is the page subtitle); D8 as above.
- **Tests:** `T_Wizard` (navigation, validation, preview, tiles and sample keys, create), `T_WizardGuards` (all pages in four text sizes, both themes, two dialog states), `T_WizardControls`, `T_WizardSpecialKeys`, `T_NarrowDialogs`, and the older `T_WizardKeyParser`, `T_WizardKeyClassifier`, `T_WizardThemePresets`, `T_WizardBuildLayoutData_ThemeFileMerge`, `T_WizardThemePage_GroupSwatchColors`.
- **Pictures:** `OnScreenKeyboard.exe --gallery <folder>` saves `wizard_*` and `wizard_keys_*` (normal width) and `narrow_wizard_*`, `narrow_specialkeys_*` (480 px). The HTML mock-up that belonged to this document was deleted.

## 1. Goal and rules

Rebuild `NewKeyboardWizard` as a content-sized `FluentDialogBase` with `Touch*` controls, like the three editors. Same alignment rule (D23: stacked controls one width, side-by-side buttons one width, checked by `UiGuard.StackedEdges`), WCAG AAA, light and dark, fits 1366 x 768 at +80 % text. Public surface stays: `CreatedFilePath`, `Presets`, `ClassifyKey`, parameterless constructor.

## 2. Structure

```
NewKeyboardWizard : FluentDialogBase       ContentMaxWidth 820
  BuildFrame(MakeFooter(_lblStep, _btnCancel, _btnBack, _btnNext/_btnCreate), withSections: false)
  one page host (SectionHost without a bar): window height = tallest page, so it never jumps between pages
  page title (Heading) + one-line subtitle on every page
```

Footer: left slot "Step 2 of 4"; buttons Cancel, Back, Next (the last page shows Create in the same slot). All footer buttons one width. Enter = Next / Create; Escape = Cancel.

### Page 1: Start
`TouchGroup` "Starting point" holding three `TouchRadioButton`s (Blank grid, Type or paste key labels, Copy from an existing layout file), each with its description line (the existing tip text) shown under it, not only as tooltip. Under the third: layout file row (`TouchTextBox` + 44 px Browse button), shown only when Copy is chosen. Below the group: Language chooser (`TouchChoiceButton`), with the hint line "Language of the new keyboard's labels and tips".

### Page 2: Grid and labels (content depends on page 1)
- Blank: rows and columns `TouchStepper`s (1-30, 1-60) + preview.
- Paste: hint paragraph, multi-line `TouchTextBox` (min 6 lines), grid size shown as read-only text "4 rows x 8 columns" (W4), preview.
- Copy: one line "Keys will be copied from: file name" + a summary of that file (rows x columns); no preview needed.
The preview is a small grid control (`WizardGridPreview`) fed with the parsed rows; it **shows the key labels live** while typing (centred, ellipsis for long phrases, blank for `_`, the gear in the reserved cell). Keys keep a minimum width: with many columns the preview scrolls sideways instead of shrinking the font to an unreadable size. Accessible description: "Preview: 4 rows, 9 columns, last column holds the gear key". Test: the parsed labels reach the control.

### Page 3: Theme
Five selectable tiles in a radio group (`TouchRadioButton`-style card, arrow keys move, Space selects): Dark, Light, High Contrast, Colorful, From file. A tile shows a miniature of three sample keys in that theme, the name, and a check mark **and** a thick ring when selected (W8). From-file row (text box + Browse) appears under the tiles when chosen. Below: the sample-key strip (ten keys, one per group) as in the old preview, in a framed card.

### Page 4: Save
File name (`TouchTextBox`), Folder (`TouchTextBox` + Browse), inline error line (`_err`, role alert), then a summary card with label: value rows (Grid, Theme, Language, Always on top: Yes / title bar shown). Create writes the file.

## 3. Validation (no MessageBox)

| Where | Rule | Message |
|---|---|---|
| Next on page 1 | Copy chosen and the file does not exist | `wiz: err no copy file` (inline under the field, field focused) |
| Next on page 2 | Paste chosen and the text parses to zero keys | `wiz: err empty paste` |
| Next on page 3 | From file chosen and no existing file | `wiz: err no theme file` |
| Create | empty name; name contains an invalid character (new, W6); folder missing | `wiz: err no name`, new `wiz: err bad name`, `wiz: err bad folder` |
| Create | file already exists (new, W5) | confirm with `TouchMessage` "Replace it?" (Replace / Choose another name) |

Errors use the shared `ErrorProvider` mechanism; the page with the first error is shown.

## 4. Decisions for the owner

| # | Question | Proposal |
|---|---|---|
| D1 | Cancel button and Enter / Escape behaviour (W7) | Yes: Cancel in the footer; Enter = Next / Create; Escape = Cancel |
| D2 | Footer instead of the dark-navy custom nav bar (W9) | Yes: the standard dialog footer, same as the editors |
| D3 | Paste mode: rows / columns read-only text (W4) | Yes. Alternative: keep the steppers and let them pad or crop the pasted grid (more work, more power) |
| D4 | Overwrite protection (W5) | Ask before replacing an existing file |
| D5 | Theme tiles as cards with a miniature + check mark + ring | Yes |
| D6 | Language chooser on page 1: also switch the wizard's own language live (W12)? | No: keep it the keyboard's language; relabel it "Keyboard language" so it is clear |
| D7 | Add "Always on top" and "Hide title bar" check boxes to the Save page (the old strings exist, unused; the Keyboard Editor already offers them) | No: keep the defaults (always on top, title bar shown) and say so in the summary |
| D8 | Window: not resizable, sized from content (W1) | Yes; scrolls inside the page if it ever exceeds the screen |
| D9 | Show a contrast warning for a from-file theme or Colorful (the unused `wiz: fail contrast` strings and `WizardThemeValidator`) | No for now (belongs to 2.5.4); delete the unused strings in 2.4.7 |
| D10 | Page 4 summary: keep it, as a text card with accessible text (W10, W11) | Yes |

## 5. Build steps (each ends with a clean build and the full suite)

- W1 Components: selectable theme tile control (`TouchTile`), read-only value row, browse-row helper (text box + 44 px button) if not yet in the base; unit tests.
- W2 Frame, footer, page host, navigation, Enter / Escape, Cancel; pages as empty shells; guards.
- W3 Page 1 and 2 (copy row, language, steppers, paste box, preview); inline validation.
- W4 Page 3 (tiles, from-file row, sample strip) with the existing colour functions untouched.
- W5 Page 4 (name, folder, summary, overwrite, invalid name) and `TryCreate`; adapt the two reflection tests.
- W6 Tests `WizardTests.cs` + `DevGallery.SaveWizard`, look at the pictures in en / nl, light / dark; docs (`todo.md`, `formdesign.md`), remove old code and unused strings.

## 6. Not verified yet

How the page host behaves when pages differ much in height (page 2 Paste vs Page 2 Copy); whether `TouchRadioButton` can carry the description line; how the card tiles look at +80 % in Dutch; Narrator announcements of the preview. These are checked in steps W1-W6.

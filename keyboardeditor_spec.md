# Keyboard Editor redesign: specification (todo 2.4.5.2)

Status: proposal, written 2026-10-02 against commit 27aa301 (branch `claude/keyboard-editor-prep`). Written from reading the code only: nothing was built, run, photographed or tested. Every decision marked **decided by Claude, to be reviewed** is a proposal for the owner to confirm or change.

**Review round, 2026-10-04 (owner).** Confirmed as proposed: D3, D4, D6, D8 (two columns), D12. D1 confirmed with an addition: Save / Save As / Load stay in the dialog **and** in the toolbar. D7 **changed**: Slow keys and Dwell click become radio buttons in a framed "Timing aid" group (sections 5 and 11). New rule from the owner: **controls that stand under each other share one width, so their right edges line up** (D23, section 1). The text and the mock-ups below follow these answers.

Companion file: `keyboardeditor_inventory.md` (what the old dialog does, defects, hidden state, tests that touch it). The HTML mock-ups that once belonged to this document were deleted on 2026-10-06; the gallery (`OnScreenKeyboard.exe --gallery <folder>`) shows the real dialog.

## Current state (2026-10-06)

Built on 2026-10-04 as `KeyboardEditorForm : FluentDialogBase` (todo 2.4.5.2), with the decisions of the review round above. The text below is the design as proposed; this is what differs or was added since:

- **Three sections** in a `SectionBar`: General (language, toolbar theme, opacity stepper, background colour chip, Always on top, Hide title bar, Save / Save As / Load), Accessibility (Sticky modifiers, Hold to enter edit mode, the framed "Timing aid" radio group with its steppers and the animation check box, corner labels), Word prediction (learning, database chooser, candidates list with Promote / Reject, Export). Footer: Cancel, Apply. `ContentMaxWidth` 920.
- **Responsive (2026-10-05, `responsive_spec.md`, section 10).** Nothing has a fixed width that could cut it off: the language and toolbar-theme choosers are 280 px wide when there is room and shrink to 160 px; hint and check-box texts wrap (`Wrap`); the Timing aid grid puts a stepper and its unit on a line under the radio button when they do not fit beside it; the word-prediction settings and candidates are side by side, else the candidates go under the settings; Promote / Reject are side by side, else stacked; Save / Save As / Load share one width and stack when they do not fit. The arrangement is chosen from the measured width of the controls, not from a typed-in breakpoint.
- **Fits from** 396 px (English), 404 (Dutch), 444 (+40 % text), 520 (+80 %) design pixels; the permanent test (`T_NarrowDialogs`) requires 480 / 480 / 520 / 540.
- **Sticky modifiers** are explained by their tooltip (tap twice to lock, a third time to release; `ModifierLatch`, `T_StickyModifiers`).
- **Tests:** `T_KeyboardEditorGuards` (all states, four text sizes, both themes), `T_KeyboardEditor` (every control writes its field, Save / Save As / Load, language restore on Cancel, database and candidates through the `WordPredictionBackend` seam), `T_KeyboardEditorAlignment` (rule D23), `T_NarrowDialogs`.
- **Pictures:** `OnScreenKeyboard.exe --gallery <folder>` saves `keyboardeditor2_*` (normal width) and `narrow_keyboardeditor_*` (480 px).

## 1. Goal and rules that apply

Rebuild `KeyboardEditorForm` the way `GroupEditorForm` and `KeyEditorForm` are built: content-sized `FluentDialogBase` (`BuildFrame` / `AddSection`), a `SectionBar` with three sections (General, Accessibility, Word prediction), `Touch*` controls of at least 44 x 44 px, table / non-wrapping inline layout, no hand-positioned control, WCAG 2.1 AAA in the light and the dark theme, English and Dutch. From `formdesign.md` this design applies: no hidden flags or values (every state is visible and explained in words), no control whose text changes with state or language inside an auto-sized column, short visible text with the long text as accessible name and tooltip, no wrapping `FlowLayoutPanel` inside a table (use the non-wrapping `InlineRow`), always-present (empty) message labels so the window is measured with them, never read `Control.Visible` in the form's logic or in tests, pseudo-localisation guards at +40 % and +80 %, and a mock-up before the code.

**Alignment rule (owner, 2026-10-04).** The old dialog looked untidy because two similar controls that stand under each other differed slightly in width, so their right edges did not line up (Background, Always on top and Hide title bar; Sticky modifiers and Hold to enter edit mode; Slow keys and Dwell click). The rule for every dialog: (a) controls that are stacked in one column (check boxes, radio buttons, chips, a button that stands alone in a stack) get **one width, the width of the widest of them** (the column is auto-sized and every member `Dock = Fill`); (b) **buttons side by side** (Save / Save As / Load, Promote / Reject, Cancel / Apply) get **one common width**, the widest; (c) drop-down choosers that stand under each other get one width; (d) in a row that has an extra control on the right (a stepper), the stepper sits in its own column so the steppers' right edges also line up. Implementation: `FluentDialogBase.OptionStack(params Control[])` (a one-column auto-sized table, children `Dock = Fill`) and `FluentDialogBase.ButtonRow(params Control[])` (an `InlineRow` whose buttons all get the maximum preferred width as `MinimumSize`). Test: `UiGuard.StackedEdges(root)` (section 12). Where this applies is marked in the control tables as "stack" / "button row".

Public API stays: `ResultTheme`, `ResultWindow`, `ResultMeta`, `ResultGroups`, the 4-argument constructor used by tests and the gallery. The only contract change is how Save / Save As work (decision D1).

## 2. Structure

```
KeyboardEditorForm : FluentDialogBase            // parameterless base ctor = content-sized
  protected override int ContentMaxWidth => 920  // the Group Editor uses 980; see D11
  BuildFrame(MakeFooter(null, _btnCancel, _btnApply), withSections: true)   // no headerRight, no preview card
  AddSection(() => Lang.T("General"))                  -> BuildGeneral(t)
  AddSection(() => Lang.T("Accessibility"))            -> BuildAccessibility(t)
  AddSection(() => Lang.T("wp: Word prediction"))      -> BuildWordPrediction(t)
```

Helpers that are private in `GroupEditorForm` move into `FluentDialogBase` (protected): `InlineRow(params Control[])` (non-wrapping table row, one auto column per control) and `Heading(...)` / `AddHeading(table, text)` (bold label spanning both columns). Both editors then share them.

The section host sizes the window to the tallest section (`SectionHost.GetPreferredSize`), so the window does not change size when switching. One `_loading` flag (set only while code fills controls) is the only flag; `Apply` never reads it.

The Word prediction section copies the Group Editor's `md` table: a two-column `TableLayoutPanel` whose cells hold non-docked inner tables anchored Top | Left | Right (see section 6).

## 3. Strings

### 3.1 Existing strings that are reused unchanged (English / Dutch from `LanguageManager.cs` / `lang_nl.xml`)

| Key | EN | NL |
|---|---|---|
| `Edit Keyboard` | Edit Keyboard | Toetsenbord bewerken |
| `Accessibility` | Accessibility | Toegankelijkheid |
| `wp: Word prediction` | Word prediction | Woordvoorspelling |
| `Apply` | A&pply | Toe&passen |
| `Cancel` | &Cancel | Ann&uleren |
| `Language` | Language | Taal |
| `Toolbar theme` | T&oolbar theme | &Werkbalkthema |
| `Dark` / `Light` / `System default` | Dark / Light / System default | Donker / Licht / Systeemstandaard |
| `Window` | Window | Venster |
| `Opacity` | Transparency | Transparantie |
| `Background` | Background | Achtergrond |
| `Always on top` | &Always on top | Altijd &bovenaan |
| `Hide title bar` | H&ide title bar | Titelbalk &verbergen |
| `Layout file` | Layout file | Lay-outbestand |
| `Save` / `Save As…` / `Load…` | Save / Sa&ve As… / Load… | Opslaan / Op&slaan als… / Laden… |
| `Sticky modifiers` | Stic&ky modifiers | Plaktoetsen (Sticky &Keys) |
| `Hold to edit` | &Hold to enter edit mode | &Ingedrukt houden voor bewerkingsmodus |
| `Slow keys` | Slow k&eys | La&ngzame toetsen |
| `Dwell click` | &Dwell click | Automatis&che klik |
| `Show timing animation` | Sho&w timing animation | Voo&rtgangsanimatie tonen |
| `Show Shift and AltGr labels` | see 3.2 | see 3.2 |
| `wp: Remember typed words` | Remember typed words | Getypte woorden onthouden |
| `wp: Database` | Database | Database |
| `wp: Auto` | (auto) | (automatisch) |
| `wp: learning on` / `wp: learning off` | remembering words / not remembering words | onthoudt woorden / onthoudt geen woorden |
| `wp: words` | words | woorden |
| `wp: No database loaded` | No database loaded | Geen database geladen |
| `wp: Export…` | Export… | Exporteren… |
| `wp: Export learned words` | Export learned words | Geleerde woorden exporteren |
| `wp: Export failed` | Export failed: | Exporteren mislukt: |
| `wp: Candidates` | Candidates | Kandidaten |
| `wp: Promote` / `wp: Reject` | Promote / Reject | Toevoegen / Verwijderen |
| `tip: Language`, `tip: Opacity`, `tip: Slow keys`, `tip: Dwell click`, `tip: Show timing animation`, `tip: Show Shift and AltGr labels`, `tip: Load`, `wp: tip remember`, `wp: tip database`, `wp: tip export`, `wp: tip candidates`, `wp: tip promote`, `wp: tip reject` | unchanged | unchanged |
| `Decrease` / `Increase` | (stepper button names) | Verlagen / Verhogen |
| `Yes` / `No` / `OK` | (TouchMessage) | Ja / Nee |

### 3.2 Existing strings that get an accelerator (the same precedent as the Priority 3 audit)

| Key | EN now | EN new | NL now | NL new |
|---|---|---|---|---|
| `Show Shift and AltGr labels` | Show Shift and AltGr labels | Show Shi&ft and AltGr labels | Shift- en AltGr-labels tonen | Shi&ft- en AltGr-labels tonen |

The Dutch text is unchanged apart from the `&`. `CornerLabelTests` only checks that the Dutch string differs from the English one; every `Lang.T` call site for this key must `StripMnemonic` for accessible names (the old form already does).

### 3.3 New strings (add to `LanguageManager.cs` and `lang_nl.xml`)

| Key | EN | NL |
|---|---|---|
| `General` | General | Algemeen |
| `milliseconds` | milliseconds | milliseconden |
| `kbd: Timing aid` | Timing aid | Tijdhulp |
| `kbd: Off` | Off | Uit |
| `tip: Timing off` | No timing aid: a key registers as soon as you press it, or click it. | Geen tijdhulp: een toets wordt geregistreerd zodra u erop drukt of klikt. |
| `tip: Sticky modifiers` | Shift, Ctrl and Alt stay on after one tap and switch off after the next key, so you never have to hold two keys at once. | Shift, Ctrl en Alt blijven na één tik aan en gaan uit na de volgende toets, zodat nooit twee toetsen tegelijk ingehouden hoeven te worden. |
| `tip: Hold to edit` | Hold a key for a moment to open its editor. A quick tap still types the key, so the editor does not open by accident. | Houd een toets even ingedrukt om de editor te openen. Een korte tik typt de toets gewoon, zodat de editor niet per ongeluk opent. |
| `tip: Always on top` | Keep the keyboard floating above all other windows. | Het toetsenbord boven alle andere vensters houden. |
| `tip: Hide title bar` | Hide the Windows title bar for a cleaner floating keyboard look. | De Windows-titelbalk verbergen voor een strakker zwevend toetsenbord. |
| `tip: Toolbar theme` | Colours of the toolbar above the keyboard: dark, light, or the same as Windows. | Kleuren van de werkbalk boven het toetsenbord: donker, licht of gelijk aan Windows. |
| `tip: Background` | Colour of the keyboard window behind the keys. Click to choose. | Kleur van het toetsenbordvenster achter de toetsen. Klik om te kiezen. |
| `tip: Save settings` | Apply the changes and save the layout file. | De wijzigingen toepassen en het lay-outbestand opslaan. |
| `tip: Save settings as` | Apply the changes and save the layout file under a new name. | De wijzigingen toepassen en het lay-outbestand onder een nieuwe naam opslaan. |
| `kbd: Load title` | Load a layout | Een lay-out laden |
| `kbd: Load msg` | Loading replaces the current keyboard at once. Changes made in this window are lost, and Cancel cannot undo the load.\n\nContinue? | Laden vervangt het huidige toetsenbord meteen. Wijzigingen die in dit venster zijn gemaakt gaan verloren en Annuleren maakt het laden niet ongedaan.\n\nDoorgaan? |
| `wp: nothing to export` | Export becomes available after words have been learned. | Exporteren kan zodra er woorden zijn geleerd. |
| `wp: no candidates` | No candidates yet. | Nog geen kandidaten. |
| `wp: learning off hint` | Candidates are collected while "Remember typed words" is on. | Kandidaten worden verzameld zolang "Getypte woorden onthouden" aan staat. |
| `wp: immediate` | Promote and Reject take effect at once; Cancel does not undo them. | Toevoegen en Verwijderen werken meteen; Annuleren maakt ze niet ongedaan. |
| `wp: (not found)` | (not found) | (niet gevonden) |
| `warn: database not found` | The database file '{0}' was not found. It stays selected until another one is chosen. | Het databasebestand '{0}' is niet gevonden. Het blijft geselecteerd tot een ander wordt gekozen. |

The `{0}` placeholder must match between English and Dutch (existing structural test). Every `tip:` key above must be registered and translated (existing structural test). The unit "ms" and "%" next to a stepper are not translated.

## 4. Section 1: General

The section is a two-column table (label | input) built with `AddRow` / `AddWideRow`, with two headings. **Alignment (section 1):** the two choosers (G1, G3) have one width (280 px, `MinimumSize` = `Size` = 280 x 44); G5, G6 and G7 are one `OptionStack` (the Background chip, Always on top and Hide title bar: one width, the widest of the three, right edges aligned); G9 is a `ButtonRow` (Save, Save As, Load: one common width). Order, top to bottom. "Acc." = accelerator letter (EN / NL). Accessible name is the visible text without `&`. Check boxes are `NewCheck(...)`, added with `AddWideRow(t, c, fill: false)`.

| # | Field | Component | Visible text EN / NL | Accessible name EN / NL | Tooltip key | Acc. | Notes |
|---|---|---|---|---|---|---|---|
| G1 | `_cmbLanguage` | `TouchChoiceButton` (RowHeight 44, `MinimumSize` 240 x 44), `AddRow(fill: false)` | label "Language" / "Taal"; items are the native names from `Lang.GetAvailable()` ("English", "Nederlands") | Language / Taal | `tip: Language` | none / none (L and T are taken by Load and Transparency) | Live change with restore on Cancel (D3). Selection re-synced after Load and after any `LanguageChanged`. Initial focus. |
| G2 | heading | `Heading` | Window / Venster | (not focusable) | - | - | |
| G3 | `_cmbToolbarTheme` | `TouchChoiceButton` (RowHeight 44, `MinimumSize` 240 x 44), `AddRow(fill: false)` | label "Toolbar theme" / "Werkbalkthema"; items Dark/Donker, Light/Licht, System default/Systeemstandaard | Toolbar theme / Werkbalkthema | `tip: Toolbar theme` (new) | O / W | Items hold the `ToolbarTheme` value explicitly (no index = enum coupling); re-translated on `LanguageChanged`. Does not re-theme the open dialog (as before). |
| G4 | `_stpOpacity` | `TouchStepper` 0..80, increment 5, then a "%" label; `AddRow` (fill: false) via `InlineRow` | label "Transparency" / "Transparantie" | Transparency / Transparantie; description "0 = fully opaque, 80 = nearly transparent" (the `tip: Opacity` text) | `tip: Opacity` on the value box | T / T | Replaces the `TrackBar` (D6). Stored `Opacity = clamp((100 - v) / 100, .2, 1)`; the source value is kept when `v` equals the rounded source (D14). |
| G5 | `_chipBackground` | `ColorChip` captioned "Background" / "Achtergrond"; stack member (G5, G6, G7 in one `OptionStack`, `AddWideRow(fill: false)`) | chip text "Background" / "Achtergrond" | generated by `ColorChip`: "Background color #1A1A2E" (see risk R7: the word "color" is English-only) | `tip: Background` (new) | none | No hex box: the chip flyout has palette, hex and "More colours…" (D10). |
| G6 | `_chkAlwaysOnTop` | `NewCheck` | Always on top / Altijd bovenaan | Always on top / Altijd bovenaan | `tip: Always on top` (new) | A / B | |
| G7 | `_chkHideTitlebar` | `NewCheck` | Hide title bar / Titelbalk verbergen | Hide title bar / Titelbalk verbergen | `tip: Hide title bar` (new) | I / V | |
| G8 | heading | `Heading` | Layout file / Lay-outbestand | - | - | - | |
| G9 | `_btnSaveFile`, `_btnSaveAsFile`, `_btnLoadFile` | three `MakeTouchButton` in one `ButtonRow` (one common width), `AddWideRow(fill: false)` | Save / Opslaan, Save As… / Opslaan als…, Load… / Laden… | Save / Opslaan, Save As / Opslaan als, Load / Laden | `tip: Save settings`, `tip: Save settings as` (new), `tip: Load` (existing) | S / O, V / S, L / L | Behaviour in section 9. The same three actions exist in the toolbar and stay there (owner, D1). |

Window title: `Edit Keyboard` / `Toetsenbord bewerken`.

## 5. Section 2: Accessibility

The whole section is one `OptionStack` (alignment rule, section 1): A1, A2, the timing group and A6 are stacked in one column of one width, so their right edges line up. The section bar button is "Accessibility" / "Toegankelijkheid".

| # | Field | Component | Visible text EN / NL | Accessible name EN / NL | Tooltip key | Acc. | Notes |
|---|---|---|---|---|---|---|---|
| A1 | `_chkStickyMods` | `NewCheck` (stack member) | Sticky modifiers / Plaktoetsen (Sticky Keys) | same | `tip: Sticky modifiers` (new) | K / K | |
| A2 | `_chkHoldToEdit` | `NewCheck` (stack member) | Hold to enter edit mode / Ingedrukt houden voor bewerkingsmodus | same | `tip: Hold to edit` (new) | H / I | The Dutch text is the widest in the stack: it sets the stack's width. |
| A3 | `_grpTiming` | **new** `TouchGroup` (a framed panel with a caption, see below; stack member) | caption: Timing aid / Tijdhulp | Timing aid / Tijdhulp (role group) | - | - | Holds A3a to A3d. |
| A3a | `_optTimingOff` | **new** `TouchRadioButton` | Off / Uit | Off / Uit | `tip: Timing off` (new) | none | Selected when `SlowKeysMs == 0 && DwellMs == 0`. |
| A3b | `_optSlowKeys` + `_stpSlowKeys` + unit | `TouchRadioButton`, `TouchStepper` 100..3000 step 50 (default 300), label "ms" | Slow keys / Langzame toetsen, stepper, "ms" | radio: Slow keys / Langzame toetsen; stepper: same name + description "milliseconds" / "milliseconden" | `tip: Slow keys` on the radio and the value box | E / N | The stepper is enabled only while this radio is selected; its value is kept when another radio is selected (hidden state D14: kept, shown, greyed). Stored `SlowKeysMs = selected ? value : 0`. |
| A3c | `_optDwell` + `_stpDwell` + unit | same, 100..5000 step 100 (default 1000) | Dwell click / Automatische klik, stepper, "ms" | same pattern | `tip: Dwell click` | D / C | |
| A3d | `_chkTimingAnimation` | `NewCheck` inside the group, below a separator line | Show timing animation / Voortgangsanimatie tonen | same | `tip: Show timing animation` | W / R | Enabled while A3b or A3c is selected (`TimingAvailable()`), disabled (greyed, value kept) while Off is selected. Needs no hint text: the group shows why. |
| A4 | `_chkCornerLabels` | `NewCheck` (stack member) | Show Shift and AltGr labels / Shift- en AltGr-labels tonen | same | `tip: Show Shift and AltGr labels` | F / F | New accelerator (3.2). `LayoutMeta.ShowCornerLabels`. |

Order: A1, A2, the timing group, A4 (the new option last, as in 9.4; D17). The old timing hint label (A6) is gone: with radio buttons the exclusion is visible, so it needs no sentence.

**How the radio buttons look like one group (owner request, D7).** (1) A **frame**: a 1 px rounded border in the control-border colour with a slightly different fill than the dialog (`#F7F7F7` light, `#383838` dark; both keep the text at 7 : 1) and 12 px padding; (2) a bold **caption** "Timing aid" / "Tijdhulp" at the top inside the frame, which is also the group's accessible name; (3) the three radio rows are **button-shaped** like the check boxes (44 px, neutral fill, 1 px border) with a round mark (24 px circle, a filled accent dot when selected) and the **selected row gets a 2 px accent border** (a light border on dark) so the choice is visible without relying on the dot; (4) inside the frame a small **grid**: column 1 holds Off, Slow keys, Dwell click and (under a thin separator line) the animation check box, **all the same width, right edges aligned**; column 2 holds the two steppers (right edges aligned); column 3 the "ms" unit; (5) **one tab stop** for the three radios (the selected one); Up / Down / Left / Right move the selection and the focus, Space selects. The mock-up `mockups/keyboardeditor_accessibility.html` shows all of it, with the behaviour (click or arrow keys).

New components (build steps S1, S2): `TouchRadioButton : RadioButton` (button-shaped row like `TouchCheckBox`, 44 px, round mark, selected border, draws and measures `&`), `TouchGroup` (a `Panel` with caption, frame and fill, content-sized, `AccessibleRole.Grouping`).

Behaviour that is kept: Slow keys and Dwell click exclude each other, and the animation setting is only usable while one of them is on. Differences: the exclusion is now structural (a radio group cannot hold two values), a file with both values above 0 shows **Slow keys** selected (it is the first option) and the next Apply stores Dwell as 0 (the old code kept Dwell: documented, not silently changed in meaning), and the steppers widen their range to hold a loaded value outside 100..3000 / 100..5000 (D14).

## 6. Section 3: Word prediction

Two columns, left = settings, right = candidates (D8, confirmed by the owner). Alignment (section 1): in the left column W1 (check box), W2 (chooser, with its label to the left) and W5 (Export) fill the column width, so their right edges line up; W8 is a `ButtonRow` of two equal halves under the list, and the list and the buttons end at the same right edge. Each column is a non-docked inner table anchored Top | Left | Right; the outer table has two percent columns (50 / 50) with a `Touch.Gap * 2` gutter. The section bar button is "Word prediction" / "Woordvoorspelling".

| # | Field | Component | Visible text EN / NL | Accessible name EN / NL | Tooltip key | Acc. | Notes |
|---|---|---|---|---|---|---|---|
| W1 | `_chkWPLearning` | `NewCheck` | Remember typed words / Getypte woorden onthouden | same | `wp: tip remember` | R / G | Written only on Apply (D4). Toggling it changes the info line and the candidate controls, nothing else. |
| W2 | `_cmbWPDatabase` | `TouchChoiceButton` (RowHeight 44, not searchable), `AddRow` | label "Database" / "Database"; items "(auto)" / "(automatisch)" then "[NL]  worddb_NL" for each base file | Database / Database; description = the info line | `wp: tip database` | none | A stored name that is not found is added as an extra row "worddb_X.wfq (not found)" / "(niet gevonden)" and selected, with an informational warning icon (`_fontWarn`, never blocks Apply); Apply writes it back unchanged (D13). |
| W3 | `_lblWPInfo` | `Label`, always present, two lines reserved | "NL · 52,431 words · remembering words" / "NL · 52.431 woorden · onthoudt woorden"; "No database loaded"; an explicit file shows language and learning state only | not focusable; text is the chooser's description | - | - | Refreshed when the chooser, W1 or `WordDatabase.Loaded` changes (marshalled to the UI thread as before). |
| W4 | `_lblExportHint` | `Label` (hint font), always present, one line reserved | `wp: nothing to export` while Export is disabled, empty otherwise | not focusable | - | - | Gives the reason the button is disabled. |
| W5 | `_btnWPExport` | `MakeTouchButton`, `AddWideRow(fill: false)` | Export… / Exporteren… | Export / Exporteren | `wp: tip export` | none | `SaveFileDialog` stays (OS dialog); the error message becomes `TouchMessage.Info`; the file filter text is translated. |
| W6 | heading | `Heading` (right column) | Candidates / Kandidaten | - | - | - | |
| W7 | `_lstWPCandidates` in `_listFrame` | `TouchList` (44 px rows, 4 rows tall = 178 px with the 1 px frame, the Group Editor's frame and focus colours) | rows "word (count)" | Candidates / Kandidaten | `wp: tip candidates` | none | Enabled only while W1 is on. Empty text inside the list: `wp: no candidates`, or `wp: learning off hint` while W1 is off (needs `TouchList.EmptyText`, build step S2). Selection is kept by word after a refresh. |
| W8 | `_btnWPPromote`, `_btnWPReject` | two `MakeTouchButton` in one `InlineRow`, wide row of the right column | Promote / Toevoegen, Reject / Verwijderen | Promote / Toevoegen, Reject / Verwijderen | `wp: tip promote`, `wp: tip reject` | none | Enabled when W1 is on and a row is selected. Immediate (D5). |
| W9 | `_lblImmediateHint` | `Label` (hint font), always present, two lines reserved | `wp: immediate` | not focusable | - | - | |

Candidates come from `WordDatabase.GetCandidates()` (the loaded database), exactly as before; the chooser does not change that list (the old mismatch B10 stays, but the hint W9 and the info line make it less surprising; a real fix would need the database to be loaded per choice and is out of scope).

## 7. Footer, tab order, accelerators, keyboard

Footer: `MakeFooter(null, _btnCancel, _btnApply)`: Cancel (neutral) and Apply (`Variant.Success`), right aligned, 44 px, minimum width 120. `AcceptButton = _btnApply`, `CancelButton = _btnCancel`. Enter applies, Esc cancels, as before. No preview card.

### 7.1 Tab order

Tab stops, in order: the section bar (one roving stop; Left / Right / Home / End move between sections; Ctrl+Tab, Ctrl+Shift+Tab, Ctrl+PageDown / PageUp switch from anywhere), then the controls of the visible section, then Cancel, then Apply. `AddRow` / `AddWideRow` hand out `TabIndex` in build order, so the build order is the table below; inline rows set their own inner indexes (0, 1, 2). Labels that carry an accelerator precede their input in `TabIndex` order (the mnemonic jumps to the next focusable control).

| Section | Order |
|---|---|
| General | G1 language, G3 toolbar theme, G4 transparency (value box; the two stepper buttons are not stops), G5 background chip, G6 always on top, G7 hide title bar, G9 Save, Save As, Load |
| Accessibility | A1, A2, the timing group (one stop: the selected radio; arrow keys change the selection), the value box of the selected option's stepper (the other stepper is disabled, so it is skipped), A3d animation check box (skipped while disabled), A4 |
| Word prediction | W1, W2, W5, W7 list, W8 Promote, W8 Reject |

`CheckTabIndexUnique` (per parent, no two tab stops share an index) must pass; the footer sits in its own table.

### 7.2 Accelerators

Rule: letters must be unique within the whole dialog in English and in Dutch (`T_Accelerators` walks every control of the form, so hidden sections count). Assignment (existing letters kept; only A4 changed its string):

| Control | EN text | EN | NL text | NL |
|---|---|---|---|---|
| Toolbar theme (G3) | T&oolbar theme | O | &Werkbalkthema | W |
| Transparency (G4) | &Transparency (prefixed) | T | &Transparantie (prefixed) | T |
| Always on top (G6) | &Always on top | A | Altijd &bovenaan | B |
| Hide title bar (G7) | H&ide title bar | I | Titelbalk &verbergen | V |
| Save (G9) | &Save (prefixed) | S | &Opslaan (prefixed) | O |
| Save As (G9) | Sa&ve As… | V | Op&slaan als… | S |
| Load (G9) | &Load… (prefixed) | L | &Laden… (prefixed) | L |
| Sticky (A1) | Stic&ky modifiers | K | Plaktoetsen (Sticky &Keys) | K |
| Hold (A2) | &Hold to enter edit mode | H | &Ingedrukt houden voor bewerkingsmodus | I |
| Slow keys (A3) | Slow k&eys | E | La&ngzame toetsen | N |
| Dwell (A4) | &Dwell click | D | Automatis&che klik | C |
| Timing animation (A3d) | Sho&w timing animation | W | Voo&rtgangsanimatie tonen | R |
| Off (A3a) | Off (no accelerator) | none | Uit (no accelerator) | none |
| Corner labels (A4) | Show Shi&ft and AltGr labels | F | Shi&ft- en AltGr-labels tonen | F |
| Remember (W1) | &Remember typed words (prefixed) | R | &Getypte woorden onthouden (prefixed) | G |
| Apply | A&pply | P | Toe&passen | P |
| Cancel | &Cancel | C | Ann&uleren | U |

English letters used: O T A I S V L K H E D W F R P C (16, all different). Dutch letters used: W T B V O S L K I N C R F G P U (16, all different). No accelerator on: language chooser, background chip, database chooser, Export, candidates list, Promote, Reject, section buttons (a letter that is free in one language is taken in the other: D is Dwell in English, E is Slow keys; in Dutch C is Dwell and K is Sticky; Dutch D and E are free but the English ones are not, and one control should not have an accelerator in one language only). These controls are reached with Tab; sections with Ctrl+Tab. The guard keeps `min` at 10.

`TouchCheckBox` currently draws its text with `TextFormatFlags.NoPrefix` (paint and `GetPreferredSize`), so a check box text with `&` would show a literal "&" and be measured too wide, while the Alt+letter would still work (the base `CheckBox` keeps `UseMnemonic`). Build step S1 makes it draw and measure the mnemonic (underline when the keyboard cues are on, like `FluentButton`), with a test. `UiGuard.ClippedText` already measures `StripMnemonic(Text)`, so it cannot catch this.

## 8. Validation and switching to the right section

There is no blocking validation left in the dialog: the colour is chosen from a flyout (never an invalid code), the steppers reject out-of-range typing, the chooser lists real values, and a missing database name is a warning, not an error. The wiring stays so a future field can use it: `Apply()` starts with `if (ShowFirstSectionWithError()) return false;`. That puts a warning marker on every section whose subtree has an `_err` message (`HasPendingErrors(section)`), selects the first of them, and the section button reads "General ⚠" with the accessible name "General, contains an error". The marker clears on the next Apply when the message is gone. Test: set an `_err` message on a control in each section and call `Apply` (see 12).

The database warning uses `_fontWarn` (the informational `ErrorProvider` the base class already keeps apart from `_err`); optional cleanup: rename it `_warn`.

## 9. Data flow: Apply, Cancel, Save, Load, language

**Apply.** `ResultTheme = _sourceTheme.Clone()`, then overwrite `Opacity` and `BackgroundColor`; `ResultWindow = _sourceWindow.Clone()`, then `AlwaysOnTop`, `HideTitlebar`; `ResultMeta = _sourceMeta.Clone()`, then `ToolbarTheme`, `StickyModifiers`, `HoldToEdit`, `SlowKeysMs`, `DwellMs`, `ShowTimingAnimation`, `ShowCornerLabels`, `WordDatabase` (file name, "" for auto, the stored name when it was not found), `WordLearningEnabled`. A field the dialog does not edit (language, last file, gear position, font, key colours, border, window size, and any field added to the models later) is never touched (fixes H9). `ResultGroups = _groups`. `DialogResult = OK`, close. `HasPendingErrors` is checked first (section 8). Apply does not write `WordDatabase.LearningEnabled`; `KeyboardForm.LoadWordDatabase` already sets it from `_meta.WordLearningEnabled` after OK (verified in `KeyboardForm.cs` line 2476).

**Cancel.** `DialogResult.Cancel`, close. A `FormClosed` handler restores the UI language if `Lang.CurrentCode != _languageOnOpen` (`Lang.Load(_languageOnOpen)`; `KeyboardForm`'s `LanguageChanged` handler then resets `_meta.Language` by itself). Nothing else needs undoing: no other control has a live effect except Promote / Reject (immediate by decision D5, said on screen) and Load (confirmed first, D1).

**Save / Save As (D1).** The click validates and applies like Apply, but also sets `FileAction = FileAction.Save` or `SaveAs` (a new public `enum FileAction { None, Save, SaveAs }` and property on the form) and closes with OK. `KeyboardForm.OpenKeyboardEditor` copies the results into `_theme / _window / _meta` first, then runs the same code as today (`SaveSettings(false / true)`) when `FileAction` is set. This fixes B1 and B2: the file contains what was edited, and the dialog closing is the visible, tooltip-explained consequence. The constructor parameters `onSave` and `onSaveAs` go away.

**Load (D1).** The constructor keeps `Func<bool> onLoad` (now returns true when a file was loaded), `getSettings`, `getGroups`. Click: `TouchMessage.Confirm` ("kbd: Load title" / "kbd: Load msg", default No) → `onLoad()` → only if it returned true: `AdoptSource(getSettings(), getGroups())` and refill every control, re-sync the language chooser, set `_languageOnOpen` to the new `Lang.CurrentCode`. If the file dialog was cancelled nothing changes (fixes B3). The load itself is not undone by Cancel (the confirmation says so).

**Language.** The chooser calls `Lang.Load(code)` at once (D3); the dialog re-translates itself through `OnLanguageChanged`, including the chooser items.

**Word database events.** Subscribe to `WordDatabase.Loaded` after construction, unsubscribe in `FormClosed` and in `Dispose(bool)` (B15).

## 10. Size budget (1366 x 768, 728 px usable; estimates, nothing measured)

All numbers are design pixels at 100 %. A row is 44 + 4 + 4 = 52. `NewTable` padding is 16 on all sides.

| Part | Height |
|---|---|
| Frame: root padding 2 x 16, section bar about 58, footer about 52 | about 142 |
| General: language 52, heading 41, toolbar 52, transparency 52, background 52, 2 check rows 104, heading 41, buttons 52, padding 32 | about 478 |
| Accessibility: 2 check rows 104, timing group (frame padding 24 + caption 28 + 3 radio rows 156 + separator 12 + animation row 52 = about 272), corner row 52, row gaps 3 x 8, padding 32 | about 488 (the hint label is gone but the group is taller; General is no longer the tallest by a clear margin, so the window height is now set by Accessibility: see the fallbacks below) |
| Word prediction, left: 52 + 52 + 44 + 22 + 52 = 222; right: 33 + 178 + 8 + 52 + 40 = 311; section = tallest column + 32 | about 343 |
| Largest section (Accessibility, about 488) + frame + non-client (about 47) | about 677 of 728 |

The window is as tall as Accessibility (General is about 478, so within 10 px of it), about 51 px under the limit at +0 %. If Accessibility needs more at +80 %, extra fallbacks: put the animation check box into the Off row's empty right-hand cells and drop the separator (saves about 60 px). At +80 % text, labels and the Dutch hints wrap, which may add 30 to 60 px. If `CheckDialogGuards` fails on height, the fallback is, in this order: put "Always on top" and "Hide title bar" in one `InlineRow` (saves 52 px), drop the two headings' extra top margin (saves 16 px), move the Layout file buttons next to the language row. A single-column Word prediction section would be about 504 + frame = about 693: it fits only at +0 %, which is why it is two columns.

## 11. Decisions (every one: decided by Claude, to be reviewed)

| ID | Question | Decision | Reason (one line) |
|---|---|---|---|
| D1 | Where do Save / Save As / Load belong, and what do they do? | In General under the heading "Layout file". Save and Save As apply the edits and set `FileAction`; the caller saves after copying the results. Load stays in the dialog, asks first, and only refills when a file was loaded. Constructor loses `onSave` / `onSaveAs`, gets `Func<bool> onLoad`. | Fixes B1 to B3 (stale save, silent edit loss) without removing a feature; the toolbar also has Save / Load, so removing the buttons is the fallback if the owner prefers fewer controls. **Owner, 2026-10-04: confirmed, and the buttons stay in the toolbar too (both places).** |
| D2 | Do the Window settings sit in General? | Yes, under a "Window" heading, next to Language. | The brief fixes three sections, and none of the window settings is an accessibility or word-prediction setting. |
| D3 | Language chooser: live or on Apply? | Live (the dialog translates itself at once) and restored on Cancel (`_languageOnOpen`). | Live shows the result; restore removes hidden state B4. **Owner, 2026-10-04: confirmed.** |
| D4 | "Remember typed words": live engine switch or on Apply? | On Apply only. `WordDatabase.LearningEnabled` is not touched by the dialog. | Nobody can type while the modal dialog is open, and the live switch stayed flipped after Cancel (B5). **Owner, 2026-10-04: confirmed.** |
| D5 | Promote / Reject: immediate or staged until Apply? | Immediate, with a visible hint (W9). | They change the learned data itself, tests of `WordDatabase` stay valid, and a staged list adds UI for little gain; staged is the alternative if Cancel must be perfect. |
| D6 | Opacity `TrackBar`? | `TouchStepper` 0..80 step 5 with a "%" label. | A slider thumb is not a 44 px target and cannot be hit with tremor; steppers are the pattern of the other editors. **Owner, 2026-10-04: confirmed.** |
| D7 | Slow keys and Dwell click: two check + stepper rows, or a chooser, or radio buttons? | **Radio buttons Off / Slow keys / Dwell click in a framed "Timing aid" group** (frame, caption, one width, steppers in a second column, the selected row has a 2 px accent border; section 5 and the mock-up). The animation check box sits in the group and is enabled while Slow keys or Dwell click is selected. | **Changed by the owner, 2026-10-04** (was: two rows with a hint): radio buttons say "exactly one of these" without a sentence. Needs the new `TouchRadioButton` and `TouchGroup` components. |
| D8 | How do the database chooser and the candidates list fit 1366 x 768? | Two columns: settings left, candidates right; list 4 rows tall. | Keeps the section about 343 px instead of about 504 and leaves General the tallest. **Owner, 2026-10-04: confirmed.** |
| D9 | Does Apply stay one button? | Yes: Cancel and Apply; Save / Save As are Apply plus a file action. No second "OK", no "Apply and keep open". | The Key and Group editors have one Apply; a second confirm would need a state to explain. |
| D10 | Background colour control? | `ColorChip` captioned "Background", no hex box. | Same component as the other editors; the flyout has the hex box; removes the `_err` hex path and `_suppressOnChanged`. |
| D11 | Width? | `ContentMaxWidth = 920`. | The widest row is the Dutch "Hold to enter edit mode" check box plus margins, and the two columns need about 900; the Group Editor's 980 is for its three chips. |
| D12 | Accelerators? | Section 7.2: 16 letters in each language, none on choosers and the word-prediction controls, an `&` added to "Show Shift and AltGr labels". | Letters are unique in both languages with only one existing string edited; the precedent is Priority 3. **Owner, 2026-10-04: confirmed.** |
| D13 | A stored database name that is not found? | Keep it as an extra selected row marked "(not found)" with a warning. | Same rule as a missing font: never rewrite stored data silently (B8). |
| D14 | Untouched values? | Opacity is kept unless the stepper changed; the Slow / Dwell stepper ranges widen to include a loaded value; Apply clones the source. | "An untouched value is stored byte for byte", as in the Key Editor; removes hidden clamping. |
| D15 | Export errors and file dialogs? | `TouchMessage.Info` for errors; `SaveFileDialog` stays; translated filter text. | The system `MessageBox` is small (flaw 1b of formdesign.md); OS file dialogs are out of scope. |
| D16 | Initial focus? | The language chooser (as before). | Unchanged behaviour. |
| D17 | Position of the new corner-label option? | Last row of Accessibility, after the timing group. | The old card had it last and the todo (9.7) names it last. |
| D18 | Remember the last section between openings? | No: always opens on General (or on the first section with an error). | A remembered section is hidden state; the dialog is opened rarely. |
| D19 | Preview card in the footer? | None. | Nothing here has a small visual result that a card would show. |
| D20 | Share `InlineRow` / `Heading` and make `TouchCheckBox` honour `&`? | Yes: move the first two into `FluentDialogBase`; extend `TouchCheckBox` (build steps S1, S2). | Otherwise the two editors duplicate code and the accelerators on check boxes print a literal "&". |
| D21 | Toolbar theme chooser vs the open dialog's own theme? | Unchanged: the dialog keeps its theme until it is reopened. | Re-theming a live dialog is not needed and not in the brief. |
| D22 | Hint text colour? | Plain text colour (what `ApplyTheme` produces: it recolours every `Label`), at the hint font size. | Matches what the app really shows today; a grey override (`TextHint` / `DialogDarkTextDim`, both at least 7 : 1) can be added in `ApplyTheme` like the Group Editor does for its error label. The mock-ups show the plain colour. |
| D23 | Alignment of neighbouring controls? | **Owner rule, 2026-10-04:** controls under each other share one width (the widest) so their right edges line up; buttons side by side share one width; choosers under each other share one width; steppers sit in their own column. Helpers `OptionStack` and `ButtonRow`; guard `UiGuard.StackedEdges`. Applied in all three sections (mock-ups updated). | Fixes the untidy look of the old dialog (Background / Always on top / Hide title bar; Sticky modifiers / Hold to enter edit mode; Slow keys / Dwell click). Should become a rule for every dialog (add to formdesign.md). |
| D24 | New components for the radio group? | `TouchRadioButton` (button-shaped row, round mark, selected border, `&` mnemonic) and `TouchGroup` (frame + caption + fill, role group), both in `TouchControls.cs`. | The existing `RadioButton` is a small native control (flaw 1 of formdesign.md) and a plain `GroupBox` has no caption control we can style. |

## 12. Tests

### 12.1 New: `KeyboardEditorTests.cs` (follow `GroupEditorTests.cs`; register next to `T_GroupEditor` in `OnScreenKeyboardTests.cs`)

`T_KeyboardEditorGuards`: `CheckDialogGuards(...)` (en, nl, +40 %, +80 %, light and dark, every section: 44 px targets, no clipped text, nothing sticks out, fits 1366 x 768) for three states: (a) everything on and filled (slow keys 300, sticky, hold, corner labels off, learning on, an explicit database, candidates injected through a seam); (b) defaults; (c) a stored database name that is not found, plus a long candidate word. Plus: the candidate list rows are at least 44 px (`ItemHeight`), every pointer control has an `AccessibleName`, every check box row is at least 44 px tall.

`T_KeyboardEditor` (use `Priv<T>(form, "_name")` as in the Group Editor tests; keep the private field names `_chkCornerLabels`, `_chkStickyMods`, `_chkHoldToEdit`, `_optTimingOff`, `_optSlowKeys`, `_optDwell`, `_chkTimingAnimation`, `_chkAlwaysOnTop`, `_chkHideTitlebar`, `_chkWPLearning`, `_cmbLanguage`, `_cmbToolbarTheme`, `_cmbWPDatabase`, `_lstWPCandidates`, `_btnWPExport`, `_btnWPPromote`, `_btnWPReject`, `_btnSaveFile`, `_btnSaveAsFile`, `_btnLoadFile`, `_btnApply`, `_btnCancel`; new: `_stpOpacity`, `_stpSlowKeys`, `_stpDwell`, `_chipBackground`; keep `private bool Apply()`):
1. Round trip untouched: a deliberately odd meta / theme / window (opacity 0.755, SlowKeysMs 50 and DwellMs 7000 in separate cases, database "missing.wfq", Language "nl", gear row/col, LastFile, non-default font and key colours, window size 800 x 300) comes back from Apply identical in every field (compare all properties by reflection so a future field is covered).
2. Each control writes its field (every check box both ways, the three steppers, the toolbar chooser for all three values, the chip, the database chooser including "(auto)" = "").
3. Timing aid (radio group): exactly one of Off / Slow keys / Dwell click is selected (selecting one clears the other; arrow keys move the selection and the focus; the group has one tab stop); the steppers are enabled only for the selected option and keep their value (shown, greyed) when it is not; Off stores 0 and 0; the animation check box follows `selected != Off` and keeps its stored value while disabled; a loaded file with both values above 0 selects Slow keys. The mock-up's behaviour is the reference.
4. Corner labels (adapted from `T_CornerLabels`): shows the setting, Apply writes it back, plus the `&` in the English and the Dutch string and `StripMnemonic` accessible name.
5. Cancel: choose Dutch, cancel, `Lang.CurrentCode` is back to the language at opening; Apply keeps Dutch. `WordDatabase.LearningEnabled` is unchanged after toggling W1, after Cancel and after Apply (save and restore the static in the test).
6. Language change while open: toolbar chooser items, "(auto)" row, hints and section titles are in Dutch afterwards.
7. Database chooser: "(auto)" stores ""; a file stores its name; a missing name is added as a row, shows the warning, is stored unchanged, and does not block Apply (`HasPendingErrors` stays false).
8. Candidates through the seams (a candidate source, and promote / reject delegates, so the static `WordDatabase` is not needed): list text "word (count)", selection kept by word after a refresh, Promote / Reject disabled without selection and while learning is off, the empty text switches between `wp: no candidates` and `wp: learning off hint`, Promote and Reject call their delegate with the plain word and refresh.
9. Export button enabled only when the overlay file of the selected database exists (a temp file; `DatabaseInfo` is built in the test through a database-source seam); the hint W4 is shown exactly when it is disabled.
10. Load: with `onLoad` returning false nothing changes and the user's edits stay; with true every control shows the new values, the language chooser re-syncs and `_languageOnOpen` is updated; the confirmation is asked first (the test calls the internal method that skips the dialog, as `RemoveSelectedGroup` does).
11. Save / Save As: `FileAction` is set, the result is OK, and `ResultMeta` already holds the edit made just before (regression test for B1).
12. Sections: an `_err` message on a control in each section makes Apply refuse, selects that section, marks it (`Sections.HasError(i)`), and the marker clears when the message is removed.
13. Tab order: per section the expected sequence of tab-stop control names (section 7.1), unique `TabIndex` per parent, Ctrl+Tab cycles sections (through `ProcessCmdKey`).
14. Window title and section titles in English and Dutch.
15. `WordDatabase.Loaded` handler is removed after `Dispose` (raise the event after disposing; nothing throws, the info label is unchanged).
16. Alignment (alignment rule, section 1): a new guard `UiGuard.StackedEdges(root)` walks every `OptionStack`, `ButtonRow` and the timing grid and fails when two members that stand under each other (or side by side, for a button row) differ in width or in right edge; it runs inside `CheckDialogGuards` for en, nl, +40 %, +80 %, both themes, in every section, plus a unit test of the guard itself on a deliberately uneven pair of check boxes (it must report it).

Component tests (build steps S1, S2): `TouchRadioButton` (44 px, round mark and selected border pixels in both themes, one selected per group, arrow keys move selection and focus, a disabled look, `&` mnemonic) and `TouchGroup` (caption is the accessible name, role group); `OptionStack` gives equal widths and `ButtonRow` equal button widths; `TouchCheckBox` with `&`: preferred width equals the width of the stripped text plus padding, no "&" is drawn (pixel check of the glyph area as in `T_DisabledLook`), `ProcessMnemonic` toggles it; `TouchList` disabled look and `EmptyText` (pixel test on both themes).

### 12.2 Existing tests to adapt

| Test | Change |
|---|---|
| `T_CornerLabels` (`CornerLabelTests.cs:60-66`) | Field `_chkCornerLabels` and method `Apply` keep their names, so it should work as is (the check box is a `TouchCheckBox`, a `CheckBox`); add the English / Dutch `&` assertion. |
| `T_ValidationBlocksApply` (`OnScreenKeyboardTests.cs:4578-4600`) | The hex box is gone: replace by the Key Editor's form (`ColorChip` count, no pending error on a fresh dialog) and by the section test in 12.1 (12). |
| `OnScreenKeyboardTests.cs:1690-1701` | Replace the direct-child scroll panel check by `UiGuard.All(f).Any(c => c is Panel p && p.AutoScroll)` (as for the Group Editor). |
| `T_AccessibilityControls` (`OnScreenKeyboardTests.cs:4407-4427`) | Count named `TouchStepper`s (3) instead of `NumericUpDown`s; add `CheckNudHasDescription`. |
| `T_Accelerators` (`OnScreenKeyboardTests.cs:4865-4905`) | Unchanged; the new dialog has 16 letters per language (min 10 passes). Todo 2.4.7.2 ("unique per dialog") is already what it does. |
| `T_UiGuardBaseline` (`UiGuardTests.cs:1011-1036`) | Delete; the dialog moves to the strict list. |
| `DevGallery.cs:192` | Add `SaveKeyboardEditor(outDir, tag)`: three pictures (one per section), en / nl / +40 %. |
| Structural translation tests | They pick up the new keys automatically (tip registered, placeholders match, at most one `&`). |

## 13. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R1 | The Save-after-copy change touches `KeyboardForm.OpenKeyboardEditor`, which no test constructs. | Test the dialog side (12.1 (11)); try Save / Save As / Load by hand and write it in `formdesign.md` section 7 as "not verified". |
| R2 | The reading of B1 (save writes old values) was not confirmed by running. | Check by hand first (change opacity, press Save, open the file) before relying on it; the redesign is safe either way. |
| R3 | `TouchCheckBox` mnemonic drawing is shared by the other dialogs. | Only the draw/measure flags change; Group and Key Editor guards run in the same step. |
| R4 | Height at +80 %: General is only about 61 px under the limit. | Fallbacks in section 10; the guard fails loudly. |
| R5 | `TouchList` disabled and empty states do not exist yet. | Step S2 with pixel tests before the section uses it. |
| R6 | The static `WordDatabase` makes candidate and export tests fragile. | Seams (candidate source, promote / reject delegates, database source) default to the static class; tests inject. |
| R7 | `ColorChip` builds its accessible name with the English word "color": a Dutch screen reader says "Achtergrond color". Same in the Key and Group Editors. | Optional fix: a `Lang` key for the word; out of scope, noted. |
| R8 | Language restore on Cancel relies on `KeyboardForm`'s `LanguageChanged` handler resetting `_meta.Language`. | Verified by reading (`KeyboardForm.cs:354`); the test covers the dialog side only. |
| R9 | The two-column section uses nested tables: formdesign lesson on inflated nested heights. | Use `InlineRow`, anchor (not dock) the inner tables, no wrapping flow; print `GetRowHeights()` through a `DiagnoseLayout` like the Group Editor. |
| R10 | Export may miss the last 30 s of learned words (the overlay is written in batches). | Unverified; consider `WordDatabase.SaveNow()` before copying (needs a look at its threading). |
| R11 | Hint labels are recoloured to the plain text colour by `ApplyTheme`; grey hints need an override. | D22. |
| R12 | Enter on a focused check box triggers the `AcceptButton` (WinForms default), as in the old form. | Unchanged; noted so nobody is surprised. |

## 14. Build plan

Each step ends with `dotnet build -nologo -v q` clean and the full test run exit code 0 (see `CLAUDE.md`), then a commit. The old `KeyboardEditorForm` stays untouched until step S9, so every step is green.

| Step | Work | Ends with |
|---|---|---|
| S0 | Hand-check B1 to B3 and the Cancel behaviour in the running app (record in `formdesign.md`). | notes only |
| S1 | `TouchCheckBox`: draw and measure `&` (remove `NoPrefix`, honour keyboard cues); new `TouchRadioButton` and `TouchGroup`; component tests. | Group / Key Editor guards still pass |
| S2 | `TouchList`: disabled look and `EmptyText`; tests (both themes). Move `InlineRow` and `Heading` into `FluentDialogBase`; add `OptionStack`, `ButtonRow` and the `UiGuard.StackedEdges` guard; `GroupEditorForm` uses the shared helpers. | Group Editor tests pass |
| S3 | Strings: section 3.2 and 3.3 into `LanguageManager.cs` and `lang_nl.xml`; structural translation tests pass. | build + tests |
| S4 | New file `KeyboardEditorForm2.cs` (temporary name): frame, footer, section bar, empty sections, `Apply` skeleton with clone-and-overwrite, `AdoptSource`, seams; `KeyboardEditorTests.cs` with the construction, Apply round-trip and section tests. | green |
| S5 | Accessibility section (A1 to A4, the radio group) + its tests (radio behaviour, animation, corner labels, accelerators, alignment). | green |
| S6 | General section (G1 to G9) + tests (stepper, chip, chooser, language restore, Save / Save As `FileAction`, Load). | green |
| S7 | Word prediction section (W1 to W9) + tests (candidates, export, database warning, `Loaded` handler). | green |
| S8 | `T_KeyboardEditorGuards` (all states, +40 % / +80 %, both themes), tab-order test, gallery pictures; look at the gallery in English and Dutch (the HTML mock-ups this once referred to are gone). Adjust (section 10 fallbacks). | green + pictures reviewed |
| S9 | Switch: `KeyboardForm.OpenKeyboardEditor` uses the new constructor and `FileAction`; rename `KeyboardEditorForm2` to `KeyboardEditorForm`, delete the old file; adapt the old tests (12.2); delete `T_UiGuardBaseline`. | green |
| S10 | Docs: `todo.md` 2.4.5.2 done and the 9.4 / 9.7 notes, `formdesign.md` (flaws B1 to B15 as a new subsection, lessons, "not verified" list), remove `ROW_H` / `HDR_H` / `WrapInScrollPanel` / old `AddColorRow` if nothing else uses them (todo 2.4.7). | green |

## 15. What could not be resolved from reading

- Whether B1 (Save writes the old values) really happens: it follows from the call order in `KeyboardEditorForm` (Apply, then `_onSave`) and `KeyboardForm.SaveSettings` (reads `_theme / _window / _meta`, which are copied only after `ShowDialog` returns), but the app was not run.
- The task brief says the word-database dropdown "reverts on cancel". In this code it does not: the file comment says "no revert logic", and the choice only takes effect when `KeyboardForm.LoadWordDatabase` runs after OK. An older version (todo "Step 5") had a revert; it was removed with the overlay redesign.
- Whether `Lang.GetAvailable()` returns more than English and Dutch on a real install (it reads `lang_*.xml`); the chooser works for any number.
- Real heights: all numbers in section 10 are estimates from the row model; only the gallery and `CheckDialogGuards` can confirm them.
- Whether `FluentButton`'s keyboard cues (underline only while Alt is held or "always show keyboard shortcuts" is on) are the right look for `TouchCheckBox` too; the mock-ups always underline the accelerator so it can be reviewed.
- `WordDatabase.SaveIfDirty` threading before an export (R10).

# Scanning — research and design options (Priority 1)

Written 2026-10-11. Purpose: get a good view of what scanning is, how other software offers it and which settings exist, before
deciding what EditableOSK needs. The owner wants only the basic settings in this keyboard; this document therefore ends in a
prioritised list of possibilities (section 8) and a proposal for a first, small version (section 9).

How to read it: statements marked **[source]** come from the pages listed in section 10. Statements marked **[mine]** are my own
knowledge or reasoning and were not checked against a source in this session. Section 10 also lists what could **not** be verified.

---

## 1. What scanning is

Scanning is a way to type (or choose) with very few movements: the software **highlights** options one after another, and the user
**selects** the highlighted option with a switch (a button, a key, a blink, a head movement …). It is used by people who cannot
point at keys directly, for instance with severe motor impairments, and it is slower than direct selection **[source: Wikipedia]**.

A scanning system is made of four independent choices. Most settings of every product below belong to one of them:

| Choice | Question | Examples |
|---|---|---|
| **Pattern** | In what order and in what groups are the options highlighted? | linear, row-column, blocks, groups |
| **Control** | How does the switch drive the highlight? | automatic, step, hold (inverse) |
| **Selection** | When does a switch action count as a choice? | on press, on release, after a time |
| **Feedback** | How does the user see or hear where the scan is? | border, background, magnify, sound, speech |

---

## 2. Scan patterns (what is highlighted, in what order)

| Pattern | How it works | Good for | Weak point |
|---|---|---|---|
| **Linear (item) scanning** | One item after another, usually left to right, row by row. | Easy to learn; few items. | Slow for a large keyboard: to reach the 7th item of the 4th row of 8 items per row, 30 other items pass first **[source: Wikipedia]**. |
| **Linear, column-wise; zigzag** | Same, by column; or alternate direction per row (zigzag) so the highlight does not jump back to the left **[source: Mind Express]**. | Variants of linear. | Same. |
| **Circular scanning** | Items in a circle, the indicator goes round. | Simplest to understand cognitively **[source: Wikipedia]**. | Visually demanding; does not fit a grid keyboard. |
| **Row-column (row, then item)** | Rows are highlighted; a selected row is scanned item by item. | The standard for keyboards and word lists; much faster than linear. | Needs a regular grid; two selections per key. Android uses it for keyboards when linear is chosen **[source: Google]**. |
| **Column-row** | The same with columns first **[source: Mind Express, Grid 3]**. | Layouts that are taller than wide. | As above. |
| **Group-item scanning** | Items are put in groups (rows, columns or any category); groups are scanned first, then items of the chosen group. Row-column is the best known form **[source: Wikipedia]**. Mind Express lets the author make up to 10 own **scan groups**; cells that are not in a group are then not scanned at all **[source: Mind Express]**. | Keyboards with meaningful groups (vowels, consonants, numbers, controls, word suggestions). | Groups have to be defined. |
| **Block scanning (block, row/column, item)** | The page is cut into blocks (Mind Express: "prescan in blokken", a number of blocks vertically and horizontally); blocks first, then rows or columns inside, then items **[source: Mind Express, Grid 3]**. | Large grids (more than ~40 keys): three short levels instead of two long ones. | Three selections per key. |
| **Binary / halving scanning** | The set is halved again and again ("is it in the top half?"). Intel's ACAT first asks top or bottom half of the rows **[source: arXiv 1606.02552]**. A quadrant variant (four parts) is the same idea **[mine]**. | Very large sets, two-switch users who can answer yes/no. | Unpredictable for the user; hard to learn. |
| **Frequency-optimised (Huffman) scanning** | The grouping is computed so that frequent letters need few selections; the best layout minimised the expected number of queries per character (4.29 against 6.23 for alphabetical row-item) and was the fastest in timed single-switch use (3.28 s against 5.60 s), but in two-switch (binary) use the predictable row-item layouts were faster and more accurate because users can anticipate the next query **[source: arXiv 1606.02552]**. | Experts with one switch. | A layout nobody can predict; confusing for beginners. |
| **Point scanning** | Two lines (horizontal, then vertical) sweep the screen; the crossing is the target **[source: Google Switch Access, iOS Switch Control]**. | Reaching any point of an arbitrary screen. | Slow; only needed when there are no defined targets. Not relevant to a keyboard with keys. |
| **Directional (joystick) selection** | A joystick or arrow keys move the highlight in the direction pressed **[source: Mind Express]**. | Users who have 4–5 switches or a joystick. | Needs more than one or two switches. |

Two more things belong to the pattern:

- **What is skipped.** Grid 3 can skip empty cells, the writing area, or both; Mind Express can skip cells without an action
  **[source: Grid 3, Mind Express]**. On a keyboard with spacer cells this matters a lot.
- **What is scanned first.** On a keyboard with a row of word suggestions, the suggestions are the most useful targets (one
  selection can type a whole word). Scanning them first, or as their own group, is a design choice **[mine]**.

---

## 3. Scan control (how the switch drives the highlight)

| Control | How it works | Switches | Notes |
|---|---|---|---|
| **Automatic (timed) scanning** | The highlight moves on by itself every *step time*; the switch **selects**. | 1 | The classic single-switch method. Needs timing ability, but only one precise action. It is the default style in iOS Switch Control **[source: AbilityNet, Apple]**. |
| **Step scanning** | Every press of a *step switch* moves the highlight one item; a second switch selects. | 2 | Easier cognitively, no timing pressure, but tiring (Wikipedia mentions ALS users) **[source: Wikipedia, Mind Express, Grid 3]**. |
| **Inverse / directed / hold scanning** | The highlight moves **while the switch is held**; releasing it selects **[source: Wikipedia]**. Grid 3: "hold to advance"; Mind Express: "zolang je schakelaar 1 ingedrukt houdt" **[source]**. | 1 | For users who can hold a switch but cannot press at an exact moment. |
| **Tap to advance** (one switch) | Each tap offers the next item; a long wait or a second switch selects. Mind Express: "als je op schakelaar 1 drukt", with an *uitsteltijd na stappen* (delay after stepping) so that a second tap too soon is ignored **[source]**. | 1–2 | |
| **Role swapping (two switches)** | Switch 1 steps, switch 2 selects a row; then switch 2 steps and switch 1 selects the item, and so on **[source: Mind Express]**. | 2 | Meant for users who tire on one switch (head movement). |
| **Joystick / arrow keys** | See directional selection above. | 4+ | |

Related to control:

- **When a selection counts:** on press, on release, short press then release, "as long as the switch is held" (Mind Express lists
  five variants) **[source]**; Grid 3 "accept on press / accept on release"; Android "release to perform action"; iOS "hold duration".
- **Auto-select:** an item is selected automatically after a time (Mind Express "automatisch selecteren na auto-selectietijd";
  iOS "Auto Tap"; Android "Auto-select" taps the highlighted item without showing a menu) **[source]**.
- **Going back:** *reverse step / terugstappen* (after the first selection the scan runs backwards and the real selection happens
  at the next switch action), *back up one level*, *return to start with switch 2*, *reverse auto-scan* with a second switch
  **[source: Mind Express, Grid 3, Google]**.
- **Pausing:** pause after each selection; pause with switch 2; stop after a number of rounds **[source: Mind Express]**. Grid 3 has a
  *stop scan* switch and warns that "unlimited" rounds need one **[source]**.

---

## 4. Settings found in other products

A "·" means: not found in the sources I could read (it may exist). Values in brackets are the ones found.

| Setting | Windows OSK | iOS Switch Control | Android Switch Access | Mind Express 5 | Grid 3 | PRC-Saltillo |
|---|---|---|---|---|---|---|
| **Scan / step time** | "Scanning speed" slider (scale not documented) | Auto Scanning Time | Auto-scan time (seconds) | Staptijd (slider, +/−) | Advance time | Scan speed 0.2–10 s, **default 1 s** |
| **Pause / delay on the first item** | · | Pause on First Item (with length) | Delay on first item (added to the scan time) | Automatisch eerste stap (ja/nee) | Pause on first item | · |
| **Number of rounds / loops** | · | Loops (default 2, range 1–10) | Number of scans | Terugkeren na aantal rondes; Scanning pauzeren na aantal rondes | Maximum number of scans (1–5, unlimited) | · |
| **Scan pattern** | scans keys (pattern not documented) | Group Items on/off; item or point scanning | Linear (default), Row-column, Group selection, Point scan | Lineair, Lineair (kolom), Zigzag, Zigzag (kolom), Rij-kolom, Kolom-rij, scan groups, prescan blocks | Cells one at a time; rows/columns then cells; blocks then cells or row/column | row-column and column-row (seen in page titles only) |
| **Control** | scanning is continuous; a switch, key, mouse click or game controller selects | Auto scanning (default); other styles with more switches | Auto-scan on/off; step with several switches | Automatisch; als je op schakelaar 1 drukt; zolang je schakelaar 1 ingedrukt houdt | Automatically advance; tap to advance; hold to advance | · |
| **Hold time before a press counts** | · | Hold Duration | · | · | Ignore presses shorter than a set time | · |
| **Ignore accidental repeats (debounce)** | · | Ignore Repeat | Ignore repeated presses (seconds) | Uitsteltijd na stappen | Ignore repeat presses | · |
| **Select on press or release** | · | · | Release to perform action | yes (five variants) | Accept on press / on release | · |
| **Reverse / back** | · | · (Move Repeat repeats a move while a switch is held) | Reverse auto-scan switch; Previous | Terugstappen + terugstaptijd; terug naar begin via schakelaar 2 | Reverse scan direction; Back up one level | · |
| **Skip empty or inactive cells** | · | · | · | Vakjes zonder actie overslaan | Skip empty cells / writing area | · |
| **Highlight style** | · | Large Cursor, five cursor colours | Highlight colour and thickness; one per group | Rand, Rand (breed), Rand (extra breed), Achtergrond inkleuren, Stip (onder/midden/boven), Vakje vergroten, colour | Progress indicator, border, background, magnify | · |
| **Sound** | · | Sound Effects (click per move) | Spoken feedback, vibration | Geluid bij stappen / bij selecteren | No sound / speak description / play sound | · |
| **Speech (auditory scanning)** | · | Speech (voice, rate) | Spoken feedback | Auditieve feedback (scanwoord or the label spoken) | Speak description | · |
| **Auto start** | · | · | Automatically start scanning | · | · | · |
| **Stop / pause scanning** | · | Loops then the cursor hides | Number of scans | Scanning pauzeren | Stop scan switch | · |
| **Saved actions** | · | Recipes | Menu customization | · | Commands on switches | · |
| **Switch tester** | · | · | · | · | Switch Tester (shows presses and timings) | · |

Sources: section 10. Apple and Google do not publish numeric defaults for the scan time **[source]**; 1 second is the Saltillo
default and a common starting point **[source: Saltillo via search result]**.

---

## 5. What the research says about settings and efficiency

- **Settings must be individual.** Scan speed, delays, layout and error handling depend on the user's response time and error
  tendencies; the KPR group (Koester, Simpson) built a free web tool, **Scanning Wizard**, that measures a user's response time with
  tasks and recommends scan speed, delay and keyboard layout **[source: KPR]**. Reported gains in text entry rate: +120 % on
  average with their method (nine single-switch users, each at least +40 %); +71 % on average (range 29–172 %) with the tool in a
  field test with five users **[source: KPR; small samples]**. Their earlier paper "Selecting an Appropriate Scan Rate: The .65 Rule"
  exists, but I could not read what the rule says; I will not guess it here.
- **Errors come in two kinds:** accidental selections (false positives) and missed ones (false negatives); both frustrate and can
  prevent functional communication **[source: search results, probably PMC 3975284; I did not open the paper]**. This is why products have *ignore repeats*, *hold duration*,
  *accept on release*, *delay after a selection* and some way to go back.
- **A pause after a selection** (the "scanning delay") helps the user recover; one study used a 700 ms scan interval with a 200 ms
  delay after each selection and sound on each switch activation **[source: a study named in a search result; not identified]**. That was
  a research set-up, not a default.
- **Fewer queries per character is not everything.** In the Huffman study (10 neurotypical users new to scanning) the optimised
  layout was fastest with one timed switch, but predictability mattered more with two switches **[source: arXiv 1606.02552]**.
  For a keyboard whose users learn it over months, a **predictable** layout (rows, groups the user knows) is the safer default.
- **Prediction** can reduce the number of selections, but adds a row that changes after every letter, which a scanning user has
  to read again each time **[mine]**; products tend to put suggestions in a fixed place (a row or a group).

---

## 6. Switch hardware and how it reaches the computer (technical, **[mine]**)

Most switches for computers are **USB or Bluetooth interfaces that look like a keyboard key (often Space or Enter) or a mouse
click**; some are game controllers (joystick, gamepad). Windows' own On-Screen Keyboard accepts exactly these as the select
action: a joystick/gamepad, a keyboard key from a list, or a mouse click **[source: Microsoft]**. Camera-based switches (iOS
"Camera": head movement) are an operating-system feature, not something an on-screen keyboard has to do.

What this means for EditableOSK, which types into **other programs** and never takes the focus:

- The switch input has to be seen **while another program has the focus**: a global keyboard hook or raw input for a chosen key,
  a global mouse hook for a click, and the game controller API for a gamepad. The switch key must then **not** also reach the
  program being typed into (the hook has to swallow it while scanning is on).
- The keyboard already has a system-wide hook for the focus, and it already has timing aids (*slow keys*, *dwell*,
  `SlowKeysMs` / `DwellMs`); scanning is a third kind of aid and the three have to exclude or combine with each other clearly.
- **Sticky modifiers**, **word prediction slots** and **layers (Shift, AltGr)** all need a scan path: a scan has to be able to
  select Shift and then a key, and a suggestion.

---

## 7. What EditableOSK already has that scanning can use (**[mine]**, from the code)

- A **grid of cells** with rows and columns, spans, and empty spacer cells: row-column and column-row fall out naturally; empty
  cells are known (skip them).
- **Style groups with ids and names** (`vowels`, `consonants`, `numbers`, `controls`, `prediction`, …): ready-made **scan groups**
  (Mind Express needs the author to make them by hand).
- **Word prediction slots** (`wp:n` keys): a natural first group.
- **Edit mode** (the keyboard can be rearranged and would have to stop scanning there), **timing aids** (slow keys, dwell,
  highlight animation) that already draw a progress fill on a key, and a **touch-friendly dialog base** for a settings dialog.
- **Settings storage:** layout settings are in the `.kbl` (`LayoutMeta`); user settings in `%AppData%\EditableOSK\user.xml`
  (`UserSettings`). Scanning speed and the switch belong to the **person**, the pattern (which groups) to the **layout**.

---

## 8. Possibilities in my order of priority

Order: what gives a user the most for the least, and what the sources show that nearly every product has first. "Basic" is what
the owner asked for; the rest is listed so that nothing is lost.

**A. Basic (build first)**

1. **Automatic scanning with one switch**, pattern **row-column** (rows, then the keys of the chosen row). Why: the one method
   that every product offers, it needs one switch, and it is the standard for keyboards **[source]**.
2. **Scan speed** (step time), default **1 s**, range about 0.2–10 s. Why: every product has it; 1 s is the common starting point.
3. **Pause on the first item** (extra time on the first row/key of each round). Why: iOS, Android and Grid 3 all have it; without
   it a user misses the first item after each selection.
4. **Rounds before stopping** (1–5, default 3 or so) and **resume** with the switch. Why: avoids a scan that never stops; the
   same idea as *loops* / *number of scans*.
5. **The switch:** choose **a keyboard key** and/or **a mouse click** (and later a gamepad button); **select on press**; **ignore
   repeated presses** (a time in ms). Why: this is what Windows' own keyboard offers, plus the accidental-repeat protection every
   product has.
6. **Highlight:** border and/or background colour, size of the border, **magnify** the highlighted key (optional). Why: users
   with low vision or a tremor need a clear, large indicator **[source: Mind Express, Grid 3]**.
7. **Sound:** a short click on each step and on each selection (optional). Why: cheap, and helps users who look away.
8. **Skip keys that do nothing** (empty cells and spacers). Why: otherwise rows full of empty cells waste time.
9. **Start and stop scanning** (a switch action or a key; the keyboard in scan mode must be recognisable). Why: Windows' scan
   mode, Grid 3 *stop scan* and Mind Express *scanning pauzeren* all have it.

**B. Next (clear value, a little more work)**

10. **Step scanning with two switches** (one steps, one selects) and **tap to advance** with *delay after stepping*. Why: easier
    than timing for some users **[source]**.
11. **Linear scanning** (and **column-row**) as extra patterns. Why: simple; column-row suits tall layouts.
12. **Scan groups from the style groups**, **word suggestions as the first group**, and a **group-item pattern** (groups, then
    rows/keys in the group). Why: shorter paths than rows on a large keyboard, and the data is already there.
13. **Go back one level** (from key to row/group) and **reverse step**. Why: a wrong row is the most common error in row-column
    scanning; Grid 3 and Mind Express both offer it.
14. **Accept on release** and **hold duration** (a press shorter than a set time is ignored). Why: for users with tremor or
    spasms **[source: iOS, Grid 3, Google]**.
15. **Scanning per level**: another scan time for the rows than for the keys. **[mine]** Why: users often need more time at the
    key level; products with two-level scanning commonly allow it.
16. **A switch test** (shows the presses and their timing) and a **small calibration** in the style of Scanning Wizard that
    proposes a scan speed. Why: individual settings are what the research asks for **[source: KPR]**.

**C. Later or only on request**

17. **Inverse / hold scanning** (the highlight moves while the switch is held). For users who cannot press at a moment.
18. **Auditory scanning** (speak the row or key). Needs text to speech; more work; for users who cannot see the screen well.
19. **Block scanning** (prescan in blocks) for very large keyboards.
20. **Role swapping** of two switches; **auto-select** after a time; **auto start**.
21. **Gamepad / joystick** as switch or for directional selection.
22. **Highlight styles** beyond border and background (dot, progress bar), colours per group.
23. **Saved actions** (iOS "recipes"): one switch action that types a phrase or presses a shortcut.

**D. Not recommended for this keyboard**

- **Point scanning**: there are defined keys, so it adds nothing.
- **Camera / eye / head switches**: an operating-system job.
- **Huffman or other frequency-optimised groupings**: unpredictable for the user **[source: arXiv]**; if ever, then as an optional,
  clearly separate layout.
- **Circular scanning**: does not fit a grid.

---

## 9. Proposal for a first version (to discuss with the owner)

**Scope.** Group A above. One switch, automatic row-column scanning, a small **Scanning** page in Edit Keyboard (or its own
section like *Style*), switch input chosen from a key or a mouse click, per-user settings.

**Settings and proposed defaults** (the numbers are my proposal, not from a source except where marked):

| Setting | Default | Range | Stored in |
|---|---|---|---|
| Scanning on/off | off | | user |
| Pattern | row-column | (later: linear, column-row, groups) | layout |
| Scan time (per step) | 1.0 s [source: Saltillo] | 0.2–10 s | user |
| Extra time on the first item | 0.5 s | 0–5 s | user |
| Rounds before stopping | 3 | 1–10, or no limit with a stop switch | user |
| Switch | a key (to be chosen) / left mouse click | | user |
| Select on | press | press / release (later) | user |
| Ignore repeated presses within | 300 ms | 0–2000 ms | user |
| Highlight | border + background | | user |
| Border thickness / magnify | wide / off | | user |
| Click on step / on select | off / on | | user |
| Skip empty keys | on | | layout |

**Decisions the owner has to make before building:**

1. **Which switch inputs:** a chosen key only, or key and mouse click; a gamepad now or later?
2. **One switch or two from the start?** (Step scanning with two switches is the easiest to build after one-switch automatic.)
3. **Where do the settings live:** in the user's own settings (my advice for speed and switch) with the pattern in the layout, or all in the layout?
4. **How scanning, slow keys and dwell interact:** exclusive (like slow keys and dwell today) or combinable?
5. **Edit mode:** scanning is switched off while editing, and how does a scanning user reach Edit mode (a key in the scan)?
6. **Modifiers and layers:** a scan has to select Shift/AltGr and then a key: sticky modifiers as normal keys in the scan, or a separate group?
7. **Word prediction:** the suggestion row as the first thing the scan visits, or the last?
8. **Test with a real user and a real switch** before more is built: the research is clear that settings are individual.

**Technical outline (not decided).** A `Scanner` class that owns the state (level, position, timer), a global input source (keyboard
hook for the chosen key with suppression, mouse hook), a highlight painter on the existing key buttons (the dwell fill already
exists), a `ScanSettings` object in `UserSettings`, and tests for the state machine without a window (like `T_StickyModifiers`).
The state machine is where most of the logic and the tests are.

---

## 10. Sources and what could not be verified

Read in this session:

- Switch access scanning (Wikipedia): <https://en.wikipedia.org/wiki/Switch_access_scanning>
- Fast Switch Scanning Keyboards: Minimal Expected Query Decision Trees (arXiv 1606.02552): <https://ar5iv.labs.arxiv.org/html/1606.02552>
- Google, Change Switch Access settings: <https://support.google.com/accessibility/android/answer/6301497>
- AbilityNet, Switch Control on iPhone/iPad (iOS 12.3): <https://mcmw.abilitynet.org.uk/switch-control-iphoneipadipod-touch-ios-12-3>
- Apple, Set up and turn on Switch Control on iPhone: <https://support.apple.com/en-tj/guide/iphone/iph400b2f114/ios> (the page text was not returned; used only through search snippets)
- Smartbox, Using switch scanning with Grid 3: <https://hub.thinksmartbox.com/knowledgebase/using-switch-scanning-with-grid-3/>
- Jabbla, Mind Express 5 webhelp: step settings <https://www.jabblasoft.com/files/helpfiles/mindexpress5_nl_webhelp/ta_de_stapinstellingen_kiezen_bij_scanningsselectie.html>,
  step pattern <https://www.jabblasoft.com/files/helpfiles/mindexpress5_nl_webhelp/ta_het_stappatroon_bij_scanningsselectie_instellen.html>,
  selection indicator <https://www.jabblasoft.com/files/helpfiles/mindexpress5_nl_webhelp/ta_de_selectie_indicator_opties_bij_scanningsselectie_instellen.html>,
  swapping step and select switches <https://www.jabblasoft.com/files/helpfiles/mindexpress5_nl_webhelp/ta_het_wisselen_van_stappen_en_selecteren_tijdens_het_scannen.html>,
  general <https://www.jabblasoft.com/files/helpfiles/mindexpress5_nl_webhelp/co_selectie_instellingen.html>
- Microsoft, Use the On-Screen Keyboard: <https://support.microsoft.com/en-us/help/10762/windows-use-on-screen-keyboard>
- KPR (Koester Performance Research), Scanning: <https://kpronline.com/scanning>; Scanning Wizard studies <https://kpronline.com/blog/study-published-on-effectiveness-of-scanning-wizard/>
- Autonomic responses … single-switch scanning (PMC 3975284): <https://www.ncbi.nlm.nih.gov/pmc/articles/PMC3975284/> (found in a search; the paper itself was not opened)
- Search results used for the Saltillo default (1 s, range 0.2–10 s): <https://saltillo.com/support/article/Scan-Timing> (the page itself could not be opened: expired certificate)

**Could not be verified (do not rely on these):**

- The content of "Selecting an Appropriate Scan Rate: The .65 Rule" (Simpson, Koester, LoPresti 2007): not found.
- The Jabbla "Scanning snelstarter en EHBO" PDFs: downloaded, but the text could not be extracted here (no PDF tools).
  The Mind Express webhelp pages above cover the settings; the troubleshooting advice (EHBO) is not covered.
- Numeric defaults of iOS Auto Scanning Time and Android Auto-scan time: not published in the pages found.
- The scale of the Windows OSK "Scanning speed" slider, and whether it scans rows, columns or keys.
- Quadrant scanning as a named method: no source found; described from general knowledge.
- Grid 3: no "inverse scanning" by that name; "hold to advance" is the equivalent. Tobii Dynavox, Click-N-Type and others were only
  seen in a product list; their scanning options were not read.

# The text format of a keyboard

The wizard's paste box ("Type or paste the keys") and **Edit Keyboard › General › Export as text…** use the same plain-text notation, so a
keyboard exported as text can be edited by hand and pasted into the wizard again (`LayoutText.cs`, `WizardKeyParser.cs`).

One **row per line**, one **key per word** (separated by spaces or tabs). Empty lines are skipped.

| Written | Key |
|---|---|
| `a` or `A` | the letter **a**, with **A** on Shift (one letter is always the small letter) |
| `[A]` | a capital on the key itself, no Shift variant (a single letter in brackets is taken as typed) |
| `ja` `7` `.` | a key with that text |
| `"thank you"` | one key with a phrase (spaces inside) |
| `_` (or `""`) | an empty key |
| `[enter]` `[backspace]` `[tab]` `[space]` `[esc]` `[del]` `[up]` … | special keys (the **?** button of the wizard lists them all) |
| `[shift]` `[ctrl]` `[alt]` `[altgr]` `[win]` `[caps]` | modifier keys |
| `[f1]` … `[f16]`, `[dead:^]` | function keys, dead keys |
| `[wp]` | a **word-prediction** key; the numbers (`wp:0`, `wp:1`, …) follow the order of the keys, at most 10 |
| `[layout:other.kbl]` | a key that switches to another layout file |
| `[ctrl+c]` `[alt+f4]` `[ctrl+shift+esc]` `[win+d]` | a **shortcut**: Ctrl, Alt, Shift, Win in any order, then one key |
| `e[shift:É]` `e[altgr:€]` | extra layers attached to a key (**no space**): what it shows and sends with Shift or AltGr |
| `[key:label\|send\|shift label\|shift send\|altgr label\|altgr send]` | every layer written out, for what the forms above cannot say (`\|` and `\]` for those characters) |
| `q*2` `[wp]*2` `[enter]*2x2` | a key **two cells wide** (and two tall): the size comes last, after any layer |

* A wide or tall key covers the cells next to it / below it: the next tokens of those rows start after it.
* The last column of the first row is reserved for the settings (gear) button; it is not in the text.
* **Left out of the text:** colours, fonts, groups, the window and every setting. A keyboard made from text gets the style chosen in the
  wizard; its groups are assigned from the keys (vowels, consonants, numbers, controls, punctuation, prediction).
* A key the short forms cannot say is exported as `[key:…]`, so an export always reads back as the same keys.

// KeyLayout.cs — Factory that builds the keyboard grid layouts shipped with the app.
//
// A "layout" here means the physical arrangement of keys: which key goes in which
// row/column, what label it shows, and what keystroke it sends when pressed.
//
// Three layouts are provided:
//   - QWERTY  — standard US keyboard (6 rows × 14 columns)
//   - AZERTY  — Belgian/French keyboard (6 rows × 14 columns)
//   - Math    — extended mathematics symbol pad (7 rows × 22 columns)
//
// Each layout is described by a GridLayout object (a grid of GridCell objects).
// The private helper methods near the bottom of this class (K, KS, KSA, KSend, Blank)
// are shorthand builders that keep the layout definitions concise and readable.

using System.Collections.Generic;
using System.Drawing;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A static factory class that constructs the default keyboard grid layouts.
    /// "Static" means you never create an instance — you call the methods directly
    /// on the class, e.g. <c>KeyLayout.BuildDefaultQwerty()</c>.
    /// </summary>
    public static class KeyLayout
    {
        // ── Shared classification sets ────────────────────────────────────────
        // These sets are used elsewhere in the app to quickly test what kind of
        // key a given label represents, without hardcoding the same strings
        // in multiple places.

        /// <summary>
        /// Labels that represent modifier keys — keys that change the meaning of
        /// the next key pressed (like Shift or Ctrl) rather than typing a character.
        /// The UI uses this to apply special visual styling and behaviour.
        /// </summary>
        public static readonly HashSet<string> ModifierLabels = new HashSet<string>
            { "Shift","Ctrl","Alt","Win","AltGr","Caps" };

        /// <summary>
        /// The subset of modifier keys that specifically activate "uppercase / symbol"
        /// mode. Both physical Shift and Caps Lock serve this purpose.
        /// </summary>
        public static readonly HashSet<string> ShiftModifiers = new HashSet<string>
            { "Shift","Caps" };

        /// <summary>
        /// Labels whose symbols are already large unicode glyphs (Backspace ⌫ and
        /// Enter ↵). The renderer uses this set to scale these symbols differently
        /// so they fit the key face nicely.
        /// </summary>
        public static readonly HashSet<string> LargeSymbolLabels = new HashSet<string>
            { "⌫","↵" };

        // ── Default send-string table ─────────────────────────────────────────
        // When a key is pressed, the app "sends" a keystroke to the active application.
        // For regular letters the send-string is just the letter itself, but for
        // special keys we need the SendKeys escape syntax: e.g. {ENTER}, {F1}, etc.
        // This dictionary maps the visible label to the string that should be sent.

        /// <summary>
        /// Maps a key's display label to the keystroke string that will be injected
        /// into the active application via SendKeys.
        /// Keys not listed here fall back to sending their label text directly.
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<string,string> DefaultSend =
            new System.Collections.Generic.Dictionary<string,string>
        {
            {"Esc","{ESC}"},
            {"F1","{F1}"},{"F2","{F2}"},{"F3","{F3}"},{"F4","{F4}"},
            {"F5","{F5}"},{"F6","{F6}"},{"F7","{F7}"},{"F8","{F8}"},
            {"F9","{F9}"},{"F10","{F10}"},{"F11","{F11}"},{"F12","{F12}"},
            {"⌫","{BACKSPACE}"},{"Tab","{TAB}"},{"Caps","{CAPSLOCK}"},
            {"↵","{ENTER}"},
            // Modifier keys do not send anything by themselves — the modifier state
            // is tracked separately and applied when the next key is pressed.
            {"Shift",""},{"Ctrl",""},{"Win",""},{"Alt",""},{"AltGr",""},
            {"Space"," "},
            {"←","{LEFT}"},{"→","{RIGHT}"},{"↑","{UP}"},{"↓","{DOWN}"},
        };

        /// <summary>
        /// Returns the SendKeys string for a given key label, falling back to the
        /// label itself when no special mapping exists.
        /// For example: <c>"↵"</c> → <c>"{ENTER}"</c>, <c>"a"</c> → <c>"a"</c>.
        /// </summary>
        /// <param name="label">The visible label on the key face.</param>
        /// <returns>The string to pass to SendKeys when this key is pressed.</returns>
        public static string GetDefaultSend(string label)
        {
            // TryGetValue avoids a KeyNotFoundException — it returns false instead
            // of throwing when the key is absent.
            if (DefaultSend.TryGetValue(label, out var s)) return s;
            return label; // No special mapping → send the character as-is
        }

        // ── Layout builders ───────────────────────────────────────────────────

        // ── QWERTY — 6 rows × 14 cols grid ───────────────────────────
        /// <summary>
        /// Builds the standard US QWERTY keyboard layout as a 6-row × 14-column grid.
        /// The layout mirrors a physical keyboard:
        /// row 0 = function keys, row 1 = number row, rows 2-4 = letter rows,
        /// row 5 = bottom modifier + arrow row.
        /// </summary>
        /// <returns>
        /// A <see cref="GridLayout"/> populated with all QWERTY key cells,
        /// including shift-layer characters (e.g. pressing Shift+1 sends "!").
        /// </returns>
        public static GridLayout BuildDefaultQwerty()
        {
            // 6 rows × 14 cols
            var g = new GridLayout(6, 14);

            // Row 0: Esc + F1-F12  (13 keys, last col empty → fill with blank)
            Add(g,0,0,1,1, K("Esc"));
            for (int i=1;i<=12;i++) Add(g,0,i,1,1, K($"F{i}"));
            Add(g,0,13,1,1, Blank()); // empty spacer at end of function-key row

            // Row 1: ` 1-0 - = ⌫   14 cols
            // Each entry is (normalLabel, shiftLabel, shiftSend).
            // shiftLabel is what appears when Shift is held; shiftSend is what gets sent.
            string[] r1l = {"`","1","2","3","4","5","6","7","8","9","0","-","="};
            string[] r1sl= {"~","!","@","#","$","%","^","&","*","(",")","_","+"};
            string[] r1ss= {"~","!","@","#","$","%","^","&","*","(",")","_","+"};
            Add(g,1,0,1,1, KS(r1l[0],r1sl[0],r1ss[0]));
            for(int i=1;i<13;i++) Add(g,1,i,1,1, KS(r1l[i],r1sl[i],r1ss[i]));
            Add(g,1,13,1,1, K("⌫")); // Backspace at end of number row

            // Row 2: Tab(wide) q-p [ ] \  → Tab 1 wide, rest single
            Add(g,2,0,1,1, K("Tab"));
            string[] row2 = {"q","w","e","r","t","y","u","i","o","p","[","]","\\"};
            string[] row2s= {"Q","W","E","R","T","Y","U","I","O","P","{","}", "|"};
            for(int i=0;i<13;i++) Add(g,2,i+1,1,1, KS(row2[i],row2s[i],row2s[i]));

            // Row 3: Caps a-l ; ' ↵(spans rows 3-4, col 13)
            // The Enter key occupies two rows (3 and 4) in column 13,
            // matching the "big Enter" style found on many European keyboards.
            Add(g,3,0,1,1, K("Caps"));
            string[] row3 = {"a","s","d","f","g","h","j","k","l",";","'"};
            string[] row3s= {"A","S","D","F","G","H","J","K","L",":","\"" };
            for(int i=0;i<11;i++) Add(g,3,i+1,1,1, KS(row3[i],row3s[i],row3s[i]));
            Add(g,3,12,1,1, Blank()); // filler before Enter — keeps the grid rectangular
            Add(g,3,13,2,1, K("↵")); // Enter spans rows 3 and 4 (rowSpan=2)

            // Row 4: Shift(2-wide) z-/ Shift(1-wide, col 12 only — col 13 is Enter bottom)
            // Left Shift is 2 columns wide (cols 0-1); right Shift is 1 column (col 12).
            Add(g,4,0,1,2, K("Shift")); // left Shift: cols 0-1 (colSpan=2)
            string[] row4 = {"z","x","c","v","b","n","m",",",".","/","Shift"};
            string[] row4s= {"Z","X","C","V","B","N","M","<",">","?"};
            for(int i=0;i<10;i++) Add(g,4,i+2,1,1, KS(row4[i],row4s[i],row4s[i]));
            Add(g,4,12,1,1, K("Shift")); // right Shift: col 12 only (col 13 = Enter row 4)

            // Row 5: Ctrl Win Alt Space Alt Ctrl ← ↑ ↓ →
            // Space spans 5 columns (3-7) to give it the wide appearance of a real spacebar.
            Add(g,5,0,1,1, K("Ctrl"));
            Add(g,5,1,1,1, K("Win"));
            Add(g,5,2,1,1, K("Alt"));
            Add(g,5,3,1,5, KS("Space"," ","",""));  // space spans cols 3-7 (colSpan=5)
            Add(g,5,8,1,1, K("Alt"));
            Add(g,5,9,1,1, K("Ctrl"));
            Add(g,5,10,1,1, K("←"));
            Add(g,5,11,1,1, K("↑"));
            Add(g,5,12,1,1, K("↓"));
            Add(g,5,13,1,1, K("→"));

            return g;
        }

        // ── Belgian AZERTY — 6 rows × 14 cols ────────────────────────
        /// <summary>
        /// Builds the Belgian AZERTY keyboard layout as a 6-row × 14-column grid.
        /// AZERTY differs from QWERTY in both key positions and the presence of a
        /// third AltGr layer for accented and special characters (e.g. AltGr+E = €).
        /// </summary>
        /// <returns>
        /// A <see cref="GridLayout"/> populated with all AZERTY key cells,
        /// including shift-layer and AltGr-layer characters.
        /// </returns>
        public static GridLayout BuildAzerty()
        {
            var g = new GridLayout(6, 14);

            // Row 0: Esc F1-F12 blank
            Add(g,0,0,1,1,K("Esc"));
            for(int i=1;i<=12;i++) Add(g,0,i,1,1,K($"F{i}"));
            Add(g,0,13,1,1,Blank());

            // Row 1: ² & é " ' ( § è ! ç à ) - ⌫
            // Each tuple is (normal, shift, altGr).
            // KSA creates a key with all three layers.
            // Note: some AltGr values are the same as Shift values — that is correct
            // for Belgian AZERTY where not every key has a distinct AltGr character.
            var r1 = new[]{("²","³","³"),("&","1","1"),("é","2","~"),("\"","3","#"),
                           ("'","4","{"),("(","5","["),("§","6","^"),("è","7",""),
                           ("!","8","\\"),("ç","9",""),("à","0","@"),("-","°","]"),
                           (")","_","}"),("⌫","","")};
            for(int i=0;i<14;i++) Add(g,1,i,1,1,KSA(r1[i].Item1,r1[i].Item2,r1[i].Item2,r1[i].Item3,r1[i].Item3));

            // Row 2: Tab a z e r t y u i o p ^ $ ↵(span 2 rows)
            // On AZERTY the Q and A swap places, and Z moves to where S is on QWERTY.
            // The Enter key here spans rows 2-3 in the last column (wide L-shaped Enter).
            Add(g,2,0,1,1,K("Tab"));
            var r2=(new[]{("a","A",""),("z","Z",""),("e","E","€"),("r","R",""),
                          ("t","T",""),("y","Y",""),("u","U",""),("i","I",""),
                          ("o","O",""),("p","P",""),("^","¨",""),("$","*","")});
            for(int i=0;i<12;i++) Add(g,2,i+1,1,1,KSA(r2[i].Item1,r2[i].Item2,r2[i].Item2,r2[i].Item3,r2[i].Item3));
            Add(g,2,13,2,1,K("↵")); // spans rows 2-3 (rowSpan=2) — L-shaped Enter

            // Row 3: Caps q s d f g h j k l m ù µ [Enter spans]
            // Column 13 is intentionally not set here because it belongs to the
            // Enter key that started in row 2 (spanning down into this row).
            Add(g,3,0,1,1,K("Caps"));
            var r3=(new[]{("q","Q",""),("s","S",""),("d","D",""),("f","F",""),
                          ("g","G",""),("h","H",""),("j","J",""),("k","K",""),
                          ("l","L",""),("m","M",""),("ù","%",""),("µ","","")});
            for(int i=0;i<12;i++) Add(g,3,i+1,1,1,KSA(r3[i].Item1,r3[i].Item2,r3[i].Item2,r3[i].Item3,r3[i].Item3));
            // col 13 taken by Enter span — no cell added here

            // Row 4: Shift > w x c v b n , ; : = Shift
            // Belgian AZERTY has a dedicated > / < key to the right of left Shift.
            Add(g,4,0,1,1,K("Shift"));
            var r4=(new[]{(">","<","\\"),("w","W",""),("x","X",""),("c","C",""),
                          ("v","V",""),("b","B",""),("n","N",""),(",","?",""),
                          (";",".",""),(":","/",""),("=","+",""),});
            for(int i=0;i<11;i++) Add(g,4,i+1,1,1,KSA(r4[i].Item1,r4[i].Item2,r4[i].Item2,r4[i].Item3,r4[i].Item3));
            Add(g,4,12,1,2,K("Shift")); // wide right Shift spans cols 12-13 (colSpan=2)

            // Row 5: Ctrl Win Alt Space AltGr Ctrl ← ↑ ↓ →
            // AZERTY uses AltGr instead of a second Alt key (to reach the third layer).
            Add(g,5,0,1,1,K("Ctrl"));
            Add(g,5,1,1,1,K("Win"));
            Add(g,5,2,1,1,K("Alt"));
            Add(g,5,3,1,5,KS("Space"," ","",""));
            Add(g,5,8,1,1,K("AltGr")); // AltGr = right Alt — activates the third character layer
            Add(g,5,9,1,1,K("Ctrl"));
            Add(g,5,10,1,1,K("←"));
            Add(g,5,11,1,1,K("↑"));
            Add(g,5,12,1,1,K("↓"));
            Add(g,5,13,1,1,K("→"));

            return g;
        }

        // ── Mathematics — 7 rows × 22 cols ───────────────────────────
        /// <summary>
        /// Builds a specialised mathematics symbol keyboard as a 7-row × 22-column grid.
        /// This layout does not represent a physical keyboard. Instead it organises
        /// mathematical symbols thematically:
        /// row 0 = digits and arithmetic, row 1 = grouping and set theory,
        /// row 2 = arrows and trigonometry, row 3 = calculus and constants,
        /// rows 4-5 = Greek alphabet and logic, row 6 = editing and navigation.
        /// Each key has a normal face and a shift face (an alternative symbol).
        /// </summary>
        /// <returns>
        /// A <see cref="GridLayout"/> populated with mathematics symbol keys.
        /// </returns>
        public static GridLayout BuildMath()
        {
            // Use 7 rows × 22 cols for math layout
            var g = new GridLayout(7, 22);

            // Row 0: digits 0-9, dot, arithmetic operators, ⌫
            // Each tuple is (normal, shift). For example: "1" normal, "¹" (superscript) on shift.
            var row0 = new[]{
                ("1","¹"),("2","²"),("3","³"),("4","⁴"),("5","⁵"),
                ("6","⁶"),("7","⁷"),("8","⁸"),("9","⁹"),("0","⁰"),
                (".","," ),
                ("+","±" ),("−","∓" ),("×","·" ),("÷","/" ),
                ("=","≡" ),("≠","≈" ),("<","≤" ),(">","≥" ),
            };
            for(int i=0;i<row0.Length;i++)
                Add(g,0,i,1,1,KS(row0[i].Item1,row0[i].Item2,row0[i].Item2));
            Add(g,0,row0.Length,1,3,K("⌫")); // wide backspace spans cols 19-21 (colSpan=3)

            // Row 1: grouping, advanced operators, sets, number sets
            // ∈ (element-of), ⊂ (subset), ∩ (intersection), ℕ (naturals), etc.
            var row1ops = new[]{
                ("(","["),(")","]"),(  "{","}"  ),
                ("√","∛"),(  "^","_"  ),("%","‰"),(  "∞","ℵ"  ),
                ("∑","∏"),("∫","∬"),("∂","∇"),("∆","□"),
                ("∈","∉"),("⊂","⊄"),("⊆","⊃"),("∩","∖"),("∪","△"),("∅","∁"),
                ("ℕ","ℤ"),("ℚ","ℝ"),("ℂ","ℍ"),
            };
            for(int i=0;i<row1ops.Length && i<22;i++)
                Add(g,1,i,1,1,KS(row1ops[i].Item1,row1ops[i].Item2,row1ops[i].Item2));
            // fill remaining cols with invisible spacers to keep the grid complete
            for(int i=row1ops.Length;i<22;i++) Add(g,1,i,1,1,Blank());

            // Row 2: arrows, trig, log + Enter(span 2 rows)
            // Trig keys are 2 columns wide (colSpan=2) because the labels
            // like "sin⁻¹" need more space than a single grid column provides.
            var row2 = new[]{
                ("→","⇒"),("←","⇐"),("↔","⇔"),("↦","⟼"),("↑","⇑"),("↓","⇓"),
            };
            for(int i=0;i<6;i++) Add(g,2,i,1,1,KS(row2[i].Item1,row2[i].Item2,row2[i].Item2));
            var trigs = new[]{
                ("sin","sin⁻¹"),("cos","cos⁻¹"),("tan","tan⁻¹"),
                ("sec","csc"  ),("cot","csc⁻¹"),
                ("log","log₂" ),("ln","log₁₀" ),
            };
            for(int i=0;i<7;i++) Add(g,2,i+6,1,2,KS(trigs[i].Item1,trigs[i].Item2,trigs[i].Item2));
            Add(g,2,20,2,2,K("↵")); // Enter spans rows 2-3, cols 20-21

            // Row 3: calculus, combinatorics, fractions, constants
            // "exp", "lim", "d/dx" etc. are compound labels that send their text directly.
            var row3 = new[]{
                ("exp","eˣ"),("lim","lim→"),("d/dx","∂/∂x"),("∮","∯"),
                ("n!","nPr"),("nCr","(nk)"),
                ("½","⅓"),("¼","¾"),
                ("⌊⌋","⌈⌉"),("|x|","‖x‖"),
                ("π","τ"),("e","ℯ"),("i","j"),("ϕ","ℏ"),
            };
            for(int i=0;i<row3.Length && i<20;i++) Add(g,3,i,1,1,KS(row3[i].Item1,row3[i].Item2,row3[i].Item2));
            for(int i=row3.Length;i<20;i++) Add(g,3,i,1,1,Blank());
            // cols 20-21 taken by Enter span from row 2 — no cell added here

            // Row 4: Greek α-ξ + logic
            // Lower-case Greek on normal layer, upper-case on shift layer.
            var row4greek = new[]{
                ("α","Α"),("β","Β"),("γ","Γ"),("δ","Δ"),("ε","Ε"),("ζ","Ζ"),
                ("η","Η"),("θ","Θ"),("ι","Ι"),("κ","Κ"),("λ","Λ"),("μ","Μ"),
                ("ν","Ν"),("ξ","Ξ"),
            };
            for(int i=0;i<row4greek.Length;i++) Add(g,4,i,1,1,KS(row4greek[i].Item1,row4greek[i].Item2,row4greek[i].Item2));
            // Logic operators fill the remaining columns of row 4.
            var row4logic = new[]{("∧","⊼"),("∨","⊽"),("¬","⊻"),("∀","∄"),("∃","∄"),
                                   ("⊢","⊣"),("⊨","⊭"),("∴","∵")};
            for(int i=0;i<8 && i+14<22;i++) Add(g,4,i+14,1,1,KS(row4logic[i].Item1,row4logic[i].Item2,row4logic[i].Item2));

            // Row 5: Greek ο-ω + proof/geometry
            // The second half of the Greek alphabet continues here.
            var row5greek = new[]{
                ("ο","Ο"),("π","Π"),("ρ","Ρ"),("σ","Σ"),("τ","Τ"),
                ("υ","Υ"),("φ","Φ"),("χ","Χ"),("ψ","Ψ"),("ω","Ω"),
            };
            for(int i=0;i<10;i++) Add(g,5,i,1,1,KS(row5greek[i].Item1,row5greek[i].Item2,row5greek[i].Item2));
            // Geometry and proof symbols fill the rest of the row.
            var row5proof = new[]{
                ("≅","≇"),("⊥","∥"),("∝","∼"),("°","′"),("∠","∡"),("≪","≫"),
                ("±","∓"),("⊕","⊗"),("ℵ","∞"),("∫","∮"),("∂","∇"),("∑","∏"),
            };
            for(int i=0;i<12;i++) Add(g,5,i+10,1,1,KS(row5proof[i].Item1,row5proof[i].Item2,row5proof[i].Item2));

            // Row 6: edit/navigation
            // Provides clipboard and navigation controls so the user does not have
            // to switch away from the maths keyboard for common editing tasks.
            // KSend is used here because these keys send Ctrl+key shortcuts, not characters.
            Add(g,6,0,1,2,K("Shift"));
            Add(g,6,2,1,2,K("Ctrl"));
            Add(g,6,4,1,2,KSend("Cut","^x"));   // ^x = Ctrl+X in SendKeys syntax
            Add(g,6,6,1,2,KSend("Copy","^c"));  // ^c = Ctrl+C
            Add(g,6,8,1,2,KSend("Paste","^v")); // ^v = Ctrl+V
            Add(g,6,10,1,2,KSend("Undo","^z")); // ^z = Ctrl+Z
            Add(g,6,12,1,6,KS("Space"," ","",""));  // wide spacebar spans cols 12-17
            Add(g,6,18,1,1,K("←"));
            Add(g,6,19,1,1,K("↑"));
            Add(g,6,20,1,1,K("↓"));
            Add(g,6,21,1,1,K("→"));

            return g;
        }

        // ── Private helper methods ────────────────────────────────────────────
        // These one-liners reduce repetition in the layout definitions above.
        // The single-letter names (K, KS, KSA) are intentionally short because
        // they are called hundreds of times and long names would obscure the layout.

        /// <summary>
        /// Adds a key cell to the grid at the given position.
        /// </summary>
        /// <param name="g">The grid to add to.</param>
        /// <param name="r">Zero-based row index (top = 0).</param>
        /// <param name="c">Zero-based column index (left = 0).</param>
        /// <param name="rs">Row span: how many rows tall this cell is (usually 1).</param>
        /// <param name="cs">Column span: how many columns wide this cell is (usually 1).</param>
        /// <param name="p">The key properties (label, send string, shift state, etc.).</param>
        private static void Add(GridLayout g, int r, int c, int rs, int cs, KeyProps p)
            => g.Cells.Add(new GridCell(r, c, p, rs, cs));

        /// <summary>
        /// Creates a simple key with a single label and no shift layer.
        /// Uses <see cref="GetDefaultSend"/> to look up the keystroke string.
        /// </summary>
        /// <param name="label">The visible text on the key face.</param>
        private static KeyProps K(string label) =>
            new KeyProps(label, GetDefaultSend(label));

        /// <summary>
        /// Creates a key with a normal layer and a Shift layer.
        /// The same send string is reused for shiftSend when omitted at the call site.
        /// </summary>
        /// <param name="label">Normal-layer display label.</param>
        /// <param name="shiftLabel">Display label shown while Shift is active.</param>
        /// <param name="shiftSend">Keystroke string sent when Shift is active.</param>
        private static KeyProps KS(string label, string shiftLabel, string shiftSend) =>
            new KeyProps(label, GetDefaultSend(label), shiftLabel, shiftSend);

        /// <summary>
        /// Creates a key with an explicit send string (overriding the DefaultSend lookup)
        /// plus Shift-layer label and send string.
        /// Used when the normal send string differs from the label (e.g. Space → " ").
        /// </summary>
        /// <param name="label">Normal-layer display label.</param>
        /// <param name="send">Keystroke string sent in normal state.</param>
        /// <param name="shiftLabel">Display label shown while Shift is active.</param>
        /// <param name="shiftSend">Keystroke string sent when Shift is active.</param>
        private static KeyProps KS(string label, string send, string shiftLabel, string shiftSend) =>
            new KeyProps(label, send, shiftLabel, shiftSend);

        /// <summary>
        /// Creates a key with three layers: normal, Shift, and AltGr.
        /// Used by AZERTY keys that have a third character accessed via the AltGr modifier.
        /// </summary>
        /// <param name="label">Normal-layer display label.</param>
        /// <param name="shiftLabel">Shift-layer display label.</param>
        /// <param name="shiftSend">Keystroke string sent when Shift is active.</param>
        /// <param name="altGrLabel">AltGr-layer display label.</param>
        /// <param name="altGrSend">Keystroke string sent when AltGr is active.</param>
        private static KeyProps KSA(string label, string shiftLabel, string shiftSend,
                                     string altGrLabel, string altGrSend) =>
            new KeyProps(label, GetDefaultSend(label), shiftLabel, shiftSend,
                         altGrLabel, altGrSend);

        /// <summary>
        /// Creates a key with a custom label and an explicit send string,
        /// bypassing the DefaultSend lookup entirely.
        /// Useful for action keys like Cut/Copy/Paste where the label is a word
        /// but the send string is a Ctrl+key shortcut (e.g. <c>"^c"</c>).
        /// </summary>
        /// <param name="label">Display text on the key face.</param>
        /// <param name="send">Exact keystroke string to send (SendKeys syntax).</param>
        private static KeyProps KSend(string label, string send) =>
            new KeyProps(label, send);

        /// <summary>
        /// Creates an invisible placeholder cell that occupies grid space without
        /// displaying anything. Used to keep the grid rectangular when a row has
        /// fewer keys than the column count, or to reserve space next to a spanning key.
        /// </summary>
        private static KeyProps Blank() =>
            new KeyProps("", "");  // all style = sentinels (inherit from global)
    }
}

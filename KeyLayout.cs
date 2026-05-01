using System.Collections.Generic;
using System.Drawing;

namespace OnScreenKeyboard
{
    /// <summary>Builds default grid layouts.</summary>
    public static class KeyLayout
    {
        public static readonly HashSet<string> ModifierLabels = new HashSet<string>
            { "Shift","Ctrl","Alt","Win","AltGr","Caps" };

        public static readonly HashSet<string> ShiftModifiers = new HashSet<string>
            { "Shift","Caps" };

        public static readonly HashSet<string> LargeSymbolLabels = new HashSet<string>
            { "⌫","↵" };

        public static readonly System.Collections.Generic.Dictionary<string,string> DefaultSend =
            new System.Collections.Generic.Dictionary<string,string>
        {
            {"Esc","{ESC}"},
            {"F1","{F1}"},{"F2","{F2}"},{"F3","{F3}"},{"F4","{F4}"},
            {"F5","{F5}"},{"F6","{F6}"},{"F7","{F7}"},{"F8","{F8}"},
            {"F9","{F9}"},{"F10","{F10}"},{"F11","{F11}"},{"F12","{F12}"},
            {"⌫","{BACKSPACE}"},{"Tab","{TAB}"},{"Caps","{CAPSLOCK}"},
            {"↵","{ENTER}"},
            {"Shift",""},{"Ctrl",""},{"Win",""},{"Alt",""},{"AltGr",""},
            {"Space"," "},
            {"←","{LEFT}"},{"→","{RIGHT}"},{"↑","{UP}"},{"↓","{DOWN}"},
        };

        public static string GetDefaultSend(string label)
        {
            if (DefaultSend.TryGetValue(label, out var s)) return s;
            return label;
        }

        // ── QWERTY — 6 rows × 14 cols grid ───────────────────────────
        public static GridLayout BuildDefaultQwerty()
        {
            // 6 rows × 14 cols
            var g = new GridLayout(6, 14);

            // Row 0: Esc + F1-F12  (13 keys, last col empty → fill with blank)
            Add(g,0,0,1,1, K("Esc"));
            for (int i=1;i<=12;i++) Add(g,0,i,1,1, K($"F{i}"));
            Add(g,0,13,1,1, Blank());

            // Row 1: ` 1-0 - = ⌫   14 cols
            string[] r1l = {"`","1","2","3","4","5","6","7","8","9","0","-","="};
            string[] r1sl= {"~","!","@","#","$","%","^","&","*","(",")","_","+"};
            string[] r1ss= {"~","!","@","#","$","%","^","&","*","(",")","_","+"};
            Add(g,1,0,1,1, KS(r1l[0],r1sl[0],r1ss[0]));
            for(int i=1;i<13;i++) Add(g,1,i,1,1, KS(r1l[i],r1sl[i],r1ss[i]));
            Add(g,1,13,1,1, K("⌫"));

            // Row 2: Tab(wide) q-p [ ] \  → Tab 1 wide, rest single
            Add(g,2,0,1,1, K("Tab"));
            string[] row2 = {"q","w","e","r","t","y","u","i","o","p","[","]","\\"};
            string[] row2s= {"Q","W","E","R","T","Y","U","I","O","P","{","}", "|"};
            for(int i=0;i<13;i++) Add(g,2,i+1,1,1, KS(row2[i],row2s[i],row2s[i]));

            // Row 3: Caps a-l ; ' ↵(spans rows 3-4, col 13)
            Add(g,3,0,1,1, K("Caps"));
            string[] row3 = {"a","s","d","f","g","h","j","k","l",";","'"};
            string[] row3s= {"A","S","D","F","G","H","J","K","L",":","\"" };
            for(int i=0;i<11;i++) Add(g,3,i+1,1,1, KS(row3[i],row3s[i],row3s[i]));
            Add(g,3,12,1,1, Blank()); // filler before Enter
            Add(g,3,13,2,1, K("↵")); // Enter spans rows 3 and 4

            // Row 4: Shift(2-wide) z-/ Shift(1-wide, col 12 only — col 13 is Enter bottom)
            Add(g,4,0,1,2, K("Shift")); // left Shift: cols 0-1
            string[] row4 = {"z","x","c","v","b","n","m",",",".","/","Shift"};
            string[] row4s= {"Z","X","C","V","B","N","M","<",">","?"};
            for(int i=0;i<10;i++) Add(g,4,i+2,1,1, KS(row4[i],row4s[i],row4s[i]));
            Add(g,4,12,1,1, K("Shift")); // right Shift: col 12 only (col 13 = Enter row 4)

            // Row 5: Ctrl Win Alt Space Alt Ctrl ← ↑ ↓ →
            Add(g,5,0,1,1, K("Ctrl"));
            Add(g,5,1,1,1, K("Win"));
            Add(g,5,2,1,1, K("Alt"));
            Add(g,5,3,1,5, KS("Space"," ","",""));  // space spans cols 3-7
            Add(g,5,8,1,1, K("Alt"));
            Add(g,5,9,1,1, K("Ctrl"));
            Add(g,5,10,1,1, K("←"));
            Add(g,5,11,1,1, K("↑"));
            Add(g,5,12,1,1, K("↓"));
            Add(g,5,13,1,1, K("→"));

            return g;
        }

        // ── Belgian AZERTY — 6 rows × 14 cols ────────────────────────
        public static GridLayout BuildAzerty()
        {
            var g = new GridLayout(6, 14);

            // Row 0: Esc F1-F12 blank
            Add(g,0,0,1,1,K("Esc"));
            for(int i=1;i<=12;i++) Add(g,0,i,1,1,K($"F{i}"));
            Add(g,0,13,1,1,Blank());

            // Row 1: ² & é " ' ( § è ! ç à ) - ⌫
            var r1 = new[]{("²","³","³"),("&","1","1"),("é","2","~"),("\"","3","#"),
                           ("'","4","{"),("(","5","["),("§","6","^"),("è","7",""),
                           ("!","8","\\"),("ç","9",""),("à","0","@"),("-","°","]"),
                           (")","_","}"),("⌫","","")};
            for(int i=0;i<14;i++) Add(g,1,i,1,1,KSA(r1[i].Item1,r1[i].Item2,r1[i].Item2,r1[i].Item3,r1[i].Item3));

            // Row 2: Tab a z e r t y u i o p ^ $ ↵(span 2 rows)
            Add(g,2,0,1,1,K("Tab"));
            var r2=(new[]{("a","A",""),("z","Z",""),("e","E","€"),("r","R",""),
                          ("t","T",""),("y","Y",""),("u","U",""),("i","I",""),
                          ("o","O",""),("p","P",""),("^","¨",""),("$","*","")});
            for(int i=0;i<12;i++) Add(g,2,i+1,1,1,KSA(r2[i].Item1,r2[i].Item2,r2[i].Item2,r2[i].Item3,r2[i].Item3));
            Add(g,2,13,2,1,K("↵")); // spans rows 2-3

            // Row 3: Caps q s d f g h j k l m ù µ [Enter spans]
            Add(g,3,0,1,1,K("Caps"));
            var r3=(new[]{("q","Q",""),("s","S",""),("d","D",""),("f","F",""),
                          ("g","G",""),("h","H",""),("j","J",""),("k","K",""),
                          ("l","L",""),("m","M",""),("ù","%",""),("µ","","")});
            for(int i=0;i<12;i++) Add(g,3,i+1,1,1,KSA(r3[i].Item1,r3[i].Item2,r3[i].Item2,r3[i].Item3,r3[i].Item3));
            // col 13 taken by Enter span

            // Row 4: Shift > w x c v b n , ; : = Shift
            Add(g,4,0,1,1,K("Shift"));
            var r4=(new[]{(">","<","\\"),("w","W",""),("x","X",""),("c","C",""),
                          ("v","V",""),("b","B",""),("n","N",""),(",","?",""),
                          (";",".",""),(":","/",""),("=","+",""),});
            for(int i=0;i<11;i++) Add(g,4,i+1,1,1,KSA(r4[i].Item1,r4[i].Item2,r4[i].Item2,r4[i].Item3,r4[i].Item3));
            Add(g,4,12,1,2,K("Shift")); // wide right Shift

            // Row 5: Ctrl Win Alt Space AltGr Ctrl ← ↑ ↓ →
            Add(g,5,0,1,1,K("Ctrl"));
            Add(g,5,1,1,1,K("Win"));
            Add(g,5,2,1,1,K("Alt"));
            Add(g,5,3,1,5,KS("Space"," ","",""));
            Add(g,5,8,1,1,K("AltGr"));
            Add(g,5,9,1,1,K("Ctrl"));
            Add(g,5,10,1,1,K("←"));
            Add(g,5,11,1,1,K("↑"));
            Add(g,5,12,1,1,K("↓"));
            Add(g,5,13,1,1,K("→"));

            return g;
        }

        // ── Mathematics — 7 rows × 22 cols ───────────────────────────
        public static GridLayout BuildMath()
        {
            // Use 7 rows × 22 cols for math layout
            var g = new GridLayout(7, 22);

            // Row 0: digits 0-9, dot, arithmetic operators, ⌫
            var row0 = new[]{
                ("1","¹"),("2","²"),("3","³"),("4","⁴"),("5","⁵"),
                ("6","⁶"),("7","⁷"),("8","⁸"),("9","⁹"),("0","⁰"),
                (".","," ),
                ("+","±" ),("−","∓" ),("×","·" ),("÷","/" ),
                ("=","≡" ),("≠","≈" ),("<","≤" ),(">","≥" ),
            };
            for(int i=0;i<row0.Length;i++)
                Add(g,0,i,1,1,KS(row0[i].Item1,row0[i].Item2,row0[i].Item2));
            Add(g,0,row0.Length,1,3,K("⌫")); // wide backspace cols 19-21

            // Row 1: grouping, advanced operators, sets, number sets
            var row1ops = new[]{
                ("(","["),(")","]"),(  "{","}"  ),
                ("√","∛"),(  "^","_"  ),("%","‰"),(  "∞","ℵ"  ),
                ("∑","∏"),("∫","∬"),("∂","∇"),("∆","□"),
                ("∈","∉"),("⊂","⊄"),("⊆","⊃"),("∩","∖"),("∪","△"),("∅","∁"),
                ("ℕ","ℤ"),("ℚ","ℝ"),("ℂ","ℍ"),
            };
            for(int i=0;i<row1ops.Length && i<22;i++)
                Add(g,1,i,1,1,KS(row1ops[i].Item1,row1ops[i].Item2,row1ops[i].Item2));
            // fill remaining cols
            for(int i=row1ops.Length;i<22;i++) Add(g,1,i,1,1,Blank());

            // Row 2: arrows, trig, log + Enter(span 2 rows)
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
            var row3 = new[]{
                ("exp","eˣ"),("lim","lim→"),("d/dx","∂/∂x"),("∮","∯"),
                ("n!","nPr"),("nCr","(nk)"),
                ("½","⅓"),("¼","¾"),
                ("⌊⌋","⌈⌉"),("|x|","‖x‖"),
                ("π","τ"),("e","ℯ"),("i","j"),("ϕ","ℏ"),
            };
            for(int i=0;i<row3.Length && i<20;i++) Add(g,3,i,1,1,KS(row3[i].Item1,row3[i].Item2,row3[i].Item2));
            for(int i=row3.Length;i<20;i++) Add(g,3,i,1,1,Blank());
            // cols 20-21 taken by Enter span

            // Row 4: Greek α-ξ + logic
            var row4greek = new[]{
                ("α","Α"),("β","Β"),("γ","Γ"),("δ","Δ"),("ε","Ε"),("ζ","Ζ"),
                ("η","Η"),("θ","Θ"),("ι","Ι"),("κ","Κ"),("λ","Λ"),("μ","Μ"),
                ("ν","Ν"),("ξ","Ξ"),
            };
            for(int i=0;i<row4greek.Length;i++) Add(g,4,i,1,1,KS(row4greek[i].Item1,row4greek[i].Item2,row4greek[i].Item2));
            var row4logic = new[]{("∧","⊼"),("∨","⊽"),("¬","⊻"),("∀","∄"),("∃","∄"),
                                   ("⊢","⊣"),("⊨","⊭"),("∴","∵")};
            for(int i=0;i<8 && i+14<22;i++) Add(g,4,i+14,1,1,KS(row4logic[i].Item1,row4logic[i].Item2,row4logic[i].Item2));

            // Row 5: Greek ο-ω + proof/geometry
            var row5greek = new[]{
                ("ο","Ο"),("π","Π"),("ρ","Ρ"),("σ","Σ"),("τ","Τ"),
                ("υ","Υ"),("φ","Φ"),("χ","Χ"),("ψ","Ψ"),("ω","Ω"),
            };
            for(int i=0;i<10;i++) Add(g,5,i,1,1,KS(row5greek[i].Item1,row5greek[i].Item2,row5greek[i].Item2));
            var row5proof = new[]{
                ("≅","≇"),("⊥","∥"),("∝","∼"),("°","′"),("∠","∡"),("≪","≫"),
                ("±","∓"),("⊕","⊗"),("ℵ","∞"),("∫","∮"),("∂","∇"),("∑","∏"),
            };
            for(int i=0;i<12;i++) Add(g,5,i+10,1,1,KS(row5proof[i].Item1,row5proof[i].Item2,row5proof[i].Item2));

            // Row 6: edit/navigation
            Add(g,6,0,1,2,K("Shift"));
            Add(g,6,2,1,2,K("Ctrl"));
            Add(g,6,4,1,2,KSend("Cut","^x"));
            Add(g,6,6,1,2,KSend("Copy","^c"));
            Add(g,6,8,1,2,KSend("Paste","^v"));
            Add(g,6,10,1,2,KSend("Undo","^z"));
            Add(g,6,12,1,6,KS("Space"," ","",""));
            Add(g,6,18,1,1,K("←"));
            Add(g,6,19,1,1,K("↑"));
            Add(g,6,20,1,1,K("↓"));
            Add(g,6,21,1,1,K("→"));

            return g;
        }

        // ── Helpers ───────────────────────────────────────────────────
        private static void Add(GridLayout g, int r, int c, int rs, int cs, KeyProps p)
            => g.Cells.Add(new GridCell(r, c, p, rs, cs));

        private static KeyProps K(string label) =>
            new KeyProps(label, GetDefaultSend(label));

        private static KeyProps KS(string label, string shiftLabel, string shiftSend) =>
            new KeyProps(label, GetDefaultSend(label), shiftLabel, shiftSend);

        // Overload accepting explicit send value for the label (e.g. Space → " ")
        private static KeyProps KS(string label, string send, string shiftLabel, string shiftSend) =>
            new KeyProps(label, send, shiftLabel, shiftSend);

        private static KeyProps KSA(string label, string shiftLabel, string shiftSend,
                                     string altGrLabel, string altGrSend) =>
            new KeyProps(label, GetDefaultSend(label), shiftLabel, shiftSend,
                         altGrLabel, altGrSend);

        private static KeyProps KSend(string label, string send) =>
            new KeyProps(label, send);

        private static KeyProps Blank() =>
            new KeyProps("", "");  // all style = sentinels (inherit from global)
    }
}

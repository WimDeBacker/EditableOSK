// LayoutTextTests.cs — the text notation of a keyboard (WizardKeyParser, LayoutText): word-prediction keys, shortcuts, layers, sizes,
// and the export that reads back as the same keys.

using System;
using System.Collections.Generic;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static WizardKeyParser.KeySpec TextKey(string text, bool dutch = false) => WizardKeyParser.Parse(text, dutch)[0][0];
        private static GridLayout TextGrid(string text, bool dutch = false) => LayoutText.BuildGrid(WizardKeyParser.Parse(text, dutch), dutch);
        private static GridCell TextCell(GridLayout g, int r, int c) => g.Cells.First(x => x.Covers(r, c));

        private static void T_LayoutText()
        {
            Section("Text format of a keyboard — word prediction, shortcuts, layers, sizes, export and import");

            // ── The name shown in the toolbar: the title, not the file name ──
            {
                var m = new LayoutMeta { Language = "nl" };
                m.Titles["nl"] = "Scanning letters"; m.Titles["en"] = "Scanning keyboard";
                Lang.Load("en");
                Assert(KeyboardForm.KeyboardName(m, @"C:\x\settings.xml") == "Scanning keyboard", "the toolbar shows the title in the language of the screens, not the file name");
                Lang.Load("nl");
                Assert(KeyboardForm.KeyboardName(m, @"C:\x\settings.xml") == "Scanning letters", "…in Dutch the Dutch title");
                m.Titles.Remove("nl"); Lang.Load("fr");
                Assert(KeyboardForm.KeyboardName(m, null) == "Scanning keyboard", "…a language without a title: the keyboard's language, else English");
                Lang.Load("en");
                Assert(KeyboardForm.KeyboardName(new LayoutMeta(), @"C:\x\azerty.kbl") == "azerty.kbl" && KeyboardForm.KeyboardName(new LayoutMeta(), null) == "default",
                    "no title: the file name, or \"default\" when there is no file");
            }

            // ── Word-prediction keys: [wp], numbered by the order of the keys ──
            {
                var g = TextGrid("[wp] [wp] a\n[wp]");
                var wp = g.Cells.Where(c => (c.Props.Send ?? "").StartsWith("wp:")).OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();
                Assert(wp.Count == 3 && wp[0].Props.Send == "wp:0" && wp[1].Props.Send == "wp:1" && wp[2].Props.Send == "wp:2",
                    "[wp] keys are numbered wp:0, wp:1, … in the order of the keys, row by row; the user never types a number");
                Assert(wp[0].Props.Label == "Word 1" && wp[2].Props.Label == "Word 3", "…and labelled Word 1, 2, …");
                Assert(TextGrid("[wp]", dutch: true).Cells.First(c => c.Props.Send == "wp:0").Props.Label == "Woord 1", "…in Dutch Woord 1, 2, …");
                Assert(NewKeyboardWizard.ClassifyKey(wp[0].Props.Label, wp[0].Props.Send) == "prediction", "…and the wizard puts them in the prediction group");
                var many = TextGrid(string.Join(" ", Enumerable.Repeat("[wp]", 12)));
                Assert(many.Cells.Count(c => (c.Props.Send ?? "").StartsWith("wp:")) == 10, "at most 10 word-prediction keys (slots 0 to 9): the others stay empty");
                Assert(TextKey("[WP]").IsWordPrediction, "[wp] in any case");
            }

            // ── Shortcuts ──
            foreach (var (text, label, send) in new[] {
                ("[ctrl+c]", "Ctrl+C", "^c"), ("[alt+f4]", "Alt+F4", "%{F4}"), ("[ctrl+shift+esc]", "Ctrl+Shift+Esc", "^+{ESC}"),
                ("[win+d]", "Win+D", "win:d"), ("[win+shift+s]", "Win+Shift+S", "win:+s"), ("[Ctrl+Alt+Del]", "Ctrl+Alt+Del", "^%{DELETE}"),
                ("[shift+tab]", "Shift+⇥", "+{TAB}"), ("[ctrl++]", "Ctrl++", "^{+}"), ("[control+z]", "Ctrl+Z", "^z") })
            {
                var k = TextKey(text);
                Assert(k.Label == label && k.Send == send, $"{text} is the shortcut {label} (sends '{send}'; it is '{k.Label}' / '{k.Send}')");
            }
            Assert(TextKey("[shift]").Label == "Shift" && TextKey("[shift]").Send == "", "a single modifier is still the modifier key");
            Assert(TextKey("[+]").Label == "+" && TextKey("[ctrl+]").Label == "ctrl+" && TextKey("[ctrl+ctrl+c]").Label == "ctrl+ctrl+c" && TextKey("[x+c]").Label == "x+c",
                "text that is not a shortcut stays text (a lone +, a missing key, a modifier twice, an unknown modifier)");
            Assert(TextKey("\"ctrl+c\"").Label == "ctrl+c", "…and quoted text is never a shortcut");

            // ── Layout switch and the all-layers form ──
            { var k = TextKey("[layout:Scanning Cijfers.kbl]"); Assert(k.Label == "Scanning Cijfers" && k.Send == "layout:Scanning Cijfers.kbl", "[layout:file] switches to a layout file (the label is the file name)"); }
            { var k = TextKey(@"[key:Enter|{ENTER}|En|{ENTER}|x\|y|z\]]"); Assert(k.Label == "Enter" && k.Send == "{ENTER}" && k.ShiftLabel == "En" && k.AltGrLabel == "x|y" && k.AltGrSend == "z]", "[key:…] says every layer (\\| and \\] for those characters)"); }
            { var k = TextKey("[key:ok]"); Assert(k.Label == "ok" && k.Send == "ok", "[key:text] alone: it sends what it shows"); }

            // ── Layers of Shift and AltGr ──
            {
                var k = TextKey("e[shift:É][altgr:€]");
                Assert(k.Label == "e" && k.ShiftLabel == "É" && k.ShiftSend == "É" && k.AltGrLabel == "€" && k.AltGrSend == "€", "e[shift:É][altgr:€]: Shift and AltGr layers attached to the key");
                Assert(TextKey("e[shift:E]").ShiftSend == "E" && TextKey("[A][altgr:@]").Label == "A", "…also on a letter and on [A]");
                Assert(WizardKeyParser.Parse("a[shift:A] b").Count == 1 && WizardKeyParser.Parse("a[shift:A] b")[0].Count == 2, "a layer is part of its key: no space");
                Assert(TextKey("[shift:x]").Label == "[shift:x]" || TextKey("[shift:x]").Label == "shift:x", "a layer on its own (after a space) is just text");
            }

            // ── Sizes ──
            {
                var g = TextGrid("q*2 w [enter]*2x2 e\na s d");
                var q = TextCell(g, 0, 0); var enter = g.Cells.First(c => c.Props.Label == "↵");
                Assert(q.ColSpan == 2 && q.RowSpan == 1 && q.Col == 0, "q*2 is two cells wide");
                Assert(enter.Col == 3 && enter.ColSpan == 2 && enter.RowSpan == 2, "[enter]*2x2 is two wide and two tall, after q (2) and w (1)");
                Assert(g.Cells.First(c => c.Props.Label == "e").Col == 5, "the next key starts after the wide one");
                var d = g.Cells.First(c => c.Props.Label == "d");
                Assert(g.Cells.First(c => c.Props.Label == "a").Col == 0 && d.Row == 1 && d.Col == 2 && g.IsValid(), "the next row skips the cells a tall key covers, and the grid is valid (every square once)");
                Assert(TextKey("a*3").ColSpan == 3 && TextKey("a*3").Label == "a" && TextKey("*").Label == "*" && TextKey("**2").Label == "*" && TextKey("5*3x").Label == "5*3x" && TextKey("ab*x").Label == "ab*x",
                    "a size only counts as *N or *NxM at the end; a lone * is the key *");
                Assert(TextKey("e[shift:É]*2").ColSpan == 2 && TextKey("\"a b\"*3").ColSpan == 3, "…also after a layer or a quoted phrase");
                var clash = TextGrid("a*5 b\nc*9");
                Assert(clash.IsValid(), "sizes that run into another key are cut short: the grid stays valid");
            }

            // ── Build: placement and the settings column ──
            {
                var g = TextGrid("a b\nc");
                Assert(g.Cols == 3 && g.Rows == 2 && g.IsValid() && TextCell(g, 0, 2).Props.Label == "" && TextCell(g, 1, 1).Props.Label == "",
                    "the grid has a settings column and an empty cell where a row is short");
            }

            // ── Export ──
            {
                string text = "[wp] [wp]*2 q w e[shift:É][altgr:€]\n[shift] [ctrl+c] [space]*4 [enter]\n_ \"two words\" 7 . [layout:other.kbl]\n";
                var g = TextGrid(text);
                string back = LayoutText.Export(g);
                Assert(back == text, "export gives back the text it was built from" + (back == text ? "" : $" (got: {back.Replace("\n", "|")})"));
                var g2 = TextGrid(back);
                Assert(g2.Rows == g.Rows && g2.Cols == g.Cols && g2.Cells.Count == g.Cells.Count, "…and building that again gives the same grid");
            }
            {   // A key with any layers at all is exported and read back unchanged.
                var odd = new GridLayout(2, 5);
                void Put(int r, int c, KeyProps p, int rs = 1, int cs = 1) => odd.Cells.Add(new GridCell(r, c, p, rs, cs));
                Put(0, 0, new KeyProps("a", "a", "A", "A"));
                Put(0, 1, new KeyProps("é", "é", "É", "É", "ë", "ë"));
                Put(0, 2, new KeyProps("Enter", "{ENTER}", "En", "{ENTER}", "x|y", "z]"));
                Put(0, 3, new KeyProps("a b", "a b"));
                Put(0, 4, new KeyProps("", ""));
                Put(1, 0, new KeyProps("B", "B"));
                Put(1, 1, new KeyProps("5*3", "5*3"));
                Put(1, 2, new KeyProps("_x", "_x"));
                Put(1, 3, new KeyProps("say \"hi\"", "say \"hi\""));
                Put(1, 4, new KeyProps("Cut", "^x"));
                string text = LayoutText.Export(odd, 0, 4);
                var g = TextGrid(text);
                bool same = true; string why = "";
                foreach (var o in odd.Cells.Where(c => c.Row * 5 + c.Col != 4))
                {
                    var n = TextCell(g, o.Row, o.Col);
                    var a = o.Props; var b = n.Props;
                    if (a.Label != b.Label || a.Send != b.Send || (a.ShiftLabel ?? "") != (b.ShiftLabel ?? "") || (a.ShiftSend ?? "") != (b.ShiftSend ?? "")
                        || (a.AltGrLabel ?? "") != (b.AltGrLabel ?? "") || (a.AltGrSend ?? "") != (b.AltGrSend ?? "")) { same = false; why += $" [{o.Row},{o.Col}] '{a.Label}' → '{b.Label}'/'{b.Send}'"; }
                }
                Assert(same, "every kind of key reads back from the export as the same key" + why + $" — text: {text.Replace("\n", "|")}");
                Assert(text.Contains("[key:"), "…a key the short forms cannot say is written [key:…]");
            }
            {   // The settings button is left out; an empty row stays a row; a tall key does not make a line of its own
                var lay = TextGrid("a b\n_\nc");
                string text = LayoutText.Export(lay);
                Assert(text == "a b\n_\nc\n", "the settings button is not in the text, an empty row is written _");
                var tall = TextGrid("a*1x2 b\nc");
                Assert(LayoutText.Export(tall) == "a*1x2 b\nc\n", "a tall key: the row below lists only its own keys");
            }
        }
    }
}

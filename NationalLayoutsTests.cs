// NationalLayoutsTests.cs — the ready-made keyboards of France, Italy and Spain (azerty_fr.kbl, qwerty_it.kbl, qwerty_es.kbl): the keys are those of
// the Windows layouts (checked against the layouts of the Interactive Comparator of National Layouts), and every file is a sound layout.

using System;
using System.IO;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_NationalLayouts()
        {
            Section("National layouts — French AZERTY, Italian and Spanish QWERTY");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            GridLayout Load(string file, out LayoutMeta meta)
            {
                meta = new LayoutMeta();
                return SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), meta, Path.Combine(baseDir, file));
            }
            KeyProps At(GridLayout l, int r, int c) => l.Cells.First(x => x.Covers(r, c)).Props;
            bool Is(KeyProps p, string label, string shift = "", string altgr = "") =>
                p.Label == label && (p.ShiftLabel ?? "") == shift && (p.AltGrLabel ?? "") == altgr;

            foreach (var (file, lang, wordLabel) in new[] { ("azerty_fr.kbl", "fr", "Mot"), ("qwerty_it.kbl", "it", "Parola"), ("qwerty_es.kbl", "es", "Palabra") })
            {
                var l = Load(file, out var m);
                Assert(l.IsValid() && l.Rows == 7 && l.Cols == 14, $"{file}: a valid grid of 7 × 14");
                Assert(m.Language == lang && m.GearRow == 6 && m.GearCol == 13, $"{file}: the language of the keyboard is '{lang}' and the settings button sits at the bottom right");
                var typed = l.Cells.Where(c => c.Props.Label.Length == 1 && char.IsLetter(c.Props.Label[0]) && !string.IsNullOrEmpty(c.Props.ShiftLabel)).Select(c => c.Props.Label).ToList();
                Assert("abcdefghijklmnopqrstuvwxyz".All(ch => typed.Count(t => t == ch.ToString()) == 1), $"{file}: every letter a to z is on one key");
                Assert(l.Cells.Where(c => c.Props.Label.Length == 1 && char.IsLetter(c.Props.Label[0]) && c.Props.ShiftLabel == c.Props.Label.ToUpperInvariant()).All(c => c.Props.ShiftSend == c.Props.ShiftLabel && c.Props.Send == c.Props.Label),
                    $"{file}: a letter key sends the small letter and the capital with Shift");
                var digits = l.Cells.SelectMany(c => new[] { c.Props.Label, c.Props.ShiftLabel }).Count(s => s != null && s.Length == 1 && char.IsDigit(s[0]));
                Assert(digits == 10, $"{file}: the ten digits are each on one key ({digits})");
                Assert(l.Cells.All(c => (string.IsNullOrEmpty(c.Props.ShiftLabel) == string.IsNullOrEmpty(c.Props.ShiftSend)) && (string.IsNullOrEmpty(c.Props.AltGrLabel) == string.IsNullOrEmpty(c.Props.AltGrSend))),
                    $"{file}: a Shift or AltGr label always has its output");
                Assert(l.Cells.SelectMany(c => new[] { c.Props.Send, c.Props.ShiftSend, c.Props.AltGrSend }).Where(s => s != null && s.StartsWith("dead:")).All(s => s.Length == 6 && WizardKeyParser.DeadChars.IndexOf(s[5]) >= 0),
                    $"{file}: a dead key is one of the five accents the program composes");
                var wp = l.Cells.Where(c => (c.Props.Send ?? "").StartsWith("wp:")).OrderBy(c => c.Col).ToList();
                Assert(wp.Count == 7 && wp.Select(c => c.Props.Send).SequenceEqual(Enumerable.Range(0, 7).Select(i => "wp:" + i)) && wp.All(c => c.Props.Label.StartsWith(wordLabel + " ")),
                    $"{file}: seven word-prediction keys, numbered, labelled in {lang}");
                Assert(l.Cells.Count(c => c.Props.Label == "Shift") == 2 && l.Cells.Any(c => c.Props.Label == "AltGr") && l.Cells.Any(c => c.Props.Label == "Ctrl") && l.Cells.Any(c => c.Props.Label == "↵" && c.RowSpan == 2),
                    $"{file}: Shift, AltGr, Ctrl and the tall Enter are there");
            }

            // ── French AZERTY (France) ──
            {
                var l = Load("azerty_fr.kbl", out _);
                Assert(Is(At(l, 2, 0), "²") && Is(At(l, 2, 1), "&", "1") && Is(At(l, 2, 3), "\"", "3", "#") && Is(At(l, 2, 6), "-", "6", "|") && Is(At(l, 2, 8), "_", "8", "\\") && Is(At(l, 2, 12), "=", "+", "}"),
                    "French: the number row (² & é \" ' ( - è _ ç à ) =) with its Shift and AltGr characters");
                Assert(At(l, 2, 2).AltGrSend == "dead:~" && At(l, 2, 7).AltGrSend == "dead:`" && Is(At(l, 2, 9), "ç", "9", "^") && Is(At(l, 2, 10), "à", "0", "@"),
                    "French: AltGr+é is the dead tilde, AltGr+è the dead grave accent, AltGr+ç ^ and AltGr+à @");
                Assert(Is(At(l, 3, 1), "a", "A") && Is(At(l, 3, 2), "z", "Z") && Is(At(l, 3, 3), "e", "E", "€") && Is(At(l, 4, 1), "q", "Q") && Is(At(l, 5, 2), "w", "W"), "French: a z e r t y u i o p / q s d f g h j k l m / w x c v b n");
                Assert(At(l, 3, 11).Send == "dead:^" && At(l, 3, 11).ShiftSend == "dead:¨" && Is(At(l, 3, 12), "$", "£", "¤"), "French: the dead circumflex and diaeresis, and $ £ ¤");
                Assert(Is(At(l, 4, 11), "ù", "%") && Is(At(l, 4, 12), "*", "µ") && Is(At(l, 5, 1), "<", ">") && Is(At(l, 5, 8), ",", "?") && Is(At(l, 5, 9), ";", ".") && Is(At(l, 5, 10), ":", "/") && Is(At(l, 5, 11), "!", "§"),
                    "French: ù % * µ < > , ? ; . : / ! §");
            }
            // ── Italian QWERTY ──
            {
                var l = Load("qwerty_it.kbl", out _);
                Assert(Is(At(l, 2, 0), "\\", "|") && Is(At(l, 2, 3), "3", "£") && Is(At(l, 2, 11), "'", "?") && Is(At(l, 2, 12), "ì", "^"), "Italian: \\ | 1 ! 2 \" 3 £ … ' ? ì ^");
                Assert(Is(At(l, 3, 1), "q", "Q") && Is(At(l, 3, 2), "w", "W") && Is(At(l, 3, 3), "e", "E", "€") && Is(At(l, 5, 2), "z", "Z"), "Italian: QWERTY letters (q w e … / a s d … / z x c …), € on AltGr+e");
                Assert(Is(At(l, 3, 11), "è", "é", "[") && Is(At(l, 3, 12), "+", "*", "]") && Is(At(l, 4, 10), "ò", "ç", "@") && Is(At(l, 4, 11), "à", "°", "#") && Is(At(l, 4, 12), "ù", "§"),
                    "Italian: è é [ , + * ] , ò ç @ , à ° # , ù §");
                Assert(!l.Cells.Any(c => (c.Props.Send ?? "").StartsWith("dead:") || (c.Props.ShiftSend ?? "").StartsWith("dead:") || (c.Props.AltGrSend ?? "").StartsWith("dead:")), "Italian: no dead keys (the accents have their own keys)");
            }
            // ── Spanish QWERTY ──
            {
                var l = Load("qwerty_es.kbl", out _);
                Assert(Is(At(l, 2, 0), "º", "ª", "\\") && Is(At(l, 2, 1), "1", "!", "|") && Is(At(l, 2, 2), "2", "\"", "@") && Is(At(l, 2, 3), "3", "·", "#") && Is(At(l, 2, 6), "6", "&", "¬") && Is(At(l, 2, 12), "¡", "¿"),
                    "Spanish: º ª \\ , 1 ! | , 2 \" @ , 3 · # , 6 & ¬ , ¡ ¿");
                Assert(At(l, 2, 4).AltGrSend == "dead:~" && At(l, 3, 11).Send == "dead:`" && At(l, 3, 11).ShiftSend == "dead:^" && At(l, 4, 11).Send == "dead:´" && At(l, 4, 11).ShiftSend == "dead:¨",
                    "Spanish: the dead tilde (AltGr+4), grave and circumflex, acute and diaeresis");
                Assert(Is(At(l, 4, 10), "ñ", "Ñ") && Is(At(l, 4, 12), "ç", "Ç", "}") && Is(At(l, 4, 11), "´", "¨", "{") && Is(At(l, 3, 11), "`", "^", "[") && Is(At(l, 3, 12), "+", "*", "]"), "Spanish: ñ, ç, { } [ ] and the accents");
            }

            // ── Through the program: the titles in the language of the screens, and Belgian / French AZERTY told apart ──
            var fr = LayoutInfo.FromFolder(baseDir, "fr");
            Assert(fr.Any(i => i.Title == "AZERTY (France)") && fr.Any(i => i.Title == "AZERTY (Belgique)") && fr.Any(i => i.Title == "QWERTY (italien)") && fr.Any(i => i.Title == "QWERTY (espagnol)"),
                "the list of keyboards names the Belgian and the French AZERTY apart, and the Italian and Spanish QWERTY");
        }
    }
}

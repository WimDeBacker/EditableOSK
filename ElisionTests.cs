// ElisionTests.cs — French and Italian elision in the word predictor: "l'", "qu'", "dell'" are words of their own (the database has them with
// the apostrophe), the word after them is predicted from them, and choosing one adds no space. A database without such words (Dutch, English,
// Spanish) treats an apostrophe as ordinary punctuation, as before.

using System;
using System.IO;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_Elision()
        {
            Section("Word prediction — elision (l', qu', aujourd'hui)");
            string dir = Path.Combine(Path.GetTempPath(), "osk_elision_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string fr = Path.Combine(dir, "worddb_FR.wfq"), nl = Path.Combine(dir, "worddb_NL.wfq");
                File.WriteAllText(fr,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<WordDatabase version=\"1\" language=\"fr\">\n" +
                    "  <Word value=\"l'\" frequency=\"500\"><Next value=\"homme\" frequency=\"50\" /><Next value=\"eau\" frequency=\"40\" /><Next value=\"on\" frequency=\"30\" /></Word>\n" +
                    "  <Word value=\"qu'\" frequency=\"400\"><Next value=\"il\" frequency=\"40\" /></Word>\n" +
                    "  <Word value=\"le\" frequency=\"300\"><Next value=\"monde\" frequency=\"20\" /></Word>\n" +
                    "  <Word value=\"il\" frequency=\"250\"><Next value=\"est\" frequency=\"20\" /></Word>\n" +
                    "  <Word value=\"homme\" frequency=\"100\"><Next value=\"est\" frequency=\"9\" /></Word>\n" +
                    "  <Word value=\"eau\" frequency=\"80\" />\n" +
                    "  <Word value=\"aujourd'hui\" frequency=\"60\"><Next value=\"je\" frequency=\"5\" /></Word>\n" +
                    "</WordDatabase>", System.Text.Encoding.UTF8);
                File.WriteAllText(nl,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<WordDatabase version=\"1\" language=\"nl\">\n" +
                    "  <Word value=\"de\" frequency=\"500\"><Next value=\"man\" frequency=\"50\" /></Word>\n" +
                    "  <Word value=\"man\" frequency=\"100\" />\n" +
                    "  <Word value=\"x\" frequency=\"10\" />\n" +
                    "</WordDatabase>", System.Text.Encoding.UTF8);

                WordDatabase.Load(fr);
                Assert(WordDatabase.IsLoaded && WordDatabase.IsElisionToken("l'") && WordDatabase.IsElisionToken("L'") && !WordDatabase.IsElisionToken("le") && !WordDatabase.IsElisionToken("aujourd'") &&
                       WordDatabase.HasWordStartingWith("aujourd'") && WordDatabase.HasWordStartingWith("AUJOURD'") && !WordDatabase.HasWordStartingWith("zz'"),
                    "the database knows its elided words (ending in an apostrophe) and the words that have an apostrophe inside");

                WordPredictor Typed(string text, bool sentenceStart = false)
                {
                    var p = new WordPredictor(5);
                    if (sentenceStart) p.OnSentenceStart();
                    foreach (char c in text) p.OnKeySent(c.ToString(), false);
                    return p;
                }
                bool Has(WordPredictor p, string w) => p.Predictions.Any(x => string.Equals(x, w, StringComparison.Ordinal));

                // "l'" is a completed word; the word after it is predicted from it.
                var a = Typed("l");
                Assert(Has(a, "l'"), "typing \"l\": the elided word l' is among the suggestions");
                a.OnKeySent("'", false);
                Assert(a.WordBuffer == "" && a.LastCompletedWord == "l'", "the apostrophe after \"l\" finishes the word l' (no space added)");
                Assert(Has(a, "homme") && Has(a, "eau") && Has(a, "on") && a.Predictions[0] == "homme", "…and the words that follow l' are suggested (homme, eau, on)");
                a.OnKeySent("h", false);
                Assert(a.Predictions[0] == "homme" && !Has(a, "eau"), "…narrowed by what is typed next");
                var click = a.OnWPClick(0);
                Assert(click.Backspaces == 1 && click.Word == "homme" && click.Suffix == " " && a.LastActionWasPrediction, "choosing homme replaces the typed h and adds a space");

                // Choosing an elided word: no space, and nothing to take away before punctuation.
                var b = Typed("l");
                var c1 = b.OnWPClick(Array.IndexOf(b.Predictions.ToArray(), "l'"));
                Assert(c1 != null && c1.Backspaces == 1 && c1.Word == "l'" && c1.Suffix == "" && !b.LastActionWasPrediction && b.LastCompletedWord == "l'",
                    "choosing l' replaces the typed l, adds NO space and leaves nothing to remove before punctuation");
                Assert(Has(b, "homme"), "…and the next words are those of l'");
                var d = new WordPredictor(5); d.RefreshNow();
                var c2 = d.OnWPClick(Array.IndexOf(d.Predictions.ToArray(), "l'"));
                Assert(c2 != null && c2.Suffix == "" && c2.Backspaces == 0, "an elided word chosen at the start of a word: nothing to erase, no space");

                // An apostrophe inside a longer word: the apostrophe belongs to the word.
                var e = Typed("aujourd");
                e.OnKeySent("'", false);
                Assert(e.WordBuffer == "aujourd'" && e.LastCompletedWord == "", "an apostrophe inside aujourd'hui is part of the word being typed");
                e.OnKeySent("h", false);
                Assert(e.Predictions[0] == "aujourd'hui", "…and the whole word is suggested");

                // The typographic apostrophe is the same; a capital at the start of a sentence too.
                var f = Typed("l");
                f.OnKeySent("’", false);
                Assert(f.LastCompletedWord == "l'" && f.WordBuffer == "", "a typographic apostrophe ’ is read as the apostrophe");
                var g = new WordPredictor(5); g.OnSentenceStart();
                g.OnKeySent("L", false); g.OnKeySent("'", false);
                Assert(g.LastCompletedWord == "L'" && g.Predictions.Take(3).SequenceEqual(new[] { "Homme", "Eau", "On" }),
                    "at the start of a sentence L' is a word too; what follows it is suggested as after any first word (still capitalised until a letter is typed)");
                g.OnKeySent("h", false);
                Assert(g.Predictions[0] == "homme", "…and the word typed after L' is not capitalised");

                // Not an elision: any other letter plus an apostrophe is punctuation, as before.
                var h = Typed("x");
                h.OnKeySent("'", false);
                Assert(h.WordBuffer == "" && h.LastCompletedWord == "x", "an apostrophe after letters that are no elided word still ends the word");

                // A database without elided words never changes how an apostrophe works.
                WordDatabase.Load(nl);
                Assert(!WordDatabase.IsElisionToken("l'") && !WordDatabase.HasWordStartingWith("x'"), "a Dutch database has no elided words");
                var i = Typed("x");
                i.OnKeySent("'", false);
                Assert(i.WordBuffer == "" && i.LastCompletedWord == "x", "Dutch: an apostrophe ends the word as before");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}

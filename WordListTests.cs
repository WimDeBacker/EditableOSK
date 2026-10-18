// WordListTests.cs — the French, Italian and Spanish word lists (made by tools/WordDbBuilder from Tatoeba and Europarl): they ship, load, know the words of
// everyday conversation, and work with the predictor (elided words in French and Italian).

using System;
using System.IO;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_WordLists()
        {
            Section("Word lists — French, Italian and Spanish");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var registry = new LanguageRegistry(baseDir).All.Select(d => d.Language.ToLowerInvariant()).ToList();
            Assert(new[] { "nl", "fr", "it", "es" }.All(registry.Contains), "the program finds a word list for Dutch, French, Italian and Spanish");

            foreach (var (code, file) in new[] { ("fr", "worddb_FR.wfq"), ("it", "worddb_IT.wfq"), ("es", "worddb_ES.wfq") })
            {
                string shipped = Path.Combine(baseDir, file);
                Assert(File.Exists(shipped), $"{file} is present next to the test executable");
                if (!File.Exists(shipped)) continue;
                string dir = ShippedDbCopy.Create(shipped, out string copy);
                try
                {
                    WordDatabase.Load(copy);
                    Assert(WordDatabase.IsLoaded && WordDatabase.Language == code && WordDatabase.WordCount >= 15000, $"{file}: loads as '{code}' with its words ({WordDatabase.WordCount})");
                    bool Has(System.Collections.Generic.List<string> l, string w) => l.Any(x => string.Equals(x, w, StringComparison.Ordinal));
                    bool Starts(System.Collections.Generic.List<string> l, string p) => l.Any(x => x.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                    WordPredictor Typed(string text)
                    {
                        var p = new WordPredictor(7);
                        foreach (char c in text) p.OnKeySent(c.ToString(), false);
                        return p;
                    }

                    if (code == "fr")
                    {
                        Assert(Has(WordDatabase.GetPredictions("", "bonj", false, 7), "bonjour"), "French: bonj → bonjour");
                        Assert(Has(WordDatabase.GetPredictions("merci", "", false, 7), "beaucoup") && Has(WordDatabase.GetPredictions("je", "", false, 7), "suis"), "French: merci → beaucoup, je → suis");
                        Assert(!Starts(WordDatabase.GetPredictions("", "putai", false, 7), "putain"), "French: a vulgar word on the blocked list is not suggested");
                        var p = Typed("j'");
                        Assert(p.LastCompletedWord == "j'" && p.WordBuffer == "" && p.Predictions.Contains("ai"), "French: j' is a word of its own, and ai follows it");
                        p = Typed("l'h");
                        Assert(p.Predictions[0] == "homme", "French: l'h → homme (the elided word is the context)");
                        var click = p.OnWPClick(0);
                        Assert(click.Backspaces == 1 && click.Word == "homme" && click.Suffix == " ", "French: choosing homme replaces the h and adds a space");
                        p = Typed("aujourd'");
                        Assert(p.WordBuffer == "aujourd'" && p.Predictions[0] == "aujourd'hui", "French: aujourd' is the start of aujourd'hui, not the end of a word");
                        p = Typed("qu");
                        var c1 = p.OnWPClick(Array.IndexOf(p.Predictions.ToArray(), "qu'"));
                        Assert(c1 != null && c1.Word == "qu'" && c1.Suffix == "", "French: qu' is suggested, and chosen without a space");
                    }
                    else if (code == "it")
                    {
                        Assert(Starts(WordDatabase.GetPredictions("", "buon", false, 7), "buon"), "Italian: buon → a word that starts with buon");
                        Assert(Has(WordDatabase.GetPredictions("grazie", "", false, 7), "mille") && Has(WordDatabase.GetPredictions("grazie", "", false, 7), "per"), "Italian: grazie → mille, per");
                        var p = Typed("c'");
                        Assert(p.LastCompletedWord == "c'" && p.Predictions.Contains("è"), "Italian: c' is a word of its own, and è follows it");
                        p = Typed("dell'");
                        Assert(p.LastCompletedWord == "dell'", "Italian: dell' is a word of its own");
                        Assert(!Starts(WordDatabase.GetPredictions("", "cazzat", false, 10), "cazzat") && !Starts(WordDatabase.GetPredictions("", "cazzo", false, 10), "cazzo"),
                            "Italian: a vulgar word on the blocked list is not suggested (cazzuola, a trowel, is a word)");
                    }
                    else
                    {
                        Assert(Has(WordDatabase.GetPredictions("", "grac", false, 7), "gracias"), "Spanish: grac → gracias");
                        Assert(Has(WordDatabase.GetPredictions("buenos", "", false, 7), "días") && Has(WordDatabase.GetPredictions("tengo", "", false, 7), "que"), "Spanish: buenos → días, tengo → que");
                        var p = Typed("d'");
                        Assert(p.WordBuffer == "" && p.LastCompletedWord == "d", "Spanish: an apostrophe ends the word as before (no elision)");
                        Assert(!Starts(WordDatabase.GetPredictions("", "mierd", false, 7), "mierda"), "Spanish: a vulgar word on the blocked list is not suggested");
                    }
                }
                finally { ShippedDbCopy.Delete(dir); }
            }
        }
    }
}

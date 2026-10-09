// LearnedImportTests.cs — importing learned words (todo Priority 4): a file made with Export is MERGED into the learned words of this PC.
//
//   • counts of words and word pairs are added, new words / pairs / candidates are taken over, nothing learned here is lost
//   • the file is refused (and nothing changes) when it is not an overlay, is unreadable, empty, or of another language
//   • the merged file loads again as a word database overlay

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_LearnedImport()
        {
            Section("Learned words — import merges a file made with Export into this PC's");

            string dir = Path.Combine(Path.GetTempPath(), "osk_import_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string basePath = Path.Combine(dir, "t.wfq");
            string overlay  = WordDatabase.GetOverlayPath(basePath);
            string imp      = Path.Combine(dir, "imp.wfq");
            const string head = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";
            void Write(string path, string xml) => File.WriteAllText(path, xml, System.Text.Encoding.UTF8);

            string mine =
                head + "<WordDatabaseOverlay version=\"1\" language=\"nl\">" +
                "<Candidates><Candidate value=\"kapstok\" count=\"1\"/><Candidate value=\"tuinhek\" count=\"2\"/></Candidates>" +
                "<PersonalUse value=\"de\" count=\"3\"/>" +
                "<NewWord value=\"wasbeer\" frequency=\"4\" personalUse=\"2\"><Next value=\"is\" frequency=\"1\" personalUse=\"1\"/></NewWord>" +
                "<PairUse word=\"de\" next=\"man\" count=\"2\"/>" +
                "<NewPair word=\"de\" next=\"boom\" frequency=\"1\" personalUse=\"1\"/>" +
                "</WordDatabaseOverlay>";
            string theirs =
                head + "<WordDatabaseOverlay version=\"1\" language=\"nl\">" +
                "<Candidates><Candidate value=\"kapstok\" count=\"2\"/><Candidate value=\"wasbeer\" count=\"1\"/></Candidates>" +
                "<PersonalUse value=\"de\" count=\"4\"/><PersonalUse value=\"het\" count=\"1\"/>" +
                "<NewWord value=\"wasbeer\" frequency=\"3\" personalUse=\"1\"><Next value=\"is\" frequency=\"2\" personalUse=\"2\"/><Next value=\"eet\" frequency=\"1\" personalUse=\"0\"/></NewWord>" +
                "<NewWord value=\"fietspomp\" frequency=\"2\" personalUse=\"2\"/>" +
                "<PairUse word=\"de\" next=\"man\" count=\"5\"/>" +
                "<NewPair word=\"de\" next=\"boom\" frequency=\"2\" personalUse=\"2\"/>" +
                "</WordDatabaseOverlay>";
            try
            {
                // ── The merge ──
                Write(overlay, mine); Write(imp, theirs);
                var r = WordDatabase.MergeLearned(basePath, "nl", imp);
                Assert(r.Ok && r.Error == null, "a valid file of the same language is merged");
                Assert(r.Words == 4 && r.Pairs == 4 && r.Candidates == 2, $"the result says what the file held: {r.Words} words, {r.Pairs} pairs, {r.Candidates} candidates");
                var root = XDocument.Load(overlay).Root;
                Assert((string)root.Attribute("language") == "nl", "the merged file names the language");
                int Cand(string w) => (int?)root.Descendants("Candidate").FirstOrDefault(e => (string)e.Attribute("value") == w)?.Attribute("count") ?? -1;
                Assert(Cand("kapstok") == 3 && Cand("tuinhek") == 2, "candidates: counts added (1 + 2), a candidate only here stays");
                Assert(Cand("wasbeer") == -1, "a candidate that is a word now is no candidate any more");
                int Use(string w) => (int?)root.Elements("PersonalUse").FirstOrDefault(e => (string)e.Attribute("value") == w)?.Attribute("count") ?? -1;
                Assert(Use("de") == 7 && Use("het") == 1, "personal use: counts added (3 + 4), a word only in the file is taken over");
                var wasbeer = root.Elements("NewWord").FirstOrDefault(e => (string)e.Attribute("value") == "wasbeer");
                Assert(wasbeer != null && (int)wasbeer.Attribute("frequency") == 7 && (int)wasbeer.Attribute("personalUse") == 3, "a new word in both: frequency and use added");
                var nexts = wasbeer.Elements("Next").ToDictionary(e => (string)e.Attribute("value"), e => ((int)e.Attribute("frequency"), (int)e.Attribute("personalUse")));
                Assert(nexts.Count == 2 && nexts["is"] == (3, 3) && nexts["eet"] == (1, 0), "its word pairs: added where both have them, taken over where one has them");
                Assert(root.Elements("NewWord").Any(e => (string)e.Attribute("value") == "fietspomp" && (int)e.Attribute("personalUse") == 2), "a new word only in the file is taken over");
                Assert((int)root.Elements("PairUse").First().Attribute("count") == 7, "pair use: counts added (2 + 5)");
                var np = root.Elements("NewPair").First();
                Assert((int)np.Attribute("frequency") == 3 && (int)np.Attribute("personalUse") == 3, "a new pair in both: frequency and use added");
                Assert(File.Exists(overlay + ".bak") && File.ReadAllText(overlay + ".bak") == mine, "the file as it was is kept as .bak");

                // ── The merged file is a normal overlay: it loads ──
                Write(basePath, head + "<WordDatabase version=\"1\" language=\"nl\"><Word value=\"de\" frequency=\"100\"/><Word value=\"man\" frequency=\"50\"/><Word value=\"boom\" frequency=\"40\"/></WordDatabase>");
                WordDatabase.LearningEnabled = true;
                WordDatabase.Load(basePath);
                Assert(WordDatabase.IsLoaded && WordDatabase.IsLoadedFor(basePath), "the merged overlay loads on its base database (IsLoadedFor says so)");
                Assert(WordDatabase.GetCandidates().Any(c => c.Word == "kapstok" && c.Count == 3), "…and the candidates are there with the added counts");
                Assert(WordDatabase.IsLoadedFor(Path.Combine(dir, "other.wfq")) == false, "IsLoadedFor is false for another database");

                // ── A file that makes nothing change ──
                void Refused(string name, string expected, Func<WordDatabase.LearnedImportResult> act, string targetBefore)
                {
                    var res = act();
                    Assert(!res.Ok && res.Error == expected, $"{name}: refused ({res.Error})");
                    Assert(File.ReadAllText(overlay) == targetBefore, $"{name}: the learned words are untouched");
                }
                string before = File.ReadAllText(overlay);
                Write(imp, theirs.Replace("language=\"nl\"", "language=\"en\""));
                Refused("another language", "language", () => WordDatabase.MergeLearned(basePath, "nl", imp), before);
                Assert(WordDatabase.MergeLearned(basePath, "nl", imp).Detail == "en", "another language: the message can name the language of the file");
                Write(imp, head + "<WordDatabase version=\"1\" language=\"nl\"><Word value=\"x\" frequency=\"1\"/></WordDatabase>");
                Refused("a base database, not learned words", "notOverlay", () => WordDatabase.MergeLearned(basePath, "nl", imp), before);
                Write(imp, "this is not xml <<<");
                Refused("a corrupt file", "corrupt", () => WordDatabase.MergeLearned(basePath, "nl", imp), before);
                Write(imp, head + "<WordDatabaseOverlay version=\"1\" language=\"nl\"><Candidates/></WordDatabaseOverlay>");
                Refused("a file without learned words", "empty", () => WordDatabase.MergeLearned(basePath, "nl", imp), before);
                Refused("a file that does not exist", "missing", () => WordDatabase.MergeLearned(basePath, "nl", Path.Combine(dir, "nothing.wfq")), before);

                // Files from before the language was written have none: accepted.
                Write(imp, theirs.Replace(" language=\"nl\"", ""));
                Assert(WordDatabase.MergeLearned(basePath, "nl", imp).Ok, "a file without a language attribute (made by an older version) is accepted");

                // This PC's own file unreadable: never written over. (Another database is loaded now: for the loaded one the words in memory
                // are saved first, which replaces the file by what the program knows.)
                string otherBase = Path.Combine(dir, "o.wfq");
                Write(otherBase, head + "<WordDatabase version=\"1\" language=\"nl\"><Word value=\"de\" frequency=\"100\"/></WordDatabase>");
                WordDatabase.Load(otherBase);
                Assert(!WordDatabase.IsLoadedFor(basePath), "another database is loaded now");
                Write(overlay, "garbage <<<");
                Write(imp, theirs);
                var bad = WordDatabase.MergeLearned(basePath, "nl", imp);
                Assert(!bad.Ok && bad.Error == "targetCorrupt" && File.ReadAllText(overlay) == "garbage <<<", "the learned words on this PC cannot be read: refused, and the file is not written over");

                // Nothing learned yet on this PC: the file is created from the import.
                File.Delete(overlay);
                var fresh = WordDatabase.MergeLearned(basePath, "nl", imp);
                Assert(fresh.Ok && File.Exists(overlay) && XDocument.Load(overlay).Root.Elements("NewWord").Count() == 2, "no learned words here yet: the file is created from the import");

                // The words in memory go into the file first when the database is loaded: what was learned a moment ago is not lost.
                WordDatabase.Load(basePath);
                WordDatabase.RecordWord(null, "okapi");                   // only in memory
                Write(imp, theirs);
                Assert(WordDatabase.MergeLearned(basePath, "nl", imp).Ok, "merge into the loaded database");
                Assert(File.ReadAllText(overlay).Contains("okapi"), "…the word learned a moment ago (not saved yet) is in the file too");

                // A save writes the language too (so a later Export can be checked on import).
                WordDatabase.Load(basePath);
                WordDatabase.RecordWord(null, "zebra");
                WordDatabase.SaveNow();
                Assert((string)XDocument.Load(overlay).Root.Attribute("language") == "nl", "a normal save writes the language of the database into the overlay");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}

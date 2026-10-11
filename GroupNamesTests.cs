// GroupNamesTests.cs — style groups with an id and names per language, the language of the keyboard apart from the language of the screens
// (todo Priority 4, steps A and B).
//
//   • which name is shown, ids made from names
//   • the file format: a round trip, an older file (one name per group) read as ids and names, the ready-made layouts
//   • the language of the screens is the user's own (UserSettings); the keyboard has a language of its own (editor, word database)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static Dictionary<string, string> Names(params (string lang, string name)[] n) =>
            n.ToDictionary(x => x.lang, x => x.name, StringComparer.OrdinalIgnoreCase);

        private static void T_GroupNames()
        {
            Section("Style groups — id and names per language");

            // ── Which name is shown: screens → keyboard → English → first → the standard group's own name → the id ──
            var g = new KeyGroup { Name = "vowels", Names = Names(("nl", "Klinkers"), ("en", "Vowels"), ("fr", "Voyelles")) };
            Assert(GroupNames.Display(g, "nl", "fr") == "Klinkers", "the language of the screens comes first");
            Assert(GroupNames.Display(g, "de", "fr") == "Voyelles", "else the language of the keyboard");
            Assert(GroupNames.Display(g, "de", "pt") == "Vowels", "else English");
            Assert(GroupNames.Display(new KeyGroup { Name = "x", Names = Names(("fr", "Voyelles")) }, "de", "pt") == "Voyelles", "else the first name there is");
            Assert(GroupNames.Display(new KeyGroup { Name = "vowels" }, "nl", "nl") == "vowels", "no names: the id");
            Assert(GroupNames.Display(g, "nl-BE", "") == "Klinkers", "a regional tag finds the name of its language (nl-BE → nl)");
            Assert(GroupNames.Display(new KeyGroup { Name = "x", Names = Names(("nl-BE", "Klinkers BE")) }, "nl", "") == "Klinkers BE", "…and the other way round (nl → a regional name of nl)");
            string was = Lang.CurrentCode;
            try
            {
                Lang.Load("nl");
                Assert(GroupNames.Display(new KeyGroup { Name = SettingsManager.StandardGroupName }) == "Standaard", "the standard group without names is shown as 'Standaard' in Dutch");
                Lang.Load("en");
                Assert(GroupNames.Display(new KeyGroup { Name = SettingsManager.StandardGroupName }) == "Standard", "…and 'Standard' in English");
            }
            finally { Lang.Load(was); }

            // ── Ids ──
            Assert(GroupNames.Slug("Function keys") == "function-keys" && GroupNames.Slug("Één  café!") == "een-cafe" && GroupNames.Slug("???") == "group", "an id is made of lower-case letters and digits joined by '-'");
            Assert(GroupNames.UniqueId("Vowels", new[] { "vowels", "vowels-2" }) == "vowels-3", "a taken id gets a number");
            Assert(GroupNames.UniqueId("Standard", new string[0]) != SettingsManager.StandardGroupName, "an id is never the reserved 'standard'");

            // ── Round trip ──
            string dir = Path.Combine(Path.GetTempPath(), "osk_gn_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "t.kbl");
                var layout = new GridLayout(1, 2);
                layout.Groups.Add(new KeyGroup { Name = "standard", KeyColor = System.Drawing.Color.White });
                layout.Groups.Add(new KeyGroup { Name = "vowels", Names = Names(("nl", "Klinkers"), ("en", "Vowels")), KeyColor = System.Drawing.Color.Red });
                layout.Cells.Add(new GridCell(0, 0, new KeyProps("a", "a") { GroupName = "vowels" }));
                layout.Cells.Add(new GridCell(0, 1, new KeyProps("b", "b") { GroupName = "standard" }));
                SettingsManager.SaveSettings(layout, new VisualTheme(), new WindowState(), new LayoutMeta { Language = "nl-BE" }, path);
                string xml = File.ReadAllText(path);
                Assert(xml.Contains("FormatVersion=\"2\"") && xml.Contains("<Group Id=\"vowels\"") && xml.Contains("GroupId=\"vowels\"") && !xml.Contains("GroupName="),
                    "the file says FormatVersion 2, and groups and keys use ids");
                var meta = new LayoutMeta();
                var loaded = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), meta, path);
                Assert(meta.Language == "nl-BE", "the language tag of the keyboard (with region) survives");
                var lg = loaded.Groups.First(x => x.Name == "vowels");
                Assert(lg.Names["nl"] == "Klinkers" && lg.Names["en"] == "Vowels", "the names of a group survive");
                Assert(loaded.Cells.First(c => c.Props.Label == "a").Props.GroupName == "vowels", "a key keeps pointing at its group");

                // ── An older file: one name per group, keys point at it by that name ──
                string old = Path.Combine(dir, "old.kbl");
                File.WriteAllText(old,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<OnScreenKeyboard>\n  <Theme BackgroundColor=\"1A1A2E\">\n" +
                    "    <Group Name=\"standard\" KeyColor=\"FFFFFF\" />\n" +
                    "    <Group Name=\"Klinkers\" KeyColor=\"FF0000\" />\n" +
                    "    <Group Name=\"Woord\" KeyColor=\"00FF00\" />\n" +
                    "    <Group Name=\"Mijn groep\" KeyColor=\"0000FF\" />\n" +
                    "  </Theme>\n  <Layout Language=\"nl\" GridRows=\"1\" GridCols=\"4\">\n" +
                    "    <Key Row=\"0\" Col=\"0\" GroupName=\"Klinkers\" Label=\"a\" Send=\"a\" />\n" +
                    "    <Key Row=\"0\" Col=\"1\" GroupName=\"Woord\" Label=\"w\" Send=\"wp:0\" />\n" +
                    "    <Key Row=\"0\" Col=\"2\" GroupName=\"Mijn groep\" Label=\"m\" Send=\"m\" />\n" +
                    "    <Key Row=\"0\" Col=\"3\" GroupName=\"standard\" Label=\"s\" Send=\"s\" />\n" +
                    "  </Layout>\n</OnScreenKeyboard>");
                var o = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), old);
                Assert(o.Groups.Select(x => x.Name).SequenceEqual(new[] { "standard", "vowels", "prediction", "mijn-groep" }),
                    "an older file: the ids are made from the names (ready-made groups get their id)");
                Assert(o.Groups.First(x => x.Name == "vowels").Names["nl"] == "Klinkers" && o.Groups.First(x => x.Name == "vowels").Names["en"] == "Vowels",
                    "an older file: a ready-made group gets its Dutch and English name");
                Assert(o.Groups.First(x => x.Name == "prediction").Names["nl"] == "Woordvoorspelling", "an older file: 'Woord' becomes the word prediction group");
                var mine = o.Groups.First(x => x.Name == "mijn-groep");
                Assert(mine.Names.Count == 1 && mine.Names["nl"] == "Mijn groep", "an older file: another group keeps its name for the language of the keyboard");
                Assert(o.Groups.First(x => x.Name == "standard").Names.Count == 0, "an older file: the standard group gets no names (it is shown by the program)");
                Assert(o.Cells.First(c => c.Props.Label == "a").Props.GroupName == "vowels" && o.Cells.First(c => c.Props.Label == "w").Props.GroupName == "prediction" &&
                       o.Cells.First(c => c.Props.Label == "m").Props.GroupName == "mijn-groep" && o.Cells.First(c => c.Props.Label == "s").Props.GroupName == "standard",
                    "an older file: the keys follow their group to its id");
                Assert(SettingsManager.LoadGroupsFromFile(old).Select(x => x.Name).SequenceEqual(new[] { "standard", "vowels", "prediction", "mijn-groep" }),
                    "importing groups from an older file gives the same ids");

                // ── The ready-made layouts: every key points at a group that exists, and every group has its names ──
                foreach (string file in new[] { "azerty.kbl", "azertycolor.kbl", "qwerty.kbl", "math.kbl" })
                {
                    string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
                    if (!File.Exists(p)) { Assert(false, $"{file}: not found next to the test binary"); continue; }
                    string text = File.ReadAllText(p);
                    Assert(text.Contains("FormatVersion=\"2\"") && !text.Contains("GroupName=") && !text.Contains("<Group Name="), $"{file}: is in the current format");
                    var m = new LayoutMeta();
                    var l = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), m, p);
                    var ids = new HashSet<string>(l.Groups.Select(x => x.Name));
                    Assert(l.Cells.All(c => string.IsNullOrEmpty(c.Props.GroupName) || ids.Contains(c.Props.GroupName)), $"{file}: every key points at a group that exists");
                    Assert(l.Groups.Where(x => x.Name != SettingsManager.StandardGroupName).All(x => x.Names.ContainsKey("nl") && x.Names.ContainsKey("en")),
                        $"{file}: every group has a Dutch and an English name");
                    Assert(l.Groups.Select(x => x.Name).Distinct().Count() == l.Groups.Count, $"{file}: the ids are unique");
                    Assert(!string.IsNullOrEmpty(m.Language), $"{file}: the keyboard has a language");
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static void T_KeyboardLanguage()
        {
            Section("Language of the screens (per user) and language of the keyboard (per layout)");

            // ── UserSettings ──
            var available = new List<string> { "en", "nl" };
            Assert(UserSettings.Default(new CultureInfo("nl-BE"), available) == "nl", "the language of Windows is the first value when the program is translated into it");
            Assert(UserSettings.Default(new CultureInfo("fr-FR"), available) == "en", "…English when it is not");
            string saved = UserData.DirectoryOverride;
            string dir = Path.Combine(Path.GetTempPath(), "osk_us_" + Guid.NewGuid().ToString("N"));
            try
            {
                UserData.DirectoryOverride = dir;
                UserSettings.Reset();
                string first = UserSettings.InterfaceLanguage;
                Assert(first == UserSettings.Default(), "no file: the default");
                UserSettings.InterfaceLanguage = "nl";
                Assert(File.Exists(Path.Combine(dir, "user.xml")), "choosing a language saves it in the user's folder");
                UserSettings.Reset();
                Assert(UserSettings.InterfaceLanguage == "nl", "…and it comes back after a restart");
                File.WriteAllText(Path.Combine(dir, "user.xml"), "<UserSettings InterfaceLanguage=\"zz\" />");
                UserSettings.Reset();
                Assert(UserSettings.InterfaceLanguage == UserSettings.Default(), "a saved language the program cannot load falls back to the default");
                File.WriteAllText(Path.Combine(dir, "user.xml"), "not xml");
                UserSettings.Reset();
                Assert(UserSettings.InterfaceLanguage == UserSettings.Default(), "a damaged file does not stop the program");
                // The language of the screens is not in a layout file.
                string kbl = Path.Combine(dir, "x.kbl");
                SettingsManager.SaveSettings(new GridLayout(1, 1) { Cells = { new GridCell(0, 0, new KeyProps("", "")) }, Groups = { new KeyGroup { Name = "standard" } } },
                    new VisualTheme(), new WindowState(), new LayoutMeta { Language = "fr-BE" }, kbl);
                Assert(!File.ReadAllText(kbl).Contains("InterfaceLanguage"), "a layout file carries the language of the keyboard, not the user's language of the screens");
            }
            finally { UserData.DirectoryOverride = saved; UserSettings.Reset(); try { Directory.Delete(dir, true); } catch { } }

            // ── Word database by language tag ──
            string dbDir = Path.Combine(Path.GetTempPath(), "osk_lr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dbDir);
            try
            {
                foreach (var (file, lang) in new[] { ("a_nl.wfq", "nl"), ("b_nlbe.wfq", "nl-BE"), ("c_en.wfq", "en") })
                    File.WriteAllText(Path.Combine(dbDir, file), $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<WordDatabase version=\"1\" language=\"{lang}\">\r\n  <Word value=\"x\" frequency=\"1\" />\r\n</WordDatabase>");
                var reg = new LanguageRegistry(dbDir);
                Assert(reg.GetForLanguage("nl-BE").Select(d => Path.GetFileName(d.FilePath)).SequenceEqual(new[] { "b_nlbe.wfq", "a_nl.wfq" }), "a language tag with a region: that database first, then the one of the language");
                Assert(reg.GetForLanguage("nl").Select(d => Path.GetFileName(d.FilePath)).SequenceEqual(new[] { "a_nl.wfq" }), "a plain language finds only its own database");
                Assert(reg.GetForLanguage("fr-BE").Count == 0 && reg.GetForLanguage("en-GB").Select(d => Path.GetFileName(d.FilePath)).SequenceEqual(new[] { "c_en.wfq" }), "no database: none; en-GB finds en");
            }
            finally { try { Directory.Delete(dbDir, true); } catch { } }

            // ── The editor: the language of the keyboard is a choice of its own ──
            using (var f = KeyboardEditor(new LayoutMeta { Language = "fr-BE" }))
            {
                var kb = Priv<TouchChoiceButton>(f, "_cmbKeyboardLanguage");
                var tags = Priv<List<string>>(f, "_keyboardLanguages");
                Assert(tags[kb.SelectedIndex] == "fr-BE", "a language tag the program has no database or translation for stays selectable and selected");
                f.ApplyForTest();
                Assert(f.ResultMeta.Language == "fr-BE", "Apply keeps the language of the keyboard");
                int nl = tags.FindIndex(t => t == "nl");
                if (nl >= 0)
                {
                    kb.SelectedIndex = nl;
                    f.ApplyForTest();
                    Assert(f.ResultMeta.Language == "nl", "choosing another language of the keyboard is stored in the layout");
                }
            }
            string wasLang = Lang.CurrentCode;
            try
            {
                Lang.Load("en");
                using (var f = KeyboardEditor(new LayoutMeta { Language = "nl" }))
                {
                    f.ApplyForTest();
                    Assert(f.ResultMeta.Language == "nl" && Lang.CurrentCode == "en", "the language of the screens does not change the language of the keyboard (and the other way round)");
                }
            }
            finally { Lang.Load(wasLang); }
        }
    }
}

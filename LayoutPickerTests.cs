// LayoutPickerTests.cs — "Open a keyboard": the ready-made layouts with a title and a description per language (todo Priority 5, step 5.8, done
// first because the layouts live in Program Files, where nobody looks).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_LayoutPicker()
        {
            Section("Open a keyboard — the ready-made layouts with title and description");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string file in new[] { "azerty.kbl", "azertycolor.kbl", "qwerty.kbl", "azerty_fr.kbl", "qwerty_it.kbl", "qwerty_es.kbl", "math.kbl", "Scanning.kbl", "ScanningCijfers.kbl" })
            {
                var info = SettingsManager.ReadInfo(Path.Combine(baseDir, file));
                Assert(info != null, $"{file}: its info can be read");
                if (info == null) continue;
                var (titles, descriptions, language) = info.Value;
                Assert(titles.ContainsKey("nl") && titles.ContainsKey("en") && descriptions.ContainsKey("nl") && descriptions.ContainsKey("en"),
                    $"{file}: a title and a description in Dutch and in English");
                Assert(descriptions["nl"].Length > 20 && descriptions["en"].Length > 20 && descriptions.Values.All(d => d.Length <= 160), $"{file}: descriptions are one or two lines (20–160 characters)");
                Assert(new[] { "fr", "it", "es" }.All(l => titles.ContainsKey(l) && descriptions.ContainsKey(l)), $"{file}: a title and a description in French, Italian and Spanish too");
                Assert(language.Length > 0, $"{file}: it names the language of the keyboard");
            }
            Assert(SettingsManager.ReadInfo(Path.Combine(baseDir, "does not exist.kbl")) == null, "a file that cannot be read gives no info (no exception)");

            // The list: in the language of the screens, sorted by title.
            var nl = LayoutInfo.FromFolder(baseDir, "nl");
            var en = LayoutInfo.FromFolder(baseDir, "en");
            Assert(nl.Count >= 4 && en.Count == nl.Count, $"the folder of the program lists its layouts ({nl.Count})");
            Assert(nl.Any(i => i.Title == "Wiskunde") && en.Any(i => i.Title == "Mathematics"), "titles follow the language of the screens (Wiskunde / Mathematics)");
            Assert(nl.Select(i => i.Title).SequenceEqual(nl.Select(i => i.Title).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)), "the list is sorted by title");
            Assert(LayoutInfo.FromFolder(baseDir, "fr").First(i => Path.GetFileName(i.Path) == "math.kbl").Title == "Mathématiques" &&
                   LayoutInfo.FromFolder(baseDir, "es").First(i => Path.GetFileName(i.Path) == "math.kbl").Title == "Matemáticas", "French and Spanish titles follow the language of the screens");
            Assert(LayoutInfo.FromFolder(baseDir, "de").First(i => Path.GetFileName(i.Path) == "math.kbl").Title == "Wiskunde",
                "a language the layout has no title in: the language of the keyboard itself (Dutch), then English");

            // A layout without info is listed under its file name; the info of a saved layout survives a save and a load.
            string dir = Path.Combine(Path.GetTempPath(), "osk_lp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var layout = new GridLayout(1, 1) { Cells = { new GridCell(0, 0, new KeyProps("a", "a")) }, Groups = { new KeyGroup { Name = "standard" } } };
                SettingsManager.SaveSettings(layout, new VisualTheme(), new WindowState(), new LayoutMeta(), Path.Combine(dir, "zonder titel.kbl"));
                var meta = new LayoutMeta { Language = "nl" };
                meta.Titles["nl"] = "Mijn toetsenbord"; meta.Descriptions["nl"] = "Een proef.";
                SettingsManager.SaveSettings(layout, new VisualTheme(), new WindowState(), meta, Path.Combine(dir, "mijn.kbl"));
                var list = LayoutInfo.FromFolder(dir, "en");
                Assert(list.Count == 2 && list.Any(i => i.Title == "zonder titel" && i.Description == "") && list.Any(i => i.Title == "Mijn toetsenbord" && i.Description == "Een proef."),
                    "a layout without a title is listed by its file name; one with a title by that title (the language of the keyboard when the screens' language has none)");
                var back = new LayoutMeta();
                SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), back, Path.Combine(dir, "mijn.kbl"));
                Assert(back.Titles["nl"] == "Mijn toetsenbord" && back.Descriptions["nl"] == "Een proef.", "title and description survive a save and a load");
                Assert(!File.ReadAllText(Path.Combine(dir, "zonder titel.kbl")).Contains("<Info"), "a layout without titles is written without an Info element");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }

            // The dialog.
            var items = new List<LayoutInfo>
            {
                new LayoutInfo { Path = @"C:\x\a.kbl", Title = "A", Description = "Eerste." },
                new LayoutInfo { Path = @"C:\x\b.kbl", Title = "B", Description = "Tweede." },
            };
            using (var d = new LayoutPickerDialog(items))
            {
                var list = Priv<TouchList>(d, "_list");
                var desc = Priv<System.Windows.Forms.Label>(d, "_description");
                Assert(list.Items.Count == 2 && list.SelectedIndex == 0 && desc.Text == "Eerste.", "the dialog lists the layouts, the first is selected and its description is shown");
                list.SelectedIndex = 1;
                Assert(desc.Text == "Tweede.", "another row: its description");
                typeof(LayoutPickerDialog).GetMethod("Choose", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(d, null);
                Assert(d.ChosenPath == @"C:\x\b.kbl" && !d.BrowseRequested && d.DialogResult == System.Windows.Forms.DialogResult.OK, "Open gives the file of the chosen row");
            }
            using (var d = new LayoutPickerDialog(items))
            {
                var browse = UiGuard.All(d).OfType<FluentButton>().First(b => b.Text == Lang.T("layouts: other"));
                browse.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(browse, new object[] { EventArgs.Empty });
                Assert(d.BrowseRequested && d.ChosenPath == null && d.DialogResult == System.Windows.Forms.DialogResult.OK, "'Other file…' asks the caller for the ordinary file dialog");
            }
            using (var d = new LayoutPickerDialog(new List<LayoutInfo>()))
            {
                var open = UiGuard.All(d).OfType<FluentButton>().First(b => b.Text == Lang.T("layouts: open"));
                Assert(!open.Enabled && Priv<TouchList>(d, "_list").EmptyText == Lang.T("layouts: none"), "no layouts: Open is off and the list says so");
            }
        }
    }
}

// FirstRunTests.cs — the question asked once, the first time the keyboard starts: key arrangement and language of the suggestions.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_FirstRun()
        {
            Section("First start — arrangement and language of the suggestions");
            var langs = new (string Code, string Name)[] { ("nl", "Nederlands"), ("en", "English") };

            Assert(FirstRunDialog.GuessArrangement(new CultureInfo("nl-BE")) == "azerty", "a Belgian system: AZERTY");
            Assert(FirstRunDialog.GuessArrangement(new CultureInfo("fr-FR")) == "azerty", "a French system: AZERTY");
            Assert(FirstRunDialog.GuessArrangement(new CultureInfo("nl-NL")) == "qwerty", "a Dutch system: QWERTY");
            Assert(FirstRunDialog.GuessArrangement(new CultureInfo("en-US")) == "qwerty", "an American system: QWERTY");
            Assert(FirstRunDialog.GuessArrangement(CultureInfo.InvariantCulture) == "qwerty", "no region: QWERTY");

            Assert(FirstRunDialog.GuessLanguage(new CultureInfo("nl-BE"), langs) == "nl", "a Dutch system: the Dutch database");
            Assert(FirstRunDialog.GuessLanguage(new CultureInfo("en-GB"), langs) == "en", "an English system: the English database");
            Assert(FirstRunDialog.GuessLanguage(new CultureInfo("de-DE"), langs) == "en", "a language without database: English when there is a database for it");
            Assert(FirstRunDialog.GuessLanguage(new CultureInfo("de-DE"), new (string, string)[] { ("nl", "Nederlands"), ("fr", "Français") }) == "nl", "…else the first installed one");
            Assert(FirstRunDialog.LanguageName("nl") == "Nederlands" && FirstRunDialog.LanguageName("xx") == "XX", "a language is shown in its own language, unknown ones by code");

            try
            {
                Lang.Load("en");
                using (var d = new FirstRunDialog(langs, "azerty", "en"))
                    Assert(d.Arrangement == "azerty" && d.Language == "en", "the dialog opens on the suggested choices");
                using (var d = new FirstRunDialog(langs, "qwerty", "nl"))
                    Assert(d.Arrangement == "qwerty" && d.Language == "nl", "the dialog opens on QWERTY and Dutch");
                using (var d = new FirstRunDialog(langs, "whatever", "zz"))
                    Assert(d.Arrangement == "qwerty" && d.Language == "nl", "an unknown suggestion falls back to QWERTY and the first language");
                using (var d = new FirstRunDialog(new (string, string)[] { ("en", "English") }, "qwerty", "nl"))
                    Assert(d.Language == "en", "with one database installed that language is the choice");

                // The language of the screens and the style are choices too.
                var screens = new (string, string)[] { ("en", "English"), ("nl", "Nederlands") };
                using (var d = new FirstRunDialog(langs, "qwerty", "en", screens, "nl", "light"))
                    Assert(d.ScreenLanguage == "nl" && d.StyleId == "light" && d.Language == "en", "the screen language and the style open on the suggested choices");
                using (var d = new FirstRunDialog(langs, "qwerty", "en", screens, "xx", "nope"))
                    Assert(d.ScreenLanguage == "en" && d.StyleId == NewKeyboardWizard.Presets[0].Id, "an unknown screen language or style falls back to the first");
                using (var d = new FirstRunDialog(langs, "qwerty", "en"))
                    Assert(d.ScreenLanguage == "en", "without a list of screen languages: English");

                // A standard style on a keyboard that has been edited: colours replace those of the theme and groups; own colours of keys stay.
                var preset = NewKeyboardWizard.Presets.First(p => p.Id == "hc");
                var theme = new VisualTheme();
                var groups = new List<KeyGroup> { new KeyGroup { Name = "standard" }, new KeyGroup { Name = "vowels", KeyColor = System.Drawing.Color.Red }, new KeyGroup { Name = "mine", KeyColor = System.Drawing.Color.Lime } };
                NewKeyboardWizard.ApplyPreset(preset, theme, groups);
                Assert(theme.BackgroundColor.ToArgb() == SettingsManager.ParseColor(preset.Background, System.Drawing.Color.Empty).ToArgb() && theme.BorderThickness == preset.BorderThickness,
                    "a standard style sets the colours and the border of the theme");
                Assert(groups.First(g => g.Name == "vowels").KeyColor.ToArgb() == SettingsManager.ParseColor("FFFF00", System.Drawing.Color.Empty).ToArgb(), "…restyles a group it knows (by id)");
                Assert(groups.First(g => g.Name == "mine").KeyColor.ToArgb() == System.Drawing.Color.Lime.ToArgb(), "…and leaves a group it does not know as it is");
                var added = groups.First(g => g.Name == "prediction");
                Assert(added.Names.ContainsKey("en") && added.Names.ContainsKey("nl"), "…adds a missing group with its names in every language");
                using (var pv = new StylePreview())
                {
                    pv.ShowPreset(preset, "High Contrast"); pv.ShowCurrent(theme, groups, "now");
                    pv.Size = new System.Drawing.Size(300, 56);
                    using var bmp = new System.Drawing.Bitmap(300, 56);
                    pv.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, 300, 56));
                    Assert(pv.AccessibleName == "now", "the style preview paints (a standard style and the style of a keyboard) and names itself");
                }
            }
            finally { Lang.Load("en"); }
        }
    }
}

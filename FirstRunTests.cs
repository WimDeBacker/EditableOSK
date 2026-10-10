// FirstRunTests.cs — the question asked once, the first time the keyboard starts: key arrangement and language of the suggestions.

using System.Globalization;

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
            Assert(FirstRunDialog.GuessLanguage(new CultureInfo("de-DE"), langs) == "nl", "a language without database: the first installed one");
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
            }
            finally { Lang.Load("en"); }
        }
    }
}

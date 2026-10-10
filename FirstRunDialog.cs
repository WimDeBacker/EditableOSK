// FirstRunDialog.cs — asked once, the very first time the keyboard starts for a user (no settings yet): which key arrangement
// (QWERTY or AZERTY) and which language for the word suggestions. The choice loads the matching ready-made layout and database; everything
// can be changed later in Edit mode.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class FirstRunDialog : FluentDialogBase
    {
        private readonly TouchChoiceButton _cmbArrangement;
        private readonly TouchChoiceButton _cmbLanguage;
        private readonly string[] _languageCodes;

        /// <summary><c>qwerty</c> or <c>azerty</c>: the ready-made layout file of that name is opened.</summary>
        public string Arrangement => _cmbArrangement.SelectedIndex == 1 ? "azerty" : "qwerty";

        /// <summary>The language code (<c>nl</c>, <c>en</c>) of the chosen word database; also the language of the keyboard's own windows.</summary>
        public string Language => _languageCodes[Math.Max(0, _cmbLanguage.SelectedIndex)];

        /// <param name="languages">The word databases that are installed, as (code, name shown). At least one.</param>
        public FirstRunDialog(IReadOnlyList<(string Code, string Name)> languages, string defaultArrangement, string defaultLanguage)
        {
            Text = Lang.T("first run: title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            var start = MakeTouchButton(() => Lang.T("first run: start"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, start), withSections: false);

            var t = AddSection(() => Lang.T("first run: title"));
            AddWideRow(t, Wrap(new Label
            {
                Text = Lang.T("first run: intro"), AutoSize = true, MaximumSize = new Size(Touch.LabelMaxWidth * 3, 0), UseMnemonic = false,
                Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
            }), fill: false);

            _cmbArrangement = Chooser(new[] { "QWERTY", "AZERTY" }, string.Equals(defaultArrangement, "azerty", StringComparison.OrdinalIgnoreCase) ? 1 : 0, 0);
            AddRow(t, () => Lang.T("first run: arrangement"), _cmbArrangement);

            _languageCodes = languages.Select(l => l.Code).ToArray();
            int li = Array.FindIndex(_languageCodes, c => string.Equals(c, defaultLanguage, StringComparison.OrdinalIgnoreCase));
            _cmbLanguage = Chooser(languages.Select(l => l.Name), Math.Max(0, li), 1);
            AddRow(t, () => Lang.T("first run: language"), _cmbLanguage);

            AddWideRow(t, Wrap(new Label
            {
                Text = Lang.T("first run: hint"), AutoSize = true, MaximumSize = new Size(Touch.LabelMaxWidth * 3, 0), UseMnemonic = false,
                Font = Fluent.FontHint, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
            }), fill: false);

            start.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            AcceptButton = start;
            ActiveControl = _cmbArrangement;
        }

        private static TouchChoiceButton Chooser(IEnumerable<string> names, int selected, int tabIndex)
        {
            var c = new TouchChoiceButton { RowHeight = 44, TabIndex = tabIndex };
            c.SetItems(names.Select(n => new TouchChoice { Text = n }), selected);
            return c;
        }

        /// <summary>AZERTY for a Belgian or French system, QWERTY otherwise: what the person at this PC most likely has.</summary>
        internal static string GuessArrangement(CultureInfo culture)
        {
            string region = culture.Name.Length >= 5 ? culture.Name.Substring(culture.Name.Length - 2) : "";
            return region.Equals("BE", StringComparison.OrdinalIgnoreCase) || culture.TwoLetterISOLanguageName == "fr" ? "azerty" : "qwerty";
        }

        /// <summary>The language of this system when a database for it is installed, else the first installed one.</summary>
        internal static string GuessLanguage(CultureInfo culture, IReadOnlyList<(string Code, string Name)> languages)
        {
            string own = culture.TwoLetterISOLanguageName;
            return languages.Any(l => string.Equals(l.Code, own, StringComparison.OrdinalIgnoreCase)) ? own : languages[0].Code;
        }

        /// <summary>The name a language is shown with: in its own language.</summary>
        internal static string LanguageName(string code) => code.ToLowerInvariant() switch
        {
            "nl" => "Nederlands", "en" => "English", "fr" => "Français", "de" => "Deutsch", _ => code.ToUpperInvariant(),
        };
    }
}

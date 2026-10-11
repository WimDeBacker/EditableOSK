// FirstRunDialog.cs — asked once, the very first time the keyboard starts for a user (no settings yet): the language of the screens, the
// key arrangement (QWERTY or AZERTY), the language of the keyboard (it chooses the word suggestions and the names of the groups) and the
// style. Each language is offered only when the program has it (English otherwise). Everything can be changed later in Edit mode.

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
        private readonly TouchChoiceButton _cmbScreen, _cmbArrangement, _cmbLanguage, _cmbStyle;
        private readonly string[] _screenCodes, _languageCodes;
        private readonly StylePreview _preview = new StylePreview { Dock = DockStyle.Fill, Margin = new Padding(0, Touch.Gap, 0, Touch.Gap) };

        /// <summary><c>qwerty</c> or <c>azerty</c>: the ready-made layout file of that name is opened.</summary>
        public string Arrangement => _cmbArrangement.SelectedIndex == 1 ? "azerty" : "qwerty";

        /// <summary>The language code (<c>nl</c>, <c>en</c>) of the chosen keyboard language: it chooses the word database.</summary>
        public string Language => _languageCodes[Math.Max(0, _cmbLanguage.SelectedIndex)];

        /// <summary>The language of the screens (menus and windows).</summary>
        public string ScreenLanguage => _screenCodes[Math.Max(0, _cmbScreen.SelectedIndex)];

        /// <summary>The id of the chosen standard style (<c>dark</c>, <c>light</c>, <c>hc</c>, <c>colorful</c>).</summary>
        public string StyleId => NewKeyboardWizard.Presets[Math.Clamp(_cmbStyle.SelectedIndex, 0, NewKeyboardWizard.Presets.Length - 1)].Id;

        /// <param name="languages">The word databases that are installed, as (code, name shown). At least one.</param>
        /// <param name="screenLanguages">The languages the program is translated into, as (code, name shown). At least English.</param>
        public FirstRunDialog(IReadOnlyList<(string Code, string Name)> languages, string defaultArrangement, string defaultLanguage,
                              IReadOnlyList<(string Code, string Name)> screenLanguages = null, string defaultScreenLanguage = "en", string defaultStyle = "dark")
        {
            screenLanguages ??= new[] { ("en", "English") };
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

            _screenCodes = screenLanguages.Select(l => l.Code).ToArray();
            _cmbScreen = Chooser(screenLanguages.Select(l => l.Name), IndexOf(_screenCodes, defaultScreenLanguage), 0);
            AddRow(t, () => Lang.T("Interface language"), _cmbScreen);

            _cmbArrangement = Chooser(new[] { "QWERTY", "AZERTY" }, string.Equals(defaultArrangement, "azerty", StringComparison.OrdinalIgnoreCase) ? 1 : 0, 1);
            AddRow(t, () => Lang.T("first run: arrangement"), _cmbArrangement);

            _languageCodes = languages.Select(l => l.Code).ToArray();
            _cmbLanguage = Chooser(languages.Select(l => l.Name), IndexOf(_languageCodes, defaultLanguage), 2);
            AddRow(t, () => Lang.T("first run: language"), _cmbLanguage);

            var presets = NewKeyboardWizard.Presets;
            int styleIdx = Math.Max(0, Array.FindIndex(presets, p => string.Equals(p.Id, defaultStyle, StringComparison.OrdinalIgnoreCase)));
            _cmbStyle = Chooser(presets.Select(p => Lang.T("wiz: theme " + p.Id)), styleIdx, 3);
            _cmbStyle.SelectedIndexChanged += (s, e) => ShowStyle();
            AddRow(t, () => Lang.T("Style"), _cmbStyle);
            AddWideRow(t, _preview);
            ShowStyle();

            start.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            AcceptButton = start;
            ActiveControl = _cmbScreen;
        }

        private void ShowStyle() =>
            _preview.ShowPreset(NewKeyboardWizard.Presets[Math.Clamp(_cmbStyle.SelectedIndex, 0, NewKeyboardWizard.Presets.Length - 1)], _cmbStyle.SelectedItem?.Text ?? "");

        private static int IndexOf(string[] codes, string code) =>
            Math.Max(0, Array.FindIndex(codes, c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)));

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

        /// <summary>The language of this system when the program has it in <paramref name="languages"/>; else English when that is there; else the first.</summary>
        internal static string GuessLanguage(CultureInfo culture, IReadOnlyList<(string Code, string Name)> languages)
        {
            string own = culture.TwoLetterISOLanguageName;
            if (languages.Any(l => string.Equals(l.Code, own, StringComparison.OrdinalIgnoreCase))) return own;
            return languages.Any(l => l.Code == "en") ? "en" : languages[0].Code;
        }

        /// <summary>The name a language (or language and region: <c>nl-BE</c>) is shown with: in its own language; an unknown tag as upper-case code.</summary>
        internal static string LanguageName(string code)
        {
            try
            {
                string n = CultureInfo.GetCultureInfo(code).NativeName;
                if (!string.IsNullOrWhiteSpace(n) && !string.Equals(n, code, StringComparison.OrdinalIgnoreCase))
                    return char.ToUpper(n[0], CultureInfo.CurrentCulture) + n.Substring(1);
            }
            catch (CultureNotFoundException) { }
            return code.ToUpperInvariant();
        }
    }
}

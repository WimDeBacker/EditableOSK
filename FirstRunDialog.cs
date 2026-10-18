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

        // The key arrangements offered: the ready-made layout file of that name is opened. azerty is the Belgian AZERTY, azerty_fr the
        // French one (they differ in many keys); qwerty is the American one, qwerty_it and qwerty_es the Italian and the Spanish; Scanning is the
        // letter keyboard for scanning (letters by frequency), never the suggestion. A null text is shown as "Scanning" in the language of the screens.
        private static readonly (string Text, string File)[] Arrangements =
        {
            ("QWERTY", "qwerty"), ("AZERTY (BE)", "azerty"), ("AZERTY (FR)", "azerty_fr"), ("QWERTY (IT)", "qwerty_it"), ("QWERTY (ES)", "qwerty_es"), (null, "Scanning"),
        };

        /// <summary>The file name (without .kbl) of the chosen arrangement: <c>qwerty</c>, <c>azerty</c>, <c>azerty_fr</c>, <c>qwerty_it</c>, <c>qwerty_es</c> or <c>Scanning</c>.</summary>
        public string Arrangement => Arrangements[Math.Clamp(_cmbArrangement.SelectedIndex, 0, Arrangements.Length - 1)].File;

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

            // The last choice is the letter keyboard for scanning (letters by frequency); it is never the suggestion.
            _cmbArrangement = Chooser(Arrangements.Select(a => a.Text ?? Lang.T("Scanning")),
                                      Math.Max(0, Array.FindIndex(Arrangements, a => string.Equals(a.File, defaultArrangement, StringComparison.OrdinalIgnoreCase))), 1);
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

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr hkl);

        /// <summary>
        /// The arrangement of the keyboard layout Windows is using for typing now, found by asking what the key where Q sits on a QWERTY keyboard
        /// (scan code 0x10) types: an "a" means AZERTY, a "q" QWERTY. Null when Windows cannot say. This is what the person types on, which the
        /// language and region settings of Windows do not tell (a Belgian keyboard in an English Windows).
        /// </summary>
        internal static string DetectKeyboardArrangement()
        {
            try
            {
                IntPtr hkl = InputLanguage.CurrentInputLanguage.Handle;
                uint vk = MapVirtualKeyEx(0x10, 1 /* MAPVK_VSC_TO_VK */, hkl);
                if (vk == 0) return null;
                uint ch = MapVirtualKeyEx(vk, 2 /* MAPVK_VK_TO_CHAR */, hkl) & 0x7FFF;      // the top bit marks a dead key
                return Refine(ArrangementFromKey((char)ch), TypingCulture());
            }
            catch { return null; }
        }

        /// <summary>
        /// Says which of the ready-made layouts a basic arrangement (<c>azerty</c> or <c>qwerty</c>, as the Q key shows it) is, from the language
        /// and region of the input language: AZERTY in France is the French layout, in Belgium (and elsewhere) the Belgian one; QWERTY of an
        /// Italian or Spanish input language is the Italian or the Spanish layout. Null stays null.
        /// </summary>
        internal static string Refine(string basic, CultureInfo culture)
        {
            if (basic == null) return null;
            string lang = culture.TwoLetterISOLanguageName, region = RegionOf(culture);
            if (basic == "azerty") return lang == "fr" && region != "BE" ? "azerty_fr" : "azerty";
            return lang == "it" ? "qwerty_it" : lang == "es" ? "qwerty_es" : "qwerty";
        }

        private static string RegionOf(CultureInfo culture) => culture.Name.Length >= 5 ? culture.Name.Substring(culture.Name.Length - 2).ToUpperInvariant() : "";

        /// <summary>The arrangement for the character the first letter key of the top letter row types: "a" is AZERTY, "q" QWERTY, anything else unknown (null).</summary>
        internal static string ArrangementFromKey(char c) =>
            char.ToLowerInvariant(c) == 'a' ? "azerty" : char.ToLowerInvariant(c) == 'q' ? "qwerty" : null;

        /// <summary>The culture of the language typed in now (the input language of Windows), else that of the screens of Windows.</summary>
        internal static CultureInfo TypingCulture()
        {
            try { return InputLanguage.CurrentInputLanguage.Culture; }
            catch { return CultureInfo.CurrentUICulture; }
        }

        /// <summary>
        /// What the person at this PC most likely has: the Belgian AZERTY for Belgium, the French AZERTY for French (not in Switzerland, Canada or
        /// Luxembourg: those keyboards are another kind), the Italian and the Spanish layout for those languages, QWERTY otherwise.
        /// </summary>
        internal static string GuessArrangement(CultureInfo culture)
        {
            string lang = culture.TwoLetterISOLanguageName, region = RegionOf(culture);
            if (region == "BE") return "azerty";
            if (lang == "fr") return region is "CH" or "CA" or "LU" ? "qwerty" : "azerty_fr";
            return lang == "it" ? "qwerty_it" : lang == "es" ? "qwerty_es" : "qwerty";
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

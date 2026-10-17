// UserSettings.cs — what belongs to the person at this PC and not to a keyboard layout: for now the language of the screens (menus and
// dialog windows). It is kept in %AppData%\EditableOSK\user.xml (see UserData), so one layout can be used by people who read different
// languages, and a layout carries only the language of the keyboard itself (LayoutMeta.Language: its names and its word prediction).

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace OnScreenKeyboard
{
    internal static class UserSettings
    {
        private const string FileName = "user.xml";
        private static string _interfaceLanguage;      // null until first needed

        /// <summary>
        /// The language of the screens: the one the user chose, else the language of Windows when the program is translated into it, else
        /// English. Setting it saves it at once.
        /// </summary>
        internal static string InterfaceLanguage
        {
            get
            {
                if (_interfaceLanguage == null)
                {
                    string saved = Load();
                    _languageIsChoice = saved != null;
                    _interfaceLanguage = saved ?? Default();
                }
                return _interfaceLanguage;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                _languageIsChoice = true;
                // Nothing to write when it is what is saved already; a choice that equals the default is still written once, so it stays
                // the user's choice when the language of Windows changes later.
                bool same = string.Equals(_interfaceLanguage, value, StringComparison.OrdinalIgnoreCase);
                _interfaceLanguage = value;
                if (!same || !File.Exists(PathOfFile)) Save();
            }
        }

        /// <summary>The language of Windows when the program has a translation for it, else "en".</summary>
        internal static string Default() => Default(CultureInfo.CurrentUICulture, Lang.GetAvailable().Select(l => l.Code).ToList());

        internal static string Default(CultureInfo culture, System.Collections.Generic.IReadOnlyList<string> available)
        {
            string own = culture.TwoLetterISOLanguageName;
            return available.Any(c => string.Equals(c, own, StringComparison.OrdinalIgnoreCase)) ? own : "en";
        }

        /// <summary>For the tests: forget what was read, so the next access reads the file (or the default) again.</summary>
        internal static void Reset() { _interfaceLanguage = null; _languageIsChoice = false; _scan = null; }

        private static ScanSettings _scan;
        private static bool _languageIsChoice;     // false while the language is only the default: then the file does not fix it

        /// <summary>The settings of scanning (off by default). Change them through <see cref="SetScan"/>, which also saves.</summary>
        internal static ScanSettings Scan => _scan ??= LoadScan();

        internal static void SetScan(ScanSettings value)
        {
            var v = value.Clone(); v.Clamp();
            _scan = v;
            Save();
        }

        private static string PathOfFile => UserData.FileFor(FileName);

        private static ScanSettings LoadScan()
        {
            var s = new ScanSettings();
            try
            {
                string path = PathOfFile;
                if (!File.Exists(path)) return s;
                var doc = new XmlDocument();
                doc.Load(path);
                var e = doc.SelectSingleNode("/UserSettings/Scanning") as XmlElement;
                if (e == null) return s;
                bool B(string n, bool d) => e.HasAttribute(n) ? e.GetAttribute(n) == "1" : d;
                int  I(string n, int d)  => int.TryParse(e.GetAttribute(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : d;
                s.Enabled        = B("Enabled", false);
                s.TwoSwitch      = B("TwoSwitch", false);
                s.SelectKey      = I("SelectKey", s.SelectKey);
                s.StepKey        = I("StepKey", s.StepKey);
                s.ScanMs         = I("ScanMs", s.ScanMs);
                s.FirstExtraMs   = I("FirstExtraMs", s.FirstExtraMs);
                s.Rounds         = I("Rounds", s.Rounds);
                s.IgnoreRepeatMs = I("IgnoreRepeatMs", s.IgnoreRepeatMs);
                s.SelectOnRelease = B("SelectOnRelease", false);
                s.SoundOnStep    = B("SoundOnStep", false);
                s.SoundOnSelect  = B("SoundOnSelect", true);
                Color C(string n, Color d) =>
                    int.TryParse(e.GetAttribute(n), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int v) ? Color.FromArgb(255, Color.FromArgb(v)) : d;
                s.SelectPad = I("SelectPad", 0); s.StepPad = I("StepPad", 0); s.BackPad = I("BackPad", 0);
                s.BackStep      = B("BackStep", true);
                s.BackSwitch    = B("BackSwitch", false);
                s.BackKey       = I("BackKey", s.BackKey);
                s.BackLongPress = B("BackLongPress", false);
                s.LongPressStepsBack = B("LongPressStepsBack", false);
                s.BackHoldMs    = I("BackHoldMs", s.BackHoldMs);
                s.BackColor = C("BackColor", s.BackColor);
                s.RowColor = C("RowColor", s.RowColor);
                s.KeyColor = C("KeyColor", s.KeyColor);
                s.Clamp();
            }
            catch { return new ScanSettings(); }
            return s;
        }

        /// <summary>The saved language when there is one and the program can still load it; otherwise null.</summary>
        private static string Load()
        {
            try
            {
                string path = PathOfFile;
                if (!File.Exists(path)) return null;
                var doc = new XmlDocument();
                doc.Load(path);
                string code = doc.SelectSingleNode("/UserSettings")?.Attributes?["InterfaceLanguage"]?.Value;
                return !string.IsNullOrWhiteSpace(code) && Lang.GetAvailable().Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                    ? code : null;
            }
            catch { return null; }
        }

        private static void Save()
        {
            try
            {
                _ = InterfaceLanguage;      // read what the file says before it is rewritten (the scan settings may be saved first)
                string path = PathOfFile, tmp = path + ".tmp";
                using (var w = XmlWriter.Create(tmp, new XmlWriterSettings { Indent = true }))
                {
                    w.WriteStartDocument();
                    w.WriteStartElement("UserSettings");
                    if (_languageIsChoice) w.WriteAttributeString("InterfaceLanguage", _interfaceLanguage);
                    var s = Scan;
                    w.WriteStartElement("Scanning");
                    string F(bool b) => b ? "1" : "0";
                    w.WriteAttributeString("Enabled", F(s.Enabled));
                    w.WriteAttributeString("TwoSwitch", F(s.TwoSwitch));
                    w.WriteAttributeString("SelectKey", s.SelectKey.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("StepKey", s.StepKey.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("ScanMs", s.ScanMs.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("FirstExtraMs", s.FirstExtraMs.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("Rounds", s.Rounds.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("IgnoreRepeatMs", s.IgnoreRepeatMs.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("SelectOnRelease", F(s.SelectOnRelease));
                    w.WriteAttributeString("SoundOnStep", F(s.SoundOnStep));
                    w.WriteAttributeString("SoundOnSelect", F(s.SoundOnSelect));
                    w.WriteAttributeString("SelectPad", s.SelectPad.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("StepPad", s.StepPad.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("BackPad", s.BackPad.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("BackStep", F(s.BackStep));
                    w.WriteAttributeString("BackSwitch", F(s.BackSwitch));
                    w.WriteAttributeString("BackKey", s.BackKey.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("BackLongPress", F(s.BackLongPress));
                    w.WriteAttributeString("LongPressStepsBack", F(s.LongPressStepsBack));
                    w.WriteAttributeString("BackHoldMs", s.BackHoldMs.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("BackColor", (s.BackColor.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
                    w.WriteAttributeString("RowColor", (s.RowColor.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
                    w.WriteAttributeString("KeyColor", (s.KeyColor.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndDocument();
                }
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception ex) { Program.LogError("saving the user settings", ex); }
        }
    }
}

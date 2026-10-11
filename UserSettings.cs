// UserSettings.cs — what belongs to the person at this PC and not to a keyboard layout: for now the language of the screens (menus and
// dialog windows). It is kept in %AppData%\EditableOSK\user.xml (see UserData), so one layout can be used by people who read different
// languages, and a layout carries only the language of the keyboard itself (LayoutMeta.Language: its names and its word prediction).

using System;
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
            get => _interfaceLanguage ??= Load() ?? Default();
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
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
        internal static void Reset() => _interfaceLanguage = null;

        private static string PathOfFile => UserData.FileFor(FileName);

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
                string path = PathOfFile, tmp = path + ".tmp";
                using (var w = XmlWriter.Create(tmp, new XmlWriterSettings { Indent = true }))
                {
                    w.WriteStartDocument();
                    w.WriteStartElement("UserSettings");
                    w.WriteAttributeString("InterfaceLanguage", _interfaceLanguage);
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

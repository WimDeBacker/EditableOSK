using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Static translation helper.
    /// English text is baked in as fallbacks — the app works without any lang_*.xml files.
    /// XML files only need to contain entries that differ from English.
    /// </summary>
    public static class Lang
    {
        public static string CurrentCode { get; private set; } = "en";
        public static string CurrentName { get; private set; } = "English";

        public static event Action LanguageChanged;

        // Built-in English strings (used as fallback when XML is missing or key not found)
        private static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            // ── Gear / main menu ────────────────────────────────────
            ["💾 Save"]              = "💾 Save",
            ["💾 Save As…"]          = "💾 Save As…",
            ["📂 Load…"]             = "📂 Load…",
            ["✏ Edit Mode"]          = "✏ Edit Mode",
            ["⚡ Quick Edit"]         = "⚡ Quick Edit",
            ["🖥 Edit Keyboard…"]    = "🖥 Edit Keyboard…",
            ["🔲 Hide title bar"]    = "🔲 Hide title bar",

            // ── Key context menu (grid edit) ─────────────────────────
            ["✏ Edit key"]           = "✏ Edit key",

            // ── Key editor ──────────────────────────────────────────
            ["Edit Key"]             = "Edit Key",
            ["Key Content"]          = "Key Content",
            ["Label"]                = "Label",
            ["Send"]                 = "Send",
            ["Shift label"]          = "Shift label",
            ["Shift send"]           = "Shift send",
            ["AltGr label"]          = "AltGr label",
            ["AltGr send"]           = "AltGr send",
            ["Width"]                = "Width",
            ["Width hint"]           = "relative  (e.g. 1, 1.5, 2)",
            ["Row span"]             = "Row span",
            ["Row span hint"]        = "1 = normal,  2 = double height (last key only)",
            ["Auto-escape"]          = "Auto-escape SendKeys special characters",
            ["Appearance"]           = "Appearance",
            ["Font"]                 = "Font",
            ["Font size"]            = "Font size",
            ["Auto"]                 = "Auto",
            ["Font color"]           = "Font color",
            ["Key color"]            = "Key color",
            ["Border"]               = "Border",
            ["Border color"]         = "Border color",
            ["Border thickness"]     = "Thickness (px)",
            ["Border hint"]          = "0 = use global default",
            ["Preview"]              = "Preview",
            ["✔ Apply"]              = "✔  Apply",
            ["✖ Cancel"]             = "✖  Cancel",

            // ── Keyboard editor ─────────────────────────────────────
            ["Edit Keyboard"]        = "Edit Keyboard",
            ["Window"]               = "Window",
            ["Opacity"]              = "Transparency",
            ["Opacity hint"]         = "0 = opaque  —  80 = 20% opacity (minimum)",
            ["Background"]           = "Background",
            ["Paste delay"]          = "Paste delay",
            ["Paste delay hint"]     = "0 = off  —  increase (e.g. 150ms) if characters are missed in Word",
            ["Default Key Style"]    = "Default Key Style",
            ["Apply to all keys"]    = "Apply style to all keys now",

            // ── Grid edit context menu ───────────────────────────────────
            ["⬆ Add row above"]     = "Add row above",
            ["⬇ Add row below"]     = "Add row below",
            ["⬅ Add col left"]      = "Add column to the left",
            ["➡ Add col right"]     = "Add column to the right",
            ["🗑 Remove row"]        = "Remove this row",
            ["🗑 Remove col"]        = "Remove this column",
            ["Split cell"]           = "Split merged cell",
            ["Merge right"]          = "Merge with cell to the right",
            ["Merge down"]           = "Merge with cell below",
            ["🌐 Language"]          = "🌐 Language",

            // ── Errors ──────────────────────────────────────────────
            ["Save failed"]          = "Save failed:",
            ["Invalid file title"]   = "Unable to Open File",
            ["Invalid file msg"]     = "The file could not be opened because it is not a valid keyboard layout file, or it was created by an incompatible version.\n\nThe keyboard layout was not changed.",
            ["Invalid file detail"]  = "Technical details:",
            // ── New UI strings ─────────────────────────────────────────────
            ["Language"]             = "Language",
            ["Hide title bar"]       = "Hide title bar",
            ["Always on top"]        = "Always on top",
            ["Sticky modifiers"]     = "Sticky modifiers",
            ["Accessibility"]        = "Accessibility",
            ["Layout file"]          = "Layout file",
            ["Key width"]            = "Key width",
            ["Key width hint"]       = "1 = one key wide,  2 = two keys wide,  ...",
            ["Key height"]           = "Key height",
            ["Key height hint"]      = "1 = one key tall,  2 = two keys tall,  ...",
            ["-1 = global default  |  0 = no border  |  1-10 = px"]
                                     = "-1 = global default  |  0 = no border  |  1-10 = px",
            ["📌 Move gear button…"] = "📌 Move gear button…",
            ["Send mode"]            = "Send mode",
            ["Text"]                 = "Text",
            ["Key/Shortcut"]         = "Key/Shortcut",
            ["Modifier"]             = "Modifier",
            ["🎹 Record key / shortcut"]          = "🎹 Record key / shortcut",
            ["⏺ Press your key or shortcut now…"] = "⏺ Press your key or shortcut now…",
            ["Press Escape to cancel"]            = "Press Escape to cancel",
            ["Cancelled"]                         = "Cancelled",
            ["Recorded — edit if needed"]    = "Recorded — edit if needed",
            ["Press 🎹 to record, or type directly"]    = "Press 🎹 to record, or type directly",
            ["Press 🎹 to re-record, or edit directly"] = "Press 🎹 to re-record, or edit directly",
        };

        private static Dictionary<string, string> _overrides = new Dictionary<string, string>();

        // ── Load ─────────────────────────────────────────────────────
        public static void Load(string code)
        {
            _overrides.Clear();

            if (code == "en")
            {
                CurrentCode = "en";
                CurrentName = "English";
                LanguageChanged?.Invoke();
                return;
            }

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"lang_{code}.xml");
            if (!File.Exists(path)) return;

            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            CurrentCode = root?.GetAttribute("code") ?? code;
            CurrentName = root?.GetAttribute("name") ?? code;

            var nodes = doc.SelectNodes("/Language/String");
            if (nodes != null)
                foreach (XmlNode node in nodes)
                {
                    string key = node.Attributes?["key"]?.Value   ?? "";
                    string val = node.Attributes?["value"]?.Value ?? "";
                    if (key != "") _overrides[key] = val;
                }

            LanguageChanged?.Invoke();
        }

        // ── Translate ────────────────────────────────────────────────
        /// <summary>
        /// Returns the translation for key. The key itself is the English text,
        /// so T("💾 Save") always returns at least "💾 Save".
        /// </summary>
        public static string T(string key)
        {
            if (_overrides.TryGetValue(key, out var ov)) return ov;
            if (_en.TryGetValue(key, out var en)) return en;
            return key;   // key IS the English text, so this is always readable
        }

        // ── Discover available languages ─────────────────────────────
        public static List<(string Code, string Name)> GetAvailable()
        {
            var result = new List<(string, string)> { ("en", "English") };
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string file in Directory.GetFiles(dir, "lang_*.xml"))
            {
                try
                {
                    var doc  = new XmlDocument();
                    doc.Load(file);
                    string code = doc.DocumentElement?.GetAttribute("code") ?? "";
                    string name = doc.DocumentElement?.GetAttribute("name") ?? code;
                    if (code != "" && code != "en") result.Add((code, name));
                }
                catch { }
            }
            result.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
}

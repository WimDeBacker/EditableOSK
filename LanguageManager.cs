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
            // ── Toolbar tooltips ─────────────────────────────────────
            ["tip: Load"]              = "Load layout file",
            ["tip: Save"]              = "Save layout file",
            ["tip: Undo"]              = "Undo last edit",
            ["tip: Redo"]              = "Redo last undone edit",
            ["tip: Edit mode"]         = "Switch to Edit mode",
            ["tip: Edit Keyboard"]     = "Edit Keyboard",
            ["tip: Exit edit mode"]    = "Exit edit mode",
            ["tip: Edit key"]          = "Edit selected key",
            ["tip: Remove key"]        = "Remove selected key (make empty)",
            ["tip: Copy formatting"]   = "Copy key formatting (style only)",
            ["tip: Copy formatting"]   = "Copy formatting — then click any key to apply",
            ["tip: Copy key"]          = "Copy key (label, send, and style)",
            ["tip: Copy key"]          = "Copy key — then click any key to paste",
            ["tip: Insert row above"]  = "Insert row above selected row",
            ["tip: Insert row below"]  = "Insert row below selected row",
            ["tip: Insert column left"]  = "Insert column to the left",
            ["tip: Insert column right"] = "Insert column to the right",
            ["tip: Remove row"]        = "Remove selected row",
            ["tip: Remove column"]     = "Remove selected column",
            ["tip: Merge right"]       = "Merge selected cell with cell to the right",
            ["tip: Merge down"]        = "Merge selected cell with cell below",
            ["tip: Split cell"]        = "Split merged cell back into single cells",

            // ── Toolbar row 1 buttons ────────────────────────────────
            ["📂 Load"]               = "📂 Load",
            ["↩ Undo"]               = "↩ Undo",
            ["↪ Redo"]               = "↪ Redo",
            ["✏ Edit"]               = "✏ Edit",
            ["🖥 Keyboard"]          = "🖥 Keyboard",
            ["✖ Exit"]               = "✖ Exit",

            // ── Toolbar row 2: key actions ───────────────────────────
            ["✏ Key"]                = "✏ Key",
            ["🗑 Key"]               = "🗑 Key",
            ["🖌 Copy fmt"]           = "🖌 Copy fmt",
            ["📄 Copy key"]          = "📄 Copy key",

            // ── Toolbar row 2: grid actions ──────────────────────────
            ["⬆ Row+"]              = "⬆ Row+",
            ["⬇ Row+"]              = "⬇ Row+",
            ["⬅ Col+"]              = "⬅ Col+",
            ["➡ Col+"]              = "➡ Col+",
            ["🗑 Row"]               = "🗑 Row",
            ["🗑 Col"]               = "🗑 Col",
            ["⊞ →"]                 = "⊞ →",
            ["⊞ ↓"]                 = "⊞ ↓",
            ["⊟ Split"]             = "⊟ Split",

            // ── Gear / main menu ────────────────────────────────────
            ["💾 Save"]              = "💾 Save",
            ["💾 Save As…"]          = "💾 Save As…",
            ["📂 Load…"]             = "📂 Load…",
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
            // Clean keys (no emoji) — these are the ones code actually calls
            ["Apply"]                = "Apply",
            ["Cancel"]               = "Cancel",
            ["Save"]                 = "Save",
            ["Save As…"]             = "Save As…",
            ["Load…"]                = "Load…",
            ["Import"]               = "Import",
            // ── Toolbar button text labels ───────────────────────────────
            ["tb: Load"]             = "Load",
            ["tb: Save"]             = "Save",
            ["tb: Undo"]             = "Undo",
            ["tb: Redo"]             = "Redo",
            ["tb: Edit"]             = "Edit",
            ["tb: Keyboard"]         = "Keyboard",
            ["tb: Exit"]             = "Exit",
            ["tb: Edit key"]         = "Edit key",
            ["tb: Remove"]           = "Remove",
            ["tb: Copy fmt"]         = "Copy fmt",
            ["tb: Copy key"]         = "Copy key",
            ["tb: Row"]              = "Row",
            ["tb: Col"]              = "Col",
            ["tb: Del row"]          = "Del row",
            ["tb: Del col"]          = "Del col",
            ["tb: Merge R"]          = "Merge R",
            ["tb: Merge D"]          = "Merge D",
            ["tb: Split"]            = "Split",

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
            ["🗑 Remove key"]         = "Clear this key (make empty)",
            ["📋 Copy formatting"]   = "Copy formatting",
            ["📋 Paste formatting"]  = "Paste formatting",
            ["Split cell"]           = "Split merged cell",
            ["Merge right"]          = "Merge with cell to the right",
            ["Merge down"]           = "Merge with cell below",
            ["🌐 Language"]          = "🌐 Language",

            // ── Errors ──────────────────────────────────────────────
            ["Save failed"]          = "Save failed:",
            ["Save invalid msg"]     = "The layout contains overlapping or missing cells and cannot be saved in its current state.\n\nTip: switch to Edit mode to inspect and fix the layout, then try saving again.",
            ["Save invalid title"]   = "Layout invalid — not saved",
            ["Invalid file title"]   = "Unable to Open File",
            ["Invalid file msg"]     = "The file could not be opened because it is not a valid keyboard layout file, or it was created by an incompatible version.\n\nThe keyboard layout was not changed.",
            ["Invalid file detail"]  = "Technical details:",
            // ── New UI strings ─────────────────────────────────────────────
            ["Language"]             = "Language",
            ["Hide title bar"]       = "Hide title bar",
            ["Always on top"]        = "Always on top",
            ["Sticky modifiers"]     = "Sticky modifiers",
            ["Hold to edit"]         = "Hold to enter edit mode",
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
            ["🗂 Layout"]                = "🗂 Layout",
            ["📂 Browse (Send)"]         = "📂 Browse (Send)",
            ["📂 Browse (Shift-send)"]   = "📂 Browse (Shift-send)",
            ["📂 Browse (AltGr-send)"]   = "📂 Browse (AltGr-send)",
            ["🎹 Record key / shortcut"]          = "🎹 Record key / shortcut",
            ["⏺ Press your key or shortcut now…"] = "⏺ Press your key or shortcut now…",
            ["Press Escape to cancel"]            = "Press Escape to cancel",
            ["Cancelled"]                         = "Cancelled",
            ["Recorded — edit if needed"]    = "Recorded — edit if needed",
            ["Press 🎹 to record, or type directly"]    = "Press 🎹 to record, or type directly",
            ["Press 🎹 to re-record, or edit directly"] = "Press 🎹 to re-record, or edit directly",

            // ── Group editor ─────────────────────────────────────────────
            ["Key Groups"]              = "Key Groups",
            ["Manage Groups…"]          = "Manage Groups…",
            ["Group"]                   = "Group",
            ["(no group)"]              = "(no group)",
            ["Manage Groups"]           = "Manage Groups",
            ["Groups"]                  = "Groups",
            ["Style"]                   = "Style",
            ["+ Add group"]             = "+ Add",
            ["− Delete group"]          = "− Delete",
            ["Name"]                    = "Name",
            ["(inherit global)"]        = "(inherit global)",
            ["(inherit)"]               = "(inherit)",
            ["-1 = inherit global"]     = "-1 = inherit global",
            ["0 = auto / inherit"]      = "0 = auto / inherit",
            ["Clear (inherit global)"]  = "Clear (inherit global)",
            ["Delete Group"]            = "Delete Group",
            ["Delete group msg"]        = "Delete group \"{0}\"?\n\nKeys assigned to this group will revert to global style.",
            ["New Group"]               = "New Group",
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

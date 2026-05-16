using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Provides UI text translations for the entire application (internationalisation / i18n).
    ///
    /// How it works:
    ///   1. Every piece of visible text in the UI is obtained by calling <see cref="T"/>(key)
    ///      instead of using a hard-coded string directly.
    ///   2. English text is baked in as the built-in fallback dictionary <see cref="_en"/>,
    ///      so the app works correctly with no external files at all.
    ///   3. If the user selects a different language (e.g. Dutch), <see cref="Load"/> reads
    ///      a file named "lang_nl.xml" from the application folder and stores any translated
    ///      strings in the <see cref="_overrides"/> dictionary.
    ///   4. When <see cref="T"/> is called it checks overrides first, then falls back to
    ///      the English dictionary, and finally returns the key itself. Because keys are
    ///      written as English text, the last fallback always produces readable output.
    ///
    /// The class is <c>static</c> because only one language is active at a time and every
    /// part of the app needs access to it — there is no need for multiple instances.
    /// </summary>
    public static class Lang
    {
        /// <summary>
        /// The BCP-47 language code of the currently active language, e.g. "en" or "nl".
        /// Read-only from outside this class; changed by <see cref="Load"/>.
        /// </summary>
        public static string CurrentCode { get; private set; } = "en";

        /// <summary>
        /// The human-readable name of the currently active language, e.g. "English" or "Nederlands".
        /// Shown in the language-selection menu.
        /// Read-only from outside this class; changed by <see cref="Load"/>.
        /// </summary>
        public static string CurrentName { get; private set; } = "English";

        /// <summary>
        /// Fired after <see cref="Load"/> finishes switching to a new language.
        /// Any UI form that displays translated text should subscribe to this event and
        /// refresh its labels when it fires.
        /// </summary>
        public static event Action LanguageChanged;

        // ── Built-in English strings ──────────────────────────────────────────

        /// <summary>
        /// The complete set of English UI strings, stored as a key → value dictionary.
        ///
        /// The "key" is deliberately the same as the English text (e.g. "Save" → "Save").
        /// This means that if a key is accidentally missing from this dictionary, the
        /// fall-through in <see cref="T"/> still returns readable English text instead
        /// of a cryptic identifier.
        ///
        /// <c>readonly</c> means this dictionary reference can never be replaced, though
        /// its contents can theoretically be modified. That is intentional: it is compiled
        /// into the executable so it is always available even if no XML files are present.
        /// </summary>
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
            ["tb: Row ↑"]            = "Row ↑",
            ["tb: Row ↓"]            = "Row ↓",
            ["tb: Col ←"]            = "Col ←",
            ["tb: Col →"]            = "Col →",
            ["tb: Del ─"]            = "Del ─",
            ["tb: Del │"]            = "Del │",
            ["tb: Merge →"]          = "Merge →",
            ["tb: Merge ↓"]          = "Merge ↓",
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
            ["Toolbar theme"]        = "Toolbar theme",
            ["Dark"]                 = "Dark",
            ["Light"]                = "Light",
            ["System default"]       = "System default",
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

        // ── Active overrides for the selected non-English language ────────────

        /// <summary>
        /// Translations for the currently active language, loaded from a lang_*.xml file.
        /// Only entries that differ from English need to be present in the XML file —
        /// anything not listed here falls back to <see cref="_en"/>.
        /// This dictionary is cleared and refilled every time <see cref="Load"/> is called.
        /// </summary>
        private static Dictionary<string, string> _overrides = new Dictionary<string, string>();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Switches the active language to the one identified by <paramref name="code"/> and
        /// fires the <see cref="LanguageChanged"/> event so all open UI forms can refresh.
        ///
        /// The XML file format is:
        /// <code>
        ///   &lt;Language code="nl" name="Nederlands"&gt;
        ///     &lt;String key="Save" value="Opslaan" /&gt;
        ///     ...
        ///   &lt;/Language&gt;
        /// </code>
        ///
        /// Only strings that differ from English need to be in the file. Missing strings
        /// silently fall back to English.
        /// </summary>
        /// <param name="code">
        /// The BCP-47 language code to activate, e.g. "en", "nl", "fr".
        /// For "en" no file is read — the built-in English dictionary is used directly.
        /// </param>
        public static void Load(string code)
        {
            // Always start fresh so stale translations from the previous language do not bleed through.
            _overrides.Clear();

            // English is fully covered by _en — no file needed.
            if (code == "en")
            {
                CurrentCode = "en";
                CurrentName = "English";
                // Notify the UI even for English in case we are switching back from another language.
                LanguageChanged?.Invoke();
                return;
            }

            // Construct the expected file path, e.g. "C:\app\lang_nl.xml".
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"lang_{code}.xml");

            // Silently do nothing if the file does not exist — the app keeps showing English.
            if (!File.Exists(path)) return;

            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;

            // Read the canonical code and display name from the XML root element's attributes,
            // falling back to the requested code if those attributes are absent.
            CurrentCode = root?.GetAttribute("code") ?? code;
            CurrentName = root?.GetAttribute("name") ?? code;

            // Walk every <String key="..." value="..." /> node and store it in _overrides.
            var nodes = doc.SelectNodes("/Language/String");
            if (nodes != null)
                foreach (XmlNode node in nodes)
                {
                    string key = node.Attributes?["key"]?.Value   ?? "";
                    string val = node.Attributes?["value"]?.Value ?? "";
                    // Skip any malformed nodes that have an empty key.
                    if (key != "") _overrides[key] = val;
                }

            // Tell the rest of the app that the language has changed so labels can be updated.
            LanguageChanged?.Invoke();
        }

        /// <summary>
        /// Returns the translated string for the given key in the currently active language.
        ///
        /// Lookup order (highest priority first):
        ///   1. The language overrides loaded from the XML file (<see cref="_overrides"/>)
        ///   2. The built-in English dictionary (<see cref="_en"/>)
        ///   3. The key itself — because keys are written as English text this always
        ///      produces a readable result, even for keys not yet added to <see cref="_en"/>.
        ///
        /// Usage example in a form:
        /// <code>
        ///   btnSave.Text = Lang.T("Save");
        /// </code>
        /// </summary>
        /// <param name="key">
        /// The translation key. Conventionally this is the English text for the string,
        /// so T("Save") returns "Save" in English and "Opslaan" in Dutch.
        /// </param>
        /// <returns>The best available translation for <paramref name="key"/>.</returns>
        public static string T(string key)
        {
            // Check language-specific overrides first — they have the highest priority.
            if (_overrides.TryGetValue(key, out var ov)) return ov;

            // Fall back to the built-in English strings.
            if (_en.TryGetValue(key, out var en)) return en;

            // Last resort: return the key itself. Because keys equal the English text,
            // this guarantees the UI always shows something human-readable.
            return key;
        }

        /// <summary>
        /// Scans the application folder for all installed language files and returns them
        /// as a sorted list so the UI can populate a language-selection menu.
        ///
        /// English is always included first (it requires no file). All other languages are
        /// discovered by looking for files that match the pattern "lang_*.xml" and reading
        /// the code and name attributes from each file's root element.
        /// </summary>
        /// <returns>
        /// A list of (Code, Name) tuples, e.g. [("en","English"), ("nl","Nederlands")],
        /// sorted alphabetically by display name (case-insensitive). English is always present.
        /// </returns>
        public static List<(string Code, string Name)> GetAvailable()
        {
            // Start with English as the always-available baseline.
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

                    // Skip files with no code, and skip "en" if it somehow has a file —
                    // English is already in the list from the hard-coded entry above.
                    if (code != "" && code != "en") result.Add((code, name));
                }
                catch { }
                // Silently skip any file that cannot be parsed (corrupt XML, wrong format, etc.)
                // so a broken language file does not crash the language picker.
            }

            // Sort by display name so the menu is in alphabetical order.
            result.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
}

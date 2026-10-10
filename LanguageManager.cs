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
            ["tip: New"]               = "Create a new keyboard layout with the wizard",
            ["tip: Load"]              = "Load layout file",
            ["tip: Save"]              = "Save layout file",
            ["tip: Undo"]              = "Undo last edit",
            ["tip: Redo"]              = "Redo last undone edit",
            ["tip: Edit mode"]         = "Switch to Edit mode",
            ["tip: Edit Keyboard"]     = "Keyboard settings",
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

            // ── Dialog control tooltips ──────────────────────────────
            ["tip: Color swatch"]          = "Click to open the colour picker",
            ["tip: Hex color"]             = "Type a hex colour (#RRGGBB)",
            ["tip: Font size"]             = "0 = auto-size to fit the key",
            ["tip: Border thickness"]      = "−1 = use the standard group thickness  |  0 = no border",
            ["tip: Key width"]             = "Relative width  (e.g. 1 = normal,  1.5 = 50% wider)",
            ["tip: Row span"]              = "1 = normal height  |  2 = double height (last key in column only)",
            ["tip: Record"]                = "Record a keystroke or shortcut",
            ["tip: Browse layout"]         = "Browse for a layout file",
            ["tip: Mode Text"]             = "The key types text characters",
            ["tip: Mode Key"]              = "The key sends a keyboard shortcut or special key",
            ["tip: Mode Modifier"]         = "The key acts as a Shift, Ctrl, Alt or other modifier toggle",
            ["tip: Mode Word prediction"]  = "The key shows a word prediction suggestion",
            ["tip: Mode Layout"]           = "The key switches to a different keyboard layout",
            ["tip: Add group"]             = "Create a new style group",
            ["tip: Delete group"]          = "Delete the selected group",
            ["tip: Import groups"]         = "Import groups from another layout file",
            ["tip: Opacity"]               = "Keyboard window transparency  (0 = fully opaque,  80 = nearly transparent)",
            ["tip: Manage Groups"]         = "Open the group editor",
            ["tip: Language"]              = "Select the interface language",
            ["tip: WP slot"]               = "Word prediction slot number (0–9)",

            // ── Validation error messages ────────────────────────────
            ["err: invalid hex"]           = "Enter a valid hex colour (#RRGGBB)",
            ["err: layout file not found"] = "This layout file could not be found.",
            ["warn: font not installed"]   = "The font '{0}' is not installed on this computer.",

            // ── Accessible descriptions ──────────────────────────────
            // Preview panel — updated live in Refresh2(); screen readers announce it on focus.
            ["preview: key '{0}', key colour {1}, font colour {2}, {3} {4} pt"] =
                "Preview: key '{0}', key colour {1}, font colour {2}, {3} {4} pt",
            // Import-dialog row accessible names.
            ["import row: {0}: New — will be added"]                 = "{0}: New — will be added",
            ["import row: {0}: Conflict — choose Overwrite, Add as new, or Skip"] =
                "{0}: Conflict — choose Overwrite, Add as new, or Skip",
            ["import row: {0}: Protected — choose Update or Skip"]   = "{0}: Protected — choose Update or Skip",

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
            ["Label"]                = "&Label",
            ["Layout"]               = "Layo&ut",
            // Captions of the three colour chips in the Key Editor (short: the chip is a button).
            ["chip: Font"]           = "Font",
            ["chip: Key"]            = "Key",
            ["chip: Border"]         = "Border",
            ["Send"]                 = "Send",
            ["Shift label"]          = "Sh&ift label",
            ["Shift send"]           = "Shift sen&d",
            ["AltGr label"]          = "AltGr label",
            ["AltGr send"]           = "AltGr send",
            ["Width"]                = "Width",
            ["Width hint"]           = "relative  (e.g. 1, 1.5, 2)",
            ["Row span"]             = "Row span",
            ["Row span hint"]        = "1 = normal,  2 = double height (last key only)",
            ["Auto-escape"]          = "Auto-escape SendKeys special characters",
            ["Appearance"]           = "Appearance",
            ["Font"]                 = "Font",
            ["Font size"]            = "Fo&nt size",
            ["Auto"]                 = "Auto",
            ["Font color"]           = "Font colo&r",
            ["Key color"]            = "Ke&y color",
            ["Border"]               = "Border",
            ["Border color"]         = "Border color",
            ["Border thickness"]     = "Thickness (p&x)",
            ["Border hint"]          = "0 = use global default",
            ["Preview"]              = "Preview",
            ["✔ Apply"]              = "✔  Apply",
            ["✖ Cancel"]             = "✖  Cancel",
            // Clean keys (no emoji) — these are the ones code actually calls
            ["Apply"]                = "A&pply",
            ["Cancel"]               = "&Cancel",
            ["Save"]                 = "Save",
            ["Save As…"]             = "Sa&ve As…",
            ["Load…"]                = "Load…",
            ["Import"]               = "Import",
            // ── Toolbar button text labels ───────────────────────────────
            ["tb: New"]              = "New",
            ["tb: Load"]             = "Load",
            ["tb: Save"]             = "Save",
            ["tb: Undo"]             = "Undo",
            ["tb: Redo"]             = "Redo",
            ["tb: Edit"]             = "Edit",
            ["tb: Keyboard"]         = "Settings",
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
            ["font missing title"]   = "Font Not Installed",
            ["font missing msg"]     = "This keyboard uses the following font(s), which are not installed on this computer: {0}.\n\nA substitute font is being used instead. Search online to download and install the missing font(s) if you want the keyboard to look exactly as intended.",
            // ── New UI strings ─────────────────────────────────────────────
            ["Language"]             = "Language",
            ["Hide title bar"]       = "H&ide title bar",
            ["Always on top"]        = "&Always on top",
            ["Sticky modifiers"]     = "Stic&ky modifiers",
            ["Hold to edit"]         = "&Hold to enter edit mode",
            ["Slow keys"]            = "Slow k&eys",
            ["Dwell click"]          = "&Dwell click",
            ["tip: Slow keys"]             = "Hold a key for this many milliseconds before it registers. Prevents accidental key activations.",
            ["tip: Dwell click"]           = "Hover over a key for this many milliseconds to activate it automatically, without clicking.",
            ["Show Shift and AltGr labels"]     = "Show Shi&ft and AltGr labels",
            ["tip: Show Shift and AltGr labels"] = "Show the small Shift and AltGr labels in the corners of the keys. Uncheck for less to look at: only the main label is drawn. The keys work exactly the same.",
            ["General"]                          = "General",
            ["milliseconds"]                     = "milliseconds",
            ["kbd: Timing aid"]                  = "Timing aid",
            ["kbd: Off"]                         = "Off",
            ["tip: Timing off"]                  = "No timing aid: a key registers as soon as you press it, or click it.",
            ["tip: Sticky modifiers"]            = "Tap Shift, Ctrl or Alt twice to lock it on until you tap it a third time (like Windows Sticky Keys). Off: one tap only applies to the next key.",
            ["tip: Hold to edit"]                = "Hold a key for a moment to open its editor. A quick tap still types the key, so the editor does not open by accident.",
            ["tip: Always on top"]               = "Keep the keyboard floating above all other windows.",
            ["tip: Hide title bar"]              = "Hide the Windows title bar for a cleaner floating keyboard look.",
            ["tip: Toolbar theme"]               = "Colours of the toolbar above the keyboard: dark, light, or the same as Windows.",
            ["tip: Background"]                  = "Colour of the keyboard window behind the keys. Click to choose.",
            ["tip: Save settings"]               = "Apply the changes and save the layout file.",
            ["tip: Save settings as"]            = "Apply the changes and save the layout file under a new name.",
            ["kbd: Load title"]                  = "Load a layout",
            ["kbd: Load msg"]                    = "Loading replaces the current keyboard at once. Changes made in this window are lost, and Cancel cannot undo the load.\n\nContinue?",
            ["first run: title"]                 = "Welcome",
            ["first run: intro"]                 = "Choose how the keyboard starts. You can change all of this later in Edit mode.",
            ["first run: arrangement"]           = "Key arrangement",
            ["first run: language"]              = "Language of the suggestions",
            ["first run: hint"]                  = "This is also the language of the keyboard's own menus and windows.",
            ["first run: start"]                 = "Start",
            ["wp: nothing to export"]            = "Export becomes available after words have been learned.",
            ["wp: no candidates"]                = "No candidates yet.",
            ["wp: learning off hint"]            = "Candidates are collected while \"Remember typed words\" is on.",
            ["wp: immediate"]                    = "Promote and Reject take effect at once; Cancel does not undo them.",
            ["wp: (not found)"]                  = "(not found)",
            ["warn: database not found"]         = "The database file '{0}' was not found. It stays selected until another one is chosen.",
            ["Show timing animation"]      = "Sho&w timing animation",
            ["tip: Show timing animation"] = "Show a visual fill animation on keys while slow keys or dwell click is counting down. Uncheck if you find the animation distracting.",
            ["wp: Word prediction"]   = "Word prediction",
            ["wp: Database"]          = "Database",
            ["wp: Auto"]              = "(auto)",
            ["wp: words"]             = "words",
            ["wp: No database loaded"] = "No database loaded",
            ["wp: Export…"]           = "Export…",
            ["wp: Export failed"]     = "Export failed:",
            ["wp: tip database"]      = "Choose the word database for this keyboard.",
            ["wp: tip export"]        = "Export the learned words to another location (backup or transfer to another PC).",
            ["wp: Remember typed words"] = "Remember typed words",
            ["wp: tip remember"]      = "Learn the words and word-pairs you type, so words you use often are suggested sooner. Turn off at any time for privacy.",
            ["wp: learning on"]       = "remembering words",
            ["wp: learning off"]      = "not remembering words",
            ["wp: Export learned words"] = "Export learned words",
            ["wp: Import…"]           = "Import…",
            ["wp: Import learned words"] = "Import learned words",
            ["wp: tip import"]        = "Add the learned words from a file made with Export to the words learned on this PC. Nothing learned here is removed; the counts are added together.",
            ["wp: import confirm"]    = "Add the learned words from \"{0}\" to the words learned on this PC? The counts are added together. The file as it is now is kept as a backup (.bak).",
            ["wp: Import done"]       = "Added: {0} words, {1} word pairs and {2} candidates.",
            ["wp: Import failed"]     = "Importing did not work. Nothing was changed.",
            ["wp: import not valid"]  = "This is not a file with learned words (made with Export).",
            ["wp: import empty"]      = "This file contains no learned words.",
            ["wp: import corrupt"]    = "The file could not be read.",
            ["wp: import missing"]    = "The file could not be found.",
            ["wp: import loading"]    = "The word database is still loading. Try again in a moment.",
            ["wp: import wrong language"] = "These words were learned for the language \"{0}\", but this database is for \"{1}\".",
            ["wp: import target corrupt"] = "The learned words on this PC could not be read, so nothing was added.",
            ["wp: Candidates"]        = "Candidates",
            ["wp: Promote"]           = "Promote",
            ["wp: Reject"]            = "Reject",
            ["wp: tip candidates"]    = "Unknown words seen while typing, not yet added to the word list. A word is added automatically once it has been typed more than twice.",
            ["wp: tip promote"]       = "Add the selected candidate to the word list now, without waiting for it to be typed again.",
            ["wp: tip reject"]        = "Discard the selected candidate. It will start being counted from zero if typed again.",
            ["Toolbar theme"]        = "T&oolbar theme",
            ["Dark"]                 = "Dark",
            ["Light"]                = "Light",
            ["System default"]       = "System default",
            ["Accessibility"]        = "Accessibility",
            ["Layout file"]          = "Layout file",
            ["Key width"]            = "K&ey width",
            ["Key width hint"]       = "1 = one key wide,  2 = two keys wide,  ...",
            ["Key height"]           = "Key &height",
            ["Key height hint"]      = "1 = one key tall,  2 = two keys tall,  ...",
            ["-1 = global default  |  0 = no border  |  1-10 = px"]
                                     = "-1 = global default  |  0 = no border  |  1-10 = px",
            ["📌 Move gear button…"] = "📌 Move gear button…",
            ["Send mode"]            = "Send mode",
            ["Text"]                 = "Text",
            ["Key/Shortcut"]         = "&Key/Shortcut",
            ["Modifier"]             = "M&odifier",
            ["🗂 Layout"]                = "🗂 Layout",
            ["📂 Browse (Send)"]         = "📂 Browse (Send)",
            ["📂 Browse (Shift-send)"]   = "📂 Browse (Shift-send)",
            ["📂 Browse (AltGr-send)"]   = "📂 Browse (AltGr-send)",
            ["🎹 Record key / shortcut"]          = "🎹 Record key / shortcut",
            ["⏺ Press your key or shortcut now…"] = "⏺ Press your key or shortcut now…",
            ["Perform the key combination you want on the keyboard."] = "Perform the key combination you want on the keyboard.",
            ["Stop recording"]                    = "Stop recording",
            ["tip: Stop recording"]               = "Stop recording (nothing is changed)",
            ["Cancelled"]                         = "Cancelled",
            ["Recorded — edit if needed"]    = "Recorded — edit if needed",
            ["Press 🎹 to record, or type directly"]    = "Press 🎹 to record, or type directly",
            ["Press 🎹 to re-record, or edit directly"] = "Press 🎹 to re-record, or edit directly",

            // ── Group editor ─────────────────────────────────────────────
            ["Key Groups"]              = "Key Groups",
            ["Manage Groups…"]          = "&Manage Groups…",
            ["Group"]                   = "Group",
            ["(no group)"]              = "(no group)",
            ["title: leave group"]      = "Take the key out of its group?",
            ["ask: leave group"]        = "This key follows group ‘{0}’. Changing it takes the key out of the group: it keeps its own look and no longer follows the group.",
            ["Manage Groups"]           = "Manage Groups",
            ["Groups"]                  = "Groups",
            ["Style"]                   = "Style",
            ["+ Add group"]             = "+ &Add",
            ["− Delete group"]          = "− &Delete",
            ["Name"]                    = "Na&me",
            ["(inherit)"]               = "(inherit)",
            ["0 = auto / inherit"]      = "0 = auto / inherit",
            ["(inherit standard)"]      = "(inherit standard)",
            ["Inherit from standard"]   = "Inherit from standard",
            ["Inherit"]                 = "Inherit",
            ["inherited"]               = "inherited",
            ["-1 = inherit standard"]   = "-1 = inherit standard",
            ["Clear (inherit standard)"]= "Clear (inherit standard)",
            ["(none / auto)"]           = "(none / auto)",
            ["Clear"]                   = "Clear",
            ["Delete Group"]            = "Delete Group",
            ["Delete group msg"]        = "Delete group \"{0}\"?\n\nKeys assigned to this group will revert to global style.",
            ["New Group"]               = "New Group",
            ["Name 'standard' is reserved."] = "Name 'standard' is reserved.",
            ["A group with this name already exists."] = "A group with this name already exists.",
            ["Update standard group style"]  = "Update standard group style",
            ["Protected"]               = "Protected",

            // ── New Keyboard Wizard ───────────────────────────────────────
            ["New Keyboard"]                    = "New Keyboard",
            ["wiz: p1 title"]                   = "How do you want to start?",
            ["wiz: p1 sub"]                     = "Choose the starting point for your new keyboard layout.",
            ["wiz: p2 title"]                   = "Grid & key labels",
            ["wiz: p4 title"]                   = "Theme",
            ["wiz: p4 sub"]                     = "Choose a colour theme for the keyboard.",
            ["wiz: p5 title"]                   = "Save as",
            ["wiz: p5 sub"]                     = "Choose a file name and folder, then click Create.",
            ["wiz: Blank grid"]                 = "Blank grid — I will fill in keys in the editor",
            ["wiz: Paste labels"]               = "Type or paste key labels",
            ["wiz: Copy from file"]             = "Copy from an existing layout file",
            ["wiz: Layout file"]                = "Layout file",
            ["wiz: Key labels"]                 = "Key labels",
            ["wiz: Rows"]                       = "Rows",
            ["wiz: Columns"]                    = "Columns",
            ["wiz: Preview"]                    = "Preview",
            ["wiz: Language"]                   = "Keyboard language",
            ["wiz: Theme file"]                 = "Theme file",
            ["wiz: From file…"]                 = "From file…",
            ["wiz: File name"]                  = "File name",
            ["wiz: Folder"]                     = "Folder",
            ["wiz: default filename"]           = "new keyboard",
            ["wiz: Step {0} of {1}"]            = "Step {0} of {1}",
            ["Next →"]                          = "Next →",
            ["← Back"]                          = "← Back",
            ["Create"]                          = "Create",
            ["Yes"]                             = "Yes",
            ["No"]                              = "No",
            // Theme preset display names
            ["wiz: theme dark"]                 = "Dark",
            ["wiz: theme light"]                = "Light",
            ["wiz: theme hc"]                   = "High Contrast",
            ["wiz: theme colorful"]             = "Colorful",
            // Tooltips
            ["wiz: tip Blank grid"]             = "An empty grid; you fill in the keys in the editor.",
            ["wiz: tip Paste labels"]           = "One row per line, words separated by spaces.",
            ["wiz: tip Copy from file"]         = "Starts from the keys of an existing layout file.",
            ["wiz: tip paste"]                  = "One row per line, words separated by spaces or tabs.\nSpecial keys go in square brackets, such as [Enter] or [Shift]; the ? button lists them all.\n\"quoted phrase\"  →  one key with that label\n_  →  blank spacer",
            ["wiz: tip folder"]                 = "Folder where the new layout file will be saved.",
            // Special keys help
            ["wiz: Special keys help"]          = "All special keys",
            ["wiz: Close"]                      = "Close",
            ["wiz: keys title"]                 = "Special keys in the key labels",
            ["wiz: keys intro"]                 = "Type a special key in square brackets, in any case.",
            ["wiz: keys intro 2"]               = "Any other word is a key with that text; quotes join several words into one key, and _ is an empty key.",
            ["wiz: keys group typing"]          = "Typing keys",
            ["wiz: keys group nav"]             = "Moving and editing",
            ["wiz: keys group mod"]             = "Modifier keys",
            ["wiz: keys group other"]           = "Other keys",
            ["wiz: keys group dead"]            = "Dead keys (accents)",
            ["wiz: keys dead note"]             = "A dead key puts its accent on the next letter that is typed.",
            ["wiz: paste hint 2"]               = "Special keys: [Enter] [Backspace] [Tab] [Space] [Up] [Down] [Left] [Right]",
            ["wiz: paste hint 3"]               = "\"quoted phrase\" is one key; _ is a blank key.",
            // Select dialogs
            ["wiz: Select layout"]              = "Select existing layout file",
            ["wiz: Select theme file"]          = "Select layout file to copy theme from",
            // Errors
            ["wiz: copy info"]                  = "Keys will be copied from: {0}",
            ["wiz: preview description"]        = "{0} rows, {1} columns; the last column of the first row holds the gear key.",
            ["wiz: p2 sub blank"]               = "Set how many rows and columns the keyboard has. The last column is added for the settings button.",
            ["wiz: p2 sub paste"]               = "One row per line, one key per word.",
            ["wiz: p2 sub copy"]                = "The keys come from the layout file you chose.",
            ["wiz: Starting point"]             = "Starting point",
            ["wiz: Sample keys"]                = "Sample keys",
            ["wiz: Summary"]                    = "Summary",
            ["wiz: Browse"]                     = "Browse",
            ["wiz: paste size"]                 = "{0} rows, {1} keys wide (a settings column is added)",
            ["wiz: paste nothing"]              = "No keys yet",
            ["wiz: sum copy"]                   = "Keys: copied from {0}",
            ["wiz: sum window"]                 = "Window: always on top, title bar shown",
            ["wiz: err bad name"]               = "The file name contains a character that is not allowed.",
            ["wiz: replace title"]              = "Replace file?",
            ["wiz: replace text"]               = "A file named {0} already exists in this folder. Replace it?",
            ["wiz: err no copy file"]           = "Please select a layout file to copy from.",
            ["wiz: err empty paste"]            = "Please paste at least one key before continuing.",
            ["wiz: err no theme file"]          = "Please select a layout file to copy the theme from.",
            ["wiz: err no name"]                = "Please enter a file name.",
            ["wiz: err bad folder"]             = "The selected folder does not exist.",
            // Validation results
            // Summary
            ["wiz: sum rows cols"]              = "Grid: {0} rows × {1} columns (last column reserved for the settings button)",
            ["wiz: sum theme"]                  = "Theme: {0}",
            ["wiz: sum language"]               = "Language: {0}",
        };

        // ── Active overrides for the selected non-English language ────────────

        /// <summary>
        /// Translations for the currently active language, loaded from a lang_*.xml file.
        /// Only entries that differ from English need to be present in the XML file —
        /// anything not listed here falls back to <see cref="_en"/>.
        /// This dictionary is cleared and refilled every time <see cref="Load"/> is called.
        /// </summary>
        private static Dictionary<string, string> _overrides = new Dictionary<string, string>();

        // Maximum size of a lang file accepted from any source.  512 KB is far more
        // than any real translation file will ever need (the current Dutch file is ~18 KB).
        // The limit prevents memory exhaustion if a large or malicious file is ever loaded.
        private const long MaxLangFileBytes = 512 * 1024;

        // ── Safe XML loader ───────────────────────────────────────────────────

        /// <summary>
        /// Loads a language XML file with hardened parser settings suitable for files
        /// that may originate from untrusted sources (e.g. a download server).
        ///
        /// <para>Protections applied:</para>
        /// <list type="bullet">
        ///   <item>File-size check before parsing — rejects files larger than
        ///     <see cref="MaxLangFileBytes"/> to prevent memory exhaustion.</item>
        ///   <item><c>DtdProcessing.Prohibit</c> — any DOCTYPE declaration causes an
        ///     immediate <see cref="XmlException"/>, which blocks both the
        ///     billion-laughs entity-expansion attack and external-entity (XXE) reads.</item>
        ///   <item><c>XmlResolver = null</c> on both the reader and the document —
        ///     belt-and-suspenders: no external resource is ever fetched.</item>
        ///   <item>Root-element name check — rejects files whose root is not
        ///     <c>&lt;Language&gt;</c> so garbage never reaches <c>_overrides</c>.</item>
        /// </list>
        ///
        /// <para>Throws on any failure so callers can decide whether to silently skip
        /// or report the error.</para>
        /// </summary>
        /// <exception cref="InvalidDataException">File too large or wrong root element.</exception>
        /// <exception cref="XmlException">Malformed XML or DOCTYPE present.</exception>
        private static XmlDocument LoadLangXml(string path)
        {
            // ── Size gate ────────────────────────────────────────────────────
            long fileSize = new FileInfo(path).Length;
            if (fileSize > MaxLangFileBytes)
                throw new InvalidDataException(
                    $"Language file exceeds the {MaxLangFileBytes / 1024} KB size limit " +
                    $"({fileSize / 1024} KB): {Path.GetFileName(path)}");

            // ── Parse with DTD prohibited ────────────────────────────────────
            // DtdProcessing.Prohibit throws XmlException immediately if a DOCTYPE
            // declaration is present — no entity expansion ever takes place.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver   = null,
            };
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path, settings))
                doc.Load(reader);

            // ── Structure gate ───────────────────────────────────────────────
            if (doc.DocumentElement?.Name != "Language")
                throw new InvalidDataException(
                    $"Language file root element must be <Language> " +
                    $"(found <{doc.DocumentElement?.Name ?? "none"}>): {Path.GetFileName(path)}");

            return doc;
        }

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

            // Load with hardened settings — silently keep English on any failure
            // (oversized file, malformed XML, wrong root element, etc.).
            XmlDocument doc;
            try   { doc = LoadLangXml(path); }
            catch { return; }
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
            string text = Lookup(key);
            return PseudoExpansion > 0 ? Pseudo(text) : text;
        }

        private static string Lookup(string key)
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
        /// Test / gallery hook (pseudo-localisation). When greater than 0, every string from
        /// <see cref="T"/> is lengthened by this fraction (0.4 = 40 % longer), which stands in for
        /// a language whose words are longer than English or Dutch. Used to check that dialogs
        /// size themselves from their text instead of relying on empty space. 0 = off (normal).
        /// </summary>
        internal static double PseudoExpansion { get; set; }

        /// <summary>
        /// Appends filler at the end, so mnemonics (&amp;) and {n} placeholders stay intact. A text of several words grows by more words
        /// ("xxxxx xxxxx …"), as a real translation does, so it can still wrap between them; one word (a button, a short label) grows by letters.
        /// An unbroken run of x's as long as a whole sentence made a wrapping label impossible to fit, which no translation does.
        /// </summary>
        private static string Pseudo(string text)
        {
            int extra = (int)Math.Ceiling(text.Length * PseudoExpansion);
            if (extra <= 0) return text;
            if (text.IndexOf(' ') < 0) return text + new string('x', extra);
            var sb = new System.Text.StringBuilder(text);
            while (extra > 0) { int n = Math.Min(extra - 1 > 0 ? extra - 1 : 1, 5); sb.Append(' ').Append('x', n); extra -= n + 1; }
            return sb.ToString();
        }

        /// <summary>
        /// Strips the WinForms mnemonic marker (<c>&amp;</c>) from a text string so it
        /// can be used as a control's <see cref="System.Windows.Forms.Control.AccessibleName"/>
        /// without confusing screen readers with a literal ampersand.
        /// </summary>
        /// <param name="text">The text to strip, e.g. <c>"&amp;Cancel"</c>.</param>
        /// <returns>The text with every <c>&amp;</c> character removed, e.g. <c>"Cancel"</c>.</returns>
        public static string StripMnemonic(string text) => text?.Replace("&", "") ?? "";

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
                    var doc  = LoadLangXml(file);   // throws on oversized / DTD / wrong root
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

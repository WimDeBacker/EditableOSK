using System;
using System.Collections.Generic;

namespace OnScreenKeyboard
{
    // ══════════════════════════════════════════════════════════════════════
    // WizardKeyParser
    // Parses the paste-text input from the New Keyboard Wizard into rows of
    // key specs that can be turned directly into GridCell / KeyProps objects.
    //
    // Syntax (one row per line, tokens separated by whitespace):
    //   word          → Label = "word",  Send = "word"
    //   "two words"   → Label = "two words", Send = "two words" (quoted phrase)
    //   [up]          → arrow key  (see SpecialKeys table)
    //   [omhoog]      → same as [up] (Dutch alias)
    //   [shift] [ctrl] [alt] [altgr] [win] [caps], [home] [end] [pageup] [pagedown] [insert] [printscreen] [numlock]
    //                 [scrolllock] [pause], [f1] … [f16]: see the SpecialKeys table (any case)
    //   _             → blank spacer (no label, no send)
    //   ""            → same as _ (an empty quoted phrase is a blank spacer, so a gap written
    //                   the CSV / spreadsheet way keeps its column instead of shifting the
    //                   rest of the row left)
    //   Empty lines are silently skipped.
    // ══════════════════════════════════════════════════════════════════════
    internal static class WizardKeyParser
    {
        // ── Parsed result type ────────────────────────────────────────────

        /// <summary>
        /// A single key produced by the parser.
        /// <paramref name="IsBlank"/> = true for underscore spacers (no label, no send).
        /// </summary>
        internal readonly struct KeySpec
        {
            public readonly string Label;
            public readonly string Send;
            public readonly bool   IsBlank;

            public KeySpec(string label, string send, bool isBlank = false)
            {
                Label   = label;
                Send    = send;
                IsBlank = isBlank;
            }

            public static readonly KeySpec Blank = new KeySpec("", "", isBlank: true);
        }

        // ── Special-key lookup table ──────────────────────────────────────

        // Tuple: (SendKeys code, English label, Dutch label)
        // Dutch label is the same as English for symbol keys; differs for [space]/[spatie].
        private static readonly Dictionary<string, (string Send, string LabelEn, string LabelNl)>
            SpecialKeys = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["up"]        = ("{UP}",        "↑",      "↑"),
            ["omhoog"]    = ("{UP}",        "↑",      "↑"),
            ["down"]      = ("{DOWN}",      "↓",      "↓"),
            ["omlaag"]    = ("{DOWN}",      "↓",      "↓"),
            ["left"]      = ("{LEFT}",      "←",      "←"),
            ["links"]     = ("{LEFT}",      "←",      "←"),
            ["right"]     = ("{RIGHT}",     "→",      "→"),
            ["rechts"]    = ("{RIGHT}",     "→",      "→"),
            ["enter"]     = ("{ENTER}",     "↵",      "↵"),
            ["backspace"] = ("{BACKSPACE}", "⌫",      "⌫"),
            ["tab"]       = ("{TAB}",       "⇥",      "⇥"),
            ["space"]     = (" ",           "Space",  "Space"),
            ["spatie"]    = (" ",           "Space",  "Spatie"),
            ["esc"]       = ("{ESC}",       "Esc",    "Esc"),
            ["escape"]    = ("{ESC}",       "Esc",    "Esc"),
            ["delete"]    = ("{DELETE}",    "Del",    "Del"),
            ["del"]       = ("{DELETE}",    "Del",    "Del"),

            // Navigation and editing keys (SendKeys names).
            ["home"]        = ("{HOME}",   "Home",  "Home"),
            ["end"]         = ("{END}",    "End",   "End"),
            ["einde"]       = ("{END}",    "End",   "End"),
            ["pageup"]      = ("{PGUP}",   "PgUp",  "PgUp"),
            ["pgup"]        = ("{PGUP}",   "PgUp",  "PgUp"),
            ["pagedown"]    = ("{PGDN}",   "PgDn",  "PgDn"),
            ["pgdn"]        = ("{PGDN}",   "PgDn",  "PgDn"),
            ["insert"]      = ("{INSERT}", "Ins",   "Ins"),
            ["ins"]         = ("{INSERT}", "Ins",   "Ins"),
            ["invoegen"]    = ("{INSERT}", "Ins",   "Ins"),
            ["printscreen"] = ("{PRTSC}",  "PrtSc", "PrtSc"),
            ["prtsc"]       = ("{PRTSC}",  "PrtSc", "PrtSc"),
            ["numlock"]     = ("{NUMLOCK}",    "NumLk", "NumLk"),
            ["scrolllock"]  = ("{SCROLLLOCK}", "ScrLk", "ScrLk"),
            ["pause"]       = ("{BREAK}",  "Pause", "Pause"),
            ["break"]       = ("{BREAK}",  "Pause", "Pause"),

            // Modifier keys. The labels must be exactly the ones in KeyLayout.ModifierLabels: the keyboard recognises a modifier
            // by its label, and the send text is the one the stock layouts use (Shift and AltGr send nothing themselves).
            ["shift"]    = ("",         "Shift", "Shift"),
            ["ctrl"]     = ("^",        "Ctrl",  "Ctrl"),
            ["control"]  = ("^",        "Ctrl",  "Ctrl"),
            ["alt"]      = ("%",        "Alt",   "Alt"),
            ["altgr"]    = ("",         "AltGr", "AltGr"),
            ["win"]      = ("win:",     "Win",   "Win"),
            ["windows"]  = ("win:",     "Win",   "Win"),
            ["caps"]     = ("{CAPSLOCK}", "Caps", "Caps"),
            ["capslock"] = ("{CAPSLOCK}", "Caps", "Caps"),
        };

        /// <summary>The function keys F1 to F16 ([f1] … [f16], any case); null when <paramref name="token"/> is none of them.</summary>
        private static (string Send, string LabelEn, string LabelNl)? FunctionKey(string token)
        {
            if (token.Length < 2 || token.Length > 3 || (token[0] != 'f' && token[0] != 'F')) return null;
            if (!int.TryParse(token.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)) return null;
            if (n < 1 || n > 16 || token[1] == '0') return null;
            return ("{F" + n + "}", "F" + n, "F" + n);
        }

        // Dead keys: the accent that SendKeysHelper can compose onto the next letter, and the names that stand for them.
        // Written [dead:^] (any one of the five characters) or by name.
        internal const string DeadChars = "^¨~`´";
        private static readonly Dictionary<string, char> DeadNames = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
        {
            ["circumflex"] = '^', ["diaeresis"] = '¨', ["umlaut"] = '¨', ["trema"] = '¨',
            ["tilde"] = '~', ["grave"] = '`', ["acute"] = '´',
        };

        private static (string Send, string LabelEn, string LabelNl)? DeadKey(string token)
        {
            char ch;
            if (token.Length == 6 && token.StartsWith("dead:", StringComparison.OrdinalIgnoreCase)) ch = token[5];
            else if (!DeadNames.TryGetValue(token, out ch)) return null;
            if (DeadChars.IndexOf(ch) < 0) return null;
            return ("dead:" + ch, ch.ToString(), ch.ToString());
        }

        /// <summary>The key a bracket token stands for ([enter], [f5], [dead:^] …); null when it is none of them (then it is plain text).</summary>
        private static (string Send, string LabelEn, string LabelNl)? Lookup(string token)
        {
            if (SpecialKeys.TryGetValue(token, out var spec)) return spec;
            return FunctionKey(token) ?? DeadKey(token);
        }

        // ── Help (the "?" window of the wizard) ───────────────────────────

        /// <summary>One line of the help window: the text to type and the group it belongs to. <see cref="Tokens"/> are all spellings (without brackets).</summary>
        internal sealed class HelpRow
        {
            public string GroupKey, Display; public string[] Tokens;
            public HelpRow(string group, string display, params string[] tokens) { GroupKey = group; Display = display; Tokens = tokens; }
        }

        /// <summary>Every special key the parser knows, grouped for the help window. A test checks that it matches the parser exactly.</summary>
        internal static readonly HelpRow[] Help = BuildHelp();

        private static HelpRow[] BuildHelp()
        {
            var rows = new List<HelpRow>();
            // The window shows at most two spellings of a key (the line must stay short); every spelling in Tokens is accepted.
            void Add(string group, params string[] tokens) =>
                rows.Add(new HelpRow(group, string.Join(" or ", Array.ConvertAll(tokens, t => "[" + t + "]"), 0, Math.Min(2, tokens.Length)), tokens));
            Add("typing", "enter"); Add("typing", "backspace"); Add("typing", "tab"); Add("typing", "space", "spatie");
            Add("typing", "esc", "escape"); Add("typing", "delete", "del");
            Add("nav", "up", "omhoog"); Add("nav", "down", "omlaag"); Add("nav", "left", "links"); Add("nav", "right", "rechts");
            Add("nav", "home"); Add("nav", "end", "einde"); Add("nav", "pageup", "pgup"); Add("nav", "pagedown", "pgdn");
            Add("nav", "insert", "ins", "invoegen");
            Add("mod", "shift"); Add("mod", "ctrl", "control"); Add("mod", "alt"); Add("mod", "altgr"); Add("mod", "win", "windows");
            Add("mod", "caps", "capslock");
            var f = new string[16];
            for (int n = 1; n <= 16; n++) f[n - 1] = "f" + n;
            rows.Add(new HelpRow("other", "[f1] … [f16]", f));       // with the other keys: a group of its own made the window two rows taller
            Add("other", "printscreen", "prtsc"); Add("other", "numlock"); Add("other", "scrolllock"); Add("other", "pause", "break");
            // The window shows two spellings of each dead key; the parser also accepts [diaeresis] and [trema] for [dead:¨].
            rows.Add(new HelpRow("dead", "[dead:^] or [circumflex]", "dead:^", "circumflex"));
            rows.Add(new HelpRow("dead", "[dead:¨] or [umlaut]", "dead:¨", "diaeresis", "umlaut", "trema"));
            rows.Add(new HelpRow("dead", "[dead:~] or [tilde]", "dead:~", "tilde"));
            rows.Add(new HelpRow("dead", "[dead:`] or [grave]", "dead:`", "grave"));
            rows.Add(new HelpRow("dead", "[dead:´] or [acute]", "dead:´", "acute"));
            return rows.ToArray();
        }

        /// <summary>The label a bracket token gives its key (null when the token is not a special key).</summary>
        internal static string LabelOf(string token, bool dutch) => Lookup(token) is { } k ? (dutch ? k.LabelNl : k.LabelEn) : null;

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Parses a multiline string into a list of rows, each row being a list of
        /// <see cref="KeySpec"/> objects ready for grid construction.
        /// </summary>
        /// <param name="text">The raw pasted text from the wizard text box.</param>
        /// <param name="dutch">
        /// When true, localised Dutch labels are used (e.g. "Spatie" instead of "Space").
        /// </param>
        /// <returns>
        /// One inner list per non-empty input line; inner lists are never empty.
        /// Returns an empty outer list when <paramref name="text"/> is blank.
        /// </returns>
        public static List<List<KeySpec>> Parse(string text, bool dutch = false)
        {
            var rows = new List<List<KeySpec>>();
            if (string.IsNullOrWhiteSpace(text)) return rows;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim('\r', ' ', '\t');
                if (line.Length == 0) continue;

                var row = new List<KeySpec>();
                ParseLine(line, dutch, row);
                if (row.Count > 0) rows.Add(row);
            }
            return rows;
        }

        // ── Internal helpers ──────────────────────────────────────────────

        private static void ParseLine(string line, bool dutch, List<KeySpec> row)
        {
            int i = 0;
            int len = line.Length;

            while (i < len)
            {
                // Skip leading whitespace between tokens.
                while (i < len && char.IsWhiteSpace(line[i])) i++;
                if (i >= len) break;

                char c = line[i];

                if (c == '_')
                {
                    // Blank spacer.
                    row.Add(KeySpec.Blank);
                    i++;
                }
                else if (c == '[')
                {
                    // Special-key token: [xxx]
                    int close = line.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        // Unclosed bracket → treat the remainder as one text key.
                        string rest = line[i..];
                        row.Add(new KeySpec(rest, rest));
                        break;
                    }
                    string token = line[(i + 1)..close];
                    i = close + 1;

                    if (Lookup(token) is { } spec)
                    {
                        string label = dutch ? spec.LabelNl : spec.LabelEn;
                        row.Add(new KeySpec(label, spec.Send));
                    }
                    else
                    {
                        // Unknown bracket token → use verbatim as text.
                        row.Add(new KeySpec(token, token));
                    }
                }
                else if (c == '"')
                {
                    // Quoted phrase: "hello world" → single key.
                    int close = line.IndexOf('"', i + 1);
                    if (close < 0)
                    {
                        // Unclosed quote → take everything after the opening quote.
                        string phrase = line[(i + 1)..];
                        if (phrase.Length > 0) row.Add(new KeySpec(phrase, phrase));
                        break;
                    }
                    string quoted = line[(i + 1)..close];
                    i = close + 1;
                    // A closed empty phrase ("") is a deliberate gap, not nothing: reserve its
                    // column exactly like "_" does. Dropping it silently shifted every key after
                    // it one column to the left. (An unclosed lone quote, above, is left alone:
                    // the preview re-parses on every keystroke and half-typed input must not
                    // flicker a blank key into the row.)
                    row.Add(quoted.Length > 0 ? new KeySpec(quoted, quoted) : KeySpec.Blank);
                }
                else
                {
                    // Plain word: read until next whitespace.
                    int start = i;
                    while (i < len && !char.IsWhiteSpace(line[i])) i++;
                    string word = line[start..i];
                    row.Add(new KeySpec(word, word));
                }
            }
        }
    }
}

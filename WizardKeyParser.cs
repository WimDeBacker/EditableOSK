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
    //   a  or  A      → a single letter is always the small letter ("a"), and the capital ("A") with Shift
//   [A]           → a single letter in brackets is that letter exactly as typed: a capital on the key itself, no Shift variant
//                   (no special key is one letter long, so this cannot be mixed up with [enter], [f5] …)
//   [wp]          → a word-prediction key; the numbers (wp:0, wp:1 …) are filled in by the order of the keys (at most 10)
//   [layout:file.kbl] → a key that switches to another layout file
//   [ctrl+c] [alt+f4] [ctrl+shift+esc] [win+d] → a shortcut (Ctrl, Alt, Shift, Win, then one key)
//   e[shift:É] e[altgr:€] → extra layers attached to a key (no space): what it shows and sends with Shift / AltGr
//   [key:label|send|shift label|shift send|altgr label|altgr send] → every layer written out (\| and \] for those characters)
//   q*2  [wp]*2  [enter]*2x2 → a key two cells wide (and two tall): the size comes last
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
            /// <summary>What the key shows and sends with Shift; null: the same as without (only a single letter has them).</summary>
            public readonly string ShiftLabel, ShiftSend;
            /// <summary>What the key shows and sends with AltGr; null: nothing of its own.</summary>
            public readonly string AltGrLabel, AltGrSend;
            /// <summary>A word-prediction key: its number (<c>wp:n</c>) is filled in when the grid is built, in the order of the keys.</summary>
            public readonly bool IsWordPrediction;
            /// <summary>Cells wide and tall (0 is read as 1: a default value).</summary>
            public readonly int ColSpan, RowSpan;

            public KeySpec(string label, string send, bool isBlank = false, string shiftLabel = null, string shiftSend = null,
                           string altGrLabel = null, string altGrSend = null, bool isWordPrediction = false, int colSpan = 1, int rowSpan = 1)
            {
                Label   = label;
                Send    = send;
                IsBlank = isBlank;
                ShiftLabel = shiftLabel;
                ShiftSend  = shiftSend;
                AltGrLabel = altGrLabel;
                AltGrSend  = altGrSend;
                IsWordPrediction = isWordPrediction;
                ColSpan = Math.Max(1, colSpan);
                RowSpan = Math.Max(1, rowSpan);
            }

            internal KeySpec WithShift(string label, string send) =>
                new KeySpec(Label, Send, IsBlank, label, send, AltGrLabel, AltGrSend, IsWordPrediction, ColSpan, RowSpan);
            internal KeySpec WithAltGr(string label, string send) =>
                new KeySpec(Label, Send, IsBlank, ShiftLabel, ShiftSend, label, send, IsWordPrediction, ColSpan, RowSpan);
            internal KeySpec WithSpan(int cols, int rows) =>
                new KeySpec(Label, Send, IsBlank, ShiftLabel, ShiftSend, AltGrLabel, AltGrSend, IsWordPrediction, cols, rows);

            /// <summary>A word-prediction key; the label ("Word 1"…) and the number are given when the grid is built.</summary>
            internal static KeySpec WordPrediction() => new KeySpec("", "wp:", isWordPrediction: true);

            /// <summary>
            /// A key for one letter: the small letter normally, the capital with Shift. A letter without a capital (or whose capital is the
            /// same) has no Shift variant.
            /// </summary>
            internal static KeySpec Letter(char ch)
            {
                string lower = char.ToLowerInvariant(ch).ToString(), upper = char.ToUpperInvariant(ch).ToString();
                return upper == lower ? new KeySpec(lower, lower) : new KeySpec(lower, lower, false, upper, upper);
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
            // by its label, and a modifier key sends nothing itself (the Key Editor shows a key like that as a modifier). Caps used
            // to carry {CAPSLOCK} (the stock layouts still do), but it is never sent: Caps Lock is a latch of the keyboard.
            ["shift"]    = ("",         "Shift", "Shift"),
            ["ctrl"]     = ("",         "Ctrl",  "Ctrl"),
            ["control"]  = ("",         "Ctrl",  "Ctrl"),
            ["alt"]      = ("",         "Alt",   "Alt"),
            ["altgr"]    = ("",         "AltGr", "AltGr"),
            ["win"]      = ("",         "Win",   "Win"),
            ["windows"]  = ("",         "Win",   "Win"),
            ["caps"]     = ("",         "Caps",  "Caps"),
            ["capslock"] = ("",         "Caps",  "Caps"),
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

        /// <summary>The special key (send text and labels) a bracket token names; null when it is none.</summary>
        internal static (string Send, string LabelEn, string LabelNl)? LookupSend(string token) => Lookup(token);

        /// <summary>The first spelling of the special key that sends <paramref name="send"/> (<c>{ENTER}</c> → "enter", <c>{F5}</c> → "f5"); null when there is none.</summary>
        internal static string NameOfSend(string send)
        {
            foreach (var kv in SpecialKeys) if (kv.Value.Send == send && send.Length > 2 && send[0] == '{') return kv.Key;
            for (int n = 1; n <= 16; n++) if (send == "{F" + n + "}") return "f" + n;
            return null;
        }

        /// <summary>The first spelling of the special key with exactly this label and send text (<c>↵</c> + <c>{ENTER}</c> → "enter"); null when there is none.</summary>
        internal static string NameOfKey(string label, string send)
        {
            foreach (var kv in SpecialKeys)
                if (kv.Value.Send == send && (kv.Value.LabelEn == label || kv.Value.LabelNl == label)) return kv.Key;
            for (int n = 1; n <= 16; n++) if (send == "{F" + n + "}" && label == "F" + n) return "f" + n;
            if (send.StartsWith("dead:", StringComparison.Ordinal) && send.Length == 6 && label == send[5].ToString() && DeadChars.IndexOf(send[5]) >= 0) return send;
            return null;
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
                KeySpec key;

                if (c == '_')
                {
                    // Blank spacer.
                    key = KeySpec.Blank;
                    i++;
                }
                else if (c == '[')
                {
                    // Special-key token: [xxx]. [key:…] has its own escapes (a backslash takes the next character literally).
                    int close = FindClose(line, i, escapes: StartsAt(line, i + 1, "key:"));
                    if (close < 0)
                    {
                        // Unclosed bracket → treat the remainder as one text key.
                        string rest = line[i..];
                        row.Add(new KeySpec(rest, rest));
                        break;
                    }
                    string token = line[(i + 1)..close];
                    i = close + 1;
                    key = ResolveBracket(token, dutch);
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
                    key = quoted.Length > 0 ? new KeySpec(quoted, quoted) : KeySpec.Blank;
                }
                else
                {
                    // Plain word: read until the next whitespace, or until a layer ([shift:…] / [altgr:…]) is attached to it.
                    int start = i;
                    while (i < len && !char.IsWhiteSpace(line[i]) && !(i > start && IsLayerAt(line, i))) i++;
                    string word = line[start..i];
                    int cols = 1, rows = 1;
                    if (i >= len || char.IsWhiteSpace(line[i])) TrySplitSpan(ref word, out cols, out rows);     // a-b*2 or w*2x2 at the end of the word
                    // One letter is always the small letter, with the capital on Shift; a capital that must be on the key itself is [A].
                    key = word.Length == 1 && char.IsLetter(word[0]) ? KeySpec.Letter(word[0]) : new KeySpec(word, word);
                    if (cols > 1 || rows > 1) key = key.WithSpan(cols, rows);
                }

                // What may follow a key without a space: the layers of Shift and AltGr, then the size (*2 wide, *2x2 wide and tall).
                while (i < len && IsLayerAt(line, i))
                {
                    int close = FindClose(line, i, escapes: true);
                    if (close < 0) break;
                    bool shift = StartsAt(line, i + 1, "shift:");
                    string text = Unescape(line[(i + 7)..close]);
                    i = close + 1;
                    if (key.IsBlank) continue;
                    key = shift ? key.WithShift(text, text) : key.WithAltGr(text, text);
                }
                if (i < len && line[i] == '*' && ReadSpan(line, ref i, out int sc, out int sr)) key = key.WithSpan(sc, sr);
                row.Add(key);
            }
        }

        // ── The bracket tokens beyond the special keys ────────────────────

        /// <summary>The key a bracket token stands for: a special key, <c>[wp]</c>, <c>[layout:path]</c>, <c>[key:…]</c>, a shortcut, else the text itself.</summary>
        private static KeySpec ResolveBracket(string token, bool dutch)
        {
            if (Lookup(token) is { } spec) return new KeySpec(dutch ? spec.LabelNl : spec.LabelEn, spec.Send);
            if (token.Equals("wp", StringComparison.OrdinalIgnoreCase)) return KeySpec.WordPrediction();
            if (StartsAt(token, 0, "layout:") && token.Length > 7 && token[7..].Trim().Length > 0)
            {
                string path = token[7..].Trim();
                return new KeySpec(System.IO.Path.GetFileNameWithoutExtension(path), "layout:" + path);
            }
            if (StartsAt(token, 0, "key:")) return FromFields(token[4..]);
            if (Shortcut.TryParse(token, out string slabel, out string ssend)) return new KeySpec(slabel, ssend);
            return new KeySpec(token, token);        // unknown bracket token → used verbatim as text
        }

        /// <summary><c>[key:label|send|shift label|shift send|altgr label|altgr send]</c>: every layer written out (what the other forms cannot say).</summary>
        private static KeySpec FromFields(string body)
        {
            var f = new List<string>();
            var cur = new System.Text.StringBuilder();
            for (int j = 0; j < body.Length; j++)
            {
                if (body[j] == '\\' && j + 1 < body.Length) { cur.Append(body[++j]); continue; }
                if (body[j] == '|') { f.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(body[j]);
            }
            f.Add(cur.ToString());
            string At(int n) => n < f.Count && f[n].Length > 0 ? f[n] : null;
            string label = At(0) ?? "", send = f.Count > 1 ? (At(1) ?? "") : label;       // one field only: the key sends what it shows
            if (label.Length == 0 && send.Length == 0) return KeySpec.Blank;
            if (send == "wp:") return KeySpec.WordPrediction();
            return new KeySpec(label, send, false, At(2), At(3), At(4), At(5));
        }

        private static bool StartsAt(string s, int i, string prefix) =>
            i >= 0 && i + prefix.Length <= s.Length && string.Compare(s, i, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;

        /// <summary>A layer starts at <paramref name="i"/>: <c>[shift:</c> or <c>[altgr:</c>.</summary>
        private static bool IsLayerAt(string line, int i) => line[i] == '[' && (StartsAt(line, i + 1, "shift:") || StartsAt(line, i + 1, "altgr:"));

        /// <summary>The ']' that closes the '[' at <paramref name="open"/>; with <paramref name="escapes"/> a backslash takes the next character literally.</summary>
        private static int FindClose(string line, int open, bool escapes)
        {
            for (int j = open + 1; j < line.Length; j++)
            {
                if (escapes && line[j] == '\\' && j + 1 < line.Length) { j++; continue; }
                if (line[j] == ']') return j;
            }
            return -1;
        }

        internal static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder();
            for (int j = 0; j < s.Length; j++) sb.Append(s[j] == '\\' && j + 1 < s.Length ? s[++j] : s[j]);
            return sb.ToString();
        }

        /// <summary>Reads <c>*3</c> or <c>*3x2</c> (columns, then rows) at <paramref name="i"/> (which is the '*').</summary>
        private static bool ReadSpan(string line, ref int i, out int cols, out int rows)
        {
            cols = rows = 1;
            int j = i + 1, s = j;
            while (j < line.Length && char.IsAsciiDigit(line[j])) j++;
            if (j == s || j - s > 2) return false;
            cols = Math.Clamp(int.Parse(line.AsSpan(s, j - s)), 1, MaxSpan);
            if (j + 1 < line.Length && (line[j] == 'x' || line[j] == 'X') && char.IsAsciiDigit(line[j + 1]))
            {
                int s2 = ++j;
                while (j < line.Length && char.IsAsciiDigit(line[j])) j++;
                if (j - s2 <= 2) rows = Math.Clamp(int.Parse(line.AsSpan(s2, j - s2)), 1, MaxSpan);
            }
            if (j < line.Length && !char.IsWhiteSpace(line[j])) return false;      // *2abc is not a size
            i = j;
            return true;
        }

        private const int MaxSpan = 60;

        /// <summary>Takes a size off the end of a plain word ("q*2" is the key q, two wide); a word that is only a size ("*2", "**") stays as it is.</summary>
        private static void TrySplitSpan(ref string word, out int cols, out int rows)
        {
            cols = rows = 1;
            int star = word.LastIndexOf('*');
            if (star <= 0) return;
            int at = star;
            if (!ReadSpan(word, ref at, out int c, out int r) || at != word.Length) return;
            word = word[..star]; cols = c; rows = r;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Shortcut — [ctrl+c], [alt+f4], [ctrl+shift+esc], [win+d]: the modifiers, then one key.
    // Sends the SendKeys way: ^ Ctrl, % Alt, + Shift; the Windows key goes through "win:" (SendKeys has no prefix for it).
    // ══════════════════════════════════════════════════════════════════════
    internal static class Shortcut
    {
        private static readonly (string Name, string Label)[] Mods = { ("ctrl", "Ctrl"), ("alt", "Alt"), ("shift", "Shift"), ("win", "Win") };

        private static readonly int[] LabelOrder = { 3, 0, 1, 2 };      // Win, Ctrl, Alt, Shift

        private static int ModIndex(string name) => name.ToLowerInvariant() switch
        {
            "ctrl" or "control" => 0, "alt" => 1, "shift" => 2, "win" or "windows" => 3, _ => -1,
        };

        /// <summary>The label and the send text of a shortcut written as <c>ctrl+c</c>; false when the text is not one.</summary>
        internal static bool TryParse(string token, out string label, out string send)
        {
            label = send = null;
            var mods = new bool[4];
            string rest = token;
            int count = 0;
            while (true)
            {
                int plus = rest.IndexOf('+');
                if (plus <= 0) break;
                int m = ModIndex(rest[..plus]);
                if (m < 0) break;
                if (mods[m]) return false;
                mods[m] = true; count++; rest = rest[(plus + 1)..];
            }
            if (count == 0 || rest.Length == 0) return false;

            string keyLabel, keySend;
            if (rest.Length == 1) { keyLabel = rest.ToUpperInvariant(); keySend = "+^%~(){}[]".IndexOf(rest[0]) >= 0 ? "{" + rest + "}" : rest; }
            else if (WizardKeyParser.LookupSend(rest) is { } k && k.Send.Length > 2 && k.Send[0] == '{') { keyLabel = k.LabelEn; keySend = k.Send; }
            else return false;

            string prefix = (mods[0] ? "^" : "") + (mods[1] ? "%" : "") + (mods[2] ? "+" : "");
            send = (mods[3] ? "win:" : "") + prefix + keySend;
            var names = new List<string>();
            foreach (int m in LabelOrder) if (mods[m]) names.Add(Mods[m].Label);        // Win+Shift+S, Ctrl+Alt+Del
            names.Add(keyLabel);
            label = string.Join("+", names);
            return true;
        }

        /// <summary>The text of a shortcut for a key with this label and send text (<c>ctrl+c</c>); null when the key is not one that <see cref="TryParse"/> would give back.</summary>
        internal static string Format(string label, string send)
        {
            if (string.IsNullOrEmpty(send)) return null;
            bool win = send.StartsWith("win:", StringComparison.Ordinal);
            string s = win ? send[4..] : send;
            var mods = new bool[4];
            mods[3] = win;
            int p = 0;
            while (p < s.Length - 1 && (s[p] == '^' || s[p] == '%' || s[p] == '+'))
            { mods[s[p] == '^' ? 0 : s[p] == '%' ? 1 : 2] = true; p++; }
            string key = s[p..], name;
            if (key.Length == 1) name = key.ToLowerInvariant();
            else if (key.Length == 3 && key[0] == '{' && key[2] == '}') name = key[1].ToString().ToLowerInvariant();
            else name = WizardKeyParser.NameOfSend(key);
            if (name == null || (!mods[0] && !mods[1] && !mods[2] && !mods[3])) return null;
            var parts = new List<string>();
            for (int m = 0; m < 4; m++) if (mods[m]) parts.Add(Mods[m].Name);
            parts.Add(name);
            string token = string.Join("+", parts);
            return TryParse(token, out string l, out string sd) && l == label && sd == send ? token : null;       // only when it reads back as the same key
        }
    }
}

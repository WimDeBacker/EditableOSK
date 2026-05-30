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
    //   _             → blank spacer (no label, no send)
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
        };

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

                    if (SpecialKeys.TryGetValue(token, out var spec))
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
                    if (quoted.Length > 0) row.Add(new KeySpec(quoted, quoted));
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

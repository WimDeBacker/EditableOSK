// LayoutText.cs — a keyboard as plain text, and back. The text is the notation of the wizard's paste box (WizardKeyParser): one row per line,
// one key per token, so a keyboard exported as text can be edited by hand and pasted into the wizard again.
//   BuildGrid — the parsed rows → a grid (sizes, word-prediction numbering, the settings column of the wizard, blank cells where nothing is).
//   Export    — a grid → the text. Every key is written in the shortest form that reads back as the same key; a key that no short form can
//               say is written with all its layers as [key:label|send|shift label|shift send|altgr label|altgr send].
// What the text holds: the place, size, labels and outputs of the keys. What it leaves out: colours, fonts and groups, the window and
// the settings (they belong to the .kbl file; a keyboard made from text gets the style chosen in the wizard).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OnScreenKeyboard
{
    internal static class LayoutText
    {
        /// <summary>The most word-prediction keys a keyboard has (slots 0 to 9).</summary>
        internal const int MaxWordKeys = 10;

        // ── Text → grid ───────────────────────────────────────────────────

        /// <summary>
        /// Places the keys: left to right in each row, skipping the cells a key from a row above (a tall key) already covers. The grid gets one
        /// extra column at the right for the settings (gear) button, and a blank cell wherever no key is.
        /// </summary>
        internal static GridLayout BuildGrid(List<List<WizardKeyParser.KeySpec>> rows, bool dutch)
        {
            var occupied = new HashSet<(int, int)>();
            var placed = new List<(int R, int C, int RS, int CS, WizardKeyParser.KeySpec K)>();
            int maxCol = 0, maxRow = Math.Max(1, rows.Count);
            for (int r = 0; r < rows.Count; r++)
            {
                int c = 0;
                foreach (var k in rows[r])
                {
                    while (occupied.Contains((r, c))) c++;
                    int cs = Math.Max(1, k.ColSpan), rs = Math.Max(1, k.RowSpan);
                    for (int cc = 1; cc < cs; cc++) if (occupied.Contains((r, c + cc))) { cs = cc; break; }       // a size never covers another key
                    for (int rr = 1; rr < rs; rr++)
                    {
                        bool free = true;
                        for (int cc = 0; cc < cs; cc++) if (occupied.Contains((r + rr, c + cc))) free = false;
                        if (!free) { rs = rr; break; }
                    }
                    for (int rr = 0; rr < rs; rr++) for (int cc = 0; cc < cs; cc++) occupied.Add((r + rr, c + cc));
                    placed.Add((r, c, rs, cs, k));
                    maxCol = Math.Max(maxCol, c + cs); maxRow = Math.Max(maxRow, r + rs);
                    c += cs;
                }
            }
            int totalCols = maxCol + 1;                       // the last column of the first row is for the settings button
            var layout = new GridLayout(maxRow, totalCols);
            layout.Cells.Add(new GridCell(0, totalCols - 1, new KeyProps("", "")));
            occupied.Add((0, totalCols - 1));

            int slot = 0;
            foreach (var (r, c, rs, cs, k) in placed)
            {
                KeyProps p;
                if (k.IsBlank || (k.IsWordPrediction && slot >= MaxWordKeys)) p = new KeyProps("", "");
                else if (k.IsWordPrediction)
                {
                    p = new KeyProps((dutch ? "Woord " : "Word ") + (slot + 1), "wp:" + slot);
                    slot++;
                }
                else
                {
                    p = new KeyProps(k.Label, k.Send);
                    if (!string.IsNullOrEmpty(k.ShiftLabel) || !string.IsNullOrEmpty(k.ShiftSend)) { p.ShiftLabel = k.ShiftLabel ?? k.ShiftSend; p.ShiftSend = k.ShiftSend ?? k.ShiftLabel; }
                    if (!string.IsNullOrEmpty(k.AltGrLabel) || !string.IsNullOrEmpty(k.AltGrSend)) { p.AltGrLabel = k.AltGrLabel ?? k.AltGrSend; p.AltGrSend = k.AltGrSend ?? k.AltGrLabel; }
                }
                layout.Cells.Add(new GridCell(r, c, p, rs, cs));
            }
            for (int r = 0; r < layout.Rows; r++)
                for (int c = 0; c < layout.Cols; c++)
                    if (!occupied.Contains((r, c))) layout.Cells.Add(new GridCell(r, c, new KeyProps("", "")));
            return layout;
        }

        // ── Grid → text ───────────────────────────────────────────────────

        /// <summary>
        /// The layout as text. The empty settings button (<paramref name="gearRow"/>, <paramref name="gearCol"/>; a column of -1 is the last one)
        /// is left out: the wizard adds it again. Lines end with a line feed.
        /// </summary>
        internal static string Export(GridLayout layout, int gearRow = 0, int gearCol = -1)
        {
            if (gearCol < 0) gearCol = layout.Cols - 1;
            var cells = layout.Cells.OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();
            var gear = cells.FirstOrDefault(c => c.Covers(gearRow, gearCol));
            bool dropGear = gear != null && string.IsNullOrEmpty(gear.Props.Label) && string.IsNullOrEmpty(gear.Props.Send);

            // Word-prediction keys are written [wp] when their numbers follow the order of the keys (the wizard numbers them that way).
            int wpOrder = 0;
            var sb = new StringBuilder();
            for (int r = 0; r < layout.Rows; r++)
            {
                var tokens = new List<string>();
                foreach (var cell in cells.Where(c => c.Row == r))
                {
                    if (dropGear && ReferenceEquals(cell, gear) && cell.Col + cell.ColSpan >= layout.Cols) continue;      // the button at the end of its row
                    tokens.Add(Token(cell, ref wpOrder));
                }
                while (tokens.Count > 1 && tokens[^1] == "_") tokens.RemoveAt(tokens.Count - 1);       // blank cells at the end of a row come back by themselves
                if (tokens.Count == 0 && !cells.Any(c => c.Row < r && c.Row + c.RowSpan > r)) tokens.Add("_");          // an empty row still counts as a row
                if (tokens.Count == 0) continue;                                                                       // covered by tall keys above
                sb.Append(string.Join(" ", tokens)).Append('\n');
            }
            return sb.ToString();
        }

        private static string Token(GridCell cell, ref int wpOrder)
        {
            var p = cell.Props;
            string label = p.Label ?? "", send = p.Send ?? "", sl = p.ShiftLabel ?? "", ss = p.ShiftSend ?? "", al = p.AltGrLabel ?? "", asd = p.AltGrSend ?? "";
            string core = null;
            bool noLayers = sl.Length == 0 && ss.Length == 0 && al.Length == 0 && asd.Length == 0;

            if (label.Length == 0 && send.Length == 0 && noLayers) core = "_";
            else if (send.StartsWith("wp:", StringComparison.Ordinal))
            {
                bool inOrder = send == "wp:" + wpOrder && noLayers && (label == "Word " + (wpOrder + 1) || label == "Woord " + (wpOrder + 1));
                if (inOrder) { core = "[wp]"; wpOrder++; }          // only the [wp] keys count: the wizard numbers just those, in this order
            }
            if (core == null) core = Simple(label, send, sl, ss, al, asd);
            if (core == null) core = Fallback(label, send, sl, ss, al, asd);
            if (cell.ColSpan > 1 || cell.RowSpan > 1)
                core += cell.RowSpan > 1 ? "*" + cell.ColSpan + "x" + cell.RowSpan : "*" + cell.ColSpan;
            return core;
        }

        /// <summary>The short form of a key, or null when the key has more than the short forms can say.</summary>
        private static string Simple(string label, string send, string sl, string ss, string al, string asd)
        {
            string core = null;
            bool shiftDone = false;

            if (label.Length > 0 || send.Length > 0)
            {
                string name = WizardKeyParser.NameOfKey(label, send);
                if (name != null) core = "[" + name + "]";
                else if (Shortcut.Format(label, send) is { } sc) core = "[" + sc + "]";
                else if (send.StartsWith("layout:", StringComparison.Ordinal) && send.Length > 7 && send[7..].Trim() == send[7..]
                         && label == Path.GetFileNameWithoutExtension(send[7..]) && send.IndexOfAny(new[] { ']', '\r', '\n' }) < 0)
                    core = "[" + send + "]";
                else if (label == send && label.Length > 0)
                {
                    if (label.Length == 1 && char.IsLetter(label[0]))
                    {
                        string lo = label.ToLowerInvariant(), up = label.ToUpperInvariant();
                        if (label == lo && lo != up && (sl.Length > 0 || ss.Length > 0)) { core = lo; shiftDone = sl == up && ss == up; }   // q → q, Q on Shift (another Shift output is a layer)
                        else if (label == lo && lo == up && sl.Length == 0 && ss.Length == 0) core = lo;                 // a letter without a capital
                        else core = "[" + label + "]";                                                                   // as it is: no automatic Shift
                    }
                    else core = Text(label);
                }
            }
            if (core == null) return null;

            var sb = new StringBuilder(core);
            if (!shiftDone && (sl.Length > 0 || ss.Length > 0))
            {
                if (sl != ss || sl.Length == 0) return null;
                sb.Append("[shift:").Append(Esc(sl)).Append(']');
            }
            if (al.Length > 0 || asd.Length > 0)
            {
                if (al != asd || al.Length == 0) return null;
                sb.Append("[altgr:").Append(Esc(al)).Append(']');
            }
            return sb.ToString();
        }

        private static readonly Regex EndsWithSize = new(@"\*\d+(x\d+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>A text key as a word when that reads back the same, else in quotes; null when neither does.</summary>
        private static string Text(string s)
        {
            bool word = s.Length > 0 && !s.Any(char.IsWhiteSpace) && s[0] != '[' && s[0] != '"' && s[0] != '_'
                        && !s.Contains("[shift:", StringComparison.OrdinalIgnoreCase) && !s.Contains("[altgr:", StringComparison.OrdinalIgnoreCase)
                        && !EndsWithSize.IsMatch(s) && !(s.Length == 1 && char.IsLetter(s[0]));
            if (word) return s;
            return s.IndexOf('"') < 0 && s.IndexOfAny(new[] { '\r', '\n' }) < 0 ? "\"" + s + "\"" : null;
        }

        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("]", "\\]").Replace("|", "\\|");

        /// <summary>Every layer written out; trailing empty layers are left off.</summary>
        private static string Fallback(string label, string send, string sl, string ss, string al, string asd)
        {
            var f = new List<string> { label, send, sl, ss, al, asd };
            while (f.Count > 2 && f[^1].Length == 0) f.RemoveAt(f.Count - 1);
            return "[key:" + string.Join("|", f.Select(Esc)) + "]";
        }
    }
}

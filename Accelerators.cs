// Accelerators.cs — Alt+letter shortcuts of the dialogs.
//
// A dialog does not write its "&" markers by hand: FluentDialogBase.RefreshAccelerators picks a letter for every control that
// can be reached by name (buttons, check boxes, radio buttons, colour chips, section buttons, and the labels of the fields),
// in the language that is on screen. A hand-written "&" in a string is only a preference. The letters are unique within what
// is visible together (the footer and one section), so a long dialog never runs out of letters.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>Reading and writing the "&amp;" accelerator marker in a text. "&amp;&amp;" is a literal ampersand.</summary>
    internal static class Accel
    {
        /// <summary>The text without its accelerator marker (a literal "&amp;&amp;" stays).</summary>
        public static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0) return text ?? "";
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '&') { sb.Append(text[i]); continue; }
                if (i + 1 < text.Length && text[i + 1] == '&') { sb.Append("&&"); i++; }
                // a single "&" is the marker: dropped
            }
            return sb.ToString();
        }

        /// <summary>The letter that is marked in <paramref name="text"/>, upper case; '\0' when there is none.</summary>
        public static char Marked(string text)
        {
            if (string.IsNullOrEmpty(text)) return '\0';
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] != '&') continue;
                if (text[i + 1] == '&') { i++; continue; }
                return char.ToUpperInvariant(text[i + 1]);
            }
            return '\0';
        }

        /// <summary>The letters and digits of a plain text that can carry a marker: (character, index in the text), literal ampersands left out.</summary>
        private static List<(char Ch, int Index)> Candidates(string plain)
        {
            var list = new List<(char, int)>();
            for (int i = 0; i < plain.Length; i++)
            {
                if (plain[i] == '&') { i++; continue; }       // "&&"
                if (char.IsLetterOrDigit(plain[i])) list.Add((char.ToUpperInvariant(plain[i]), i));
            }
            return list;
        }

        /// <summary>
        /// The letters of <paramref name="plain"/> in the order they are preferred: the initials of the words first (in reading order),
        /// then every other letter or digit. Each letter once.
        /// </summary>
        public static List<char> Preferred(string plain) => Preferred(plain, out _);

        /// <summary><see cref="Preferred(string)"/>, also telling how many of the leading letters are word initials.</summary>
        public static List<char> Preferred(string plain, out int initialCount)
        {
            var all = Candidates(plain);
            var result = new List<char>();
            var initials = new List<(char, int)>();
            bool inWord = false;
            for (int i = 0; i < plain.Length; i++)
            {
                if (plain[i] == '&') { i++; inWord = false; continue; }
                bool letter = char.IsLetterOrDigit(plain[i]);
                if (letter && !inWord) initials.Add((char.ToUpperInvariant(plain[i]), i));
                inWord = letter;
            }
            foreach (var (c, _) in initials) if (!result.Contains(c)) result.Add(c);
            initialCount = result.Count;
            foreach (var (c, _) in all) if (!result.Contains(c)) result.Add(c);
            return result;
        }

        /// <summary>
        /// <paramref name="plain"/> with the marker in front of the first occurrence of <paramref name="letter"/> (preferably at the start of a
        /// word, so "Save As" gets "&amp;Save As" and "Sa&amp;ve As" only when S is taken). Unchanged when the letter does not occur.
        /// </summary>
        public static string Mark(string plain, char letter)
        {
            var hit = Candidates(plain).Where(c => c.Ch == char.ToUpperInvariant(letter)).Select(c => (int?)c.Index).FirstOrDefault();
            if (hit == null) return plain;
            // Prefer an occurrence at the start of a word.
            foreach (var (ch, idx) in Candidates(plain))
                if (ch == char.ToUpperInvariant(letter) && (idx == 0 || !char.IsLetterOrDigit(plain[idx - 1]))) { hit = idx; break; }
            return plain.Insert(hit.Value, "&");
        }
    }

    /// <summary>
    /// A field label with an Alt+letter that moves focus to the field it names. Unlike a plain label it does not depend on the tab order
    /// of its neighbours: it names its targets itself (a container: its first control that takes focus). With several targets (a row
    /// of fields) pressing the letter again moves to the next one.
    /// </summary>
    public sealed class AccelLabel : Label
    {
        private readonly List<Control> _targets = new List<Control>();

        public AccelLabel() { UseMnemonic = false; }

        /// <summary>The controls this label names. Setting any makes the label's "&amp;" an accelerator.</summary>
        public IReadOnlyList<Control> Targets => _targets;

        public void SetTargets(params Control[] targets)
        {
            _targets.Clear();
            _targets.AddRange(targets.Where(t => t != null));
            UseMnemonic = _targets.Count > 0;
        }

        /// <summary>True when a target or anything inside it is a Tab stop (a note or a preview inside a group is not): the label then has no use for a letter.</summary>
        internal bool HasReachableTarget() => _targets.Any(t => ReachableIn(t));

        private static bool ReachableIn(Control c) =>
            (c.TabStop && c.Controls.Count == 0) || c.Controls.Cast<Control>().Any(ReachableIn);

        /// <summary>The control that takes focus for <paramref name="target"/>: itself, or the first control inside it that does.</summary>
        internal static Control FocusableIn(Control target)
        {
            if (target == null || !target.Enabled || !target.Visible) return null;
            // Only a control without parts takes the focus itself; a container (a row, a stepper) hands it to the first of its parts that does.
            // (GetNextControl does not look inside a user control such as the stepper, so the parts are walked here.)
            if (target.Controls.Count == 0) return target.CanFocus && target.TabStop ? target : null;
            foreach (Control child in target.Controls.Cast<Control>().OrderBy(c => c.TabIndex))
            {
                var f = FocusableIn(child);
                if (f != null) return f;
            }
            return null;
        }

        protected override bool ProcessMnemonic(char charCode)
        {
            if (!UseMnemonic || _targets.Count == 0 || !Enabled || !Visible || !IsMnemonic(charCode, Text)) return false;
            int current = _targets.FindIndex(t => t.ContainsFocus);
            for (int n = 1; n <= _targets.Count; n++)
            {
                int i = ((current < 0 ? -1 : current) + n) % _targets.Count;
                var f = FocusableIn(_targets[i]);
                if (f == null) continue;
                f.Select();
                if (f is TextBoxBase tb) tb.SelectAll();          // as when Tab arrives in a text box
                return true;
            }
            return true;       // the letter is ours even when nothing can take focus now (a disabled field): no beep
        }
    }
}

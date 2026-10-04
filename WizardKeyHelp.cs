// WizardKeyHelp.cs — the "?" window of the New Keyboard Wizard: every special key that can be typed in the key labels.
// The rows come from WizardKeyParser.Help, so the window cannot say something the parser does not do (a test compares them).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class SpecialKeysDialog : FluentDialogBase
    {
        private const int MaxWidth = 1040;
        private const int TextWidth = MaxWidth - 4 * Fluent.Pad;     // window minus the padding of frame and section

        // Three columns of groups side by side, so the window stays short enough for a laptop screen.
        private static readonly Font MonoFont = new Font("Courier New", 10.5f);
        protected override int ContentMaxWidth => MaxWidth;

        public SpecialKeysDialog(bool dutch)
        {
            Text = Lang.T("wiz: keys title");
            FormBorderStyle = FormBorderStyle.FixedDialog;

            var close = MakeTouchButton(() => Lang.T("wiz: Close"), FluentButton.Variant.Primary);
            BuildFrame(MakeFooter(null, close), withSections: false);
            close.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            AcceptButton = CancelButton = close;
            ActiveControl = close;

            var t = AddSection(() => Lang.T("wiz: keys title"));
            AddWideRow(t, Note(() => Lang.T("wiz: keys intro"), TextWidth));
            AddWideRow(t, Note(() => Lang.T("wiz: keys intro 2"), TextWidth));

            // Two columns of groups, side by side; each group is a small two-column table (what to type | the key it makes).
            var cols = new[]
            {
                Column(Group("typing", dutch), Group("other", dutch)),
                Column(Group("nav", dutch), Group("func", dutch)),
                Column(Group("mod", dutch), Group("dead", dutch)),
            };
            var both = new TableLayoutPanel { ColumnCount = cols.Length, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            for (int i = 0; i < cols.Length; i++)
            {
                both.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                cols[i].Margin = new Padding(0, 0, i < cols.Length - 1 ? Fluent.Pad : 0, 0);
                cols[i].Anchor = AnchorStyles.Top | AnchorStyles.Left;
                both.Controls.Add(cols[i], i, 0);
            }
            AddWideRow(t, both, fill: false);
            AddWideRow(t, Note(() => Lang.T("wiz: keys dead note"), TextWidth));
        }

        private Label Note(Func<string> text, int maxWidth)
        {
            var l = new Label
            {
                Text = text(), AutoSize = true, UseMnemonic = false, MaximumSize = new Size(maxWidth, 0), Font = Fluent.FontLabel,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, AccessibleName = text(), Margin = new Padding(0, 0, 0, Touch.Gap),
            };
            _transLabels.Add((l, () => { string s = text(); l.AccessibleName = s; return s; }));
            return l;
        }

        private static TableLayoutPanel Column(params Control[] groups)
        {
            var c = new TableLayoutPanel { ColumnCount = 1, RowCount = groups.Length, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            c.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < groups.Length; i++)
            {
                c.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                groups[i].Anchor = AnchorStyles.Top | AnchorStyles.Left;
                groups[i].Margin = new Padding(0, 0, 0, Touch.Gap / 2);
                c.Controls.Add(groups[i], 0, i);
            }
            return c;
        }

        private TableLayoutPanel NewGroup(string groupKey, Func<string> note = null)
        {
            var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var head = Heading(() => Lang.T("wiz: keys group " + groupKey));
            AddSpan(g, head);
            return g;
        }

        private static void AddSpan(TableLayoutPanel g, Control c)
        {
            int r = g.RowCount++;
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.Controls.Add(c, 0, r);
            g.SetColumnSpan(c, 2);
        }

        private void AddKeyRow(TableLayoutPanel g, string typed, string result)
        {
            var a = new Label
            {
                Text = typed, AutoSize = true, UseMnemonic = false, Font = MonoFont, ForeColor = Fluent.TextPrimary,
                BackColor = Color.Transparent, Margin = new Padding(0, 1, Fluent.Pad, 1), Anchor = AnchorStyles.Left, AccessibleName = typed,
            };
            var b = new Label
            {
                Text = result, AutoSize = true, UseMnemonic = false, Font = Fluent.FontBtnLg, ForeColor = Fluent.TextPrimary,
                BackColor = Color.Transparent, Margin = new Padding(0, 1, 0, 1), Anchor = AnchorStyles.Left, AccessibleName = result,
            };
            int r = g.RowCount++;
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.Controls.Add(a, 0, r);
            g.Controls.Add(b, 1, r);
        }

        /// <summary>A group of the parser's help list: one row per key, the spellings on the left, the key it makes on the right.</summary>
        private TableLayoutPanel Group(string groupKey, bool dutch, Func<string> note = null)
        {
            var g = NewGroup(groupKey);
            foreach (var row in WizardKeyParser.Help)
            {
                if (row.GroupKey != groupKey) continue;
                string first = WizardKeyParser.LabelOf(row.Tokens[0], dutch);
                string shown = groupKey == "func" ? WizardKeyParser.LabelOf("f1", dutch) + " … " + WizardKeyParser.LabelOf("f16", dutch) : first;
                AddKeyRow(g, row.Display, shown);
            }
            if (note != null) AddSpan(g, Note(note, 300));
            return g;
        }
    }
}

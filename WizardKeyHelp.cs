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
        private static readonly Font MonoFont = new Font("Courier New", 10.5f);
        protected override int ContentMaxWidth => 1080;      // the three-column arrangement with +80 % text needs about 950 of the 1040 it had; text measures slightly differently from one display to the next

        public SpecialKeysDialog(bool dutch)
        {
            Text = Lang.T("wiz: keys title");
            FormBorderStyle = FormBorderStyle.Sizable;       // the groups reflow with the width

            var close = MakeTouchButton(() => Lang.T("wiz: Close"), FluentButton.Variant.Primary);
            BuildFrame(MakeFooter(null, close), withSections: false);
            close.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            AcceptButton = CancelButton = close;
            ActiveControl = close;

            var t = AddSection(() => Lang.T("wiz: keys title"));
            AddWideRow(t, Note(() => Lang.T("wiz: keys intro")));
            AddWideRow(t, Note(() => Lang.T("wiz: keys intro 2")));

            // The groups, each a small two-column table (what to type | the key it makes), in three columns when they fit, else in two or
            // one. A tall group (the navigation keys) spans two rows of the three-column arrangement.
            var typing = Group("typing", dutch); var other = Group("other", dutch); var nav = Group("nav", dutch);
            var mod = Group("mod", dutch);       var dead = Group("dead", dutch);
            foreach (var g in new[] { typing, other, nav, mod, dead })
            { g.Anchor = AnchorStyles.Top | AnchorStyles.Left; g.Margin = new Padding(0, 0, Fluent.Pad, Touch.Gap / 2); }
            var both = new AdaptiveTable { ColumnCount = 3 };
            both.AddVariant(() => new[]
            {
                new AdaptiveTable.Cell(typing, 0, 0), new AdaptiveTable.Cell(other, 0, 1),
                new AdaptiveTable.Cell(nav, 1, 0, 1, 2),
                new AdaptiveTable.Cell(mod, 2, 0), new AdaptiveTable.Cell(dead, 2, 1),
            }, 3);
            both.AddVariant(() => new[]
            {
                new AdaptiveTable.Cell(typing, 0, 0), new AdaptiveTable.Cell(nav, 1, 0),
                new AdaptiveTable.Cell(other, 0, 1), new AdaptiveTable.Cell(mod, 1, 1),
                new AdaptiveTable.Cell(dead, 0, 2),
            }, 2);
            both.AddVariant(() => new[]
            {
                new AdaptiveTable.Cell(typing, 0, 0), new AdaptiveTable.Cell(other, 0, 1), new AdaptiveTable.Cell(nav, 0, 2),
                new AdaptiveTable.Cell(mod, 0, 3),    new AdaptiveTable.Cell(dead, 0, 4),
            }, 1);
            AddWideRow(t, both, fill: false);
            AddWideRow(t, Note(() => Lang.T("wiz: keys dead note")));
        }

        /// <summary>A line of text that wraps at the width the window leaves it.</summary>
        private Label Note(Func<string> text)
        {
            var l = Wrap(new Label
            {
                Text = text(), AutoSize = true, UseMnemonic = false, Font = Fluent.FontLabel,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, AccessibleName = text(), Margin = new Padding(0, 0, 0, Touch.Gap),
            });
            _transLabels.Add((l, () => { string s = text(); l.AccessibleName = s; return s; }));
            return l;
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
                string shown = row.Display.StartsWith("[f1]") ? WizardKeyParser.LabelOf("f1", dutch) + " … " + WizardKeyParser.LabelOf("f16", dutch) : first;
                AddKeyRow(g, row.Display, shown);
            }
            if (note != null) AddSpan(g, Note(note));
            return g;
        }
    }
}

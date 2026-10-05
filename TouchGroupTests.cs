// TouchGroupTests.cs — the components added for the Keyboard Editor redesign (spec steps S1 and S2):
// accelerators on TouchCheckBox, TouchRadioButton, TouchGroup, OptionStack, ButtonRow and the alignment guard.

using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        /// <summary>Hosts a control in a form with a handle (never shown) and lays it out.</summary>
        private static Form HostFor(Control c, int w, int h, Color? back = null)
        {
            var host = new Form { Opacity = 0, ShowInTaskbar = false, BackColor = back ?? Fluent.BgPage };
            host.Controls.Add(c);
            host.ClientSize = new Size(w, h);
            _ = host.Handle;
            host.PerformLayout();
            return host;
        }

        private static Bitmap Snapshot(Control c)
        {
            var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
            c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.Size));
            return bmp;
        }

        private static bool SamePixels(Bitmap a, Bitmap b)
        {
            if (a.Size != b.Size) return false;
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
            return true;
        }

        private static void T_TouchGroupComponents()
        {
            Section("TouchCheckBox accelerators, TouchRadioButton, TouchGroup, OptionStack, ButtonRow, alignment guard");

            bool wasLight = ToolbarButton.IsLightTheme;
            try
            {
                ToolbarButton.IsLightTheme = true;

                // ── TouchCheckBox: "&" marks an accelerator, it is never printed and never measured ──
                var plain = new TouchCheckBox { Text = "Show timing" };
                var accel = new TouchCheckBox { Text = "Sho&w timing" };
                Assert(accel.GetPreferredSize(Size.Empty).Width == plain.GetPreferredSize(Size.Empty).Width,
                    "check box: an '&' takes no room (the preferred width is that of the stripped text)");
                Assert(accel.UseMnemonic, "check box: the accelerator still works (UseMnemonic)");
                using (var h1 = HostFor(plain, 220, 60)) using (var h2 = HostFor(accel, 220, 60))
                {
                    bool cues = (bool)typeof(Control).GetProperty("ShowKeyboardCues", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(accel);
                    if (!cues)
                    {
                        using var a = Snapshot(plain); using var b = Snapshot(accel);
                        Assert(SamePixels(a, b), "check box: no '&' is drawn while the keyboard cues are off (identical to the text without it)");
                    }
                    else Assert(true, "check box: keyboard cues are on in this session; the '&' draw test is skipped");
                }

                // ── TouchRadioButton ──
                using (var panel = new Panel())
                {
                    var r1 = new TouchRadioButton { Text = "Off", TabIndex = 0, Checked = true };
                    var r2 = new TouchRadioButton { Text = "Slow keys", TabIndex = 1 };
                    var r3 = new TouchRadioButton { Text = "Dwell click", TabIndex = 2 };
                    foreach (var r in new[] { r1, r2, r3 }) { r.Dock = DockStyle.Top; panel.Controls.Add(r); }
                    panel.Controls.SetChildIndex(r1, 2); panel.Controls.SetChildIndex(r2, 1); panel.Controls.SetChildIndex(r3, 0);
                    using var host = HostFor(panel, 300, 200);

                    Assert(r1.Height >= 44 && r2.Height >= 44, "radio button: at least 44 px tall");
                    Assert(r1.GetPreferredSize(Size.Empty).Width > TextRenderer.MeasureText("Off", r1.Font).Width, "radio button: room for the mark and the padding around the text");
                    Assert(r1.Checked && !r2.Checked && !r3.Checked, "radio group: one checked");
                    r3.Checked = true;
                    Assert(!r1.Checked && !r2.Checked && r3.Checked, "radio group: checking one clears the others");

                    // Arrow keys move the choice and the focus, skipping a disabled one and wrapping.
                    r1.Checked = true;
                    Key(r1, Keys.Down);
                    Assert(r2.Checked && !r1.Checked, "arrow Down: the next radio button is chosen");
                    r3.Enabled = false;
                    Key(r2, Keys.Down);
                    Assert(r1.Checked, "arrow Down: a disabled radio button is skipped and the group wraps round");
                    Key(r1, Keys.Up);
                    Assert(r2.Checked, "arrow Up: back to the previous enabled one (the disabled one is skipped)");
                    r3.Enabled = true;
                    Key(r1, Keys.Left);   // r1 is not checked now, but the key moves from r1's place in the group
                    Assert(r3.Checked, "arrow Left works like Up (wraps to the last one)");
                    Key(r3, Keys.Right);
                    Assert(r1.Checked, "arrow Right works like Down (wraps to the first one)");

                    // Pixels: the dot of the chosen one is the accent colour, the unchosen mark is white inside; disabled is not the accent.
                    Color Centre(TouchRadioButton r) { using var b = Snapshot(r); return b.GetPixel(14 + 12, r.Height / 2); }
                    Assert(Centre(r1).ToArgb() == Fluent.Accent.ToArgb(), $"radio button: the chosen one has the accent dot ({Centre(r1)})");
                    Assert(Centre(r2).ToArgb() == Color.White.ToArgb(), $"radio button: an unchosen one has an empty white mark ({Centre(r2)})");
                    r1.Enabled = false;
                    Assert(Centre(r1).ToArgb() != Fluent.Accent.ToArgb(), "radio button: a disabled chosen one is not drawn in the accent colour");
                    r1.Enabled = true;
                    // The chosen row has an accent border inside the normal one; an unchosen row does not.
                    Color Edge(TouchRadioButton r) { using var b = Snapshot(r); return b.GetPixel(r.Width / 2, 1); }
                    Assert(Edge(r1).ToArgb() != Edge(r2).ToArgb(), "radio button: the chosen row has a second border (not only the dot)");
                }

                // ── TouchList: the disabled look and the empty text ──
                {
                    var list = new TouchList { Size = new Size(240, 180) };
                    list.Items.Add("kapstok (2)");
                    list.SelectedIndex = 0;
                    using var host = HostFor(list, 260, 200);
                    Color RowBackground(DrawItemState state)
                    {
                        using var bmp = new Bitmap(240, 44);
                        using (var g = Graphics.FromImage(bmp))
                            typeof(ListBox).GetMethod("OnDrawItem", BindingFlags.NonPublic | BindingFlags.Instance)
                                .Invoke(list, new object[] { new DrawItemEventArgs(g, list.Font, new Rectangle(0, 0, 240, 44), 0, state) });
                        return bmp.GetPixel(236, 22);          // right of the text
                    }
                    var off = FluentPainter.DisabledPalette(false);
                    Assert(RowBackground(DrawItemState.Selected).ToArgb() == Fluent.Accent.ToArgb(), "list: a selected row is the accent colour");
                    Assert(RowBackground(DrawItemState.None).ToArgb() == Fluent.BgInput.ToArgb(), "list: an unselected row is the input colour");
                    list.Enabled = false;
                    Assert(RowBackground(DrawItemState.Selected).ToArgb() == off.Fill.ToArgb() && RowBackground(DrawItemState.None).ToArgb() == off.Fill.ToArgb(),
                        "list: disabled, every row is the flat disabled grey and there is no selection highlight");
                    Assert(list.BackColor.ToArgb() == off.Fill.ToArgb(), "list: disabled, the empty part is the flat disabled grey too");
                    list.Enabled = true;
                    Assert(list.BackColor.ToArgb() == Fluent.BgInput.ToArgb(), "list: enabled again, the normal input colour");

                    var empty = new TouchList { Size = new Size(240, 180), EmptyText = "No candidates yet." };
                    using var h2 = HostFor(empty, 260, 200);
                    int Ink(string text, bool enabled)
                    {
                        empty.EmptyText = text; empty.Enabled = enabled;
                        using var bmp = new Bitmap(240, 180);
                        int n = 0;
                        using (var g = Graphics.FromImage(bmp)) { g.Clear(empty.BackColor); empty.PaintEmptyText(g); }
                        for (int y = 0; y < 180; y++) for (int x = 0; x < 240; x++) if (bmp.GetPixel(x, y).ToArgb() != empty.BackColor.ToArgb()) n++;
                        return n;
                    }
                    Assert(Ink("No candidates yet.", true) > 50, "list: the empty text is drawn inside the list");
                    Assert(Ink("", true) == 0, "list: no empty text, nothing is drawn");
                    Assert(Ink("No candidates yet.", false) > 50, "list: the empty text is also drawn while the list is disabled");
                }

                // ── TouchGroup ──
                var content = new Label { Text = "content", AutoSize = true };
                var group = new TouchGroup("Timing aid");
                group.SetContent(content);
                Assert(group.AccessibleName == "Timing aid" && group.AccessibleRole == AccessibleRole.Grouping, "group: the caption is its accessible name and it is announced as a group");
                Assert(group.CaptionLabel.Text == "Timing aid", "group: the caption is shown");
                using (var host = HostFor(group, 300, 200))
                {
                    var pref = group.GetPreferredSize(Size.Empty);
                    Assert(pref.Width >= content.PreferredSize.Width + 24 && pref.Height >= content.PreferredSize.Height + group.CaptionLabel.PreferredSize.Height + 24,
                        "group: as big as its content, the caption and the padding need");
                    using var b = Snapshot(group);
                    Assert(b.GetPixel(b.Width / 2, 0).ToArgb() == Fluent.ControlBorder.ToArgb() && b.GetPixel(0, b.Height / 2).ToArgb() == Fluent.ControlBorder.ToArgb()
                           && b.GetPixel(b.Width - 1, b.Height / 2).ToArgb() == Fluent.ControlBorder.ToArgb() && b.GetPixel(b.Width / 2, b.Height - 1).ToArgb() == Fluent.ControlBorder.ToArgb(),
                        "group: the frame is a 1 px control border on all four sides");
                }

                // ── OptionStack: one width, right edges aligned ──
                var c1 = new TouchCheckBox { Text = "Short" };
                var c2 = new TouchCheckBox { Text = "A much longer text on this one" };
                var chip = new ColorChip("Background", Color.Navy);
                var stack = new OptionStackPanel { ColumnCount = 1, AutoSize = true };
                // built through the same helper the dialogs use
                var built = (TableLayoutPanel)typeof(FluentDialogBase).GetMethod("OptionStack", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { new Control[] { c1, c2, chip } });
                using (var host = HostFor(built, 500, 300))
                {
                    Assert(c1.Width == c2.Width && c2.Width == chip.Width, $"option stack: all members have one width ({c1.Width}, {c2.Width}, {chip.Width})");
                    Assert(c1.Right == c2.Right && c2.Right == chip.Right, "option stack: the right edges line up");
                    Assert(c2.Width >= c2.GetPreferredSize(Size.Empty).Width, "option stack: the width is that of the widest member");
                    Assert(UiGuard.StackedEdges(host, false).Count == 0, "the guard accepts a stack built by the helper");
                    c1.Text = "A text that is now the longest of all three of these controls";
                    host.PerformLayout();
                    Assert(c1.Width == c2.Width && c1.Right == chip.Right && c1.Width >= c1.GetPreferredSize(Size.Empty).Width, "option stack: it follows when one member's text grows");
                }

                // ── ButtonRow: one common width, kept when the text changes ──
                var b1 = new FluentButton { Text = "Save", MinimumSize = new Size(120, 44), AutoSize = true };
                var b2 = new FluentButton { Text = "Save As…", MinimumSize = new Size(120, 44), AutoSize = true };
                var b3 = new FluentButton { Text = "Load a layout file from disk", MinimumSize = new Size(120, 44), AutoSize = true };
                var row = (TableLayoutPanel)typeof(FluentDialogBase).GetMethod("ButtonRow", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { new Control[] { b1, b2, b3 } });
                using (var host = HostFor(row, 700, 80))
                {
                    Assert(b1.Width == b2.Width && b2.Width == b3.Width, $"button row: all buttons have one width ({b1.Width}, {b2.Width}, {b3.Width})");
                    Assert(b1.Width >= b3.GetPreferredSize(Size.Empty).Width - 1, "button row: the width is that of the widest button");
                    b3.Text = "Load";
                    host.PerformLayout();
                    Assert(b1.Width == b3.Width && b1.Width < 200, $"button row: it shrinks again when the widest text gets shorter ({b1.Width})");
                    Assert(UiGuard.StackedEdges(host, false).Count == 0, "the guard accepts a button row built by the helper");
                }

                // ── The guard itself must catch an uneven stack and an uneven button row ──
                var u1 = new Button { Text = "a", Dock = DockStyle.None, Size = new Size(120, 44) };
                var u2 = new Button { Text = "b", Dock = DockStyle.None, Size = new Size(180, 44) };
                var uneven = new OptionStackPanel { ColumnCount = 1, RowCount = 2, Size = new Size(300, 120) };
                uneven.Controls.Add(u1, 0, 0); uneven.Controls.Add(u2, 0, 1);
                using (var host = HostFor(uneven, 400, 200))
                {
                    u1.Dock = DockStyle.None; u2.Dock = DockStyle.None;
                    u1.Size = new Size(120, 44); u2.Size = new Size(180, 44);
                    Assert(UiGuard.StackedEdges(host, false).Count == 1, "the guard reports a stack whose members differ in width");
                }
                var v1 = new Button { Text = "a", Size = new Size(100, 44) };
                var v2 = new Button { Text = "b", Size = new Size(150, 44) };
                var unevenRow = ReflowRows.Buttons(new Control[] { v1, v2 }, 2, 1);
                using (var host = HostFor(unevenRow, 400, 80))
                {
                    v1.MinimumSize = Size.Empty; v2.MinimumSize = Size.Empty;
                    v1.AutoSize = false; v2.AutoSize = false;
                    v1.Size = new Size(100, 44); v2.Size = new Size(150, 44);
                    Assert(UiGuard.StackedEdges(host, false).Count == 1, "the guard reports a button row whose buttons differ in width");
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLight; }
        }
    }
}

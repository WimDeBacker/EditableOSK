// WizardControlTests.cs — the wizard's own controls: TouchTile (selectable card) and WizardGridPreview (grid with live labels).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_WizardControls()
        {
            Section("Wizard controls — TouchTile and WizardGridPreview");
            Lang.Load("en");

            // ── TouchTile: a radio group made of cards ────────────────────
            using (var host = new Panel { Size = new Size(600, 140) })
            {
                var a = new TouchTile { Text = "Dark",  Swatches = new[] { Color.Navy, Color.Blue, Color.Teal }, Location = new Point(0, 0),   Size = new Size(140, 100), TabIndex = 0 };
                var b = new TouchTile { Text = "Light", Swatches = new[] { Color.White, Color.AliceBlue },        Location = new Point(150, 0), Size = new Size(140, 100), TabIndex = 1 };
                var c = new TouchTile { Text = "From file…",                                                       Location = new Point(300, 0), Size = new Size(140, 100), TabIndex = 2 };
                host.Controls.AddRange(new Control[] { a, b, c });
                host.CreateControl();

                Assert(a is RadioButton, "a tile is a radio button (keyboard, accessibility and the group come with it)");
                Assert(a.MinimumSize.Height >= Touch.Target && a.GetPreferredSize(Size.Empty).Height >= Touch.Target, "a tile is at least 44 px tall");
                a.Checked = true;
                Assert(a.Checked && !b.Checked, "choosing a tile");
                b.Checked = true;
                Assert(!a.Checked && b.Checked, "tiles with one parent are one group: choosing another clears the first");
                Assert(b.Neighbour(1) == c && c.Neighbour(1) == a && a.Neighbour(-1) == c, "the arrow keys move through the tiles in tab order and wrap round");

                // The chosen tile looks different from the others without relying on colour alone: a ring and a check mark.
                using var bmpOn  = new Bitmap(140, 100); b.DrawToBitmap(bmpOn, new Rectangle(0, 0, 140, 100));
                using var bmpOff = new Bitmap(140, 100); a.DrawToBitmap(bmpOff, new Rectangle(0, 0, 140, 100));
                bool ringOn  = bmpOn.GetPixel(1, 50).GetBrightness()  < 0.5f && bmpOn.GetPixel(2, 50).GetBrightness() < 0.5f;
                bool ringOff = bmpOff.GetPixel(2, 50).GetBrightness() < 0.5f;
                Assert(ringOn && !ringOff, $"the chosen tile has a thick ring (3 px) the others do not (on {bmpOn.GetPixel(1,50)} {bmpOn.GetPixel(2,50)}, off {bmpOff.GetPixel(2,50)})");
                bool check = false;
                for (int x = 105; x < 136 && !check; x++)
                    for (int y = 4; y < 30 && !check; y++)
                        if (bmpOn.GetPixel(x, y).GetBrightness() < 0.45f) check = true;
                Assert(check, "the chosen tile shows a check mark top right");
            }

            // ── WizardGridPreview ─────────────────────────────────────────
            using (var form = new Form { Size = new Size(640, 400), ShowInTaskbar = false })
            {
                var pv = new WizardGridPreview { Dock = DockStyle.Top, Height = 200 };
                form.Controls.Add(pv);
                form.CreateControl();

                pv.ShowBlank(4, 8);
                Assert(pv.GridRows == 4 && pv.GridCols == 8, "blank grid: rows and columns as given (the gear column is extra)");
                Assert(pv.LabelAt(0, 8) == "⚙" && pv.LabelAt(1, 8) == "" && pv.LabelAt(0, 0) == "", "blank grid: only the reserved cell shows the gear");
                Assert(pv.AccessibleName == Lang.T("wiz: Preview") && pv.AccessibleDescription.Contains("4") && pv.AccessibleDescription.Contains("9"),
                    "the preview describes itself for a screen reader (rows, columns including the gear column)");

                var rows = WizardKeyParser.Parse("q w e\na _ s [Backspace]", false);
                pv.ShowParsed(rows);
                Assert(pv.GridRows == 2 && pv.GridCols == 4, "pasted text: the size follows the rows and the longest row");
                Assert(pv.LabelAt(0, 0) == "q" && pv.LabelAt(0, 2) == "e" && pv.LabelAt(1, 2) == "s", "pasted text: the typed labels are in their keys");
                Assert(pv.LabelAt(1, 1) == "", "pasted text: _ is a blank key");
                Assert(pv.LabelAt(1, 3) == rows[1][3].Label && pv.LabelAt(1, 3).Length > 0, "pasted text: a special key shows its symbol");
                Assert(pv.LabelAt(0, 4) == "⚙", "pasted text: the gear sits in the extra column of the first row");
                Assert(pv.LabelAt(0, 3) == "", "pasted text: a cell the short first row does not reach is empty");

                pv.ShowParsed(new List<List<WizardKeyParser.KeySpec>>());
                Assert(pv.GridRows == 1 && pv.GridCols == 1, "nothing typed: a minimal grid with the gear");

                // A wide grid keeps its key width and scrolls instead of squeezing the text.
                pv.ShowBlank(3, 40);
                Assert(pv.AutoScrollMinSize.Width >= 41 * WizardGridPreview.MinKeyWidth, "40 columns: the preview is wider than the control and scrolls sideways");
                pv.ShowBlank(2, 3);
                Assert(pv.GetPreferredSize(new Size(500, 0)).Height >= 2 * WizardGridPreview.KeyHeight, "the preview is tall enough for its rows");
                pv.ShowBlank(20, 3);
                Assert(pv.GetPreferredSize(new Size(500, 0)).Height <= (WizardGridPreview.VisibleRows + 1) * WizardGridPreview.KeyHeight + 40,
                    "a tall grid is limited to a few visible rows and scrolls");

                using var bmp = new Bitmap(pv.Width, pv.Height);
                pv.ShowParsed(rows);
                pv.DrawToBitmap(bmp, new Rectangle(0, 0, pv.Width, pv.Height));
                bool inked = false;
                for (int x = 2; x < 140 && !inked; x++)
                    for (int y = 2; y < WizardGridPreview.KeyHeight && !inked; y++)
                        if (bmp.GetPixel(x, y).GetBrightness() < 0.3f) inked = true;
                Assert(inked, "the first key of the preview is drawn with its letter");
            }
        }
    }
}

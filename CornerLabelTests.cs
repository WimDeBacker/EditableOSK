// CornerLabelTests.cs — the option to hide the small Shift / AltGr labels in the corners of the keys
// (LayoutMeta.ShowCornerLabels, for people who find so many signs on the keyboard overstimulating).

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_CornerLabels()
        {
            Section("Corner labels — the option to hide the Shift / AltGr labels");

            // ── The setting: on by default, copied, saved only when off, old files keep the labels ──
            var meta = new LayoutMeta();
            Assert(meta.ShowCornerLabels, "default: the corner labels are shown");
            meta.ShowCornerLabels = false;
            Assert(!meta.Clone().ShowCornerLabels, "Clone keeps the setting");
            var other = new LayoutMeta();
            other.CopyFrom(meta);
            Assert(!other.ShowCornerLabels, "CopyFrom keeps the setting");

            string Save(bool show)
            {
                string path = Path.Combine(Path.GetTempPath(), $"corner_{Guid.NewGuid()}.kbl");
                var layout = new GridLayout(1, 1);
                layout.Cells.Add(new GridCell(0, 0, new KeyProps("a", "a", "A", "A", "á", "á")));
                layout.Groups.Add(new KeyGroup { Name = SettingsManager.StandardGroupName });
                SettingsManager.SaveSettings(layout, new VisualTheme(), new WindowState(), new LayoutMeta { ShowCornerLabels = show }, path);
                return path;
            }
            LayoutMeta Load(string path)
            {
                var m = new LayoutMeta();
                SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), m, path);
                return m;
            }

            string off = Save(false), on = Save(true);
            try
            {
                Assert(File.ReadAllText(off).Contains("ShowCornerLabels=\"0\""), "switched off: the file says so");
                Assert(!Load(off).ShowCornerLabels, "switched off: it loads as off");
                Assert(!File.ReadAllText(on).Contains("ShowCornerLabels"), "switched on: nothing is written (the file stays as small as before)");
                Assert(Load(on).ShowCornerLabels, "switched on: it loads as on");

                // A layout written before this option existed has no attribute: it must keep its labels.
                string old = File.ReadAllText(on);
                Assert(!old.Contains("ShowCornerLabels") && Load(on).ShowCornerLabels, "an old file (no attribute) keeps the labels");
            }
            finally { File.Delete(off); File.Delete(on); }

            // ── The Keyboard Editor: the check box shows the setting and writes it back ──
            foreach (bool show in new[] { true, false })
            {
                using var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta { ShowCornerLabels = show }, owner: null);
                var chk = (CheckBox)typeof(KeyboardEditorForm).GetField("_chkCornerLabels", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
                Assert(chk.Checked == show, $"Keyboard Editor: the check box shows the setting ({(show ? "on" : "off")})");
                chk.Checked = !show;
                bool applied = (bool)typeof(KeyboardEditorForm).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
                Assert(applied && f.ResultMeta.ShowCornerLabels == !show, $"Keyboard Editor: Apply writes the changed setting back ({(!show ? "on" : "off")})");
            }
            try
            {
                Lang.Load("nl");
                Assert(Lang.T("Show Shift and AltGr labels") != "Show Shift and AltGr labels" && Lang.T("tip: Show Shift and AltGr labels") != "tip: Show Shift and AltGr labels",
                    "the check box and its tooltip are translated into Dutch");
            }
            finally { Lang.Load("en"); }

            // ── What is drawn: the Shift label top right, the AltGr label top left, nothing for an empty label ──
            using var font = new Font("Arial", 8f);
            Bitmap Paint(string shift, string altGr)
            {
                var bmp = new Bitmap(80, 50);
                using var g = Graphics.FromImage(bmp);
                g.Clear(Color.Black);
                KeyboardForm.DrawCornerLabels(g, bmp.Size, shift, altGr, font, Color.White);
                return bmp;
            }
            bool Lit(Bitmap b, Rectangle r)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                    for (int x = r.Left; x < r.Right; x++)
                        if (b.GetPixel(x, y).R > 40) return true;
                return false;
            }
            var topLeft = new Rectangle(0, 0, 30, 20); var topRight = new Rectangle(50, 0, 30, 20); var middle = new Rectangle(0, 25, 80, 25);
            using (var b = Paint("A", "á"))
                Assert(Lit(b, topRight) && Lit(b, topLeft) && !Lit(b, middle), "both labels: Shift top right, AltGr top left, nothing else");
            using (var b = Paint("A", ""))
                Assert(Lit(b, topRight) && !Lit(b, topLeft), "only a Shift label: nothing in the top left");
            using (var b = Paint("", "á"))
                Assert(!Lit(b, topRight) && Lit(b, topLeft), "only an AltGr label: nothing in the top right");
            using (var b = Paint("", ""))
                Assert(!Lit(b, topRight) && !Lit(b, topLeft), "no labels: nothing is drawn");
            using (var b = Paint("A", "A"))
                Assert(Lit(b, topRight) && !Lit(b, topLeft), "an AltGr label equal to the Shift label is not drawn twice");
            // Not covered headlessly: KeyboardForm itself is never constructed in the suite, so the one `if (_meta.ShowCornerLabels)`
            // around the call in OnButtonPaint is verified by reading the code; try the option by hand (Edit Keyboard → Accessibility).
        }
    }
}

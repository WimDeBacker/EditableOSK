// DevGallery.cs — developer tools for the editor-dialog redesign.
//
//   TouchSpikeForm  a small content-sized dialog that uses every touch-friendly building block
//                   (section bar, table rows, steppers, colour rows, footer). It mirrors the
//                   planned Key Editor, and is what the UI guard tests exercise.
//   DevGallery      "OnScreenKeyboard.exe --gallery <folder>" opens the dialogs in English, Dutch
//                   and a pseudo-localised language (every string 40 % longer) and saves a PNG of
//                   each, so a layout can be checked by eye without clicking through the app.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>A content-sized, sectioned sample dialog built only from the touch-friendly controls.</summary>
    internal sealed class TouchSpikeForm : FluentDialogBase
    {
        private readonly Label  _lblPreview;

        internal TouchCheckBox AutoSizeCheck { get; }
        internal TouchStepper  FontSize { get; }
        internal TouchTextBox  LabelBox { get; }
        internal Button        KeySwatch { get; }
        internal FluentButton  ApplyButton { get; }
        internal TouchComboBox FontCombo { get; }

        public TouchSpikeForm()
        {
            Text = "Touch spike";

            // ── Footer: live preview on the left, Cancel / Apply on the right ──
            var preview = new Panel { Size = new Size(88, Touch.Target), Tag = "notheme", BackColor = Color.FromArgb(30, 30, 50) };
            _lblPreview = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(224, 224, 255), BackColor = Color.FromArgb(45, 45, 74),
                Font = Fluent.FontPreviewKey, Text = "a",
            };
            preview.Controls.Add(_lblPreview);
            var cancel = MakeTouchButton(() => Lang.T("Cancel"));
            ApplyButton = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(preview, cancel, ApplyButton), withSections: true);

            // ── Section 1: Key ──
            var key = AddSection(() => Lang.T("Key Content"));
            LabelBox = new TouchTextBox { Text = "a" };
            LabelBox.TextChanged += (s, e) => _lblPreview.Text = LabelBox.Text;
            AddRow(key, () => Lang.T("Label"), LabelBox);

            var modes = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            foreach (var m in new Func<string>[]
                { () => Lang.T("Text"), () => Lang.T("Key/Shortcut"), () => Lang.T("Modifier"),
                  () => Lang.T("Word prediction"), () => Lang.T("Layout") })
            {
                var b = MakeTouchButton(m);
                b.Margin = new Padding(0, 0, Touch.Gap, Touch.Gap);
                modes.Controls.Add(b);
            }
            AddWideRow(key, modes);
            AddRow(key, () => Lang.T("Send"), new TouchTextBox());

            var span = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            span.Controls.Add(SpanPair(() => Lang.T("Key width")));
            span.Controls.Add(SpanPair(() => Lang.T("Key height")));
            AddWideRow(key, span);

            // ── Section 2: Shift & AltGr ──
            var alt = AddSection(() => "Shift / AltGr");
            AddRow(alt, () => Lang.T("Shift label"),  new TouchTextBox());
            AddRow(alt, () => Lang.T("Shift send"),   new TouchTextBox());
            AddRow(alt, () => Lang.T("AltGr label"),  new TouchTextBox());
            AddRow(alt, () => Lang.T("AltGr send"),   new TouchTextBox());

            // ── Section 3: Appearance ──
            var look = AddSection(() => Lang.T("Appearance"));
            var group = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var groupCombo = new TouchComboBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Touch.Gap, 0) };
            groupCombo.Items.AddRange(new object[] { "(no group)", "standard", "Klinkers", "Medeklinkers" });
            groupCombo.SelectedIndex = 0;
            var manage = MakeTouchButton(() => Lang.T("Manage Groups…"));
            manage.Margin = Padding.Empty;
            group.Controls.Add(groupCombo, 0, 0);
            group.Controls.Add(manage, 1, 0);
            AddRow(look, () => Lang.T("Group"), group);

            FontCombo = new TouchComboBox();
            FontCombo.Items.AddRange(new object[] { "Segoe UI", "Arial", "Courier New", "Verdana" });
            FontCombo.SelectedIndex = 0;
            AddRow(look, () => Lang.T("Font"), FontCombo);

            FontSize = new TouchStepper { Minimum = 0, Maximum = 72, Value = 14 };
            AutoSizeCheck = NewCheck(() => Lang.T("Auto"));
            AutoSizeCheck.CheckedChanged += (s, e) => FontSize.Enabled = !AutoSizeCheck.Checked;
            var sizeRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            FontSize.Margin = new Padding(0, 0, Touch.Gap, 0);
            sizeRow.Controls.Add(FontSize);
            sizeRow.Controls.Add(AutoSizeCheck);
            AddRow(look, () => Lang.T("Font size"), sizeRow, fill: false);

            AddRow(look, () => Lang.T("Font color"),   MakeColorRow(out _), fill: true);
            var keyRow = MakeColorRow(out var keySwatch);
            KeySwatch = keySwatch;
            AddRow(look, () => Lang.T("Key color"),    keyRow);
            AddRow(look, () => Lang.T("Border color"), MakeColorRow(out _));
            AddRow(look, () => Lang.T("Border thickness"), new TouchStepper { Minimum = -1, Maximum = 10, Value = 1 }, fill: false);

            AcceptButton = ApplyButton;
            CancelButton = cancel;
        }

        private Control SpanPair(Func<string> label)
        {
            var pair = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, Touch.Gap * 2, 0) };
            var lbl = new Label
            {
                Text = label(), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = true,
                Margin = new Padding(0, 0, Touch.Gap, 0), MinimumSize = new Size(0, Touch.Target),
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = Fluent.FontLabel,
            };
            _transLabels.Add((lbl, label));
            var st = new TouchStepper { Minimum = 1, Maximum = 14, Value = 1, AccessibleName = Lang.StripMnemonic(label()), Margin = Padding.Empty };
            pair.Controls.Add(lbl);
            pair.Controls.Add(st);
            return pair;
        }

        /// <summary>Selects a section by index (used by the gallery and the tests).</summary>
        internal void SelectSection(int index) => ShowSection(index);
        internal SectionBar SectionButtons => Sections;
        internal bool CheckSections() => ShowFirstSectionWithError();
        internal int Measure(out Size preferred) { preferred = MeasureContent(); return preferred.Height; }

        /// <summary>Layout numbers for diagnosing sizing problems (written by the gallery).</summary>
        internal string Diagnose()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"client={ClientSize} form={Size} dpi={DeviceDpi}");
            sb.AppendLine($"measure(760)={MeasureContent()}  measure(client w)={MeasureContent(ClientSize.Width)}");
            foreach (Control c in Controls)
                Dump(sb, c, 0);
            return sb.ToString();
        }

        private static void Dump(System.Text.StringBuilder sb, Control c, int depth)
        {
            if (depth > 3) return;
            sb.AppendLine($"{new string(' ', depth * 2)}{c.GetType().Name} vis={c.Visible} bounds={c.Bounds} pref={c.GetPreferredSize(new Size(c.Width, 0))}");
            foreach (Control ch in c.Controls) Dump(sb, ch, depth + 1);
        }
    }

    /// <summary>"--gallery &lt;folder&gt;": saves a PNG of each dialog in several languages.</summary>
    internal static class DevGallery
    {
        public static int Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var languages = new (string Tag, string Code, double Pseudo)[]
                { ("en", "en", 0), ("nl", "nl", 0), ("pseudo", "en", 0.4) };
            try
            {
                foreach (var (tag, code, pseudo) in languages)
                {
                    Lang.Load(code);
                    Lang.PseudoExpansion = pseudo;

                    using (var spike = new TouchSpikeForm())
                    {
                        Show(spike);
                        if (tag == "en") File.WriteAllText(Path.Combine(outDir, "spike_diag.txt"), spike.Diagnose());
                        for (int i = 0; i < spike.SectionButtons.Count; i++)
                        {
                            spike.SelectSection(i);
                            spike.SectionButtons.Select(i, focus: false);
                            Save(spike, Path.Combine(outDir, $"spike_{tag}_{i + 1}.png"));
                            if (tag == "en" && i == 2) SaveCrop(spike, spike.FontSize, Path.Combine(outDir, "zoom_stepper.png"));
                            if (tag == "en" && i == 0) SaveCrop(spike, spike.LabelBox, Path.Combine(outDir, "zoom_textbox.png"));
                        }
                    }

                    var groups = new List<KeyGroup> { new KeyGroup { Name = "standard" } };
                    using (var f = new KeyEditorForm(new KeyProps("a", "a"), null, groups: groups)) { Show(f); Save(f, Path.Combine(outDir, $"keyeditor_{tag}.png")); }
                    using (var f = new GroupEditorForm(groups))                                     { Show(f); Save(f, Path.Combine(outDir, $"groupeditor_{tag}.png")); }
                    using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), null))
                    { Show(f); Save(f, Path.Combine(outDir, $"keyboardeditor_{tag}.png")); }

                    // Mock-ups for the Key Editor layout decision (see KeyEditorMockups.cs); no Dutch needed.
                    if (tag != "nl") SaveMockups(outDir, tag);
                    SaveMockupD(outDir, tag);
                    if (tag != "nl") SaveMockupE(outDir, tag);
                }
            }
            finally { Lang.PseudoExpansion = 0; Lang.Load("en"); }
            return 0;
        }

        /// <summary>One screenshot per state worth judging of each Key Editor mock-up option.</summary>
        private static void SaveMockups(string outDir, string tag)
        {
            using (var a = new KeyEditorMockupA())
            {
                Show(a);
                Save(a, Path.Combine(outDir, $"mockup_A1_normal_{tag}.png"));
                a.SelectLayer(1);
                Save(a, Path.Combine(outDir, $"mockup_A2_shift_{tag}.png"));
            }
            using (var b = new KeyEditorMockupB())
            {
                Show(b);
                Save(b, Path.Combine(outDir, $"mockup_B_{tag}.png"));
            }
            using (var c = new KeyEditorMockupC())
            {
                Show(c);
                Save(c, Path.Combine(outDir, $"mockup_C_{tag}.png"));
            }
        }

        /// <summary>Option D: the Key section, the open action flyout, and the Appearance section.</summary>
        private static void SaveMockupD(string outDir, string tag)
        {
            using var d = new KeyEditorMockupD();
            Show(d);
            Save(d, Path.Combine(outDir, $"mockup_D1_key_{tag}.png"));

            // The flyout is a window of its own, so it is photographed separately and pasted onto the dialog.
            var popup = d.Choosers[1].OpenPopup(keepOpen: true);
            Application.DoEvents();
            SaveWithPopup(d, popup, Path.Combine(outDir, $"mockup_D2_flyout_{tag}.png"));
            popup.Close();

            d.ShowAppearanceSection();
            Application.DoEvents();
            d.Refit();
            Save(d, Path.Combine(outDir, $"mockup_D3_appearance_{tag}.png"));
        }

        /// <summary>Photographs a dialog with one of its flyouts, including any part of the flyout that hangs outside the dialog.</summary>
        internal static void SaveWithPopup(Form f, Form popup, string path)
        {
            var union = Rectangle.Union(new Rectangle(f.Left, f.Top, f.Width, f.Height),
                                        new Rectangle(popup.Left, popup.Top, popup.Width, popup.Height));
            using var fb = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(fb, new Rectangle(0, 0, f.Width, f.Height));
            using var pb = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(pb, new Rectangle(0, 0, popup.Width, popup.Height));
            using var bmp = new Bitmap(union.Width, union.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(200, 200, 200));           // the desktop, where the flyout leaves the dialog
                g.DrawImage(fb, f.Left - union.Left, f.Top - union.Top);
                g.DrawImage(pb, popup.Left - union.Left, popup.Top - union.Top);
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        /// <summary>Option E in both themes: both sections, plus the Group, Font and colour flyouts open.</summary>
        private static void SaveMockupE(string outDir, string tag)
        {
            bool wasLight = ToolbarButton.IsLightTheme;
            try
            {
                foreach (bool light in new[] { true, false })
                {
                    ToolbarButton.IsLightTheme = light;       // read when a dialog is created, so set it first
                    string theme = light ? "light" : "dark";
                    using var d = new KeyEditorMockupE();
                    Show(d);
                    Save(d, Path.Combine(outDir, $"mockup_E1_key_{theme}_{tag}.png"));

                    d.ShowAppearanceSection();
                    Application.DoEvents();
                    d.Refit();
                    Save(d, Path.Combine(outDir, $"mockup_E2_appearance_{theme}_{tag}.png"));

                    var group = d.GroupChooser.OpenPopup(keepOpen: true);
                    Application.DoEvents();
                    SaveWithPopup(d, group, Path.Combine(outDir, $"mockup_E3_group_{theme}_{tag}.png"));
                    group.Close();

                    // A small screen: the font list gets only 420 px, so it scrolls instead of growing past the screen.
                    var font = d.FontChooser.OpenPopup(keepOpen: true, maxHeightOverride: 420);
                    Application.DoEvents();
                    SaveWithPopup(d, font, Path.Combine(outDir, $"mockup_E4_font_{theme}_{tag}.png"));
                    font.Close();

                    var colour = d.KeyChip.OpenPicker(keepOpen: true);
                    Application.DoEvents();
                    SaveWithPopup(d, colour, Path.Combine(outDir, $"mockup_E5_colour_{theme}_{tag}.png"));
                    colour.Close();
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLight; }
        }

        /// <summary>Shows a dialog invisibly (opacity 0) so it lays out at its real size, then lets it settle.</summary>
        internal static void Show(Form f)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location      = new Point(20, 20);
            f.ShowInTaskbar = false;
            f.Opacity       = 0;
            f.Show();
            Application.DoEvents();
            f.PerformLayout();
            Application.DoEvents();
        }

        /// <summary>Saves an enlarged crop of one control (to inspect alignment down to the pixel).</summary>
        internal static void SaveCrop(Form f, Control c, string path, int scale = 4)
        {
            using var full = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(full, new Rectangle(0, 0, f.Width, f.Height));
            // DrawToBitmap output is offset by the non-client area (title bar and borders).
            var origin = f.PointToScreen(Point.Empty);
            var nonClient = new Point(origin.X - f.Left, origin.Y - f.Top);
            var r = c.Parent.RectangleToScreen(c.Bounds);
            var src = new Rectangle(r.X - f.Left, r.Y - f.Top, r.Width, r.Height);
            using var crop = new Bitmap(src.Width * scale, src.Height * scale);
            using (var g = Graphics.FromImage(crop))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(full, new Rectangle(0, 0, crop.Width, crop.Height), src, GraphicsUnit.Pixel);
            }
            crop.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        internal static void Save(Form f, string path)
        {
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}

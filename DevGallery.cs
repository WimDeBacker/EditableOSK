// DevGallery.cs — developer tool for checking the dialogs by eye.
//
//   DevGallery      "OnScreenKeyboard.exe --gallery <folder>" opens the dialogs in English, Dutch
//                   and a pseudo-localised language (every string 40 % longer) and saves a PNG of
//                   each, so a layout can be checked without clicking through the app.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
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

                    var groups = new List<KeyGroup> { new KeyGroup { Name = "standard" } };
                    using (var f = new KeyEditorForm(new KeyProps("a", "a"), null, groups: groups)) { Show(f); Save(f, Path.Combine(outDir, $"keyeditor_{tag}.png")); }
                    using (var f = new GroupEditorForm(groups))                                     { Show(f); Save(f, Path.Combine(outDir, $"groupeditor_{tag}.png")); }
                    using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), null))
                    { Show(f); Save(f, Path.Combine(outDir, $"keyboardeditor_{tag}.png")); }

                    SaveKeyEditor(outDir, tag);
                    SaveGroupEditor(outDir, tag);
                    SaveKeyboardEditor(outDir, tag);
                    SaveWizard(outDir, tag);
                }
            }
            finally { Lang.PseudoExpansion = 0; Lang.Load("en"); }
            return 0;
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

        /// <summary>The real Key Editor in both themes: both sections and the Shift layer's action chooser open.</summary>
        private static void SaveKeyEditor(string outDir, string tag)
        {
            bool wasLight = ToolbarButton.IsLightTheme;
            var groups = new List<KeyGroup> { new KeyGroup { Name = SettingsManager.StandardGroupName }, new KeyGroup { Name = "Klinkers" } };
            var props = new KeyProps("Ctrl+c", "^c", "A", "layout:azerty.kbl", "€", "€");
            try
            {
                foreach (bool light in new[] { true, false })
                {
                    ToolbarButton.IsLightTheme = light;
                    string theme = light ? "light" : "dark";
                    using var d = new KeyEditorForm(props, null, groups: groups, layoutDir: AppDomain.CurrentDomain.BaseDirectory);
                    Show(d);
                    Save(d, Path.Combine(outDir, $"keyeditor_real_key_{theme}_{tag}.png"));
                    if (tag == "en") SaveCrop(d, d.SectionBarAccess.Tabs[0], Path.Combine(outDir, $"zoom_active_tab_{theme}.png"));

                    var shiftType = ((TouchChoiceButton[])typeof(KeyEditorForm)
                        .GetField("_types", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(d))[1];
                    var popup = shiftType.OpenPopup(keepOpen: true);
                    Application.DoEvents();
                    SaveWithPopup(d, popup, Path.Combine(outDir, $"keyeditor_real_action_{theme}_{tag}.png"));
                    popup.Close();

                    d.SectionBarAccess.Select(1, focus: false);
                    Application.DoEvents();
                    d.PerformLayout();
                    Save(d, Path.Combine(outDir, $"keyeditor_real_appearance_{theme}_{tag}.png"));

                    // A narrow screen (480 design px: a small tablet, or 200 % scaling on a laptop): the Action row takes two lines.
                    using (var n = new KeyEditorForm(props, null, groups: groups, layoutDir: AppDomain.CurrentDomain.BaseDirectory))
                    {
                        Show(n);
                        n.MinimumSize = Size.Empty;
                        n.ClientSize = new Size(480, n.ClientSize.Height);
                        Application.DoEvents();
                        n.PerformLayout();
                        Save(n, Path.Combine(outDir, $"keyeditor_narrow_key_{theme}_{tag}.png"));
                        n.SectionBarAccess.Select(1, focus: false);
                        Application.DoEvents();
                        n.PerformLayout();
                        Save(n, Path.Combine(outDir, $"keyeditor_narrow_appearance_{theme}_{tag}.png"));
                    }

                    // A word prediction key: its Shift and AltGr rows are empty and disabled.
                    var wpProps = new KeyProps("w", "wp:1", "W", "x", "€", "y");
                    using var w = new KeyEditorForm(wpProps, null, groups: groups, layoutDir: AppDomain.CurrentDomain.BaseDirectory);
                    Show(w);
                    Save(w, Path.Combine(outDir, $"keyeditor_real_wp_{theme}_{tag}.png"));
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLight; }
        }

        /// <summary>The real Group Editor in both themes: a group that inherits, the standard group, the colour flyout with its inherit button, and the small dialogs.</summary>
        private static void SaveGroupEditor(string outDir, string tag)
        {
            bool wasLight = ToolbarButton.IsLightTheme;
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            T Field<T>(object o, string name) => (T)o.GetType().GetField(name, bf).GetValue(o);
            var groups = new List<KeyGroup>
            {
                new KeyGroup { Name = SettingsManager.StandardGroupName, FontName = "Arial", KeyColor = Color.FromArgb(45, 45, 74),
                               FontColor = Color.FromArgb(224, 224, 255), BorderColor = Color.FromArgb(120, 120, 160), BorderThickness = 1 },
                new KeyGroup { Name = "Klinkers", KeyColor = Color.FromArgb(74, 143, 212), FontSize = 18, BorderThickness = -1 },
                new KeyGroup { Name = "Medeklinkers", FontColor = Color.White, BorderThickness = 2, FontName = "Consolas" },
                new KeyGroup { Name = "Cijfers", BorderThickness = -1 },
            };
            var imported = new List<KeyGroup>
            {
                new KeyGroup { Name = "standard" }, new KeyGroup { Name = "Klinkers" }, new KeyGroup { Name = "Pijlen" },
            };
            var existing = new HashSet<string>(groups.ConvertAll(g => g.Name), StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (bool light in new[] { true, false })
                {
                    ToolbarButton.IsLightTheme = light;
                    string theme = light ? "light" : "dark";
                    using (var d = new GroupEditorForm(groups, "Klinkers"))
                    {
                        Show(d);
                        Save(d, Path.Combine(outDir, $"groupeditor_real_{theme}_{tag}.png"));
                        if (light && tag == "en") File.WriteAllText(Path.Combine(outDir, "groupeditor_diag.txt"), d.DiagnoseLayout());
                        var chip = Field<ColorChip>(d, "_chipFont");
                        var fly = chip.OpenPicker(keepOpen: true);
                        Application.DoEvents();
                        SaveWithPopup(d, fly, Path.Combine(outDir, $"groupeditor_real_colour_{theme}_{tag}.png"));
                        fly.Close();
                        var font = Field<TouchChoiceButton>(d, "_cmbFont").OpenPopup(keepOpen: true, maxHeightOverride: 420);
                        Application.DoEvents();
                        SaveWithPopup(d, font, Path.Combine(outDir, $"groupeditor_real_font_{theme}_{tag}.png"));
                        font.Close();
                    }
                    using (var d = new GroupEditorForm(groups, SettingsManager.StandardGroupName))
                    {
                        Show(d);
                        Save(d, Path.Combine(outDir, $"groupeditor_real_standard_{theme}_{tag}.png"));
                    }
                    using (var n = new NameDialog(Lang.T("New Group"), name =>
                               string.Equals(name, "standard", StringComparison.OrdinalIgnoreCase) ? Lang.T("Name 'standard' is reserved.") : null))
                    {
                        Show(n);
                        Field<TouchTextBox>(n, "_txt").Text = "Standard";
                        Application.DoEvents();
                        Save(n, Path.Combine(outDir, $"groupeditor_name_{theme}_{tag}.png"));
                    }
                    using (var im = new ImportDialog(imported, existing))
                    {
                        Show(im);
                        Save(im, Path.Combine(outDir, $"groupeditor_import_{theme}_{tag}.png"));
                    }
                    using (var m = new TouchMessage(Lang.T("Delete Group"), string.Format(Lang.T("Delete group msg"), "Klinkers"), question: true))
                    {
                        Show(m);
                        Save(m, Path.Combine(outDir, $"groupeditor_confirm_{theme}_{tag}.png"));
                    }
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLight; }
        }

        /// <summary>The new Keyboard Editor: its three sections, in both themes (with fake word-database data, so no real file is needed).</summary>
        private static void SaveKeyboardEditor(string outDir, string tag)
        {
            bool wasLight = ToolbarButton.IsLightTheme;
            var backend = new WordPredictionBackend
            {
                Candidates     = () => new List<(string Word, int Count)> { ("kapstok", 2), ("tuinhek", 2), ("wasbeer", 1), ("fietspomp", 1) },
                Databases      = () => new List<DatabaseInfo> { new DatabaseInfo(@"C:\x\worddb_NL.wfq", "nl"), new DatabaseInfo(@"C:\x\worddb_EN.wfq", "en") },
                IsLoaded       = () => true, LoadedLanguage = () => "nl", WordCount = () => 52431,
                FileExists     = p => true,
            };
            var meta = new LayoutMeta { StickyModifiers = true, SlowKeysMs = 300, WordDatabase = "worddb_NL.wfq", Language = Lang.CurrentCode };
            try
            {
                foreach (bool light in new[] { true, false })
                {
                    ToolbarButton.IsLightTheme = light;
                    string theme = light ? "light" : "dark";
                    using var d = new KeyboardEditorForm(new VisualTheme(), new WindowState(), meta, null, null, null, null, null, backend);
                    Show(d);
                    for (int i = 0; i < d.SectionBarAccess.Count; i++)
                    {
                        d.SectionBarAccess.Select(i, focus: false);
                        Application.DoEvents();
                        d.PerformLayout();
                        Save(d, Path.Combine(outDir, $"keyboardeditor2_{i + 1}_{theme}_{tag}.png"));
                    }
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLight; }
        }

        /// <summary>Every page of the New Keyboard Wizard in both themes, with pasted labels so the preview and the summary show real content.</summary>
        private static void SaveWizard(string outDir, string tag)
        {
            bool wasLight = ToolbarButton.IsLightTheme;
            try
            {
                foreach (bool light in new[] { true, false })
                {
                    ToolbarButton.IsLightTheme = light;
                    string theme = light ? "light" : "dark";
                    using var w = new NewKeyboardWizard();
                    var t = typeof(NewKeyboardWizard);
                    using (var keys = new SpecialKeysDialog(tag == "nl")) { Show(keys); Save(keys, Path.Combine(outDir, $"wizard_keys_{theme}_{tag}.png")); }
                    ((RadioButton)t.GetField("_rbPaste", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w)).Checked = true;
                    ((TextBox)t.GetField("_txtPaste", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w)).Text =
                        "q w e r t y u i o p\r\na s d f g h j k l\r\nz x c v b n m [Backspace]\r\n[Space] \"good morning\"";
                    Show(w);
                    for (int i = 0; i < w.HostSectionCount; i++)
                    {
                        w.ShowSectionForGuard(i);
                        Application.DoEvents();
                        w.PerformLayout();
                        Save(w, Path.Combine(outDir, $"wizard_{i + 1}_{theme}_{tag}.png"));
                    }
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

// UiGuardTests.cs — tests for the touch-friendly controls and the reusable UI "guards" that
// keep the editor dialogs usable for people with a motor disability:
//
//   • every clickable / typeable control is at least 44 x 44 px            (TargetViolations)
//   • no label or button text is cut off, in English, Dutch or a language   (ClippedText)
//     whose words are 40 % longer than English (pseudo-localisation)
//   • nothing sticks out of the area that holds it                          (Overflow)
//   • a dialog fits a 1366 x 768 laptop screen                              (ScreenFit)
//
// The guards run strictly on the new touch dialog. The three existing editors are only
// *reported* (ui_guard_report.txt next to the test results) until each is migrated; migrating a
// dialog means moving it from the report to the strict list.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal static class UiGuard
    {
        /// <summary>Every control below <paramref name="root"/>, whether visible or not.</summary>
        public static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var d in All(c)) yield return d;
            }
        }

        private static string Describe(Control c) =>
            $"{c.GetType().Name} '{c.Text}' [{c.AccessibleName}] {c.Width}x{c.Height}";

        /// <summary>Scale factor between design pixels and the pixels this form is actually laid out in.</summary>
        private static double Scale(Control root) => root is Form f && f.IsHandleCreated ? f.DeviceDpi / 96.0 : 1.0;

        /// <summary>Pointer controls smaller than 44 x 44 design pixels.</summary>
        public static List<string> TargetViolations(Control root, bool visibleOnly)
        {
            int min = (int)Math.Floor(Touch.Target * Scale(root));
            return All(root)
                .Where(c => (!visibleOnly || c.Visible) && Touch.IsPointerControl(c))
                .Where(c => c.Width < min || c.Height < min)
                .Select(Describe).ToList();
        }

        /// <summary>Labels, buttons and check boxes whose text does not fit inside the control.</summary>
        public static List<string> ClippedText(Control root, bool visibleOnly)
        {
            var bad = new List<string>();
            foreach (var c in All(root))
            {
                if (visibleOnly && !c.Visible) continue;
                if (!(c is Label || c is ButtonBase) || string.IsNullOrWhiteSpace(c.Text)) continue;
                bool wraps = c is Label || c is TouchTile;     // a tile name may take two lines
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | (wraps ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine);
                string text = Lang.StripMnemonic(c.Text);
                var avail = new Size(Math.Max(1, c.ClientSize.Width - c.Padding.Horizontal), wraps ? int.MaxValue : c.ClientSize.Height);
                var need = TextRenderer.MeasureText(text, c.Font, avail, flags);
                if (need.Width  > c.ClientSize.Width  - c.Padding.Horizontal + 2 ||
                    need.Height > c.ClientSize.Height + 2)
                    bad.Add($"{Describe(c)} needs {need.Width}x{need.Height}");
            }
            return bad;
        }

        /// <summary>
        /// Alignment rule (spec D23): the members of an <see cref="OptionStackPanel"/> have one width and one right edge, and the
        /// buttons of a <see cref="ButtonRowPanel"/> have one width (a pixel of rounding is allowed).
        /// </summary>
        public static List<string> StackedEdges(Control root, bool visibleOnly)
        {
            var bad = new List<string>();
            foreach (var c in All(root))
            {
                if (visibleOnly && !c.Visible) continue;
                if (c is OptionStackPanel stack)
                {
                    var members = stack.Controls.Cast<Control>().Where(m => !visibleOnly || m.Visible).ToList();
                    if (members.Count > 1 && (members.Max(m => m.Right) - members.Min(m => m.Right) > 1 || members.Max(m => m.Width) - members.Min(m => m.Width) > 1))
                        bad.Add("stack: " + string.Join(", ", members.Select(m => $"{m.GetType().Name} '{m.Text}' w{m.Width} right{m.Right}")));
                }
                else if (c is ButtonRowPanel row)
                {
                    var members = row.Controls.Cast<Control>().Where(m => !visibleOnly || m.Visible).ToList();
                    if (members.Count > 1 && members.Max(m => m.Width) - members.Min(m => m.Width) > 1)
                        bad.Add("button row: " + string.Join(", ", members.Select(m => $"'{m.Text}' w{m.Width}")));
                }
            }
            return bad;
        }

        /// <summary>Controls that extend past the container that holds them (scrolling containers may grow downwards).</summary>
        public static List<string> Overflow(Control root, bool visibleOnly)
        {
            var bad = new List<string>();
            foreach (var c in All(root))
            {
                if (visibleOnly && !c.Visible) continue;
                var p = c.Parent;
                if (p == null || c.Dock != DockStyle.None && c.Dock != DockStyle.Top) continue;
                bool scrolls = p is ScrollableControl s && s.AutoScroll;
                if (c.Right > p.ClientSize.Width + 1 || (!scrolls && c.Bottom > p.ClientSize.Height + 1))
                    bad.Add($"{Describe(c)} at {c.Bounds} in {p.GetType().Name} {p.ClientSize}");
            }
            return bad;
        }
    }

    public static partial class TestRunner
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RectStruct r);
        [StructLayout(LayoutKind.Sequential)] private struct RectStruct { public int Left, Top, Right, Bottom; }

        private static T Invoke<T>(object target, string method, params object[] args)
        {
            var m = typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)m.Invoke(target, args);
        }

        /// <summary>A plain click (as from Space / Enter); PerformClick does nothing on a button that is not visible.</summary>
        private static void ClickButton(Button b) => Invoke<object>(b, "OnClick", EventArgs.Empty);

        private static void RaiseMouse(Control c, string handler, MouseButtons b = MouseButtons.Left) =>
            typeof(Control).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(c, new object[] { new MouseEventArgs(b, 1, 5, 5, 0) });

        // ════════════════════════════════════════════════════════════════
        // The touch-friendly building blocks
        // ════════════════════════════════════════════════════════════════
        private static void T_TouchControls()
        {
            Section("Touch controls — TouchTextBox / TouchComboBox / TouchCheckBox");

            using (var host = new Form())
            {
                // ── TouchTextBox ──
                var tb = new TouchTextBox();
                host.Controls.Add(tb);
                _ = host.Handle;   // create the handles without showing the form
                Assert(tb.Height >= Touch.Target, $"TouchTextBox: at least {Touch.Target} px tall ({tb.Height})");
                Assert(tb.Multiline && !tb.AcceptsReturn && !tb.WordWrap,
                    "TouchTextBox: multi-line box locked to one line (Enter goes to the default button)");
                var r = new RectStruct();
                SendMessage(tb.Handle, 0x00B2 /* EM_GETRECT */, IntPtr.Zero, ref r);
                int textH = TextRenderer.MeasureText("Ag", tb.Font).Height;
                Assert(r.Top >= (tb.ClientSize.Height - textH) / 2 - 2,
                    $"TouchTextBox: text is vertically centred (inset {r.Top}px)");
                tb.Text = "a\r\nb\rc\nd";
                Assert(tb.Text == "a b c d", "TouchTextBox: line breaks (typed or pasted) become spaces");
                int changed = 0;
                tb.TextChanged += (s, e) => changed++;
                tb.Text = "x\ny";
                Assert(tb.Text == "x y" && changed == 1, $"TouchTextBox: one TextChanged for a cleaned paste ({changed})");
                tb.Height = 10;
                Assert(tb.Height >= Touch.Target, "TouchTextBox: cannot be made shorter than the target size");

                // ── TouchComboBox ──
                var cb = new TouchComboBox();
                cb.Items.AddRange(new object[] { "one", "two" });
                host.Controls.Add(cb);
                _ = cb.Handle;
                int minH = (int)Math.Round(Touch.Target * cb.DeviceDpi / 96.0);
                Assert(cb.Height >= minH, $"TouchComboBox: at least {minH} px tall ({cb.Height})");
                Assert(cb.DropDownStyle == ComboBoxStyle.DropDownList && cb.DrawMode == DrawMode.OwnerDrawFixed,
                    "TouchComboBox: drop-down list, owner-drawn");
                Assert(cb.ItemHeight >= Touch.Target - 8, $"TouchComboBox: list rows are tall enough to tap ({cb.ItemHeight})");

                // ── TouchCheckBox ──
                var ck = new TouchCheckBox { Text = "Auto" };
                host.Controls.Add(ck);
                _ = ck.Handle;
                Assert(ck.GetPreferredSize(Size.Empty).Height >= Touch.Target, "TouchCheckBox: at least 44 px tall (the whole row toggles it)");
                Assert(ck.MinimumSize.Height >= Touch.Target, "TouchCheckBox: minimum height is the target size");
            }

            Section("Touch controls — TouchStepper");
            {
                using var host = new Form();
                var st = new TouchStepper { Minimum = 1, Maximum = 5, Value = 3, AccessibleName = "Key width" };
                host.Controls.Add(st);
                _ = host.Handle;
                int events = 0;
                st.ValueChanged += (s, e) => events++;

                Assert(st.Value == 3 && st.ValueBox.Text == "3", "stepper: shows its value");
                Assert(st.DecreaseButton.Width >= Touch.Target && st.DecreaseButton.Height >= Touch.Target &&
                       st.IncreaseButton.Width >= Touch.Target && st.IncreaseButton.Height >= Touch.Target,
                    "stepper: both buttons are at least 44 x 44");
                Assert(st.Height >= Touch.Target && st.ValueBox.Height >= Touch.Target, "stepper: value box and control are at least 44 px tall");

                ClickButton(st.IncreaseButton);
                Assert(st.Value == 4 && events == 1, "stepper: '+' (keyboard click) adds one increment, one event");
                ClickButton(st.DecreaseButton);
                ClickButton(st.DecreaseButton);
                Assert(st.Value == 2, "stepper: '-' subtracts");
                for (int i = 0; i < 10; i++) st.Step(-1);
                Assert(st.Value == 1, "stepper: stops at the minimum");
                for (int i = 0; i < 10; i++) st.Step(+1);
                Assert(st.Value == 5, "stepper: stops at the maximum");
                events = 0;
                st.Step(+1);
                Assert(events == 0, "stepper: no event when the value does not change");

                st.Value = 99;
                Assert(st.Value == 5, "stepper: an out-of-range value is clamped, not thrown");
                st.Maximum = 3;
                Assert(st.Value == 3, "stepper: lowering Maximum clamps the value");
                st.Maximum = 10;

                // Typing
                st.Value = 2; events = 0;
                st.ValueBox.Text = "4";
                Assert(st.Value == 4 && events == 1, "stepper: a valid typed number applies immediately");
                st.ValueBox.Text = "abc";
                Assert(st.Value == 4, "stepper: text that is not a number is ignored");
                st.ValueBox.Text = "50";
                Assert(st.Value == 4, "stepper: a typed number outside the range is ignored");
                Invoke<object>(st.ValueBox, "OnLeave", EventArgs.Empty);
                Assert(st.ValueBox.Text == "4", "stepper: leaving the box restores the last valid value");

                // One mouse press / release / click must step exactly once (Click follows MouseUp)
                st.Value = 2; events = 0;
                RaiseMouse(st.IncreaseButton, "OnMouseDown");
                RaiseMouse(st.IncreaseButton, "OnMouseUp");
                Invoke<object>(st.IncreaseButton, "OnClick", EventArgs.Empty);
                Assert(st.Value == 3, $"stepper: a mouse click steps exactly once ({st.Value})");
                ClickButton(st.IncreaseButton);
                Assert(st.Value == 4, "stepper: the next keyboard click still works after a mouse click");

                // Accessibility
                Assert(st.ValueBox.AccessibleName == "Key width", "stepper: the value box is named after the field");
                Assert(st.DecreaseButton.AccessibleName.Contains("Key width") && st.IncreaseButton.AccessibleName.Contains("Key width"),
                    "stepper: the buttons say which value they change");
                Assert(!st.TabStop && st.ValueBox.TabStop && !st.DecreaseButton.TabStop && !st.IncreaseButton.TabStop,
                    "stepper: one Tab stop (the value box)");
                st.Enabled = false;
                Assert(!st.ValueBox.Enabled && !st.IncreaseButton.Enabled, "stepper: disabling it disables its parts");
            }

            Section("Touch controls — SectionBar");
            {
                using var host = new Form();
                string b = "Appearance";
                var bar = new SectionBar();
                host.Controls.Add(bar);
                _ = host.Handle;
                bar.Add(() => "Key"); bar.Add(() => "Shift"); bar.Add(() => b);
                int changes = 0;
                bar.SelectedIndexChanged += (s, e) => changes++;

                Assert(bar.Count == 3 && bar.SelectedIndex == 0, "section bar: first section selected");
                Assert(bar.Tabs.All(t => t.Height >= Touch.Target && t.Width >= Touch.Target), "section bar: every button is at least 44 x 44");
                Assert(bar.Tabs[0].Style == FluentButton.Variant.Primary && bar.Tabs[1].Style == FluentButton.Variant.Neutral,
                    "section bar: the selected button looks selected");
                Assert(bar.Tabs[0].TabStop && !bar.Tabs[1].TabStop && !bar.Tabs[2].TabStop, "section bar: one Tab stop for the whole bar");

                bar.Select(2, focus: false);
                Assert(bar.SelectedIndex == 2 && changes == 1, "section bar: Select changes the section and raises one event");
                bar.Select(2, focus: false);
                Assert(changes == 1, "section bar: selecting the current section raises no event");
                Assert(bar.Tabs[2].TabStop && !bar.Tabs[0].TabStop, "section bar: the Tab stop follows the selection");

                Invoke<object>(bar.Tabs[2], "OnKeyDown", new KeyEventArgs(Keys.Right));
                Assert(bar.SelectedIndex == 0, "section bar: Right arrow moves on and wraps around");
                Invoke<object>(bar.Tabs[0], "OnKeyDown", new KeyEventArgs(Keys.Left));
                Assert(bar.SelectedIndex == 2, "section bar: Left arrow wraps to the last section");
                Invoke<object>(bar.Tabs[2], "OnKeyDown", new KeyEventArgs(Keys.Home));
                Assert(bar.SelectedIndex == 0, "section bar: Home selects the first section");
                Invoke<object>(bar.Tabs[0], "OnKeyDown", new KeyEventArgs(Keys.End));
                Assert(bar.SelectedIndex == 2, "section bar: End selects the last section");

                bar.SetError(1, true);
                Assert(bar.HasError(1) && bar.Tabs[1].Text.Contains("⚠"), "section bar: an error shows a warning marker on the button");
                Assert(bar.Tabs[1].AccessibleName.Contains(Lang.T("contains an error")), "section bar: the error is also in the accessible name");
                bar.SetError(1, false);
                Assert(!bar.Tabs[1].Text.Contains("⚠"), "section bar: the marker clears");

                b = "Look";
                bar.RefreshTitles();
                Assert(bar.Tabs[2].Text == "Look", "section bar: RefreshTitles re-reads the titles (language change)");
            }

            Section("Pseudo-localisation hook");
            {
                Lang.Load("en");
                string plain = Lang.T("Cancel");
                Lang.PseudoExpansion = 0.4;
                try
                {
                    string longer = Lang.T("Cancel");
                    Assert(longer.StartsWith(plain) && longer.Length > plain.Length, "pseudo: the text is kept and filler is appended (mnemonics survive)");
                    Assert(longer.Length >= (int)Math.Ceiling(plain.Length * 1.4), "pseudo: at least the requested expansion");
                }
                finally { Lang.PseudoExpansion = 0; }
                Assert(Lang.T("Cancel") == plain, "pseudo: switching it off restores the normal text");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // A whole content-sized dialog built from those parts
        // ════════════════════════════════════════════════════════════════
        private static void T_TouchDialogFrame()
        {
            Section("Touch dialog frame — sections, sizing, validation, languages");

            var cases = new (string Name, string Code, double Pseudo)[]
                { ("English", "en", 0), ("Dutch", "nl", 0), ("+40 % text", "en", 0.4), ("+80 % text", "en", 0.8) };
            try
            {
                foreach (var (name, code, pseudo) in cases)
                {
                    Lang.Load(code);
                    Lang.PseudoExpansion = pseudo;
                    using var d = new TouchSpikeForm();
                    DevGallery.Show(d);

                    // Sized from its content, not a constant, and fits a 1366 x 768 laptop.
                    int need = d.Measure(out var pref);
                    int nonClient = d.Height - d.ClientSize.Height;
                    Assert(need + nonClient <= 728, $"{name}: window needs {need + nonClient}px, fits a 1366x768 screen (728px usable)");
                    var wa = Screen.FromControl(d).WorkingArea;
                    Assert(d.ClientSize.Height == Math.Min(need, wa.Height - 10 - nonClient),
                        $"{name}: opened at the height its content needs ({d.ClientSize.Height} vs {need})");

                    // Every section, strictly: targets, clipping, overflow.
                    for (int i = 0; i < d.SectionButtons.Count; i++)
                    {
                        d.SelectSection(i);
                        d.SectionButtons.Select(i, focus: false);
                        Application.DoEvents();
                        d.PerformLayout();
                        var t = UiGuard.TargetViolations(d, visibleOnly: true);
                        var c = UiGuard.ClippedText(d, visibleOnly: true);
                        var o = UiGuard.Overflow(d, visibleOnly: true);
                        Assert(t.Count == 0, $"{name}, section {i + 1}: all controls >= 44x44 {(t.Count > 0 ? "— " + t[0] : "")}");
                        Assert(c.Count == 0, $"{name}, section {i + 1}: no clipped text {(c.Count > 0 ? "— " + c[0] : "")}");
                        Assert(o.Count == 0, $"{name}, section {i + 1}: nothing sticks out {(o.Count > 0 ? "— " + o[0] : "")}");
                    }
                }
            }
            finally { Lang.PseudoExpansion = 0; Lang.Load("en"); }

            // ── Validation on a hidden section ──
            {
                Lang.Load("en");
                using var d = new TouchSpikeForm();
                DevGallery.Show(d);
                d.SelectSection(0);
                d.SectionButtons.Select(0, focus: false);
                Assert(!d.CheckSections(), "validation: no error, nothing to show");

                ((TextBox)d.KeySwatch.Tag).Text = "zzz";           // invalid hex on the Appearance section
                Assert(d.SectionButtons.SelectedIndex == 0, "validation: typing the error does not switch section by itself");
                Assert(d.CheckSections(), "validation: an error is found");
                Assert(d.SectionButtons.SelectedIndex == 2, "validation: jumps to the section that holds the error");
                Assert(d.SectionButtons.HasError(2) && !d.SectionButtons.HasError(0), "validation: only that section is marked");
                Assert(d.SectionButtons.Tabs[2].Text.Contains("⚠"), "validation: the marked section shows the warning sign");

                ((TextBox)d.KeySwatch.Tag).Text = "#FF0000";
                Assert(!d.CheckSections() && !d.SectionButtons.HasError(2), "validation: fixing the value clears the marker");
            }

            // ── Ctrl+Tab and language change ──
            {
                Lang.Load("en");
                using var d = new TouchSpikeForm();
                DevGallery.Show(d);
                var pm = typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
                Message msg = default;
                object[] a = { msg, Keys.Control | Keys.Tab };
                Assert((bool)pm.Invoke(d, a) && d.SectionButtons.SelectedIndex == 1, "Ctrl+Tab: next section");
                a = new object[] { msg, Keys.Control | Keys.Shift | Keys.Tab };
                Assert((bool)pm.Invoke(d, a) && d.SectionButtons.SelectedIndex == 0, "Ctrl+Shift+Tab: previous section");
                a = new object[] { msg, Keys.Control | Keys.Shift | Keys.Tab };
                pm.Invoke(d, a);
                Assert(d.SectionButtons.SelectedIndex == 2, "Ctrl+Shift+Tab: wraps to the last section");

                string before = d.SectionButtons.Tabs[2].Text;
                Lang.Load("nl");
                Application.DoEvents();
                Assert(d.SectionButtons.Tabs[2].Text == Lang.T("Appearance"), "language change: section titles follow the language");
                Assert(UiGuard.All(d).OfType<FluentButton>().Any(b => b.Text == Lang.T("Cancel")),
                    "language change: buttons follow the language");
                Lang.Load("en");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Strict UI guards for a migrated dialog: every section, in English, Dutch and two "long language"
        // stress cases, in the light and the dark theme.
        // ════════════════════════════════════════════════════════════════
        private static void CheckDialogGuards(string dialog, Func<FluentDialogBase> make)
        {
            var cases = new (string Name, string Code, double Pseudo)[]
                { ("en", "en", 0), ("nl", "nl", 0), ("+40%", "en", 0.4), ("+80%", "en", 0.8) };
            bool wasLight = ToolbarButton.IsLightTheme;
            try
            {
                foreach (bool light in new[] { true, false })
                {
                    ToolbarButton.IsLightTheme = light;          // read when a dialog is created
                    foreach (var (name, code, pseudo) in cases)
                    {
                        Lang.Load(code);
                        Lang.PseudoExpansion = pseudo;
                        string tag = $"{dialog} [{name}, {(light ? "light" : "dark")}]";
                        using var d = make();
                        DevGallery.Show(d);

                        int nonClient = d.Height - d.ClientSize.Height;
                        int need = d.MeasureContent().Height + nonClient;
                        Assert(need <= 728, $"{tag}: needs {need}px, fits a 1366x768 screen (728px usable)");

                        var bar = d.SectionBarAccess;
                        int sections = bar?.Count ?? Math.Max(1, d.HostSectionCount);
                        for (int i = 0; i < sections; i++)
                        {
                            d.ShowSectionForGuard(i);
                            Application.DoEvents();
                            d.PerformLayout();
                            var t = UiGuard.TargetViolations(d, visibleOnly: true);
                            var c = UiGuard.ClippedText(d, visibleOnly: true);
                            var o = UiGuard.Overflow(d, visibleOnly: true);
                            var a = UiGuard.StackedEdges(d, visibleOnly: true);
                            Assert(a.Count == 0, $"{tag}, section {i + 1}: stacked controls share one width {(a.Count > 0 ? "— " + a[0] : "")}");
                            Assert(t.Count == 0, $"{tag}, section {i + 1}: all controls >= 44x44 {(t.Count > 0 ? "— " + t[0] : "")}");
                            Assert(c.Count == 0, $"{tag}, section {i + 1}: no clipped text {(c.Count > 0 ? "— " + c[0] : "")}");
                            Assert(o.Count == 0, $"{tag}, section {i + 1}: nothing sticks out {(o.Count > 0 ? "— " + o[0] : "")}");
                        }
                    }
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLight; Lang.PseudoExpansion = 0; Lang.Load("en"); }
        }

        private static void T_KeyEditorGuards()
        {
            Section("Key Editor — strict UI guards (all sections, languages and themes)");
            var groups = new List<KeyGroup> { new KeyGroup { Name = SettingsManager.StandardGroupName }, new KeyGroup { Name = "Klinkers" } };
            // A key that uses every part of the dialog: a shortcut, a Shift layout jump and an AltGr text.
            var props = new KeyProps("Ctrl+c", "^c", "A", "layout:azerty.kbl", "€", "€") { GroupName = "Klinkers" };
            CheckDialogGuards("KeyEditorForm", () => new KeyEditorForm(props, null, groups: groups, layoutDir: AppDomain.CurrentDomain.BaseDirectory));
        }

        // ════════════════════════════════════════════════════════════════
        // Borders: the same on all four sides, one pixel wide, the same colour on every kind of control.
        // Found 2026-09-19: a 1 px pen on whole coordinates covers half a pixel on the left/top and two half
        // pixels on the right/bottom, so some edges looked thin and faint and others wide and blurred.
        // ════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════
        // Rings drawn with whole-coordinate pens (key selection ring, swatch focus ring): the same width on all four sides.
        // Found 2026-10-02: the 2 px white band of the key selection ring sat one pixel nearer the edge on the left and top
        // than on the right and bottom.
        // ════════════════════════════════════════════════════════════════
        private static void T_KeyRings()
        {
            Section("Key selection ring and swatch focus ring — identical on all four sides");

            var bg = Color.FromArgb(128, 128, 128);
            Bitmap Draw(int w, int h, Action<Graphics> paint)
            {
                var bmp = new Bitmap(w, h);
                using var g = Graphics.FromImage(bmp);
                g.Clear(bg);
                paint(g);
                return bmp;
            }
            static bool Same(Color a, Color b) => a.ToArgb() == b.ToArgb();

            // Layer i (0 = the pixel on the edge): the pixel at distance i from each of the four edges, and from each of the four corners.
            void Symmetric(string what, Bitmap b, int layers)
            {
                for (int i = 0; i < layers; i++)
                {
                    var l = b.GetPixel(i, b.Height / 2);
                    var r = b.GetPixel(b.Width - 1 - i, b.Height / 2);
                    var t = b.GetPixel(b.Width / 2, i);
                    var d = b.GetPixel(b.Width / 2, b.Height - 1 - i);
                    Assert(Same(l, r) && Same(l, t) && Same(l, d), $"{what}: pixel {i} from the edge is the same on all four sides (left {l}, right {r}, top {t}, bottom {d})");
                    var c1 = b.GetPixel(i, i); var c2 = b.GetPixel(b.Width - 1 - i, i);
                    var c3 = b.GetPixel(i, b.Height - 1 - i); var c4 = b.GetPixel(b.Width - 1 - i, b.Height - 1 - i);
                    Assert(Same(c1, c2) && Same(c1, c3) && Same(c1, c4), $"{what}: pixel {i} of the four corners is the same");
                }
            }

            foreach (var (w, h) in new[] { (60, 40), (61, 41), (27, 18) })
            {
                using var sel = Draw(w, h, g => KeyboardForm.DrawSelectionRing(g, w, h));
                Symmetric($"selection ring {w}x{h}", sel, 5);
                var white = Color.FromArgb(255, 255, 255);
                Assert(Same(sel.GetPixel(0, h / 2), white) && Same(sel.GetPixel(1, h / 2), white) &&
                       !Same(sel.GetPixel(2, h / 2), bg) && !Same(sel.GetPixel(3, h / 2), bg) && !Same(sel.GetPixel(2, h / 2), white) &&
                       Same(sel.GetPixel(4, h / 2), bg),
                    $"selection ring {w}x{h}: 2 px white band on the edge, 2 px dark band inside it, then nothing");

                using var foc = Draw(w, h, g => ColorSwatchButton.DrawFocusRing(g, w, h));
                Symmetric($"swatch focus ring {w}x{h}", foc, 6);
                Assert(Same(foc.GetPixel(1, h / 2), bg) && Same(foc.GetPixel(2, h / 2), Color.FromArgb(255, 255, 255)) &&
                       Same(foc.GetPixel(3, h / 2), Color.FromArgb(255, 255, 255)) && !Same(foc.GetPixel(4, h / 2), bg) && Same(foc.GetPixel(5, h / 2), bg),
                    $"swatch focus ring {w}x{h}: 2 px white band, then a dark line, nothing around them");
            }

            // The border of a key itself is the native flat border (FlatAppearance): check it too, at every thickness the app uses.
            foreach (int thick in new[] { 0, 1, 2, 3 })
            {
                using var host = new Form();
                var key = new Button { FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 100, 160), Text = "", UseVisualStyleBackColor = false };
                key.FlatAppearance.BorderColor = Color.Red;
                key.FlatAppearance.BorderSize  = thick;
                host.Controls.Add(key);
                key.SetBounds(0, 0, 61, 41);
                _ = host.Handle;
                using var bmp = new Bitmap(61, 41);
                key.DrawToBitmap(bmp, new Rectangle(0, 0, 61, 41));
                bool IsBorder(Color c) => c.R > 150 && c.G < 90;
                int Width(Func<int, Color> at) { int n = 0; for (int i = 0; i < 8 && IsBorder(at(i)); i++) n++; return n; }
                int left = Width(i => bmp.GetPixel(i, 20)), right = Width(i => bmp.GetPixel(60 - i, 20));
                int top  = Width(i => bmp.GetPixel(30, i)), bottom = Width(i => bmp.GetPixel(30, 40 - i));
                Assert(left == thick && right == thick && top == thick && bottom == thick,
                    $"key border {thick} px: left {left}, right {right}, top {top}, bottom {bottom} px wide");
            }

            // Every layer of the key selection ring must be visible against both a dark and a pale key (the reason for the sandwich).
            foreach (var key in new[] { Color.Black, Color.FromArgb(20, 40, 120), Color.White, Color.FromArgb(240, 240, 200) })
            {
                using var bmp = new Bitmap(40, 30);
                using (var g = Graphics.FromImage(bmp)) { g.Clear(key); KeyboardForm.DrawSelectionRing(g, 40, 30); }
                double Ratio(Color a, Color b)
                {
                    double L(Color c) { double F(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); } return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B); }
                    double x = L(a), y = L(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
                }
                double best = new[] { 0, 2 }.Max(i => Ratio(bmp.GetPixel(i, 15), key));
                Assert(best >= 3.0, $"selection ring on key {key.Name}/{key}: the ring contrasts at least 3:1 with the key ({best:0.0}:1)");
            }
        }

        private static void T_ControlBorders()
        {
            Section("Control borders — identical on all four sides, 1 px, one colour for every control");

            static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2;

            // The middle of each side (left, right, top, bottom) — away from the rounded corners.
            static Color[] Sides(Bitmap b) => new[]
            {
                b.GetPixel(0, b.Height / 2), b.GetPixel(b.Width - 1, b.Height / 2),
                b.GetPixel(b.Width / 2, 0),  b.GetPixel(b.Width / 2, b.Height - 1),
            };

            void CheckSides(string what, Bitmap b, Color expected)
            {
                var s = Sides(b);
                string[] names = { "left", "right", "top", "bottom" };
                AssertAll(Enumerable.Range(0, 4), i => Near(s[i], expected),
                    i => $"{names[i]} side is {s[i]} instead of {expected}", $"{what}: all four sides have the border colour");
            }

            Bitmap Render(Control c, int w, int h)
            {
                using var host = new Form();
                host.Controls.Add(c);
                c.AutoSize = false;                    // keep the size we ask for (chips and check boxes size to their text)
                c.SetBounds(0, 0, w, h);
                _ = host.Handle;
                var bmp = new Bitmap(w, h);
                c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                return bmp;
            }

            bool wasLight = ToolbarButton.IsLightTheme;
            try
            {
                ToolbarButton.IsLightTheme = true;

                // ── Neutral button, from the real painter ──
                using (var bmp = new Bitmap(120, 44))
                {
                    using (var g = Graphics.FromImage(bmp))
                        FluentPainter.PaintLight(g, new Rectangle(0, 0, 120, 44), "OK", "", Fluent.FontBtnLg,
                            FluentButton.Variant.Neutral, false, false, true, Fluent.RadiusBtn, Fluent.BgPage);
                    CheckSides("neutral button", bmp, Fluent.ControlBorder);
                    Assert(Near(bmp.GetPixel(1, 22), Fluent.Neutral) && Near(bmp.GetPixel(118, 22), Fluent.Neutral),
                        "neutral button: the border is one pixel wide (the pixel inside it is the fill)");
                }
                using (var bmp = new Bitmap(120, 44))
                {
                    using (var g = Graphics.FromImage(bmp))
                        FluentPainter.PaintLight(g, new Rectangle(0, 0, 120, 44), "OK", "", Fluent.FontBtnLg,
                            FluentButton.Variant.Neutral, true, false, true, Fluent.RadiusBtn, Fluent.BgPage);
                    CheckSides("neutral button (hover)", bmp, Fluent.ControlBorderHover);
                }

                // ── Coloured button on a dark parent: it gets the light outline, equally on every side ──
                using (var bmp = new Bitmap(120, 44))
                {
                    using (var g = Graphics.FromImage(bmp))
                        FluentPainter.PaintLight(g, new Rectangle(0, 0, 120, 44), "OK", "", Fluent.FontBtnLg,
                            FluentButton.Variant.Primary, false, false, true, Fluent.RadiusBtn, Fluent.DialogDarkCard);
                    CheckSides("primary button on a dark parent", bmp, Fluent.DialogDarkBorder);
                }

                // ── The other controls draw the same border (light theme) ──
                using (var bmp = Render(new ColorChip("Key", Color.White), 116, 44))
                    CheckSides("colour chip", bmp, Fluent.ControlBorder);
                using (var bmp = Render(new TouchTextBox(), 200, 44))
                    CheckSides("text box", bmp, Fluent.ControlBorder);
                using (var bmp = Render(new TouchCheckBox { Text = "Auto" }, 120, 44))
                    CheckSides("check box", bmp, Fluent.ControlBorder);

                // ── Dark theme: chips and text boxes use the dark border colour ──
                ToolbarButton.IsLightTheme = false;
                using (var bmp = Render(new ColorChip("Key", Color.Black), 116, 44))
                    CheckSides("colour chip (dark theme)", bmp, Fluent.DialogDarkBorder);
                using (var bmp = Render(new TouchTextBox(), 200, 44))
                    CheckSides("text box (dark theme)", bmp, Fluent.DialogDarkBorder);
            }
            finally { ToolbarButton.IsLightTheme = wasLight; }

            // ── The focus ring follows the rounded corners (a square ring over a rounded button looked wrong) ──
            using (var bmp = new Bitmap(120, 44))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    FluentPainter.DrawRoundedRing(g, 120, 44, Fluent.RadiusBtn, 3f, 2f, Color.White);
                }
                Assert(bmp.GetPixel(60, 3).R > 200 && bmp.GetPixel(3, 22).R > 200 && bmp.GetPixel(116, 22).R > 200 && bmp.GetPixel(60, 40).R > 200,
                    "focus ring: all four sides are drawn");
                Assert(bmp.GetPixel(2, 2).R < 80 && bmp.GetPixel(117, 2).R < 80 && bmp.GetPixel(2, 41).R < 80 && bmp.GetPixel(117, 41).R < 80,
                    "focus ring: the four corners are rounded, not square");
            }

            // ── The check box is a real 44 px target with a large tick box ──
            using (var host = new Form())
            {
                var ck = new TouchCheckBox { Text = "Auto" };
                host.Controls.Add(ck);
                _ = host.Handle;
                Assert(ck.GetPreferredSize(Size.Empty).Height >= Touch.Target, "check box: the whole row is at least 44 px tall");
                Assert(ck.GetPreferredSize(Size.Empty).Width >= Touch.Target * 2, "check box: wide enough for the tick box and its text");
                ck.Checked = true;
                Assert(ck.Checked, "check box: still toggles");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Disabled controls must LOOK disabled on both themes. Found 2026-10-02: a translucent white wash over a light
        // button leaves it light, so on the dark theme a disabled dropdown looked as bright as an enabled one.
        // ════════════════════════════════════════════════════════════════
        private static void T_DisabledLook()
        {
            Section("Disabled controls look disabled (flat grey palette) on both themes");

            static double Bright(Color c) => c.GetBrightness();
            static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 3 && Math.Abs(a.G - b.G) <= 3 && Math.Abs(a.B - b.B) <= 3;

            Bitmap Paint(bool enabled, Color parent)
            {
                var bmp = new Bitmap(120, 44);
                using var g = Graphics.FromImage(bmp);
                FluentPainter.PaintLight(g, new Rectangle(0, 0, 120, 44), "OK", "", Fluent.FontBtnLg,
                    FluentButton.Variant.Neutral, false, false, enabled, Fluent.RadiusBtn, parent);
                return bmp;
            }

            foreach (bool dark in new[] { false, true })
            {
                string theme = dark ? "dark" : "light";
                Color parent = dark ? Fluent.DialogDarkCard : Fluent.BgPage;
                var palette = FluentPainter.DisabledPalette(dark);
                using var on  = Paint(true,  parent);
                using var off = Paint(false, parent);
                Assert(Near(off.GetPixel(8, 22), palette.Fill), $"disabled button ({theme}): flat disabled fill {off.GetPixel(8, 22)}");
                Assert(Near(off.GetPixel(0, 22), palette.Border), $"disabled button ({theme}): disabled border {off.GetPixel(0, 22)}");
                Assert(!Near(on.GetPixel(8, 22), off.GetPixel(8, 22)), $"disabled button ({theme}): clearly different from an enabled one");
            }
            // On the dark theme a disabled button must be dark, not bright.
            using (var offDark = Paint(false, Fluent.DialogDarkCard))
                Assert(Bright(offDark.GetPixel(8, 22)) < 0.3f, "disabled button (dark): the fill is dark, not bright");

            // A disabled text box that still shows text (e.g. "Slot 1 (automatic)") draws it in the disabled-text grey of the
            // palette, the same grey as a disabled dropdown — not in the fainter colour of the edit control itself.
            bool wasLightTheme = ToolbarButton.IsLightTheme;
            try
            {
                foreach (bool dark in new[] { false, true })
                {
                    ToolbarButton.IsLightTheme = !dark;
                    var palette = FluentPainter.DisabledPalette(dark);
                    using var host = new Form { BackColor = dark ? Fluent.DialogDarkCard : Fluent.BgPage };
                    var tb = new TouchTextBox { Text = "Slot 1 (automatic)", Enabled = false };
                    host.Controls.Add(tb);
                    tb.SetBounds(0, 0, 220, 44);
                    _ = host.Handle;
                    using var bmp = new Bitmap(220, 44);
                    tb.DrawToBitmap(bmp, new Rectangle(0, 0, 220, 44));
                    int exact = 0;
                    for (int y = 0; y < 44; y++)
                        for (int x = 4; x < 216; x++)
                            if (Near(bmp.GetPixel(x, y), palette.Text)) exact++;
                    Assert(exact >= 20, $"disabled text box ({(dark ? "dark" : "light")}): its text is drawn in the disabled-text grey ({exact} pixels)");
                    Assert(Near(bmp.GetPixel(210, 40), palette.Fill), $"disabled text box ({(dark ? "dark" : "light")}): flat disabled fill");
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLightTheme; }

            // The dropdown, the check box and the text box follow.
            bool wasLight = ToolbarButton.IsLightTheme;
            try
            {
                foreach (bool dark in new[] { false, true })
                {
                    ToolbarButton.IsLightTheme = !dark;
                    string theme = dark ? "dark" : "light";
                    var palette = FluentPainter.DisabledPalette(dark);
                    Color parent = dark ? Fluent.DialogDarkCard : Fluent.BgPage;

                    using (var host = new Form { BackColor = parent })
                    {
                        var choice = new TouchChoiceButton { Enabled = false };
                        choice.Items.Add(new TouchChoice { Text = "Text" });
                        choice.SelectSilently(0);
                        var check = new TouchCheckBox { Text = "Auto", Enabled = false, AutoSize = false };
                        host.Controls.Add(choice); host.Controls.Add(check);
                        choice.AutoSize = false;
                        choice.SetBounds(0, 0, 150, 44); check.SetBounds(0, 60, 150, 44);
                        _ = host.Handle;
                        using var b1 = new Bitmap(150, 44); choice.DrawToBitmap(b1, new Rectangle(0, 0, 150, 44));
                        using var b2 = new Bitmap(150, 44); check.DrawToBitmap(b2, new Rectangle(0, 0, 150, 44));
                        Assert(Near(b1.GetPixel(75, 40), palette.Fill), $"disabled dropdown ({theme}): flat disabled fill {b1.GetPixel(75, 40)}");
                        Assert(Near(b2.GetPixel(140, 22), palette.Fill), $"disabled check box ({theme}): flat disabled fill {b2.GetPixel(140, 22)}");
                        if (dark)
                        {
                            Assert(Bright(b1.GetPixel(75, 40)) < 0.3f, "disabled dropdown (dark): dark, not bright");
                            Assert(Bright(b2.GetPixel(140, 22)) < 0.3f, "disabled check box (dark): dark, not bright");
                        }
                    }
                }
            }
            finally { ToolbarButton.IsLightTheme = wasLight; }
        }

        // ════════════════════════════════════════════════════════════════
        // Key Editor behaviour: every action type survives load + Apply
        // ════════════════════════════════════════════════════════════════
        private static void T_KeyEditorRoundTrip()
        {
            Section("Key Editor — every action type survives load and Apply; layers behave");

            var applyMi = typeof(KeyEditorForm).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance);
            T Field<T>(object f, string name) => (T)typeof(KeyEditorForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
            // Types are chosen by name: the row of a type differs between the Normal layer and Shift / AltGr.
            const string Text = "Text", Key = "Key/Shortcut", Modifier = "Modifier", WordPrediction = "Word prediction", Layout = "Layout";
            void Pick(TouchChoiceButton b, string type) => b.SelectedIndex = b.Items.FindIndex(i => i.Text == type);
            string Current(TouchChoiceButton b) => b.SelectedItem?.Text;

            KeyProps Apply(KeyProps p, Action<KeyEditorForm> edit = null, HashSet<int> usedWp = null, List<KeyGroup> groups = null)
            {
                using var f = new KeyEditorForm(p, null, usedWpSlots: usedWp, groups: groups, layoutDir: AppDomain.CurrentDomain.BaseDirectory);
                edit?.Invoke(f);
                applyMi.Invoke(f, null);
                return f.DialogResult == DialogResult.OK ? f.Result : null;
            }

            // ── Normal layer: each type comes back exactly as it went in ──
            var r = Apply(new KeyProps("a", "a"));
            Assert(r != null && r.Label == "a" && r.Send == "a", "text: label and send unchanged");
            r = Apply(new KeyProps("(", "{(}"));
            Assert(r.Send == "{(}", "special character: an already-escaped send is unchanged");
            r = Apply(new KeyProps("Ctrl+c", "^c"));
            Assert(r.Send == "^c", "key sequence: '^c' unchanged (shown as {Ctrl}c, stored as ^c)");
            r = Apply(new KeyProps("Alt+F4", "%{F4}"));
            Assert(r.Send == "%{F4}", "key sequence: '%{F4}' unchanged");
            r = Apply(new KeyProps("Win+D", "win:d"));
            Assert(r.Send == "win:d", "key sequence: 'win:d' unchanged");
            r = Apply(new KeyProps("Shift", ""));
            Assert(r.Label == "Shift" && r.Send == "", "modifier: label kept, send stays empty");
            r = Apply(new KeyProps("w", "wp:3"));
            Assert(r.Send == "wp:3", "word prediction: slot 3 unchanged");
            r = Apply(new KeyProps("Nl", "layout:azerty.kbl"));
            Assert(r.Send == "layout:azerty.kbl", "layout jump: 'layout:azerty.kbl' unchanged");

            // ── Shift / AltGr: an untouched layer is kept byte for byte, even where the readable form is lossy ──
            r = Apply(new KeyProps("a", "a", "A", "+(ab)", "€", "layout:azerty.kbl"));
            Assert(r.ShiftSend == "+(ab)", "Shift layer untouched: a grouped send is not rewritten");
            Assert(r.AltGrSend == "layout:azerty.kbl", "AltGr layer untouched: layout jump kept");
            Assert(r.ShiftLabel == "A" && r.AltGrLabel == "€", "Shift / AltGr labels kept");

            // ── Editing the layers ──
            r = Apply(new KeyProps("a", "a"), f => { var t = Field<TouchChoiceButton[]>(f, "_types"); var v = Field<TouchTextBox[]>(f, "_values");
                Pick(t[1], Key);    v[1].Text = "{Ctrl}v"; });
            Assert(r.ShiftSend == "^v", "Shift layer: a typed shortcut is stored in SendKeys syntax");
            r = Apply(new KeyProps("a", "a"), f => { var t = Field<TouchChoiceButton[]>(f, "_types"); var v = Field<TouchTextBox[]>(f, "_values");
                Pick(t[2], Layout); v[2].Text = "azerty.kbl"; });
            Assert(r.AltGrSend == "layout:azerty.kbl", "AltGr layer: a chosen layout gets its 'layout:' prefix");
            r = Apply(new KeyProps("a", "a"), f => { var t = Field<TouchChoiceButton[]>(f, "_types"); var v = Field<TouchTextBox[]>(f, "_values");
                Pick(t[1], Text);   v[1].Text = "B"; });
            Assert(r.ShiftSend == "B", "Shift layer: plain text is stored as typed");

            // ── Word prediction exists on the Normal layer only; Modifier is listed on Shift / AltGr but unavailable ──
            using (var f = new KeyEditorForm(new KeyProps("a", "a"), null))
            {
                var t = Field<TouchChoiceButton[]>(f, "_types");
                Assert(t[0].Items.Any(i => i.Text == WordPrediction), "Normal layer: Word prediction is an option");
                Assert(!t[1].Items.Any(i => i.Text == WordPrediction) && !t[2].Items.Any(i => i.Text == WordPrediction),
                    "Shift and AltGr: Word prediction is not an option at all");
                Pick(t[1], Modifier);
                Pick(t[2], Modifier);
                Assert(Current(t[1]) == Text && Current(t[2]) == Text, "Modifier cannot be chosen on Shift or AltGr");
                var mod = t[1].Items.First(i => i.Text == Modifier);
                Assert(!mod.Enabled && !string.IsNullOrEmpty(mod.DisabledReason), "the unavailable Modifier row says why");
                Assert(t[0].Items.All(i => i.Enabled), "on the Normal layer all five types are available");
                Assert(t[0].Items.Count == 5 && t[1].Items.Count == 4 && t[2].Items.Count == 4, "five types on Normal, four on Shift / AltGr");
            }

            // ── A word prediction key has no Shift / AltGr action: those rows are empty and disabled ──
            using (var f = new KeyEditorForm(new KeyProps("w", "wp:1", "W", "x", "€", "layout:azerty.kbl"), null, layoutDir: AppDomain.CurrentDomain.BaseDirectory))
            {
                var labels = Field<TouchTextBox[]>(f, "_labels");
                var t = Field<TouchChoiceButton[]>(f, "_types");
                var v = Field<TouchTextBox[]>(f, "_values");
                var pk = Field<FluentButton[]>(f, "_pickers");
                bool Off(int i) => !labels[i].Enabled && !t[i].Enabled && !v[i].Enabled && !pk[i].Enabled
                                   && labels[i].Text == "" && v[i].Text == "" && Current(t[i]) == Text;
                Assert(Off(1) && Off(2), "word prediction key: Shift and AltGr are empty and disabled");
                Assert(!labels[0].Enabled && labels[0].Text == "",
                    "word prediction key: the Normal label is not editable (it is a placeholder that the predicted word replaces)");
                Assert(t[0].Enabled, "word prediction key: the type can still be changed");

                // Choosing another type brings back what the layers held; choosing word prediction again empties them.
                Pick(t[0], Text);
                Assert(labels[1].Enabled && t[1].Enabled && v[1].Enabled && labels[2].Enabled && t[2].Enabled && v[2].Enabled,
                    "leaving word prediction enables Shift and AltGr again");
                Assert(labels[1].Text == "W" && v[1].Text == "x" && labels[2].Text == "€" && v[2].Text == "azerty.kbl" && Current(t[2]) == Layout,
                    "leaving word prediction brings back the Shift and AltGr label, type and value");
                Assert(labels[0].Enabled && labels[0].Text == "w", "leaving word prediction brings back the Normal label and makes it editable");
                Pick(t[0], WordPrediction);
                Assert(Off(1) && Off(2), "choosing word prediction again empties and disables them again");
            }
            r = Apply(new KeyProps("w", "wp:1", "W", "x", "€", "layout:azerty.kbl"));
            Assert(r.Send == "wp:1" && r.ShiftLabel == "" && r.ShiftSend == "" && r.AltGrLabel == "" && r.AltGrSend == "",
                "word prediction key: Apply stores no Shift / AltGr label or action");
            Assert(r.Label == "w", $"word prediction key: an existing placeholder label is kept ('{r.Label}')");
            r = Apply(new KeyProps("w", "wp:4"));
            Assert(r.Label == "w", "word prediction key: the label is kept whatever the slot");
            r = Apply(new KeyProps("a", "a", "A", "B", "€", "E"), f => Pick(Field<TouchChoiceButton[]>(f, "_types")[0], WordPrediction), usedWp: new HashSet<int>());
            Assert(r.Send == "wp:0" && r.ShiftLabel == "" && r.ShiftSend == "" && r.AltGrLabel == "" && r.AltGrSend == "",
                "choosing word prediction on a key with Shift / AltGr values clears them");
            Assert(r.Label == "word 1", $"a key that becomes a word prediction gets the placeholder label for its slot ('{r.Label}')");
            r = Apply(new KeyProps("a", "a", "A", "B", "€", "E"), f => { var t = Field<TouchChoiceButton[]>(f, "_types");
                Pick(t[0], WordPrediction); Pick(t[0], Text); });
            Assert(r.Label == "a" && r.Send == "a" && r.ShiftLabel == "A" && r.ShiftSend == "B" && r.AltGrLabel == "€" && r.AltGrSend == "E",
                $"word prediction and back: label and Shift / AltGr values are untouched (label '{r.Label}', send '{r.Send}', shift '{r.ShiftLabel}'/'{r.ShiftSend}', altgr '{r.AltGrLabel}'/'{r.AltGrSend}')");
            // Regression (found 2026-10-02): a loaded word prediction key, switched to Text, kept the explanation
            // "Prediction slot 1 (assigned automatically)" in its value box, so the key would have typed that text.
            r = Apply(new KeyProps("w", "wp:1"), f => Pick(Field<TouchChoiceButton[]>(f, "_types")[0], Text));
            Assert(r.Send == "w", $"a loaded word prediction key switched to Text does not keep the explanation as its text (send '{r.Send}')");
            // An unresolvable layout path on a layer that word prediction emptied must not block Apply.
            r = Apply(new KeyProps("w", "wp:1", "", "", "", "layout:does_not_exist.kbl"));
            Assert(r != null && r.AltGrSend == "", "word prediction key: a stale layout path on AltGr does not block Apply");

            // ── Choosing a type resets the value for it; word prediction takes the first free slot ──
            r = Apply(new KeyProps("a", "a"), f => Pick(Field<TouchChoiceButton[]>(f, "_types")[0], WordPrediction), usedWp: new HashSet<int> { 0, 1 });
            Assert(r.Send == "wp:2", $"word prediction: the first free slot is assigned ({r.Send})");
            r = Apply(new KeyProps("a", "a"), f => Pick(Field<TouchChoiceButton[]>(f, "_types")[0], Modifier));
            Assert(r.Label == "Shift" && r.Send == "", $"modifier: the first modifier is chosen and becomes the label ({r.Label})");

            // ── Leaving Key/Shortcut, Layout or Modifier for Text must not carry the value over as text (all three layers) ──
            // (A Normal key with an empty Text value types its label, so a cleared value shows up as send == label.)
            r = Apply(new KeyProps("Nl", "layout:azerty.kbl"), f => Pick(Field<TouchChoiceButton[]>(f, "_types")[0], Text));
            Assert(r.Send == "Nl", $"Layout -> Text on Normal: the file name is not kept as text (send '{r.Send}')");
            r = Apply(new KeyProps("Ctrl+c", "^c"), f => Pick(Field<TouchChoiceButton[]>(f, "_types")[0], Text));
            Assert(r.Send == "Ctrl+c", $"Key/Shortcut -> Text on Normal: the shortcut is not kept as text (send '{r.Send}')");
            r = Apply(new KeyProps("a", "a", "A", "^v", "€", "layout:azerty.kbl"), f => { var t = Field<TouchChoiceButton[]>(f, "_types");
                Pick(t[1], Text); Pick(t[2], Text); });
            Assert(r.ShiftSend == "" && r.AltGrSend == "", $"Key/Layout -> Text on Shift and AltGr: nothing is kept as text (shift '{r.ShiftSend}', altgr '{r.AltGrSend}')");
            r = Apply(new KeyProps("a", "a"), f => { var t = Field<TouchChoiceButton[]>(f, "_types");
                Pick(t[0], Modifier); Pick(t[0], Text); });
            Assert(r.Send == r.Label, $"Modifier -> Text on Normal: no modifier token is kept as text (send '{r.Send}', label '{r.Label}')");
            r = Apply(new KeyProps("a", "a", "A", "B", "€", "E"), f => { var t = Field<TouchChoiceButton[]>(f, "_types"); var v = Field<TouchTextBox[]>(f, "_values");
                Pick(t[1], Key); Pick(t[1], Text); v[1].Text = "typed"; });
            Assert(r.ShiftSend == "typed", "typing text after returning to Text is stored as typed");

            // ── Recording a shortcut: every held modifier is kept (Priority 6) ──
            //                     vk    ctrl   alt    shift  win
            const uint A = 0x41, S = 0x53, D = 0x44, One = 0x31, F4 = 0x73;
            Assert(KeyEditorForm.BuildSendFromHook(A, false, false, false, false) == "a", "recorded: a plain letter");
            Assert(KeyEditorForm.BuildSendFromHook(A, false, false, true,  false) == "A", "recorded: Shift + letter alone is the capital letter");
            Assert(KeyEditorForm.BuildSendFromHook(A, true,  false, false, false) == "^a", "recorded: Ctrl + A");
            Assert(KeyEditorForm.BuildSendFromHook(A, true,  false, true,  false) == "^+a", "recorded: Ctrl + Shift + A keeps both modifiers");
            Assert(KeyEditorForm.BuildSendFromHook(A, false, true,  true,  false) == "%+a", "recorded: Alt + Shift + A keeps both modifiers");
            Assert(KeyEditorForm.BuildSendFromHook(A, true,  true,  true,  false) == "^%+a", "recorded: Ctrl + Alt + Shift + A keeps all three");
            Assert(KeyEditorForm.BuildSendFromHook(F4, false, true, false, false) == "%{F4}", "recorded: Alt + F4");
            Assert(KeyEditorForm.BuildSendFromHook(F4, false, false, true, false) == "+{F4}", "recorded: Shift + F4 (not a printable key: Shift is a modifier)");
            Assert(KeyEditorForm.BuildSendFromHook(One, false, false, true, false) == "+1", "recorded: Shift + 1 keeps Shift");
            Assert(KeyEditorForm.BuildSendFromHook(D, false, false, false, true) == "win:d", "recorded: Win + D");
            Assert(KeyEditorForm.BuildSendFromHook(S, false, false, true,  true) == "win:+s", "recorded: Win + Shift + S keeps Shift");
            Assert(KeyEditorForm.BuildSendFromHook(S, true,  false, true,  true) == "win:^+s", "recorded: Win + Ctrl + Shift + S keeps all");
            Assert(KeyEditorForm.BuildHumanLabel(A, true, false, true, false) == "Ctrl+Shift+A", "label: Ctrl+Shift+A");
            Assert(KeyEditorForm.BuildHumanLabel(A, false, false, true, false) == "A", "label: Shift + letter alone is just A");
            Assert(KeyEditorForm.BuildHumanLabel(S, false, false, true, true) == "Win+Shift+S", "label: Win+Shift+S");
            foreach (var send in new[] { "^+a", "^%+a", "win:+s", "win:^+s", "%+a", "+{F4}", "A" })
                Assert(KeyEditorForm.FromHuman(KeyEditorForm.ToHuman(send)) == send, $"readable form round-trips: '{send}' -> '{KeyEditorForm.ToHuman(send)}'");
            Assert(KeyEditorForm.ToHuman("^+a") == "{Ctrl}{Shift}a", "readable form of Ctrl + Shift + A");
            Assert(KeyEditorForm.ToHuman("win:+s") == "{Win}{Shift}s", "readable form of Win + Shift + S");

            // ── The label a recording fills in: replaced by the next recording, never over a label the user typed ──
            using (var f = new KeyEditorForm(new KeyProps("", ""), null))
            {
                var labels = Field<TouchTextBox[]>(f, "_labels");
                var mi = typeof(KeyEditorForm).GetMethod("ApplyRecordedLabel", BindingFlags.NonPublic | BindingFlags.Instance);
                mi.Invoke(f, new object[] { 0, "Ctrl+C" });
                Assert(labels[0].Text == "Ctrl+C", "recording: an empty label gets the label of the shortcut");
                mi.Invoke(f, new object[] { 0, "Ctrl+V" });
                Assert(labels[0].Text == "Ctrl+V", "recording again: the label of the previous recording is replaced");
                labels[0].Text = "Paste";
                mi.Invoke(f, new object[] { 0, "Ctrl+X" });
                Assert(labels[0].Text == "Paste", "recording: a label the user typed is kept");
                labels[0].Text = "";
                mi.Invoke(f, new object[] { 0, "Ctrl+Z" });
                Assert(labels[0].Text == "Ctrl+Z", "recording: a label the user cleared is filled again");
            }

            // ── The Record / Browse button is an icon-only 44 px square in every language and state, so it never covers its neighbour ──
            foreach (var lang in new[] { "en", "nl" })
            {
                Lang.Load(lang);
                try
                {
                    using var f = new KeyEditorForm(new KeyProps("a", "a"), null);
                    var pk = Field<FluentButton[]>(f, "_pickers");
                    var t = Field<TouchChoiceButton[]>(f, "_types");
                    Pick(t[0], Key);
                    string idle = pk[0].AccessibleName;
                    var recordIcon = pk[0].IconImage;
                    Assert(pk[0].Text == "" && recordIcon != null, $"[{lang}] the Record button shows the record icon and no text");
                    Assert(pk[0].Width == Touch.Target && pk[0].Height == Touch.Target, $"[{lang}] the Record button is 44 x 44 ({pk[0].Width} x {pk[0].Height})");
                    Assert(!string.IsNullOrWhiteSpace(idle), $"[{lang}] the symbol button has an accessible name ('{idle}')");

                    typeof(KeyEditorForm).GetMethod("StartRecording", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, new object[] { 0 });
                    try
                    {
                        Assert(pk[0].IconImage != null && !ReferenceEquals(pk[0].IconImage, recordIcon) && pk[0].Text == "", $"[{lang}] while recording the button shows the stop icon");
                        Assert(pk[0].AccessibleName != idle && pk[0].AccessibleName == Lang.T("Stop recording"), $"[{lang}] while recording the button is named 'Stop recording'");
                        Assert(pk[0].Width == Touch.Target, $"[{lang}] the button does not grow while recording");
                        Assert(Field<Label>(f, "_lblHint").Text == Lang.T("Perform the key combination you want on the keyboard."),
                            $"[{lang}] while recording the help text asks for the key combination");
                    }
                    finally { typeof(KeyEditorForm).GetMethod("StopRecording", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, new object[] { true }); }
                    Assert(ReferenceEquals(pk[0].IconImage, recordIcon) && pk[0].AccessibleName == idle, $"[{lang}] after stopping the button shows the record icon again");
                }
                finally { Lang.Load("en"); }
            }
            Lang.Load("nl");
            Assert(Lang.T("Perform the key combination you want on the keyboard.") == "Voer de gewenste toetscombinatie uit op het toetsenbord.",
                "Dutch help text while recording");
            Lang.Load("en");

            // ── Size ──
            using (var f = new KeyEditorForm(new KeyProps("a", "a"), null, colSpan: 3, rowSpan: 2, maxCols: 5, maxRows: 4))
            {
                applyMi.Invoke(f, null);
                Assert(f.ResultColSpan == 3 && f.ResultRowSpan == 2, "width and height come back unchanged");
            }

            // ── Appearance: a key in a group stays in it until a field is changed ──
            var groups = new List<KeyGroup> { new KeyGroup { Name = SettingsManager.StandardGroupName }, new KeyGroup { Name = "Klinkers", KeyColor = Color.Red } };
            r = Apply(new KeyProps("a", "a") { GroupName = "Klinkers" }, groups: groups);
            Assert(r.GroupName == "Klinkers" && r.KeyColor.IsEmpty, "untouched key stays in its group and keeps no colour of its own");
            r = Apply(new KeyProps("a", "a") { GroupName = "Klinkers" }, f => Field<ColorChip>(f, "_chipKey").Value = Color.Blue, groups: groups);
            Assert(r.GroupName == "" && r.KeyColor.ToArgb() == Color.Blue.ToArgb(), "changing a colour detaches the key and keeps the new colour");
        }

        // ════════════════════════════════════════════════════════════════
        // WCAG 2.1 AAA colour contrast of the dialog palette, light and dark
        //   1.4.6  text                        >= 7 : 1
        //   1.4.11 boundary of a control/focus >= 3 : 1
        // Disabled text is exempt from both rules.
        // ════════════════════════════════════════════════════════════════
        private static void T_ColourContrastAaa()
        {
            Section("WCAG AAA — colour contrast of the dialog palette (light and dark)");

            double Ratio(Color a, Color b) => WizardThemeValidator.ContrastRatio(a, b);
            Color Darken(Color c, float amount) =>
                Color.FromArgb(c.A, (int)(c.R * (1f - amount)), (int)(c.G * (1f - amount)), (int)(c.B * (1f - amount)));

            var lightBgs = new[] { ("BgPage", Fluent.BgPage), ("BgCard", Fluent.BgCard), ("BgInput", Fluent.BgInput) };
            var darkBgs  = new[] { ("DarkBg", Fluent.DarkBg), ("DialogDarkCard", Fluent.DialogDarkCard), ("DialogDarkInput", Fluent.DialogDarkInput) };

            void Check(string what, double min, (string Name, Color Fg)[] foregrounds, (string Name, Color Bg)[] backgrounds)
            {
                var pairs = new List<(string Text, double Ratio)>();
                foreach (var f in foregrounds)
                    foreach (var b in backgrounds)
                        pairs.Add(($"{f.Name} on {b.Name}", Ratio(f.Fg, b.Bg)));
                AssertAll(pairs, p => p.Ratio >= min, p => $"{p.Text} = {p.Ratio:F2}:1", $"{what} >= {min}:1");
            }

            // ── Text (AAA 1.4.6): 7 : 1 ──
            Check("light: text", 7.0,
                new[] { ("TextPrimary", Fluent.TextPrimary), ("TextSecondary", Fluent.TextSecondary),
                        ("TextHint", Fluent.TextHint), ("Danger", Fluent.Danger) }, lightBgs);
            Check("dark: text", 7.0,
                new[] { ("DialogDarkText", Fluent.DialogDarkText), ("DialogDarkTextDim", Fluent.DialogDarkTextDim),
                        ("DialogDarkDanger", Fluent.DialogDarkDanger) }, darkBgs);

            // Button labels: white on the coloured fills (a hover or pressed fill is darker, so only rises), dark on Neutral.
            Check("button labels: white on Accent / Success / Danger", 7.0,
                new[] { ("White", Color.White) },
                new[] { ("Accent", Fluent.Accent), ("Success", Fluent.Success), ("Danger", Fluent.Danger) });
            Check("button labels: TextPrimary on Neutral (rest, hover, pressed)", 7.0,
                new[] { ("TextPrimary", Fluent.TextPrimary) },
                new[] { ("Neutral", Fluent.Neutral), ("Neutral hover", Darken(Fluent.Neutral, 0.07f)), ("Neutral pressed", Darken(Fluent.Neutral, 0.12f)) });

            // ── Boundaries and focus (1.4.11): 3 : 1 ──
            Check("light: control boundary", 3.0,
                new[] { ("ControlBorder", Fluent.ControlBorder), ("ControlBorderHover", Fluent.ControlBorderHover) }, lightBgs);
            Check("dark: control boundary", 3.0,
                new[] { ("DialogDarkBorder", Fluent.DialogDarkBorder) }, darkBgs);
            Check("focus ring / selection (Accent) on the light surfaces and on a Neutral button", 3.0,
                new[] { ("Accent", Fluent.Accent) },
                new[] { ("BgPage", Fluent.BgPage), ("BgCard", Fluent.BgCard), ("Neutral", Fluent.Neutral) });

            // The pale greys stay for grouping lines only; make sure nobody uses one as a control border again.
            Assert(Ratio(Fluent.ControlBorder, Fluent.BgPage) > Ratio(Fluent.BorderCard, Fluent.BgPage) * 2,
                "the control border is much stronger than the pale grouping line (BorderCard)");
        }

    }
}

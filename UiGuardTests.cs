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
                bool wraps = c is Label;
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
                        int sections = bar?.Count ?? 1;
                        for (int i = 0; i < sections; i++)
                        {
                            bar?.Select(i, focus: false);
                            Application.DoEvents();
                            d.PerformLayout();
                            var t = UiGuard.TargetViolations(d, visibleOnly: true);
                            var c = UiGuard.ClippedText(d, visibleOnly: true);
                            var o = UiGuard.Overflow(d, visibleOnly: true);
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
        // Key Editor behaviour: every action type survives load + Apply
        // ════════════════════════════════════════════════════════════════
        private static void T_KeyEditorRoundTrip()
        {
            Section("Key Editor — every action type survives load and Apply; layers behave");

            var applyMi = typeof(KeyEditorForm).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance);
            T Field<T>(object f, string name) => (T)typeof(KeyEditorForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
            const int Text = 0, Key = 1, Modifier = 2, WordPrediction = 3, Layout = 4;

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
                t[1].SelectedIndex = Key;    v[1].Text = "{Ctrl}v"; });
            Assert(r.ShiftSend == "^v", "Shift layer: a typed shortcut is stored in SendKeys syntax");
            r = Apply(new KeyProps("a", "a"), f => { var t = Field<TouchChoiceButton[]>(f, "_types"); var v = Field<TouchTextBox[]>(f, "_values");
                t[2].SelectedIndex = Layout; v[2].Text = "azerty.kbl"; });
            Assert(r.AltGrSend == "layout:azerty.kbl", "AltGr layer: a chosen layout gets its 'layout:' prefix");
            r = Apply(new KeyProps("a", "a"), f => { var t = Field<TouchChoiceButton[]>(f, "_types"); var v = Field<TouchTextBox[]>(f, "_values");
                t[1].SelectedIndex = Text;   v[1].Text = "B"; });
            Assert(r.ShiftSend == "B", "Shift layer: plain text is stored as typed");

            // ── Modifier and Word prediction belong to the whole key: refused on Shift / AltGr ──
            using (var f = new KeyEditorForm(new KeyProps("a", "a"), null))
            {
                var t = Field<TouchChoiceButton[]>(f, "_types");
                t[1].SelectedIndex = Modifier;
                t[2].SelectedIndex = WordPrediction;
                Assert(t[1].SelectedIndex == Text && t[2].SelectedIndex == Text, "Modifier / Word prediction cannot be chosen on Shift or AltGr");
                Assert(!t[1].Items[Modifier].Enabled && !string.IsNullOrEmpty(t[1].Items[Modifier].DisabledReason),
                    "the disabled types say why");
                Assert(t[0].Items[Modifier].Enabled && t[0].Items[WordPrediction].Enabled, "on the Normal layer all five types are available");
            }

            // ── Choosing a type resets the value for it; word prediction takes the first free slot ──
            r = Apply(new KeyProps("a", "a"), f => Field<TouchChoiceButton[]>(f, "_types")[0].SelectedIndex = WordPrediction, usedWp: new HashSet<int> { 0, 1 });
            Assert(r.Send == "wp:2", $"word prediction: the first free slot is assigned ({r.Send})");
            r = Apply(new KeyProps("a", "a"), f => Field<TouchChoiceButton[]>(f, "_types")[0].SelectedIndex = Modifier);
            Assert(r.Label == "Shift" && r.Send == "", $"modifier: the first modifier is chosen and becomes the label ({r.Label})");

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

        // ════════════════════════════════════════════════════════════════
        // Baseline: how far the existing editors are from the guards
        // ════════════════════════════════════════════════════════════════
        private static void T_UiGuardBaseline()
        {
            Section("UI guard baseline — existing editor dialogs (reported, not enforced yet)");

            var lines = new List<string> { "UI guard baseline — existing dialogs (not migrated yet)", $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "" };
            var groups = new List<KeyGroup> { new KeyGroup { Name = "standard" } };
            var forms = new (string Name, Func<Form> Make)[]
            {
                ("GroupEditorForm",    () => new GroupEditorForm(groups)),
                ("KeyboardEditorForm", () => new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), null)),
            };
            foreach (var (name, make) in forms)
            {
                using var f = make();
                var t = UiGuard.TargetViolations(f, visibleOnly: false);
                int pointer = UiGuard.All(f).Count(Touch.IsPointerControl);
                lines.Add($"{name}: {f.Width} x {f.Height} px, {t.Count} of {pointer} pointer controls are smaller than 44 x 44");
                foreach (var v in t) lines.Add("    " + v);
                lines.Add("");
                Assert(pointer > 0, $"baseline: {name} has pointer controls to check ({pointer})");
            }
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_guard_report.txt");
            try { File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true)); } catch { }
            Assert(File.Exists(path), "baseline: report written (ui_guard_report.txt)");
        }
    }
}

// TouchChoiceColorTests.cs — behaviour tests for the two flyout components of the touch editors:
//
//   • TouchChoiceButton + ChoicePopup  (selection skips unavailable rows, filter, scrolling limits, keyboard)
//   • ColorChip + ColorFlyout          (hex parsing, palette selection and keyboard, invalid code)
//
// The flyouts are built but never shown: their state is read through reflection and their keyboard and mouse
// handlers are called directly, so the tests need no window and no focus.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

        private static T Priv<T>(object o, string field) => (T)o.GetType().GetField(field, NonPublic).GetValue(o);

        /// <summary>Calls a protected key / mouse handler (OnKeyDown, OnMouseUp, …) of a control.</summary>
        private static void Send(Control c, string handler, EventArgs args)
        {
            var mi = c.GetType().GetMethod(handler, NonPublic | BindingFlags.FlattenHierarchy) ?? typeof(Control).GetMethod(handler, NonPublic);
            mi.Invoke(c, new object[] { args });
        }

        private static void Key(Control c, Keys k) => Send(c, "OnKeyDown", new KeyEventArgs(k));

        private static List<TouchChoice> Choices(int n, Func<int, bool> enabled = null, string prefix = "Item ") =>
            Enumerable.Range(0, n).Select(i => new TouchChoice { Text = prefix + i, Enabled = enabled?.Invoke(i) ?? true }).ToList();

        // ════════════════════════════════════════════════════════════════
        // TouchChoiceButton and its flyout
        // ════════════════════════════════════════════════════════════════
        private static void T_TouchChoiceButton()
        {
            Section("TouchChoiceButton — selection, unavailable rows, keyboard");

            // ── Selection rules of the button itself ──
            using (var b = new TouchChoiceButton())
            {
                int raised = 0;
                b.SelectedIndexChanged += (s, e) => raised++;
                b.SetItems(Choices(4, i => i != 2), 0);
                Assert(b.SelectedIndex == 0 && raised == 0, "SetItems: selects the given row without raising the event");
                Assert(b.Text == "Item 0" && b.AccessibleName == "Item 0", "the button shows (and announces) the selected choice");

                b.SelectedIndex = 1;
                Assert(b.SelectedIndex == 1 && raised == 1 && b.Text == "Item 1", "choosing another row changes the selection and raises the event once");
                b.SelectedIndex = 1;
                Assert(raised == 1, "choosing the row that is already selected raises nothing");
                b.SelectedIndex = 2;
                Assert(b.SelectedIndex == 1 && raised == 1, "an unavailable row cannot be chosen");
                b.SelectedIndex = -1; b.SelectedIndex = 4; b.SelectedIndex = 99;
                Assert(b.SelectedIndex == 1 && raised == 1, "an index outside the list is ignored");

                b.SelectSilently(2);
                Assert(b.SelectedIndex == 2 && raised == 1, "SelectSilently accepts an unavailable row and raises nothing");
                b.SelectSilently(2); b.SelectSilently(-1); b.SelectSilently(10);
                Assert(b.SelectedIndex == 2, "SelectSilently ignores an index outside the list");

                b.SetItems(Choices(2), 7);
                Assert(b.SelectedIndex == 1 && b.Items.Count == 2, "SetItems clamps a selection past the end to the last row");
                b.SetItems(new List<TouchChoice>(), 0);
                Assert(b.SelectedIndex == -1 && b.SelectedItem == null, "SetItems with no rows: nothing is selected");

                // SetItems(Items) — the caller may pass the button's own list
                b.SetItems(Choices(3), 0);
                b.SetItems(b.Items, 1);
                Assert(b.Items.Count == 3 && b.SelectedIndex == 1, "SetItems works when given the button's own Items list");
            }

            // ── Keyboard on the closed button: Up / Down change the choice, skipping unavailable rows ──
            using (var b = new TouchChoiceButton())
            {
                var chosen = new List<int>();
                b.SetItems(Choices(5, i => i != 1 && i != 3), 0);                    // available: 0, 2, 4
                b.SelectedIndexChanged += (s, e) => chosen.Add(b.SelectedIndex);
                Key(b, Keys.Down);
                Assert(b.SelectedIndex == 2, "Down skips an unavailable row");
                Key(b, Keys.Down);
                Assert(b.SelectedIndex == 4, "Down skips the next unavailable row too");
                Key(b, Keys.Down);
                Assert(b.SelectedIndex == 4 && chosen.Count == 2, "Down on the last row stays and raises nothing");
                Key(b, Keys.Up);
                Assert(b.SelectedIndex == 2, "Up skips an unavailable row");
                Key(b, Keys.Up); Key(b, Keys.Up);
                Assert(b.SelectedIndex == 0, "Up on the first row stays");

                // The last available rows are unavailable: Down finds nothing and stays.
                b.SetItems(Choices(4, i => i < 2), 1);
                Key(b, Keys.Down);
                Assert(b.SelectedIndex == 1, "Down with only unavailable rows below stays");
            }

            Section("ChoicePopup — height, scrolling limits, filter, keyboard");

            // Builds an unshown flyout; Chosen records the ORIGINAL index of what was picked.
            ChoicePopup Popup(List<TouchChoice> items, int selected, bool searchable = false, int maxHeight = 2000, int maxRows = 8, int rowHeight = 44, List<int> picked = null)
            {
                var p = new ChoicePopup(items, selected, 1f, 400, rowHeight, searchable, maxHeight, maxRows);
                if (picked != null) p.Chosen += i => picked.Add(i);
                return p;
            }
            int Hot(ChoicePopup p) => Priv<int>(p, "_hot");
            int Top(ChoicePopup p) => Priv<int>(p, "_top");
            List<int> View(ChoicePopup p) => Priv<List<int>>(p, "_view");
            Rectangle Viewport(ChoicePopup p) => Priv<Rectangle>(p, "_viewport");
            int MaxTop(ChoicePopup p) => Math.Max(0, View(p).Count * 44 - Viewport(p).Height);

            // ── Size: short lists do not scroll, long ones are capped ──
            using (var p = Popup(Choices(3), 0))
            {
                Assert(!Priv<bool>(p, "_scrollable"), "3 rows: no scroll strips");
                Assert(Viewport(p).Height == 3 * 44, "3 rows: the list is exactly as tall as its rows");
            }
            using (var p = Popup(Choices(30), 0))
            {
                Assert(Priv<bool>(p, "_scrollable"), "30 rows: the list scrolls");
                Assert(Viewport(p).Height <= 8 * 44 && Viewport(p).Height >= 2 * 44, $"30 rows: at most 8 rows show at once ({Viewport(p).Height / 44.0:0.#} rows)");
            }
            using (var p = Popup(Choices(30), 0, searchable: true, maxHeight: 300))
                Assert(p.Height <= 300, $"a flyout never exceeds the room it is given ({p.Height} of 300 px)");
            using (var p = Popup(Choices(30), 0, maxHeight: 40))
                Assert(Viewport(p).Height >= 2 * 44, "even with no room at all, at least two rows stay visible");
            using (var p = Popup(Choices(30), 0, maxRows: 5))
                Assert(Viewport(p).Height <= 5 * 44, "MaxVisibleRows is respected");

            // ── The selected row is visible when the flyout opens ──
            foreach (int sel in new[] { 0, 7, 15, 29 })
                using (var p = Popup(Choices(30), sel))
                {
                    int rowTop = sel * 44 - Top(p);
                    Assert(rowTop >= 0 && rowTop + 44 <= Viewport(p).Height, $"row {sel} of 30 is inside the viewport when the flyout opens");
                    Assert(Hot(p) == sel, $"row {sel}: the highlight starts on the selected row");
                }

            // ── Scrolling stays within 0 … MaxTop ──
            using (var p = Popup(Choices(30), 0))
            {
                int max = MaxTop(p);
                Assert(max > 0, "30 rows: there is something to scroll");
                for (int i = 0; i < 100; i++) Send(p, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120));
                Assert(Top(p) == max, $"wheel down stops at the end ({Top(p)} = {max})");
                for (int i = 0; i < 100; i++) Send(p, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, +120));
                Assert(Top(p) == 0, "wheel up stops at the start");
                for (int i = 0; i < 100; i++) Key(p, Keys.PageDown);
                Assert(Top(p) >= 0 && Top(p) <= max && Hot(p) == 29, $"PageDown past the end stays inside the list (hot {Hot(p)}, top {Top(p)})");
                Assert(Top(p) == max, "…and the last row is visible");
                for (int i = 0; i < 100; i++) Key(p, Keys.PageUp);
                Assert(Top(p) == 0 && Hot(p) == 0, "PageUp past the start stops at the first row");
                Key(p, Keys.End);
                Assert(Hot(p) == 29 && Top(p) == max, "End goes to the last row");
                Key(p, Keys.Home);
                Assert(Hot(p) == 0 && Top(p) == 0, "Home goes to the first row");
            }
            using (var p = Popup(Choices(3), 0))
            {
                Send(p, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120));
                Assert(Top(p) == 0, "a list that fits does not scroll");
            }

            // ── Highlight skips unavailable rows ──
            var skipped = Choices(8, i => i == 0 || i == 3 || i == 4 || i == 7);              // available: 0, 3, 4, 7
            using (var p = Popup(skipped, 0))
            {
                Key(p, Keys.Down); Assert(Hot(p) == 3, "Down skips the unavailable rows 1 and 2");
                Key(p, Keys.Down); Assert(Hot(p) == 4, "Down moves to the next available row");
                Key(p, Keys.Down); Assert(Hot(p) == 7, "Down skips 5 and 6");
                Key(p, Keys.Down); Assert(Hot(p) == 7, "Down on the last available row stays");
                Key(p, Keys.Up);   Assert(Hot(p) == 4, "Up moves back");
                Key(p, Keys.Up); Key(p, Keys.Up); Key(p, Keys.Up);
                Assert(Hot(p) == 0, "Up stops at the first available row");
                Key(p, Keys.End);  Assert(Hot(p) == 7, "End goes to the last AVAILABLE row");
            }
            using (var p = Popup(Choices(6, i => i > 1), 5))
            {
                Key(p, Keys.Home);
                Assert(Hot(p) == 2, "Home goes to the first AVAILABLE row when the first rows are unavailable");
                Key(p, Keys.Up);
                Assert(Hot(p) == 2, "…and Up from there stays");
            }
            using (var p = Popup(Choices(6, i => i < 4), 0))
            {
                Key(p, Keys.End);
                Assert(Hot(p) == 3, "End goes to the last AVAILABLE row when the last rows are unavailable");
                Key(p, Keys.Down);
                Assert(Hot(p) == 3, "…and Down from there stays");
            }
            using (var p = Popup(Choices(12, i => i < 9), 0))
            {
                Key(p, Keys.PageDown); Key(p, Keys.PageDown); Key(p, Keys.PageDown);
                Assert(Hot(p) == 8, $"PageDown into unavailable rows stops on the last available row ({Hot(p)})");
            }

            // ── Picking ──
            var picked = new List<int>();
            using (var p = Popup(Choices(6, i => i != 2), 0, picked: picked))
            {
                Key(p, Keys.Down); Key(p, Keys.Down);                                        // 1, then 3 (2 is unavailable)
                Key(p, Keys.Enter);
                Assert(picked.SequenceEqual(new[] { 3 }), $"Enter picks the highlighted row ({string.Join(",", picked)})");
                Assert(p.IsDisposed, "picking closes the flyout");
            }
            picked.Clear();
            using (var p = Popup(Choices(6, i => i != 2), 0, picked: picked))
            {
                typeof(ChoicePopup).GetMethod("Pick", NonPublic).Invoke(p, new object[] { 2 });      // a click on the unavailable row
                Assert(picked.Count == 0 && !p.IsDisposed, "clicking an unavailable row picks nothing and keeps the flyout open");
                typeof(ChoicePopup).GetMethod("Pick", NonPublic).Invoke(p, new object[] { -1 });
                typeof(ChoicePopup).GetMethod("Pick", NonPublic).Invoke(p, new object[] { 99 });
                Assert(picked.Count == 0, "a click outside the rows picks nothing");
                Key(p, Keys.Space);
                Assert(picked.SequenceEqual(new[] { 0 }), "Space picks the highlighted row of a list without a search box");
            }
            picked.Clear();
            using (var p = Popup(Choices(6), 4, picked: picked))
            {
                Key(p, Keys.Escape);
                Assert(picked.Count == 0 && p.IsDisposed, "Escape closes the flyout and picks nothing");
            }

            // ── Filter ──
            var fonts = new List<TouchChoice>();
            foreach (var n in new[] { "Arial", "Arial Black", "Calibri", "Cambria", "Consolas", "Courier New", "Segoe UI", "Verdana" })
                fonts.Add(new TouchChoice { Text = n });
            fonts[1].Enabled = false;                                                         // Arial Black unavailable
            picked.Clear();
            using (var p = Popup(fonts, 2, searchable: true, picked: picked))
            {
                var search = Priv<TouchTextBox>(p, "_search");
                search.Text = "ar";
                Assert(View(p).Select(i => fonts[i].Text).SequenceEqual(new[] { "Arial", "Arial Black" }),
                    $"filter 'ar': {string.Join(", ", View(p).Select(i => fonts[i].Text))}");
                search.Text = "CA";
                Assert(View(p).Select(i => fonts[i].Text).SequenceEqual(new[] { "Calibri", "Cambria" }), "the filter ignores upper / lower case");
                search.Text = "  segoe ";
                Assert(View(p).Count == 1 && fonts[View(p)[0]].Text == "Segoe UI", "the filter ignores spaces around the text");
                search.Text = "";
                Assert(View(p).Count == fonts.Count, "an empty filter shows every row again");

                search.Text = "ar";
                Assert(Hot(p) == 0 && Top(p) == 0, "after filtering the highlight is on the first available match");
                Key(p, Keys.Down);
                Assert(Hot(p) == 0, "…and Down does not move onto the unavailable match");
                Key(p, Keys.Enter);
                Assert(picked.SequenceEqual(new[] { 0 }), "Enter in the filtered list picks the ORIGINAL index of the match");
            }
            picked.Clear();
            using (var p = Popup(fonts, 0, searchable: true, picked: picked))
            {
                var search = Priv<TouchTextBox>(p, "_search");
                search.Text = "ver";
                Key(p, Keys.Enter);
                Assert(picked.SequenceEqual(new[] { 7 }), $"filtered 'ver' → Verdana is picked as item 7, not row 0 ({string.Join(",", picked)})");
            }
            picked.Clear();
            using (var p = Popup(fonts, 0, searchable: true, picked: picked))
            {
                var search = Priv<TouchTextBox>(p, "_search");
                search.Text = "zzz";
                Assert(View(p).Count == 0 && Hot(p) == -1, "no match: no rows and no highlight");
                Key(p, Keys.Enter); Key(p, Keys.Down); Key(p, Keys.PageDown);
                Assert(picked.Count == 0 && !p.IsDisposed, "no match: Enter, Down and PageDown do nothing");
                search.Text = "co";
                Assert(View(p).Select(i => fonts[i].Text).SequenceEqual(new[] { "Consolas", "Courier New" }),
                    "typing again after 'no match' brings the matches back");
            }
            picked.Clear();
            using (var p = Popup(fonts, 0, searchable: true, picked: picked))
            {
                Key(p, Keys.Space);
                Assert(picked.Count == 0, "with a search box Space is a character, not a pick");
                Key(p, Keys.End);
                Assert(Hot(p) == 0, "with a search box Home / End belong to the text box");
            }

            // ── A filter on a scrolled list returns to the top ──
            using (var p = Popup(Choices(40, prefix: "Font "), 0, searchable: true))
            {
                Key(p, Keys.PageDown);
                Assert(Top(p) > 0, "scrolled down");
                Priv<TouchTextBox>(p, "_search").Text = "Font 1";
                Assert(Top(p) == 0 && View(p).Count == 11, $"filtering scrolls back to the top ({View(p).Count} matches for 'Font 1')");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // ColorChip and ColorFlyout
        // ════════════════════════════════════════════════════════════════
        private static void T_ColorFlyout()
        {
            Section("ColorChip / ColorFlyout — hex box, palette, keyboard");

            var palette = (Color[])typeof(ColorFlyout).GetField("Palette", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Assert(palette.Length == 24 && palette.Select(c => c.ToArgb()).Distinct().Count() == 24, "the palette has 24 different colours");

            Color Parse(string s) => SettingsManager.ParseColor(s, Color.Empty);

            // ── The chip ──
            using (var chip = new ColorChip("Key", Color.White))
            {
                Assert(chip.Value == Color.White && chip.ForeColor == Color.Black, "chip: a light colour gets dark text");
                chip.Value = Color.Black;
                Assert(chip.ForeColor == Color.White, "chip: a dark colour gets white text");
                chip.Value = Color.FromArgb(255, 215, 0);
                Assert(chip.ForeColor == Color.Black, "chip: yellow gets dark text");
                chip.Value = Color.FromArgb(26, 78, 138);
                Assert(chip.ForeColor == Color.White, "chip: dark blue gets white text");
                Assert(chip.AccessibleName == "Key color 1A4E8A", $"chip: the accessible name carries the caption and the code ('{chip.AccessibleName}')");
                Assert(chip.Width >= Touch.Target || chip.MinimumSize.Height >= Touch.Target, "chip: at least 44 px high");
            }

            // ── The hex box ──
            using (var f = new ColorFlyout(Color.FromArgb(18, 52, 86), 1f))
            {
                var hex = Priv<TouchTextBox>(f, "_hex");
                var picks = new List<Color>();
                f.Picked += c => picks.Add(c);
                Assert(hex.Text == "#123456", $"the hex box starts with the current colour ('{hex.Text}')");

                hex.Text = "#FF0000";
                Assert(picks.Count == 1 && picks[0].ToArgb() == Color.Red.ToArgb(), "typing a valid code applies the colour");
                hex.Text = "00ff7f";
                Assert(picks.Count == 2 && picks[1].ToArgb() == Parse("00FF7F").ToArgb(), "a code without '#' and in lower case works");

                var normal = hex.ForeColor;
                int before = picks.Count;
                hex.Text = "#12";
                Assert(picks.Count == before, "a half-typed code applies nothing");
                Assert(hex.ForeColor != normal, "an invalid code turns the text red");
                hex.Text = "#GGGGGG";
                Assert(picks.Count == before, "a code with non-hex characters applies nothing");
                hex.Text = "#1234567";
                Assert(picks.Count == before, "a code that is too long applies nothing");
                hex.Text = "";
                Assert(picks.Count == before, "an empty box applies nothing");
                hex.Text = "#0078D4";
                Assert(picks.Count == before + 1 && hex.ForeColor == normal, "a valid code applies the colour and the text is normal again");
                var current = Priv<Color>(f, "_current");
                Assert(current.ToArgb() == Parse("0078D4").ToArgb(), "the flyout keeps the last valid colour");

                Key(hex, Keys.Enter);
                Assert(f.IsDisposed, "Enter in the hex box closes the flyout");
            }
            using (var f = new ColorFlyout(Color.Red, 1f))
            {
                var picks = new List<Color>();
                f.Picked += c => picks.Add(c);
                var hex = Priv<TouchTextBox>(f, "_hex");
                hex.Text = "#zzzzzz";
                Key(hex, Keys.Enter);
                Assert(picks.Count == 0, "an invalid code never reaches the caller, not even with Enter");
            }

            // ── The palette ──
            object Palette(ColorFlyout f) => Priv<object>(f, "_palette");
            int Focus(ColorFlyout f) => Priv<int>(Palette(f), "_focus");
            void PKey(ColorFlyout f, Keys k) => Key((Control)Palette(f), k);

            using (var f = new ColorFlyout(palette[5], 1f))
            {
                Assert(Focus(f) == 5, "the palette cursor starts on the current colour");
                PKey(f, Keys.Right); Assert(Focus(f) == 6, "Right moves one swatch");
                PKey(f, Keys.Down);  Assert(Focus(f) == 12, "Down moves one row (6 swatches)");
                PKey(f, Keys.Left);  Assert(Focus(f) == 11, "Left moves back one swatch");
                PKey(f, Keys.Up);    Assert(Focus(f) == 5, "Up moves up one row");
                PKey(f, Keys.Up);    Assert(Focus(f) == 0, "Up in the top row stops at the first swatch");
                PKey(f, Keys.Left);  Assert(Focus(f) == 0, "Left at the first swatch stays");
                for (int i = 0; i < 10; i++) PKey(f, Keys.Down);
                Assert(Focus(f) == 23, "Down past the bottom stops at the last swatch");
                PKey(f, Keys.Right); Assert(Focus(f) == 23, "Right at the last swatch stays");
            }
            using (var f = new ColorFlyout(Color.FromArgb(1, 2, 3), 1f))
                Assert(Focus(f) == 0, "a colour that is not in the palette: the cursor starts on the first swatch");

            var got = new List<Color>();
            using (var f = new ColorFlyout(palette[5], 1f))
            {
                f.Picked += c => got.Add(c);
                PKey(f, Keys.Right); PKey(f, Keys.Right);
                PKey(f, Keys.Enter);
                Assert(got.Count == 1 && got[0].ToArgb() == palette[7].ToArgb(), "Enter picks the swatch under the cursor");
                Assert(f.IsDisposed, "picking a swatch closes the flyout");
                Assert(Priv<Color>(f, "_current").ToArgb() == palette[7].ToArgb(), "the flyout remembers the picked colour");
            }
            got.Clear();
            using (var f = new ColorFlyout(palette[0], 1f))
            {
                f.Picked += c => got.Add(c);
                PKey(f, Keys.Space);
                Assert(got.Count == 1 && got[0].ToArgb() == palette[0].ToArgb(), "Space picks the swatch under the cursor");
            }
            got.Clear();
            using (var f = new ColorFlyout(palette[0], 1f))
            {
                f.Picked += c => got.Add(c);
                // Click on swatch 7 (row 1, column 1): cells are 44 px with a 6 px gap.
                Send((Control)Palette(f), "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 50 + 10, 50 + 10, 0));
                Assert(got.Count == 1 && got[0].ToArgb() == palette[7].ToArgb(), "a click on a swatch picks it");
                Assert(Focus(f) == 7, "…and moves the cursor there");
            }
            got.Clear();
            using (var f = new ColorFlyout(palette[0], 1f))
            {
                f.Picked += c => got.Add(c);
                Send((Control)Palette(f), "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 47, 10, 0));    // in the gap between two swatches
                Assert(got.Count == 0, "a click in the gap between swatches picks nothing");
            }
            using (var f = new ColorFlyout(palette[0], 1f))
            {
                typeof(Control).GetMethod("ProcessCmdKey", NonPublic).Invoke(f, new object[] { new Message(), Keys.Escape });      // Esc as the form receives it
                Assert(f.IsDisposed, "Escape closes the flyout");
            }

            // ── Hex box and palette stay in step ──
            using (var f = new ColorFlyout(Color.Red, 1f))
            {
                var hex = Priv<TouchTextBox>(f, "_hex");
                hex.Text = "#" + SettingsManager.Hex(palette[11]);
                Assert(Priv<Color>(Palette(f), "_selected").ToArgb() == palette[11].ToArgb() && Focus(f) == 11,
                    "typing the code of a palette colour moves the palette cursor there");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // The flyouts opened from a real chip / button on a shown form: placement, keyboard, focus return, closing.
        // (The flyouts stay open on their own with KeepOpen, as in the gallery; "More colours…" is a modal Windows dialog: by hand.)
        // ════════════════════════════════════════════════════════════════
        private static void T_FlyoutsFromControls()
        {
            Section("Flyouts opened from a chip and a choice button — placement, keyboard, focus, closing");

            var wa = Screen.PrimaryScreen.WorkingArea;
            // The host is shown invisibly at a given spot; the chip and the button sit at its top left.
            (Form Host, ColorChip Chip, TouchChoiceButton Choice) MakeHost(int x, int y)
            {
                var host = new Form { StartPosition = FormStartPosition.Manual, ClientSize = new Size(400, 200), Location = new Point(x, y) };
                var chip = new ColorChip("Key", Color.Red) { Location = new Point(10, 10), InheritText = "Back to the group" };
                var choice = new TouchChoiceButton { Location = new Point(10, 80), Size = new Size(200, Touch.Target) };
                choice.SetItems(Choices(5), 0);
                host.Controls.Add(chip);
                host.Controls.Add(choice);
                DevGallery.Show(host);
                host.Location = new Point(x, y);          // Show puts the window at (20, 20)
                Application.DoEvents();
                return (host, chip, choice);
            }
            Rectangle Screen_(Control c) => c.RectangleToScreen(c.ClientRectangle);

            // ── Chip: below the chip when there is room ──
            var (host1, chip1, choice1) = MakeHost(wa.Left + 100, wa.Top + 100);
            try
            {
                int raised = 0;
                chip1.ValueChanged += (s, e) => raised++;
                var f = chip1.OpenPicker(keepOpen: true);
                Assert(f != null && f.Visible && !f.IsDisposed, "chip: the flyout opens");
                Assert(f.Top >= Screen_(chip1).Bottom && f.Left == Screen_(chip1).Left, "chip: the flyout is under the chip, left edges aligned");
                Assert(ReferenceEquals(chip1.OpenPicker(keepOpen: true), f), "chip: opening again does not open a second flyout");

                // Keyboard: Right, Right, Enter picks the swatch next to the cursor, applies it and closes the flyout.
                var pal = Priv<object>(f, "_palette");
                Key((Control)pal, Keys.Right);
                Key((Control)pal, Keys.Enter);
                Assert(f.IsDisposed, "chip: Enter on a swatch closes the flyout");
                Assert(raised == 1 && !chip1.Inherited && chip1.Value.ToArgb() != Color.Red.ToArgb(), "chip: the picked colour is applied and raised once");
                Assert(host1.ActiveControl == chip1, "chip: the focus returns to the chip");

                // Escape closes without changing anything.
                raised = 0;
                var before = chip1.Value;
                // (Esc as the form receives it: through ProcessCmdKey, whichever control inside has the focus.)
                bool Esc(ColorFlyout fl) => (bool)typeof(Control).GetMethod("ProcessCmdKey", NonPublic | BindingFlags.Instance).Invoke(fl, new object[] { new Message(), Keys.Escape });
                f = chip1.OpenPicker(keepOpen: true);
                Assert(Esc(f), "chip: the flyout handles Escape");
                Assert(f.IsDisposed && raised == 0 && chip1.Value == before, "chip: Escape closes the flyout and changes nothing");
                // …and a colour typed into the hex box meanwhile is taken back.
                f = chip1.OpenPicker(keepOpen: true);
                Priv<TouchTextBox>(f, "_hex").Text = "#00FF00";
                Assert(chip1.Value.ToArgb() == Color.FromArgb(0, 255, 0).ToArgb(), "chip: a typed hex colour applies at once");
                Esc(f);
                Assert(f.IsDisposed && chip1.Value.ToArgb() == before.ToArgb(), "chip: Escape takes back a colour typed meanwhile");
                Assert(chip1.OpenPicker(keepOpen: true) is ColorFlyout again && !ReferenceEquals(again, f), "chip: after closing, the next click opens a fresh flyout");
                chip1.OpenPicker(keepOpen: true).Close();

                // The inherit button hands the colour back to the parent.
                raised = 0;
                f = chip1.OpenPicker(keepOpen: true);
                ClickButton(Priv<FluentButton>(f, "_inherit"));
                Assert(chip1.Inherited && raised == 1 && f.IsDisposed, "chip: 'inherit' marks the chip as inherited, raises once and closes the flyout");
                chip1.SetOwn(Color.Red);
                Assert(!chip1.Inherited, "chip: choosing an own colour ends the inheritance");

                // Losing focus closes it, unless a dialog is open on top of it ("More colours…").
                f = chip1.OpenPicker(keepOpen: true);
                f.KeepOpen = false;
                typeof(ColorFlyout).GetField("_dialogOpen", NonPublic).SetValue(f, true);
                Send(f, "OnDeactivate", EventArgs.Empty);
                Assert(!f.IsDisposed, "chip: the flyout stays open while the standard colour dialog is on top of it");
                typeof(ColorFlyout).GetField("_dialogOpen", NonPublic).SetValue(f, false);
                Send(f, "OnDeactivate", EventArgs.Empty);
                Assert(f.IsDisposed, "chip: the flyout closes when it loses focus");

                // ── Choice button: below the button, keyboard, focus return ──
                var p = choice1.OpenPopup(keepOpen: true);
                Assert(p != null && p.Visible, "choice: the flyout opens");
                Assert(p.Top >= Screen_(choice1).Bottom, "choice: the flyout is under the button when there is room");
                Assert(ReferenceEquals(choice1.OpenPopup(keepOpen: true), p), "choice: opening again does not open a second flyout");
                int changed = 0;
                choice1.SelectedIndexChanged += (s, e) => changed++;
                Key(p, Keys.Down);
                Key(p, Keys.Enter);
                Assert(p.IsDisposed && choice1.SelectedIndex == 1 && changed == 1, "choice: Down and Enter choose the next row and close the flyout");
                Assert(host1.ActiveControl == choice1, "choice: the focus returns to the button");

                p = choice1.OpenPopup(keepOpen: true);
                Key(p, Keys.Down);
                Key(p, Keys.Escape);
                Assert(p.IsDisposed && choice1.SelectedIndex == 1 && changed == 1, "choice: Escape closes the flyout and keeps the choice");

                // Alt+Down and F4 open it from the keyboard, as on a normal drop-down.
                Key(choice1, Keys.F4);
                var viaKey = typeof(TouchChoiceButton).GetField("_popup", NonPublic).GetValue(choice1) as ChoicePopup;
                Assert(viaKey != null && !viaKey.IsDisposed, "choice: F4 opens the flyout");
                viaKey?.Close();
            }
            finally { host1.Dispose(); }

            // ── Placement: no room below → above; no room at the right → moved left, inside the working area ──
            var (host2, chip2, choice2) = MakeHost(wa.Right - 80, wa.Bottom - 150);
            try
            {
                var f = chip2.OpenPicker(keepOpen: true);
                Assert(f.Bottom <= Screen_(chip2).Top, "chip near the bottom: the flyout opens above the chip");
                Assert(f.Left >= wa.Left && f.Right <= wa.Right && f.Top >= wa.Top, "chip near the right edge: the flyout stays inside the working area");
                f.Close();

                var p = choice2.OpenPopup(keepOpen: true);
                Assert(p.Bottom <= Screen_(choice2).Top, "button near the bottom: the flyout opens above the button");
                Assert(p.Left >= wa.Left && p.Right <= wa.Right && p.Top >= wa.Top, "button near the right edge: the flyout stays inside the working area");
                p.Close();
            }
            finally { host2.Dispose(); }
        }
    }
}

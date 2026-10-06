// WizardContrastTests.cs — colour contrast of the New Keyboard Wizard's own controls (todo 2.5.2): the theme tiles (TouchTile), the
// grid preview (WizardGridPreview), and the fall-back colours of the sample keys. The text, error lines and buttons of the pages use
// the dialog palette, which T_ColourContrastAaa checks. The colours of the theme presets themselves are todo 2.5.4.
//
// Like T_ToolbarContrast: the AA floor is asserted, the AAA gaps are reported (wizard_contrast_report.txt next to the program).

using System;
using System.Collections.Generic;
using System.Drawing;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_WizardContrast()
        {
            Section("Wizard — colour contrast of the theme tiles, the grid preview and the sample-key fall-backs (AA floor asserted, AAA gaps reported)");

            double Ratio(Color a, Color b) => WizardThemeValidator.ContrastRatio(a, b);
            var rows = new List<(string Name, double Ratio, double Aaa, double Aa, bool Info)>();
            void Floor(string name, Color fg, Color bg, double aaa, double aa) => rows.Add((name, Ratio(fg, bg), aaa, aa, false));
            void Info(string name, Color fg, Color bg, double aaa) => rows.Add((name, Ratio(fg, bg), aaa, 0, true));

            // The dialog surfaces a tile or the preview can sit on: light page and card; dark page and card.
            var surfaces = new (string Name, Color Bg, bool Dark)[]
            {
                ("BgPage", Fluent.BgPage, false), ("BgCard", Fluent.BgCard, false),
                ("DarkBg", Fluent.DarkBg, true),  ("DialogDarkCard", Fluent.DialogDarkCard, true),
            };

            // ── Theme tile (TouchTile.OnPaint): the tile keeps its light Neutral fill (dark text) in both themes. The selection mark
            // (3 px ring and a tick) and the focus ring are drawn INSIDE the tile, so they sit on the tile's own fill, not on the dialog. ──
            var hoverFill = Color.FromArgb(225, 225, 225);
            foreach (var dark in new[] { false, true })
            {
                string th = dark ? "dark" : "light";
                // Found by this test: the dark theme painted the mark with the light dialog text colour, 1.1 : 1 on the tile's own light fill.
                Color sel = Fluent.Accent;                                      // what TouchTile paints the mark and the ring with, in both themes
                Floor($"tile, {th} theme: selection ring on the tile's Neutral fill", sel, Fluent.Neutral, 3, 3);
                Floor($"tile, {th} theme: selection ring on the hovered tile fill (#E1E1E1)", sel, hoverFill, 3, 3);
                Floor($"tile, {th} theme: tick of the selected tile on its Neutral fill (a symbol: 3 : 1)", sel, Fluent.Neutral, 3, 3);
                Floor($"tile, {th} theme: keyboard focus ring on the tile's Neutral fill", sel, Fluent.Neutral, 3, 3);

                // And what is really painted: a selected tile on a dialog of that theme, ring pixel against the fill pixel.
                var parent = new System.Windows.Forms.Panel { BackColor = dark ? Fluent.DialogDarkCard : Fluent.BgCard, Size = new Size(170, 130) };
                var tile = new TouchTile { Text = "Dark", Checked = true, Location = new Point(10, 8), Size = new Size(150, 112) };
                parent.Controls.Add(tile);
                using (var bmp = new Bitmap(tile.Width, tile.Height))
                {
                    tile.DrawToBitmap(bmp, new Rectangle(0, 0, tile.Width, tile.Height));
                    Color ringPx = bmp.GetPixel(tile.Width / 2, 2), fillPx = bmp.GetPixel(14, 40);
                    Assert(Ratio(ringPx, fillPx) >= 3, $"tile, {th} theme: the painted selection ring (#{SettingsManager.Hex(ringPx)}) shows on the painted fill (#{SettingsManager.Hex(fillPx)}) = {Ratio(ringPx, fillPx):F2} : 1");
                }
                parent.Dispose();
            }
            foreach (var (name, bg, dark) in surfaces)
            {
                // What marks the tile off from the dialog: the fill itself (dark theme) or its 1 px border (light theme: the fill is nearly white).
                if (dark) Floor($"tile edge on {name}: the light fill (Neutral) against the dark dialog", Fluent.Neutral, bg, 3, 3);
                else      Floor($"tile edge on {name}: the 1 px border (ControlBorder)", Fluent.ControlBorder, bg, 3, 3);
            }
            Info("tile: outline of a sample-colour key (black at 47 %) against the tile's Neutral fill: decoration", Color.FromArgb(120, 120, 120), Fluent.Neutral, 3);

            // ── Grid preview (WizardGridPreview.OnPaint) ──
            foreach (var dark in new[] { false, true })
            {
                string th = dark ? "dark" : "light";
                Color keyFill  = dark ? Color.FromArgb(58, 58, 58)    : Fluent.BgInput;
                Color gearFill = dark ? Color.FromArgb(44, 44, 44)    : Fluent.Neutral;
                Color line     = dark ? Color.FromArgb(150, 150, 150) : Fluent.ControlBorder;
                Color text     = dark ? Fluent.DialogDarkText         : Fluent.TextPrimary;
                Color ring     = dark ? Fluent.DialogDarkText         : Fluent.Accent;
                Floor($"grid preview, {th}: key label on a key", text, keyFill, 7, 4.5);
                Floor($"grid preview, {th}: gear glyph on the gear key", text, gearFill, 7, 4.5);
                Floor($"grid preview, {th}: key outline against a key", line, keyFill, 3, 3);
                Floor($"grid preview, {th}: key outline against the gear key", line, gearFill, 3, 3);
                foreach (var (name, bg, sdark) in surfaces)
                {
                    if (sdark != dark) continue;
                    Floor($"grid preview, {th}: key outline against {name}", line, bg, 3, 3);
                    Floor($"grid preview, {th}: focus frame against {name}", ring, bg, 3, 3);
                }
            }

            // ── Fall-backs of the sample keys when a theme file cannot be read (NewKeyboardWizard.GetGroupKeyColor / GetPreviewFontColor) ──
            Floor("sample key fall-back: white font on a DimGray key", Color.White, Color.DimGray, 7, 4.5);
            Info("sample keys fall-back, light page: DimGray key against the light strip (235,235,235)", Color.DimGray, Color.FromArgb(235, 235, 235), 3);
            Info("sample keys fall-back, dark page: DimGray key against the dark strip (28,28,40)", Color.DimGray, Color.FromArgb(28, 28, 40), 3);

            ContrastReport("wizard", "wizard_contrast_report.txt", rows);
        }
    }
}

// EditorContrastTests.cs — colour contrast of the colours that the editor dialogs and their controls paint with hand-typed values
// (todo 2.5.3). The dialog palette in Fluent.* is checked strictly by T_ColourContrastAaa; what is left are the literals in the
// controls (a hover fill of #E1E1E1, the white of a check box, the fill of the scroll strips of a list, the preview card) and the
// text on the colour chips, whose fill is whatever colour the user picked.
//
// Like T_ToolbarContrast: the AA floor is asserted, the AAA gaps are reported (editor_contrast_report.txt next to the program).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_EditorContrast()
        {
            Section("Editors — colour contrast of the hand-typed colours in the dialog controls (AA floor asserted, AAA gaps reported)");

            double Ratio(Color a, Color b) => WizardThemeValidator.ContrastRatio(a, b);
            var rows = new List<(string Name, double Ratio, double Aaa, double Aa, bool Info)>();
            void Floor(string name, Color fg, Color bg, double aaa, double aa) => rows.Add((name, Ratio(fg, bg), aaa, aa, false));
            void Info(string name, Color fg, Color bg, double aaa) => rows.Add((name, Ratio(fg, bg), aaa, 0, true));

            // ── Light dialog: the hover fill of a check box, radio button, tile and chooser (TouchControls, WizardControls) ──
            var hover = Color.FromArgb(225, 225, 225);
            Floor("light: label on a hovered check box / radio button / tile (#E1E1E1)", Fluent.TextPrimary, hover, 7, 4.5);
            Floor("light: boundary of a hovered control against its hover fill", Fluent.ControlBorderHover, hover, 3, 3);
            Floor("light: selection ring (Accent) against the hover fill of a tile", Fluent.Accent, hover, 3, 3);

            // ── Check box and radio button ──
            foreach (var (name, bg) in new[] { ("BgPage", Fluent.BgPage), ("BgCard", Fluent.BgCard) })
            {
                Floor($"check box / radio: boundary (ControlBorderHover) on {name}", Fluent.ControlBorderHover, bg, 3, 3);
                Floor($"check box / radio: checked fill (Accent) on {name}", Fluent.Accent, bg, 3, 3);
            }
            Floor("check box: boundary (ControlBorderHover) against the white box", Fluent.ControlBorderHover, Color.White, 3, 3);
            Floor("check box: white tick on the checked Accent box", Color.White, Fluent.Accent, 7, 3);
            Floor("radio button: Accent dot on the white ring", Fluent.Accent, Color.White, 7, 3);

            // ── List rows and the flyout of a chooser (TouchList, TouchChoiceButton) ──
            Floor("list: white text on the selected Accent row", Color.White, Fluent.Accent, 7, 4.5);
            Floor("list: white focus ring on the selected Accent row", Color.White, Fluent.Accent, 3, 3);
            Info("list, light: line between rows (BorderCard) on a row: grouping only, not a control boundary", Fluent.BorderCard, Fluent.BgInput, 3);
            var stripLight = Color.FromArgb(240, 240, 240); var stripDark = Color.FromArgb(62, 62, 62);
            Floor("chooser flyout, light: scroll arrow (TextPrimary) on its strip (#F0F0F0)", Fluent.TextPrimary, stripLight, 7, 3);
            Floor("chooser flyout, dark: scroll arrow (DialogDarkText) on its strip (#3E3E3E)", Fluent.DialogDarkText, stripDark, 7, 3);
            Info("chooser flyout, light: arrow when it cannot scroll further (ControlBorder, disabled look)", Fluent.ControlBorder, stripLight, 3);
            Info("chooser flyout, dark: arrow when it cannot scroll further (DialogDarkBorder, disabled look)", Fluent.DialogDarkBorder, stripDark, 3);

            // ── Preview card of the Key Editor and Group Editor (KeyPreviewCard) ──
            var cardLight = Color.FromArgb(250, 250, 250);
            Floor("preview card, light: caption (TextSecondary) on the card (#FAFAFA)", Fluent.TextSecondary, cardLight, 7, 4.5);
            Floor("preview card, dark: caption (DialogDarkTextDim) on the card (DialogDarkInput)", Fluent.DialogDarkTextDim, Fluent.DialogDarkInput, 7, 4.5);
            Floor("preview card, light: card boundary (ControlBorder) against the dialog (BgPage)", Fluent.ControlBorder, Fluent.BgPage, 3, 3);
            Floor("preview card, light: card boundary (ControlBorder) against the card", Fluent.ControlBorder, cardLight, 3, 3);
            Floor("preview card, dark: card boundary (DialogDarkBorder) against the card", Fluent.DialogDarkBorder, Fluent.DialogDarkInput, 3, 3);
            // The colours a new key / group starts with (KeyPreviewCard, GroupEditorForm.Default*): the user changes them from there.
            Floor("default key preview: font #E0E0FF on key #2D2D4A", SettingsManager.ParseColor("E0E0FF", Color.Black), SettingsManager.ParseColor("2D2D4A", Color.Black), 7, 4.5);
            Info("default key preview: border #3C3C5A against key #2D2D4A (decoration)", SettingsManager.ParseColor("3C3C5A", Color.Black), SettingsManager.ParseColor("2D2D4A", Color.Black), 3);

            // ── Literals in the wizard preview that repeat a token (a cleanup note, not a contrast matter) ──
            Info("wizard preview, dark: key fill (58,58,58) is DialogDarkInput; line (150,150,150) is DialogDarkBorder; gear fill (44,44,44) is not a token",
                Color.FromArgb(150, 150, 150), Color.FromArgb(58, 58, 58), 3);

            // ── The label of a colour chip: black or white, whichever the code picks for the chip's colour (ColorChip.Value) ──
            // The user's colour can be anything, and one of black / white gives at least 4.58 : 1 on any colour, but 7 : 1 only where
            // the colour is dark or light enough. (Found by this test: the label used to be chosen by perceived brightness, not by
            // contrast: white on #F7630C was 3.1 : 1.) The real rule is asked, and a chip must show it.
            Color Label(Color c) => ColorChip.LabelColorFor(c);
            using (var chip = new ColorChip("x", Color.White))
            {
                chip.Value = Color.FromArgb(247, 99, 12);
                Assert(chip.ForeColor == Color.Black, "chip: black text on orange (#F7630C): 6.7 : 1, white would be 3.1 : 1");
                chip.Value = Color.FromArgb(0, 225, 60);
                Assert(chip.ForeColor == Color.Black, "chip: black text on bright green (#00E13C)");
                chip.Value = Color.FromArgb(26, 78, 138);
                Assert(chip.ForeColor == Color.White, "chip: white text on dark blue (#1A4E8A)");
                Assert(ColorChip.LabelColorFor(Color.FromArgb(128, 128, 128)) == Color.Black, "chip: black text on mid grey (#808080): 5.3 : 1 against 3.95 : 1 for white");
            }
            var palette = (Color[])typeof(ColorFlyout).GetField("Palette", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (var c in palette)
                Floor($"chip label on palette colour #{SettingsManager.Hex(c)} ({(Label(c) == Color.Black ? "black" : "white")} label)", Label(c), c, 7, 4.5);

            double worst = 99, worstBest = 99; Color worstAt = Color.Empty, worstBestAt = Color.Empty; int n = 0, below7 = 0, below45 = 0, best7 = 0;
            for (int r = 0; r < 256; r += 15)
                for (int g = 0; g < 256; g += 15)
                    for (int b = 0; b < 256; b += 15)
                    {
                        var c = Color.FromArgb(r, g, b); n++;
                        double chosen = Ratio(Label(c), c);
                        double bestOf = Math.Max(Ratio(Color.Black, c), Ratio(Color.White, c));
                        if (chosen < 7) below7++; if (chosen < 4.5) below45++; if (bestOf < 7) best7++;
                        if (chosen < worst) { worst = chosen; worstAt = c; }
                        if (bestOf < worstBest) { worstBest = bestOf; worstBestAt = c; }
                    }
            rows.Add(($"chip label, any colour (a sample of {n}): worst case with the code's pick, at #{SettingsManager.Hex(worstAt)}; {below7} colours below 7 : 1, {below45} below 4.5 : 1", worst, 7, 0, true));
            rows.Add(($"chip label, any colour: worst case with the better of black / white, at #{SettingsManager.Hex(worstBestAt)}; {best7} colours below 7 : 1 whichever is chosen", worstBest, 7, 0, true));

            ContrastReport("editors", "editor_contrast_report.txt", rows);
        }
    }
}

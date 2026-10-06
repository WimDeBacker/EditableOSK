// ToolbarContrastTests.cs — colour contrast of the main keyboard toolbar (todo 2.5.1).
//
// The dialogs have a strict AAA test (T_ColourContrastAaa). The toolbar is painted by FluentPainter.PaintDark with its own colours
// (Fluent.Dark*), semi-transparent hover and press overlays and the blue DarkActive tab, in a light and a dark theme, on two panels
// (the toolbar and the edit toolbar). This group works out the colour that is really painted (overlays blended over the panel) and
//
//   • asserts the AA floor of every pair that matters (text 4.5 : 1, focus ring and active tab 3 : 1), so nothing gets worse;
//   • reports every pair that is below AAA (text 7 : 1) in the console and in toolbar_contrast_report.txt next to the program,
//     with the number, so the owner can decide what to change (a change of a colour changes how the toolbar looks);
//   • lists, for information, what is exempt (disabled labels) and the tokens that the toolbar does not use at all.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_ToolbarContrast()
        {
            Section("Toolbar — colour contrast of the main keyboard toolbar (AA floor asserted, AAA gaps reported)");

            double Ratio(Color a, Color b) => WizardThemeValidator.ContrastRatio(a, b);
            // The colour that is painted: a semi-transparent colour blended over what is under it.
            Color Over(Color top, Color under)
            {
                float a = top.A / 255f;
                return Color.FromArgb(255, (int)Math.Round(under.R + (top.R - under.R) * a),
                                           (int)Math.Round(under.G + (top.G - under.G) * a),
                                           (int)Math.Round(under.B + (top.B - under.B) * a));
            }

            // Floor rows are asserted at AA, reported when below AAA; information rows are only reported.
            var rows = new List<(string Name, double Ratio, double Aaa, double Aa, bool Info)>();
            void Floor(string name, Color fg, Color bg, double aaa, double aa) => rows.Add((name, Ratio(fg, bg), aaa, aa, false));
            void Info(string name, Color fg, Color bg, double aaa) => rows.Add((name, Ratio(fg, bg), aaa, 0, true));

            // The panels of ToolbarButton.OnPaint / ApplyToolbarTheme, with the overlays of FluentPainter.PaintDark.
            var surfaces = new (string Theme, string Panel, Color Bg, bool Light)[]
            {
                ("dark",  "toolbar",      Fluent.DarkBg,  false), ("dark",  "edit toolbar", Fluent.DarkBg2, false),
                ("light", "toolbar",      Fluent.BgPage,  true),  ("light", "edit toolbar", Fluent.BgCard,  true),
            };
            foreach (var (theme, panel, bg, light) in surfaces)
            {
                string at = $"{theme} {panel}";
                Color fg      = light ? Fluent.TextPrimary : Fluent.DarkText;
                Color hover   = Over(light ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255), bg);
                Color pressed = Over(light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(60, 255, 255, 255), bg);

                // Labels and icons of a button, in its three states.
                Floor($"{at}: label at rest", fg, bg, 7, 4.5);
                Floor($"{at}: label on hover", fg, hover, 7, 4.5);
                Floor($"{at}: label pressed", fg, pressed, 7, 4.5);

                // The active tab (blue) must stand out from the panel, and the keyboard focus ring from the button it surrounds.
                // Light theme: the blue tab itself; dark theme: the blue is dark, so the light outline of PaintDark is the edge.
                Color tabEdge = light ? Fluent.DarkActive : Fluent.DialogDarkText;
                Floor($"{at}: active tab edge ({(light ? "DarkActive" : "light outline")}) against the panel", tabEdge, bg, 3, 3);
                Color ring = light ? Fluent.Accent : Fluent.DialogDarkText;
                if (!light) Floor($"{at}: keyboard focus ring on the active tab (DarkActive)", ring, Fluent.DarkActive, 3, 3);
                Floor($"{at}: keyboard focus ring against the panel", ring, bg, 3, 3);
                Floor($"{at}: keyboard focus ring on a hovered button", ring, hover, 3, 3);

                // Supporting text: the file name under the toolbar, and the chip panel that shows the selected key.
                Color fileName = light ? Fluent.TextSecondary : Color.FromArgb(190, 200, 225);
                Floor($"{at}: file name label", fileName, bg, 7, 4.5);
                Color chipText = light ? Fluent.TextPrimary : Color.FromArgb(225, 232, 245);
                Floor($"{at}: selected-key panel text", chipText, bg, 7, 4.5);

                // Exempt (disabled), or not required (the label names the button): for information.
                Info($"{at}: disabled label (exempt from 1.4.6)", light ? Color.FromArgb(171, 171, 171) : Color.FromArgb(134, 134, 134), bg, 7);
                if (light)
                    Info($"{at}: button outline against the panel (the label names the button: not required)", Over(Color.FromArgb(28, 0, 0, 0), bg), bg, 3);
            }
            Floor("white label on the active tab (DarkActive), both themes", Color.White, Fluent.DarkActive, 7, 4.5);

            // Colours of Fluent that no toolbar code reads (checked 2026-10-06): their values do not matter until something uses them.
            foreach (var panelBg in new[] { Fluent.DarkBg, Fluent.DarkBg2 })
            {
                Info($"unused DarkTextDim on {SettingsManager.Hex(panelBg)}", Over(Fluent.DarkTextDim, panelBg), panelBg, 7);
                Info($"unused DarkBorder on {SettingsManager.Hex(panelBg)}", Over(Fluent.DarkBorder, panelBg), panelBg, 3);
            }

            // The AA floor: everything that matters, nothing may get worse.
            AssertAll(rows.Where(r => !r.Info), r => r.Ratio >= r.Aa, r => $"{r.Name} = {r.Ratio:F2}:1 (floor {r.Aa}:1)",
                "toolbar: every pair reaches its WCAG AA floor (text 4.5 : 1, ring and active tab 3 : 1)");

            // The AAA gaps, for the owner.
            var gaps = rows.Where(r => !r.Info && r.Ratio < r.Aaa).ToList();
            var lines = new List<string> { $"Toolbar colour contrast, {DateTime.Now:yyyy-MM-dd HH:mm}", "AAA: text 7 : 1, ring and active tab 3 : 1.", "" };
            lines.Add($"{gaps.Count} of {rows.Count(r => !r.Info)} pairs are below AAA:");
            lines.AddRange(gaps.Select(r => $"  GAP   {r.Ratio,5:F2} : 1   needs {r.Aaa,2}   {r.Name}"));
            lines.Add("");
            lines.Add("All pairs:");
            lines.AddRange(rows.Select(r => $"  {(r.Info ? "info" : r.Ratio >= r.Aaa ? "ok  " : "GAP ")} {r.Ratio,5:F2} : 1   needs {r.Aaa,2}   {r.Name}"));
            try { File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "toolbar_contrast_report.txt"), lines); } catch (Exception) { }

            Console.ForegroundColor = gaps.Count > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine($"  Toolbar: {gaps.Count} of {rows.Count(r => !r.Info)} pairs below AAA (toolbar_contrast_report.txt)");
            foreach (var g in gaps) Console.WriteLine($"    {g.Ratio,5:F2} : 1  (needs {g.Aaa})  {g.Name}");
            Console.ResetColor();
        }
    }
}

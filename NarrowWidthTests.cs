// NarrowWidthTests.cs — how narrow can each dialog get before its content no longer fits?
//
// A dialog is sized from its content and capped at the screen, so on a narrow screen (a small tablet, or 175-200 % scaling on a
// 1366-px laptop: 683-780 design pixels) it is as wide as the screen and whatever does not fit sticks out or is cut off. This makes the
// window narrower and applies the guards the real tests use (nothing sticks out, nothing cut off, no clipped text).
//
//   T_NarrowWidthReport   prints the narrowest width of every dialog (a report that takes minutes; it only runs on request:
//                         --test NarrowWidthReport)
//   T_KeyEditorNarrow     the Key Editor must fit from its supported minimum width upwards (the first dialog made responsive)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        /// <summary>
        /// The first problem the guards find at <paramref name="width"/> (client width in design pixels) in any section of the dialog, or null
        /// when everything fits: nothing cut off, nothing sticking out, no clipped text.
        /// </summary>
        private static string NarrowProblemAt(FluentDialogBase d, int width)
        {
            d.MinimumSize = Size.Empty;
            d.ClientSize = new Size(width, d.ClientSize.Height);
            int sections = Math.Max(1, d.HostSectionCount > 0 ? d.HostSectionCount : (d.SectionBarAccess?.Count ?? 1));
            for (int i = 0; i < sections; i++)
            {
                d.ShowSectionForGuard(i);
                Application.DoEvents();
                d.PerformLayout();
                var k = UiGuard.CutOff(d, visibleOnly: true);
                if (k.Count > 0) return $"section {i + 1}: {k[0]}";
                var v = UiGuard.Overlaps(d, visibleOnly: true);
                if (v.Count > 0) return $"section {i + 1}: {v[0]}";
                var o = UiGuard.Overflow(d, visibleOnly: true);
                if (o.Count > 0) return $"section {i + 1}: sticks out — {o[0]}";
                var c = UiGuard.ClippedText(d, visibleOnly: true);
                if (c.Count > 0) return $"section {i + 1}: clipped — {c[0]}";
                var t = UiGuard.TargetViolations(d, visibleOnly: true);
                if (t.Count > 0) return $"section {i + 1}: target — {t[0]}";
            }
            return null;
        }

        /// <summary>Walks the client width down from <paramref name="from"/> in steps of 8 px; returns the smallest width at which everything fits and names the next problem.</summary>
        private static int NarrowestFit(FluentDialogBase d, int from, out string why)
        {
            why = "";
            int good = from;
            for (int w = from; w >= 280; w -= 8)
            {
                string problem = NarrowProblemAt(d, w);
                if (problem != null) { why = problem; break; }
                good = w;
            }
            return good;
        }

        private static FluentDialogBase NarrowKeyEditor()
        {
            var groups = new List<KeyGroup> { new KeyGroup { Name = SettingsManager.StandardGroupName }, new KeyGroup { Name = "Klinkers" } };
            var props = new KeyProps("Ctrl+c", "^c", "A", "layout:azerty.kbl", "€", "€") { GroupName = "Klinkers" };
            return new KeyEditorForm(props, null, groups: groups, layoutDir: AppDomain.CurrentDomain.BaseDirectory);
        }

        private static void T_NarrowWidthReport()
        {
            Section("Narrow screens — the smallest width each dialog fits in (report)");
            var dialogs = new (string Name, Func<FluentDialogBase> Make)[]
            {
                ("KeyEditorForm", NarrowKeyEditor),
                ("GroupEditorForm", () => new GroupEditorForm(SampleGroups(), "Klinkers")),
                ("KeyboardEditorForm", () => KeyboardEditor()),
                ("NewKeyboardWizard", () => new NewKeyboardWizard()),
                ("SpecialKeysDialog", () => new SpecialKeysDialog(false)),
            };
            try
            {
                foreach (var (name, make) in dialogs)
                    foreach (var (label, code, pseudo) in new[] { ("en", "en", 0.0), ("nl", "nl", 0.0), ("+40%", "en", 0.4) })
                    {
                        Lang.Load(code);
                        Lang.PseudoExpansion = pseudo;
                        using var d = make();
                        DevGallery.Show(d);
                        int start = Math.Max(d.ClientSize.Width, 1100);
                        d.ClientSize = new Size(start, d.ClientSize.Height);
                        int w = NarrowestFit(d, start, out string why);
                        string text = why.Length > 130 ? why.Substring(0, 130) : why;
                        Console.WriteLine($"    {name,-20} {label,-5} fits down to {w} px   (next problem: {text})");
                        Assert(w > 0, $"{name} [{label}]: fits down to {w} px (report)");
                    }
            }
            finally { Lang.PseudoExpansion = 0; Lang.Load("en"); }
        }

        /// <summary>
        /// The Key Editor is the first dialog made responsive (its Action row is an AdaptiveTable). It must fit from the supported minimum
        /// width up: 480 design px for the real languages (the owner's screenshot at 175 % scaling was about 463), a little more for the
        /// +40 % and +80 % stress texts.
        /// </summary>
        private static void T_KeyEditorNarrow()
        {
            Section("Key Editor — fits from the supported minimum width upwards (responsive Action row)");
            var cases = new (string Name, string Code, double Pseudo, int Minimum)[]
                { ("en", "en", 0, 480), ("nl", "nl", 0, 480), ("+40%", "en", 0.4, 520), ("+80%", "en", 0.8, 580) };
            try
            {
                foreach (var (name, code, pseudo, minimum) in cases)
                {
                    Lang.Load(code);
                    Lang.PseudoExpansion = pseudo;
                    using var d = NarrowKeyEditor();
                    DevGallery.Show(d);
                    var grid = (AdaptiveTable)typeof(KeyEditorForm).GetField("_grid", NonPublic).GetValue(d);
                    var problems = new List<string>();
                    foreach (int w in new[] { minimum, 700, 1000 })      // the narrowest, a laptop at 200 %, and a wide window
                    {
                        string p = NarrowProblemAt(d, w);
                        if (p != null) problems.Add($"{w}px: {p}");
                    }
                    Assert(problems.Count == 0, $"Key Editor [{name}]: nothing cut off, sticking out or clipped from {minimum} px up {(problems.Count > 0 ? "— " + problems[0] : "")}");

                    NarrowProblemAt(d, minimum);
                    Assert(grid.CurrentVariant == grid.VariantCount - 1, $"Key Editor [{name}]: at {minimum} px the Action row uses its two-line arrangement");
                    NarrowProblemAt(d, 1000);
                    Assert(grid.CurrentVariant == 0, $"Key Editor [{name}]: at 1000 px the Action row is on one line");
                }
            }
            finally { Lang.PseudoExpansion = 0; Lang.Load("en"); }
        }
    }
}

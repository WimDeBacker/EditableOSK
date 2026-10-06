// NarrowWidthTests.cs — how narrow can each dialog get before its content no longer fits?
//
// A dialog is sized from its content and capped at the screen, so on a narrow screen (a small tablet, or 175-200 % scaling on a
// 1366-px laptop: 683-780 design pixels) it is as wide as the screen and whatever does not fit sticks out or is cut off. The dialogs
// are responsive (AdaptiveTable, wrapping text, see responsive_spec.md); these tests make the window narrower and apply the guards
// (nothing cut off, nothing overlapping, nothing sticking out, no clipped text, every target 44 px).
//
//   T_NarrowDialogs       every dialog fits from its supported minimum width upwards (English, Dutch, +40 % and +80 % text)
//   T_NarrowWidthReport   prints the narrowest width of every dialog (it only runs on request: --test NarrowWidthReport[,<dialog>])

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
        /// when everything fits: nothing cut off, nothing overlapping, nothing sticking out, no clipped text, every target big enough.
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
                Application.DoEvents();          // a responsive table asks its container for another layout once its own pass has ended
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

        /// <summary>
        /// The smallest client width (steps of 4 px, at least 280) at which everything fits, found by bisection between 280 and
        /// <paramref name="from"/>; <paramref name="why"/> names the problem one step below it. Assumes that a dialog that fits at some
        /// width fits at every larger one (T_NarrowDialogs checks that for the supported range).
        /// </summary>
        private static int NarrowestFit(FluentDialogBase d, int from, out string why)
        {
            why = "";
            if (NarrowProblemAt(d, from) is string top) { why = "does not fit even at " + from + " px: " + top; return from; }
            int lo = 280 / 4, hi = from / 4;                          // hi fits
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (NarrowProblemAt(d, mid * 4) == null) hi = mid; else lo = mid + 1;
            }
            if (hi * 4 > 280) why = NarrowProblemAt(d, hi * 4 - 4) ?? "";
            return hi * 4;
        }

        private static FluentDialogBase NarrowKeyEditor()
        {
            var groups = new List<KeyGroup> { new KeyGroup { Name = SettingsManager.StandardGroupName }, new KeyGroup { Name = "Klinkers" } };
            var props = new KeyProps("Ctrl+c", "^c", "A", "layout:azerty.kbl", "€", "€") { GroupName = "Klinkers" };
            return new KeyEditorForm(props, null, groups: groups, layoutDir: AppDomain.CurrentDomain.BaseDirectory);
        }

        /// <summary>
        /// The dialogs, and the narrowest width (design px) each must fit for English / Dutch (480: the owner's screenshot at 175 % scaling
        /// was about 463), for +40 % text, and for +80 % text. The +80 % columns are 0 where a long hint line of the dialog would have to break
        /// inside the stress text's unbreakable run of x's: not checked there.
        /// </summary>
        private static (string Name, Func<FluentDialogBase> Make, int Normal, int Plus40, int Plus80)[] NarrowDialogs() => new (string, Func<FluentDialogBase>, int, int, int)[]
        {
            ("KeyEditorForm",      NarrowKeyEditor,                                          480, 520, 580),
            ("GroupEditorForm",    () => new GroupEditorForm(SampleGroups(), "Klinkers"),    480, 520, 620),
            ("KeyboardEditorForm", () => KeyboardEditor(),                                   480, 520, 540),
            ("NewKeyboardWizard",  () => new NewKeyboardWizard(),                            480, 520, 0),
            ("SpecialKeysDialog",  () => new SpecialKeysDialog(false),                       480, 520, 0),
        };

        private static void T_NarrowDialogs()
        {
            Section("Narrow screens — every dialog fits from its supported minimum width upwards");
            try
            {
                foreach (var (name, make, normal, plus40, plus80) in NarrowDialogs())
                    foreach (var (label, code, pseudo, minimum) in new[] { ("en", "en", 0.0, normal), ("nl", "nl", 0.0, normal), ("+40%", "en", 0.4, plus40), ("+80%", "en", 0.8, plus80) })
                    {
                        if (minimum == 0) continue;
                        if (Quick && label is "nl" or "+40%") continue;      // --quick: English and the widest text only
                        Lang.Load(code);
                        Lang.PseudoExpansion = pseudo;
                        using var d = make();
                        DevGallery.Show(d);
                        var problems = new List<string>();
                        // The narrowest width; a laptop at 150 % (800) only in English: the other languages are covered at that width by
                        // CheckDialogGuards, and a dialog that fits at its minimum fits wider.
                        foreach (int w in label == "en" ? new[] { minimum, 800 } : new[] { minimum })
                        {
                            string p = NarrowProblemAt(d, w);
                            if (p != null) problems.Add($"{w}px: {p}");
                        }
                        Assert(problems.Count == 0, $"{name} [{label}]: nothing cut off, overlapping, sticking out or clipped from {minimum} px up {(problems.Count > 0 ? "— " + problems[0] : "")}");
                    }

                // The Key Editor's Action row: two lines when narrow, one line when wide (the arrangement is chosen by the width).
                Lang.Load("en");
                Lang.PseudoExpansion = 0;
                using var key = NarrowKeyEditor();
                DevGallery.Show(key);
                var grid = (AdaptiveTable)typeof(KeyEditorForm).GetField("_grid", NonPublic).GetValue(key);
                NarrowProblemAt(key, 480);
                Assert(grid.CurrentVariant == grid.VariantCount - 1, "Key Editor: at 480 px the Action row uses its two-line arrangement");
                NarrowProblemAt(key, 1000);
                Assert(grid.CurrentVariant == 0, "Key Editor: at 1000 px the Action row is on one line");
            }
            finally { Lang.PseudoExpansion = 0; Lang.Load("en"); }
        }

        private static void T_NarrowWidthReport()
        {
            Section("Narrow screens — the smallest width each dialog fits in (report)");
            // "--test NarrowWidthReport,Wizard" reports only the dialogs whose name contains one of the other terms.
            var only = _filter.Where(t => t.IndexOf("Narrow", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            try
            {
                foreach (var (name, make, _, _, _) in NarrowDialogs())
                    foreach (var (label, code, pseudo) in new[] { ("en", "en", 0.0), ("nl", "nl", 0.0), ("+40%", "en", 0.4), ("+80%", "en", 0.8) })
                    {
                        if (only.Count > 0 && !only.Any(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        Lang.Load(code);
                        Lang.PseudoExpansion = pseudo;
                        using var d = make();
                        DevGallery.Show(d);
                        int start = Math.Max(d.ClientSize.Width, 1100);
                        d.ClientSize = new Size(start, d.ClientSize.Height);
                        int w = NarrowestFit(d, start, out string why);
                        string text = why.Length > 400 ? why.Substring(0, 400) : why;
                        Console.WriteLine($"    {name,-20} {label,-5} fits down to {w} px   (next problem: {text})");
                        Assert(w > 0, $"{name} [{label}]: fits down to {w} px (report)");
                    }
            }
            finally { Lang.PseudoExpansion = 0; Lang.Load("en"); }
        }
    }
}

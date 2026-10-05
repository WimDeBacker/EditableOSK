// ResponsiveTests.cs — the responsive layout containers (responsive_spec.md): AdaptiveTable now; the others as they are built.

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_AdaptiveTable()
        {
            Section("AdaptiveTable — the first arrangement that fits, with no typed-in breakpoint");

            using var host = new Panel { Size = new Size(900, 600) };
            var table = new AdaptiveTable { ColumnCount = 3, Dock = DockStyle.Top, Margin = Padding.Empty };   // a margin would be taken off the width it may use
            var a = new Button { Text = "a", AutoSize = false, Size = new Size(120, 44), Margin = new Padding(0) };
            var b = new Button { Text = "b", AutoSize = false, Size = new Size(120, 44), Margin = new Padding(0) };
            var c = new Button { Text = "c", AutoSize = false, Size = new Size(120, 44), Margin = new Padding(0) };
            table.AddVariant(() => new[] { new AdaptiveTable.Cell(a, 0, 0), new AdaptiveTable.Cell(b, 1, 0), new AdaptiveTable.Cell(c, 2, 0) });
            table.AddVariant(() => new[] { new AdaptiveTable.Cell(a, 0, 0), new AdaptiveTable.Cell(b, 1, 0), new AdaptiveTable.Cell(c, 0, 1, 2) });
            table.AddVariant(() => new[] { new AdaptiveTable.Cell(a, 0, 0), new AdaptiveTable.Cell(b, 0, 1), new AdaptiveTable.Cell(c, 0, 2) });
            host.Controls.Add(table);
            host.CreateControl();

            // The table takes its width from the container it sits in, so the tests narrow the container.
            int At(int width) { host.Width = width; Application.DoEvents(); table.Reevaluate(); return table.CurrentVariant; }

            Assert(table.VariantCount == 3, "three arrangements registered");

            Assert(At(900) == 0, "plenty of room: the widest arrangement");
            int wide = table.GetPreferredSize(Size.Empty).Width;
            Assert(wide >= 360, $"the widest arrangement needs the three controls side by side ({wide} px)");

            // The boundary comes from the content: exactly the natural width still fits, one pixel less does not.
            Assert(At(wide) == 0, "exactly the natural width of the widest arrangement: it still fits");
            Assert(At(wide - 1) == 1, "one pixel less: the next arrangement");
            Assert(table.GetRow(c) == 1 && table.GetColumnSpan(c) == 2 && table.GetRow(a) == 0 && table.GetRow(b) == 0,
                $"second arrangement: c moved to a second line under a and b, spanning both columns (row {table.GetRow(c)}, span {table.GetColumnSpan(c)})");
            int two = table.GetPreferredSize(Size.Empty).Width;
            Assert(At(two) == 1 && At(two - 1) == 2, "the same boundary rule for the next arrangement");
            Assert(table.GetRow(a) == 0 && table.GetRow(b) == 1 && table.GetRow(c) == 2 && table.GetColumn(c) == 0,
                $"last arrangement: one control per line (rows {table.GetRow(a)},{table.GetRow(b)},{table.GetRow(c)})");
            Assert(At(10) == 2, "when nothing fits, the last arrangement is used");
            Assert(table.Controls.Count == 3, "controls are moved between cells, never duplicated or lost");

            // Height follows the arrangement.
            At(900);
            int h1 = table.GetPreferredSize(Size.Empty).Height;
            At(10);
            int h3 = table.GetPreferredSize(Size.Empty).Height;
            Assert(h3 >= h1 * 2 + 20, $"three lines are taller than one ({h3} vs {h1} px)");

            // Widening the container again goes back to the widest arrangement, with nobody calling Select.
            host.Width = 900; Application.DoEvents();
            Assert(table.CurrentVariant == 0, "container widened again: back to the widest arrangement by itself");
            host.Width = wide - 40; Application.DoEvents();
            Assert(table.CurrentVariant >= 1, "container narrowed: a narrower arrangement by itself");

            // A measurement for a given width (what the dialog does) chooses by that width.
            host.Width = wide - 1; Application.DoEvents();
            var sized = table.GetPreferredSize(new Size(wide - 1, 0));
            Assert(table.CurrentVariant == 1 && sized.Width <= wide - 1, "GetPreferredSize(width) answers for the arrangement that fits that width");
        }
    }
}

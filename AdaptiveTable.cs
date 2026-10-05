// AdaptiveTable.cs — a table that arranges the same controls in more than one way and uses the first arrangement that fits.
//
// A "variant" is a list of cells: which control goes in which column and row, and how many columns it spans. The table measures each
// variant's natural width (what the table would need if nothing limited it) and shows the first one that fits the width it is given;
// the last variant is used when none fits. No breakpoint is typed in anywhere: it follows from the width of the controls, so a longer
// translation or a larger font moves the breakpoint by itself. See responsive_spec.md, section 4.3.
//
// The columns (ColumnStyles) are shared by all variants; the rows are rebuilt for each: every row is sized by its content.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Rows and grids that wrap: the same controls in a grid of N per line, for each N in a list (widest first), as variants of an
    /// <see cref="AdaptiveTable"/>. <c>Rows(items, 3, 1)</c> is "three on one line, else one per line"; the wizard's five tiles are
    /// <c>Rows(tiles, 5, 3, 2, 1)</c>. This replaced the idea of a separate wrapping panel (responsive_spec.md, 4.2): one container does both.
    /// </summary>
    internal static class ReflowRows
    {
        /// <summary>Controls one after the other, N per line; each keeps its own size and margins (a gap is added under every line but the last).</summary>
        internal static AdaptiveTable Rows(IReadOnlyList<Control> items, params int[] perLine) => Build(items, equalColumns: false, buttons: false, gap: false, perLine);

        /// <summary>A grid whose columns are all equally wide, with a gap between the tiles (the wizard's theme tiles).</summary>
        internal static AdaptiveTable Tiles(IReadOnlyList<Control> items, params int[] perLine) => Build(items, equalColumns: true, buttons: false, gap: true, perLine);

        /// <summary>
        /// Buttons that all have the same width, the widest of them (alignment rule D23), N per line. Re-measured when a button's text
        /// changes (a language change), so the buttons stay equal.
        /// </summary>
        internal static AdaptiveTable Buttons(IReadOnlyList<Control> buttons, params int[] perLine) => Build(buttons, equalColumns: false, buttons: true, gap: true, perLine);

        private static AdaptiveTable Build(IReadOnlyList<Control> items, bool equalColumns, bool buttons, bool gap, int[] perLine)
        {
            int columns = perLine.Max();
            var table = new AdaptiveTable { ColumnCount = columns, EqualWidth = buttons };
            for (int i = 0; i < columns; i++)
                table.ColumnStyles.Add(equalColumns ? new ColumnStyle(SizeType.Percent, 100f / columns) : new ColumnStyle(SizeType.AutoSize));
            var margins = items.Select(c => c.Margin).ToList();
            if (buttons)
                foreach (var b in items)
                {
                    b.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                    if (b is Button bb) { bb.AutoSize = true; bb.AutoSizeMode = AutoSizeMode.GrowAndShrink; }   // so a shorter text can shrink the row again
                    b.TextChanged += (s, e) => { EqualiseWidths(items); table.Reevaluate(); };
                }
            foreach (int k in perLine)
            {
                int perRow = Math.Max(1, k);
                table.AddVariant(() =>
                {
                    int lines = (items.Count + perRow - 1) / perRow;
                    var cells = new List<AdaptiveTable.Cell>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        int row = i / perRow, col = i % perRow;
                        int left = gap ? 0 : margins[i].Left;
                        int right = gap ? (col < perRow - 1 && i < items.Count - 1 ? Touch.Gap : 0) : margins[i].Right;     // a gap between neighbours, none at the end
                        int bottom = row < lines - 1 ? Math.Max(margins[i].Bottom, Touch.Gap / 2) : margins[i].Bottom;
                        items[i].Margin = new Padding(left, margins[i].Top, right, bottom);
                        cells.Add(new AdaptiveTable.Cell(items[i], col, row));
                    }
                    return cells;
                }, perRow, equalColumns);
            }
            if (buttons) EqualiseWidths(items);
            return table;
        }

        /// <summary>Gives every button the width of the widest one (and at least 120).</summary>
        internal static void EqualiseWidths(IReadOnlyList<Control> buttons)
        {
            foreach (var b in buttons) b.MinimumSize = new Size(120, Touch.Target);       // forget the previous common width first
            int w = 120;
            foreach (var b in buttons) w = Math.Max(w, b.GetPreferredSize(Size.Empty).Width);
            foreach (var b in buttons) b.MinimumSize = new Size(w, Touch.Target);
        }
    }

    internal sealed class AdaptiveTable : TableLayoutPanel
    {
        /// <summary>Where one control goes in a variant.</summary>
        internal readonly struct Cell
        {
            public readonly Control Control; public readonly int Column, Row, ColumnSpan, RowSpan;
            public Cell(Control control, int column, int row, int columnSpan = 1, int rowSpan = 1)
            { Control = control; Column = column; Row = row; ColumnSpan = columnSpan; RowSpan = rowSpan; }
        }

        private readonly List<(Func<IEnumerable<Cell>> Cells, int Columns, bool EqualColumns)> _variants =
            new List<(Func<IEnumerable<Cell>>, int, bool)>();
        private int _current = -1;
        private int _decidedFor = -1;          // the width the current choice was made for
        private int[] _naturals;               // natural width of each variant; null = measure again
        private string _signature;             // the cells of the arrangement on show
        private bool _busy;                    // a decision is being made (see Select)
        private bool _relayoutQueued;          // the container will be laid out again (see RelayoutParentSoon)
        private bool _reselectQueued;          // the table will decide again (see OnChildChanged)

        /// <summary>Index of the arrangement on show (0 is the widest), or -1 before the first one is chosen.</summary>
        internal int CurrentVariant => _current;

        /// <summary>Number of arrangements.</summary>
        internal int VariantCount => _variants.Count;

        /// <summary>True for a row of buttons that must all be equally wide (checked by the alignment guard).</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool EqualWidth { get; set; }

        public AdaptiveTable()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        /// <summary>
        /// Adds an arrangement; the first is the preferred (widest) one. The function is called every time the variant is applied, so it may
        /// look at the current state of the controls (a button that is only sometimes visible).
        /// </summary>
        /// <param name="cells">Which control goes where.</param>
        /// <param name="columns">When above 0 the variant has its own number of columns (all sized to their content, or equally wide with
        /// <paramref name="equalColumns"/>); otherwise the columns the table already has are kept.</param>
        internal void AddVariant(Func<IEnumerable<Cell>> cells, int columns = 0, bool equalColumns = false) => _variants.Add((cells, columns, equalColumns));

        /// <summary>Applies the variant again (call it when something the variant looks at has changed). Nothing happens when the cells come out the same.</summary>
        internal void Rearrange() { if (_current >= 0) Apply(_current); }

        /// <summary>
        /// Shows the first variant whose natural width is at most <paramref name="available"/> (the last one if none is), and returns its index.
        /// The natural width of each variant is measured once and remembered (<see cref="Reevaluate"/> forgets it); deciding for a width is then
        /// a comparison, and the table is only rearranged when the answer is a different variant. Measuring every variant at every layout
        /// pass made the Key Editor eight times slower to build.
        /// </summary>
        internal int Select(int available)
        {
            if (_variants.Count == 0) return -1;
            if (available == _decidedFor && _current >= 0) return _current;     // nothing to decide again
            // Arranging the table lays out its container, and that raises the container's Layout event, which asks this table to decide
            // again: without this guard the decision interrupted itself (the Group Editor hung).
            if (_busy) return _current;
            _busy = true;
            try
            {
                var natural = Naturals();
                int pick = _variants.Count - 1;
                for (int i = 0; i < natural.Length - 1; i++)
                    if (natural[i] <= available) { pick = i; break; }
                if (_current != pick)
                {
                    SuspendLayout();
                    try { Apply(pick); }
                    finally { ResumeLayout(true); }
                    RelayoutParentSoon();
                }
                _decidedFor = available;
                return pick;
            }
            finally { _busy = false; }
        }

        /// <summary>The natural width of each variant (what the table would need if nothing limited it), worked out once.</summary>
        private int[] Naturals()
        {
            if (_naturals != null) return _naturals;
            var n = new int[_variants.Count];
            for (int i = 0; i < n.Length; i++) n[i] = NaturalWidth(i);
            if (_current >= 0) _variants[_current].Cells();     // a variant's function also sets margins and anchors: leave those as the arrangement on show wants them
            return _naturals = n;
        }

        /// <summary>
        /// The width an arrangement needs, worked out from the controls: each column as wide as its widest control (with its margins), a
        /// control that spans columns making up what the columns lack, equal columns as wide as the widest control. Asking the layout
        /// engine for the preferred size gave out-of-date answers right after the margins changed (the colour chips were cut off).
        /// </summary>
        private int NaturalWidth(int index)
        {
            var variant = _variants[index];
            bool shown = Visible;            // a hidden section shows every child as invisible: then count them all
            var cells = variant.Cells().Where(c => !shown || c.Control.Visible).ToList();
            int columns = Math.Max(1, variant.Columns > 0 ? variant.Columns : ColumnCount);
            var widths = new int[columns];
            var fixedWidth = new bool[columns];
            if (variant.Columns == 0)
                for (int c = 0; c < columns && c < ColumnStyles.Count; c++)
                    if (ColumnStyles[c].SizeType == SizeType.Absolute) { widths[c] = (int)ColumnStyles[c].Width; fixedWidth[c] = true; }
            int Need(Cell cell) => cell.Control.GetPreferredSize(Size.Empty).Width + cell.Control.Margin.Horizontal;
            foreach (var cell in cells.Where(c => c.ColumnSpan <= 1 && c.Column < columns && !fixedWidth[c.Column]))
                widths[cell.Column] = Math.Max(widths[cell.Column], Need(cell));
            foreach (var cell in cells.Where(c => c.ColumnSpan > 1))
            {
                int last = Math.Min(columns - 1, cell.Column + cell.ColumnSpan - 1);
                int have = 0;
                for (int c = cell.Column; c <= last; c++) have += widths[c];
                if (Need(cell) > have && !fixedWidth[last]) widths[last] += Need(cell) - have;
            }
            int total = widths.Sum() + Padding.Horizontal;
            if (variant.EqualColumns && cells.Count > 0)
                total = Math.Max(total, cells.Max(Need) * columns + Padding.Horizontal);
            return total;
        }

        /// <summary>
        /// A different arrangement has a different height, and the container has to know. The choice is usually made inside the container's own
        /// layout pass (its Layout event), where a request to lay it out again is ignored, so the container rows below stayed as high as before and
        /// cut this table off. The request is made once that pass has ended.
        /// </summary>
        private void RelayoutParentSoon()
        {
            if (_relayoutQueued || Parent == null || !IsHandleCreated) return;
            _relayoutQueued = true;
            BeginInvoke((Action)(() =>
            {
                _relayoutQueued = false;
                if (!IsDisposed && Parent != null) { Parent.PerformLayout(); Parent.Parent?.PerformLayout(); }
            }));
        }

        /// <summary>Forgets the measured widths and decides again for the current width, after something changed them (a language change, a font change).</summary>
        internal void Reevaluate()
        {
            _naturals = null;
            _decidedFor = -1;
            Select(AvailableWidth());
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            _naturals = null; _decidedFor = -1;
            // A control that appears or disappears (a button of the wizard's footer, per page) or changes its text changes how wide
            // an arrangement is: the remembered widths are out of date.
            e.Control.VisibleChanged += OnChildChanged;
            e.Control.TextChanged += OnChildChanged;
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            e.Control.VisibleChanged -= OnChildChanged;
            e.Control.TextChanged -= OnChildChanged;
        }

        // Forget the widths at once, decide again once: a language change sets the text of every control, and deciding after each of
        // them (with all the widths worked out again every time) made the dialogs several times slower to build and to translate.
        private void OnChildChanged(object sender, EventArgs e)
        {
            _naturals = null; _decidedFor = -1;
            if (_reselectQueued || !IsHandleCreated || Parent == null) return;
            _reselectQueued = true;
            BeginInvoke((Action)(() => { _reselectQueued = false; if (!IsDisposed && !_busy) Reselect(); }));
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _naturals = null; _decidedFor = -1;
        }

        // The controls change size when the form is scaled for the display (design pixels become real ones) and when the window gets its
        // handle; widths measured before that are out of date.
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            _naturals = null; _decidedFor = -1;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _naturals = null; _decidedFor = -1;
        }

        private void Apply(int index)
        {
            var variant = _variants[index];
            var cells = variant.Cells().ToList();
            string signature = index + ":" + string.Join(";", cells.Select(c => $"{c.Control.GetHashCode()},{c.Column},{c.Row},{c.ColumnSpan},{c.RowSpan},{c.Control.Parent == this}"));
            if (index == _current && signature == _signature) return;           // already arranged exactly like this
            foreach (var c in cells) if (c.Control.Parent != this) Controls.Add(c.Control);
            int rows = cells.Count == 0 ? 0 : cells.Max(c => c.Row + c.RowSpan);
            int columns = variant.Columns > 0 ? variant.Columns : ColumnCount;
            if (RowCount < rows) RowCount = rows;                          // grow first, so a cell never lies outside the table
            if (ColumnCount < columns) ColumnCount = columns;
            foreach (var c in cells)
            {
                SetCellPosition(c.Control, new TableLayoutPanelCellPosition(c.Column, c.Row));
                SetColumnSpan(c.Control, c.ColumnSpan);
                SetRowSpan(c.Control, c.RowSpan);
            }
            RowStyles.Clear();
            for (int r = 0; r < rows; r++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RowCount = rows;
            if (variant.Columns > 0)
            {
                ColumnStyles.Clear();
                for (int c = 0; c < columns; c++)
                    ColumnStyles.Add(variant.EqualColumns ? new ColumnStyle(SizeType.Percent, 100f / columns) : new ColumnStyle(SizeType.AutoSize));
                ColumnCount = columns;
            }
            _current = index;
            _signature = signature;
        }

        /// <summary>
        /// The width this table may use. The table's own width follows its content, so it cannot tell; the container does: its width minus
        /// its padding, and when the container is itself a table, minus the columns this table does not occupy (so a table in the input
        /// column of a "label | input" table is given the input column, not the whole row).
        /// </summary>
        private int AvailableWidth()
        {
            // From the top down: the visible width of the dialog's body (or the outermost container), minus what lies between it and
            // this table on the right. Taking the width from the nearest container does not work: that one is often sized by its
            // content, which is this table, so the answer depended on itself and the table flipped between arrangements.
            Control top = null;
            int chrome = Margin.Right;
            for (var a = Parent; a != null; a = a.Parent)
            {
                top = a;
                if (a is SectionHost || a is Form) break;
                chrome += a.Padding.Right + (a is TouchGroup ? 3 : 0);
            }
            if (top == null || Parent == null || top.ClientSize.Width <= 1) return int.MaxValue;      // not laid out yet: the widest arrangement
            int topRight = top.RectangleToScreen(top.ClientRectangle).Right;
            // The body of a dialog scrolls vertically, and its scroll bar appears once the content is taller than the window: that takes
            // its width from what is left after the choice was made. Keep the room for it free in every case.
            if (top is SectionHost) topRight -= SystemInformation.VerticalScrollBarWidth;
            int left = Parent.RectangleToScreen(new Rectangle(Left, 0, 0, 0)).X;
            return Math.Max(0, topRight - left - chrome);
        }

        // No choice is made here: the layout engine asks for preferred sizes at all kinds of widths while it measures, and choosing by
        // those made the arrangement jump about (and a dialog 180 px too tall). The choice follows the real width, in Select.

        // The container's Layout event covers a resize and also a change of its column widths (a longer label next to this table).
        protected override void OnParentChanged(EventArgs e)
        {
            if (Parent != null) Parent.Layout -= OnParentLaidOut;
            base.OnParentChanged(e);
            if (Parent != null) { Parent.Layout += OnParentLaidOut; Reselect(); }
        }

        private void OnParentLaidOut(object sender, LayoutEventArgs e) => Reselect();

        internal void Reselect()
        {
            if (_variants.Count == 0 || IsDisposed) return;
            Select(AvailableWidth());
        }

        /// <summary>
        /// Lets every responsive table under <paramref name="root"/> choose again for the window as it is now. The tables of a section that
        /// is not on show get no layout of their own, so without this they keep the choice made when the window was small, and the window
        /// was measured too tall for them.
        /// </summary>
        internal static void ReselectAll(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is AdaptiveTable t) t.Reselect();
                ReselectAll(c);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Parent != null) Parent.Layout -= OnParentLaidOut;
            base.Dispose(disposing);
        }
    }
}

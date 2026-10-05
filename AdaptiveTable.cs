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
    internal sealed class AdaptiveTable : TableLayoutPanel
    {
        /// <summary>Where one control goes in a variant.</summary>
        internal readonly struct Cell
        {
            public readonly Control Control; public readonly int Column, Row, ColumnSpan;
            public Cell(Control control, int column, int row, int columnSpan = 1) { Control = control; Column = column; Row = row; ColumnSpan = columnSpan; }
        }

        private readonly List<Func<IEnumerable<Cell>>> _variants = new List<Func<IEnumerable<Cell>>>();
        private int _current = -1;
        private int _decidedFor = -1;          // the width the current choice was made for
        private int[] _naturals;               // natural width of each variant; null = measure again
        private string _signature;             // the cells of the arrangement on show
        private bool _selecting;

        /// <summary>Index of the arrangement on show (0 is the widest), or -1 before the first one is chosen.</summary>
        internal int CurrentVariant => _current;

        /// <summary>Number of arrangements.</summary>
        internal int VariantCount => _variants.Count;

        public AdaptiveTable()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        /// <summary>
        /// Adds an arrangement; the first is the preferred (widest) one. The function is called every time the variant is applied, so it may
        /// look at the current state of the controls (a button that is only sometimes visible).
        /// </summary>
        internal void AddVariant(Func<IEnumerable<Cell>> cells) => _variants.Add(cells);

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
            var natural = Naturals();
            int pick = _variants.Count - 1;
            for (int i = 0; i < natural.Length - 1; i++)
                if (natural[i] <= available) { pick = i; break; }
            if (_current != pick)
            {
                SuspendLayout();
                try { Apply(pick); }
                finally { ResumeLayout(true); }
            }
            _decidedFor = available;
            return pick;
        }

        /// <summary>The natural width of each variant (what the table would need if nothing limited it), measured once.</summary>
        private int[] Naturals()
        {
            if (_naturals != null) return _naturals;
            var n = new int[_variants.Count];
            int keep = _current;
            SuspendLayout();
            try
            {
                for (int i = 0; i < n.Length; i++) { Apply(i); n[i] = base.GetPreferredSize(Size.Empty).Width; }
                if (keep >= 0) Apply(keep);
            }
            finally { ResumeLayout(true); }
            return _naturals = n;
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
            var cells = _variants[index]().ToList();
            string signature = index + ":" + string.Join(";", cells.Select(c => $"{c.Control.GetHashCode()},{c.Column},{c.Row},{c.ColumnSpan},{c.Control.Parent == this}"));
            if (index == _current && signature == _signature) return;           // already arranged exactly like this
            _selecting = true;
            try
            {
                foreach (var c in cells) if (c.Control.Parent != this) Controls.Add(c.Control);
                int rows = cells.Count == 0 ? 0 : cells.Max(c => c.Row) + 1;
                if (RowCount < rows) RowCount = rows;                      // grow first, so a cell never lies outside the table
                foreach (var c in cells)
                {
                    SetCellPosition(c.Control, new TableLayoutPanelCellPosition(c.Column, c.Row));
                    SetColumnSpan(c.Control, c.ColumnSpan);
                    SetRowSpan(c.Control, 1);
                }
                RowStyles.Clear();
                for (int r = 0; r < rows; r++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
                RowCount = rows;
                _current = index;
                _signature = signature;
            }
            finally { _selecting = false; }
        }

        /// <summary>
        /// The width this table may use. The table's own width follows its content, so it cannot tell; the container does: its width minus
        /// its padding, and when the container is itself a table, minus the columns this table does not occupy (so a table in the input
        /// column of a "label | input" table is given the input column, not the whole row).
        /// </summary>
        private int AvailableWidth()
        {
            var p = Parent;
            if (p == null) return int.MaxValue;
            int width = p.ClientSize.Width - p.Padding.Horizontal;
            if (p is TableLayoutPanel outer)
            {
                var pos = outer.GetPositionFromControl(this);
                var widths = outer.GetColumnWidths();
                if (pos.Column >= 0 && widths.Length > 0)
                {
                    int span = Math.Max(1, outer.GetColumnSpan(this));
                    for (int c = 0; c < widths.Length; c++)
                        if (c < pos.Column || c >= pos.Column + span) width -= widths[c];
                }
            }
            return Math.Max(0, width - Margin.Horizontal);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            // A measurement for a given width chooses by that width. An unconstrained one (the layout engine asks that all the time)
            // measures what is on show: choosing the widest arrangement there would undo a narrow choice at every layout pass.
            if (!_selecting && proposedSize.Width > 0 && proposedSize.Width < int.MaxValue / 2)
                Select(proposedSize.Width - Margin.Horizontal);
            return base.GetPreferredSize(proposedSize);
        }

        // The container's Layout event covers a resize and also a change of its column widths (a longer label next to this table).
        protected override void OnParentChanged(EventArgs e)
        {
            if (Parent != null) Parent.Layout -= OnParentLaidOut;
            base.OnParentChanged(e);
            if (Parent != null) { Parent.Layout += OnParentLaidOut; Reselect(); }
        }

        private void OnParentLaidOut(object sender, LayoutEventArgs e) => Reselect();

        private void Reselect()
        {
            if (_variants.Count == 0 || IsDisposed) return;
            Select(AvailableWidth());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Parent != null) Parent.Layout -= OnParentLaidOut;
            base.Dispose(disposing);
        }
    }
}

// WizardControls.cs — the two controls the New Keyboard Wizard needs that no other dialog has:
//   TouchTile         a selectable card (a radio button that looks like a tile): sample colours, a name, a check mark and a thick ring
//   WizardGridPreview the grid of keys with the labels typed so far (scrolls instead of shrinking the text)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    // ════════════════════════════════════════════════════════════════════
    //  TouchTile
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A <see cref="TouchRadioButton"/> drawn as a tile: up to three sample colours on top, the name underneath. The chosen
    /// tile has a 3 px ring and a check mark, so the choice never rests on colour alone. Tiles that share a parent are one
    /// group (one tab stop, arrow keys move the choice) — the behaviour comes from <see cref="TouchRadioButton"/>.
    /// </summary>
    internal sealed class TouchTile : TouchRadioButton
    {
        private bool _hover;

        /// <summary>The sample colours painted at the top (none: an ellipsis is drawn instead, for "From file…").</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color[] Swatches { get; set; } = new Color[0];

        public TouchTile()
        {
            Padding     = new Padding(2);
            MinimumSize = new Size(Touch.Target * 2, 112);
            TextAlign   = ContentAlignment.BottomCenter;
            AutoSize    = false;      // a tile is sized by its container (equal tiles in a row)
        }

        // As wide as the longest word of the name needs (a long name may wrap between words, never inside one): this is the width the
        // responsive grid of tiles looks at when it decides how many tiles fit on a line.
        public override Size GetPreferredSize(Size proposedSize)
        {
            int widest = 0;
            foreach (var word in (Text ?? "").Split(' ')) widest = Math.Max(widest, Touch.TextWidth(word, Font));
            return new Size(Math.Max(MinimumSize.Width, widest + Padding.Horizontal + 16), MinimumSize.Height);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Parent?.BackColor ?? Fluent.BgPage);
            bool hc   = SystemInformation.HighContrast;
            bool dark = FluentPainter.IsDarkSurface(Parent?.BackColor ?? Fluent.BgPage);

            // Like a neutral button: the tile keeps dark text on its light fill in both themes.
            Color fill   = hc ? SystemColors.Control : _hover ? Color.FromArgb(225, 225, 225) : Fluent.Neutral;
            Color border = hc ? SystemColors.ControlText : _hover ? Fluent.ControlBorderHover : Fluent.ControlBorder;
            Color text   = hc ? SystemColors.ControlText : Fluent.TextPrimary;
            using (var path = Fluent.RoundedRectF(Fluent.CrispBorderRect(Width, Height), Fluent.RadiusBtn))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border))      g.DrawPath(p, path);
            }

            Color sel = hc ? SystemColors.Highlight : dark ? Fluent.DialogDarkText : Fluent.Accent;
            if (Checked)
            {
                using (var path = Fluent.RoundedRectF(new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), Math.Max(1, Fluent.RadiusBtn - 1)))
                using (var p = new Pen(sel, 3f)) g.DrawPath(p, path);
            }

            // Sample colours: up to three small keys, centred above the name.
            int lineH = TextRenderer.MeasureText("Ag", Font).Height, nameH = lineH * 2;     // the name may wrap onto a second line
            int top = 12, keyH = Math.Max(16, Height - top - nameH - 18), keyW = 26, gap = 4;
            if (Swatches.Length > 0)
            {
                int total = Swatches.Length * keyW + (Swatches.Length - 1) * gap;
                int x = (Width - total) / 2;
                foreach (var c in Swatches)
                {
                    var r = new Rectangle(x, top, keyW, keyH);
                    using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
                    using (var p = new Pen(Color.FromArgb(120, 0, 0, 0))) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                    x += keyW + gap;
                }
            }
            else
                TextRenderer.DrawText(g, "…", Touch.StepperFont, new Rectangle(0, top, Width, keyH), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(Padding.Left, Height - nameH - 8, Math.Max(0, Width - Padding.Horizontal), nameH), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | Touch.PrefixFlag(ShowKeyboardCues));

            if (Checked)
                TextRenderer.DrawText(g, "✓", Touch.StepperFont, new Rectangle(Width - 30, 4, 26, 26), sel,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused)
            {
                if (hc) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 9, Height - 9));
                else FluentPainter.DrawRoundedRing(g, Width, Height, Fluent.RadiusBtn, 3f, 2f, dark ? Fluent.DialogDarkText : Fluent.Accent);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  WizardGridPreview
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The grid the wizard is about to create: <c>GridRows</c> rows, <c>GridCols</c> columns plus the reserved gear column at
    /// the top right, with the key labels typed so far. Keys keep a minimum width; a wide grid scrolls sideways instead of
    /// shrinking the text. Drawn in neutral dialog colours (the theme of the new keyboard is chosen on a later page).
    /// </summary>
    internal sealed class WizardGridPreview : ScrollableControl
    {
        internal const int MinKeyWidth = 56;
        internal const int KeyHeight   = 36;
        internal const int VisibleRows = 3;

        private string[][] _labels = new string[0][];

        public int GridRows { get; private set; } = 1;
        public int GridCols { get; private set; } = 1;

        /// <summary>The label of the key at (<paramref name="row"/>, <paramref name="col"/>) as drawn; "" for a blank key, "⚙" for the gear.</summary>
        public string LabelAt(int row, int col)
        {
            if (row == 0 && col == GridCols) return "⚙";
            return row < _labels.Length && col < _labels[row].Length ? _labels[row][col] ?? "" : "";
        }

        public WizardGridPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop        = true;
            AutoScroll     = true;
            Font           = Fluent.FontLabel;
            AccessibleRole = AccessibleRole.Table;
            // A table row takes the control's own height (not its preferred size), so the height is fixed here: always the same room, whatever the grid.
            MinimumSize    = new Size(0, VisibleRows * KeyHeight + 2 + SystemInformation.HorizontalScrollBarHeight);
            Height         = MinimumSize.Height;
            Refill(1, 1, null);
        }

        /// <summary>Shows a blank grid of the given size (the gear column is added).</summary>
        public void ShowBlank(int rows, int cols) => Refill(rows, cols, null);

        /// <summary>Shows the keys parsed from the pasted text; the size follows the longest row.</summary>
        internal void ShowParsed(List<List<WizardKeyParser.KeySpec>> rows)
        {
            if (rows == null || rows.Count == 0) { Refill(1, 1, null); return; }
            int cols = 0;
            foreach (var r in rows) cols = Math.Max(cols, r.Count);
            var labels = new string[rows.Count][];
            for (int r = 0; r < rows.Count; r++)
            {
                labels[r] = new string[rows[r].Count];
                for (int c = 0; c < rows[r].Count; c++) labels[r][c] = rows[r][c].IsBlank ? "" : rows[r][c].Label;
            }
            Refill(rows.Count, Math.Max(1, cols), labels);
        }

        private void Refill(int rows, int cols, string[][] labels)
        {
            GridRows = Math.Max(1, rows); GridCols = Math.Max(1, cols);
            _labels  = labels ?? new string[0][];
            AccessibleName        = Lang.T("wiz: Preview");
            AccessibleDescription = string.Format(Lang.T("wiz: preview description"), GridRows, GridCols + 1);
            AutoScrollMinSize = new Size((GridCols + 1) * MinKeyWidth + 2, GridRows * KeyHeight + 2);
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize) =>
            new Size(proposedSize.Width > 0 ? proposedSize.Width : (GridCols + 1) * MinKeyWidth + 2,
                     VisibleRows * KeyHeight + 2 + SystemInformation.HorizontalScrollBarHeight);

        protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // The preview can be scrolled with the arrow keys when it has the focus.
        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int dx = e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Left ? -1 : 0;
            int dy = e.KeyCode == Keys.Down  ? 1 : e.KeyCode == Keys.Up   ? -1 : 0;
            if (dx != 0 || dy != 0)
            {
                AutoScrollPosition = new Point(-AutoScrollPosition.X + dx * MinKeyWidth, -AutoScrollPosition.Y + dy * KeyHeight);
                Invalidate();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool dark = FluentPainter.IsDarkSurface(Parent?.BackColor ?? Fluent.BgPage);
            bool hc   = SystemInformation.HighContrast;
            Color keyFill  = hc ? SystemColors.Window : dark ? Color.FromArgb(58, 58, 58) : Fluent.BgInput;
            Color gearFill = hc ? SystemColors.Control : dark ? Color.FromArgb(44, 44, 44) : Fluent.Neutral;
            Color line     = hc ? SystemColors.ControlText : dark ? Color.FromArgb(150, 150, 150) : Fluent.ControlBorder;
            Color text     = hc ? SystemColors.ControlText : dark ? Fluent.DialogDarkText : Fluent.TextPrimary;
            g.Clear(Parent?.BackColor ?? Fluent.BgPage);

            int cols = GridCols + 1;
            int keyW = Math.Max(MinKeyWidth, (ClientSize.Width - 2) / cols);
            var origin = AutoScrollPosition;
            for (int r = 0; r < GridRows; r++)
                for (int c = 0; c < cols; c++)
                {
                    bool gear = r == 0 && c == cols - 1;
                    var rect = new Rectangle(1 + c * keyW + origin.X, 1 + r * KeyHeight + origin.Y, keyW - 1, KeyHeight - 1);
                    if (rect.Right < 0 || rect.Left > ClientSize.Width || rect.Bottom < 0 || rect.Top > ClientSize.Height) continue;
                    using (var b = new SolidBrush(gear ? gearFill : keyFill)) g.FillRectangle(b, rect);
                    using (var p = new Pen(line)) g.DrawRectangle(p, rect);
                    string label = LabelAt(r, c);
                    if (label.Length > 0)
                        TextRenderer.DrawText(g, label, Font, Rectangle.Inflate(rect, -3, 0), text,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }

            if (Focused)
                using (var p = new Pen(hc ? SystemColors.Highlight : dark ? Fluent.DialogDarkText : Fluent.Accent, 2f))
                    g.DrawRectangle(p, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        }
    }
}

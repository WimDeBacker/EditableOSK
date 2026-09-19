// TouchChoiceButton.cs — a touch-friendly replacement for the drop-down list.
//
// A classic ComboBox is a small box with a tiny arrow, and its list rows are cramped. This is a
// large button (at least 44 px) that shows the current choice; tapping it opens a flyout whose rows
// are 44 px (a plain list) or 56 px (name + one-line description, and the reason when a choice is
// not available here).
//
// A list can be far taller than the screen (all installed fonts), so the flyout copes:
//   • its height is limited to the room above or below the button, and to a number of rows;
//   • two 44 px arrow strips scroll it (press and hold repeats) — for people who cannot use a
//     mouse wheel or a touch drag; the wheel and the keyboard scroll it as well;
//   • a search box at the top (Searchable) filters the list as you type.
//
//   Keyboard  Enter / Space / Alt+Down open the flyout; Up / Down change the choice without
//             opening it; in the flyout Up / Down / PageUp / PageDown move, Enter picks, Esc closes.
//   Touch     tap the button, tap a row; tapping outside closes the flyout.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>One row of a <see cref="TouchChoiceButton"/>.</summary>
    internal sealed class TouchChoice
    {
        public string Text;
        /// <summary>One line under the name that says what the choice does (optional).</summary>
        public string Description;
        public bool   Enabled = true;
        /// <summary>Shown instead of the description when the choice is not available.</summary>
        public string DisabledReason;
        public override string ToString() => Text;
    }

    /// <summary>A button that shows the current choice and opens a big-row flyout to change it.</summary>
    internal sealed class TouchChoiceButton : FluentButton
    {
        private int _selected = -1;
        private ChoicePopup _popup;
        private bool _blankText;

        public List<TouchChoice> Items { get; } = new List<TouchChoice>();

        /// <summary>Raised when the user (or code) picks another choice.</summary>
        public event EventHandler SelectedIndexChanged;

        /// <summary>Row height of the flyout in design pixels: 56 for name + description rows, 44 for a plain list.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int RowHeight { get; set; } = 56;

        /// <summary>Adds a search box to the flyout; use it for long lists.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Searchable { get; set; }

        /// <summary>Most rows shown at once before the flyout scrolls.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int MaxVisibleRows { get; set; } = 8;

        public TouchChoiceButton()
        {
            Style          = FluentButton.Variant.Neutral;
            TabStop        = true;
            AutoSize       = true;
            AutoSizeMode   = AutoSizeMode.GrowAndShrink;
            MinimumSize    = new Size(150, Touch.Target);
            Padding        = new Padding(14, 0, 14, 0);
            Font           = Fluent.FontLabel;
            AccessibleRole = AccessibleRole.ComboBox;
        }

        /// <summary>The base paints the button shape and focus ring with an empty text; the text and arrow are drawn here.</summary>
        public override string Text
        {
            get => _blankText ? "" : base.Text;
            set => base.Text = value;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var s = base.GetPreferredSize(proposedSize);
            return new Size(s.Width + 34, Math.Max(s.Height, Touch.Target));   // room for the arrow
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _blankText = true;
            try { base.OnPaint(e); } finally { _blankText = false; }

            bool hc = SystemInformation.HighContrast;
            Color fg = !Enabled ? (hc ? SystemColors.GrayText : Fluent.TextHint)
                                : (hc ? SystemColors.ControlText : Fluent.TextPrimary);
            var r = ClientRectangle;
            TextRenderer.DrawText(e.Graphics, base.Text, Font, new Rectangle(14, 0, Math.Max(0, r.Width - 14 - 34), r.Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics, "▾", Font, new Rectangle(r.Width - 34, 0, 26, r.Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (value < 0 || value >= Items.Count || value == _selected || !Items[value].Enabled) return;
                _selected = value;
                ShowSelection();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public TouchChoice SelectedItem => _selected >= 0 && _selected < Items.Count ? Items[_selected] : null;

        /// <summary>Call after filling <see cref="Items"/> (and again when an item's text changes).</summary>
        public void ShowSelection()
        {
            var item = SelectedItem;
            if (item == null) return;
            Text = item.Text;
            AccessibleName = item.Text;      // the Text setter derived it already; kept explicit
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            OpenPopup();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && e.Alt || e.KeyCode == Keys.F4) { OpenPopup(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                int dir = e.KeyCode == Keys.Down ? 1 : -1;
                for (int i = _selected + dir; i >= 0 && i < Items.Count; i += dir)
                    if (Items[i].Enabled) { SelectedIndex = i; break; }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        /// <summary>
        /// Opens the flyout under the button, or above it when there is more room there. Its height
        /// never exceeds the room on that side of the button (<paramref name="maxHeightOverride"/>
        /// lets the gallery show what a small screen does).
        /// </summary>
        internal ChoicePopup OpenPopup(bool keepOpen = false, int? maxHeightOverride = null)
        {
            if (_popup != null && !_popup.IsDisposed) return _popup;
            float scale = DeviceDpi / 96f;
            var wa    = Screen.FromControl(this).WorkingArea;
            var below = PointToScreen(new Point(0, Height + 2));
            var above = PointToScreen(Point.Empty);
            int roomBelow = wa.Bottom - below.Y - 8;
            int roomAbove = above.Y - wa.Top - 8;
            int maxH = maxHeightOverride ?? Math.Max(roomBelow, roomAbove);

            var p = new ChoicePopup(Items, _selected, scale, Math.Max(Width, (int)(400 * scale)),
                                    RowHeight, Searchable, maxH, MaxVisibleRows) { KeepOpen = keepOpen };
            p.Chosen += i => SelectedIndex = i;
            p.FormClosed += (s, e) => { _popup = null; if (!IsDisposed) Focus(); };

            bool fitsBelow = p.Height <= roomBelow;
            int y = fitsBelow || roomBelow >= roomAbove ? below.Y : above.Y - p.Height - 2;
            p.Location = new Point(Math.Max(wa.Left, Math.Min(below.X, wa.Right - p.Width)), Math.Max(wa.Top, y));
            _popup = p;
            p.Show(FindForm());
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _popup != null && !_popup.IsDisposed) _popup.Close();
            base.Dispose(disposing);
        }
    }

    /// <summary>The flyout: a borderless window with owner-drawn rows, optional search box and scroll strips.</summary>
    internal sealed class ChoicePopup : Form
    {
        private readonly IReadOnlyList<TouchChoice> _items;
        private readonly List<int> _view = new List<int>();          // item indexes that pass the filter
        private readonly int  _rowH, _stripH, _searchH;
        private readonly bool _dark, _scrollable;
        private readonly TouchTextBox _search;
        private readonly Timer _repeat = new Timer();
        private Rectangle _viewport, _up, _down;
        private int  _selected, _hot = -1, _top, _repeatDir;
        private bool _stripPressed;

        public event Action<int> Chosen;

        /// <summary>The gallery keeps the flyout open to photograph it; normally it closes when it loses focus.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool KeepOpen { get; set; }

        public ChoicePopup(IReadOnlyList<TouchChoice> items, int selected, float scale, int width,
                           int rowHeight, bool searchable, int maxHeight, int maxRows)
        {
            _items    = items;
            _selected = selected;
            _rowH     = (int)Math.Round(rowHeight * scale);
            _stripH   = (int)Math.Round(Touch.Target * scale);
            _searchH  = searchable ? _stripH : 0;
            _dark     = !ToolbarButton.IsLightTheme;
            for (int i = 0; i < items.Count; i++) _view.Add(i);

            // Height: the rows, capped by the room available and by MaxVisibleRows; more rows than that scroll.
            int fixedH  = 2 + _searchH;
            int avail   = Math.Max(_rowH * 2, maxHeight - fixedH);
            int content = items.Count * _rowH;
            int viewH;
            if (content <= Math.Min(avail, maxRows * _rowH)) { viewH = content; _scrollable = false; }
            else
            {
                _scrollable = true;
                viewH = Math.Max(_rowH * 2, Math.Min(avail - 2 * _stripH, maxRows * _rowH));
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.Manual;
            ShowInTaskbar   = false;
            TopMost         = true;
            DoubleBuffered  = true;
            Font            = Fluent.FontLabel;
            ClientSize      = new Size(width, fixedH + (_scrollable ? 2 * _stripH : 0) + viewH);

            int y = 1;
            if (searchable)
            {
                _search = new TouchTextBox
                {
                    Bounds = new Rectangle(1, 1, width - 2, _searchH), MinimumSize = Size.Empty,
                    BackColor = _dark ? Fluent.DialogDarkInput : Fluent.BgInput,
                    ForeColor = _dark ? Fluent.DialogDarkText  : Fluent.TextPrimary,
                    AccessibleName = "Search", Hint = "Type to search…",
                };
                _search.TextChanged += (s, e) => ApplyFilter();
                _search.KeyDown += (s, e) => { if (HandleKey(e)) e.Handled = e.SuppressKeyPress = true; };
                Controls.Add(_search);
                y += _searchH;
            }
            if (_scrollable) { _up = new Rectangle(1, y, width - 2, _stripH); y += _stripH; }
            _viewport = new Rectangle(1, y, width - 2, viewH);
            y += viewH;
            if (_scrollable) _down = new Rectangle(1, y, width - 2, _stripH);

            int pos = _view.IndexOf(selected);
            _hot = pos;
            if (pos >= 0) _top = Math.Max(0, Math.Min(MaxTop, pos * _rowH - viewH / 2 + _rowH / 2));   // selected row in the middle

            _repeat.Tick += (s, e) => { _repeat.Interval = 90; ScrollBy(_repeatDir * _rowH); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }   // CS_DROPSHADOW
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _search?.Focus();
        }

        // ── Scrolling and filtering ──────────────────────────────────────

        private int MaxTop => Math.Max(0, _view.Count * _rowH - _viewport.Height);

        private void ScrollBy(int pixels)
        {
            int t = Math.Max(0, Math.Min(MaxTop, _top + pixels));
            if (t != _top) { _top = t; Invalidate(); }
        }

        private void EnsureVisible(int pos)
        {
            int rowTop = pos * _rowH;
            if (rowTop < _top) _top = rowTop;
            else if (rowTop + _rowH > _top + _viewport.Height) _top = rowTop + _rowH - _viewport.Height;
            _top = Math.Max(0, Math.Min(MaxTop, _top));
        }

        private void ApplyFilter()
        {
            string q = _search?.Text.Trim() ?? "";
            _view.Clear();
            for (int i = 0; i < _items.Count; i++)
                if (q.Length == 0 || _items[i].Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) _view.Add(i);
            _top = 0;
            _hot = _view.FindIndex(i => _items[i].Enabled);
            Invalidate();
        }

        // ── Painting ─────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g  = e.Graphics;
            bool hc = SystemInformation.HighContrast;
            // AAA: fg and dim (descriptions) are text at >= 7 : 1; off is for a choice that is not available
            // (disabled text is exempt from contrast rules, but stays readable).
            Color bg  = hc ? SystemColors.Window     : _dark ? Fluent.DialogDarkCard    : Fluent.BgCard;
            Color fg  = hc ? SystemColors.WindowText : _dark ? Fluent.DialogDarkText    : Fluent.TextPrimary;
            Color dim = hc ? SystemColors.WindowText : _dark ? Fluent.DialogDarkTextDim : Fluent.TextHint;
            Color off = hc ? SystemColors.GrayText   : _dark ? Fluent.DialogDarkBorder  : Fluent.ControlBorder;
            Color hot = hc ? SystemColors.Highlight  : Fluent.Accent;
            Color hotText = hc ? SystemColors.HighlightText : Color.White;
            g.Clear(bg);

            var saved = g.Save();
            g.SetClip(_viewport);
            for (int p = 0; p < _view.Count; p++)
            {
                int y = _viewport.Y + p * _rowH - _top;
                if (y + _rowH < _viewport.Y || y > _viewport.Bottom) continue;
                var item = _items[_view[p]];
                var r = new Rectangle(_viewport.X, y, _viewport.Width, _rowH);
                bool isHot = p == _hot && item.Enabled;
                if (isHot) using (var b = new SolidBrush(hot)) g.FillRectangle(b, r);
                Color nameColor = !item.Enabled ? off : isHot ? hotText : fg;
                Color descColor = !item.Enabled ? off : isHot ? hotText : dim;

                if (_view[p] == _selected)
                    TextRenderer.DrawText(g, "✓", Fluent.FontBtnLg, new Rectangle(r.X + 10, r.Y, 26, r.Height), nameColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                int textW = r.Width - 44 - 12;
                const TextFormatFlags oneLine = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                string second = item.Enabled ? item.Description : item.DisabledReason;
                if (string.IsNullOrEmpty(second))
                {
                    // Plain list row: one line, vertically centred.
                    TextRenderer.DrawText(g, item.Text, Fluent.FontLabel, new Rectangle(r.X + 44, r.Y, textW, r.Height), nameColor,
                        oneLine | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    TextRenderer.DrawText(g, item.Text, Fluent.FontBtnLg,
                        new Rectangle(r.X + 44, r.Y + 6, textW, Fluent.FontBtnLg.Height), nameColor, oneLine);
                    TextRenderer.DrawText(g, second, Fluent.FontHint,
                        new Rectangle(r.X + 44, r.Y + 6 + Fluent.FontBtnLg.Height + 2, textW, Fluent.FontHint.Height), descColor, oneLine);
                }
            }
            g.Restore(saved);

            if (_view.Count == 0)
                TextRenderer.DrawText(g, "No matches", Fluent.FontLabel, _viewport.IsEmpty ? ClientRectangle : _viewport, dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (_scrollable)
            {
                DrawStrip(g, _up,   "▲", _top > 0,      bg, fg, off);
                DrawStrip(g, _down, "▼", _top < MaxTop, bg, fg, off);
            }
            using (var pen = new Pen(hc ? SystemColors.WindowText : _dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder))
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private void DrawStrip(Graphics g, Rectangle r, string glyph, bool active, Color bg, Color fg, Color dim)
        {
            Color fill = SystemInformation.HighContrast ? SystemColors.Control : _dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(240, 240, 240);
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            TextRenderer.DrawText(g, glyph, Fluent.FontLabel, r, active ? fg : dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // ── Mouse ────────────────────────────────────────────────────────

        private int PosAt(Point p)
        {
            if (!_viewport.Contains(p)) return -1;
            int pos = (p.Y - _viewport.Y + _top) / _rowH;
            return pos >= 0 && pos < _view.Count ? pos : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int pos = PosAt(e.Location);
            if (pos != _hot) { _hot = pos; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_scrollable) return;
            int dir = _up.Contains(e.Location) ? -1 : _down.Contains(e.Location) ? 1 : 0;
            if (dir == 0) return;
            _stripPressed = true;
            ScrollBy(dir * _rowH * 2);
            _repeatDir = dir;
            _repeat.Interval = 400;             // pause, so a plain tap scrolls once
            _repeat.Start();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _repeat.Stop();
            if (_stripPressed) { _stripPressed = false; return; }
            Pick(PosAt(e.Location));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * _rowH * 3);
        }

        // ── Keyboard ─────────────────────────────────────────────────────

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (HandleKey(e)) e.Handled = true;
        }

        private bool HandleKey(KeyEventArgs e)
        {
            int page = Math.Max(1, _viewport.Height / _rowH);
            switch (e.KeyCode)
            {
                case Keys.Escape:   Close(); return true;
                case Keys.Down:     MoveHot(+1);    return true;
                case Keys.Up:       MoveHot(-1);    return true;
                case Keys.PageDown: MoveHot(+page); return true;
                case Keys.PageUp:   MoveHot(-page); return true;
                case Keys.Enter:    Pick(_hot);     return true;
                case Keys.Space when _search == null: Pick(_hot); return true;
                case Keys.Home when _search == null:  MoveHot(-_view.Count); return true;
                case Keys.End  when _search == null:  MoveHot(+_view.Count); return true;
            }
            return false;
        }

        /// <summary>Moves the highlight by <paramref name="delta"/> rows to the nearest available row.</summary>
        private void MoveHot(int delta)
        {
            if (_view.Count == 0) return;
            int dir    = delta >= 0 ? 1 : -1;
            int start  = _hot < 0 ? (dir > 0 ? -1 : _view.Count) : _hot;
            int target = Math.Max(0, Math.Min(_view.Count - 1, start + delta));
            // The first available row at or beyond the target in the direction of travel; none = stay.
            for (int i = target; i >= 0 && i < _view.Count; i += dir)
                if (_items[_view[i]].Enabled) { _hot = i; EnsureVisible(i); Invalidate(); return; }
        }

        private void Pick(int pos)
        {
            if (pos < 0 || pos >= _view.Count || !_items[_view[pos]].Enabled) return;   // a disabled row explains itself and stays open
            Chosen?.Invoke(_view[pos]);
            Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!KeepOpen && !IsDisposed) Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _repeat.Dispose();
            base.Dispose(disposing);
        }
    }
}

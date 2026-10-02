// TouchColorPicker.cs — touch-friendly colour choosing.
//
// Windows' standard ColorDialog (what the editors use now) has no hex box: its custom-colour area
// takes Hue / Sat / Lum or Red / Green / Blue numbers. The newer Windows colour picker with a hex
// field exists only in WinUI, not in WinForms. So this is a small flyout of our own:
//
//   • a palette of 44 px swatches (one tap picks a colour and closes the flyout),
//   • a hex box for an exact colour (applies while typing),
//   • "More colours…" which opens the standard Windows dialog for anything else.
//
// A ColorChip is the button that opens it: a 44 px chip filled with the colour and labelled with
// its role ("Font", "Key", "Border"), so a whole row of colours needs no hex text at all.

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>A 44 px chip filled with a colour and labelled with what the colour is for; opens a <see cref="ColorFlyout"/>.</summary>
    internal sealed class ColorChip : ColorSwatchButton
    {
        private readonly string _caption;
        private ColorFlyout _flyout;

        /// <summary>Raised when the user picks another colour.</summary>
        public event EventHandler ValueChanged;

        public ColorChip(string caption, Color initial)
        {
            _caption    = caption;
            Text        = caption;
            Font        = Fluent.FontLabel;
            Tag         = "notheme";                       // the dialog theme must not repaint the text colour
            AutoSize     = true;                           // grows with a long caption (another language)
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding      = new Padding(14, 0, 14, 0);
            MinimumSize  = new Size(104, Touch.Target);
            Margin       = new Padding(0, 0, Touch.Gap, 0);
            Value        = initial;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Value
        {
            get => BackColor;
            set
            {
                BackColor = value;
                double lum = (0.299 * value.R + 0.587 * value.G + 0.114 * value.B) / 255.0;
                ForeColor = lum > 0.55 ? Color.Black : Color.White;      // the label stays readable on any colour
                UpdateAccessibleName();
                Invalidate();
            }
        }

        // ── Inheriting the colour from a parent (a group inherits from the standard group) ──

        /// <summary>
        /// Text of the flyout's "inherit" button. When set, the flyout offers to hand the colour back to the parent
        /// instead of choosing one (null = no such button: the colour is always a concrete one).
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string InheritText { get; set; }

        /// <summary>
        /// True when the colour is not set here but taken from the parent. <see cref="Value"/> then holds the colour
        /// that is inherited, so the chip shows what the key will actually look like; a dashed outline marks it.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Inherited
        {
            get => _inherited;
            private set { if (_inherited == value) return; _inherited = value; UpdateAccessibleName(); Invalidate(); }
        }
        private bool _inherited;

        /// <summary>Sets the chip to "inherited", showing <paramref name="inheritedColor"/> (the parent's colour). Raises nothing.</summary>
        public void SetInherited(Color inheritedColor)
        {
            Inherited = true;
            Value = inheritedColor;
        }

        /// <summary>Sets an own colour (no longer inherited). Raises nothing.</summary>
        public void SetOwn(Color color)
        {
            Inherited = false;
            Value = color;
        }

        private void UpdateAccessibleName()
        {
            string hex = SettingsManager.Hex(BackColor);
            AccessibleName = _inherited ? $"{_caption} color {hex} ({Lang.T("inherited")})" : $"{_caption} color {hex}";
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            OpenPicker();
        }

        private bool _hovered;
        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        /// <summary>
        /// Paints the chip like the neutral buttons: the same rounded shape and the same one-pixel outline (a
        /// chip is a control whatever colour it holds, e.g. a white chip on a white dialog), the colour as its fill.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;   // pixel centres at .5, as in FluentPainter
            g.Clear(Parent?.BackColor ?? Fluent.BgPage);

            bool hc = SystemInformation.HighContrast;
            Color border = hc ? SystemColors.ControlText
                         : !ToolbarButton.IsLightTheme ? Fluent.DialogDarkBorder
                         : _hovered ? Fluent.ControlBorderHover : Fluent.ControlBorder;
            using (var path = Fluent.RoundedRectF(Fluent.CrispBorderRect(Width, Height), Fluent.RadiusBtn))
            {
                using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
                // An inherited colour has a dashed outline: it is shown, but it is not set on this item.
                using (var pen = new Pen(border) { DashStyle = _inherited ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid })
                    g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            // Two-tone focus ring, visible on any chip colour (WCAG 2.4.7).
            if (Focused)
            {
                // Rounded like the chip itself; a dark 1 px line outside a white 2 px line shows on any chip colour.
                FluentPainter.DrawRoundedRing(g, Width, Height, Fluent.RadiusBtn, 2.5f, 1f, Color.FromArgb(30, 30, 30));
                FluentPainter.DrawRoundedRing(g, Width, Height, Fluent.RadiusBtn, 4f,   2f, Color.White);
            }
        }

        /// <summary>Opens the flyout under the chip (above it when there is no room below).</summary>
        internal ColorFlyout OpenPicker(bool keepOpen = false)
        {
            if (_flyout != null && !_flyout.IsDisposed) return _flyout;
            var f = new ColorFlyout(Value, DeviceDpi / 96f, InheritText) { KeepOpen = keepOpen };
            f.Picked += c => { SetOwn(c); ValueChanged?.Invoke(this, EventArgs.Empty); };
            f.InheritChosen += () => { Inherited = true; ValueChanged?.Invoke(this, EventArgs.Empty); };
            f.FormClosed += (s, e) => { _flyout = null; if (!IsDisposed) Focus(); };

            var wa    = Screen.FromControl(this).WorkingArea;
            var below = PointToScreen(new Point(0, Height + 2));
            int y = below.Y + f.Height > wa.Bottom ? PointToScreen(Point.Empty).Y - f.Height - 2 : below.Y;
            f.Location = new Point(Math.Max(wa.Left, Math.Min(below.X, wa.Right - f.Width)), Math.Max(wa.Top, y));
            _flyout = f;
            f.Show(FindForm());
            return f;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _flyout != null && !_flyout.IsDisposed) _flyout.Close();
            base.Dispose(disposing);
        }
    }

    /// <summary>The colour flyout: palette, hex box and a button to the standard Windows colour dialog.</summary>
    internal sealed class ColorFlyout : Form
    {
        private static readonly Color[] Palette =
        {
            Hex("000000"), Hex("595959"), Hex("808080"), Hex("C0C0C0"), Hex("E6E6E6"), Hex("FFFFFF"),
            Hex("C42B1C"), Hex("F7630C"), Hex("FFD700"), Hex("107C10"), Hex("038387"), Hex("0078D4"),
            Hex("8E1B12"), Hex("C86A00"), Hex("F0E68C"), Hex("2A6B35"), Hex("005B70"), Hex("1A4E8A"),
            Hex("5B3080"), Hex("8764B8"), Hex("E3008C"), Hex("8E562E"), Hex("1C1C28"), Hex("4A8FD4"),
        };
        private const int Cols = 6;

        private readonly PaletteControl _palette;
        private readonly TouchTextBox   _hex;
        private readonly FluentButton   _more;
        private readonly Rectangle      _previewRect;
        private readonly bool           _dark;
        private Color _current;
        private bool  _dialogOpen;

        /// <summary>Raised when a colour is chosen (also while a valid hex is being typed).</summary>
        public event Action<Color> Picked;

        /// <summary>The gallery keeps the flyout open to photograph it; normally it closes when it loses focus.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool KeepOpen { get; set; }

        private static Color Hex(string rgb) => SettingsManager.ParseColor(rgb, Color.Black);

        /// <summary>Raised when the user chooses to inherit the colour instead of picking one (only offered when an inherit text is given).</summary>
        public event Action InheritChosen;

        private readonly FluentButton _inherit;

        public ColorFlyout(Color current, float scale, string inheritText = null)
        {
            _current = current;
            _dark    = !ToolbarButton.IsLightTheme;
            int cell = (int)Math.Round(Touch.Target * scale);
            int gap  = (int)Math.Round(6 * scale);
            int pad  = (int)Math.Round(12 * scale);
            int rows = Palette.Length / Cols;
            int gridW = Cols * cell + (Cols - 1) * gap;
            int gridH = rows * cell + (rows - 1) * gap;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.Manual;
            ShowInTaskbar   = false;
            TopMost         = true;
            DoubleBuffered  = true;
            Font            = Fluent.FontLabel;
            BackColor       = SystemInformation.HighContrast ? SystemColors.Window : _dark ? Fluent.DialogDarkCard : Fluent.BgCard;

            _palette = new PaletteControl(Palette, cell, gap, Cols) { Bounds = new Rectangle(pad, pad, gridW, gridH), Selected = current };
            _palette.Picked += c => { Apply(c, fromHexBox: false); Close(); };

            int y = pad + gridH + pad;
            _hex = new TouchTextBox
            {
                Bounds = new Rectangle(pad, y, gridW - cell - gap, cell), MinimumSize = Size.Empty,
                Font = Fluent.FontCourier, Text = "#" + SettingsManager.Hex(current), AccessibleName = Lang.T("Hex color"),
                BackColor = _dark ? Fluent.DialogDarkInput : Fluent.BgInput,
                ForeColor = _dark ? Fluent.DialogDarkText  : Fluent.TextPrimary,
            };
            _hex.TextChanged += (s, e) =>
            {
                var c = SettingsManager.ParseColor(_hex.Text, Color.Empty);
                bool ok = !c.IsEmpty;
                // An invalid code turns red (>= 7 : 1 on either theme); the swatch next to it also stops updating.
                _hex.ForeColor = ok ? (_dark ? Fluent.DialogDarkText : Fluent.TextPrimary)
                                    : (_dark ? Fluent.DialogDarkDanger : Fluent.Danger);
                if (ok) Apply(c, fromHexBox: true);
            };
            _hex.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Close(); e.Handled = e.SuppressKeyPress = true; } };
            _previewRect = new Rectangle(pad + gridW - cell, y, cell, cell);

            y += cell + gap;
            _more = new FluentButton
            {
                Text = Lang.T("More colours…"), Style = FluentButton.Variant.Neutral, TabStop = true,
                Bounds = new Rectangle(pad, y, gridW, cell),
            };
            _more.Click += (s, e) => MoreColours();
            y += cell;

            if (!string.IsNullOrEmpty(inheritText))
            {
                // Hands the colour back to the parent (e.g. the standard group) instead of setting one here.
                y += gap;
                _inherit = new FluentButton
                {
                    Text = inheritText, Style = FluentButton.Variant.Neutral, TabStop = true,
                    Bounds = new Rectangle(pad, y, gridW, cell),
                };
                _inherit.Click += (s, e) => { InheritChosen?.Invoke(); Close(); };
                y += cell;
            }

            ClientSize = new Size(gridW + 2 * pad, y + pad);
            Controls.Add(_palette);
            Controls.Add(_hex);
            Controls.Add(_more);
            if (_inherit != null) Controls.Add(_inherit);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }   // CS_DROPSHADOW
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _palette.Focus();
        }

        private void Apply(Color c, bool fromHexBox)
        {
            _current = c;
            _palette.Selected = c;
            _palette.Invalidate();
            Invalidate();
            Picked?.Invoke(c);
        }

        /// <summary>The standard Windows colour dialog, for a colour the palette and hex box do not cover.</summary>
        private void MoreColours()
        {
            _dialogOpen = true;      // the flyout loses focus to the dialog; that must not close it
            try
            {
                using var dlg = new ColorDialog { Color = _current, FullOpen = true };
                if (dlg.ShowDialog(this) == DialogResult.OK) { Apply(dlg.Color, fromHexBox: false); Close(); }
            }
            finally { _dialogOpen = false; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var b = new SolidBrush(_current)) e.Graphics.FillRectangle(b, _previewRect);
            using (var pen = new Pen(_dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder))
            {
                e.Graphics.DrawRectangle(pen, _previewRect.X, _previewRect.Y, _previewRect.Width - 1, _previewRect.Height - 1);
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!KeepOpen && !_dialogOpen && !IsDisposed) Close();
        }

        /// <summary>The grid of colour swatches; a real, focusable control so the keyboard works (arrows + Enter).</summary>
        private sealed class PaletteControl : Control
        {
            private readonly Color[] _colors;
            private readonly int _cell, _gap, _cols;
            private int _focus;
            private Color _selected;

            /// <summary>The chosen colour; the keyboard cursor starts on it.</summary>
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color Selected
            {
                get => _selected;
                set
                {
                    _selected = value;
                    int i = Array.FindIndex(_colors, c => c.ToArgb() == value.ToArgb());
                    if (i >= 0) _focus = i;
                }
            }
            public event Action<Color> Picked;

            public PaletteControl(Color[] colors, int cell, int gap, int cols)
            {
                _colors = colors; _cell = cell; _gap = gap; _cols = cols;
                SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer, true);
                TabStop = true;
                AccessibleName = Lang.T("Colour palette");
            }

            private Rectangle CellRect(int i) =>
                new Rectangle((i % _cols) * (_cell + _gap), (i / _cols) * (_cell + _gap), _cell, _cell);

            protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? BackColor);
                for (int i = 0; i < _colors.Length; i++)
                {
                    var r = CellRect(i);
                    using (var b = new SolidBrush(_colors[i])) g.FillRectangle(b, r);
                    // A real boundary (3 : 1), so a white swatch on a white flyout is still a visible target.
                    using (var pen = new Pen(ToolbarButton.IsLightTheme ? Fluent.ControlBorder : Fluent.DialogDarkBorder))
                        g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                    var state = g.Save();
                    g.TranslateTransform(r.X, r.Y);
                    if (_colors[i].ToArgb() == Selected.ToArgb())
                    {
                        // Two-tone ring: a white band with a dark line inside it, visible on both light and dark swatches.
                        // (Fluent.DrawSquareRing: equally wide on all four sides.)
                        Fluent.DrawSquareRing(g, r.Width, r.Height, inset: 3, penWidth: 3, Color.White);
                        Fluent.DrawSquareRing(g, r.Width, r.Height, inset: 6, penWidth: 1, Color.FromArgb(30, 30, 30));
                    }
                    if (Focused && i == _focus)
                        Fluent.DrawSquareRing(g, r.Width, r.Height, inset: 0, penWidth: 3, Fluent.Accent);
                    g.Restore(state);
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                for (int i = 0; i < _colors.Length; i++)
                    if (CellRect(i).Contains(e.Location)) { _focus = i; Picked?.Invoke(_colors[i]); return; }
            }

            protected override bool IsInputKey(Keys keyData) =>
                keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                int f = _focus;
                switch (e.KeyCode)
                {
                    case Keys.Left:  f = Math.Max(0, f - 1); break;
                    case Keys.Right: f = Math.Min(_colors.Length - 1, f + 1); break;
                    case Keys.Up:    f = Math.Max(0, f - _cols); break;
                    case Keys.Down:  f = Math.Min(_colors.Length - 1, f + _cols); break;
                    case Keys.Enter:
                    case Keys.Space: Picked?.Invoke(_colors[_focus]); e.Handled = true; return;
                    default: return;
                }
                _focus = f;
                Invalidate();
                e.Handled = true;
            }
        }
    }
}

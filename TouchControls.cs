// TouchControls.cs — the touch-friendly building blocks of the editor dialogs.
//
// Why these exist
//   The on-screen keyboard is meant for people with a motor disability, so every clickable
//   control in the editors must be a comfortable target: at least 44 x 44 design pixels
//   (WCAG 2.5.5, "Target Size"). The stock WinForms TextBox / ComboBox / NumericUpDown take
//   their height from the font (about 26–30 px) and NumericUpDown's spin arrows are only a few
//   pixels wide, so they are wrapped or replaced here:
//
//     TouchTextBox   a real TextBox (same type, same API) drawn 44 px tall with the text
//                    vertically centred
//     TouchComboBox  a real ComboBox, owner-drawn so it and its list rows are 44 px tall
//     TouchCheckBox  a real CheckBox whose whole 44 px row is the click target
//     TouchStepper   "[-] value [+]" with two 44 px buttons, replaces NumericUpDown
//     SectionBar     large segmented buttons that switch between the sections of a dialog
//
//   Sizes are in design pixels (96 dpi). The dialogs use AutoScaleMode.Dpi, so they grow with
//   the display scaling automatically.

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>Sizing constants shared by the touch-friendly controls and their layouts.</summary>
    internal static class Touch
    {
        /// <summary>Minimum width and height of a pointer target, in design pixels (WCAG 2.5.5).</summary>
        public const int Target = 44;

        /// <summary>Space between neighbouring controls, in design pixels.</summary>
        public const int Gap = 8;

        /// <summary>
        /// Widest a field label may grow before it wraps onto a second line. Keeps a long
        /// translation from stretching the label column of a whole dialog.
        /// </summary>
        public const int LabelMaxWidth = 260;

        /// <summary>Narrowest text or combo input, so a section is never squeezed to nothing.</summary>
        public const int InputMinWidth = 240;

        /// <summary>The kinds of control the user clicks, taps or types into (checked by the size guard).</summary>
        public static bool IsPointerControl(Control c) =>
            c is ButtonBase || c is ComboBox || c is TextBoxBase || c is NumericUpDown || c is TrackBar;

        /// <summary>Font of the "-" and "+" glyphs on a stepper button.</summary>
        internal static readonly Font StepperFont = new Font(Fluent.FontLabel.FontFamily, 16f, FontStyle.Bold);
    }

    // ════════════════════════════════════════════════════════════════════
    //  TouchTextBox
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A <see cref="TextBox"/> that is at least 44 px tall with its text vertically centred.
    /// A single-line TextBox cannot be made taller than its font allows, so this is a
    /// multi-line box locked to one line: Enter is left to the dialog's default button, pasted
    /// line breaks become spaces, and the text is centred by setting the edit control's
    /// formatting rectangle (EM_SETRECT).
    /// </summary>
    internal class TouchTextBox : TextBox
    {
        private const int EM_SETRECT = 0x00B3;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

        /// <summary>Removes the clip region of a device context (the edit control clips its painting to its text rectangle).</summary>
        [DllImport("gdi32.dll")]
        private static extern int SelectClipRgn(IntPtr hdc, IntPtr hrgn);

        private bool _stripping;

        public TouchTextBox()
        {
            Multiline     = true;
            WordWrap      = false;
            AcceptsReturn = false;
            AcceptsTab    = false;
            ScrollBars    = ScrollBars.None;
            BorderStyle   = BorderStyle.None;      // the border is drawn in PaintOverlay, in the same grey as the buttons
            Font          = Fluent.FontInput;
            MinimumSize   = new Size(0, Touch.Target);
            Height        = Touch.Target;
        }

        protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        private const int WM_SIZE = 0x0005;

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); CenterText(); }
        protected override void OnSizeChanged(EventArgs e)   { base.OnSizeChanged(e);   CenterText(); }
        protected override void OnFontChanged(EventArgs e)   { base.OnFontChanged(e);   CenterText(); }

        /// <summary>
        /// The edit control resets its text rectangle when it is resized, and that happens after
        /// the managed size events, so the rectangle is set again once WM_SIZE has been handled.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_SIZE) CenterText();
            else if (m.Msg == WM_PAINT) PaintOverlay(IntPtr.Zero);
            else if (m.Msg == WM_PRINTCLIENT) PaintOverlay(m.WParam);
        }

        private const int WM_PAINT = 0x000F;
        private const int WM_PRINTCLIENT = 0x0318;    // DrawToBitmap / screenshots paint through this message

        /// <summary>Grey text shown while the box is empty (a multi-line TextBox has no PlaceholderText of its own).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Hint { get; set; }

        /// <summary>
        /// Draws what the edit control does not: the border (one pixel wide, the same grey as the buttons, so a text
        /// box and a button look alike; a 2 px focus ring while it has the keyboard focus) and the hint text.
        /// </summary>
        private void PaintOverlay(IntPtr hdc)
        {
            // When printing (DrawToBitmap) the edit control's device context is clipped to its text rectangle,
            // which would hide the border, so the clip is removed first.
            if (hdc != IntPtr.Zero) SelectClipRgn(hdc, IntPtr.Zero);
            using var g = hdc == IntPtr.Zero ? CreateGraphics() : Graphics.FromHdc(hdc);
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;   // pixel centres at .5, as in FluentPainter
            bool hc = SystemInformation.HighContrast;
            using (var pen = new Pen(hc ? SystemColors.WindowText : ToolbarButton.IsLightTheme ? Fluent.ControlBorder : Fluent.DialogDarkBorder))
                g.DrawRectangle(pen, 0.5f, 0.5f, Width - 1, Height - 1);
            if (Focused)
            {
                // The ring must stand out from the input's own background: the accent blue on light, a light grey on dark.
                Color ring = hc ? SystemColors.Highlight : ToolbarButton.IsLightTheme ? Fluent.Accent : Fluent.DialogDarkText;
                using var pen = new Pen(ring, 2f);
                g.DrawRectangle(pen, 1f, 1f, Width - 2, Height - 2);
            }
            if (Text.Length == 0 && !string.IsNullOrEmpty(Hint)) PaintHint(g);
        }

        private void PaintHint(Graphics g)
        {
            TextRenderer.DrawText(g, Hint, Font, new Rectangle(6, 0, Math.Max(0, ClientSize.Width - 12), ClientSize.Height),
                SystemInformation.HighContrast ? SystemColors.GrayText
                    : ToolbarButton.IsLightTheme ? Fluent.TextHint : Fluent.DialogDarkTextDim,   // >= 7 : 1 on either theme
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        /// <summary>Insets the edit control's text rectangle so a single line sits in the middle.</summary>
        private void CenterText()
        {
            if (!IsHandleCreated) return;
            int textH = TextRenderer.MeasureText("Ag", Font).Height;
            int top   = Math.Max(0, (ClientSize.Height - textH) / 2);
            var r = new RECT { Left = 6, Top = top, Right = Math.Max(6, ClientSize.Width - 6), Bottom = ClientSize.Height };
            SendMessage(Handle, EM_SETRECT, IntPtr.Zero, ref r);
        }

        /// <summary>Keeps the box single-line: line breaks (typed or pasted) become spaces.</summary>
        protected override void OnTextChanged(EventArgs e)
        {
            if (!_stripping && Text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                _stripping = true;
                int caret = SelectionStart;
                Text = Text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
                SelectionStart = Math.Min(caret, Text.Length);
                _stripping = false;
                return;   // the nested assignment already raised TextChanged with the clean text
            }
            base.OnTextChanged(e);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TouchComboBox
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A drop-down list <see cref="ComboBox"/> that is at least 44 px tall, and whose list rows
    /// are big enough to tap. Owner-drawn: a ComboBox's height follows its item height, and the
    /// drawing has to honour the dialog's dark / light / high-contrast colours itself.
    /// </summary>
    internal class TouchComboBox : ComboBox
    {
        public TouchComboBox()
        {
            DropDownStyle  = ComboBoxStyle.DropDownList;
            DrawMode       = DrawMode.OwnerDrawFixed;
            FlatStyle      = FlatStyle.Flat;
            IntegralHeight = false;
            Font           = Fluent.FontInput;
            ItemHeight     = Touch.Target - 8;
            MinimumSize    = new Size(0, Touch.Target);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnsureTouchHeight();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (IsHandleCreated) EnsureTouchHeight();
        }

        /// <summary>Grows the item height until the closed box reaches the (DPI-scaled) target size.</summary>
        private void EnsureTouchHeight()
        {
            int min = (int)Math.Round(Touch.Target * DeviceDpi / 96.0);
            for (int guard = 0; Height < min && guard < 60; guard++) ItemHeight++;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool hc      = SystemInformation.HighContrast;
            bool inList  = (e.State & DrawItemState.ComboBoxEdit) == 0;
            bool hot     = inList && (e.State & DrawItemState.Selected) != 0;
            Color bg = hot ? (hc ? SystemColors.Highlight     : Fluent.Accent)
                           : (hc ? SystemColors.Window        : BackColor);
            Color fg = hot ? (hc ? SystemColors.HighlightText : Color.White)
                           : (hc ? SystemColors.WindowText    : ForeColor);

            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
            {
                var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (!inList && (e.State & DrawItemState.Focus) != 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), fg, bg);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TouchCheckBox
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A <see cref="CheckBox"/> that is 44 px tall. The whole control — not just the small tick
    /// box — toggles it, so the click target is the full row.
    /// </summary>
    public class TouchCheckBox : CheckBox
    {
        private const int GlyphSize = 24;          // the tick box itself, in design pixels
        private const int GlyphLeft = 14;
        private bool _hovered;

        public TouchCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize    = true;
            MinimumSize = new Size(Touch.Target, Touch.Target);
            TextAlign   = ContentAlignment.MiddleLeft;
            Font        = Fluent.FontLabel;
            // The text starts after the tick box; the guards measure the text against the client area minus this padding.
            Padding     = new Padding(GlyphLeft + GlyphSize + 10, 0, 16, 0);
            Cursor      = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int textW = string.IsNullOrEmpty(Text) ? 0
                : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            return new Size(Padding.Horizontal + textW, Math.Max(Touch.Target, MinimumSize.Height));
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e)   { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e)  { Invalidate(); base.OnLostFocus(e); }

        /// <summary>
        /// Drawn as one button-shaped 44 px target (same shape and outline as every other button) holding a large
        /// tick box, so the whole row visibly is the control. The state is shown by a tick, not only by colour.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;   // pixel centres at .5, as in FluentPainter
            g.Clear(Parent?.BackColor ?? Fluent.BgPage);
            bool hc = SystemInformation.HighContrast;

            Color fill   = hc ? SystemColors.Control : _hovered ? Color.FromArgb(225, 225, 225) : Fluent.Neutral;
            Color border = hc ? SystemColors.ControlText : _hovered ? Fluent.ControlBorderHover : Fluent.ControlBorder;
            using (var path = Fluent.RoundedRectF(Fluent.CrispBorderRect(Width, Height), Fluent.RadiusBtn))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border))      g.DrawPath(p, path);
                if (!Enabled) using (var wash = new SolidBrush(Color.FromArgb(100, 255, 255, 255))) g.FillPath(wash, path);
            }

            // The tick box.
            int y = (Height - GlyphSize) / 2;
            var box = new Rectangle(GlyphLeft, y, GlyphSize, GlyphSize);
            using (var path = Fluent.RoundedRectF(new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), 4))
            {
                Color boxFill = Checked ? (hc ? SystemColors.Highlight : Fluent.Accent) : (hc ? SystemColors.Window : Color.White);
                using (var b = new SolidBrush(boxFill)) g.FillPath(b, path);
                using (var p = new Pen(hc ? SystemColors.ControlText : Fluent.ControlBorderHover, 2f)) g.DrawPath(p, path);
            }
            if (Checked)
                using (var tick = new Pen(hc ? SystemColors.HighlightText : Color.White, 3f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                    g.DrawLines(tick, new[] { new Point(box.X + 6, box.Y + 12), new Point(box.X + 10, box.Y + 17), new Point(box.X + 18, box.Y + 7) });

            TextRenderer.DrawText(g, Text, Font, new Rectangle(Padding.Left, 0, Math.Max(0, Width - Padding.Horizontal), Height),
                hc ? SystemColors.ControlText : Enabled ? Fluent.TextPrimary : Fluent.TextHint,
                // NoPadding, as when the width was measured in GetPreferredSize: otherwise the text is a few pixels too
                // wide for its rectangle and gets an ellipsis ("A…").
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            if (Focused)
            {
                if (hc) ControlPaint.DrawFocusRectangle(g, new Rectangle(2, 2, Width - 5, Height - 5));
                else FluentPainter.DrawRoundedRing(g, Width, Height, Fluent.RadiusBtn, 3f, 2f, Fluent.Accent);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TouchStepper
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A whole-number spinner made of a "-" button, an editable value and a "+" button, each
    /// 44 px, replacing <see cref="NumericUpDown"/> (whose arrow buttons are a few pixels wide).
    /// The API mirrors the parts of NumericUpDown the dialogs use (Value, Minimum, Maximum,
    /// Increment, ValueChanged). Holding a button repeats. Keyboard: the value box is the only
    /// Tab stop; Up / Down step it.
    /// </summary>
    internal sealed class TouchStepper : UserControl
    {
        private readonly FluentButton  _dec = new FluentButton();
        private readonly FluentButton  _inc = new FluentButton();
        private readonly TouchTextBox  _txt = new TouchTextBox();
        private readonly Timer         _repeat = new Timer();

        private decimal _min, _max = 100, _step = 1, _value;
        private bool    _syncing, _mouseStep;
        private int     _repeatDir;

        /// <summary>Raised whenever <see cref="Value"/> changes, including while typing a valid number.</summary>
        public event EventHandler ValueChanged;

        public TouchStepper()
        {
            AutoScaleMode = AutoScaleMode.None;
            TabStop       = false;
            Size          = new Size(Touch.Target * 4, Touch.Target);
            MinimumSize   = new Size(Touch.Target * 3, Touch.Target);

            foreach (var b in new[] { _dec, _inc })
            {
                b.Style    = FluentButton.Variant.Neutral;
                b.Font     = Touch.StepperFont;
                b.TabStop  = false;
                b.Size     = new Size(Touch.Target, Touch.Target);
                Controls.Add(b);
            }
            _dec.Text = "−";   // minus sign
            _inc.Text = "+";

            _txt.TextAlign = HorizontalAlignment.Center;
            _txt.TabStop   = true;
            Controls.Add(_txt);

            _dec.MouseDown += (s, e) => BeginRepeat(e, -1);
            _inc.MouseDown += (s, e) => BeginRepeat(e, +1);
            _dec.MouseUp   += (s, e) => EndRepeat(_dec, e);
            _inc.MouseUp   += (s, e) => EndRepeat(_inc, e);
            // Click also fires after a mouse press; only a keyboard click (Space/Enter) steps here.
            _dec.Click += (s, e) => { if (_mouseStep) _mouseStep = false; else Step(-1); };
            _inc.Click += (s, e) => { if (_mouseStep) _mouseStep = false; else Step(+1); };

            _txt.TextChanged += (s, e) => OnTyped();
            _txt.Leave       += (s, e) => SyncText();
            _txt.KeyDown     += (s, e) =>
            {
                if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
                Step(e.KeyCode == Keys.Up ? +1 : -1);
                e.Handled = e.SuppressKeyPress = true;
            };

            _repeat.Tick += (s, e) => { _repeat.Interval = 80; Step(_repeatDir); };

            SyncText();
            UpdateButtonNames();
        }

        // ── Value model (mirrors NumericUpDown) ─────────────────────────

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Minimum
        {
            get => _min;
            set { _min = value; if (_max < _min) _max = _min; Value = _value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Maximum
        {
            get => _max;
            set { _max = value; if (_min > _max) _min = _max; Value = _value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Increment { get => _step; set => _step = value <= 0 ? 1 : value; }

        /// <summary>The current value, always within Minimum..Maximum (out-of-range values are clamped).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Value
        {
            get => _value;
            set
            {
                decimal v = Math.Max(_min, Math.Min(_max, value));
                bool changed = v != _value;
                _value = v;
                SyncText();
                if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Moves the value one increment down (-1) or up (+1), stopping at the limits.</summary>
        internal void Step(int direction) => Value += direction * _step;

        // ── Accessibility ───────────────────────────────────────────────

        /// <summary>
        /// Accessible name of the whole stepper. Also names the value box, and the buttons as
        /// "Decrease X" / "Increase X" so a screen reader says which value they change.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new string AccessibleName
        {
            get => base.AccessibleName;
            set { base.AccessibleName = value; _txt.AccessibleName = value; UpdateButtonNames(); }
        }

        private void UpdateButtonNames()
        {
            string what = string.IsNullOrEmpty(base.AccessibleName) ? "" : " " + base.AccessibleName;
            _dec.AccessibleName = Lang.T("Decrease") + what;
            _inc.AccessibleName = Lang.T("Increase") + what;
        }

        internal FluentButton DecreaseButton => _dec;
        internal FluentButton IncreaseButton => _inc;
        internal TextBox      ValueBox       => _txt;

        // ── Text <-> value ──────────────────────────────────────────────

        private void SyncText()
        {
            string s = ((long)_value).ToString(CultureInfo.InvariantCulture);
            if (_txt.Text == s) return;
            _syncing = true;
            _txt.Text = s;
            _syncing = false;
        }

        /// <summary>A number typed into the box takes effect immediately when it is valid.</summary>
        private void OnTyped()
        {
            if (_syncing) return;
            if (!decimal.TryParse(_txt.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal v)) return;
            if (v < _min || v > _max || v == _value) return;
            _value = v;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Press-and-hold repeat ───────────────────────────────────────

        private void BeginRepeat(MouseEventArgs e, int dir)
        {
            if (e.Button != MouseButtons.Left) return;
            _mouseStep = true;
            Step(dir);
            _repeatDir = dir;
            _repeat.Interval = 450;   // pause before repeating, so a plain tap steps once
            _repeat.Start();
        }

        private void EndRepeat(Control button, MouseEventArgs e)
        {
            _repeat.Stop();
            // Released outside the button: no Click follows, so do not swallow the next keyboard click.
            if (!button.ClientRectangle.Contains(e.Location)) _mouseStep = false;
        }

        // ── Layout ──────────────────────────────────────────────────────

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = Height;
            _dec.SetBounds(0, 0, h, h);
            _inc.SetBounds(Width - h, 0, h, h);
            _txt.SetBounds(h + 4, 0, Math.Max(0, Width - 2 * h - 8), h);
        }

        public override Size GetPreferredSize(Size proposedSize) => new Size(Math.Max(Width, MinimumSize.Width), Touch.Target);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _repeat.Dispose();
            base.Dispose(disposing);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SectionBar
    // ════════════════════════════════════════════════════════════════════

    /// <summary>One segment of a <see cref="SectionBar"/>: a button that arrow keys can move between.</summary>
    internal sealed class SectionTab : FluentButton
    {
        public SectionTab()
        {
            AutoSize     = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize  = new Size(96, Touch.Target);
            Padding      = new Padding(14, 0, 14, 0);
            Margin       = new Padding(0, 0, 6, 6);
            Font         = Fluent.FontLabel;
            TabStop      = false;
            AccessibleRole = AccessibleRole.PageTab;
        }

        /// <summary>Arrow keys navigate the bar, so they must reach KeyDown instead of moving focus.</summary>
        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End
            || base.IsInputKey(keyData);
    }

    /// <summary>
    /// A row of large segmented buttons, one per section of a dialog. Exactly one is selected;
    /// only that one is a Tab stop, and Left / Right / Home / End move between them (Ctrl+Tab
    /// works from anywhere in the dialog). A section can be flagged with a warning marker when
    /// something in it needs attention, e.g. a validation error on a section that is not visible.
    /// The bar wraps onto more lines when a long translation does not fit.
    /// </summary>
    public sealed class SectionBar : FlowLayoutPanel
    {
        private readonly List<SectionTab>  _tabs   = new List<SectionTab>();
        private readonly List<Func<string>> _titles = new List<Func<string>>();
        private readonly List<bool>        _errors = new List<bool>();

        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex { get; private set; } = -1;
        public int Count => _tabs.Count;
        internal IReadOnlyList<SectionTab> Tabs => _tabs;

        public SectionBar()
        {
            AutoSize       = true;
            AutoSizeMode   = AutoSizeMode.GrowAndShrink;
            WrapContents   = true;
            FlowDirection  = FlowDirection.LeftToRight;
            Margin         = new Padding(0, 0, 0, Touch.Gap);
            AccessibleRole = AccessibleRole.PageTabList;
        }

        /// <summary>Adds a section button; the title is re-read on <see cref="RefreshTitles"/> (language change).</summary>
        public int Add(Func<string> title)
        {
            int index = _tabs.Count;
            var tab = new SectionTab();
            tab.Click   += (s, e) => Select(index, focus: false);
            tab.KeyDown += (s, e) => OnTabKey(index, e);
            _tabs.Add(tab); _titles.Add(title); _errors.Add(false);
            Controls.Add(tab);
            ApplyLook(index);
            if (SelectedIndex < 0) Select(0, focus: false);
            return index;
        }

        /// <summary>Selects section <paramref name="index"/>; optionally moves keyboard focus to its button.</summary>
        public void Select(int index, bool focus)
        {
            if (index < 0 || index >= _tabs.Count) return;
            bool changed = index != SelectedIndex;
            int  old = SelectedIndex;
            SelectedIndex = index;
            if (old >= 0) ApplyLook(old);
            ApplyLook(index);
            if (focus) _tabs[index].Focus();
            if (changed) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Selects the next (+1) or previous (-1) section, wrapping around.</summary>
        public void SelectNext(int direction, bool focus)
        {
            if (_tabs.Count == 0) return;
            Select((SelectedIndex + direction + _tabs.Count) % _tabs.Count, focus);
        }

        /// <summary>Shows or clears the warning marker on a section's button.</summary>
        public void SetError(int index, bool hasError)
        {
            if (index < 0 || index >= _tabs.Count || _errors[index] == hasError) return;
            _errors[index] = hasError;
            ApplyLook(index);
        }

        public bool HasError(int index) => index >= 0 && index < _errors.Count && _errors[index];

        /// <summary>Re-reads every title, e.g. after the language changed.</summary>
        public void RefreshTitles()
        {
            for (int i = 0; i < _tabs.Count; i++) ApplyLook(i);
        }

        private void OnTabKey(int index, KeyEventArgs e)
        {
            int target = -1;
            switch (e.KeyCode)
            {
                case Keys.Left:  target = (index + _tabs.Count - 1) % _tabs.Count; break;
                case Keys.Right: target = (index + 1) % _tabs.Count;               break;
                case Keys.Home:  target = 0;                                       break;
                case Keys.End:   target = _tabs.Count - 1;                         break;
            }
            if (target < 0) return;
            Select(target, focus: true);
            e.Handled = e.SuppressKeyPress = true;
        }

        private void ApplyLook(int i)
        {
            var tab = _tabs[i];
            string title = _titles[i]();
            tab.Text = _errors[i] ? title + "  ⚠" : title;
            tab.AccessibleName = Lang.StripMnemonic(title)
                               + (_errors[i] ? ", " + Lang.T("contains an error") : "");
            tab.Style   = i == SelectedIndex ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            tab.TabStop = i == SelectedIndex;     // roving tab stop: one Tab stop for the whole bar
            tab.Invalidate();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SectionHost
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Holds the sections of a dialog and shows one at a time. Its preferred size is that of the
    /// largest section, so the window fits every section and does not resize when switching.
    /// Scrolls only when even that does not fit the screen.
    /// </summary>
    internal sealed class SectionHost : Panel
    {
        private readonly List<Control> _sections = new List<Control>();

        public SectionHost() { AutoScroll = true; }

        public int SectionCount => _sections.Count;
        public Control SectionAt(int index) => _sections[index];

        public void Add(Control section)
        {
            section.Dock    = DockStyle.Top;
            section.Visible = _sections.Count == 0;
            _sections.Add(section);
            Controls.Add(section);
        }

        public void ShowSection(int index)
        {
            for (int i = 0; i < _sections.Count; i++) _sections[i].Visible = i == index;
            AutoScrollPosition = Point.Empty;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = 0, h = 0;
            foreach (var s in _sections)
            {
                var p = s.GetPreferredSize(new Size(Math.Max(0, proposedSize.Width - Padding.Horizontal), 0));
                w = Math.Max(w, p.Width);
                h = Math.Max(h, p.Height);
            }
            return new Size(w + Padding.Horizontal, h + Padding.Vertical);
        }
    }
}

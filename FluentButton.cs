using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    // ════════════════════════════════════════════════════════════════════
    //  FluentButton — light-theme owner-drawn button for modal dialogs.
    // ════════════════════════════════════════════════════════════════════
    public class FluentButton : Button
    {
        public enum Variant { Primary, Danger, Success, Neutral }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Variant Style        { get; set; } = Variant.Primary;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string  IconGlyph    { get; set; } = "";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int     CornerRadius { get; set; } = Fluent.RadiusBtn;

        private bool _hovered, _pressed;

        public FluentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TabStop = false;
            Cursor  = Cursors.Hand;
            Font    = Fluent.FontBtnLg;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* suppress — OnPaint owns the full surface */ }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Resolve the parent background so rounded corners blend in correctly.
            Color parentBg = Parent?.BackColor ?? Fluent.BgPage;
            FluentPainter.PaintLight(e.Graphics, ClientRectangle, Text, IconGlyph,
                Font, Style, _hovered, _pressed, Enabled, CornerRadius, parentBg);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ToolbarButton — dark-theme owner-drawn button for the main toolbar.
    //  Extends NoActivateButton so clicking it never steals focus.
    // ════════════════════════════════════════════════════════════════════
    public class ToolbarButton : NoActivateButton
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IconGlyph { get; set; } = "";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool   IsActive  { get; set; } = false;

        private bool _hovered, _pressed;

        public ToolbarButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TabStop = false;
            Cursor  = Cursors.Hand;
            Font    = Fluent.FontBtnTb;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* suppress — OnPaint owns the full surface */ }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color parentBg = Parent?.BackColor ?? Fluent.DarkBg;
            FluentPainter.PaintDark(e.Graphics, ClientRectangle, Text, IconGlyph,
                Font, IsActive, _hovered, _pressed, Enabled, Fluent.RadiusBtn, parentBg);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  FluentPainter — shared owner-draw routines.
    // ════════════════════════════════════════════════════════════════════
    internal static class FluentPainter
    {
        // ── Light theme (dialogs) ─────────────────────────────────────
        internal static void PaintLight(
            Graphics g, Rectangle r, string text, string icon,
            Font font, FluentButton.Variant style,
            bool hovered, bool pressed, bool enabled, int radius,
            Color parentBg = default)
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

            // Clear the whole control rect to the parent background so rounded
            // corners don't leave stale or mismatched paint behind.
            g.Clear(parentBg == default ? Fluent.BgPage : parentBg);

            // Compute background color
            Color baseBg = style switch
            {
                FluentButton.Variant.Danger  => Fluent.Danger,
                FluentButton.Variant.Success => Fluent.Success,
                FluentButton.Variant.Neutral => Fluent.Neutral,
                _                            => Fluent.Accent,
            };

            Color bg = pressed ? Darken(baseBg, 0.12f)
                     : hovered ? Darken(baseBg, 0.07f)
                     :           baseBg;

            var paint = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
            using var path = Fluent.RoundedRect(paint, radius);

            using (var br = new SolidBrush(bg))
                g.FillPath(br, path);

            // Neutral: draw border
            if (style == FluentButton.Variant.Neutral)
            {
                var bc = hovered ? Fluent.BorderInput : Fluent.BorderCard;
                using var pen = new Pen(bc);
                g.DrawPath(pen, path);
            }

            // Disabled overlay
            if (!enabled)
            {
                using var dim = new SolidBrush(Color.FromArgb(100, 255, 255, 255));
                g.FillPath(dim, path);
            }

            // Text / foreground color
            Color fg = (style == FluentButton.Variant.Neutral)
                ? (enabled ? Fluent.TextPrimary : Fluent.TextHint)
                : (enabled ? Color.White : Color.FromArgb(160, 255, 255, 255));

            // Draw icon + text
            if (!string.IsNullOrEmpty(icon))
            {
                var mf  = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                var inf = new Size(int.MaxValue, int.MaxValue);
                int iw  = TextRenderer.MeasureText(icon, Fluent.FontIconSm, inf, mf).Width;
                int tw  = string.IsNullOrEmpty(text) ? 0
                        : TextRenderer.MeasureText(text, font, inf, mf).Width;
                int gap = string.IsNullOrEmpty(text) ? 0 : 4;
                int startX = Math.Max(4, (r.Width - iw - gap - tw) / 2);
                TextRenderer.DrawText(g, icon, Fluent.FontIconSm, new Rectangle(startX, 0, iw, r.Height), fg, mf);
                if (!string.IsNullOrEmpty(text))
                    TextRenderer.DrawText(g, text, font, new Rectangle(startX + iw + gap, 0, tw + 4, r.Height), fg, mf);
            }
            else
            {
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
                            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, text, font, r, fg, flags);
            }
        }

        // ── Dark theme (toolbar) ──────────────────────────────────────
        internal static void PaintDark(
            Graphics g, Rectangle r, string text, string icon,
            Font font, bool active, bool hovered, bool pressed, bool enabled, int radius,
            Color parentBg = default)
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Clear to the parent background first — eliminates ghost-paint
            // accumulation on repeated hover/leave cycles.
            Color bg = parentBg == default ? Fluent.DarkBg : parentBg;
            g.Clear(bg);

            var paint = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
            using var path = Fluent.RoundedRect(paint, radius);

            // State overlay on top of the cleared background
            Color bgFill =
                active  ? Fluent.DarkActive :
                pressed ? Fluent.DarkPress  :
                hovered ? Fluent.DarkHover  :
                          Color.Transparent;

            if (bgFill.A > 0)
                using (var br = new SolidBrush(bgFill))
                    g.FillPath(br, path);

            // Disabled overlay
            if (!enabled)
            {
                using var dim = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
                g.FillPath(dim, path);
            }

            Color fg = enabled
                ? (active ? Color.White : Fluent.DarkText)
                : Color.FromArgb(80, 255, 255, 255);

            if (!string.IsNullOrEmpty(icon))
            {
                // Icon in upper area, label in bottom strip
                int iconH = r.Height - 18;
                var iconRect = new Rectangle(0, 1, r.Width, iconH);
                var textRect = new Rectangle(0, r.Height - 16, r.Width, 16);
                TextRenderer.DrawText(g, icon, Fluent.FontIconTb, iconRect, fg,
                    TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (!string.IsNullOrEmpty(text))
                    TextRenderer.DrawText(g, text, font, textRect, fg,
                        TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                TextRenderer.DrawText(g, text, font, r, fg,
                    TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        // ── Card / group-panel background ─────────────────────────────
        /// <summary>
        /// Paints a WinUI 3-style card: white rounded rect with subtle border,
        /// a coloured left-side accent strip, and a section title.
        /// <para>The hosting Panel's BackColor should be set to Fluent.BgPage
        /// so that the rounded corners blend into the form background.</para>
        /// </summary>
        internal static void PaintCard(
            Graphics g, int w, int h, string title, Color accentColor, int hdrH)
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // White rounded card
            var cardRect = new Rectangle(0, 0, w - 1, h - 1);
            using var cardPath = Fluent.RoundedRect(cardRect, Fluent.RadiusCard);
            using (var br = new SolidBrush(Fluent.BgCard))
                g.FillPath(br, cardPath);
            using (var pen = new Pen(Fluent.BorderCard))
                g.DrawPath(pen, cardPath);

            // Left accent bar
            var accentRect = new Rectangle(2, 12, 4, h - 24);
            using var accentPath = Fluent.RoundedRect(accentRect, 2);
            using (var br = new SolidBrush(accentColor))
                g.FillPath(br, accentPath);

            // Section title
            var titleRect = new Rectangle(14, 0, w - 18, hdrH);
            TextRenderer.DrawText(g, title, Fluent.FontTitle, titleRect,
                Fluent.TextPrimary,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter);

            // Thin divider below header
            using var divPen = new Pen(Fluent.BorderCard);
            g.DrawLine(divPen, 2, hdrH, w - 3, hdrH);
        }

        // ── Helper ────────────────────────────────────────────────────
        private static Color Darken(Color c, float amount)
        {
            int r = Math.Max(0, (int)(c.R * (1f - amount)));
            int g = Math.Max(0, (int)(c.G * (1f - amount)));
            int b = Math.Max(0, (int)(c.B * (1f - amount)));
            return Color.FromArgb(c.A, r, g, b);
        }
    }
}

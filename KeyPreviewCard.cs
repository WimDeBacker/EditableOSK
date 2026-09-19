// KeyPreviewCard.cs — the live preview of the key being edited.
//
// A card with the caption "Preview" above a sample key, so it is unmistakably the example and not
// a control. It sits at the top right of the Key Editor (visible on every section) and follows the
// label, colours, font and border thickness as they are edited.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>The preview card: a caption and a sample key drawn with the current label, font, colours and border.</summary>
    internal sealed class KeyPreviewCard : Panel
    {
        private string _label = "a";
        private Color  _key = Color.FromArgb(45, 45, 74), _fontColor = Color.FromArgb(224, 224, 255), _border = Color.FromArgb(120, 120, 140);
        private int    _thickness = 2;
        private Font   _font = new Font("Arial", 13f, FontStyle.Bold);

        public KeyPreviewCard()
        {
            Size           = new Size(136, 100);
            Margin         = new Padding(Touch.Gap, 0, 0, Touch.Gap);
            Tag            = "notheme";                 // paints itself; the dialog theme must not recolour it
            DoubleBuffered = true;
            AccessibleName = Lang.T("Preview");
            AccessibleRole = AccessibleRole.Graphic;
        }

        /// <summary>Shows a key with these properties. An unusable font name keeps the previous font.</summary>
        public void Set(string label, Color key, Color font, Color border,
                        string fontName = null, float fontSize = 13f, int borderThickness = 2)
        {
            _label = label ?? ""; _key = key; _fontColor = font; _border = border;
            _thickness = Math.Max(0, borderThickness);
            try
            {
                var newFont = new Font(string.IsNullOrEmpty(fontName) ? "Arial" : fontName, Math.Max(1f, fontSize), FontStyle.Bold);
                _font.Dispose();                        // free the previous dynamic font before replacing it
                _font = newFont;
            }
            catch (ArgumentException) { }               // an invalid font name: the preview keeps its current font
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;   // pixel centres at .5, as in FluentPainter
            g.Clear(Parent?.BackColor ?? BackColor);

            bool dark = !ToolbarButton.IsLightTheme;
            Color cardBg     = dark ? Fluent.DialogDarkInput : Color.FromArgb(250, 250, 250);
            Color cardBorder = dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder;
            Color caption    = dark ? Fluent.DialogDarkTextDim : Fluent.TextSecondary;

            using (var path = Fluent.RoundedRectF(Fluent.CrispBorderRect(Width, Height), 8))
            using (var fill = new SolidBrush(cardBg))
            using (var pen = new Pen(cardBorder, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Lang.T("Preview"), Fluent.FontBtnSm, new Point(12, 8), caption, TextFormatFlags.NoPadding);
            int top = 8 + Fluent.FontBtnSm.Height + 8;
            var key = new Rectangle(14, top, Width - 29, Height - top - 14);
            using (var path = Fluent.RoundedRect(key, 6))
            using (var fill = new SolidBrush(_key))
            {
                g.FillPath(fill, path);
                if (_thickness > 0)
                    using (var pen = new Pen(_border, _thickness) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                        g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, _label, _font, key, _fontColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }
    }
}

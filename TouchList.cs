// TouchList.cs — a list box with 44 px rows, for the master list of a master/detail dialog (the Group Editor).
//
// A plain ListBox has rows as tall as its text (about 18 px). This one is owner-drawn with rows of at least 44 px (the touch
// target size), a real selection colour (white on the dark accent, 7 : 1) and a focus ring on the selected row. It keeps the
// native ListBox behaviour for everything else (keyboard: arrows, Home / End, type-ahead; screen readers; the mouse wheel).
//
// Known limit: the scroll bar is the native thin one. A list of more rows than fit scrolls with the wheel, the keyboard or a
// touch drag on the list itself; the dialogs keep their lists short (a keyboard rarely has more than a dozen groups).

using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>An owner-drawn <see cref="ListBox"/> whose rows are at least 44 px tall.</summary>
    internal sealed class TouchList : ListBox
    {
        public TouchList()
        {
            DrawMode       = DrawMode.OwnerDrawFixed;
            BorderStyle    = BorderStyle.None;
            IntegralHeight = false;
            Font           = Fluent.FontLabel;
            ItemHeight     = Touch.Target;
            AccessibleRole = AccessibleRole.List;
            Tag            = "notheme";          // paints its own colours; the dialog theme must not recolour it
            // The empty part of the list below the last row (rows are painted in OnDrawItem).
            BackColor      = ToolbarButton.IsLightTheme ? Fluent.BgInput : Fluent.DialogDarkInput;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // ItemHeight is in real pixels: scale the design size to this display.
            ItemHeight = (int)Math.Round(Touch.Target * DeviceDpi / 96.0);
        }

        protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            bool sel  = (e.State & DrawItemState.Selected) != 0;
            bool dark = !ToolbarButton.IsLightTheme;
            bool hc   = SystemInformation.HighContrast;

            Color bg = hc ? (sel ? SystemColors.Highlight : SystemColors.Window)
                     : sel ? Fluent.Accent
                     : dark ? Fluent.DialogDarkInput : Fluent.BgInput;
            Color fg = hc ? (sel ? SystemColors.HighlightText : SystemColors.WindowText)
                     : sel ? Color.White
                     : dark ? Fluent.DialogDarkText : Fluent.TextPrimary;
            Color line = hc ? SystemColors.WindowText : dark ? Fluent.DialogDarkBorder : Fluent.BorderCard;

            var g = e.Graphics;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);
            // A faint line between unselected rows (it is grouping, not a control boundary).
            if (!sel) using (var p = new Pen(line)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            int pad = (int)Math.Round(12 * DeviceDpi / 96.0);
            TextRenderer.DrawText(g, Items[e.Index].ToString(), Font,
                new Rectangle(e.Bounds.X + pad, e.Bounds.Y, e.Bounds.Width - 2 * pad, e.Bounds.Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            // Keyboard focus: a ring inside the selected row (white on the accent fill, 3 : 1 and more).
            if (sel && Focused)
            {
                var state = g.Save();
                g.TranslateTransform(e.Bounds.X, e.Bounds.Y);
                Fluent.DrawSquareRing(g, e.Bounds.Width, e.Bounds.Height, inset: 2, penWidth: 2, hc ? SystemColors.HighlightText : Color.White);
                g.Restore(state);
            }
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Shared visual constants targeting WinUI 3 / Fluent Design aesthetics.
    /// Light theme for modal dialogs; dark theme for the main toolbar.
    /// </summary>
    internal static class Fluent
    {
        // ── Light (dialog) palette ─────────────────────────────────────
        internal static readonly Color BgPage        = Color.FromArgb(243, 243, 243);
        internal static readonly Color BgCard        = Color.FromArgb(255, 255, 255);
        internal static readonly Color BgInput       = Color.FromArgb(255, 255, 255);
        internal static readonly Color BorderCard    = Color.FromArgb(229, 229, 229);
        internal static readonly Color BorderInput   = Color.FromArgb(196, 196, 196);
        internal static readonly Color TextPrimary   = Color.FromArgb(28,  28,  28);
        internal static readonly Color TextSecondary = Color.FromArgb(96,  94,  92);
        internal static readonly Color TextHint      = Color.FromArgb(160, 160, 160);
        internal static readonly Color Accent        = Color.FromArgb(0,   120, 212);
        internal static readonly Color AccentDark    = Color.FromArgb(0,   102, 180);
        internal static readonly Color Danger        = Color.FromArgb(196,  43,  28);
        internal static readonly Color DangerDark    = Color.FromArgb(168,  36,  24);
        internal static readonly Color Success       = Color.FromArgb(16,  124,  16);
        internal static readonly Color SuccessDark   = Color.FromArgb(13,  105,  13);
        internal static readonly Color Neutral       = Color.FromArgb(242, 242, 242);
        internal static readonly Color NeutralBorder = Color.FromArgb(196, 196, 196);

        // ── Dark (toolbar) palette ─────────────────────────────────────
        internal static readonly Color DarkBg      = Color.FromArgb(32,  32,  32);
        internal static readonly Color DarkBg2     = Color.FromArgb(44,  44,  44);
        internal static readonly Color DarkText    = Color.FromArgb(255, 255, 255);
        internal static readonly Color DarkTextDim = Color.FromArgb(160, 255, 255, 255);
        internal static readonly Color DarkHover   = Color.FromArgb(28,  255, 255, 255);
        internal static readonly Color DarkPress   = Color.FromArgb(16,  255, 255, 255);
        internal static readonly Color DarkBorder  = Color.FromArgb(55,  255, 255, 255);
        internal static readonly Color DarkActive  = Color.FromArgb(0,   120, 212);

        // ── Fonts ──────────────────────────────────────────────────────
        private static readonly string _ff =
            IsFontAvailable("Segoe UI Variable") ? "Segoe UI Variable" : "Segoe UI";

        // Dialog / settings window fonts (+2 pt vs original)
        internal static readonly Font FontLabel   = new Font(_ff, 12.5f);
        internal static readonly Font FontLabelSm = new Font(_ff, 11.0f);
        internal static readonly Font FontTitle   = new Font(_ff, 12.5f, FontStyle.Bold);
        internal static readonly Font FontInput   = new Font(_ff, 12.5f);
        internal static readonly Font FontCaption = new Font(_ff, 11.0f);
        internal static readonly Font FontBtnLg   = new Font(_ff, 12.5f, FontStyle.Bold);
        internal static readonly Font FontBtnSm   = new Font(_ff, 10.0f);
        internal static readonly Font FontHint    = new Font(_ff, 11.0f);
        // Toolbar fonts — kept at original size
        internal static readonly Font FontBtnTb   = new Font(_ff,  8.5f);
        internal static readonly Font FontIconTb  = new Font("Segoe MDL2 Assets", 13f);
        internal static readonly Font FontIconSm  = new Font("Segoe MDL2 Assets", 11f);

        // ── Dimensions ────────────────────────────────────────────────
        internal const int RadiusBtn  = 5;
        internal const int RadiusCard = 8;
        internal const int Pad        = 16;
        internal const int PadSm      = 8;
        internal const int RowH       = 44;
        internal const int HdrH       = 38;
        internal const int BtnH       = 34;

        // ── Rounded-rect helpers ───────────────────────────────────────
        internal static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.Left,      r.Top,          d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top,          d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d,   d, d,   0, 90);
            p.AddArc(r.Left,      r.Bottom - d,   d, d,  90, 90);
            p.CloseFigure();
            return p;
        }

        internal static Region RoundedRegion(int w, int h, int radius)
        {
            using var p = RoundedRect(new Rectangle(0, 0, w, h), radius);
            return new Region(p);
        }

        private static bool IsFontAvailable(string name)
        {
            try
            {
                using var col = new InstalledFontCollection();
                foreach (var f in col.Families)
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// Segoe MDL2 Assets icon codepoints — present on Windows 10 and later.
    /// Use Fluent.FontIconTb (toolbar) or Fluent.FontIconSm (dialogs) to render.
    /// </summary>
    internal static class FIcon
    {
        internal const string Save       = "\uE74E";  // floppy disk
        internal const string Load       = "\uE8B7";  // open folder
        internal const string Undo       = "\uE7A7";  // undo arrow
        internal const string Redo       = "\uE7A6";  // redo arrow
        internal const string Edit       = "\uE70F";  // pencil
        internal const string Delete     = "\uE74D";  // trash can
        internal const string Settings   = "\uE713";  // gear
        internal const string Close      = "\uE8BB";  // X
        internal const string Copy       = "\uE8C8";  // copy
        internal const string Brush      = "\uE771";  // paint brush
        internal const string Keyboard   = "\uE765";  // keyboard
        internal const string Add        = "\uE710";  // plus
        internal const string Remove     = "\uE738";  // minus
        internal const string ArrowUp    = "\uE74A";  // up arrow
        internal const string ArrowDown  = "\uE74B";  // down arrow
        internal const string ArrowLeft  = "\uE76B";  // back arrow
        internal const string ArrowRight = "\uE76C";  // forward arrow
        internal const string Accept     = "\uE8FB";  // check mark
        internal const string Cancel     = "\uE8BB";  // cancel
        internal const string Import     = "\uE8B5";  // import
        internal const string Merge      = "\uE8C4";  // merge
        internal const string Split      = "\uE8C6";  // split
        internal const string Groups     = "\uE81E";  // layers
        internal const string Font       = "\uE8D2";  // font
        internal const string Language   = "\uE12B";  // globe
        internal const string Window     = "\uE8A7";  // window
        internal const string Access     = "\uED5A";  // accessibility
        internal const string File       = "\uE8A5";  // document
        internal const string Exit       = "\uE7E8";  // exit
    }
}

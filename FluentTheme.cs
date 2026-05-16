// FluentTheme.cs — centralised visual design tokens for the on-screen keyboard UI
//
// Why have a separate file for this?
//   In any real app you want one place to control the look. If you scatter
//   Color.FromArgb(0, 120, 212) throughout 10 forms, changing the accent colour
//   later means hunting through all of them. Here, every form imports from the
//   same source, so one edit fixes everything at once.
//
// What is "Fluent Design"?
//   It is Microsoft's design language for Windows 10/11. The colours and
//   measurements below are chosen to match native Windows controls so this
//   keyboard app feels like it belongs on the OS.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Shared visual constants that define the app's look and feel,
    /// targeting the WinUI 3 / Fluent Design aesthetic.
    ///
    /// Two palettes live here:
    /// <list type="bullet">
    ///   <item><b>Light</b> — used by modal dialogs and settings windows.</item>
    ///   <item><b>Dark</b>  — used by the main keyboard toolbar.</item>
    /// </list>
    ///
    /// All forms and controls read their colours, fonts, and sizes from this
    /// class so the entire UI can be restyled by editing one file.
    /// </summary>
    internal static class Fluent
    {
        // ── Light (dialog) palette ─────────────────────────────────────
        //
        // These are the colours for settings dialogs, editor windows, and
        // any other pop-up panel that appears on top of the main keyboard.
        //
        // Color.FromArgb(r, g, b) creates a colour from red/green/blue values,
        // each in the range 0–255. Lower = darker, higher = lighter.

        // Slightly off-white page background — pure white feels too harsh on modern displays.
        internal static readonly Color BgPage        = Color.FromArgb(243, 243, 243);

        // White card / panel background (sits on top of the page background).
        internal static readonly Color BgCard        = Color.FromArgb(255, 255, 255);

        // White background used inside text input fields.
        internal static readonly Color BgInput       = Color.FromArgb(255, 255, 255);

        // Subtle grey border that separates cards from the page background.
        internal static readonly Color BorderCard    = Color.FromArgb(229, 229, 229);

        // Slightly darker border for text input fields — makes them look editable.
        internal static readonly Color BorderInput   = Color.FromArgb(196, 196, 196);

        // Near-black for body text — softer than pure black (0,0,0), which can look harsh.
        internal static readonly Color TextPrimary   = Color.FromArgb(28,  28,  28);

        // Medium grey for supporting text (labels, descriptions).
        internal static readonly Color TextSecondary = Color.FromArgb(96,  94,  92);

        // Light grey for placeholder / hint text inside empty input fields.
        internal static readonly Color TextHint      = Color.FromArgb(160, 160, 160);

        // Microsoft's standard blue — used for primary action buttons and focused borders.
        internal static readonly Color Accent        = Color.FromArgb(0,   120, 212);

        // Darker blue for the "pressed" state of an accent button.
        internal static readonly Color AccentDark    = Color.FromArgb(0,   102, 180);

        // Red for destructive actions (delete, remove). Slightly muted — not fire-engine red.
        internal static readonly Color Danger        = Color.FromArgb(196,  43,  28);

        // Darker red for the pressed state of a danger button.
        internal static readonly Color DangerDark    = Color.FromArgb(168,  36,  24);

        // Green for confirmations and success states.
        internal static readonly Color Success       = Color.FromArgb(16,  124,  16);

        // Darker green for the pressed state of a success button.
        internal static readonly Color SuccessDark   = Color.FromArgb(13,  105,  13);

        // Very light grey for neutral / secondary buttons in the resting state.
        internal static readonly Color Neutral       = Color.FromArgb(242, 242, 242);

        // Border colour for neutral buttons.
        internal static readonly Color NeutralBorder = Color.FromArgb(196, 196, 196);

        // ── Dark (toolbar) palette ─────────────────────────────────────
        //
        // The main keyboard toolbar uses a dark theme so it contrasts visually
        // with whatever application is open behind it.
        //
        // Several colours below use a FOUR-argument FromArgb(alpha, r, g, b)
        // overload. The first value is the opacity (0 = fully transparent,
        // 255 = fully opaque). This lets us layer semi-transparent white over
        // the dark background to produce hover / press effects without needing
        // separate colours for every possible background shade.

        // Darkest background — the main toolbar surface.
        internal static readonly Color DarkBg      = Color.FromArgb(32,  32,  32);

        // Slightly lighter dark — used for nested panels or secondary areas.
        internal static readonly Color DarkBg2     = Color.FromArgb(44,  44,  44);

        // Pure white text on dark backgrounds.
        internal static readonly Color DarkText    = Color.FromArgb(255, 255, 255);

        // Dimmed white (63% opacity) for secondary labels on the dark toolbar.
        internal static readonly Color DarkTextDim = Color.FromArgb(160, 255, 255, 255);

        // Very faint white overlay (11% opacity) for the mouse-hover highlight.
        internal static readonly Color DarkHover   = Color.FromArgb(28,  255, 255, 255);

        // Even fainter white overlay (6% opacity) for the mouse-press / active state.
        internal static readonly Color DarkPress   = Color.FromArgb(16,  255, 255, 255);

        // Subtle white border (22% opacity) around dark toolbar buttons.
        internal static readonly Color DarkBorder  = Color.FromArgb(55,  255, 255, 255);

        // Solid Microsoft blue — highlights the currently active keyboard layout tab.
        internal static readonly Color DarkActive  = Color.FromArgb(0,   120, 212);

        // ── Fonts ──────────────────────────────────────────────────────
        //
        // "Segoe UI Variable" is the newer, higher-quality version of the
        // Windows system font introduced in Windows 11. It has better
        // letter-spacing at small sizes. We fall back to the older "Segoe UI"
        // if the newer version is not installed (e.g. on Windows 10).
        //
        // This check happens once at startup and the result is stored in _ff
        // (font-family name) so every font object below uses the same choice.
        private static readonly string _ff =
            IsFontAvailable("Segoe UI Variable") ? "Segoe UI Variable" : "Segoe UI";

        // Fonts for dialogs and settings windows — all 2pt larger than the toolbar
        // for better readability at normal reading distance.
        internal static readonly Font FontLabel   = new Font(_ff, 12.5f);              // standard body label
        internal static readonly Font FontLabelSm = new Font(_ff, 11.0f);              // smaller supporting label
        internal static readonly Font FontTitle   = new Font(_ff, 12.5f, FontStyle.Bold); // section headings
        internal static readonly Font FontInput   = new Font(_ff, 12.5f);              // text inside input fields
        internal static readonly Font FontCaption = new Font(_ff, 11.0f);              // captions / hints
        internal static readonly Font FontBtnLg   = new Font(_ff, 12.5f, FontStyle.Bold); // primary action buttons
        internal static readonly Font FontBtnSm   = new Font(_ff, 10.0f);              // small / secondary buttons
        internal static readonly Font FontHint    = new Font(_ff, 11.0f);              // placeholder text

        // Fonts for the compact toolbar — intentionally smaller than dialog fonts.
        internal static readonly Font FontBtnTb   = new Font(_ff,  8.5f);              // text labels on toolbar buttons

        // "Segoe MDL2 Assets" is Windows' built-in icon font.
        // Characters in this font render as icons (save, edit, delete, etc.)
        // rather than letters. See the FIcon class below for the character codes.
        internal static readonly Font FontIconTb  = new Font("Segoe MDL2 Assets", 13f); // larger icons on toolbar
        internal static readonly Font FontIconSm  = new Font("Segoe MDL2 Assets", 11f); // smaller icons in dialogs

        // ── Dimensions ────────────────────────────────────────────────
        //
        // Storing pixel measurements as named constants means changing the
        // overall density of the UI is a one-line edit per constant.

        internal const int RadiusBtn  = 5;   // corner radius (px) for buttons
        internal const int RadiusCard = 8;   // corner radius (px) for cards / panels
        internal const int Pad        = 16;  // standard padding between elements
        internal const int PadSm      = 8;   // half-size padding for tighter layouts
        internal const int RowH       = 44;  // height of a standard form row
        internal const int HdrH       = 38;  // height of a section header bar
        internal const int BtnH       = 34;  // height of a standard button

        // ── Rounded-rect helpers ───────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="GraphicsPath"/> that describes a rectangle with
        /// rounded corners. Use this path with <c>Graphics.FillPath</c> /
        /// <c>DrawPath</c> to draw pill-shaped buttons and cards.
        /// </summary>
        /// <param name="r">The bounding rectangle (position + size) of the shape.</param>
        /// <param name="radius">
        /// Corner radius in pixels. Pass 0 for sharp corners (plain rectangle).
        /// </param>
        /// <returns>
        /// A <see cref="GraphicsPath"/> that the caller is responsible for
        /// disposing after use.
        /// </returns>
        internal static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();

            // A radius of 0 means plain rectangle — skip the arc calculations.
            if (radius <= 0) { p.AddRectangle(r); return p; }

            // An arc is defined by its bounding square (left, top, width, height),
            // a start angle, and a sweep angle (both in degrees, clockwise).
            // d is the diameter of the corner circle.
            int d = radius * 2;

            // Top-left corner: starts at 180° (left) and sweeps 90° clockwise to 270° (top).
            p.AddArc(r.Left,      r.Top,          d, d, 180, 90);

            // Top-right corner: starts at 270° (top) and sweeps to 0° (right).
            p.AddArc(r.Right - d, r.Top,          d, d, 270, 90);

            // Bottom-right corner: starts at 0° (right) and sweeps to 90° (bottom).
            p.AddArc(r.Right - d, r.Bottom - d,   d, d,   0, 90);

            // Bottom-left corner: starts at 90° (bottom) and sweeps to 180° (left).
            p.AddArc(r.Left,      r.Bottom - d,   d, d,  90, 90);

            // Connect the last point back to the first to close the outline.
            p.CloseFigure();
            return p;
        }

        /// <summary>
        /// Creates a <see cref="Region"/> (a clipping mask) shaped like a
        /// rounded rectangle that fills the given dimensions.
        /// </summary>
        /// <remarks>
        /// Assign this region to a form's <c>Region</c> property to clip the
        /// window itself to a rounded shape so the OS doesn't draw sharp corners.
        /// </remarks>
        /// <param name="w">Total width in pixels.</param>
        /// <param name="h">Total height in pixels.</param>
        /// <param name="radius">Corner radius in pixels.</param>
        internal static Region RoundedRegion(int w, int h, int radius)
        {
            // We build the path just to hand it to Region, then dispose it —
            // Region makes its own internal copy, so we don't need to keep the path.
            using var p = RoundedRect(new Rectangle(0, 0, w, h), radius);
            return new Region(p);
        }

        /// <summary>
        /// Checks whether a font family with the given name is installed on
        /// this computer.
        /// </summary>
        /// <param name="name">The font family name to look for (case-insensitive).</param>
        /// <returns><c>true</c> if the font is found; <c>false</c> otherwise.</returns>
        private static bool IsFontAvailable(string name)
        {
            try
            {
                // InstalledFontCollection enumerates every font family on the system.
                // We walk through all of them looking for an exact name match.
                using var col = new InstalledFontCollection();
                foreach (var f in col.Families)
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }   // silently return false if the font system is unavailable
            return false;
        }
    }

    /// <summary>
    /// Unicode codepoints for icons in the "Segoe MDL2 Assets" font.
    ///
    /// <para>
    /// Segoe MDL2 Assets is a built-in Windows icon font where each character
    /// code renders as a small glyph (save, edit, trash-can, etc.) instead of
    /// a letter. To draw an icon, render the corresponding string constant here
    /// using <see cref="Fluent.FontIconTb"/> or <see cref="Fluent.FontIconSm"/>.
    /// </para>
    ///
    /// <para>
    /// The hex values (e.g. <c></c>) are Unicode "Private Use Area"
    /// codepoints that Microsoft has assigned to icons. They are only
    /// meaningful when rendered with this specific font.
    /// </para>
    /// </summary>
    internal static class FIcon
    {
        internal const string Save       = "";  // floppy disk
        internal const string Load       = "";  // open folder
        internal const string Undo       = "";  // undo arrow
        internal const string Redo       = "";  // redo arrow
        internal const string Edit       = "";  // pencil
        internal const string Delete     = "";  // trash can
        internal const string Settings   = "";  // gear
        internal const string Close      = "";  // X
        internal const string Copy       = "";  // copy
        internal const string Brush      = "";  // paint brush
        internal const string Keyboard   = "";  // keyboard
        internal const string Add        = "";  // plus
        internal const string Remove     = "";  // minus
        internal const string ArrowUp    = "";  // up arrow
        internal const string ArrowDown  = "";  // down arrow
        internal const string ArrowLeft  = "";  // back arrow
        internal const string ArrowRight = "";  // forward arrow
        internal const string Accept     = "";  // check mark
        internal const string Cancel     = "";  // cancel (same glyph as Close)
        internal const string Import     = "";  // import
        internal const string Merge      = "";  // merge
        internal const string Split      = "";  // split
        internal const string Groups     = "";  // layers
        internal const string Font       = "";  // font
        internal const string Language   = "";  // globe
        internal const string Window     = "";  // window
        internal const string Access     = "";  // accessibility
        internal const string File       = "";  // document
        internal const string Exit       = "";  // exit
    }
}

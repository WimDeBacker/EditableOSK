using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnScreenKeyboard
{
    // ════════════════════════════════════════════════════════════════════
    //  FluentButton — light-theme owner-drawn button for modal dialogs.
    //
    //  "Owner-drawn" means we take over the painting ourselves instead of
    //  letting Windows draw the standard grey button.  This lets us produce
    //  the rounded, coloured, Fluent-Design look used in the dialog boxes.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A custom button control that paints itself with a Fluent Design / WinUI 3
    /// visual style, for use inside light-background dialog windows.
    ///
    /// <para>It inherits from the standard WinForms <see cref="Button"/> so it works
    /// exactly like a normal button (Click events, keyboard activation, tab order,
    /// etc.) but overrides the painting to produce rounded corners, accent colours,
    /// and smooth hover/press feedback.</para>
    ///
    /// <para>Four colour variants are available via <see cref="Variant"/>:
    /// Primary (blue accent), Danger (red), Success (green), Neutral (grey/outlined).
    /// Choose the variant that matches the intent of the action.</para>
    /// </summary>
    public class FluentButton : Button
    {
        /// <summary>
        /// Defines the colour intent of the button, which maps to the actual
        /// paint colours defined in the <see cref="Fluent"/> colour palette.
        /// </summary>
        public enum Variant
        {
            /// <summary>The main action on the dialog (e.g. "Save", "OK"). Rendered in the app accent colour.</summary>
            Primary,
            /// <summary>A destructive or irreversible action (e.g. "Delete"). Rendered in red.</summary>
            Danger,
            /// <summary>A positive confirmation action (e.g. "Apply"). Rendered in green.</summary>
            Success,
            /// <summary>A secondary or cancel action. Rendered with an outlined grey style.</summary>
            Neutral
        }

        /// <summary>
        /// The colour style of this button instance.
        /// Set this in code after creating the button:
        /// <c>myButton.Style = FluentButton.Variant.Danger;</c>
        /// The <see cref="DesignerSerializationVisibility"/> attribute tells the
        /// Visual Studio designer not to try to serialise this property into the
        /// .Designer.cs file (it handles enums fine, but keeping designer files
        /// clean avoids conflicts).
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Variant Style        { get; set; } = Variant.Primary;

        /// <summary>
        /// An optional icon glyph to display to the left of the button label.
        /// This is typically a single Unicode character from an icon font such as
        /// Segoe MDL2 or Segoe Fluent Icons (e.g. "" for a save icon).
        /// Leave as empty string for a text-only button.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string  IconGlyph    { get; set; } = "";

        /// <summary>
        /// The radius in pixels of the rounded corners.
        /// Defaults to the app-wide button corner radius defined in <see cref="Fluent.RadiusBtn"/>.
        /// A value of 0 produces square corners; larger values produce more pill-shaped buttons.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int     CornerRadius { get; set; } = Fluent.RadiusBtn;

        /// <summary>
        /// Overrides the base <see cref="System.Windows.Forms.Control.Text"/> setter to keep
        /// <see cref="System.Windows.Forms.Control.AccessibleName"/> in sync automatically.
        /// WinForms reads <c>AccessibleName</c> as the label Narrator announces when the
        /// button receives focus; without this, owner-drawn buttons have no accessible name
        /// because the base class text is not surfaced to the accessibility tree.
        /// The mnemonic marker (<c>&amp;</c>) is stripped so screen readers hear
        /// "Cancel" rather than "&amp;Cancel".
        /// </summary>
        public override string Text
        {
            get => base.Text;
            set
            {
                base.Text = value;
                AccessibleName = Lang.StripMnemonic(value);
            }
        }

        // Private state flags that drive the visual feedback.
        // We track these ourselves because WinForms does not expose a reliable
        // "currently hovered" property on the base Button class.
        private bool _hovered, _pressed;

        // Handler stored so it can be unsubscribed in Dispose — prevents memory leaks.
        private readonly UserPreferenceChangedEventHandler _prefChanged;

        /// <summary>
        /// Initialises the button with the settings required for owner-draw painting.
        /// </summary>
        public FluentButton()
        {
            // UserPaint: we handle OnPaint ourselves — WinForms won't draw the standard button chrome.
            // AllPaintingInWmPaint: prevents a separate WM_ERASEBKGND message, which would cause flicker.
            // OptimizedDoubleBuffer: WinForms buffers painting off-screen and swaps the result in
            //   one go, eliminating the brief blank-then-paint flash visible with single buffering.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            FlatStyle = FlatStyle.Flat;             // required so the base class doesn't draw its own border on top of ours
            FlatAppearance.BorderSize = 0;           // hide the focus rectangle that FlatStyle.Flat normally draws
            TabStop = false;                         // exclude from Tab-key navigation (dialogs use Enter/Escape instead)
            Cursor  = Cursors.Hand;                  // pointer cursor signals "clickable"
            Font    = Fluent.FontBtnLg;              // use the app's standard dialog-button font

            // Repaint when the user toggles high-contrast mode so the HC paint path activates.
            _prefChanged = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.Accessibility
                    && IsHandleCreated && !IsDisposed)
                    BeginInvoke((Action)Invalidate);
            };
            SystemEvents.UserPreferenceChanged += _prefChanged;
        }

        /// <summary>Unsubscribes from <see cref="SystemEvents.UserPreferenceChanged"/> to prevent leaks.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SystemEvents.UserPreferenceChanged -= _prefChanged;
            base.Dispose(disposing);
        }

        /// <summary>
        /// Suppresses the default background erase step.
        ///
        /// <para>Without this override WinForms would paint the parent's background
        /// colour before calling <see cref="OnPaint"/>, causing a visible flicker on
        /// hover transitions.  Because <see cref="OnPaint"/> already fills the entire
        /// control surface (including the "transparent" areas around the rounded
        /// corners), we can safely skip the background step.</para>
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e) { /* suppress — OnPaint owns the full surface */ }

        // ── Mouse state tracking ──────────────────────────────────────
        // Each handler sets/clears a flag and then calls Invalidate() to request
        // a repaint.  Calling base.OnMouseXxx() ensures the standard Button
        // events (MouseEnter, MouseLeave, …) still fire for external subscribers.

        /// <summary>Sets the hovered state and triggers a repaint when the mouse enters the button.</summary>
        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }

        /// <summary>Clears the hovered state and triggers a repaint when the mouse leaves the button.</summary>
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        /// <summary>Triggers a repaint when the button gains keyboard focus so the focus ring appears.</summary>
        protected override void OnGotFocus(EventArgs e)  { Invalidate(); base.OnGotFocus(e); }

        /// <summary>Triggers a repaint when the button loses keyboard focus so the focus ring disappears.</summary>
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        /// <summary>Sets the pressed state on left-button down and triggers a repaint for the "pushed" look.</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        /// <summary>Clears the pressed state on mouse release and repaints to return to the normal/hovered look.</summary>
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        // WM_UPDATEUISTATE (0x0128) is broadcast to every control when the user
        // presses Alt, toggling the system's "show keyboard cues" state.
        // The base Control class updates ShowKeyboardCues in response, but it only
        // calls Invalidate() for non-UserPaint controls.  For owner-drawn controls
        // we must trigger the repaint ourselves so PaintLight can switch between
        // HidePrefix (cues off) and no-prefix (cues on, underline visible).
        private const int WM_UPDATEUISTATE = 0x0128;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_UPDATEUISTATE) Invalidate();
        }

        /// <summary>
        /// Paints the button by delegating to <see cref="FluentPainter.PaintLight"/>.
        ///
        /// <para>We pass the parent's background colour so <see cref="FluentPainter"/>
        /// can flood-fill the control rectangle with that colour before drawing the
        /// rounded shape on top.  This makes the corners look transparent even though
        /// WinForms controls are always rectangular — the "outside-the-corners" area
        /// is simply painted the same colour as the parent form.</para>
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            // Resolve the parent background so rounded corners blend in correctly.
            Color parentBg = Parent?.BackColor ?? Fluent.BgPage;
            // ShowKeyboardCues is true when Alt is held or the accessibility setting
            // "Always show keyboard shortcuts" is on — pass it so PaintLight can
            // show/hide the mnemonic underline at the right moment.
            FluentPainter.PaintLight(e.Graphics, ClientRectangle, Text, IconGlyph,
                Font, Style, _hovered, _pressed, Enabled, CornerRadius, parentBg,
                ShowKeyboardCues);

            // Focus ring — drawn whenever the button has focus, regardless of how focus was acquired.
            // WCAG 2.1 AA §2.4.7: keyboard focus must be visible.
            // The older WinForms convention of gating this on ShowFocusCues (hiding the ring on
            // mouse-click) caused a first-focus race: ShowFocusCues starts false when a new dialog
            // opens, so the ring never appeared on the very first Tab press.  WinUI 3 / Windows 11
            // always show the focus ring when the control has focus — we follow that behaviour.
            // Ring colour: accent blue on Neutral (grey) buttons, white on coloured variants.
            if (Focused)
            {
                var ring = new Rectangle(2, 2, Width - 5, Height - 5);
                if (SystemInformation.HighContrast)
                    ControlPaint.DrawFocusRectangle(e.Graphics, ring);
                else
                {
                    var ringColor = (Style == Variant.Neutral) ? Fluent.Accent : Color.White;
                    using var pen = new Pen(ringColor, 2f);
                    e.Graphics.DrawRectangle(pen, ring);
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ToolbarButton — dark-theme owner-drawn button for the main toolbar.
    //  Extends NoActivateButton so clicking it never steals focus.
    //
    //  The keyboard toolbar must never steal focus away from the application
    //  the user is typing into.  NoActivateButton handles this by overriding
    //  the Windows WM_MOUSEACTIVATE message so the click is registered without
    //  activating (focusing) the toolbar window.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A custom button used in the main keyboard toolbar, styled for the dark
    /// background of the toolbar panel.
    ///
    /// <para>Functionally identical to <see cref="FluentButton"/> but uses the dark
    /// colour palette from <see cref="Fluent"/> and supports an "active" toggle state
    /// (e.g. for a Shift or Caps Lock key that is currently engaged).</para>
    ///
    /// <para>Inherits from <c>NoActivateButton</c> rather than <see cref="Button"/> so
    /// that clicking a toolbar button never moves keyboard focus away from the target
    /// application — essential for an on-screen keyboard where the user must keep
    /// typing into another window.</para>
    /// </summary>
    public class ToolbarButton : NoActivateButton
    {
        /// <summary>
        /// An optional icon glyph displayed above the text label.
        /// Works the same way as <see cref="FluentButton.IconGlyph"/>.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IconGlyph { get; set; } = "";

        /// <summary>
        /// An optional bitmap image displayed in the icon area of the button,
        /// taking priority over <see cref="IconGlyph"/> when both are set.
        /// Typically loaded from an SVG file via <see cref="SvgIconLoader"/>.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap IconImage { get; set; } = null;

        /// <summary>
        /// When <c>true</c> the button is drawn in the "active/toggled" state —
        /// a brighter fill that indicates the function is currently on
        /// (e.g. Shift is held, or a panel is open).
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool   IsActive  { get; set; } = false;

        /// <summary>
        /// The SVG filename (inside the icons directory) used for this button's icon.
        /// Stored so the icon can be re-rendered at a different tint when the toolbar
        /// theme changes at runtime.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SvgFile { get; set; } = "";

        /// <summary>
        /// When <c>true</c> the button draws an accent-coloured focus ring — used by
        /// the keyboard-navigation layer in Edit mode to show which toolbar button
        /// would be activated by pressing Enter or Space.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool HasNavFocus { get; set; } = false;

        /// <summary>
        /// When <c>true</c> all <see cref="ToolbarButton"/> instances paint in light-theme
        /// mode (dark icons on a light background).  Set this before calling
        /// <see cref="Control.Invalidate"/> on all toolbar buttons to switch themes at runtime.
        /// </summary>
        public static bool IsLightTheme { get; set; } = false;

        // Same hover/pressed flags as FluentButton — see comments there.
        private bool _hovered, _pressed;

        // Same HC subscription pattern as FluentButton.
        private readonly UserPreferenceChangedEventHandler _prefChanged;

        /// <summary>
        /// Initialises the toolbar button with owner-draw and dark-theme defaults.
        /// </summary>
        public ToolbarButton()
        {
            // Same owner-draw setup as FluentButton — see the comments there.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TabStop = false;
            Cursor  = Cursors.Hand;
            Font    = Fluent.FontBtnTb;   // smaller font used in the compact toolbar

            _prefChanged = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.Accessibility
                    && IsHandleCreated && !IsDisposed)
                    BeginInvoke((Action)Invalidate);
            };
            SystemEvents.UserPreferenceChanged += _prefChanged;
        }

        /// <summary>Unsubscribes from <see cref="SystemEvents.UserPreferenceChanged"/> to prevent leaks.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SystemEvents.UserPreferenceChanged -= _prefChanged;
            base.Dispose(disposing);
        }

        /// <summary>Suppresses background erase for flicker-free painting. See <see cref="FluentButton.OnPaintBackground"/>.</summary>
        protected override void OnPaintBackground(PaintEventArgs e) { /* suppress — OnPaint owns the full surface */ }

        /// <summary>Marks hovered and repaints.</summary>
        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }

        /// <summary>Clears hovered and repaints.</summary>
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        /// <summary>Sets pressed on left-click and repaints.</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        /// <summary>Clears pressed on release and repaints.</summary>
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        /// <summary>
        /// Paints the toolbar button using the dark-theme painter.
        /// Passes the parent background colour for the same corner-blending reason
        /// described in <see cref="FluentButton.OnPaint"/>.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            Color parentBg = Parent?.BackColor ?? (IsLightTheme ? Fluent.BgPage : Fluent.DarkBg);
            FluentPainter.PaintDark(e.Graphics, ClientRectangle, Text, IconGlyph, IconImage,
                Font, IsActive, _hovered, _pressed, Enabled, Fluent.RadiusBtn, parentBg,
                IsLightTheme);

            // Keyboard-navigation focus ring — drawn when this button is the current
            // keyboard-nav target inside the toolbar (HasNavFocus is set by KeyboardForm).
            if (HasNavFocus)
            {
                var ring = new Rectangle(2, 2, Width - 5, Height - 5);
                if (SystemInformation.HighContrast)
                    ControlPaint.DrawFocusRectangle(e.Graphics, ring);
                else
                {
                    using var pen = new Pen(Fluent.Accent, 2f);
                    e.Graphics.DrawRectangle(pen, ring);
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  FluentPainter — shared owner-draw routines.
    //
    //  This is a static helper class — it has no state of its own and is
    //  never instantiated.  Both FluentButton and ToolbarButton call into it
    //  so the actual drawing code lives in one place (DRY principle).
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides the low-level GDI+ painting methods shared by
    /// <see cref="FluentButton"/>, <see cref="ToolbarButton"/>, and card panels.
    ///
    /// <para>All methods are <c>internal static</c>: internal because they are an
    /// implementation detail not needed outside this assembly; static because they
    /// carry no per-instance data — every piece of information they need is passed
    /// as a parameter.</para>
    /// </summary>
    internal static class FluentPainter
    {
        // ── Light theme (dialogs) ─────────────────────────────────────

        /// <summary>
        /// Paints a single Fluent-style button for a light-background dialog.
        ///
        /// <para>The painting sequence is:
        /// <list type="number">
        ///   <item>Flood-fill the rectangular control bounds with the parent colour
        ///         (so rounded corners appear transparent).</item>
        ///   <item>Compute and fill the rounded-rectangle shape with the appropriate
        ///         variant colour, darkened slightly on hover/press.</item>
        ///   <item>For Neutral buttons, draw an outlined border instead of a solid fill.</item>
        ///   <item>If disabled, overlay a semi-transparent white wash to dim the button.</item>
        ///   <item>Draw the icon glyph and/or label text, centred inside the shape.</item>
        /// </list></para>
        /// </summary>
        /// <param name="g">The GDI+ <see cref="Graphics"/> surface to draw on.</param>
        /// <param name="r">The full bounding rectangle of the control (in client coordinates).</param>
        /// <param name="text">The button label. May be empty.</param>
        /// <param name="icon">Icon glyph character(s). May be empty.</param>
        /// <param name="font">Font to use for the label text.</param>
        /// <param name="style">Colour variant — controls which base colour is used.</param>
        /// <param name="hovered">Whether the mouse is currently over the button.</param>
        /// <param name="pressed">Whether the left mouse button is currently held down on the button.</param>
        /// <param name="enabled">Whether the button is enabled (accepts clicks).</param>
        /// <param name="radius">Corner radius in pixels.</param>
        /// <param name="parentBg">
        ///   The background colour of the parent container.  Used to paint the
        ///   area outside the rounded rectangle so the corners look cut out.
        /// </param>
        internal static void PaintLight(
            Graphics g, Rectangle r, string text, string icon,
            Font font, FluentButton.Variant style,
            bool hovered, bool pressed, bool enabled, int radius,
            Color parentBg = default, bool showKeyboardCues = false)
        {
            // Quality settings for crisp anti-aliased shapes and ClearType text.
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

            // High-contrast mode: use system colours so the OS-chosen HC theme shows through.
            // All custom fills and tints are replaced with flat system-colour paints so the
            // button looks and behaves like a native Windows button in every HC theme.
            if (SystemInformation.HighContrast)
            {
                g.Clear(SystemColors.Control);
                ControlPaint.DrawBorder(g, r,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid);
                Color hcFg    = enabled ? SystemColors.ControlText : SystemColors.GrayText;
                var   pfxFlag = showKeyboardCues ? TextFormatFlags.Default : TextFormatFlags.HidePrefix;
                if (!string.IsNullOrEmpty(icon))
                {
                    var mf  = pfxFlag | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                    var inf = new Size(int.MaxValue, int.MaxValue);
                    int iw  = TextRenderer.MeasureText(icon, Fluent.FontIconSm, inf, mf).Width;
                    int tw  = string.IsNullOrEmpty(text) ? 0
                            : TextRenderer.MeasureText(text, font, inf, mf).Width;
                    int gap     = string.IsNullOrEmpty(text) ? 0 : 4;
                    int startX  = Math.Max(4, (r.Width - iw - gap - tw) / 2);
                    TextRenderer.DrawText(g, icon, Fluent.FontIconSm,
                        new Rectangle(startX, 0, iw, r.Height), hcFg, mf);
                    if (!string.IsNullOrEmpty(text))
                        TextRenderer.DrawText(g, text, font,
                            new Rectangle(startX + iw + gap, 0, tw + 4, r.Height), hcFg, mf);
                }
                else
                {
                    var flags = pfxFlag | TextFormatFlags.HorizontalCenter |
                                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                    TextRenderer.DrawText(g, text, font, r, hcFg, flags);
                }
                return;
            }

            // Step 1: flood-fill the whole rectangle with the parent background.
            // The rounded-rect shape is drawn on top; anything outside its curves
            // (the four corner regions) will show this background colour, creating
            // the illusion of transparent corners.
            g.Clear(parentBg == default ? Fluent.BgPage : parentBg);

            // Step 2: pick the base fill colour from the variant, then darken it
            // slightly for hover and more for press, giving tactile visual feedback.
            Color baseBg = style switch
            {
                FluentButton.Variant.Danger  => Fluent.Danger,
                FluentButton.Variant.Success => Fluent.Success,
                FluentButton.Variant.Neutral => Fluent.Neutral,
                _                            => Fluent.Accent,   // Primary and any future variants
            };

            // The pressed state gets a stronger darkening than hover so the user
            // can feel (visually) that the button is being pushed in.
            Color bg = pressed ? Darken(baseBg, 0.12f)
                     : hovered ? Darken(baseBg, 0.07f)
                     :           baseBg;

            // Step 3: draw the filled rounded rectangle.
            // We use r.Width - 1 / r.Height - 1 because GDI+ draws the bottom-right
            // pixel one unit outside the given rectangle — subtracting 1 keeps
            // everything inside the control's bounds and avoids clipping artefacts.
            var paint = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
            using var path = Fluent.RoundedRect(paint, radius);

            using (var br = new SolidBrush(bg))
                g.FillPath(br, path);

            // Step 4 (Neutral only): draw an outline border.
            // Neutral buttons have a subtle grey background and rely on the border
            // to define their shape, similar to a "secondary" button style.
            if (style == FluentButton.Variant.Neutral)
            {
                // Use a slightly darker border on hover to reinforce the interactive feel.
                var bc = hovered ? Fluent.BorderInput : Fluent.BorderCard;
                using var pen = new Pen(bc);
                g.DrawPath(pen, path);
            }

            // Step 5 (disabled): overlay a semi-transparent white wash.
            // This dims the colours without replacing them, so the button still
            // looks like itself — just clearly unavailable.
            if (!enabled)
            {
                using var dim = new SolidBrush(Color.FromArgb(100, 255, 255, 255));
                g.FillPath(dim, path);
            }

            // Step 6: choose foreground (text/icon) colour.
            // Neutral uses dark text on its light background; other variants use
            // white text on their coloured backgrounds.
            Color fg = (style == FluentButton.Variant.Neutral)
                ? (enabled ? Fluent.TextPrimary : Fluent.TextHint)
                : (enabled ? Color.White : Color.FromArgb(160, 255, 255, 255));

            // Step 7: draw icon glyph + label, or label alone.
            // Prefix flag for mnemonic underline rendering:
            //   HidePrefix — '&' stripped from display, underline hidden (cues off)
            //   (no flag)  — '&' stripped from display, underline always shown (cues on)
            var prefixFlag = showKeyboardCues ? TextFormatFlags.Default : TextFormatFlags.HidePrefix;

            if (!string.IsNullOrEmpty(icon))
            {
                // When an icon is present we need to lay out icon and text side-by-side,
                // so we measure each piece individually and compute a starting X that
                // centres the combined icon+gap+text block inside the button.
                var mf  = prefixFlag | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                var inf = new Size(int.MaxValue, int.MaxValue);   // "no size limit" for measuring

                int iw  = TextRenderer.MeasureText(icon, Fluent.FontIconSm, inf, mf).Width;
                int tw  = string.IsNullOrEmpty(text) ? 0
                        : TextRenderer.MeasureText(text, font, inf, mf).Width;
                int gap = string.IsNullOrEmpty(text) ? 0 : 4;   // 4px gap between icon and text

                // Centre the block; enforce a minimum left margin of 4px so
                // the icon doesn't touch the left edge in very narrow buttons.
                int startX = Math.Max(4, (r.Width - iw - gap - tw) / 2);

                TextRenderer.DrawText(g, icon, Fluent.FontIconSm, new Rectangle(startX, 0, iw, r.Height), fg, mf);
                if (!string.IsNullOrEmpty(text))
                    TextRenderer.DrawText(g, text, font, new Rectangle(startX + iw + gap, 0, tw + 4, r.Height), fg, mf);
            }
            else
            {
                // Text-only button: centre and apply the resolved prefix flag.
                var flags = prefixFlag | TextFormatFlags.HorizontalCenter |
                            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, text, font, r, fg, flags);
            }
        }

        // ── Dark theme (toolbar) ──────────────────────────────────────

        /// <summary>
        /// Paints a single toolbar button for a dark-background toolbar.
        ///
        /// <para>The structure is similar to <see cref="PaintLight"/> but uses the
        /// dark colour palette from <see cref="Fluent"/> and adds an extra "active"
        /// state (for toggleable toolbar buttons like Shift or Settings).
        /// Icon and text are stacked vertically rather than side-by-side:
        /// the icon occupies most of the button height and the label sits in a
        /// narrow strip at the bottom.</para>
        /// </summary>
        /// <param name="g">The GDI+ drawing surface.</param>
        /// <param name="r">Bounding rectangle of the control.</param>
        /// <param name="text">Label text shown below the icon. May be empty.</param>
        /// <param name="icon">Icon glyph drawn in the upper portion of the button. May be empty.</param>
        /// <param name="font">Font for the small label text.</param>
        /// <param name="active">
        ///   When <c>true</c> the button is in its "on" / toggled state and uses
        ///   a brighter highlight colour.
        /// </param>
        /// <param name="hovered">Mouse is currently over the button.</param>
        /// <param name="pressed">Left mouse button is currently held down.</param>
        /// <param name="enabled">Whether the button can be interacted with.</param>
        /// <param name="radius">Corner radius in pixels.</param>
        /// <param name="parentBg">Parent background colour for corner blending.</param>
        internal static void PaintDark(
            Graphics g, Rectangle r, string text, string icon, Bitmap iconImage,
            Font font, bool active, bool hovered, bool pressed, bool enabled, int radius,
            Color parentBg = default, bool lightTheme = false)
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // High-contrast mode: same flat system-colour approach as PaintLight.
            // Active state uses SystemColors.Highlight / HighlightText so the OS HC
            // theme can express "selected" in its own chosen colours.
            if (SystemInformation.HighContrast)
            {
                Color hcBg = active ? SystemColors.Highlight : SystemColors.Control;
                g.Clear(hcBg);
                ControlPaint.DrawBorder(g, r,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid,
                    SystemColors.ControlText, 1, ButtonBorderStyle.Solid);
                Color hcFg = !enabled       ? SystemColors.GrayText
                           : active         ? SystemColors.HighlightText
                           :                  SystemColors.ControlText;
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
                            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                if (!string.IsNullOrEmpty(icon))
                {
                    int iconH    = r.Height - 18;
                    var iconRect = new Rectangle(0, 1, r.Width, iconH);
                    var textRect = new Rectangle(0, r.Height - 16, r.Width, 16);
                    TextRenderer.DrawText(g, icon, Fluent.FontIconTb, iconRect, hcFg, flags);
                    if (!string.IsNullOrEmpty(text))
                        TextRenderer.DrawText(g, text, font, textRect, hcFg, flags);
                }
                else
                {
                    TextRenderer.DrawText(g, text, font, r, hcFg, flags);
                }
                return;
            }

            // Flood-fill to parent background first — this eliminates the "ghost"
            // remnant of a previous paint that would otherwise accumulate and create
            // a blurry halo effect around the button on repeated hover/leave cycles.
            Color bg = parentBg == default ? (lightTheme ? Fluent.BgPage : Fluent.DarkBg) : parentBg;
            g.Clear(bg);

            var paint = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
            using var path = Fluent.RoundedRect(paint, radius);

            // Overlay colour: active uses accent blue in both themes; hover/press differ.
            //
            // Dark theme: white semi-transparent overlays on the dark background.
            //   Hover alpha 60 → button goes from ~#202020 to ~#424242 — clearly visible.
            //   Press alpha 40 → slightly darker than hover = "pushed in" feel.
            //   (Previous values of 28/16 were too close to the background to see.)
            //
            // Light theme: dark semi-transparent overlays on the light background.
            Color bgFill = lightTheme
                ? (active  ? Fluent.DarkActive                :
                   pressed ? Color.FromArgb(40,  0,   0,   0) :
                   hovered ? Color.FromArgb(18,  0,   0,   0) :
                             Color.Transparent)
                : (active  ? Fluent.DarkActive :
                   pressed ? Color.FromArgb(60, 255, 255, 255) :
                   hovered ? Color.FromArgb(40, 255, 255, 255) :
                             Color.Transparent);

            // Only paint if there is something to paint (alpha > 0).
            // Attempting to fill with a fully transparent brush wastes time and can
            // leave artefacts with some GDI+ rendering paths.
            if (bgFill.A > 0)
                using (var br = new SolidBrush(bgFill))
                    g.FillPath(br, path);

            // Draw a subtle border around each button in light mode so buttons
            // are distinguishable against the light panel background.
            if (lightTheme && !active)
            {
                var bc = hovered
                    ? Color.FromArgb(60, 0, 0, 0)
                    : Color.FromArgb(28, 0, 0, 0);
                using var pen = new Pen(bc);
                g.DrawPath(pen, path);
            }

            if (!enabled)
            {
                // Light theme: white wash dims the coloured button surface (same as FluentButton).
                // Dark theme: skip the black overlay (it made the already-dark button pitch-black
                // and invisible).  Contrast is achieved entirely through the dimmed fg colour
                // and reduced icon opacity below.
                if (lightTheme)
                    using (var dim = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                        g.FillPath(dim, path);
            }

            // Foreground colour for label text and MDL2 glyph icons.
            //
            // IMPORTANT: TextRenderer.DrawText uses GDI (not GDI+) and silently drops
            // the alpha channel — Color.FromArgb(115, 255, 255, 255) renders as pure
            // white, identical to the enabled state.  Disabled contrast must therefore
            // be expressed as a solid pre-computed grey, not a semi-transparent white.
            //
            // Dark theme:  enabled = white (#FFFFFF),  disabled = medium grey (#868686)
            // Light theme: enabled = near-black (#1C1C1C), disabled = medium grey (#ABABAB)
            // Active (both): always white on the blue accent background.
            Color fg = lightTheme
                ? (enabled ? (active ? Color.White : Fluent.TextPrimary)
                           : Color.FromArgb(171, 171, 171))   // solid mid-grey on light bg
                : (enabled ? (active ? Color.White : Fluent.DarkText)
                           : Color.FromArgb(134, 134, 134));  // solid mid-grey on dark bg

            if (iconImage != null)
            {
                // Bitmap icon: draw scaled and centred in the upper portion of the button,
                // with the text label in the fixed 16-px strip at the bottom.
                int iconH = r.Height - 18;   // leave 18 px for the label strip
                float scale = Math.Min(
                    (float)(r.Width - 4) / iconImage.Width,
                    (float)(iconH  - 4) / iconImage.Height);
                int drawW = Math.Max(1, (int)(iconImage.Width  * scale));
                int drawH = Math.Max(1, (int)(iconImage.Height * scale));
                int drawX = (r.Width - drawW) / 2;
                int drawY = 1 + (iconH - drawH) / 2;
                var destRect = new Rectangle(drawX, drawY, drawW, drawH);

                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                if (!enabled)
                {
                    // Dim the icon for the disabled state using a colour matrix.
                    // 0.30 alpha keeps the icon recognisable while making the enabled/disabled
                    // contrast clearly visible, particularly in the dark theme where there is
                    // no background wash to help distinguish the two states.
                    using var ia = new System.Drawing.Imaging.ImageAttributes();
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.30f };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(iconImage, destRect,
                                0, 0, iconImage.Width, iconImage.Height,
                                GraphicsUnit.Pixel, ia);
                }
                else
                {
                    g.DrawImage(iconImage, destRect);
                }

                if (!string.IsNullOrEmpty(text))
                {
                    var textRect = new Rectangle(0, r.Height - 16, r.Width, 16);
                    TextRenderer.DrawText(g, text, font, textRect, fg,
                        TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
            else if (!string.IsNullOrEmpty(icon))
            {
                // Glyph icon (MDL2 Assets font): stacked icon + label layout.
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
                // Text-only toolbar button: centre it in the full rectangle.
                TextRenderer.DrawText(g, text, font, r, fg,
                    TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        // ── Card / group-panel background ─────────────────────────────

        /// <summary>
        /// Paints a WinUI 3-style "card" panel — a white rounded rectangle with a
        /// subtle border, a coloured accent strip on the left edge, and a section title.
        ///
        /// <para>Cards are used in editor dialogs to visually group related controls
        /// (e.g. "Key Appearance", "Key Behaviour").  The accent strip colour matches
        /// the category, making it easy to scan a dialog and find the right section.</para>
        ///
        /// <para>The hosting <see cref="System.Windows.Forms.Panel"/>'s
        /// <c>BackColor</c> should be set to <see cref="Fluent.BgPage"/> so that the
        /// rounded corners of the card blend seamlessly into the form background.</para>
        /// </summary>
        /// <param name="g">GDI+ drawing surface (typically from a Panel's Paint event).</param>
        /// <param name="w">Full width of the card in pixels.</param>
        /// <param name="h">Full height of the card in pixels.</param>
        /// <param name="title">Section heading displayed in the top-left of the card.</param>
        /// <param name="accentColor">Colour of the vertical strip painted on the left edge.</param>
        /// <param name="hdrH">Height in pixels of the header area (title + divider line).</param>
        internal static void PaintCard(
            Graphics g, int w, int h, string title, Color accentColor, int hdrH,
            bool dark = false)
        {
            // In high-contrast mode the panel's BackColor (set to SystemColors.Control by
            // ApplyDialogTheme) already provides the correct system-theme background.
            // Custom painting would override the OS colours, so we skip it entirely.
            if (SystemInformation.HighContrast) return;

            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color cardFill   = dark ? Color.FromArgb(48, 48, 48)    : Fluent.BgCard;
            Color cardBorder = dark ? Color.FromArgb(72, 72, 72)    : Fluent.BorderCard;
            Color titleColor = dark ? Color.FromArgb(230, 230, 230) : Fluent.TextPrimary;

            // Draw the card background with a rounded outline.
            var cardRect = new Rectangle(0, 0, w - 1, h - 1);
            using var cardPath = Fluent.RoundedRect(cardRect, Fluent.RadiusCard);
            using (var br = new SolidBrush(cardFill))
                g.FillPath(br, cardPath);
            using (var pen = new Pen(cardBorder))
                g.DrawPath(pen, cardPath);

            // Draw the vertical accent bar on the left edge.
            // It is inset from the card edge (x=2) and has 12px top/bottom margins
            // so it floats visually within the card rather than touching the border.
            var accentRect = new Rectangle(2, 12, 4, h - 24);
            using var accentPath = Fluent.RoundedRect(accentRect, 2);
            using (var br = new SolidBrush(accentColor))
                g.FillPath(br, accentPath);

            // Draw the section title, indented to clear the accent bar (left=14).
            var titleRect = new Rectangle(14, 0, w - 18, hdrH);
            TextRenderer.DrawText(g, title, Fluent.FontTitle, titleRect,
                titleColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter);

            // Draw a thin horizontal divider line below the title row to separate
            // the header from the content area of the card.
            using var divPen = new Pen(cardBorder);
            g.DrawLine(divPen, 2, hdrH, w - 3, hdrH);
        }

        // ── Dialog dark/light theming ─────────────────────────────────

        /// <summary>
        /// Recursively applies a dark or light colour theme to every control in a dialog.
        /// Call this after <c>BuildUI()</c> so initial colours set during construction are
        /// overridden by the chosen theme.
        ///
        /// <para>Rules:
        /// <list type="bullet">
        ///   <item><see cref="FluentButton"/> instances paint themselves — left untouched.</item>
        ///   <item>Controls whose <c>Tag</c> is the string <c>"notheme"</c> are skipped entirely
        ///         (including their children), so preview panels keep their key colours.</item>
        ///   <item>Colour-swatch panels (those whose <c>Tag</c> is a <see cref="System.Windows.Forms.TextBox"/>)
        ///         keep their swatch <c>BackColor</c> but their children are still themed.</item>
        ///   <item>Controls in <paramref name="skip"/> are skipped (and their children).</item>
        /// </list></para>
        /// </summary>
        internal static void ApplyDialogTheme(Control root, bool dark, params Control[] skip)
        {
            var skipSet  = new HashSet<Control>(skip);

            // High-contrast mode: replace all custom colours with OS system colours so the
            // Windows-chosen HC theme (e.g. "Black", "High Contrast White") shows correctly.
            if (SystemInformation.HighContrast)
            {
                root.BackColor = SystemColors.Control;
                root.ForeColor = SystemColors.ControlText;
                ApplyHCChildren(root, skipSet);
                return;
            }

            Color formBg  = dark ? Fluent.DarkBg                 : Fluent.BgPage;
            Color cardBg  = dark ? Color.FromArgb(48, 48, 48)    : Fluent.BgCard;
            Color inputBg = dark ? Color.FromArgb(58, 58, 58)    : Fluent.BgInput;
            Color fg      = dark ? Color.FromArgb(230, 230, 230) : Fluent.TextPrimary;

            root.BackColor = formBg;
            ApplyThemeChildren(root, cardBg, inputBg, fg, skipSet);
        }

        /// <summary>
        /// Recursive worker that applies system colours throughout a dialog tree when
        /// Windows high-contrast mode is active.  Standard child controls handle their
        /// own HC rendering automatically once BackColor / ForeColor are reset to the
        /// system palette; <see cref="FluentButton"/> instances paint themselves and are
        /// left alone here (they detect HC mode in <see cref="PaintLight"/>/<see cref="PaintDark"/>).
        /// </summary>
        private static void ApplyHCChildren(Control parent, HashSet<Control> skip)
        {
            foreach (Control c in parent.Controls)
            {
                if (skip.Contains(c)) continue;
                if (c is FluentButton) continue;
                if (c.Tag is string t && t == "notheme") continue;

                switch (c)
                {
                    case Panel pnl:
                        pnl.BackColor = SystemColors.Control;
                        pnl.Invalidate();   // triggers PaintCard, which returns early in HC mode
                        break;
                    case Label lbl:
                        lbl.ForeColor = SystemColors.ControlText;
                        lbl.BackColor = Color.Transparent;
                        break;
                    case TextBox txt:
                        txt.BackColor = SystemColors.Window;
                        txt.ForeColor = SystemColors.WindowText;
                        break;
                    case ComboBox cmb:
                        cmb.BackColor = SystemColors.Window;
                        cmb.ForeColor = SystemColors.WindowText;
                        break;
                    case NumericUpDown nud:
                        nud.BackColor = SystemColors.Window;
                        nud.ForeColor = SystemColors.WindowText;
                        break;
                    case CheckBox chk:
                        chk.ForeColor = SystemColors.ControlText;
                        break;
                    case ListBox lst:
                        lst.BackColor = SystemColors.Window;
                        lst.ForeColor = SystemColors.WindowText;
                        break;
                }

                if (c.HasChildren)
                    ApplyHCChildren(c, skip);
            }
        }

        /// <summary>Recursive worker for <see cref="ApplyDialogTheme"/>.</summary>
        private static void ApplyThemeChildren(
            Control parent, Color cardBg, Color inputBg, Color fg,
            HashSet<Control> skip)
        {
            foreach (Control c in parent.Controls)
            {
                if (skip.Contains(c)) continue;
                if (c is FluentButton) continue;
                if (c.Tag is string t && t == "notheme") continue;

                // Colour-swatch buttons: keep the swatch colour, but still recurse into children.
                bool isSwatch = c is ColorSwatchButton && c.Tag is TextBox;

                if (!isSwatch)
                {
                    switch (c)
                    {
                        case Panel pnl:
                            pnl.BackColor = cardBg;
                            pnl.Invalidate();
                            break;
                        case Label lbl:
                            lbl.ForeColor = fg;
                            break;
                        case TextBox txt:
                            txt.BackColor = inputBg;
                            txt.ForeColor = fg;
                            break;
                        case ComboBox cmb:
                            cmb.BackColor = inputBg;
                            cmb.ForeColor = fg;
                            break;
                        case NumericUpDown nud:
                            nud.BackColor = inputBg;
                            nud.ForeColor = fg;
                            break;
                        case CheckBox chk:
                            chk.ForeColor = fg;
                            break;
                        case ListBox lst:
                            lst.BackColor = inputBg;
                            lst.ForeColor = fg;
                            break;
                    }
                }

                if (c.HasChildren)
                    ApplyThemeChildren(c, cardBg, inputBg, fg, skip);
            }
        }

        // ── Helper ────────────────────────────────────────────────────

        /// <summary>
        /// Returns a new <see cref="Color"/> that is darker than the input by the
        /// given fractional amount.
        ///
        /// <para>Each RGB channel is multiplied by <c>(1 - amount)</c>.
        /// An <paramref name="amount"/> of 0.10 means "10% darker".
        /// The alpha channel is preserved unchanged.
        /// <see cref="Math.Max"/> clamps each channel at 0 so no channel wraps
        /// around to a negative value (which would overflow the byte range).</para>
        /// </summary>
        /// <param name="c">The original colour to darken.</param>
        /// <param name="amount">
        ///   Fraction in [0, 1].  0 = no change, 1 = black.
        ///   Values like 0.07 (hover) and 0.12 (press) give subtle feedback.
        /// </param>
        private static Color Darken(Color c, float amount)
        {
            int r = Math.Max(0, (int)(c.R * (1f - amount)));
            int g = Math.Max(0, (int)(c.G * (1f - amount)));
            int b = Math.Max(0, (int)(c.B * (1f - amount)));
            return Color.FromArgb(c.A, r, g, b);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ColorSwatchButton — keyboard-reachable colour swatch.
    //
    //  A minimal Button subclass used as a colour swatch in AddColorRow().
    //  It replaces the plain Panel that was mouse-only, so it participates
    //  in the Tab order and opens the colour picker on Space/Enter natively.
    //
    //  Focus indicator: a two-tone ring (white outer + dark inner) that is
    //  visible against any swatch colour, satisfying WCAG 2.1 AA §2.4.7.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A flat <see cref="Button"/> used as a colour swatch in colour-row controls.
    /// Participates in Tab order (<c>TabStop = true</c>) and opens the colour
    /// picker on Space or Enter via the standard <c>Button.Click</c> event.
    ///
    /// <para>A two-tone focus ring (white outer / dark inner) is painted over the
    /// swatch colour when the button has keyboard focus, satisfying WCAG 2.1 AA
    /// §2.4.7 on any background colour.</para>
    /// </summary>
    public class ColorSwatchButton : Button
    {
        /// <summary>
        /// Initialises the swatch with a flat appearance and keyboard accessibility.
        /// </summary>
        public ColorSwatchButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            TabStop = true;
            Cursor  = Cursors.Hand;
            Text    = "";   // pure colour display — no label
        }

        /// <summary>Triggers a repaint when focus is gained so the focus ring appears.</summary>
        protected override void OnGotFocus(EventArgs e)  { Invalidate(); base.OnGotFocus(e); }

        /// <summary>Triggers a repaint when focus is lost so the focus ring disappears.</summary>
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        /// <summary>
        /// Paints the swatch and, when focused via keyboard, overlays a two-tone focus
        /// ring that is visible against any swatch colour.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);   // draws BackColor fill + FlatStyle border

            // Two-tone focus ring: white outer ring + dark inner ring.
            // White is visible against dark swatches; the dark ring is visible against light ones.
            // ShowFocusCues is false for mouse clicks, so this only appears during Tab navigation.
            if (Focused && ShowFocusCues)
            {
                var g = e.Graphics;
                using var wpen = new Pen(Color.White, 2f);
                g.DrawRectangle(wpen, new Rectangle(2, 2, Width - 5, Height - 5));
                using var dpen = new Pen(Color.FromArgb(30, 30, 30), 1f);
                g.DrawRectangle(dpen, new Rectangle(4, 4, Width - 9, Height - 9));
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  KeyChipPanel — double-buffered Panel for the selected-key chip.
    //
    //  Used on the edit toolbar to show the currently selected key as a
    //  small graphical chip (mini key) instead of a plain text label.
    //  Double buffering eliminates flicker when the panel is repainted
    //  during window resizes or theme switches.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A <see cref="Panel"/> subclass with owner-draw double buffering,
    /// used to host the selected-key chip on the edit toolbar.
    /// The actual painting is performed by the <c>Paint</c> event handler
    /// wired up in <c>KeyboardForm.BuildToolbarEditButtons</c>.
    /// </summary>
    internal class KeyChipPanel : Panel
    {
        /// <summary>
        /// Enables the owner-draw pipeline and double buffering so the
        /// host's <c>Paint</c> handler can draw without flicker.
        /// </summary>
        public KeyChipPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        /// <summary>Suppress background erase — the Paint handler fills everything.</summary>
        protected override void OnPaintBackground(PaintEventArgs e) { /* suppress */ }
    }
}

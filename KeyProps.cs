using System.Drawing;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Holds all the visual and behavioral properties for a single key on the on-screen keyboard.
    ///
    /// Think of this class as a "recipe card" for one key. It stores:
    ///   - What text to show on the key face (the label)
    ///   - What text/character to actually send to the computer when the key is pressed (send)
    ///   - Alternate versions of label and send when Shift or AltGr is active
    ///   - Visual styling: font, colors, border
    ///   - Which named group this key belongs to (for shared styling)
    ///
    /// Having label and send as separate values is important: for example, the Backspace key
    /// might display "⌫" but send the special string "{BACKSPACE}" to the OS.
    ///
    /// A KeyProps on its own does not know where it lives in the layout grid — that is the
    /// job of the grid/layout classes. KeyProps only knows "what this key looks and acts like".
    /// </summary>
    public class KeyProps
    {
        // ── What is shown on the key face ────────────────────────────────────────

        /// <summary>The text displayed on the key in its default (unshifted) state.</summary>
        public string Label           { get; set; }

        /// <summary>
        /// The string sent to the target application when this key is pressed (unshifted).
        /// This can be a plain character like "a", a Unicode string, or a special SendKeys
        /// sequence like "{ENTER}" or "^c" (Ctrl+C).
        /// </summary>
        public string Send            { get; set; }

        /// <summary>
        /// The text displayed on the key when Shift is active.
        /// If empty, the key does not change its label under Shift — <see cref="Label"/> is used.
        /// </summary>
        public string ShiftLabel      { get; set; }

        /// <summary>
        /// The string sent when this key is pressed while Shift is active.
        /// If empty, <see cref="Send"/> is used even when Shift is on.
        /// </summary>
        public string ShiftSend       { get; set; }

        /// <summary>
        /// The text displayed on the key when AltGr (right Alt) is active.
        /// AltGr is used on many European keyboard layouts to access a third character
        /// on each key (e.g. € on the E key on a French keyboard).
        /// If empty, the key has no AltGr label.
        /// </summary>
        public string AltGrLabel      { get; set; }

        /// <summary>
        /// The string sent when this key is pressed while AltGr is active.
        /// If empty, <see cref="Send"/> is used.
        /// </summary>
        public string AltGrSend       { get; set; }

        // ── Visual styling ────────────────────────────────────────────────────

        /// <summary>
        /// The font family name for this key's label text (e.g. "Arial", "Segoe UI").
        /// An empty string means "use whatever font the keyboard-wide settings define".
        /// </summary>
        public string FontName        { get; set; }

        /// <summary>
        /// The font size for this key's label text in points.
        /// 0 means "use the keyboard-wide default font size".
        /// </summary>
        public int    FontSize        { get; set; }

        /// <summary>
        /// The color used to draw the label text on this key.
        /// <see cref="Color.Empty"/> means "use the keyboard-wide default font color".
        /// </summary>
        public Color  FontColor       { get; set; }

        /// <summary>
        /// The background fill color of this key.
        /// <see cref="Color.Empty"/> means "use the keyboard-wide default key color".
        /// </summary>
        public Color  KeyColor        { get; set; }

        /// <summary>
        /// The color drawn around the edge (border) of this key.
        /// <see cref="Color.Empty"/> means "use the keyboard-wide default border color".
        /// </summary>
        public Color  BorderColor     { get; set; }

        /// <summary>
        /// How thick (in pixels) the border around this key should be.
        /// -1 means "use the keyboard-wide default border thickness".
        ///  0 means no border at all for this key.
        /// </summary>
        public int    BorderThickness { get; set; }

        // ── Group membership ──────────────────────────────────────────────────

        /// <summary>
        /// The name of the styling group this key belongs to.
        /// Groups let multiple keys share a common visual style (color scheme, font, etc.)
        /// without having to set it on each key individually.
        /// An empty string means this key does not belong to any group.
        /// </summary>
        public string GroupName       { get; set; } = "";

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new KeyProps with label/send values. All styling properties are left at
        /// their "use global default" sentinel values so the keyboard-wide theme applies unless
        /// you explicitly override them afterwards.
        /// </summary>
        /// <param name="label">Text shown on the key face in its normal (unshifted) state.</param>
        /// <param name="send">Text/sequence sent to the OS when the key is pressed normally.</param>
        /// <param name="shiftLabel">Text shown when Shift is active (optional; defaults to empty = same as label).</param>
        /// <param name="shiftSend">Text sent when Shift is active (optional; defaults to empty = same as send).</param>
        /// <param name="altGrLabel">Text shown when AltGr is active (optional).</param>
        /// <param name="altGrSend">Text sent when AltGr is active (optional).</param>
        public KeyProps(string label, string send,
                        string shiftLabel = "", string shiftSend = "",
                        string altGrLabel = "", string altGrSend = "")
        {
            Label      = label;  Send      = send;
            ShiftLabel = shiftLabel; ShiftSend = shiftSend;
            AltGrLabel = altGrLabel; AltGrSend = altGrSend;

            // Sentinel values: these special "empty/zero/-1" values are the signal
            // to the rendering code that this key defers to the keyboard-wide style.
            FontName        = "";        // "" = use global
            FontSize        = 0;
            FontColor       = Color.Empty;  // Empty = use global
            KeyColor        = Color.Empty;  // Empty = use global
            BorderColor     = Color.Empty;  // Empty = use global
            BorderThickness = -1;           // -1 = use global
        }

        // ── Methods ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an independent copy of this KeyProps with identical values.
        ///
        /// Why clone? When you copy a key in the editor, you want a completely separate
        /// object so that editing the copy does not change the original. Without Clone()
        /// you would only get a second reference to the same object.
        /// </summary>
        /// <returns>A new KeyProps with the same label, send, and styling values.</returns>
        public KeyProps Clone() => new KeyProps(Label, Send, ShiftLabel, ShiftSend, AltGrLabel, AltGrSend)
        {
            FontName        = FontName,
            FontSize        = FontSize,
            FontColor       = FontColor,
            KeyColor        = KeyColor,
            BorderColor     = BorderColor,
            BorderThickness = BorderThickness,
            GroupName       = GroupName,
        };

        /// <summary>
        /// Returns the text that should be displayed on the key face given the current
        /// modifier state. The priority order is: AltGr first, then Shift, then normal.
        ///
        /// This is used by the rendering code every time it repaints the keyboard.
        /// </summary>
        /// <param name="shifted">True when the Shift key is currently held/toggled on.</param>
        /// <param name="altGr">True when the AltGr (right Alt) key is currently held/toggled on.</param>
        /// <returns>
        /// The AltGr label if altGr is true and an AltGr label exists;
        /// the Shift label if shifted is true and a Shift label exists;
        /// otherwise the normal label.
        /// </returns>
        public string GetDisplayLabel(bool shifted, bool altGr = false)
        {
            // AltGr takes highest priority — check it before Shift.
            // This mirrors how real keyboard firmware works.
            if (altGr  && !string.IsNullOrEmpty(AltGrLabel)) return AltGrLabel;
            if (shifted && !string.IsNullOrEmpty(ShiftLabel)) return ShiftLabel;
            return Label;
        }

        /// <summary>
        /// Returns the string that should be sent to the target application when this key
        /// is pressed, taking the current modifier state into account.
        ///
        /// Same priority logic as <see cref="GetDisplayLabel"/>: AltGr > Shift > normal.
        /// </summary>
        /// <param name="shifted">True when the Shift key is currently held/toggled on.</param>
        /// <param name="altGr">True when the AltGr (right Alt) key is currently held/toggled on.</param>
        /// <returns>
        /// The AltGr send string if altGr is true and one exists;
        /// the Shift send string if shifted is true and one exists;
        /// otherwise the normal send string.
        /// </returns>
        public string GetSend(bool shifted, bool altGr = false)
        {
            if (altGr  && !string.IsNullOrEmpty(AltGrSend)) return AltGrSend;
            if (shifted && !string.IsNullOrEmpty(ShiftSend)) return ShiftSend;
            return Send;
        }
    }
}

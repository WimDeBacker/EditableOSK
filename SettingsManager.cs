using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Xml;

namespace OnScreenKeyboard
{
    // ══════════════════════════════════════════════════════════════════════
    // VisualTheme — colours, fonts, opacity (everything purely cosmetic)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Holds every setting that controls how the keyboard looks: background
    /// colour, font, key colours, border style, and overall window opacity.
    /// Nothing here affects which keys exist or how they behave — that is
    /// handled by <see cref="GridLayout"/> and <see cref="LayoutMeta"/>.
    /// </summary>
    /// <remarks>
    /// All colour properties default to a dark-blue palette defined as
    /// HTML hex strings.  FontSize 0 is the sentinel meaning "choose a
    /// size automatically based on key height".
    /// </remarks>
    public class VisualTheme
    {
        /// <summary>The colour that fills the keyboard window behind all keys.</summary>
        public Color  BackgroundColor  { get; set; } = ColorTranslator.FromHtml("#1A1A2E");

        /// <summary>
        /// Window transparency from 0.0 (invisible) to 1.0 (fully opaque).
        /// Values below 0.2 are not allowed because the window would become
        /// too hard to interact with.
        /// </summary>
        public double Opacity          { get; set; } = 1.0;

        /// <summary>Name of the font used to draw key labels (e.g. "Arial").</summary>
        public string FontName         { get; set; } = "Arial";

        /// <summary>
        /// Point size for key labels.  0 means "auto-size" — the keyboard will
        /// pick the largest size that still fits inside the key rectangle.
        /// </summary>
        public int    FontSize         { get; set; } = 0;

        /// <summary>Colour used to draw key label text.</summary>
        public Color  FontColor        { get; set; } = ColorTranslator.FromHtml("#E0E0FF");

        /// <summary>Default fill colour for key buttons.</summary>
        public Color  KeyColor         { get; set; } = ColorTranslator.FromHtml("#2D2D4A");

        /// <summary>Default colour for the border drawn around each key.</summary>
        public Color  BorderColor      { get; set; } = ColorTranslator.FromHtml("#3C3C5A");

        /// <summary>Width in pixels of the border drawn around each key.</summary>
        public int    BorderThickness  { get; set; } = 1;

        /// <summary>
        /// Creates a new <see cref="VisualTheme"/> with identical values to
        /// this one.  Useful when you want to work with a temporary copy
        /// without modifying the live theme until the user clicks "Apply".
        /// </summary>
        /// <returns>A new object with the same field values as this instance.</returns>
        public VisualTheme Clone() => new VisualTheme
        {
            BackgroundColor = BackgroundColor,
            Opacity         = Opacity,
            FontName        = FontName,
            FontSize        = FontSize,
            FontColor       = FontColor,
            KeyColor        = KeyColor,
            BorderColor     = BorderColor,
            BorderThickness = BorderThickness,
        };

        /// <summary>
        /// Overwrites every field in this instance with the values from
        /// <paramref name="src"/>, keeping the same object reference.
        /// Use this instead of <see cref="Clone"/> when other code already
        /// holds a reference to this object and must see the updated values.
        /// </summary>
        /// <param name="src">The theme to copy values from.</param>
        public void CopyFrom(VisualTheme src)
        {
            BackgroundColor = src.BackgroundColor;
            Opacity         = src.Opacity;
            FontName        = src.FontName;
            FontSize        = src.FontSize;
            FontColor       = src.FontColor;
            KeyColor        = src.KeyColor;
            BorderColor     = src.BorderColor;
            BorderThickness = src.BorderThickness;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // WindowState — window size and chrome (not layout, not visual style)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stores settings that control the application window itself:
    /// its size, whether the title bar is shown, and whether it floats
    /// above all other windows.  This is separate from the visual style
    /// (<see cref="VisualTheme"/>) and the key layout (<see cref="LayoutMeta"/>).
    /// </summary>
    public class WindowState
    {
        /// <summary>Width of the keyboard window in pixels (default 1050).</summary>
        public int  WindowWidth  { get; set; } = 1050;

        /// <summary>Height of the keyboard window in pixels (default 290).</summary>
        public int  WindowHeight { get; set; } = 290;

        /// <summary>
        /// When true the standard Windows title bar and border are hidden,
        /// giving a cleaner floating-keyboard look.  The window can still
        /// be moved by dragging the keyboard body itself.
        /// </summary>
        public bool HideTitlebar { get; set; } = false;

        /// <summary>
        /// When true the keyboard window stays on top of all other
        /// application windows, so it is never covered while typing.
        /// </summary>
        public bool AlwaysOnTop  { get; set; } = true;

        /// <summary>
        /// Creates a new <see cref="WindowState"/> with identical values to
        /// this one.
        /// </summary>
        /// <returns>A new object with the same field values as this instance.</returns>
        public WindowState Clone() => new WindowState
        {
            WindowWidth  = WindowWidth,
            WindowHeight = WindowHeight,
            HideTitlebar = HideTitlebar,
            AlwaysOnTop  = AlwaysOnTop,
        };

        /// <summary>
        /// Overwrites every field in this instance with the values from
        /// <paramref name="src"/>, keeping the same object reference.
        /// </summary>
        /// <param name="src">The window state to copy values from.</param>
        public void CopyFrom(WindowState src)
        {
            WindowWidth  = src.WindowWidth;
            WindowHeight = src.WindowHeight;
            HideTitlebar = src.HideTitlebar;
            AlwaysOnTop  = src.AlwaysOnTop;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // LayoutMeta — language, gear position, last file, sticky modifiers
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stores behavioural and structural settings that go alongside the
    /// key grid: the UI language, whether modifier keys are "sticky"
    /// (stay pressed after one tap), where the settings-gear icon lives,
    /// and the path of the most recently opened layout file.
    /// </summary>
    public class LayoutMeta
    {
        /// <summary>
        /// Two-letter language code (e.g. "en", "nl") used to load the
        /// correct localisation file for the editor UI.
        /// </summary>
        public string Language        { get; set; } = "en";

        /// <summary>
        /// When true, modifier keys (Shift, Ctrl, Alt) act as toggles: one
        /// press activates them, the next normal keypress uses them and they
        /// release automatically.  This makes the keyboard usable with a
        /// single pointer without holding two keys at once.
        /// </summary>
        public bool   StickyModifiers { get; set; } = true;

        /// <summary>
        /// When true, a key must be held for a moment (long-press) before the
        /// editor opens for that key.  A regular quick tap still sends the
        /// character.  Prevents accidental editor openings during normal typing.
        /// </summary>
        public bool   HoldToEdit      { get; set; } = false;

        /// <summary>
        /// Zero-based grid row where the settings-gear button is placed
        /// (default 0 = first row).
        /// </summary>
        public int    GearRow         { get; set; } = 0;

        /// <summary>
        /// Zero-based grid column where the settings-gear button is placed.
        /// -1 is a sentinel meaning "place it in the last column of the grid",
        /// so it stays at the right edge regardless of how many columns there are.
        /// </summary>
        public int    GearCol         { get; set; } = -1;  // -1 = last column

        /// <summary>
        /// Full path of the XML layout file that was open when settings were
        /// last saved.  The application re-opens this file automatically on
        /// the next launch.  Empty string means no file was loaded.
        /// </summary>
        public string LastFile        { get; set; } = "";

        /// <summary>
        /// Creates a new <see cref="LayoutMeta"/> with identical values to
        /// this one.
        /// </summary>
        /// <returns>A new object with the same field values as this instance.</returns>
        public LayoutMeta Clone() => new LayoutMeta
        {
            Language        = Language,
            StickyModifiers = StickyModifiers,
            HoldToEdit      = HoldToEdit,
            GearRow         = GearRow,
            GearCol         = GearCol,
            LastFile        = LastFile,
        };

        /// <summary>
        /// Overwrites every field in this instance with the values from
        /// <paramref name="src"/>, keeping the same object reference.
        /// </summary>
        /// <param name="src">The layout meta to copy values from.</param>
        public void CopyFrom(LayoutMeta src)
        {
            Language        = src.Language;
            StickyModifiers = src.StickyModifiers;
            HoldToEdit      = src.HoldToEdit;
            GearRow         = src.GearRow;
            GearCol         = src.GearCol;
            LastFile        = src.LastFile;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // SettingsManager — XML read / write
    // New format: <Theme> (visual + groups) + <Layout> (structure + keys)
    // Old format: flat <Global> + sibling <Group>/<Key> — still loads fine
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads and writes the application's XML settings file.
    /// <para>
    /// The file contains everything needed to recreate the keyboard from
    /// scratch: visual theme, window dimensions, key grid, key groups,
    /// and behavioural flags.
    /// </para>
    /// <para>
    /// Two XML formats are understood:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>New format</b> (written by current code): visual settings live in a
    ///     <c>&lt;Theme&gt;</c> element; structural settings in a <c>&lt;Layout&gt;</c>
    ///     element.  Key groups are children of <c>&lt;Theme&gt;</c> and individual
    ///     keys are children of <c>&lt;Layout&gt;</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Old format</b> (read-only backwards compatibility): a single flat
    ///     <c>&lt;Global&gt;</c> element holds all settings, with <c>&lt;Group&gt;</c>
    ///     and <c>&lt;Key&gt;</c> elements as direct siblings inside
    ///     <c>&lt;OnScreenKeyboard&gt;</c>.
    ///   </description></item>
    /// </list>
    /// </para>
    /// All methods are static — you never create an instance of this class.
    /// </summary>
    public static class SettingsManager
    {
        /// <summary>
        /// The default path where <c>settings.xml</c> is read from and
        /// written to: the same folder as the running executable.
        /// </summary>
        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");

        // ── Save ─────────────────────────────────────────────────────

        /// <summary>
        /// Serialises the current keyboard state to an XML file at
        /// <paramref name="path"/>.  If the file already exists it is first
        /// copied to <c>&lt;path&gt;.bak</c> so the previous state can be
        /// recovered in case of a mistake.
        /// </summary>
        /// <param name="layout">The full key grid including all cells and groups.</param>
        /// <param name="theme">Visual settings (colours, font, opacity).</param>
        /// <param name="window">Window size and chrome settings.</param>
        /// <param name="meta">Behavioural settings and gear-button position.</param>
        /// <param name="path">Absolute path of the XML file to write.</param>
        public static void SaveSettings(GridLayout layout,
                                        VisualTheme theme, WindowState window, LayoutMeta meta,
                                        string path)
        {
            // Keep a rolling backup so accidental Apply/overwrite is always recoverable.
            // To restore: rename  filename.xml.bak  →  filename.xml
            if (File.Exists(path))
                File.Copy(path, path + ".bak", overwrite: true);

            // XmlWriterSettings.Indent = true produces nicely indented XML,
            // making the file human-readable and easy to diff in version control.
            var xs = new XmlWriterSettings { Indent = true };
            using var writer = XmlWriter.Create(path, xs);
            writer.WriteStartDocument();
            writer.WriteStartElement("OnScreenKeyboard");
            WriteTheme(writer, theme, layout);
            WriteLayout(writer, window, meta, layout);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        /// <summary>
        /// Writes the <c>&lt;Theme&gt;</c> element which holds all visual
        /// settings as XML attributes, followed by child <c>&lt;Group&gt;</c>
        /// elements for each named key group.
        /// </summary>
        /// <remarks>
        /// Example output:
        /// <code>
        /// &lt;Theme BackgroundColor="1A1A2E" Opacity="1.00" FontName="Arial" ...&gt;
        ///   &lt;Group Name="Numbers" KeyColor="2D2D4A" /&gt;
        /// &lt;/Theme&gt;
        /// </code>
        /// </remarks>
        // <Theme BackgroundColor="..." ...>
        //   <Group Name="..." ... />
        // </Theme>
        private static void WriteTheme(XmlWriter w, VisualTheme t, GridLayout layout)
        {
            w.WriteStartElement("Theme");
            // Colours are stored as 6-digit hex strings (no '#' prefix) for compactness.
            w.WriteAttributeString("BackgroundColor", Hex(t.BackgroundColor));
            // "F2" format keeps two decimal places; InvariantCulture avoids locale-specific
            // decimal separators (e.g. some locales use a comma instead of a dot).
            w.WriteAttributeString("Opacity",         t.Opacity.ToString("F2", Inv));
            w.WriteAttributeString("FontName",        t.FontName);
            w.WriteAttributeString("FontSize",        t.FontSize.ToString());
            w.WriteAttributeString("FontColor",       Hex(t.FontColor));
            w.WriteAttributeString("KeyColor",        Hex(t.KeyColor));
            w.WriteAttributeString("BorderColor",     Hex(t.BorderColor));
            w.WriteAttributeString("BorderThickness", t.BorderThickness.ToString());
            WriteGroups(w, layout);
            w.WriteEndElement();
        }

        /// <summary>
        /// Writes the <c>&lt;Layout&gt;</c> element which holds structural
        /// and behavioural settings as attributes, followed by child
        /// <c>&lt;Key&gt;</c> elements for every non-trivial grid cell.
        /// </summary>
        /// <remarks>
        /// Example output:
        /// <code>
        /// &lt;Layout Language="en" GridRows="4" GridCols="14" ...&gt;
        ///   &lt;Key Row="0" Col="0" Label="q" Send="q" /&gt;
        /// &lt;/Layout&gt;
        /// </code>
        /// </remarks>
        // <Layout Language="..." GridRows="..." ...>
        //   <Key Row="..." ... />
        // </Layout>
        private static void WriteLayout(XmlWriter w, WindowState ws, LayoutMeta m, GridLayout layout)
        {
            w.WriteStartElement("Layout");
            w.WriteAttributeString("Language",        m.Language ?? "en");
            w.WriteAttributeString("GridRows",        layout.Rows.ToString());
            w.WriteAttributeString("GridCols",        layout.Cols.ToString());
            w.WriteAttributeString("GearRow",         m.GearRow.ToString());
            w.WriteAttributeString("GearCol",         m.GearCol.ToString());
            w.WriteAttributeString("WindowWidth",     ws.WindowWidth.ToString());
            w.WriteAttributeString("WindowHeight",    ws.WindowHeight.ToString());
            // Boolean flags are stored as "1"/"0" rather than "true"/"false" for brevity.
            w.WriteAttributeString("HideTitlebar",    ws.HideTitlebar   ? "1" : "0");
            w.WriteAttributeString("StickyModifiers", m.StickyModifiers ? "1" : "0");
            w.WriteAttributeString("HoldToEdit",      m.HoldToEdit      ? "1" : "0");
            w.WriteAttributeString("AlwaysOnTop",     ws.AlwaysOnTop    ? "1" : "0");
            w.WriteAttributeString("LastFile",        m.LastFile ?? "");
            WriteGrid(w, layout);
            w.WriteEndElement();
        }

        /// <summary>
        /// Returns true if a grid cell is a "pure spacer" — an empty,
        /// unstyled, 1×1 placeholder that exists only to fill a grid position
        /// with no visible content.
        /// </summary>
        /// <remarks>
        /// Pure spacers are omitted from the XML file because the loader
        /// automatically fills any grid position not covered by an explicit
        /// <c>&lt;Key&gt;</c> element.  Skipping them keeps the file small
        /// and focused on keys that actually do something.
        /// <para>
        /// The check for <c>BorderThickness == -1</c> uses the sentinel value
        /// -1 which means "inherit from group or theme" — a key that explicitly
        /// overrides the border thickness would have a value of 0 or higher.
        /// </para>
        /// </remarks>
        // A spacer that has no content, no style override, and is 1×1 adds no information
        // to the XML — the loader auto-fills any uncovered position on read.
        private static bool IsPureSpacer(GridCell cell)
        {
            var p = cell.Props;
            return string.IsNullOrEmpty(p.Label)
                && string.IsNullOrEmpty(p.Send)
                && string.IsNullOrEmpty(p.GroupName)
                && string.IsNullOrEmpty(p.FontName)
                && p.FontSize        == 0
                && p.FontColor.IsEmpty        // Color.Empty means "no override"
                && p.KeyColor.IsEmpty
                && p.BorderColor.IsEmpty
                && p.BorderThickness == -1    // -1 = "inherit", so no explicit override
                && cell.RowSpan == 1
                && cell.ColSpan == 1;
        }

        /// <summary>
        /// Writes one <c>&lt;Group&gt;</c> element for each named key group
        /// in the layout.  Unnamed groups are silently skipped because they
        /// cannot be referenced by keys.
        /// </summary>
        /// <param name="w">The XML writer to write into.</param>
        /// <param name="layout">The layout whose groups should be serialised.</param>
        private static void WriteGroups(XmlWriter w, GridLayout layout)
        {
            foreach (var g in layout.Groups)
            {
                // Groups without a name are anonymous and cannot be assigned to keys,
                // so there is no point saving them.
                if (string.IsNullOrEmpty(g.Name)) continue;
                w.WriteStartElement("Group");
                w.WriteAttributeString("Name",            g.Name);
                w.WriteAttributeString("FontName",        g.FontName ?? "");
                w.WriteAttributeString("FontSize",        g.FontSize.ToString());
                // Empty string for colour means "no group-level override — use the theme default".
                w.WriteAttributeString("FontColor",       g.FontColor.IsEmpty   ? "" : Hex(g.FontColor));
                w.WriteAttributeString("KeyColor",        g.KeyColor.IsEmpty    ? "" : Hex(g.KeyColor));
                w.WriteAttributeString("BorderColor",     g.BorderColor.IsEmpty ? "" : Hex(g.BorderColor));
                w.WriteAttributeString("BorderThickness", g.BorderThickness.ToString());
                w.WriteEndElement();
            }
        }

        /// <summary>
        /// Writes one <c>&lt;Key&gt;</c> element for every non-trivial cell
        /// in the grid.  Pure spacers (see <see cref="IsPureSpacer"/>) are
        /// omitted to keep the file compact.
        /// </summary>
        /// <param name="w">The XML writer to write into.</param>
        /// <param name="layout">The layout whose cells should be serialised.</param>
        private static void WriteGrid(XmlWriter w, GridLayout layout)
        {
            foreach (var cell in layout.Cells)
            {
                if (IsPureSpacer(cell)) continue;   // omit invisible spacers — loader fills gaps
                var p = cell.Props;
                w.WriteStartElement("Key");
                w.WriteAttributeString("Row",             cell.Row.ToString());
                w.WriteAttributeString("Col",             cell.Col.ToString());
                // RowSpan/ColSpan allow a key to span multiple grid cells (like a wide Space bar).
                w.WriteAttributeString("RowSpan",         cell.RowSpan.ToString());
                w.WriteAttributeString("ColSpan",         cell.ColSpan.ToString());
                // GroupName links this key to a KeyGroup for shared visual defaults.
                w.WriteAttributeString("GroupName",       p.GroupName ?? "");
                // Label is what the user sees; Send is the character/sequence actually typed.
                // They can differ, e.g. Label="Enter" Send="\r\n".
                w.WriteAttributeString("Label",           p.Label   ?? "");
                w.WriteAttributeString("Send",            p.Send    ?? "");
                // ShiftLabel/ShiftSend: what this key shows and sends when Shift is active.
                w.WriteAttributeString("ShiftLabel",      p.ShiftLabel  ?? "");
                w.WriteAttributeString("ShiftSend",       p.ShiftSend   ?? "");
                // AltGrLabel/AltGrSend: what this key shows and sends when AltGr is active.
                w.WriteAttributeString("AltGrLabel",      p.AltGrLabel  ?? "");
                w.WriteAttributeString("AltGrSend",       p.AltGrSend   ?? "");
                // Per-key style overrides; empty string means "use group or theme default".
                w.WriteAttributeString("FontName",        p.FontName ?? "");
                w.WriteAttributeString("FontSize",        p.FontSize.ToString());
                w.WriteAttributeString("FontColor",       p.FontColor.IsEmpty ? "" : Hex(p.FontColor));
                w.WriteAttributeString("KeyColor",        p.KeyColor.IsEmpty  ? "" : Hex(p.KeyColor));
                w.WriteAttributeString("BorderColor",     p.BorderColor.IsEmpty ? "" : Hex(p.BorderColor));
                w.WriteAttributeString("BorderThickness", p.BorderThickness.ToString());
                w.WriteEndElement();
            }
        }

        // ── Load ─────────────────────────────────────────────────────

        /// <summary>
        /// Reads an XML settings file and populates the supplied objects in-place,
        /// then returns the reconstructed <see cref="GridLayout"/>.
        /// </summary>
        /// <param name="theme">Receives visual settings (colours, font, opacity).</param>
        /// <param name="window">Receives window size and chrome settings.</param>
        /// <param name="meta">Receives behavioural settings and gear-button position.</param>
        /// <param name="path">Absolute path of the XML file to read.</param>
        /// <returns>
        /// The populated <see cref="GridLayout"/>, or <c>null</c> if the file
        /// does not exist or does not contain a recognised keyboard layout.
        /// </returns>
        /// <remarks>
        /// The method handles both the current format (<c>&lt;Theme&gt;</c> +
        /// <c>&lt;Layout&gt;</c>) and the legacy format (flat <c>&lt;Global&gt;</c>)
        /// transparently — callers do not need to know which format the file uses.
        /// </remarks>
        public static GridLayout LoadSettings(VisualTheme theme, WindowState window, LayoutMeta meta,
                                              string path)
        {
            if (!File.Exists(path)) return null;
            var doc = new XmlDocument();
            doc.Load(path);

            // Detect which format the file uses.
            // New format: <Theme> + <Layout>   Old format: flat <Global> (backward compat)
            var themeNode  = doc.SelectSingleNode("/OnScreenKeyboard/Theme");
            var layoutNode = doc.SelectSingleNode("/OnScreenKeyboard/Layout");
            var globalNode = doc.SelectSingleNode("/OnScreenKeyboard/Global");

            // If neither a <Theme> nor a <Global> element exists, this is not a
            // keyboard layout file (could be some other XML).  Return null to signal failure.
            if (themeNode == null && globalNode == null) return null;   // not a keyboard layout file

            // Visual settings from <Theme> (new) or <Global> (old)
            // In old files, <Global> held both visual and structural settings in one element.
            var tNode = themeNode ?? globalNode;
            theme.BackgroundColor = ParseColor(Attr(tNode,"BackgroundColor",""), theme.BackgroundColor);
            // Opacity must stay between 0.2 and 1.0 — below 0.2 the window becomes
            // practically invisible and difficult to interact with.
            if (double.TryParse(Attr(tNode,"Opacity","1.0"),
                    System.Globalization.NumberStyles.Float, Inv, out double op))
                theme.Opacity = Math.Clamp(op, 0.2, 1.0);
            theme.FontName    = Attr(tNode,"FontName",theme.FontName);
            // FontSize 0 means auto-size; clamp to 200 to prevent absurdly large values.
            if (int.TryParse(Attr(tNode,"FontSize","0"), out int gfs)) theme.FontSize = Math.Clamp(gfs, 0, 200);
            theme.FontColor   = ParseColor(Attr(tNode,"FontColor",""),  theme.FontColor);
            theme.KeyColor    = ParseColor(Attr(tNode,"KeyColor",""),   theme.KeyColor);
            theme.BorderColor = ParseColor(Attr(tNode,"BorderColor",""),theme.BorderColor);
            if (int.TryParse(Attr(tNode,"BorderThickness","1"), out int gbt)) theme.BorderThickness = gbt;

            // Structural settings from <Layout> (new) or <Global> (old)
            var lNode = layoutNode ?? globalNode;
            meta.Language        = Attr(lNode,"Language","en");
            meta.LastFile        = Attr(lNode,"LastFile","");
            // "1" / "0" string comparison — matches how SaveSettings writes these flags.
            meta.StickyModifiers = Attr(lNode,"StickyModifiers","1") == "1";
            meta.HoldToEdit      = Attr(lNode,"HoldToEdit","0")      == "1";

            // Sanity-clamp window size to reasonable display boundaries
            // (600–7680 wide, 180–4320 tall) to prevent windows that are
            // too small to use or larger than any realistic monitor.
            if (int.TryParse(Attr(lNode,"WindowWidth","1050"),  out int ww) && ww>=600 && ww<=7680) window.WindowWidth=ww;
            if (int.TryParse(Attr(lNode,"WindowHeight","290"),  out int wh) && wh>=180 && wh<=4320) window.WindowHeight=wh;
            window.HideTitlebar = Attr(lNode,"HideTitlebar","0") == "1";
            window.AlwaysOnTop  = Attr(lNode,"AlwaysOnTop","1") == "1";

            // Grid dimensions are capped at 50×50 to avoid runaway memory allocation
            // from a corrupt or hand-edited file.
            int gridRows = 2, gridCols = 2;
            if (int.TryParse(Attr(lNode,"GridRows","2"), out int gr) && gr>=1) gridRows = Math.Min(gr,50);
            if (int.TryParse(Attr(lNode,"GridCols","2"), out int gc) && gc>=1) gridCols = Math.Min(gc,50);

            // GearRow/GearCol are optional attributes — if absent (old files) they
            // default to row 0, last column (GearCol = -1).
            string gearRowAttr = Attr(lNode,"GearRow","");
            string gearColAttr = Attr(lNode,"GearCol","");
            meta.GearRow = (!string.IsNullOrEmpty(gearRowAttr) && int.TryParse(gearRowAttr, out int gearR))
                ? Math.Clamp(gearR, 0, Math.Max(0, gridRows - 1)) : 0;
            // Allow -1 as a valid value (means "last column"), but reject anything lower.
            meta.GearCol = (!string.IsNullOrEmpty(gearColAttr) && int.TryParse(gearColAttr, out int gearC))
                ? Math.Max(-1, gearC) : -1;

            var layout = new GridLayout(gridRows, gridCols);

            // Groups: inside <Theme> (new) or siblings of <Global> (old)
            var groupNodes = themeNode != null
                ? doc.SelectNodes("/OnScreenKeyboard/Theme/Group")
                : doc.SelectNodes("/OnScreenKeyboard/Group");
            if (groupNodes != null)
            {
                foreach (XmlNode gn in groupNodes)
                {
                    string gname = Attr(gn, "Name", "");
                    if (string.IsNullOrEmpty(gname)) continue;
                    // Duplicate group names would cause ambiguity when keys try to look
                    // up their group, so we skip the second definition and log a warning.
                    if (layout.Groups.Exists(g => g.Name == gname))
                    {
                        System.Diagnostics.Debug.WriteLine($"OSK XML: duplicate group name '{gname}', skipping.");
                        continue;
                    }
                    var grp = new KeyGroup { Name = gname };
                    grp.FontName    = Attr(gn, "FontName", "");
                    // Color.Empty as fallback means "no override at group level".
                    grp.FontColor   = ParseColor(Attr(gn, "FontColor",   ""), Color.Empty);
                    grp.KeyColor    = ParseColor(Attr(gn, "KeyColor",    ""), Color.Empty);
                    grp.BorderColor = ParseColor(Attr(gn, "BorderColor", ""), Color.Empty);
                    // Clamp FontSize 0–200; -1 BorderThickness means "inherit from theme".
                    if (int.TryParse(Attr(gn, "FontSize",        "0"),  out int grpFs)) grp.FontSize        = Math.Clamp(grpFs, 0, 200);
                    if (int.TryParse(Attr(gn, "BorderThickness", "-1"), out int grpBt)) grp.BorderThickness = Math.Clamp(grpBt, -1, 10);
                    layout.Groups.Add(grp);
                }
            }

            // Keys: inside <Layout> (new) or siblings of <Global> (old)
            var nodes = layoutNode != null
                ? doc.SelectNodes("/OnScreenKeyboard/Layout/Key")
                : doc.SelectNodes("/OnScreenKeyboard/Key");

            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    // Track whether Row/Col were explicitly specified so we can warn on
                    // missing attributes (they default to 0, which may be unintentional).
                    bool rowMissing = node.Attributes?["Row"] == null;
                    bool colMissing = node.Attributes?["Col"] == null;
                    if (!int.TryParse(Attr(node,"Row","0"), out int r)) r=0;
                    if (!int.TryParse(Attr(node,"Col","0"), out int c)) c=0;
                    if (rowMissing) System.Diagnostics.Debug.WriteLine($"OSK XML: Key element missing Row= attribute, defaulting to 0 (Label='{Attr(node,"Label","")}')");
                    if (colMissing) System.Diagnostics.Debug.WriteLine($"OSK XML: Key element missing Col= attribute, defaulting to 0 (Label='{Attr(node,"Label","")}')");
                    // Keys outside the declared grid dimensions are silently dropped — they
                    // could appear in a file edited by hand or from a different-sized layout.
                    if (r<0||r>=gridRows||c<0||c>=gridCols)
                    {
                        System.Diagnostics.Debug.WriteLine($"OSK XML: skipping key at Row={r} Col={c} (grid is {gridRows}×{gridCols}, Label='{Attr(node,"Label","")}')");
                        continue;
                    }
                    if (!int.TryParse(Attr(node,"RowSpan","1"), out int rs)) rs=1;
                    if (!int.TryParse(Attr(node,"ColSpan","1"), out int cs)) cs=1;
                    // Clamp spans so a key can never extend beyond the grid boundary.
                    rs = Math.Clamp(rs, 1, gridRows - r);
                    cs = Math.Clamp(cs, 1, gridCols - c);
                    // Two keys occupying the same cell is an error in the XML; skip the
                    // second one and leave the first in place.
                    if (layout.CellAt(r, c) != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"OSK XML: overlapping cell at Row={r} Col={c}, skipping second key (Label='{Attr(node,"Label","")}')");
                        continue;
                    }

                    string groupName = Attr(node,"GroupName","");
                    // Unesc converts "&&" to "&" — older builds incorrectly applied
                    // WinForms ampersand-escaping to labels before writing XML.
                    string label  = Unesc(Attr(node,"Label",""));
                    // If Send is absent, fall back to Label so a key always has
                    // something to type, even if the XML only specifies the visible text.
                    string send   = Attr(node,"Send",label);
                    string sl     = Unesc(Attr(node,"ShiftLabel",""));
                    string ss     = Attr(node,"ShiftSend","");
                    string al     = Unesc(Attr(node,"AltGrLabel",""));
                    string as2    = Attr(node,"AltGrSend","");
                    string fn     = Attr(node,"FontName","");   // "" = use global

                    var p = new KeyProps(label,send,sl,ss,al,as2)
                    {
                        FontName  = fn,
                        GroupName = groupName,
                        // Color.Empty means "no per-key override — use group or theme colour".
                        FontColor = ParseColor(Attr(node,"FontColor",""), Color.Empty),
                        KeyColor  = ParseColor(Attr(node,"KeyColor",""),  Color.Empty),
                    };
                    // FontSize 0 means auto-size; clamp to 72pt to prevent oversized keys.
                    if (int.TryParse(Attr(node,"FontSize","0"),out int fs)) p.FontSize=Math.Clamp(fs,0,72);
                    string bc = Attr(node,"BorderColor","");
                    // An absent BorderColor attribute is stored as "" and should yield
                    // Color.Empty (inherit), not black (the default for ParseColor failure).
                    p.BorderColor = bc=="" ? Color.Empty : ParseColor(bc,Color.Empty);
                    // -1 means "inherit border thickness from group or theme".
                    if (int.TryParse(Attr(node,"BorderThickness","-1"),out int bt)) p.BorderThickness = Math.Clamp(bt, -1, 10);

                    layout.Cells.Add(new GridCell(r,c,p,rs,cs));
                }
            }

            // Auto-fill any grid positions not covered by an explicit <Key> element.
            // This supports sparse XML files that omit pure invisible spacers.
            // Every cell in the grid must have a GridCell object so the drawing code
            // never has to deal with null references.
            for (int r = 0; r < gridRows; r++)
                for (int c = 0; c < gridCols; c++)
                    if (layout.CellAt(r, c) == null)
                        layout.Cells.Add(new GridCell(r, c, new KeyProps("", ""), 1, 1));

            return layout;
        }

        // ── Group-only loader (used by the import feature) ────────────

        /// <summary>
        /// Reads only the <c>&lt;Group&gt;</c> definitions from an XML file,
        /// ignoring all keys, grid structure, and window settings.
        /// </summary>
        /// <remarks>
        /// This is used by the "import groups" feature in the editor, which lets
        /// you copy colour/font groups from one layout file into another without
        /// overwriting the key arrangement.  Errors (missing file, malformed XML)
        /// return an empty list rather than throwing.
        /// </remarks>
        /// <param name="path">Absolute path of the XML file to read groups from.</param>
        /// <returns>
        /// A list of <see cref="KeyGroup"/> objects found in the file.
        /// Returns an empty list if the file cannot be read or contains no groups.
        /// </returns>
        public static List<KeyGroup> LoadGroupsFromFile(string path)
        {
            var result = new List<KeyGroup>();
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                // Support both new format (groups inside <Theme>) and old format
                // (groups as direct children of <OnScreenKeyboard>).
                var themeNode = doc.SelectSingleNode("/OnScreenKeyboard/Theme");
                var nodes = themeNode != null
                    ? doc.SelectNodes("/OnScreenKeyboard/Theme/Group")
                    : doc.SelectNodes("/OnScreenKeyboard/Group");
                if (nodes == null) return result;
                foreach (XmlNode n in nodes)
                {
                    string name = Attr(n, "Name", "");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var g = new KeyGroup
                    {
                        Name        = name,
                        FontName    = Attr(n, "FontName", ""),
                        FontColor   = ParseColor(Attr(n, "FontColor",   ""), Color.Empty),
                        KeyColor    = ParseColor(Attr(n, "KeyColor",    ""), Color.Empty),
                        BorderColor = ParseColor(Attr(n, "BorderColor", ""), Color.Empty),
                    };
                    if (int.TryParse(Attr(n, "BorderThickness", "-1"), out int bt)) g.BorderThickness = bt;
                    if (int.TryParse(Attr(n, "FontSize",         "0"), out int fs)) g.FontSize        = fs;
                    result.Add(g);
                }
            }
            catch { }   // Return empty list on any error (file not found, bad XML, etc.)
            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// A shorthand for <see cref="System.Globalization.CultureInfo.InvariantCulture"/>.
        /// Using InvariantCulture when parsing and formatting numbers ensures that
        /// decimal points are always written as "." regardless of the user's locale
        /// (some locales use "," which would produce unreadable XML).
        /// </summary>
        private static System.Globalization.CultureInfo Inv =>
            System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>
        /// Reads the value of a named XML attribute from <paramref name="node"/>,
        /// returning <paramref name="fallback"/> if the attribute is absent.
        /// </summary>
        /// <param name="node">The XML element to read from.</param>
        /// <param name="name">The attribute name to look up.</param>
        /// <param name="fallback">Value to return when the attribute does not exist.</param>
        /// <returns>The attribute's string value, or <paramref name="fallback"/>.</returns>
        public static string Attr(XmlNode node, string name, string fallback)
        {
            var a = node.Attributes?[name];
            return a != null ? a.Value : fallback;
        }

        /// <summary>
        /// Sanitises a label string that was written by an old build that
        /// incorrectly applied WinForms &&-escaping before saving to XML.
        /// "&&" in a stored label should always be a plain "&".
        /// </summary>
        /// <param name="s">The raw string read from the XML attribute.</param>
        /// <returns>The string with "&&" replaced by "&", or empty string if null.</returns>
        private static string Unesc(string s) => s?.Replace("&&", "&") ?? "";

        /// <summary>
        /// Converts a <see cref="Color"/> to a 6-digit uppercase hex string
        /// (e.g. <c>Color.Red</c> → <c>"FF0000"</c>).
        /// The '#' prefix is intentionally omitted; <see cref="ParseColor"/> adds
        /// it back when reading.
        /// </summary>
        /// <param name="c">The colour to convert.</param>
        /// <returns>A 6-character hex string representing the RGB components.</returns>
        public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>
        /// Parses a 6-digit hex colour string (with or without a leading '#')
        /// and returns the corresponding <see cref="Color"/>.
        /// </summary>
        /// <param name="hex">
        /// The hex string to parse, e.g. <c>"FF0000"</c> or <c>"#FF0000"</c>.
        /// An empty or whitespace string is treated as "not set".
        /// </param>
        /// <param name="fallback">
        /// Returned unchanged when <paramref name="hex"/> is empty or cannot be
        /// parsed.  Pass <c>Color.Empty</c> to propagate the "no override" sentinel,
        /// or pass the theme's current colour to keep the existing default.
        /// </param>
        /// <returns>The parsed colour, or <paramref name="fallback"/> on failure.</returns>
        public static Color ParseColor(string hex, Color fallback)
        {
            // Empty/blank hex: return fallback unchanged.
            // When fallback is Color.Empty this propagates the "use global" sentinel.
            // When fallback is a real color (VisualTheme) the global default is kept.
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                // Strip a leading '#' if present, then re-add it so
                // ColorTranslator.FromHtml always receives the format it expects.
                hex = hex.TrimStart('#');
                if (hex.Length == 6) return ColorTranslator.FromHtml("#" + hex);
            }
            catch { }
            return fallback;
        }
    }
}

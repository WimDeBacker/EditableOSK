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
    public class VisualTheme
    {
        public Color  BackgroundColor  { get; set; } = ColorTranslator.FromHtml("#1A1A2E");
        public double Opacity          { get; set; } = 1.0;
        public string FontName         { get; set; } = "Arial";
        public int    FontSize         { get; set; } = 0;
        public Color  FontColor        { get; set; } = ColorTranslator.FromHtml("#E0E0FF");
        public Color  KeyColor         { get; set; } = ColorTranslator.FromHtml("#2D2D4A");
        public Color  BorderColor      { get; set; } = ColorTranslator.FromHtml("#3C3C5A");
        public int    BorderThickness  { get; set; } = 1;

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

        /// <summary>Copy all fields from <paramref name="src"/> into this instance (keeps same object identity).</summary>
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
    public class WindowState
    {
        public int  WindowWidth  { get; set; } = 1050;
        public int  WindowHeight { get; set; } = 290;
        public bool HideTitlebar { get; set; } = false;
        public bool AlwaysOnTop  { get; set; } = true;

        public WindowState Clone() => new WindowState
        {
            WindowWidth  = WindowWidth,
            WindowHeight = WindowHeight,
            HideTitlebar = HideTitlebar,
            AlwaysOnTop  = AlwaysOnTop,
        };

        /// <summary>Copy all fields from <paramref name="src"/> into this instance.</summary>
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
    public class LayoutMeta
    {
        public string Language        { get; set; } = "en";
        public bool   StickyModifiers { get; set; } = true;
        public bool   HoldToEdit      { get; set; } = false;
        public int    GearRow         { get; set; } = 0;
        public int    GearCol         { get; set; } = -1;  // -1 = last column
        public string LastFile        { get; set; } = "";

        public LayoutMeta Clone() => new LayoutMeta
        {
            Language        = Language,
            StickyModifiers = StickyModifiers,
            HoldToEdit      = HoldToEdit,
            GearRow         = GearRow,
            GearCol         = GearCol,
            LastFile        = LastFile,
        };

        /// <summary>Copy all fields from <paramref name="src"/> into this instance.</summary>
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
    public static class SettingsManager
    {
        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");

        // ── Save ─────────────────────────────────────────────────────
        public static void SaveSettings(GridLayout layout,
                                        VisualTheme theme, WindowState window, LayoutMeta meta,
                                        string path)
        {
            // Keep a rolling backup so accidental Apply/overwrite is always recoverable.
            // To restore: rename  filename.xml.bak  →  filename.xml
            if (File.Exists(path))
                File.Copy(path, path + ".bak", overwrite: true);

            var xs = new XmlWriterSettings { Indent = true };
            using var writer = XmlWriter.Create(path, xs);
            writer.WriteStartDocument();
            writer.WriteStartElement("OnScreenKeyboard");
            WriteTheme(writer, theme, layout);
            WriteLayout(writer, window, meta, layout);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        // <Theme BackgroundColor="..." ...>
        //   <Group Name="..." ... />
        // </Theme>
        private static void WriteTheme(XmlWriter w, VisualTheme t, GridLayout layout)
        {
            w.WriteStartElement("Theme");
            w.WriteAttributeString("BackgroundColor", Hex(t.BackgroundColor));
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
            w.WriteAttributeString("HideTitlebar",    ws.HideTitlebar   ? "1" : "0");
            w.WriteAttributeString("StickyModifiers", m.StickyModifiers ? "1" : "0");
            w.WriteAttributeString("HoldToEdit",      m.HoldToEdit      ? "1" : "0");
            w.WriteAttributeString("AlwaysOnTop",     ws.AlwaysOnTop    ? "1" : "0");
            w.WriteAttributeString("LastFile",        m.LastFile ?? "");
            WriteGrid(w, layout);
            w.WriteEndElement();
        }

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
                && p.FontColor.IsEmpty
                && p.KeyColor.IsEmpty
                && p.BorderColor.IsEmpty
                && p.BorderThickness == -1
                && cell.RowSpan == 1
                && cell.ColSpan == 1;
        }

        private static void WriteGroups(XmlWriter w, GridLayout layout)
        {
            foreach (var g in layout.Groups)
            {
                if (string.IsNullOrEmpty(g.Name)) continue;
                w.WriteStartElement("Group");
                w.WriteAttributeString("Name",            g.Name);
                w.WriteAttributeString("FontName",        g.FontName ?? "");
                w.WriteAttributeString("FontSize",        g.FontSize.ToString());
                w.WriteAttributeString("FontColor",       g.FontColor.IsEmpty   ? "" : Hex(g.FontColor));
                w.WriteAttributeString("KeyColor",        g.KeyColor.IsEmpty    ? "" : Hex(g.KeyColor));
                w.WriteAttributeString("BorderColor",     g.BorderColor.IsEmpty ? "" : Hex(g.BorderColor));
                w.WriteAttributeString("BorderThickness", g.BorderThickness.ToString());
                w.WriteEndElement();
            }
        }

        private static void WriteGrid(XmlWriter w, GridLayout layout)
        {
            foreach (var cell in layout.Cells)
            {
                if (IsPureSpacer(cell)) continue;   // omit invisible spacers — loader fills gaps
                var p = cell.Props;
                w.WriteStartElement("Key");
                w.WriteAttributeString("Row",             cell.Row.ToString());
                w.WriteAttributeString("Col",             cell.Col.ToString());
                w.WriteAttributeString("RowSpan",         cell.RowSpan.ToString());
                w.WriteAttributeString("ColSpan",         cell.ColSpan.ToString());
                w.WriteAttributeString("GroupName",       p.GroupName ?? "");
                w.WriteAttributeString("Label",           p.Label   ?? "");
                w.WriteAttributeString("Send",            p.Send    ?? "");
                w.WriteAttributeString("ShiftLabel",      p.ShiftLabel  ?? "");
                w.WriteAttributeString("ShiftSend",       p.ShiftSend   ?? "");
                w.WriteAttributeString("AltGrLabel",      p.AltGrLabel  ?? "");
                w.WriteAttributeString("AltGrSend",       p.AltGrSend   ?? "");
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
        public static GridLayout LoadSettings(VisualTheme theme, WindowState window, LayoutMeta meta,
                                              string path)
        {
            if (!File.Exists(path)) return null;
            var doc = new XmlDocument();
            doc.Load(path);

            // New format: <Theme> + <Layout>   Old format: flat <Global> (backward compat)
            var themeNode  = doc.SelectSingleNode("/OnScreenKeyboard/Theme");
            var layoutNode = doc.SelectSingleNode("/OnScreenKeyboard/Layout");
            var globalNode = doc.SelectSingleNode("/OnScreenKeyboard/Global");

            if (themeNode == null && globalNode == null) return null;   // not a keyboard layout file

            // Visual settings from <Theme> (new) or <Global> (old)
            var tNode = themeNode ?? globalNode;
            theme.BackgroundColor = ParseColor(Attr(tNode,"BackgroundColor",""), theme.BackgroundColor);
            if (double.TryParse(Attr(tNode,"Opacity","1.0"),
                    System.Globalization.NumberStyles.Float, Inv, out double op))
                theme.Opacity = Math.Clamp(op, 0.2, 1.0);
            theme.FontName    = Attr(tNode,"FontName",theme.FontName);
            if (int.TryParse(Attr(tNode,"FontSize","0"), out int gfs)) theme.FontSize = Math.Clamp(gfs, 0, 200);
            theme.FontColor   = ParseColor(Attr(tNode,"FontColor",""),  theme.FontColor);
            theme.KeyColor    = ParseColor(Attr(tNode,"KeyColor",""),   theme.KeyColor);
            theme.BorderColor = ParseColor(Attr(tNode,"BorderColor",""),theme.BorderColor);
            if (int.TryParse(Attr(tNode,"BorderThickness","1"), out int gbt)) theme.BorderThickness = gbt;

            // Structural settings from <Layout> (new) or <Global> (old)
            var lNode = layoutNode ?? globalNode;
            meta.Language        = Attr(lNode,"Language","en");
            meta.LastFile        = Attr(lNode,"LastFile","");
            meta.StickyModifiers = Attr(lNode,"StickyModifiers","1") == "1";
            meta.HoldToEdit      = Attr(lNode,"HoldToEdit","0")      == "1";

            if (int.TryParse(Attr(lNode,"WindowWidth","1050"),  out int ww) && ww>=600 && ww<=7680) window.WindowWidth=ww;
            if (int.TryParse(Attr(lNode,"WindowHeight","290"),  out int wh) && wh>=180 && wh<=4320) window.WindowHeight=wh;
            window.HideTitlebar = Attr(lNode,"HideTitlebar","0") == "1";
            window.AlwaysOnTop  = Attr(lNode,"AlwaysOnTop","1") == "1";

            int gridRows = 2, gridCols = 2;
            if (int.TryParse(Attr(lNode,"GridRows","2"), out int gr) && gr>=1) gridRows = Math.Min(gr,50);
            if (int.TryParse(Attr(lNode,"GridCols","2"), out int gc) && gc>=1) gridCols = Math.Min(gc,50);
            string gearRowAttr = Attr(lNode,"GearRow","");
            string gearColAttr = Attr(lNode,"GearCol","");
            meta.GearRow = (!string.IsNullOrEmpty(gearRowAttr) && int.TryParse(gearRowAttr, out int gearR))
                ? Math.Clamp(gearR, 0, Math.Max(0, gridRows - 1)) : 0;
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
                    if (layout.Groups.Exists(g => g.Name == gname))
                    {
                        System.Diagnostics.Debug.WriteLine($"OSK XML: duplicate group name '{gname}', skipping.");
                        continue;
                    }
                    var grp = new KeyGroup { Name = gname };
                    grp.FontName    = Attr(gn, "FontName", "");
                    grp.FontColor   = ParseColor(Attr(gn, "FontColor",   ""), Color.Empty);
                    grp.KeyColor    = ParseColor(Attr(gn, "KeyColor",    ""), Color.Empty);
                    grp.BorderColor = ParseColor(Attr(gn, "BorderColor", ""), Color.Empty);
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
                    bool rowMissing = node.Attributes?["Row"] == null;
                    bool colMissing = node.Attributes?["Col"] == null;
                    if (!int.TryParse(Attr(node,"Row","0"), out int r)) r=0;
                    if (!int.TryParse(Attr(node,"Col","0"), out int c)) c=0;
                    if (rowMissing) System.Diagnostics.Debug.WriteLine($"OSK XML: Key element missing Row= attribute, defaulting to 0 (Label='{Attr(node,"Label","")}')");
                    if (colMissing) System.Diagnostics.Debug.WriteLine($"OSK XML: Key element missing Col= attribute, defaulting to 0 (Label='{Attr(node,"Label","")}')");
                    if (r<0||r>=gridRows||c<0||c>=gridCols)
                    {
                        System.Diagnostics.Debug.WriteLine($"OSK XML: skipping key at Row={r} Col={c} (grid is {gridRows}×{gridCols}, Label='{Attr(node,"Label","")}')");
                        continue;
                    }
                    if (!int.TryParse(Attr(node,"RowSpan","1"), out int rs)) rs=1;
                    if (!int.TryParse(Attr(node,"ColSpan","1"), out int cs)) cs=1;
                    rs = Math.Clamp(rs, 1, gridRows - r);
                    cs = Math.Clamp(cs, 1, gridCols - c);
                    if (layout.CellAt(r, c) != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"OSK XML: overlapping cell at Row={r} Col={c}, skipping second key (Label='{Attr(node,"Label","")}')");
                        continue;
                    }

                    string groupName = Attr(node,"GroupName","");
                    string label  = Unesc(Attr(node,"Label",""));
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
                        FontColor = ParseColor(Attr(node,"FontColor",""), Color.Empty),
                        KeyColor  = ParseColor(Attr(node,"KeyColor",""),  Color.Empty),
                    };
                    if (int.TryParse(Attr(node,"FontSize","0"),out int fs)) p.FontSize=Math.Clamp(fs,0,72);
                    string bc = Attr(node,"BorderColor","");
                    p.BorderColor = bc=="" ? Color.Empty : ParseColor(bc,Color.Empty);
                    if (int.TryParse(Attr(node,"BorderThickness","-1"),out int bt)) p.BorderThickness = Math.Clamp(bt, -1, 10);

                    layout.Cells.Add(new GridCell(r,c,p,rs,cs));
                }
            }

            // Auto-fill any grid positions not covered by an explicit <Key> element.
            // This supports sparse XML files that omit pure invisible spacers.
            for (int r = 0; r < gridRows; r++)
                for (int c = 0; c < gridCols; c++)
                    if (layout.CellAt(r, c) == null)
                        layout.Cells.Add(new GridCell(r, c, new KeyProps("", ""), 1, 1));

            return layout;
        }

        // ── Group-only loader (used by the import feature) ────────────
        public static List<KeyGroup> LoadGroupsFromFile(string path)
        {
            var result = new List<KeyGroup>();
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
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
            catch { }
            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────
        private static System.Globalization.CultureInfo Inv =>
            System.Globalization.CultureInfo.InvariantCulture;

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
        private static string Unesc(string s) => s?.Replace("&&", "&") ?? "";
        public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
        public static Color ParseColor(string hex, Color fallback)
        {
            // Empty/blank hex: return fallback unchanged.
            // When fallback is Color.Empty this propagates the "use global" sentinel.
            // When fallback is a real color (VisualTheme) the global default is kept.
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6) return ColorTranslator.FromHtml("#" + hex);
            }
            catch { }
            return fallback;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Xml;

namespace OnScreenKeyboard
{
    public class GlobalSettings
    {
        public Color  BackgroundColor  { get; set; } = ColorTranslator.FromHtml("#1A1A2E");
        public double Opacity          { get; set; } = 1.0;
        public string FontName         { get; set; } = "Arial";
        public int    FontSize         { get; set; } = 0;
        public Color  FontColor        { get; set; } = ColorTranslator.FromHtml("#E0E0FF");
        public Color  KeyColor         { get; set; } = ColorTranslator.FromHtml("#2D2D4A");
        public Color  BorderColor      { get; set; } = ColorTranslator.FromHtml("#3C3C5A");
        public int    BorderThickness  { get; set; } = 1;
        public string Language         { get; set; } = "en";
        public int    WindowWidth      { get; set; } = 1050;
        public int    WindowHeight     { get; set; } = 290;
        public string LastFile         { get; set; } = "";
        public bool   HideTitlebar     { get; set; } = false;
        public bool   StickyModifiers  { get; set; } = true;
        public bool   AlwaysOnTop      { get; set; } = true;
        public int    GearRow          { get; set; } = 0;
        public int    GearCol          { get; set; } = -1;  // -1 = last column

    }

    public static class SettingsManager
    {
        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");

        // ── Save ─────────────────────────────────────────────────────
        public static void SaveSettings(GridLayout layout, GlobalSettings g, string path)
        {
            var xs = new XmlWriterSettings { Indent = true };
            using var writer = XmlWriter.Create(path, xs);
            writer.WriteStartDocument();
            writer.WriteStartElement("OnScreenKeyboard");
            WriteGlobal(writer, g, layout.Rows, layout.Cols);
            WriteGrid(writer, layout);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        private static void WriteGlobal(XmlWriter w, GlobalSettings g, int rows, int cols)
        {
            w.WriteStartElement("Global");
            w.WriteAttributeString("BackgroundColor", Hex(g.BackgroundColor));
            w.WriteAttributeString("Opacity",         g.Opacity.ToString("F2", Inv));
            w.WriteAttributeString("FontName",        g.FontName);
            w.WriteAttributeString("FontSize",        g.FontSize.ToString());
            w.WriteAttributeString("FontColor",       Hex(g.FontColor));
            w.WriteAttributeString("KeyColor",        Hex(g.KeyColor));
            w.WriteAttributeString("BorderColor",     Hex(g.BorderColor));
            w.WriteAttributeString("BorderThickness", g.BorderThickness.ToString());
            w.WriteAttributeString("Language",        g.Language ?? "en");
            w.WriteAttributeString("WindowWidth",     g.WindowWidth.ToString());
            w.WriteAttributeString("WindowHeight",    g.WindowHeight.ToString());
            w.WriteAttributeString("LastFile",        g.LastFile ?? "");
            w.WriteAttributeString("GridRows",        rows.ToString());
            w.WriteAttributeString("GearRow",         g.GearRow.ToString());
            w.WriteAttributeString("GearCol",         g.GearCol.ToString());
            w.WriteAttributeString("GridCols",        cols.ToString());
            w.WriteAttributeString("HideTitlebar",    g.HideTitlebar    ? "1" : "0");
            w.WriteAttributeString("StickyModifiers", g.StickyModifiers ? "1" : "0");
            w.WriteAttributeString("AlwaysOnTop",     g.AlwaysOnTop     ? "1" : "0");

            w.WriteEndElement();
        }

        private static void WriteGrid(XmlWriter w, GridLayout layout)
        {
            foreach (var cell in layout.Cells)
            {
                var p = cell.Props;
                w.WriteStartElement("Key");
                w.WriteAttributeString("Row",             cell.Row.ToString());
                w.WriteAttributeString("Col",             cell.Col.ToString());
                w.WriteAttributeString("RowSpan",         cell.RowSpan.ToString());
                w.WriteAttributeString("ColSpan",         cell.ColSpan.ToString());
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
        public static GridLayout LoadSettings(GlobalSettings g, string path)
        {
            if (!File.Exists(path)) return null;
            var doc = new XmlDocument();
            doc.Load(path);

            var gNode = doc.SelectSingleNode("/OnScreenKeyboard/Global");
            int gridRows = 2, gridCols = 2;
            if (gNode != null)
            {
                g.BackgroundColor = ParseColor(Attr(gNode,"BackgroundColor",""), g.BackgroundColor);
                if (double.TryParse(Attr(gNode,"Opacity","1.0"),
                        System.Globalization.NumberStyles.Float, Inv, out double op))
                    g.Opacity = Math.Clamp(op, 0.2, 1.0);
                g.FontName   = Attr(gNode,"FontName",g.FontName);
                if (int.TryParse(Attr(gNode,"FontSize","0"), out int gfs)) g.FontSize = gfs;
                g.FontColor  = ParseColor(Attr(gNode,"FontColor",""),  g.FontColor);
                g.KeyColor   = ParseColor(Attr(gNode,"KeyColor",""),   g.KeyColor);
                g.BorderColor= ParseColor(Attr(gNode,"BorderColor",""),g.BorderColor);
                if (int.TryParse(Attr(gNode,"BorderThickness","1"), out int gbt)) g.BorderThickness = gbt;
                g.Language   = Attr(gNode,"Language","en");
                if (int.TryParse(Attr(gNode,"WindowWidth","1050"),  out int ww) && ww>=600) g.WindowWidth=ww;
                if (int.TryParse(Attr(gNode,"WindowHeight","290"),  out int wh) && wh>=180) g.WindowHeight=wh;
                g.LastFile   = Attr(gNode,"LastFile","");
                if (int.TryParse(Attr(gNode,"GridRows","2"), out int gr) && gr>=1) gridRows = Math.Min(gr,50);
                // GearRow/GearCol: per-layout gear position.
                // Default: row 0, last column (-1). Clamp row to layout bounds.
                string gearRowAttr = Attr(gNode, "GearRow", "");
                string gearColAttr = Attr(gNode, "GearCol", "");
                if (!string.IsNullOrEmpty(gearRowAttr) && int.TryParse(gearRowAttr, out int gearR))
                    g.GearRow = Math.Clamp(gearR, 0, Math.Max(0, gridRows - 1));
                else
                    g.GearRow = 0;
                if (!string.IsNullOrEmpty(gearColAttr) && int.TryParse(gearColAttr, out int gearC))
                    g.GearCol = Math.Max(-1, gearC);
                else
                    g.GearCol = -1;
                if (int.TryParse(Attr(gNode,"GridCols","2"), out int gc) && gc>=1) gridCols = Math.Min(gc,50);
                g.HideTitlebar    = Attr(gNode,"HideTitlebar","0")    == "1";
                g.StickyModifiers = Attr(gNode,"StickyModifiers","1") == "1";
                g.AlwaysOnTop     = Attr(gNode,"AlwaysOnTop","1")     == "1";

            }

            var layout = new GridLayout(gridRows, gridCols);
            var nodes  = doc.SelectNodes("/OnScreenKeyboard/Key");
            if (nodes == null || nodes.Count == 0) return null;

            foreach (XmlNode node in nodes)
            {
                if (!int.TryParse(Attr(node,"Row","0"), out int r)) r=0;
                if (!int.TryParse(Attr(node,"Col","0"), out int c)) c=0;
                if (!int.TryParse(Attr(node,"RowSpan","1"), out int rs)) rs=1;
                if (!int.TryParse(Attr(node,"ColSpan","1"), out int cs)) cs=1;
                rs = Math.Clamp(rs,1,gridRows); cs = Math.Clamp(cs,1,gridCols);
                if (r<0||r>=gridRows||c<0||c>=gridCols) continue;

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
                    FontColor = ParseColor(Attr(node,"FontColor",""), Color.Empty),
                    KeyColor  = ParseColor(Attr(node,"KeyColor",""),  Color.Empty),
                };
                if (int.TryParse(Attr(node,"FontSize","0"),out int fs)) p.FontSize=Math.Clamp(fs,0,72);
                string bc = Attr(node,"BorderColor","");
                p.BorderColor = bc=="" ? Color.Empty : ParseColor(bc,Color.Empty);
                if (int.TryParse(Attr(node,"BorderThickness","-1"),out int bt)) p.BorderThickness = Math.Clamp(bt, -1, 10);

                layout.Cells.Add(new GridCell(r,c,p,rs,cs));
            }

            return layout;
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
            // When fallback is a real color (GlobalSettings) the global default is kept.
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

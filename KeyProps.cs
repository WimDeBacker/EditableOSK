using System.Drawing;

namespace OnScreenKeyboard
{
    public class KeyProps
    {
        public string Label           { get; set; }
        public string Send            { get; set; }
        public string ShiftLabel      { get; set; }
        public string ShiftSend       { get; set; }
        public string AltGrLabel      { get; set; }
        public string AltGrSend       { get; set; }
        public string FontName        { get; set; }
        public int    FontSize        { get; set; }
        public Color  FontColor       { get; set; }
        public Color  KeyColor        { get; set; }
        public Color  BorderColor     { get; set; }
        public int    BorderThickness { get; set; }
        public KeyProps(string label, string send,
                        string shiftLabel = "", string shiftSend = "",
                        string altGrLabel = "", string altGrSend = "")
        {
            Label      = label;  Send      = send;
            ShiftLabel = shiftLabel; ShiftSend = shiftSend;
            AltGrLabel = altGrLabel; AltGrSend = altGrSend;
            FontName        = "";        // "" = use global
            FontSize        = 0;
            FontColor       = Color.Empty;  // Empty = use global
            KeyColor        = Color.Empty;  // Empty = use global
            BorderColor     = Color.Empty;  // Empty = use global
            BorderThickness = -1;           // -1 = use global
        }

        public KeyProps Clone() => new KeyProps(Label, Send, ShiftLabel, ShiftSend, AltGrLabel, AltGrSend)
        {
            FontName        = FontName,
            FontSize        = FontSize,
            FontColor       = FontColor,
            KeyColor        = KeyColor,
            BorderColor     = BorderColor,
            BorderThickness = BorderThickness,
        };

        public string GetDisplayLabel(bool shifted, bool altGr = false)
        {
            if (altGr  && !string.IsNullOrEmpty(AltGrLabel)) return AltGrLabel;
            if (shifted && !string.IsNullOrEmpty(ShiftLabel)) return ShiftLabel;
            return Label;
        }

        public string GetSend(bool shifted, bool altGr = false)
        {
            if (altGr  && !string.IsNullOrEmpty(AltGrSend)) return AltGrSend;
            if (shifted && !string.IsNullOrEmpty(ShiftSend)) return ShiftSend;
            return Send;
        }
    }
}

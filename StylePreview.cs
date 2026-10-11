// StylePreview.cs — a strip of six sample keys in the colours of a style (a standard style, or the style the keyboard has now), so a
// style can be chosen by looking at it. Used by Edit Keyboard (Style) and by the first-start dialog.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class StylePreview : Control
    {
        /// <summary>One sample key: its label and the colours it is drawn in.</summary>
        internal sealed class Sample
        {
            public string Label; public Color Key, Font, Border; public int Thickness;
        }

        // The ids of the groups the six samples stand for (a vowel, a consonant, a digit, a sign, a control key, a word suggestion).
        private static readonly (string Label, string Group)[] Keys =
            { ("a", "vowels"), ("b", "consonants"), ("1", "numbers"), (".", "punctuation"), ("↵", "controls"), ("abc", "prediction") };

        private Color _background = Color.Gray;
        private List<Sample> _samples = new List<Sample>();

        public StylePreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MinimumSize = new Size(Touch.Target * 4, Touch.Target + 8);
            Size = new Size(Touch.Target * 6, Touch.Target + 12);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            Tag = "notheme";                                    // its colours are those of the style, not of the dialog
        }

        /// <summary>Shows a standard style.</summary>
        public void ShowPreset(NewKeyboardWizard.ThemePreset p, string name)
        {
            _background = SettingsManager.ParseColor(p.Background, Color.Gray);
            Color key = SettingsManager.ParseColor(p.KeyColor, Color.DimGray), font = SettingsManager.ParseColor(p.FontColor, Color.White), border = SettingsManager.ParseColor(p.BorderColor, Color.Gray);
            _samples = Keys.Select(k =>
            {
                var g = p.ExtraGroups.FirstOrDefault(e => e.Name == k.Group);
                bool known = g.Name != null;
                return new Sample
                {
                    Label = k.Label,
                    Key = known ? SettingsManager.ParseColor(g.Key, key) : key,
                    Font = known ? SettingsManager.ParseColor(g.Font, font) : font,
                    Border = known ? SettingsManager.ParseColor(g.Border, border) : border,
                    Thickness = known ? g.Thick : p.BorderThickness,
                };
            }).ToList();
            AccessibleName = name;
            Invalidate();
        }

        /// <summary>Shows the style a keyboard has now: each sample key as the group it stands for resolves through the standard group and the theme.</summary>
        public void ShowCurrent(VisualTheme theme, IReadOnlyList<KeyGroup> groups, string name)
        {
            _background = theme.BackgroundColor;
            var std = groups?.FirstOrDefault(g => g.Name == SettingsManager.StandardGroupName);
            Color Pick(Color own, Color fromStd, Color fromTheme) => !own.IsEmpty ? own : !fromStd.IsEmpty ? fromStd : fromTheme;
            _samples = Keys.Select(k =>
            {
                var g = groups?.FirstOrDefault(x => x.Name == k.Group);
                int bt = g != null && g.BorderThickness >= 0 ? g.BorderThickness : std != null && std.BorderThickness >= 0 ? std.BorderThickness : theme.BorderThickness;
                return new Sample
                {
                    Label = k.Label,
                    Key = Pick(g?.KeyColor ?? Color.Empty, std?.KeyColor ?? Color.Empty, theme.KeyColor),
                    Font = Pick(g?.FontColor ?? Color.Empty, std?.FontColor ?? Color.Empty, theme.FontColor),
                    Border = Pick(g?.BorderColor ?? Color.Empty, std?.BorderColor ?? Color.Empty, theme.BorderColor),
                    Thickness = bt,
                };
            }).ToList();
            AccessibleName = name;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var gr = e.Graphics;
            gr.Clear(_background);
            if (_samples.Count == 0) return;
            const int gap = 6, pad = 6;
            int n = _samples.Count;
            int w = Math.Max(8, (ClientSize.Width - 2 * pad - (n - 1) * gap) / n);
            int h = Math.Max(8, ClientSize.Height - 2 * pad);
            for (int i = 0; i < n; i++)
            {
                var s = _samples[i];
                var r = new Rectangle(pad + i * (w + gap), pad, w, h);
                using (var b = new SolidBrush(s.Key)) gr.FillRectangle(b, r);
                if (s.Thickness > 0)
                    using (var pen = new Pen(s.Border, s.Thickness))
                        gr.DrawRectangle(pen, r.X + s.Thickness / 2f, r.Y + s.Thickness / 2f, r.Width - s.Thickness, r.Height - s.Thickness);
                TextRenderer.DrawText(gr, s.Label, Fluent.FontLabel, r, s.Font,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }
}

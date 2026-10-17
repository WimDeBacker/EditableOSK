// ScanSettings.cs — the settings of scanning (a person with one or two switches steps through the keys and selects one). They belong to
// the person, not to a keyboard layout, so they are kept in the user settings (UserSettings, user.xml). See scanning_research.md.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class ScanSettings
    {
        internal const int VkEscape = 0x1B, VkSpace = 0x20, VkReturn = 0x0D, VkF8 = 0x77, VkF9 = 0x78;

        public bool Enabled;
        /// <summary>false: one switch, the highlight moves by itself; true: a step switch moves it and a select switch selects.</summary>
        public bool TwoSwitch;
        /// <summary>
        /// Virtual-key codes of the switches. The select key also starts scanning; the step key is used in two-switch mode only. While scanning
        /// is on these keys do not reach the program that is typed in, so the defaults are keys that nobody types with (a switch interface is
        /// set to send them); Space or Enter can be chosen, but then they are no longer typed.
        /// </summary>
        public int  SelectKey = VkF8;
        public int  StepKey   = VkF9;
        /// <summary>The ring round the keys of the row being scanned, and the ring round the key itself (the keys keep their own colours).</summary>
        public Color RowColor = Color.FromArgb(0, 102, 255);
        public Color KeyColor = Color.FromArgb(255, 60, 0);
        /// <summary>Time the highlight stays on a row or key (automatic mode).</summary>
        public int  ScanMs = 1000;
        /// <summary>Extra time on the first row or key of a round, so the person is ready.</summary>
        public int  FirstExtraMs = 800;
        /// <summary>Rounds without a press before an automatic scan stops (it starts again with the switch).</summary>
        public int  Rounds = 3;
        /// <summary>Presses closer together than this are one press (tremor, a switch that bounces).</summary>
        public int  IgnoreRepeatMs = 300;
        public bool SoundOnStep;
        public bool SoundOnSelect = true;

        internal const int MinScanMs = 200, MaxScanMs = 10000, MaxExtraMs = 5000, MinRounds = 1, MaxRounds = 10, MaxIgnoreMs = 2000;

        internal ScanSettings Clone() => (ScanSettings)MemberwiseClone();

        /// <summary>Brings every value inside its range (a hand-edited or damaged file must not give a scan that never moves).</summary>
        internal void Clamp()
        {
            ScanMs         = Math.Clamp(ScanMs, MinScanMs, MaxScanMs);
            FirstExtraMs   = Math.Clamp(FirstExtraMs, 0, MaxExtraMs);
            Rounds         = Math.Clamp(Rounds, MinRounds, MaxRounds);
            IgnoreRepeatMs = Math.Clamp(IgnoreRepeatMs, 0, MaxIgnoreMs);
            if (SelectKey <= 0 || SelectKey == VkEscape) SelectKey = VkF8;
            if (StepKey   <= 0 || StepKey   == VkEscape) StepKey   = VkF9;
            if (StepKey == SelectKey) StepKey = SelectKey == VkF9 ? VkF8 : VkF9;
            if (RowColor.IsEmpty) RowColor = new ScanSettings().RowColor;
            if (KeyColor.IsEmpty) KeyColor = new ScanSettings().KeyColor;
            RowColor = Color.FromArgb(255, RowColor); KeyColor = Color.FromArgb(255, KeyColor);
        }

        internal bool SameAs(ScanSettings o) =>
            o != null && Enabled == o.Enabled && TwoSwitch == o.TwoSwitch && SelectKey == o.SelectKey && StepKey == o.StepKey
            && RowColor.ToArgb() == o.RowColor.ToArgb() && KeyColor.ToArgb() == o.KeyColor.ToArgb()
            && ScanMs == o.ScanMs && FirstExtraMs == o.FirstExtraMs && Rounds == o.Rounds && IgnoreRepeatMs == o.IgnoreRepeatMs
            && SoundOnStep == o.SoundOnStep && SoundOnSelect == o.SoundOnSelect;

        /// <summary>The name of a key for the screens ("Space", "Enter", "F8"…).</summary>
        internal static string KeyName(int vk) => vk switch
        {
            VkSpace  => Lang.T("Space"),
            VkReturn => "Enter",
            _        => ((Keys)vk).ToString(),
        };
    }
}

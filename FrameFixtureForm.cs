// FrameFixtureForm.cs — a small sectioned dialog that only the tests use, to exercise FluentDialogBase's frame itself:
// content sizing, section switching (Ctrl+Tab), the warning marker on a section with an error, and language changes.
// The real editors have their own guard tests; this one stays so the frame is tested without depending on any of them.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class FrameFixtureForm : FluentDialogBase
    {
        /// <summary>A text box that holds a colour in hex and flags an invalid value the way the editors do.</summary>
        internal TouchTextBox HexBox { get; } = new TouchTextBox { Text = "#336699" };

        public FrameFixtureForm()
        {
            Text = "Frame fixture";
            var cancel = MakeTouchButton(() => Lang.T("Cancel"));
            var apply  = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, cancel, apply), withSections: true);

            var key = AddSection(() => Lang.T("Key Content"));
            AddRow(key, () => Lang.T("Label"), new TouchTextBox { Text = "a" });
            AddRow(key, () => Lang.T("Send"), new TouchTextBox());

            var alt = AddSection(() => "Shift / AltGr");
            AddRow(alt, () => Lang.T("Shift label"), new TouchTextBox());
            AddRow(alt, () => Lang.T("AltGr label"), new TouchTextBox());

            var look = AddSection(() => Lang.T("Appearance"));
            AddRow(look, () => Lang.T("Key color"), HexBox);
            AddRow(look, () => Lang.T("Border thickness"), new TouchStepper { Minimum = -1, Maximum = 10, Value = 1 }, fill: false);
            HexBox.TextChanged += (s, e) =>
                _err.SetError(HexBox, SettingsManager.ParseColor(HexBox.Text, Color.Empty).IsEmpty ? Lang.T("err: invalid hex") : "");

            AcceptButton = apply;
            CancelButton = cancel;
        }

        internal void SelectSection(int index) => ShowSection(index);
        internal SectionBar SectionButtons => Sections;
        internal bool CheckSections() => ShowFirstSectionWithError();
        internal int Measure(out Size preferred) { preferred = MeasureContent(); return preferred.Height; }
    }
}

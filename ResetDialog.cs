// ResetDialog.cs — the question asked before the program is reset to its first-start state (Edit Keyboard › Info › Reset): what is removed,
// what stays, whether the words word prediction learned go too, and — when the keyboard in use is only kept in the program's own settings
// — a warning with a Save As… button, so it is not lost without the person knowing.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class ResetDialog : FluentDialogBase
    {
        private readonly TouchCheckBox _chkLearned;
        private readonly Label _lblUnsaved;
        private readonly Control _unsavedRow;
        private readonly FluentButton _btnCancel, _btnReset, _btnSaveAs;

        /// <summary>True when the person also wants the learned words of word prediction removed (off by default). Valid after OK.</summary>
        public bool ForgetLearnedWords => _chkLearned.Checked;

        /// <param name="keyboardOnlyInSettings">The keyboard in use is not in a file of its own: a reset removes it, so the dialog warns.</param>
        /// <param name="saveAs">Saves the keyboard as a file (the Save As… button); true when it was saved, which takes the warning away.</param>
        public ResetDialog(bool keyboardOnlyInSettings, Func<bool> saveAs)
        {
            Text = Lang.T("reset: title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            var cancel = _btnCancel = MakeTouchButton(() => Lang.T("Cancel"));
            var reset  = _btnReset  = MakeTouchButton(() => Lang.T("reset: button"), FluentButton.Variant.Danger);
            BuildFrame(MakeFooter(null, cancel, reset), withSections: false);

            var t = AddSection(() => Lang.T("reset: title"));
            AddWideRow(t, Paragraph("reset: intro"), fill: false);
            AddWideRow(t, Paragraph("reset: kept"), fill: false);

            if (keyboardOnlyInSettings)
            {
                _lblUnsaved = Paragraph("reset: unsaved");
                var saveAsBtn = _btnSaveAs = MakeTouchButton(() => Lang.T("Save As…"));
                saveAsBtn.Click += (s, e) =>
                {
                    if (saveAs != null && saveAs()) { _lblUnsaved.Visible = false; _unsavedRow.Visible = false; ActiveControl = cancel; }
                };
                AddWideRow(t, _lblUnsaved, fill: false);
                _unsavedRow = ButtonRow(saveAsBtn);
                AddWideRow(t, _unsavedRow, fill: false);
            }

            _chkLearned = NewCheck(() => Lang.T("reset: forget words"));
            AddWideRow(t, _chkLearned, fill: false);
            AddWideRow(t, Paragraph("reset: words hint", hint: true), fill: false);

            reset.Click  += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton  = cancel;                    // the safe answer is the default: a stray Enter resets nothing
            CancelButton  = cancel;
            ActiveControl = cancel;
        }

        private Label Paragraph(string key, bool hint = false) => Wrap(new Label
        {
            Text = Lang.T(key), AutoSize = true, MaximumSize = new Size(Touch.LabelMaxWidth * 3, 0), UseMnemonic = false,
            Font = hint ? Fluent.FontHint : Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 4),
        });
    }
}

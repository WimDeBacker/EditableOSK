// ScanSettingsDialog.cs — the settings of scanning (see ScanSettings, Scanner). They are the user's own (UserSettings), so this dialog is
// opened from the gear button's menu and not from the layout's keyboard editor. The switch keys are chosen by pressing them.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class ScanSettingsDialog : FluentDialogBase
    {
        private readonly ScanSettings _value;
        private readonly TouchCheckBox _chkSoundStep, _chkSoundSelect;
        private readonly ColorChip _chipRow, _chipKey;
        private readonly TouchChoiceButton _cmbSwitches;
        private readonly FluentButton _btnSelect, _btnStep;
        private readonly TouchStepper _stpScan, _stpExtra, _stpRounds, _stpIgnore;
        private readonly Label _hint;
        private int _capturing;                  // 0 none, 1 select key, 2 step key

        /// <summary>The settings as the dialog shows them (valid after OK).</summary>
        public ScanSettings Result
        {
            get
            {
                var r = _value.Clone();
                r.RowColor       = _chipRow.Value;
                r.KeyColor       = _chipKey.Value;
                r.TwoSwitch     = _cmbSwitches.SelectedIndex == 1;
                r.ScanMs         = (int)_stpScan.Value;
                r.FirstExtraMs   = (int)_stpExtra.Value;
                r.Rounds         = (int)_stpRounds.Value;
                r.IgnoreRepeatMs = (int)_stpIgnore.Value;
                r.SoundOnStep    = _chkSoundStep.Checked;
                r.SoundOnSelect  = _chkSoundSelect.Checked;
                r.Clamp();
                return r;
            }
        }

        public ScanSettingsDialog(ScanSettings current)
        {
            _value = current.Clone();
            Text = Lang.T("Scanning");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            var cancel = MakeTouchButton(() => Lang.T("Cancel"));
            var apply  = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, cancel, apply), withSections: false);
            apply.Click  += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = apply; CancelButton = cancel;

            var t = AddSection(() => Lang.T("Scanning"));

            // Whether scanning is on is chosen in Edit Keyboard (the radio button "Scanning"); this window holds how it works.
            _chkSoundStep   = NewCheck(() => Lang.T("scan: sound step"));
            _chkSoundSelect = NewCheck(() => Lang.T("scan: sound select"));
            _chkSoundStep.Checked = _value.SoundOnStep; _chkSoundSelect.Checked = _value.SoundOnSelect;
            AddWideRow(t, OptionStack(_chkSoundStep, _chkSoundSelect), fill: false);

            _cmbSwitches = new TouchChoiceButton { RowHeight = 44 };
            _cmbSwitches.SetItems(new[] { new TouchChoice { Text = Lang.T("scan: one switch") }, new TouchChoice { Text = Lang.T("scan: two switches") } },
                                  _value.TwoSwitch ? 1 : 0);
            _cmbSwitches.SelectedIndexChanged += (s, e) => UpdateEnabled();
            SetTip(_cmbSwitches, () => Lang.T("tip: scan switches"));
            AddRow(t, () => Lang.T("scan: switches"), _cmbSwitches);

            _btnSelect = MakeTouchButton(() => KeyText(1));
            _btnStep   = MakeTouchButton(() => KeyText(2));
            _btnSelect.Click += (s, e) => BeginCapture(1);
            _btnStep.Click   += (s, e) => BeginCapture(2);
            AddRow(t, () => Lang.T("scan: keys"), InlineRow(_btnSelect, _btnStep), fill: false);

            _stpScan   = Stepper(ScanSettings.MinScanMs, ScanSettings.MaxScanMs, 100, _value.ScanMs);
            _stpExtra  = Stepper(0, ScanSettings.MaxExtraMs, 100, _value.FirstExtraMs);
            _stpRounds = Stepper(ScanSettings.MinRounds, ScanSettings.MaxRounds, 1, _value.Rounds);
            _stpIgnore = Stepper(0, ScanSettings.MaxIgnoreMs, 50, _value.IgnoreRepeatMs);
            AddRow(t, () => Lang.T("scan: scan time"),     _stpScan,   fill: false);
            AddRow(t, () => Lang.T("scan: first extra"),   _stpExtra,  fill: false);
            AddRow(t, () => Lang.T("scan: rounds"),        _stpRounds, fill: false);
            AddRow(t, () => Lang.T("scan: ignore repeat"), _stpIgnore, fill: false);

            _chipRow = new ColorChip(Lang.T("scan: row color"), _value.RowColor);
            _chipKey = new ColorChip(Lang.T("scan: key color"), _value.KeyColor);
            _transTexts.Add((_chipRow, () => Lang.T("scan: row color")));
            _transTexts.Add((_chipKey, () => Lang.T("scan: key color")));
            SetTip(_chipRow, () => Lang.T("tip: scan colors"));
            SetTip(_chipKey, () => Lang.T("tip: scan colors"));
            AddRow(t, () => Lang.T("scan: colors"), InlineRow(_chipRow, _chipKey), fill: false);

            _hint = HintLabel(2);
            _hint.Text = Lang.T("scan: hint");
            _transTexts.Add((_hint, () => Lang.T("scan: hint")));
            AddWideRow(t, _hint, fill: false);

            UpdateEnabled();
            ActiveControl = _cmbSwitches;
        }

        private Label HintLabel(int lines) => Wrap(new Label
        {
            AutoSize = true, UseMnemonic = false, Font = Fluent.FontHint, BackColor = Color.Transparent, ForeColor = Fluent.TextPrimary,
            MaximumSize = new Size(Touch.LabelMaxWidth * 3 / 2 + 60, 0), MinimumSize = new Size(0, Fluent.FontHint.Height * lines + 4),
            Margin = new Padding(0, 4, 0, 4),
        });

        private static TouchStepper Stepper(int min, int max, int step, int value) =>
            new TouchStepper { Minimum = min, Maximum = max, Increment = step, Value = value };

        private string KeyText(int which) =>
            _capturing == which ? Lang.T("scan: press a key")
                : Lang.StripMnemonic(Lang.T(which == 1 ? "scan: select key" : "scan: step key")) + ": " + ScanSettings.KeyName(which == 1 ? _value.SelectKey : _value.StepKey);

        private void UpdateEnabled()
        {
            bool two = _cmbSwitches.SelectedIndex == 1;
            _btnStep.Enabled  = two;
            _stpScan.Enabled  = !two;       // a person who steps by hand sets the pace
            _stpExtra.Enabled = !two;
            _stpRounds.Enabled = !two;
        }

        internal void BeginCapture(int which)
        {
            _capturing = which;
            RefreshKeyButtons();
            ActiveControl = which == 1 ? _btnSelect : _btnStep;
        }

        private void RefreshKeyButtons()
        {
            _btnSelect.Text = KeyText(1);
            _btnStep.Text   = KeyText(2);
        }

        /// <summary>Takes <paramref name="key"/> as the key being chosen. False when no key is being chosen (the key is not ours).</summary>
        internal bool TryCapture(Keys key)
        {
            if (_capturing == 0) return false;
            int vk = (int)(key & Keys.KeyCode);
            if (vk == ScanSettings.VkEscape) { _capturing = 0; RefreshKeyButtons(); return true; }      // Esc is the key that stops scanning: it cannot be a switch
            if (vk == 0) return true;
            int other = _capturing == 1 ? _value.StepKey : _value.SelectKey;
            if (vk == other) { _hint.Text = Lang.T("scan: key taken"); return true; }
            if (_capturing == 1) _value.SelectKey = vk; else _value.StepKey = vk;
            _capturing = 0;
            _hint.Text = Lang.T("scan: hint");
            RefreshKeyButtons();
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            TryCapture(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }
}

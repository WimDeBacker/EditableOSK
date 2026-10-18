// ScanSettingsDialog.cs — the settings of scanning (see ScanSettings, Scanner). They are the user's own (UserSettings), so they are not in
// the layout; the window is opened from Edit Keyboard (Access control › Scanning › Settings…). Four tabs: Switches, Timing, Back
// (the three ways back, which may all be on at once) and Feedback (colours and sound). The switch keys are chosen by pressing them.

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    internal sealed class ScanSettingsDialog : FluentDialogBase
    {
        private readonly ScanSettings _value;
        private readonly TouchChoiceButton _cmbSwitches, _cmbSound;
        private readonly TouchCheckBox _chkOnRelease, _chkBackStep, _chkBackSwitch;
        private readonly TouchChoiceButton _cmbLong;
        private readonly FluentButton _btnSelect, _btnStep, _btnBack;
        private GamepadPoller _pad;              // looks at the controllers only while a switch is being chosen (a key or a gamepad button)
        private readonly TouchStepper _stpScan, _stpExtra, _stpRounds, _stpIgnore, _stpHold;
        private readonly ColorChip _chipRow, _chipKey, _chipBack;
        private readonly Label _hint, _backHint;
        private int _capturing;                  // 0 none, 1 select, 2 step, 3 back: the next key or gamepad button pressed is the switch
        private bool _onReleaseValue, _syncing;  // what the person chose for "select on release"; _syncing: the code sets the check box

        /// <summary>The settings as the dialog shows them (valid after OK). Whether scanning is on is not decided here.</summary>
        public ScanSettings Result
        {
            get
            {
                var r = _value.Clone();
                r.TwoSwitch       = _cmbSwitches.SelectedIndex == 1;
                r.SelectOnRelease = _onReleaseValue;           // the person's own choice, also while the check box shows "on" because of the long press
                r.ScanMs          = (int)_stpScan.Value;
                r.FirstExtraMs    = (int)_stpExtra.Value;
                r.Rounds          = (int)_stpRounds.Value;
                r.IgnoreRepeatMs  = (int)_stpIgnore.Value;
                r.BackStep        = _chkBackStep.Checked;
                r.BackSwitch      = _chkBackSwitch.Checked;
                r.BackLongPress   = _cmbLong.SelectedIndex > 0;
                r.LongPressStepsBack = _cmbLong.SelectedIndex == 2;
                r.BackHoldMs      = (int)_stpHold.Value;
                r.RowColor        = _chipRow.Value;
                r.KeyColor        = _chipKey.Value;
                r.BackColor       = _chipBack.Value;
                r.SoundOnStep     = _cmbSound.SelectedIndex >= 2;
                r.SoundOnSelect   = _cmbSound.SelectedIndex % 2 == 1;
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
            BuildFrame(MakeFooter(null, cancel, apply), withSections: true);
            apply.Click  += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = apply; CancelButton = cancel;

            // ── Tab 1: Switches ──
            var t = AddSection(() => Lang.T("scan: switches"));
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
            SetTip(_btnSelect, () => Lang.T("tip: scan pad"));
            SetTip(_btnStep,   () => Lang.T("tip: scan pad"));
            AddRow(t, () => Lang.T("scan: keys"), InlineRow(_btnSelect, _btnStep), fill: false);

            _chkOnRelease = NewCheck(() => Lang.T("scan: on release"));
            _onReleaseValue = _value.SelectOnRelease;
            _chkOnRelease.Checked = _onReleaseValue;
            _chkOnRelease.CheckedChanged += (s, e) => { if (!_syncing) _onReleaseValue = _chkOnRelease.Checked; };
            SetTip(_chkOnRelease, OnReleaseTip);
            AddWideRow(t, OptionStack(_chkOnRelease), fill: false);

            _hint = HintLabel(2);
            _hint.Text = Lang.T("scan: hint");
            _transTexts.Add((_hint, HintText));
            AddWideRow(t, _hint, fill: false);

            // ── Tab 2: Timing ──
            t = AddSection(() => Lang.T("scan: tab timing"));
            _stpScan   = Stepper(ScanSettings.MinScanMs, ScanSettings.MaxScanMs, 100, _value.ScanMs);
            _stpExtra  = Stepper(0, ScanSettings.MaxExtraMs, 100, _value.FirstExtraMs);
            _stpRounds = Stepper(ScanSettings.MinRounds, ScanSettings.MaxRounds, 1, _value.Rounds);
            _stpIgnore = Stepper(0, ScanSettings.MaxIgnoreMs, 50, _value.IgnoreRepeatMs);
            AddRow(t, () => Lang.T("scan: scan time"),     _stpScan,   fill: false);
            AddRow(t, () => Lang.T("scan: first extra"),   _stpExtra,  fill: false);
            AddRow(t, () => Lang.T("scan: rounds"),        _stpRounds, fill: false);
            AddRow(t, () => Lang.T("scan: ignore repeat"), _stpIgnore, fill: false);

            // ── Tab 3: Back — three ways, each on or off, all at once if wanted ──
            t = AddSection(() => Lang.T("scan: tab back"));
            _chkBackStep   = NewCheck(() => Lang.T("scan: back step"));
            _chkBackSwitch = NewCheck(() => Lang.T("scan: back switch"));
            _chkBackStep.Checked = _value.BackStep; _chkBackSwitch.Checked = _value.BackSwitch;
            SetTip(_chkBackStep,   () => Lang.T("tip: scan back step"));
            SetTip(_chkBackSwitch, () => Lang.T("tip: scan back switch"));
            foreach (var c in new[] { _chkBackStep, _chkBackSwitch }) c.CheckedChanged += (s, e) => UpdateEnabled();
            // Each way with the settings that belong to it right under it: the back step; back with a switch and its key; the long press (off,
            // restarts scanning, or steps back: one of the two) and its time.
            AddWideRow(t, OptionStack(_chkBackStep), fill: false);
            AddWideRow(t, OptionStack(_chkBackSwitch), fill: false);
            _btnBack = MakeTouchButton(() => KeyText(3));
            _btnBack.Click += (s, e) => BeginCapture(3);
            AddRow(t, () => Lang.T("scan: back key"), _btnBack, fill: false);
            SetTip(_btnBack, () => Lang.T("tip: scan pad"));
            _cmbLong = new TouchChoiceButton { RowHeight = 44 };
            _cmbLong.SetItems(new[] { "scan: long off", "scan: long restarts", "scan: long steps back" }.Select(k => new TouchChoice { Text = Lang.T(k) }),
                              !_value.BackLongPress ? 0 : _value.LongPressStepsBack ? 2 : 1);
            _cmbLong.SelectedIndexChanged += (s, e) => UpdateEnabled();
            SetTip(_cmbLong, () => Lang.T("tip: scan long press"));
            AddRow(t, () => Lang.T("scan: long press"), _cmbLong);
            _stpHold = Stepper(ScanSettings.MinBackHoldMs, ScanSettings.MaxBackHoldMs, 100, _value.BackHoldMs);
            AddRow(t, () => Lang.T("scan: hold"), _stpHold, fill: false);
            _backHint = HintLabel(2);
            _backHint.Text = Lang.T("scan: back hint");
            _transTexts.Add((_backHint, () => Lang.T("scan: back hint")));
            AddWideRow(t, _backHint, fill: false);

            // ── Tab 4: Feedback — the rings and the sound ──
            t = AddSection(() => Lang.T("scan: tab feedback"));
            _chipRow  = Chip("scan: row color", _value.RowColor);
            _chipKey  = Chip("scan: key color", _value.KeyColor);
            _chipBack = Chip("scan: back color", _value.BackColor);
            AddRow(t, () => Lang.T("scan: colors"), InlineRow(_chipRow, _chipKey, _chipBack), fill: false);

            // The sound: none, when a key is selected, at each step, or both.
            _cmbSound = new TouchChoiceButton { RowHeight = 44 };
            _cmbSound.SetItems(new[] { "scan: sound none", "scan: sound select", "scan: sound step", "scan: sound both" }.Select(k => new TouchChoice { Text = Lang.T(k) }),
                               (_value.SoundOnStep ? 2 : 0) + (_value.SoundOnSelect ? 1 : 0));
            AddRow(t, () => Lang.T("scan: sound"), _cmbSound);

            UpdateEnabled();
            ActiveControl = _cmbSwitches;
        }

        private ColorChip Chip(string key, Color initial)
        {
            var c = new ColorChip(Lang.T(key), initial);
            _transTexts.Add((c, () => Lang.T(key)));
            SetTip(c, () => Lang.T("tip: scan colors"));
            return c;
        }

        private Label HintLabel(int lines) => Wrap(new Label
        {
            AutoSize = true, UseMnemonic = false, Font = Fluent.FontHint, BackColor = Color.Transparent, ForeColor = Fluent.TextPrimary,
            MaximumSize = new Size(Touch.LabelMaxWidth * 3 / 2 + 60, 0), MinimumSize = new Size(0, Fluent.FontHint.Height * lines + 4),
            Margin = new Padding(0, 4, 0, 4),
        });

        private static TouchStepper Stepper(int min, int max, int step, int value) =>
            new TouchStepper { Minimum = min, Maximum = max, Increment = step, Value = value };

        private int KeyOf(int which) => which == 1 ? _value.SelectKey : which == 2 ? _value.StepKey : _value.BackKey;

        private string KeyText(int which) =>
            _capturing == which ? Lang.T("scan: press a key")
                : which == 3 ? ScanSettings.KeyName(_value.BackKey)
                : Lang.StripMnemonic(Lang.T(which == 1 ? "scan: select key" : "scan: step key")) + ": " + ScanSettings.KeyName(KeyOf(which));

        private void UpdateEnabled()
        {
            bool two = _cmbSwitches.SelectedIndex == 1;
            _btnStep.Enabled   = two;
            _stpScan.Enabled   = !two;       // a person who steps by hand sets the pace
            _stpExtra.Enabled  = !two;
            _stpRounds.Enabled = !two;
            _btnBack.Enabled   = _chkBackSwitch.Checked;
            _stpHold.Enabled   = _cmbLong.SelectedIndex > 0;
            // A long press needs the length of the press: the select switch then acts when it is let go. The check box shows that ("on", and
            // greyed, with the reason in its tip); what the person chose is kept and comes back when the long press is switched off.
            bool forced = _cmbLong.SelectedIndex > 0;
            _syncing = true;
            try { _chkOnRelease.Checked = forced || _onReleaseValue; }
            finally { _syncing = false; }
            _chkOnRelease.Enabled = !forced;
            _tip.SetToolTip(_chkOnRelease, OnReleaseTip());
            if (_hint != null && _capturing == 0) _hint.Text = HintText();      // a disabled control shows no tip: the reason is written under it
        }

        /// <summary>The line under the switches: the Esc hint, or why "select on release" is on and grey.</summary>
        private string HintText() => Forced()
            ? Lang.T("tip: scan on release forced") + " " + Lang.T("scan: hint")
            : Lang.T("scan: hint");

        /// <summary>The select switch acts on release whatever is chosen: a long press that goes back needs the length of the press.</summary>
        private bool Forced() => _cmbLong != null && _cmbLong.SelectedIndex > 0;

        private string OnReleaseTip() => Lang.T(Forced() ? "tip: scan on release forced" : "tip: scan on release");

        internal void BeginCapture(int which)
        {
            _capturing = which;
            RefreshKeyButtons();
            ActiveControl = which == 1 ? _btnSelect : which == 2 ? _btnStep : _btnBack;
            // The next key of the keyboard OR the next button of a gamepad becomes the switch: both are looked at while choosing.
            _pad ??= new GamepadPoller((code, down) => { if (down) TryCapturePad(code); });
            _pad.Start();
        }

        private void RefreshKeyButtons()
        {
            _btnSelect.Text = KeyText(1);
            _btnStep.Text   = KeyText(2);
            _btnBack.Text   = KeyText(3);
        }

        /// <summary>Takes the gamepad button <paramref name="code"/> as the switch being chosen. False when none is being chosen.</summary>
        internal bool TryCapturePad(int code) => _capturing != 0 && Gamepad.IsButton(code) && Take(code);

        /// <summary>Takes a key or a gamepad button as the switch being chosen; refused (with a note) when it is another switch already.</summary>
        private bool Take(int code)
        {
            var note = _capturing == 3 ? _backHint : _hint;
            for (int other = 1; other <= 3; other++)
                if (other != _capturing && KeyOf(other) == code) { note.Text = Lang.T("scan: key taken"); return true; }
            if (_capturing == 1) _value.SelectKey = code; else if (_capturing == 2) _value.StepKey = code; else _value.BackKey = code;
            EndCapture();
            return true;
        }

        private void EndCapture()
        {
            _capturing = 0;
            _pad?.Stop();
            _hint.Text = HintText(); _backHint.Text = Lang.T("scan: back hint");
            RefreshKeyButtons();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _pad?.Dispose(); _pad = null; }
            base.Dispose(disposing);
        }

        /// <summary>Takes <paramref name="key"/> as the key being chosen. False when no key is being chosen (the key is not ours).</summary>
        internal bool TryCapture(Keys key)
        {
            if (_capturing == 0) return false;
            int vk = (int)(key & Keys.KeyCode);
            if (vk == ScanSettings.VkEscape) { EndCapture(); return true; }      // Esc pauses scanning: it cannot be a switch; it cancels the choice
            if (vk == 0) return true;
            return Take(vk);
        }

        /// <summary>Whether the check box for select-on-release can be changed (it cannot while a long press is on).</summary>
        internal bool OnReleaseEnabled => _chkOnRelease.Enabled;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            TryCapture(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }
}

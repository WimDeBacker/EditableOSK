using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public class KeyEditorForm : Form
    {
        public KeyProps Result        { get; private set; }
        public int     ResultColSpan { get; private set; } = 1;
        public int     ResultRowSpan { get; private set; } = 1;
        private int    _initColSpan, _initRowSpan, _maxRows;

        // Controls
        private TextBox       _txtLabel, _txtSend, _txtShiftLabel, _txtShiftSend,
                              _txtAltGrLabel, _txtAltGrSend;
        private NumericUpDown _nudColSpan, _nudRowSpan, _nudWPSlot;
        private Label         _lblSendFieldName;   // "Send" or "Prediction cell" depending on mode
        private Label         _lblWPDuplicate;     // warning when slot already in use
        private HashSet<int>  _usedWpSlots;        // slots used by OTHER keys in this layout
        private ComboBox      _cmbFont;
        private NumericUpDown _nudFontSize;
        private CheckBox      _chkAutoSize;
        private Panel         _pnlFontColor, _pnlKeyColor, _pnlBorderColor;
        private NumericUpDown _nudBorderThickness;
        private Panel         _pnlPreview;
        private Label         _lblPreviewKey;
        private Button        _btnApply, _btnCancel;
        private ComboBox      _cmbGroup;

        private readonly KeyProps        _original;
        private readonly Color          _globalBorderColor;  // fallback shown when key has no border override
        private readonly VisualTheme _ownerGlobal;        // cached at ctor time; Owner property is null until ShowDialog
        private readonly List<(Label Ctrl, Func<string> GetText)> _transLabels
            = new List<(Label, Func<string>)>();
        private readonly List<(Panel Pnl, Func<string> GetTitle)> _transGroups
            = new List<(Panel, Func<string>)>();

        // ── Theme (WinUI 3) ───────────────────────────────────────────
        private static Color C_BG        => Fluent.BgPage;
        private static Color C_PANEL_BG  => Fluent.BgCard;
        private static Color C_BORDER    => Fluent.BorderCard;
        private static Color C_LBL       => Fluent.TextPrimary;
        private static Color C_HINT      => Fluent.TextHint;
        private static Color C_BTN_OK    => Fluent.Success;
        private static Color C_BTN_CANCEL=> Fluent.Danger;
        private static Color C_INPUT_BG  => Fluent.BgInput;
        private static Font  F_LABEL     => Fluent.FontLabel;
        private static Font  F_INPUT     => Fluent.FontInput;
        private static Font  F_HEADER    => Fluent.FontTitle;
        private static Font  F_BTN       => Fluent.FontBtnLg;
        private static Font  F_HINT      => Fluent.FontHint;
        private const int HDR_H = 42;
        private const int ROW_H = 50;
        private const int PAD   = 18;

        private readonly int             _maxCols;  // total columns in layout, caps ColSpan
        private readonly List<KeyGroup>  _groups;   // available named groups for this layout

        // ══════════════════════════════════════════════════════════════
        // ── OPTION 3 BEGIN: SendMode enum and mode-related fields ─────
        // To remove option 3: delete everything between OPTION 3 BEGIN
        // and OPTION 3 END markers, then revert the two marked changes
        // in BuildUI() and PopulateFields().
        // ─────────────────────────────────────────────────────────────
        private enum SendMode { Text, KeySequence, Modifier, WordPrediction, Layout }
        private SendMode _sendMode = SendMode.Text;

        // Mode selector buttons
        private FluentButton _btnModeText, _btnModeKey, _btnModeMod, _btnModeWP, _btnModeLayout;

        // Layout picker panel (browse button, shown in Layout mode)
        private Panel        _pnlLayoutPicker;
        private FluentButton _btnBrowseLayout;
        private string  _layoutDir;        // directory of the current layout file, for relative path suggestions
        private TextBox _activeSendField;  // which send field was last focused (drives Browse target)
        // Whether ShiftSend / AltGrSend are being displayed without their "layout:" prefix
        private bool _shiftSendIsLayout  = false;
        private bool _altGrSendIsLayout  = false;
        // Guards: true while code is setting the text programmatically (suppresses the TextChanged flag-reset)
        private bool _progShiftSend = false;
        private bool _progAltGrSend = false;

        private static readonly Color C_MODE_LAYOUT = Color.FromArgb(211, 84, 0); // dark orange

        // Key sequence recorder panel: shown when mode = KeySequence
        private Panel        _pnlKeyPicker;   // reuse name so OPTION 3 markers stay consistent
        private FluentButton _btnRecord;
        private Label  _lblRecordHint;
        private bool   _recording = false;
        private bool   _winHeld  = false;   // Win key held during low-level hook recording

        // ── Low-level keyboard hook P/Invoke ──────────────────────────
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint   vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        private const int  WH_KEYBOARD_LL = 13;
        private const int  WM_KEYDOWN     = 0x0100;
        private const int  WM_KEYUP       = 0x0101;
        private const int  WM_SYSKEYDOWN  = 0x0104;  // Alt+key down
        private const int  WM_SYSKEYUP    = 0x0105;  // Alt+key up
        private const uint VK_LWIN        = 0x5B;
        private const uint VK_RWIN        = 0x5C;
        private const uint VK_ESCAPE      = 0x1B;

        private IntPtr              _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc _hookProc;   // kept alive to prevent GC

        // Modifier picker: shown when mode = Modifier
        private Panel    _pnlModPicker;
        private ComboBox _cmbModChoice;

        // Accent colors per mode
        private static readonly Color C_MODE_TEXT = Color.FromArgb(41,  128, 185);
        private static readonly Color C_MODE_KEY  = Color.FromArgb(192,  57,  43);
        private static readonly Color C_MODE_MOD  = Color.FromArgb(142,  68, 173);
        private static readonly Color C_MODE_OFF  = Color.FromArgb(180, 185, 192);
        private static readonly Color C_RECORDING = Color.FromArgb(220,  50,  50);

        // Modifier labels (must match KeyLayout.ModifierLabels)
        private static readonly (string Label, string Display)[] _modifiers =
        {
            ("Shift",  "Shift"),
            ("Caps",   "Caps Lock"),
            ("Ctrl",   "Ctrl"),
            ("Alt",    "Alt"),
            ("AltGr",  "AltGr"),
            ("Win",    "Win"),
        };
        // ── OPTION 3 END: enum and field declarations ─────────────────

        // ── Constructor ───────────────────────────────────────────────
        public KeyEditorForm(KeyProps props, Form owner, int colSpan = 1, int rowSpan = 1, int maxCols = 14, int maxRows = 6, HashSet<int> usedWpSlots = null, List<KeyGroup> groups = null, string layoutDir = null)
        {
            _original    = props;
            _layoutDir   = layoutDir;
            _groups      = groups ?? new List<KeyGroup>();
            _maxCols     = Math.Max(1, maxCols);
            _usedWpSlots = usedWpSlots ?? new HashSet<int>();
            // Cache the owner's global settings now — Owner property is null until ShowDialog fires
            _ownerGlobal       = (owner as KeyboardForm)?._theme;
            _globalBorderColor = _ownerGlobal?.BorderColor ?? ColorTranslator.FromHtml("#3C3C5A");
            Result    = props.Clone();
            ResultColSpan = Math.Max(1, colSpan);
            ResultRowSpan = Math.Max(1, rowSpan);
            _initColSpan  = ResultColSpan;
            _initRowSpan  = ResultRowSpan;
            _maxRows      = Math.Max(1, maxRows);
            Text         = $"{Lang.T("Edit Key")}  [{props.Label}]";
            BackColor    = C_BG;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox  = MinimizeBox = false;
            ShowIcon     = false;
            StartPosition = FormStartPosition.CenterParent;
            Size         = new Size(980, 560);
            TopMost      = true;
            Font         = F_LABEL;

            BuildUI(props);
            Lang.LanguageChanged += OnLanguageChanged;
            FormClosed += (s, e) =>
            {
                Lang.LanguageChanged -= OnLanguageChanged;
                // Safety net: uninstall hook if form closes while recording
                if (_hookHandle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
            };
        }

        private void OnLanguageChanged()
        {
            Text = $"{Lang.T("Edit Key")}  [{_original.Label}]";
            _btnApply.Text         = Lang.T("Apply");
            _btnCancel.Text        = Lang.T("Cancel");
            _chkAutoSize.Text      = Lang.T("Auto");
            _btnModeLayout.Text    = Lang.T("Layout");
            UpdateBrowseLabel();
            foreach (var (ctrl, getText) in _transLabels) ctrl.Text = getText();
            foreach (var (pnl,  _)       in _transGroups) pnl.Invalidate();
            Invalidate(true);
        }

        // ══════════════════════════════════════════════════════════════
        private void BuildUI(KeyProps p)
        {
            int margin  = 18;
            int gap     = 14;
            int leftW   = 510;   // Key Content column
            int rightW  = ClientSize.Width - margin * 2 - gap - leftW;  // Appearance column
            int leftX   = margin;
            int rightX  = margin + leftW + gap;
            int colW    = leftW;  // alias used by left-column group sizing

            // ── OPTION 3 BEGIN: extra rows in Key Content for mode UI ─
            // Original keyRows = 8. Added 4 rows: mode selector (3 rows) + picker row.
            int keyRows = 12;
            // ── OPTION 3 END ──────────────────────────────────────────

            int keyH    = HDR_H + PAD + keyRows * ROW_H + PAD;
            var grpKey  = AddGroup(() => Lang.T("Key Content"), leftX, margin, colW, keyH,
                                   Color.FromArgb(41, 128, 185));

            int lx = PAD, vx = 175, vw = colW - lx - vx - PAD;
            int gy = HDR_H + PAD;

            AddFieldLabel(grpKey, () => Lang.T("Label"), lx, gy);
            _txtLabel = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            // ── OPTION 3 BEGIN: mode selector row ─────────────────────
            AddOption3ModeSelector(grpKey, lx, vx, vw, ref gy);
            // ── OPTION 3 END ──────────────────────────────────────────

            _lblSendFieldName = new Label
            {
                Text = Lang.T("Send"), Left = lx, Top = gy + 4, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            grpKey.Controls.Add(_lblSendFieldName);
            _txtSend = AddInput(grpKey, vx, gy, vw);
            // Word-prediction slot spinner — overlays Send field, visible only in WP mode
            _nudWPSlot = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 0, Maximum = 9,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, Visible = false,
            };
            _nudWPSlot.ValueChanged += (s, e) => { Refresh2(); CheckWPDuplicate(); };
            grpKey.Controls.Add(_nudWPSlot);
            // Duplicate slot warning label (shown below the NUD)
            _lblWPDuplicate = new Label
            {
                Left = vx, Top = gy + 24, Width = vw, Height = 18,
                ForeColor = Fluent.Danger, BackColor = Color.Transparent,
                Font = Fluent.FontHint, Visible = false, Text = "",
            };
            grpKey.Controls.Add(_lblWPDuplicate);
            gy += ROW_H;

            // ── OPTION 3 BEGIN: picker row (key sequence / modifier) ──
            AddOption3PickerRow(grpKey, lx, vx, vw, ref gy);
            // ── OPTION 3 END ──────────────────────────────────────────

            AddFieldLabel(grpKey, () => Lang.T("Shift label"), lx, gy);
            _txtShiftLabel = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("Shift send"), lx, gy);
            _txtShiftSend = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("AltGr label"), lx, gy);
            _txtAltGrLabel = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("AltGr send"), lx, gy);
            _txtAltGrSend = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("Key width"), lx, gy);
            _nudColSpan = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 1, Maximum = _maxCols,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            grpKey.Controls.Add(_nudColSpan);
            gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("Key height"), lx, gy);
            _nudRowSpan = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 1, Maximum = _maxRows,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            grpKey.Controls.Add(_nudRowSpan);
            gy += ROW_H;



            // ── RIGHT COLUMN ──────────────────────────────────────────
            int rightY = margin;
            int styleRows = 8;  // font+size+fontcolor+keycolor+bordercolor+borderthickness+group+preview
            int styleH    = HDR_H + PAD + styleRows * ROW_H + 28 + PAD;
            var grpStyle  = AddGroup(() => Lang.T("Appearance"), rightX, rightY, rightW, styleH,
                                     Color.FromArgb(39, 174, 96));
            rightY += styleH + gap;

            int slx = PAD, svx = 140, svw = rightW - slx - svx - PAD;
            gy = HDR_H + PAD;

            AddFieldLabel(grpStyle, () => Lang.T("Font"), slx, gy);
            _cmbFont = new ComboBox
            {
                Left = svx, Top = gy, Width = svw,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
            };
            _cmbFont.Items.AddRange(GetInstalledFonts().ToArray<object>());
            _cmbFont.SelectedIndexChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_cmbFont); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Font size"), slx, gy);
            _nudFontSize = new NumericUpDown
            {
                Left = svx, Top = gy, Width = 65,
                Minimum = 0, Maximum = 72,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            _nudFontSize.ValueChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_nudFontSize);
            _chkAutoSize = new CheckBox
            {
                Text = Lang.T("Auto"), Left = svx + 71, Top = gy + 5,
                AutoSize = true, ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            _chkAutoSize.CheckedChanged += (s, e) =>
            {
                _nudFontSize.Enabled = !_chkAutoSize.Checked;
                Refresh2();
            };
            grpStyle.Controls.Add(_chkAutoSize); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Font color"), slx, gy);
            _pnlFontColor = AddColorRow(grpStyle, svx, gy, svw); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Key color"), slx, gy);
            _pnlKeyColor = AddColorRow(grpStyle, svx, gy, svw); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Border color"), slx, gy);
            _pnlBorderColor = AddColorRow(grpStyle, svx, gy, svw); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Border thickness"), slx, gy);
            _nudBorderThickness = new NumericUpDown
            {
                Left = svx, Top = gy, Width = 65, Minimum = -1, Maximum = 10,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            _nudBorderThickness.ValueChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_nudBorderThickness);
            gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Group"), slx, gy);
            _cmbGroup = new ComboBox
            {
                Left = svx, Top = gy, Width = svw,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
            };
            _cmbGroup.Items.Add(Lang.T("(no group)"));
            foreach (var g in _groups) _cmbGroup.Items.Add(g.Name);
            _cmbGroup.SelectedIndex = 0;
            _cmbGroup.SelectedIndexChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_cmbGroup); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Preview"), slx, gy);
            int keyBtnW = 80, keyBtnH = 46;
            _pnlPreview = new Panel
            {
                Left = svx, Top = gy, Width = keyBtnW, Height = keyBtnH,
                BackColor = Color.FromArgb(30, 30, 50),
            };
            grpStyle.Controls.Add(_pnlPreview);
            _lblPreviewKey = new Label
            {
                Text = p.Label, TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                ForeColor = ColorTranslator.FromHtml("#E0E0FF"),
                BackColor = ColorTranslator.FromHtml("#2D2D4A"),
                Font = new Font("Arial", 13f, FontStyle.Bold),
            };
            _pnlPreview.Controls.Add(_lblPreviewKey);

            int btnTop = Math.Max(margin + keyH, rightY) + gap;
            int bw     = (leftW + gap + rightW - gap) / 2;
            _btnCancel = MakeActionBtn(Lang.T("Cancel"), margin,        btnTop, bw, 44);
            _btnApply  = MakeActionBtn(Lang.T("Apply"),  margin+bw+gap, btnTop, bw, 44);
            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            ClientSize = new Size(ClientSize.Width, btnTop + 44 + margin);

            SetupLayoutFocusTracking();
            PopulateFields(p);
        }

        // ══════════════════════════════════════════════════════════════
        // ── OPTION 3 BEGIN: mode selector and picker UI methods ───────
        // All methods and logic below this marker until OPTION 3 END
        // are exclusively for option 3. Delete them to fully remove it.
        // ─────────────────────────────────────────────────────────────

        private bool _initialising = false;

        private void AddOption3ModeSelector(Panel parent, int lx, int vx, int vw, ref int gy)
        {
            // Buttons span the full panel width (lx → right edge) so translated labels always fit.
            // 2×2 grid + full-width row:
            //   row 1 = Text | Key/Shortcut
            //   row 2 = Modifier | Word prediction
            //   row 3 = Layout switch (full width)
            int fullW = vx + vw - lx;   // from lx to the same right edge as value fields
            int bw    = (fullW - 4) / 2;
            _btnModeText   = MakeModeBtn(parent, Lang.T("Text"),            lx,          gy,             bw);
            _btnModeKey    = MakeModeBtn(parent, Lang.T("Key/Shortcut"),    lx + bw + 4, gy,             bw);
            _btnModeMod    = MakeModeBtn(parent, Lang.T("Modifier"),        lx,          gy + ROW_H,     bw);
            _btnModeWP     = MakeModeBtn(parent, Lang.T("Word prediction"), lx + bw + 4, gy + ROW_H,     bw);
            _btnModeLayout = MakeModeBtn(parent, Lang.T("Layout"),          lx,          gy + ROW_H * 2, fullW);

            _btnModeText.Click   += (s, e) => SetSendMode(SendMode.Text,           applyPicker: true);
            _btnModeKey.Click    += (s, e) => SetSendMode(SendMode.KeySequence,    applyPicker: true);
            _btnModeMod.Click    += (s, e) => SetSendMode(SendMode.Modifier,       applyPicker: true);
            _btnModeWP.Click     += (s, e) => SetSendMode(SendMode.WordPrediction, applyPicker: true);
            _btnModeLayout.Click += (s, e) => SetSendMode(SendMode.Layout,         applyPicker: true);

            gy += ROW_H * 3;  // three rows of buttons
        }

        private void AddOption3PickerRow(Panel parent, int lx, int vx, int vw, ref int gy)
        {
            int pickerVx = vx - lx;

            // ── Key sequence recorder ─────────────────────────────────
            _pnlKeyPicker = new Panel
            {
                Left = lx, Top = gy, Width = lx + vx + vw, Height = ROW_H - 4,
                BackColor = Fluent.BgCard, Visible = false,
            };
            parent.Controls.Add(_pnlKeyPicker);

            _btnRecord = new FluentButton
            {
                Text = Lang.T("Record key / shortcut"),
                Left = pickerVx, Top = 0, Width = vw, Height = ROW_H - 8,
                Style = FluentButton.Variant.Neutral, TabStop = false,
            };
            _btnRecord.Click += (s, e) => StartRecording();
            _pnlKeyPicker.Controls.Add(_btnRecord);

            _lblRecordHint = new Label
            {
                Text = "", Left = pickerVx, Top = ROW_H - 6, AutoSize = true,
                ForeColor = C_HINT, BackColor = Color.Transparent,
                Font = Fluent.FontHint,
            };
            _pnlKeyPicker.Controls.Add(_lblRecordHint);

            // ── Layout file picker ────────────────────────────────────
            _pnlLayoutPicker = new Panel
            {
                Left = lx, Top = gy, Width = lx + vx + vw, Height = ROW_H - 4,
                BackColor = Fluent.BgCard, Visible = false,
            };
            parent.Controls.Add(_pnlLayoutPicker);

            _btnBrowseLayout = new FluentButton
            {
                Text = Lang.T("Browse (Send)"),
                Left = pickerVx, Top = 0, Width = vw, Height = ROW_H - 8,
                Style = FluentButton.Variant.Neutral, TabStop = false,
            };
            _btnBrowseLayout.Click += (s, e) =>
            {
                string initDir = _layoutDir ?? AppDomain.CurrentDomain.BaseDirectory;
                using var dlg = new OpenFileDialog
                {
                    Title            = Lang.T("Layout file"),
                    Filter           = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    InitialDirectory = initDir,
                };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string selected = dlg.FileName;
                // Prefer a relative path when the file is inside the layout directory
                if (_layoutDir != null &&
                    selected.StartsWith(_layoutDir, StringComparison.OrdinalIgnoreCase))
                    selected = selected.Substring(_layoutDir.Length).TrimStart('\\', '/');
                // Fill whichever send field was last focused (default: primary Send).
                // All three fields display without "layout:" prefix; Apply() / the flags re-add it.
                var target = _activeSendField ?? _txtSend;
                if      (target == _txtShiftSend)  SetShiftSendText(selected, isLayout: true);
                else if (target == _txtAltGrSend)  SetAltGrSendText(selected, isLayout: true);
                else                               target.Text = selected;
            };
            _pnlLayoutPicker.Controls.Add(_btnBrowseLayout);

            // ── Modifier picker ───────────────────────────────────────
            _pnlModPicker = new Panel
            {
                Left = lx, Top = gy, Width = lx + vx + vw, Height = ROW_H - 4,
                BackColor = Fluent.BgCard, Visible = false,
            };
            parent.Controls.Add(_pnlModPicker);

            var lblMod = new Label
            {
                Text = Lang.T("Modifier"), Left = 0, Top = 4, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            _pnlModPicker.Controls.Add(lblMod);

            _cmbModChoice = new ComboBox
            {
                Left = pickerVx, Top = 0, Width = vw,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
            };
            foreach (var (_, display) in _modifiers) _cmbModChoice.Items.Add(display);
            _cmbModChoice.SelectedIndex = 0;
            _cmbModChoice.SelectedIndexChanged += (s, e) => ApplyModChoice();
            _pnlModPicker.Controls.Add(_cmbModChoice);

            gy += ROW_H;
        }

        /// <summary>
        /// Sets _txtShiftSend programmatically (suppressing the manual-edit flag reset).
        /// </summary>
        private void SetShiftSendText(string text, bool isLayout)
        {
            _progShiftSend    = true;
            _shiftSendIsLayout = isLayout;
            _txtShiftSend.Text = text;
            _progShiftSend    = false;
        }

        /// <summary>
        /// Sets _txtAltGrSend programmatically (suppressing the manual-edit flag reset).
        /// </summary>
        private void SetAltGrSendText(string text, bool isLayout)
        {
            _progAltGrSend    = true;
            _altGrSendIsLayout = isLayout;
            _txtAltGrSend.Text = text;
            _progAltGrSend    = false;
        }

        /// <summary>
        /// Wires Enter events on the three send fields so the Browse button label
        /// always reflects which field it will fill.
        /// Also resets the layout-path flag when the user manually edits Shift/AltGr.
        /// </summary>
        private void SetupLayoutFocusTracking()
        {
            _activeSendField = _txtSend;  // default

            void Track(TextBox txt)
            {
                txt.Enter += (s, e) => { _activeSendField = txt; UpdateBrowseLabel(); };
            }
            Track(_txtSend);
            Track(_txtShiftSend);
            Track(_txtAltGrSend);

            // When the user manually types in Shift/AltGr fields, clear the layout flag
            // so Apply() does not wrongly re-add "layout:" to a non-path value.
            _txtShiftSend.TextChanged += (s, e) => { if (!_progShiftSend) _shiftSendIsLayout  = false; };
            _txtAltGrSend.TextChanged += (s, e) => { if (!_progAltGrSend) _altGrSendIsLayout  = false; };
        }

        private void UpdateBrowseLabel()
        {
            if (_btnBrowseLayout == null) return;
            if (_activeSendField == _txtShiftSend)
                _btnBrowseLayout.Text = Lang.T("Browse (Shift-send)");
            else if (_activeSendField == _txtAltGrSend)
                _btnBrowseLayout.Text = Lang.T("Browse (AltGr-send)");
            else
                _btnBrowseLayout.Text = Lang.T("Browse (Send)");
        }

        private FluentButton MakeModeBtn(Panel parent, string text, int x, int y, int w)
        {
            var btn = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = ROW_H - 6,
                Style = FluentButton.Variant.Neutral, TabStop = false,
            };
            parent.Controls.Add(btn);
            return btn;
        }

        private void SetSendMode(SendMode mode, bool applyPicker = false)
        {
            _sendMode = mode;

            _btnModeText.Style   = mode == SendMode.Text           ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeKey.Style    = mode == SendMode.KeySequence    ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeMod.Style    = mode == SendMode.Modifier       ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeWP.Style     = mode == SendMode.WordPrediction ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeLayout.Style = mode == SendMode.Layout         ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeText.Invalidate(); _btnModeKey.Invalidate(); _btnModeMod.Invalidate();
            _btnModeWP.Invalidate();   _btnModeLayout.Invalidate();

            bool isKey    = mode == SendMode.KeySequence;
            bool isMod    = mode == SendMode.Modifier;
            bool isWP     = mode == SendMode.WordPrediction;
            bool isLayout = mode == SendMode.Layout;

            // WP mode: show slot NUD, hide Send textbox. Layout/all others: show textbox.
            _txtSend.Visible         = !isWP;
            _nudWPSlot.Visible       = false;   // always hidden — slot is auto-assigned
            _txtSend.Enabled         = !isMod;
            _pnlKeyPicker.Visible    = isKey;
            _pnlModPicker.Visible    = isMod;
            _pnlLayoutPicker.Visible = isLayout;
            // Update the Send field label dynamically
            if (_lblSendFieldName != null)
                _lblSendFieldName.Text = isWP     ? Lang.T("Prediction cell")
                                       : isLayout ? Lang.T("Layout file")
                                       : Lang.T("Send");
            if (_lblWPDuplicate != null) _lblWPDuplicate.Visible = false;
            if (isWP)
            {
                // Auto-assign the next free slot; never show a duplicate warning
                int next = 0;
                while (_usedWpSlots.Contains(next) && next < 9) next++;
                _nudWPSlot.Value = Math.Min(9, next);
            }

            if (applyPicker && isMod) ApplyModChoice();
            if (applyPicker && isKey)
            {
                _txtSend.Text       = "";
                _lblRecordHint.Text = Lang.T("Press Record to record, or type directly");
            }
            if (applyPicker && isLayout)
                _txtSend.Text = "";
        }

        // ── Recording ─────────────────────────────────────────────────
        private void StartRecording()
        {
            if (_recording) return;
            _recording           = true;
            _winHeld             = false;
            _btnRecord.Text  = Lang.T("Press key now…");
            _btnRecord.Style = FluentButton.Variant.Danger;
            _btnRecord.Invalidate();
            _lblRecordHint.Text = Lang.T("Press Escape to cancel");
            _txtSend.Text        = "";

            // Install low-level keyboard hook so we capture Win key combinations
            // before Windows/shell acts on them, and suppress them from the OS.
            _hookProc   = LowLevelHookCallback;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                              GetModuleHandle(null), 0);
        }

        private void StopRecording(bool cancelled)
        {
            _recording  = false;
            _winHeld    = false;
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            _btnRecord.Text  = Lang.T("Record key / shortcut");
            _btnRecord.Style = FluentButton.Variant.Neutral;
            _btnRecord.Invalidate();
            _lblRecordHint.Text = cancelled ? Lang.T("Cancelled") : Lang.T("Recorded — edit if needed");
        }

        private IntPtr LowLevelHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var kbd = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUp   = wParam == (IntPtr)WM_KEYUP   || wParam == (IntPtr)WM_SYSKEYUP;

            // Always let key-up events through — suppressing them confuses keyboard state.
            if (isUp)
            {
                if (kbd.vkCode == VK_LWIN || kbd.vkCode == VK_RWIN)
                    _winHeld = false;
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (!isDown)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            // ── Key-down handling ─────────────────────────────────────

            // Escape: cancel recording, pass key through
            if (kbd.vkCode == VK_ESCAPE)
            {
                // Invoke on UI thread — hook runs on UI thread already but
                // BeginInvoke is safer for recursive message-pump situations.
                this.BeginInvoke((Action)(() => StopRecording(cancelled: true)));
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // Win key down: set flag, suppress so shell doesn't act on it
            if (kbd.vkCode == VK_LWIN || kbd.vkCode == VK_RWIN)
            {
                _winHeld = true;
                return (IntPtr)1; // suppress — do NOT call CallNextHookEx
            }

            // Bare modifier keys (Ctrl, Alt, Shift): ignore, wait for real key
            if (kbd.vkCode == 0x10 || kbd.vkCode == 0xA0 || kbd.vkCode == 0xA1 || // Shift
                kbd.vkCode == 0x11 || kbd.vkCode == 0xA2 || kbd.vkCode == 0xA3 || // Ctrl
                kbd.vkCode == 0x12 || kbd.vkCode == 0xA4 || kbd.vkCode == 0xA5)   // Alt
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // Real key: record it, then stop recording
            // Build send string from hook data (not KeyEventArgs — we're in the hook)
            bool ctrl  = (System.Windows.Forms.Control.ModifierKeys & Keys.Control) != 0;
            bool alt   = (System.Windows.Forms.Control.ModifierKeys & Keys.Alt)     != 0;
            bool shift = (System.Windows.Forms.Control.ModifierKeys & Keys.Shift)   != 0;

            string send = BuildSendFromHook(kbd.vkCode, ctrl, alt, shift, _winHeld);

            // Update UI on UI thread
            this.BeginInvoke((Action)(() =>
            {
                var newMode = DetectSendMode(send, _txtLabel.Text);
                if (newMode != _sendMode)
                    SetSendMode(newMode, applyPicker: false);

                _txtSend.Text = ToHuman(send);

                if (string.IsNullOrWhiteSpace(_txtLabel.Text))
                    _txtLabel.Text = BuildHumanLabel(kbd.vkCode, ctrl, alt, shift, _winHeld);

                StopRecording(cancelled: false);
            }));

            // Suppress the key so it doesn't type into whatever app is behind the editor
            return (IntPtr)1;
        }

        /// <summary>
        /// Builds the internal send string from raw hook data.
        /// Win key → "win:" prefix. Others → SendKeys syntax (^, %, +).
        /// </summary>
        private static string BuildSendFromHook(uint vk, bool ctrl, bool alt, bool shift, bool win)
        {
            string keyPart = VkCodeToSendKeys(vk, shift);

            if (win)
                return "win:" + keyPart;

            string prefix = "";
            if (ctrl)  prefix += "^";
            if (alt)   prefix += "%";
            if (shift && !IsPrintableVk(vk)) prefix += "+";

            return prefix + keyPart;
        }

        /// <summary>Builds a short human label from raw hook data.</summary>
        private static string BuildHumanLabel(uint vk, bool ctrl, bool alt, bool shift, bool win)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (win)   parts.Add("Win");
            if (ctrl)  parts.Add("Ctrl");
            if (alt)   parts.Add("Alt");
            if (shift && !IsPrintableVk(vk)) parts.Add("Shift");
            string key = VkCodeToSendKeys(vk, shift).TrimStart('{').TrimEnd('}');
            if (vk >= 0x41 && vk <= 0x5A) key = key.ToUpper(); // A-Z
            parts.Add(key);
            return string.Join("+", parts);
        }

        /// <summary>True for letter/digit VK codes where Shift changes the character.</summary>
        private static bool IsPrintableVk(uint vk) =>
            (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39);

        /// <summary>
        /// Converts a raw VK code to its SendKeys key string.
        /// Letters → lowercase, digits → digit char, others → {NAME}.
        /// </summary>
        private static string VkCodeToSendKeys(uint vk, bool shift)
        {
            if (vk >= 0x41 && vk <= 0x5A) // A-Z
                return ((char)('a' + vk - 0x41)).ToString();
            if (vk >= 0x30 && vk <= 0x39) // 0-9
                return ((char)('0' + vk - 0x30)).ToString();
            if (vk >= 0x60 && vk <= 0x69) // Numpad 0-9
                return "{NUMPAD" + (vk - 0x60) + "}";
            if (vk >= 0x70 && vk <= 0x7B) // F1-F12
                return "{F" + (vk - 0x70 + 1) + "}";
            return vk switch
            {
                0x0D => "{ENTER}",
                0x08 => "{BACKSPACE}",
                0x09 => "{TAB}",
                0x1B => "{ESC}",
                0x2E => "{DELETE}",
                0x2D => "{INSERT}",
                0x24 => "{HOME}",
                0x23 => "{END}",
                0x21 => "{PGUP}",
                0x22 => "{PGDN}",
                0x25 => "{LEFT}",
                0x26 => "{UP}",
                0x27 => "{RIGHT}",
                0x28 => "{DOWN}",
                0x20 => " ",
                0x14 => "{CAPSLOCK}",
                0x90 => "{NUMLOCK}",
                0x91 => "{SCROLLLOCK}",
                0x2C => "{PRTSC}",
                0x13 => "{BREAK}",
                _    => "{" + vk.ToString("X2") + "}",
            };
        }



        private void ApplyModChoice()
        {
            if (_initialising) return;
            if (_sendMode != SendMode.Modifier) return;
            int idx = _cmbModChoice.SelectedIndex;
            if (idx < 0 || idx >= _modifiers.Length) return;
            var (modLabel, _) = _modifiers[idx];
            _txtSend.Text  = "{" + modLabel + "}";
            _txtLabel.Text = modLabel;
        }

        // ── Human-readable ↔ internal Send conversion ─────────────────
        // Internal:       ^c   %{F4}   {ENTER}   ^v    +a
        // Human-readable: {Ctrl}c  {Alt}{F4}  {ENTER}  {Ctrl}v  {Shift}a

        private static string ToHuman(string send)
        {
            if (string.IsNullOrEmpty(send)) return send;
            // "win:" prefix → "{Win}" + rest
            if (send.StartsWith("win:"))
                return "{Win}" + ToHuman(send.Substring(4));
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < send.Length)
            {
                char ch = send[i];
                if      (ch == '^') { sb.Append("{Ctrl}");  i++; }
                else if (ch == '%') { sb.Append("{Alt}");   i++; }
                else if (ch == '+') { sb.Append("{Shift}"); i++; }
                else if (ch == '(')
                {
                    i++;
                    while (i < send.Length && send[i] != ')') { sb.Append(send[i]); i++; }
                    if (i < send.Length) i++;
                }
                else { sb.Append(ch); i++; }
            }
            return sb.ToString();
        }

        private static string FromHuman(string human)
        {
            if (string.IsNullOrEmpty(human)) return human;
            // {Win} must be handled before other replacements and produces "win:" prefix.
            // Because win: is a prefix for the whole expression, we handle it specially:
            // {Win}m → win:m,  {Win}{LEFT} → win:{LEFT}
            if (human.StartsWith("{Win}"))
                return "win:" + FromHuman(human.Substring(5));
            return human
                .Replace("{Ctrl}",  "^")
                .Replace("{Alt}",   "%")
                .Replace("{Shift}", "+");
        }

        private void CheckWPDuplicate()
        {
            if (_lblWPDuplicate == null) return;
            int slot = (int)_nudWPSlot.Value;
            bool isDuplicate = _usedWpSlots.Contains(slot);
            _lblWPDuplicate.Text    = isDuplicate ? Lang.T("WP slot already in use") : "";
            _lblWPDuplicate.Visible = isDuplicate;
        }

        private SendMode DetectSendMode(string send, string label)
        {
            if (string.IsNullOrEmpty(send) && _modifiers.Any(m => m.Label == label))
                return SendMode.Modifier;
            if (!string.IsNullOrEmpty(send) && send.StartsWith("wp:", StringComparison.Ordinal))
                return SendMode.WordPrediction;
            if (!string.IsNullOrEmpty(send) && send.StartsWith("layout:", StringComparison.Ordinal))
                return SendMode.Layout;
            if (!string.IsNullOrEmpty(send) && !SendKeysHelper.IsPlainText(send))
                return SendMode.KeySequence;
            return SendMode.Text;
        }
        // ── OPTION 3 END: mode selector and picker UI methods ─────────

        // ── Group panel ───────────────────────────────────────────────
        private Panel AddGroup(Func<string> getTitle, int x, int y, int w, int h, Color accentColor)
        {
            var pnl = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = Fluent.BgPage };
            pnl.Paint += (s, e) =>
                FluentPainter.PaintCard(e.Graphics, pnl.Width, pnl.Height, getTitle(), accentColor, HDR_H);
            Controls.Add(pnl);
            _transGroups.Add((pnl, getTitle));
            return pnl;
        }

        // ── Color row: hex textbox + swatch ──────────────────────────
        private Panel AddColorRow(Panel parent, int x, int y, int totalW)
        {
            int sw = 32;
            var txtHex = new TextBox
            {
                Left = x, Top = y, Width = totalW - sw - 5,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Courier New", 12f),
            };
            var swatch = new Panel
            {
                Left = x + totalW - sw, Top = y, Width = sw, Height = 26,
                BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, BackColor = Color.Gray,
            };
            txtHex.TextChanged += (s, e) => { swatch.BackColor = ParseColor(txtHex.Text, swatch.BackColor); Refresh2(); };
            swatch.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { Color = swatch.BackColor };
                if (dlg.ShowDialog() == DialogResult.OK) txtHex.Text = SettingsManager.Hex(dlg.Color);
            };
            parent.Controls.Add(txtHex);
            parent.Controls.Add(swatch);
            swatch.Tag = txtHex;
            return swatch;
        }

        private string GetSwatchHex(Panel s) => (s.Tag is TextBox t) ? t.Text : "";
        private void SetSwatchHex(Panel s, string hex)
        {
            if (s.Tag is TextBox t) { t.Text = hex; s.BackColor = ParseColor(hex, s.BackColor); }
        }

        private void AddFieldLabel(Panel parent, Func<string> getText, int x, int y)
        {
            var lbl = new Label
            {
                Text = getText(), Left = x, Top = y + 4, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            parent.Controls.Add(lbl);
            _transLabels.Add((lbl, getText));
        }

        private void AddHint(Panel parent, Func<string> getText, int x, int y)
        {
            int maxW = parent.Width - x - PAD;
            parent.Controls.Add(new Label
            {
                Text = getText(), Left = x, Top = y + 6,
                AutoSize = true, MaximumSize = new Size(maxW, 0),
                ForeColor = C_HINT, BackColor = Color.Transparent, Font = F_HINT,
            });
        }

        private TextBox AddInput(Panel parent, int x, int y, int w)
        {
            var tb = new TextBox
            {
                Left = x, Top = y, Width = w,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle, Font = F_INPUT,
            };
            parent.Controls.Add(tb);
            return tb;
        }

        private Button MakeActionBtn(string text, int x, int y, int w, int h)
        {
            var btn = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                Style = FluentButton.Variant.Neutral,
            };
            Controls.Add(btn);
            return btn;
        }

        // ── Populate ──────────────────────────────────────────────────
        private void PopulateFields(KeyProps p)
        {
            _txtLabel.Text      = p.Label      ?? "";
            _txtSend.Text       = p.Send       ?? "";
            // Populate WP slot NUD from wp:N format
            if (p.Send != null && p.Send.StartsWith("wp:") &&
                int.TryParse(p.Send.Substring(3), out int wpSlot))
                _nudWPSlot.Value = Math.Clamp(wpSlot, 0, 9);
            else
                _nudWPSlot.Value = 0;
            _txtShiftLabel.Text = p.ShiftLabel ?? "";
            _txtAltGrLabel.Text = p.AltGrLabel ?? "";
            // Strip "layout:" prefix for display — flags let Apply() re-add it
            string shiftRaw  = p.ShiftSend  ?? "";
            string altGrRaw  = p.AltGrSend  ?? "";
            SetShiftSendText(
                shiftRaw.StartsWith("layout:", StringComparison.Ordinal) ? shiftRaw.Substring(7) : shiftRaw,
                shiftRaw.StartsWith("layout:", StringComparison.Ordinal));
            SetAltGrSendText(
                altGrRaw.StartsWith("layout:", StringComparison.Ordinal) ? altGrRaw.Substring(7) : altGrRaw,
                altGrRaw.StartsWith("layout:", StringComparison.Ordinal));
            _nudColSpan.Value = Math.Max(1, Math.Min(_maxCols, _initColSpan));
            _nudRowSpan.Value = Math.Max(1, Math.Min(_maxRows, _initRowSpan));

            // Resolve global settings once — used for font and colour placeholders
            var ownerG = _ownerGlobal;   // set at ctor time; Owner property is null here (called before ShowDialog)
            Color gFontColor   = ownerG?.FontColor   ?? ColorTranslator.FromHtml("#E0E0FF");
            Color gKeyColor    = ownerG?.KeyColor    ?? ColorTranslator.FromHtml("#2D2D4A");

            string resolvedFont = string.IsNullOrEmpty(p.FontName)
                ? (ownerG?.FontName ?? "Arial") : p.FontName;
            int fi = _cmbFont.Items.IndexOf(resolvedFont);
            _cmbFont.SelectedIndex = fi >= 0 ? fi : (_cmbFont.Items.Count > 0 ? 0 : -1);

            int clampedSize = Math.Clamp(p.FontSize, 0, (int)_nudFontSize.Maximum);
            if (clampedSize > 0)
            { _nudFontSize.Value = clampedSize; _chkAutoSize.Checked = false; _nudFontSize.Enabled = true; }
            else
            { _nudFontSize.Value = 0; _chkAutoSize.Checked = true; _nudFontSize.Enabled = false; }

            // For empty spacer keys (no label, no send) always show the current global values,
            // even if the XML baked in explicit per-key styles (e.g. background-matching spacers).
            // This lets the user see what the key would look like with current global settings.
            bool isEmptyKey = string.IsNullOrEmpty(p.Label) && string.IsNullOrEmpty(p.Send);

            int globalBt = ownerG?.BorderThickness ?? 1;
            _nudBorderThickness.Value = Math.Clamp(
                (isEmptyKey || p.BorderThickness == -1) ? globalBt : p.BorderThickness,
                -1, (int)_nudBorderThickness.Maximum);

            SetSwatchHex(_pnlFontColor,   SettingsManager.Hex(
                (isEmptyKey || p.FontColor.IsEmpty)   ? gFontColor         : p.FontColor));
            SetSwatchHex(_pnlKeyColor,    SettingsManager.Hex(
                (isEmptyKey || p.KeyColor.IsEmpty)    ? gKeyColor          : p.KeyColor));
            SetSwatchHex(_pnlBorderColor, SettingsManager.Hex(
                (isEmptyKey || p.BorderColor.IsEmpty) ? _globalBorderColor : p.BorderColor));

            // Group selector
            if (_cmbGroup != null)
            {
                int gi = string.IsNullOrEmpty(p.GroupName)
                    ? 0
                    : _cmbGroup.Items.IndexOf(p.GroupName);
                _cmbGroup.SelectedIndex = gi >= 0 ? gi : 0;
            }

            // ── OPTION 3 BEGIN: detect and set initial send mode ──────
            // _initialising suppresses ApplyModChoice() while combo boxes are
            // being populated — without it, the SelectedIndex assignment below
            // fires the change event and overwrites fields mid-initialisation.
            // Fix 2: display human-readable Send value for key-sequence mode.
            // To revert: remove this block and replace with SetSendMode(SendMode.Text)
            _initialising = true;
            var detectedMode = DetectSendMode(p.Send ?? "", p.Label ?? "");
            SetSendMode(detectedMode, applyPicker: false);
            if (detectedMode == SendMode.WordPrediction) CheckWPDuplicate();
            // Sync dropdowns to match the actual key function
            if (detectedMode == SendMode.Modifier)
            {
                _txtSend.Text = "{" + (p.Label ?? "") + "}";
                int mi = Array.FindIndex(_modifiers, m => m.Label == (p.Label ?? ""));
                if (mi >= 0) _cmbModChoice.SelectedIndex = mi;
            }
            else if (detectedMode == SendMode.KeySequence)
            {
                _txtSend.Text = ToHuman(p.Send ?? "");
                _lblRecordHint.Text = Lang.T("Press Record to re-record, or edit directly");
            }
            else if (detectedMode == SendMode.Layout)
            {
                // Strip the "layout:" prefix — the mode button already indicates the type
                string raw = p.Send ?? "";
                _txtSend.Text = raw.StartsWith("layout:", StringComparison.Ordinal)
                    ? raw.Substring(7) : raw;
            }
            _initialising = false;
            // ── OPTION 3 END ──────────────────────────────────────────

            Refresh2();
        }

        // ── Live preview ──────────────────────────────────────────────
        private void Refresh2()
        {
            var ownerGlob = _ownerGlobal;   // cached at ctor time; Owner property is null during construction
            Color gFc = ownerGlob?.FontColor   ?? ColorTranslator.FromHtml("#E0E0FF");
            Color gKc = ownerGlob?.KeyColor    ?? ColorTranslator.FromHtml("#2D2D4A");
            Color gBc = ownerGlob?.BorderColor ?? ColorTranslator.FromHtml("#3C3C5A");
            Color fc = ParseColor(GetSwatchHex(_pnlFontColor),   gFc);
            Color kc = ParseColor(GetSwatchHex(_pnlKeyColor),    gKc);
            Color bc = ParseColor(GetSwatchHex(_pnlBorderColor), gBc);
            string fn = _cmbFont.SelectedItem?.ToString() ?? ownerGlob?.FontName ?? "Arial";
            int    fs = (_chkAutoSize.Checked || _nudFontSize.Value == 0) ? 13 : (int)_nudFontSize.Value;
            int    btRaw = (int)_nudBorderThickness.Value;
            // -1 = use global (retrieve from owner); 0 = no border; n = explicit
            int    bt = btRaw == -1
                ? (ownerGlob?.BorderThickness ?? 1)
                : btRaw;

            _lblPreviewKey.Text      = (_txtLabel?.Text ?? "").Replace("&", "&&");
            _lblPreviewKey.ForeColor = fc;
            _lblPreviewKey.BackColor = kc;
            _pnlPreview.BackColor    = bc;
            _pnlPreview.Padding      = new Padding(Math.Max(0, bt));
            try
            {
                var newFont = new Font(fn, fs, FontStyle.Bold);
                // Dispose the old font if it is not one of the static shared fonts
                var oldFont = _lblPreviewKey.Font;
                _lblPreviewKey.Font = newFont;
                if (oldFont != null && oldFont != F_LABEL && oldFont != F_INPUT &&
                    oldFont != F_HEADER && oldFont != F_BTN && oldFont != F_HINT)
                    oldFont.Dispose();
            }
            catch { }
        }

        // ── Apply ─────────────────────────────────────────────────────
        private void Apply()
        {
            string label = _txtLabel.Text.Trim();

            var ownerGl = _ownerGlobal;   // cached at ctor time; consistent with PopulateFields and Refresh2
            // Store Color.Empty when the hex matches the global value — preserves the
            // "use global" sentinel so changing the global later cascades to this key.
            string fcHex = GetSwatchHex(_pnlFontColor).Trim();
            string kcHex = GetSwatchHex(_pnlKeyColor).Trim();
            string bcStr = GetSwatchHex(_pnlBorderColor).Trim();
            Color fc = HexMatchesGlobal(fcHex, ownerGl?.FontColor)
                ? Color.Empty : ParseColor(fcHex, Color.Empty);
            Color kc = HexMatchesGlobal(kcHex, ownerGl?.KeyColor)
                ? Color.Empty : ParseColor(kcHex, Color.Empty);
            Color bc = HexMatchesGlobal(bcStr, ownerGl?.BorderColor) || bcStr == ""
                ? Color.Empty : ParseColor(bcStr, Color.Empty);

            // ── OPTION 3 BEGIN: convert human-readable Send back to internal format
            // Fix 1: modifier keys always store "" as Send — never fall back to label
            // Fix 2: skip auto-escape for key-sequence and modifier modes
            string send;
            if (_sendMode == SendMode.Modifier)
            {
                send = "";  // modifier keys have empty Send; label drives behaviour
            }
            else if (_sendMode == SendMode.WordPrediction)
            {
                send = "wp:" + (int)_nudWPSlot.Value;
            }
            else if (_sendMode == SendMode.KeySequence)
            {
                send = FromHuman(_txtSend.Text);
                // Fix 2: do NOT apply EscapeForSend — key sequences are already correct syntax
            }
            else if (_sendMode == SendMode.Layout)
            {
                string path = _txtSend.Text.Trim();
                send = string.IsNullOrEmpty(path) ? "" : "layout:" + path;
            }
            else
            {
                send = SendKeysHelper.EscapeForSend(_txtSend.Text);
                if (string.IsNullOrEmpty(send)) send = label;
            }
            // ── OPTION 3 END ──────────────────────────────────────────

            // A key only needs a visible label when it actually sends something.
            // If both label and send are empty the key stays a spacer — don't force a label.
            // If send is set but label is empty, mirror the send value as the label.
            if (string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(send)) label = send;

            ResultColSpan = (int)_nudColSpan.Value;
            ResultRowSpan = (int)_nudRowSpan.Value;

            string fontName = _cmbFont.SelectedItem?.ToString() ?? "";
            // Store "" (use global) when font matches global setting
            if (fontName == (ownerGl?.FontName ?? "Arial")) fontName = "";

            int fontSize = (_chkAutoSize.Checked || _nudFontSize.Value == 0)
                ? 0 : (int)_nudFontSize.Value;

            string groupName = (_cmbGroup != null && _cmbGroup.SelectedIndex > 0)
                ? _cmbGroup.SelectedItem?.ToString() ?? ""
                : "";

            // Re-add "layout:" prefix to Shift/AltGr sends when the field was showing a stripped path
            string shiftSend = _txtShiftSend.Text ?? "";
            if (_shiftSendIsLayout && !string.IsNullOrEmpty(shiftSend))
                shiftSend = "layout:" + shiftSend;

            string altGrSend = _txtAltGrSend.Text ?? "";
            if (_altGrSendIsLayout && !string.IsNullOrEmpty(altGrSend))
                altGrSend = "layout:" + altGrSend;

            Result = new KeyProps(label, send,
                                  _txtShiftLabel.Text ?? "",
                                  shiftSend,
                                  _txtAltGrLabel.Text ?? "",
                                  altGrSend)
            {
                FontName        = fontName,
                FontSize        = fontSize,
                FontColor       = fc, KeyColor = kc, BorderColor = bc,
                // Store -1 (inherit from global) when the value matches the global setting,
                // exactly like HexMatchesGlobal for colours — preserves the inherit sentinel.
                BorderThickness = ((int)_nudBorderThickness.Value == -1 ||
                                   (int)_nudBorderThickness.Value == (ownerGl?.BorderThickness ?? 1))
                                  ? -1 : (int)_nudBorderThickness.Value,
                GroupName       = groupName,
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Color ParseColor(string hex, Color fallback) => SettingsManager.ParseColor(hex, fallback);

        /// <summary>True when the hex string represents the same colour as the global value.</summary>
        private static bool HexMatchesGlobal(string hex, Color? globalColor)
        {
            if (globalColor == null || string.IsNullOrEmpty(hex)) return false;
            Color parsed = SettingsManager.ParseColor(hex, Color.Empty);
            return !parsed.IsEmpty &&
                   parsed.R == globalColor.Value.R &&
                   parsed.G == globalColor.Value.G &&
                   parsed.B == globalColor.Value.B;
        }

        private static List<string> GetInstalledFonts()
        {
            var list = new List<string>();
            using (var ifc = new InstalledFontCollection())
                foreach (var ff in ifc.Families) list.Add(ff.Name);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }
}

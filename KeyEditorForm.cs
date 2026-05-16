using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A modal dialog that lets the user edit all properties of a single keyboard key.
    /// This includes the key's label, what it sends when pressed (including modifier keys,
    /// shortcuts, word-prediction slots, or layout switches), its size (column/row span),
    /// and its visual appearance (font, colors, border).
    ///
    /// After the dialog closes with OK, the caller reads <see cref="Result"/>,
    /// <see cref="ResultColSpan"/>, and <see cref="ResultRowSpan"/> to apply the changes.
    /// </summary>
    public class KeyEditorForm : Form
    {
        /// <summary>
        /// The edited key properties after the user clicks Apply.
        /// If the dialog is cancelled this still holds the original values.
        /// </summary>
        public KeyProps Result        { get; private set; }

        /// <summary>How many grid columns wide the key should be (minimum 1).</summary>
        public int     ResultColSpan { get; private set; } = 1;

        /// <summary>How many grid rows tall the key should be (minimum 1).</summary>
        public int     ResultRowSpan { get; private set; } = 1;

        // The column/row span values the key had when the dialog opened.
        // These are used as initial values for the span spinners.
        private int    _initColSpan, _initRowSpan, _maxRows;

        // ── Form controls ─────────────────────────────────────────────
        // These fields hold references to every interactive control on the form
        // so that BuildUI(), PopulateFields(), and Apply() can all reach them.
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

        // The key state at the moment the dialog was opened, kept for reference
        // (e.g. the form title shows the original label even if the user edits it).
        private readonly KeyProps        _original;

        // The global border color is used as a fallback when a key has no per-key border override.
        // Stored once at construction because it may be needed before ShowDialog() wires up Owner.
        private readonly Color          _globalBorderColor;

        // The keyboard window's current theme settings (font, colors, etc.).
        // Cached at construction time because the Owner property is null until ShowDialog().
        private readonly VisualTheme _ownerGlobal;

        // These two lists let OnLanguageChanged() update every label and group header
        // without knowing which controls exist — each entry stores the control plus
        // a lambda that returns the freshly-translated text.
        private readonly List<(Label Ctrl, Func<string> GetText)> _transLabels
            = new List<(Label, Func<string>)>();
        private readonly List<(Panel Pnl, Func<string> GetTitle)> _transGroups
            = new List<(Panel, Func<string>)>();

        // ── Fluent/WinUI 3 theme colors and fonts ─────────────────────
        // These properties pull values from the static Fluent palette so the
        // editor always matches the rest of the application's visual style.
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

        // Layout constants used throughout BuildUI() to keep row heights and padding consistent.
        private const int HDR_H = 42;   // height of the colored group header strip
        private const int ROW_H = 50;   // vertical space each field row occupies
        private const int PAD   = 18;   // inner padding inside group panels

        // Maximum number of grid columns in the layout — used to cap the ColSpan spinner.
        private readonly int             _maxCols;

        // The list of named key groups defined for this layout.
        // Groups allow keys to be toggled on/off together (e.g. a "symbols" layer).
        private readonly List<KeyGroup>  _groups;

        // ══════════════════════════════════════════════════════════════
        // ── OPTION 3 BEGIN: SendMode enum and mode-related fields ─────
        // To remove option 3: delete everything between OPTION 3 BEGIN
        // and OPTION 3 END markers, then revert the two marked changes
        // in BuildUI() and PopulateFields().
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Describes what a key does when pressed. The mode determines which UI controls
        /// are shown in the "Send" area and how the send string is formatted when saved.
        /// </summary>
        private enum SendMode
        {
            /// <summary>The key types one or more plain text characters.</summary>
            Text,
            /// <summary>The key sends a keyboard shortcut or special key (e.g. Ctrl+C, F5).</summary>
            KeySequence,
            /// <summary>The key acts as a modifier toggle (Shift, Ctrl, Alt, etc.).</summary>
            Modifier,
            /// <summary>The key shows a word-prediction suggestion in a numbered slot.</summary>
            WordPrediction,
            /// <summary>The key switches the keyboard to a different layout file.</summary>
            Layout
        }

        // The currently selected mode. Updated when the user clicks a mode button.
        private SendMode _sendMode = SendMode.Text;

        // The five mode-selector buttons shown above the Send field.
        private FluentButton _btnModeText, _btnModeKey, _btnModeMod, _btnModeWP, _btnModeLayout;

        // The panel and button for picking a layout file (visible only in Layout mode).
        private Panel        _pnlLayoutPicker;
        private FluentButton _btnBrowseLayout;

        // The folder that contains the currently open layout XML file.
        // Used as the starting directory when the user browses for another layout file,
        // and for converting absolute paths to relative ones.
        private string  _layoutDir;

        // Tracks which of the three send fields (_txtSend, _txtShiftSend, _txtAltGrSend)
        // the user last clicked into, so the Browse button fills the right field.
        private TextBox _activeSendField;

        // These flags remember whether _txtShiftSend / _txtAltGrSend are currently
        // displaying a layout path that was stripped of its "layout:" prefix.
        // Apply() uses them to re-add the prefix before saving.
        private bool _shiftSendIsLayout  = false;
        private bool _altGrSendIsLayout  = false;

        // Guards used to prevent TextChanged handlers from resetting the layout flags
        // when the code itself is updating the text (not the user).
        private bool _progShiftSend = false;
        private bool _progAltGrSend = false;

        // Orange accent color for the Layout mode button to make it visually distinct.
        private static readonly Color C_MODE_LAYOUT = Color.FromArgb(211, 84, 0);

        // ── Key sequence recorder UI ───────────────────────────────────
        // Shown only when mode = KeySequence.
        private Panel        _pnlKeyPicker;
        private FluentButton _btnRecord;
        private Label  _lblRecordHint;
        private bool   _recording = false;

        // True while the Windows key is physically held during a recording session.
        // Tracked separately because the low-level hook suppresses the Win key-up event.
        private bool   _winHeld  = false;

        // ── Low-level keyboard hook P/Invoke declarations ─────────────
        // A low-level keyboard hook lets us intercept keystrokes system-wide,
        // including Win key combinations, before Windows acts on them.
        // This is necessary for the "Record key / shortcut" feature.

        /// <summary>Delegate type required by SetWindowsHookEx for a low-level keyboard hook.</summary>
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>Installs a system-wide hook for the given hook type.</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        /// <summary>Removes a previously installed hook.</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        /// <summary>Passes the hook event to the next hook in the chain.</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        /// <summary>Returns the module handle needed when registering a global hook.</summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        /// <summary>
        /// The data block passed to the low-level keyboard hook callback.
        /// Contains the virtual key code and timing information for the intercepted keystroke.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint   vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        // Windows hook type constant: low-level keyboard hook (intercepts all keystrokes).
        private const int  WH_KEYBOARD_LL = 13;

        // Windows message constants sent to the hook callback to identify the event type.
        private const int  WM_KEYDOWN     = 0x0100;
        private const int  WM_KEYUP       = 0x0101;
        private const int  WM_SYSKEYDOWN  = 0x0104;  // Alt+key down
        private const int  WM_SYSKEYUP    = 0x0105;  // Alt+key up

        // Virtual key codes for keys that need special handling in the recorder.
        private const uint VK_LWIN        = 0x5B;
        private const uint VK_RWIN        = 0x5C;
        private const uint VK_ESCAPE      = 0x1B;

        // The handle returned by SetWindowsHookEx. IntPtr.Zero means no hook is installed.
        private IntPtr              _hookHandle = IntPtr.Zero;

        // The hook callback delegate stored in a field to prevent the garbage collector
        // from freeing it while the hook is still active (the native hook holds a raw pointer).
        private LowLevelKeyboardProc _hookProc;

        // ── Modifier picker UI ─────────────────────────────────────────
        // Shown only when mode = Modifier.
        private Panel    _pnlModPicker;
        private ComboBox _cmbModChoice;

        // Accent colors for the five mode buttons (one color per mode, grey when unselected).
        private static readonly Color C_MODE_TEXT = Color.FromArgb(41,  128, 185);
        private static readonly Color C_MODE_KEY  = Color.FromArgb(192,  57,  43);
        private static readonly Color C_MODE_MOD  = Color.FromArgb(142,  68, 173);
        private static readonly Color C_MODE_OFF  = Color.FromArgb(180, 185, 192);
        private static readonly Color C_RECORDING = Color.FromArgb(220,  50,  50);

        /// <summary>
        /// The modifier keys the user can assign.
        /// Each entry is (internal label stored in XML, display name shown in the UI).
        /// The internal label must match the strings used in KeyLayout.ModifierLabels.
        /// </summary>
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

        /// <summary>
        /// Creates and prepopulates the key editor dialog.
        /// </summary>
        /// <param name="props">The current properties of the key being edited.</param>
        /// <param name="owner">
        ///   The parent <see cref="KeyboardForm"/>. Used to read global theme settings
        ///   (font, colors) so the editor can show correct placeholder values and compare
        ///   per-key overrides against the global defaults.
        /// </param>
        /// <param name="colSpan">Current column span of the key (how many columns wide).</param>
        /// <param name="rowSpan">Current row span of the key (how many rows tall).</param>
        /// <param name="maxCols">Total number of columns in the layout — caps the ColSpan spinner.</param>
        /// <param name="maxRows">Total number of rows in the layout — caps the RowSpan spinner.</param>
        /// <param name="usedWpSlots">
        ///   Word-prediction slot numbers already used by other keys in the layout.
        ///   Used to warn when the user selects a duplicate slot.
        /// </param>
        /// <param name="groups">Named key groups available in this layout.</param>
        /// <param name="layoutDir">
        ///   The directory that contains the current layout XML file.
        ///   Used as the starting folder for the layout-file browser and for converting
        ///   absolute paths to relative ones.
        /// </param>
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

            // Start with a working copy of the props so Apply() can write to Result
            // without touching the original object.
            Result    = props.Clone();
            ResultColSpan = Math.Max(1, colSpan);
            ResultRowSpan = Math.Max(1, rowSpan);
            _initColSpan  = ResultColSpan;
            _initRowSpan  = ResultRowSpan;
            _maxRows      = Math.Max(1, maxRows);

            // Standard WinForms form setup
            Text         = $"{Lang.T("Edit Key")}  [{props.Label}]";
            BackColor    = C_BG;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox  = MinimizeBox = false;
            ShowIcon     = false;
            StartPosition = FormStartPosition.CenterParent;
            Size         = new Size(980, 560);
            TopMost      = true;   // stay on top of the keyboard window
            Font         = F_LABEL;

            BuildUI(props);

            // Subscribe to language-change events so translated text updates live
            Lang.LanguageChanged += OnLanguageChanged;

            // Always clean up the hook and unsubscribe from events when the form closes,
            // regardless of whether the user clicked Apply or Cancel.
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

        /// <summary>
        /// Called whenever the application language changes.
        /// Updates every translatable string on the form without rebuilding the whole UI.
        /// </summary>
        private void OnLanguageChanged()
        {
            Text = $"{Lang.T("Edit Key")}  [{_original.Label}]";
            _btnApply.Text         = Lang.T("Apply");
            _btnCancel.Text        = Lang.T("Cancel");
            _chkAutoSize.Text      = Lang.T("Auto");
            _btnModeLayout.Text    = Lang.T("Layout");
            UpdateBrowseLabel();

            // Update all labels registered during BuildUI() via AddFieldLabel()
            foreach (var (ctrl, getText) in _transLabels) ctrl.Text = getText();

            // Trigger a repaint of group panels — their headers are drawn in the Paint event,
            // not in a Label control, so Invalidate() is needed to refresh the translated title.
            foreach (var (pnl,  _)       in _transGroups) pnl.Invalidate();
            Invalidate(true);
        }

        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Constructs all controls and lays them out.
        /// The form is divided into two columns:
        ///   Left  — "Key Content": label, mode selector, send fields, span spinners.
        ///   Right — "Appearance": font, colors, border, group, live preview.
        /// Apply/Cancel buttons sit below both columns.
        /// </summary>
        /// <param name="p">The key being edited, used to pre-populate field values.</param>
        private void BuildUI(KeyProps p)
        {
            int margin  = 18;
            int gap     = 14;
            int leftW   = 510;   // Key Content column width in pixels
            int rightW  = ClientSize.Width - margin * 2 - gap - leftW;  // Appearance column width
            int leftX   = margin;
            int rightX  = margin + leftW + gap;
            int colW    = leftW;  // alias used by left-column group sizing

            // ── OPTION 3 BEGIN: extra rows in Key Content for mode UI ─
            // Original keyRows = 8. Added 4 rows: mode selector (3 rows) + picker row.
            int keyRows = 12;
            // ── OPTION 3 END ──────────────────────────────────────────

            int keyH    = HDR_H + PAD + keyRows * ROW_H + PAD;

            // AddGroup() creates a rounded card panel with a colored header strip
            var grpKey  = AddGroup(() => Lang.T("Key Content"), leftX, margin, colW, keyH,
                                   Color.FromArgb(41, 128, 185));

            // lx = label column x, vx = value/control column x, vw = value column width
            int lx = PAD, vx = 175, vw = colW - lx - vx - PAD;
            int gy = HDR_H + PAD;   // current vertical position within the group panel

            AddFieldLabel(grpKey, () => Lang.T("Label"), lx, gy);
            _txtLabel = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            // ── OPTION 3 BEGIN: mode selector row ─────────────────────
            // Adds three rows of mode-selector buttons that control what the Send field does.
            AddOption3ModeSelector(grpKey, lx, vx, vw, ref gy);
            // ── OPTION 3 END ──────────────────────────────────────────

            // The Send field label changes depending on the selected mode (e.g. "Prediction cell")
            _lblSendFieldName = new Label
            {
                Text = Lang.T("Send"), Left = lx, Top = gy + 4, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            grpKey.Controls.Add(_lblSendFieldName);
            _txtSend = AddInput(grpKey, vx, gy, vw);

            // Word-prediction slot spinner — overlays Send field, visible only in WP mode.
            // The slot number (0-9) determines which prediction suggestion this key displays.
            _nudWPSlot = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 0, Maximum = 9,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, Visible = false,
            };
            _nudWPSlot.ValueChanged += (s, e) => { Refresh2(); CheckWPDuplicate(); };
            grpKey.Controls.Add(_nudWPSlot);

            // Duplicate slot warning label (shown below the NUD when the chosen slot is taken)
            _lblWPDuplicate = new Label
            {
                Left = vx, Top = gy + 24, Width = vw, Height = 18,
                ForeColor = Fluent.Danger, BackColor = Color.Transparent,
                Font = Fluent.FontHint, Visible = false, Text = "",
            };
            grpKey.Controls.Add(_lblWPDuplicate);
            gy += ROW_H;

            // ── OPTION 3 BEGIN: picker row (key sequence / modifier / layout) ──
            // Adds the contextual panel that appears below the Send field depending on the mode.
            AddOption3PickerRow(grpKey, lx, vx, vw, ref gy);
            // ── OPTION 3 END ──────────────────────────────────────────

            // Shift and AltGr fields: optional alternative labels/actions for modified key states
            AddFieldLabel(grpKey, () => Lang.T("Shift label"), lx, gy);
            _txtShiftLabel = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("Shift send"), lx, gy);
            _txtShiftSend = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("AltGr label"), lx, gy);
            _txtAltGrLabel = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            AddFieldLabel(grpKey, () => Lang.T("AltGr send"), lx, gy);
            _txtAltGrSend = AddInput(grpKey, vx, gy, vw); gy += ROW_H;

            // ColSpan: how many grid columns the key occupies (1 = normal width)
            AddFieldLabel(grpKey, () => Lang.T("Key width"), lx, gy);
            _nudColSpan = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 1, Maximum = _maxCols,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            grpKey.Controls.Add(_nudColSpan);
            gy += ROW_H;

            // RowSpan: how many grid rows the key occupies (1 = normal height)
            AddFieldLabel(grpKey, () => Lang.T("Key height"), lx, gy);
            _nudRowSpan = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 1, Maximum = _maxRows,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            grpKey.Controls.Add(_nudRowSpan);
            gy += ROW_H;



            // ── RIGHT COLUMN: Appearance ───────────────────────────────
            int rightY = margin;
            int styleRows = 8;  // font+size+fontcolor+keycolor+bordercolor+borderthickness+group+preview
            int styleH    = HDR_H + PAD + styleRows * ROW_H + 28 + PAD;
            var grpStyle  = AddGroup(() => Lang.T("Appearance"), rightX, rightY, rightW, styleH,
                                     Color.FromArgb(39, 174, 96));
            rightY += styleH + gap;

            // Same lx/vx/vw pattern as the left column, but narrower
            int slx = PAD, svx = 140, svw = rightW - slx - svx - PAD;
            gy = HDR_H + PAD;

            // Font family selector (populated with all installed system fonts)
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

            // Font size: a numeric spinner plus an "Auto" checkbox.
            // When Auto is checked the font size is determined at render time to fit the key.
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
                // Disable the numeric spinner when auto-sizing is active
                _nudFontSize.Enabled = !_chkAutoSize.Checked;
                Refresh2();
            };
            grpStyle.Controls.Add(_chkAutoSize); gy += ROW_H;

            // Color rows: each AddColorRow() creates a hex text box + a color swatch button
            AddFieldLabel(grpStyle, () => Lang.T("Font color"), slx, gy);
            _pnlFontColor = AddColorRow(grpStyle, svx, gy, svw); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Key color"), slx, gy);
            _pnlKeyColor = AddColorRow(grpStyle, svx, gy, svw); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Border color"), slx, gy);
            _pnlBorderColor = AddColorRow(grpStyle, svx, gy, svw); gy += ROW_H;

            // Border thickness: -1 means "use global setting", 0 means no border
            AddFieldLabel(grpStyle, () => Lang.T("Border thickness"), slx, gy);
            _nudBorderThickness = new NumericUpDown
            {
                Left = svx, Top = gy, Width = 65, Minimum = -1, Maximum = 10,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            _nudBorderThickness.ValueChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_nudBorderThickness);
            gy += ROW_H;

            // Group selector: index 0 = "(no group)", indices 1+ = named groups from the layout
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

            // Live preview: a small key-shaped panel that reflects the current settings
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

            // Apply/Cancel buttons: placed below whichever column is taller
            int btnTop = Math.Max(margin + keyH, rightY) + gap;
            int bw     = (leftW + gap + rightW - gap) / 2;
            _btnCancel = MakeActionBtn(Lang.T("Cancel"), margin,        btnTop, bw, 44);
            _btnApply  = MakeActionBtn(Lang.T("Apply"),  margin+bw+gap, btnTop, bw, 44);
            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            // Adjust the form height to fit all controls with a bottom margin
            ClientSize = new Size(ClientSize.Width, btnTop + 44 + margin);

            SetupLayoutFocusTracking();
            PopulateFields(p);
        }

        // ══════════════════════════════════════════════════════════════
        // ── OPTION 3 BEGIN: mode selector and picker UI methods ───────
        // All methods and logic below this marker until OPTION 3 END
        // are exclusively for option 3. Delete them to fully remove it.
        // ─────────────────────────────────────────────────────────────

        // Guard flag: true while PopulateFields() is running.
        // Prevents the SelectedIndexChanged handler on _cmbModChoice from calling
        // ApplyModChoice() and overwriting fields before they are all populated.
        private bool _initialising = false;

        /// <summary>
        /// Adds a 2×2 grid of mode buttons plus a full-width "Layout" button
        /// to the Key Content panel. These buttons appear above the Send field
        /// and let the user choose what the key does.
        /// </summary>
        /// <param name="parent">The group panel to add the buttons to.</param>
        /// <param name="lx">Left edge x (label column start).</param>
        /// <param name="vx">Value column start x.</param>
        /// <param name="vw">Value column width.</param>
        /// <param name="gy">Current vertical position; incremented by the rows added.</param>
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

            // Wire each button to switch the editor into its corresponding mode
            _btnModeText.Click   += (s, e) => SetSendMode(SendMode.Text,           applyPicker: true);
            _btnModeKey.Click    += (s, e) => SetSendMode(SendMode.KeySequence,    applyPicker: true);
            _btnModeMod.Click    += (s, e) => SetSendMode(SendMode.Modifier,       applyPicker: true);
            _btnModeWP.Click     += (s, e) => SetSendMode(SendMode.WordPrediction, applyPicker: true);
            _btnModeLayout.Click += (s, e) => SetSendMode(SendMode.Layout,         applyPicker: true);

            gy += ROW_H * 3;  // three rows of buttons
        }

        /// <summary>
        /// Adds the contextual "picker" panels that appear just below the Send field.
        /// All three panels occupy the same vertical slot — only the relevant one is shown
        /// based on the active mode.
        /// </summary>
        /// <param name="parent">The group panel to add the pickers to.</param>
        /// <param name="lx">Left edge x.</param>
        /// <param name="vx">Value column start x.</param>
        /// <param name="vw">Value column width.</param>
        /// <param name="gy">Current vertical position; incremented by one row.</param>
        private void AddOption3PickerRow(Panel parent, int lx, int vx, int vw, ref int gy)
        {
            // pickerVx: x position of controls inside the picker panels.
            // The panels start at lx so the controls shift right by vx-lx to align with value fields.
            int pickerVx = vx - lx;

            // ── Key sequence recorder ─────────────────────────────────
            // Contains a "Record" button and a hint label. When the user clicks Record,
            // a low-level keyboard hook captures the next keystroke combination.
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
            // Contains a "Browse" button that opens a file dialog for selecting an XML layout.
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

                // Prefer a relative path when the file is inside the layout directory.
                // This makes the layout portable — moving the whole folder still works.
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
            // Contains a dropdown of available modifier key types.
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
            // Add the display name of each modifier; the internal label is looked up by index
            foreach (var (_, display) in _modifiers) _cmbModChoice.Items.Add(display);
            _cmbModChoice.SelectedIndex = 0;
            _cmbModChoice.SelectedIndexChanged += (s, e) => ApplyModChoice();
            _pnlModPicker.Controls.Add(_cmbModChoice);

            gy += ROW_H;
        }

        /// <summary>
        /// Sets the Shift send field text from code (not from user input).
        /// The <paramref name="isLayout"/> flag records whether the value is a layout file path
        /// so Apply() knows to re-add the "layout:" prefix before saving.
        /// The guard flag <c>_progShiftSend</c> suppresses the TextChanged handler that
        /// would otherwise clear <c>_shiftSendIsLayout</c> on a programmatic update.
        /// </summary>
        /// <param name="text">The text to display (without any "layout:" prefix).</param>
        /// <param name="isLayout">True if the text is a layout file path.</param>
        private void SetShiftSendText(string text, bool isLayout)
        {
            _progShiftSend    = true;
            _shiftSendIsLayout = isLayout;
            _txtShiftSend.Text = text;
            _progShiftSend    = false;
        }

        /// <summary>
        /// Sets the AltGr send field text from code (not from user input).
        /// Works the same way as <see cref="SetShiftSendText"/>.
        /// </summary>
        /// <param name="text">The text to display (without any "layout:" prefix).</param>
        /// <param name="isLayout">True if the text is a layout file path.</param>
        private void SetAltGrSendText(string text, bool isLayout)
        {
            _progAltGrSend    = true;
            _altGrSendIsLayout = isLayout;
            _txtAltGrSend.Text = text;
            _progAltGrSend    = false;
        }

        /// <summary>
        /// Wires the Enter and TextChanged events on the three send fields.
        /// Enter events update <see cref="_activeSendField"/> so the Browse button
        /// always fills the field the user most recently clicked.
        /// TextChanged events on Shift/AltGr clear the layout-flag when the user
        /// types manually, preventing Apply() from wrongly prepending "layout:" to
        /// a value that isn't a file path.
        /// </summary>
        private void SetupLayoutFocusTracking()
        {
            _activeSendField = _txtSend;  // default: primary Send field

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

        /// <summary>
        /// Updates the Browse button label to name the field it will fill
        /// (Send, Shift-send, or AltGr-send) based on which field is currently active.
        /// </summary>
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

        /// <summary>
        /// Creates a single mode-selector button with a neutral (unselected) style
        /// and adds it to <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The panel to add the button to.</param>
        /// <param name="text">The button label.</param>
        /// <param name="x">Left position within the panel.</param>
        /// <param name="y">Top position within the panel.</param>
        /// <param name="w">Button width.</param>
        /// <returns>The newly created button.</returns>
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

        /// <summary>
        /// Switches the editor into <paramref name="mode"/> and updates all related UI:
        /// highlights the active mode button, shows/hides the appropriate picker panel,
        /// updates the Send field label, and optionally resets the Send field contents
        /// to match the newly selected mode.
        /// </summary>
        /// <param name="mode">The mode to switch to.</param>
        /// <param name="applyPicker">
        ///   When true the Send field is cleared/pre-filled for the new mode
        ///   (used when the user explicitly clicks a mode button).
        ///   When false the field is left as-is (used during initial population).
        /// </param>
        private void SetSendMode(SendMode mode, bool applyPicker = false)
        {
            _sendMode = mode;

            // Highlight the active button by switching it to Primary style; others to Neutral.
            _btnModeText.Style   = mode == SendMode.Text           ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeKey.Style    = mode == SendMode.KeySequence    ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeMod.Style    = mode == SendMode.Modifier       ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeWP.Style     = mode == SendMode.WordPrediction ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _btnModeLayout.Style = mode == SendMode.Layout         ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;

            // Force a repaint on each button so the style change is visible immediately
            _btnModeText.Invalidate(); _btnModeKey.Invalidate(); _btnModeMod.Invalidate();
            _btnModeWP.Invalidate();   _btnModeLayout.Invalidate();

            bool isKey    = mode == SendMode.KeySequence;
            bool isMod    = mode == SendMode.Modifier;
            bool isWP     = mode == SendMode.WordPrediction;
            bool isLayout = mode == SendMode.Layout;

            // WP mode hides the text box and shows the slot spinner instead.
            // Modifier mode disables direct editing of the Send text box (handled by picker).
            _txtSend.Visible         = !isWP;
            _nudWPSlot.Visible       = false;   // always hidden — slot is auto-assigned
            _txtSend.Enabled         = !isMod;

            // Show exactly one picker panel depending on the mode
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
                // Auto-assign the next free slot so the user doesn't have to think about it.
                // Slots range from 0-9; if all are taken we leave it at 9.
                int next = 0;
                while (_usedWpSlots.Contains(next) && next < 9) next++;
                _nudWPSlot.Value = Math.Min(9, next);
            }

            // When the user actively clicks a mode button, pre-fill the Send field appropriately
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

        /// <summary>
        /// Begins a key-recording session. Installs a low-level keyboard hook so
        /// the next keystroke (including Win key combinations) is captured and
        /// written into the Send field rather than being sent to the operating system.
        /// </summary>
        private void StartRecording()
        {
            if (_recording) return;  // prevent double-starts
            _recording           = true;
            _winHeld             = false;
            _btnRecord.Text  = Lang.T("Press key now…");
            _btnRecord.Style = FluentButton.Variant.Danger;
            _btnRecord.Invalidate();
            _lblRecordHint.Text = Lang.T("Press Escape to cancel");
            _txtSend.Text        = "";

            // Install low-level keyboard hook so we capture Win key combinations
            // before Windows/shell acts on them, and suppress them from the OS.
            // Passing GetModuleHandle(null) and thread ID 0 makes this a global hook.
            _hookProc   = LowLevelHookCallback;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                              GetModuleHandle(null), 0);
        }

        /// <summary>
        /// Ends a key-recording session and restores the Record button to its normal state.
        /// Uninstalls the low-level keyboard hook so normal keyboard processing resumes.
        /// </summary>
        /// <param name="cancelled">
        ///   True when the user pressed Escape to cancel; false when a key was successfully recorded.
        /// </param>
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

        /// <summary>
        /// The low-level keyboard hook callback. Called by Windows for every keystroke
        /// while the hook is installed. Intercepts keys during a recording session and
        /// either suppresses them (preventing them from reaching the OS/other apps) or
        /// passes them through normally.
        /// </summary>
        /// <param name="nCode">
        ///   Hook code from Windows. Values less than 0 must be passed through without processing.
        /// </param>
        /// <param name="wParam">Identifies the keyboard event type (key-down, key-up, etc.).</param>
        /// <param name="lParam">Pointer to a <see cref="KBDLLHOOKSTRUCT"/> with key details.</param>
        /// <returns>
        ///   <c>(IntPtr)1</c> to suppress the key (not passed to any other app);
        ///   the result of <c>CallNextHookEx</c> to let it through normally.
        /// </returns>
        private IntPtr LowLevelHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // nCode < 0 means we must not process this event — just pass it along
            if (nCode < 0)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var kbd = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUp   = wParam == (IntPtr)WM_KEYUP   || wParam == (IntPtr)WM_SYSKEYUP;

            // Always let key-up events through — suppressing them confuses keyboard state
            // (e.g. the OS might think a key is still held down).
            if (isUp)
            {
                if (kbd.vkCode == VK_LWIN || kbd.vkCode == VK_RWIN)
                    _winHeld = false;  // clear the Win-held flag when the Win key is released
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (!isDown)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            // ── Key-down handling ─────────────────────────────────────

            // Escape cancels recording and lets the Escape key reach the application normally
            if (kbd.vkCode == VK_ESCAPE)
            {
                // BeginInvoke is used instead of a direct call because the hook callback
                // runs inside the message pump; calling UI methods directly here can cause
                // re-entrancy issues.
                this.BeginInvoke((Action)(() => StopRecording(cancelled: true)));
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // Win key: set the flag and suppress so the Start menu / shell doesn't react.
            // We track it manually because we suppress the key-down event here.
            if (kbd.vkCode == VK_LWIN || kbd.vkCode == VK_RWIN)
            {
                _winHeld = true;
                return (IntPtr)1; // suppress — do NOT call CallNextHookEx
            }

            // Bare modifier keys (Ctrl, Alt, Shift) on their own are not a complete shortcut.
            // Wait for the non-modifier "real" key before recording anything.
            if (kbd.vkCode == 0x10 || kbd.vkCode == 0xA0 || kbd.vkCode == 0xA1 || // Shift (generic + L/R)
                kbd.vkCode == 0x11 || kbd.vkCode == 0xA2 || kbd.vkCode == 0xA3 || // Ctrl (generic + L/R)
                kbd.vkCode == 0x12 || kbd.vkCode == 0xA4 || kbd.vkCode == 0xA5)   // Alt  (generic + L/R)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // ── A real (non-modifier) key was pressed — record it ─────
            // Read current modifier state from WinForms (more reliable inside the hook than raw VK checks)
            bool ctrl  = (System.Windows.Forms.Control.ModifierKeys & Keys.Control) != 0;
            bool alt   = (System.Windows.Forms.Control.ModifierKeys & Keys.Alt)     != 0;
            bool shift = (System.Windows.Forms.Control.ModifierKeys & Keys.Shift)   != 0;

            string send = BuildSendFromHook(kbd.vkCode, ctrl, alt, shift, _winHeld);

            // Update UI on the UI thread (BeginInvoke is non-blocking and safe from a hook)
            this.BeginInvoke((Action)(() =>
            {
                // Switch to the appropriate mode for the recorded combination
                var newMode = DetectSendMode(send, _txtLabel.Text);
                if (newMode != _sendMode)
                    SetSendMode(newMode, applyPicker: false);

                // Show the human-readable version in the text field (e.g. "{Ctrl}c" not "^c")
                _txtSend.Text = ToHuman(send);

                // If the label is still empty, auto-generate a readable label from the key
                if (string.IsNullOrWhiteSpace(_txtLabel.Text))
                    _txtLabel.Text = BuildHumanLabel(kbd.vkCode, ctrl, alt, shift, _winHeld);

                StopRecording(cancelled: false);
            }));

            // Suppress the key so it doesn't type into whatever app is behind the editor
            return (IntPtr)1;
        }

        /// <summary>
        /// Builds the internal send string from raw hook data.
        /// The internal format uses SendKeys notation: ^ for Ctrl, % for Alt, + for Shift.
        /// Win key combinations use the custom "win:" prefix instead.
        /// </summary>
        /// <param name="vk">Virtual key code of the pressed key.</param>
        /// <param name="ctrl">True if Ctrl was held.</param>
        /// <param name="alt">True if Alt was held.</param>
        /// <param name="shift">True if Shift was held.</param>
        /// <param name="win">True if a Win key was held.</param>
        /// <returns>Internal send string, e.g. "^c", "%{F4}", "win:d".</returns>
        private static string BuildSendFromHook(uint vk, bool ctrl, bool alt, bool shift, bool win)
        {
            string keyPart = VkCodeToSendKeys(vk, shift);

            if (win)
                return "win:" + keyPart;

            string prefix = "";
            if (ctrl)  prefix += "^";
            if (alt)   prefix += "%";
            // Only add the Shift prefix for non-printable keys — for letters and digits,
            // pressing Shift produces the uppercase/symbol character which VkCodeToSendKeys handles.
            if (shift && !IsPrintableVk(vk)) prefix += "+";

            return prefix + keyPart;
        }

        /// <summary>
        /// Builds a short, human-readable label string from raw hook data.
        /// Used to auto-populate the Label field when it is empty after recording.
        /// For example: Ctrl+C pressed → "Ctrl+c", Win+D → "Win+d".
        /// </summary>
        private static string BuildHumanLabel(uint vk, bool ctrl, bool alt, bool shift, bool win)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (win)   parts.Add("Win");
            if (ctrl)  parts.Add("Ctrl");
            if (alt)   parts.Add("Alt");
            if (shift && !IsPrintableVk(vk)) parts.Add("Shift");
            string key = VkCodeToSendKeys(vk, shift).TrimStart('{').TrimEnd('}');
            // Letter keys (A-Z, VK 0x41-0x5A) should display as uppercase in the label
            if (vk >= 0x41 && vk <= 0x5A) key = key.ToUpper();
            parts.Add(key);
            return string.Join("+", parts);
        }

        /// <summary>
        /// Returns true for letter (A-Z) and digit (0-9) virtual key codes.
        /// For these keys, the Shift modifier changes the character output rather than
        /// acting as a separate modifier prefix, so it should not be added as "+".
        /// </summary>
        private static bool IsPrintableVk(uint vk) =>
            (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39);

        /// <summary>
        /// Converts a Windows virtual key code to its SendKeys key string.
        /// Letters produce a lowercase character (e.g. 'a'); digits produce the digit character.
        /// Special keys produce a {NAME} token (e.g. "{ENTER}", "{F5}").
        /// Unknown keys fall back to a hex code in braces (e.g. "{B2}").
        /// </summary>
        /// <param name="vk">The virtual key code.</param>
        /// <param name="shift">Whether Shift is held (not used in this method but kept for signature consistency).</param>
        /// <returns>The SendKeys-compatible key string.</returns>
        private static string VkCodeToSendKeys(uint vk, bool shift)
        {
            if (vk >= 0x41 && vk <= 0x5A) // VK_A through VK_Z → lowercase letters
                return ((char)('a' + vk - 0x41)).ToString();
            if (vk >= 0x30 && vk <= 0x39) // VK_0 through VK_9 → digit characters
                return ((char)('0' + vk - 0x30)).ToString();
            if (vk >= 0x60 && vk <= 0x69) // VK_NUMPAD0 through VK_NUMPAD9
                return "{NUMPAD" + (vk - 0x60) + "}";
            if (vk >= 0x70 && vk <= 0x7B) // VK_F1 through VK_F12
                return "{F" + (vk - 0x70 + 1) + "}";
            // Named special keys
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
                0x20 => " ",             // Space produces a literal space, not a token
                0x14 => "{CAPSLOCK}",
                0x90 => "{NUMLOCK}",
                0x91 => "{SCROLLLOCK}",
                0x2C => "{PRTSC}",
                0x13 => "{BREAK}",
                _    => "{" + vk.ToString("X2") + "}",  // unknown → hex fallback
            };
        }

        /// <summary>
        /// Reads the currently selected modifier from <see cref="_cmbModChoice"/> and
        /// writes the corresponding Send and Label values to their text fields.
        /// Only runs when not initialising and when the current mode is Modifier.
        /// </summary>
        private void ApplyModChoice()
        {
            if (_initialising) return;
            if (_sendMode != SendMode.Modifier) return;
            int idx = _cmbModChoice.SelectedIndex;
            if (idx < 0 || idx >= _modifiers.Length) return;
            var (modLabel, _) = _modifiers[idx];
            // Modifier keys store the label name in braces as their Send value (e.g. "{Shift}"),
            // and the raw label name as the Label (e.g. "Shift"). The keyboard engine
            // recognises these special Send values to toggle modifier state.
            _txtSend.Text  = "{" + modLabel + "}";
            _txtLabel.Text = modLabel;
        }

        // ── Human-readable ↔ internal Send conversion ─────────────────
        // The internal format (stored in XML and used by SendKeysHelper) uses
        // compact modifier prefixes: ^ (Ctrl), % (Alt), + (Shift), and "win:" prefix.
        // The human-readable format expands these to named tokens for display in the editor:
        //   Internal:       ^c        %{F4}       win:d
        //   Human-readable: {Ctrl}c   {Alt}{F4}   {Win}d

        /// <summary>
        /// Converts an internal send string to a human-readable form for display in the editor.
        /// Modifier prefixes (^, %, +) and the "win:" prefix are replaced with named tokens.
        /// </summary>
        /// <param name="send">Internal send string (e.g. "^c", "%{F4}").</param>
        /// <returns>Human-readable string (e.g. "{Ctrl}c", "{Alt}{F4}").</returns>
        private static string ToHuman(string send)
        {
            if (string.IsNullOrEmpty(send)) return send;
            // Handle the win: prefix recursively so the rest of the string is also converted
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
                    // SendKeys grouping parentheses: copy the content without the parens
                    i++;
                    while (i < send.Length && send[i] != ')') { sb.Append(send[i]); i++; }
                    if (i < send.Length) i++;  // skip closing ')'
                }
                else { sb.Append(ch); i++; }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Converts a human-readable send string back to the internal format.
        /// Named modifier tokens ({Ctrl}, {Alt}, {Shift}, {Win}) are replaced with
        /// their compact prefix equivalents.
        /// </summary>
        /// <param name="human">Human-readable string (e.g. "{Ctrl}c", "{Win}{LEFT}").</param>
        /// <returns>Internal send string (e.g. "^c", "win:{LEFT}").</returns>
        private static string FromHuman(string human)
        {
            if (string.IsNullOrEmpty(human)) return human;
            // {Win} must be handled first because it becomes a prefix for the whole rest,
            // not a simple string replacement. Handle it recursively: {Win}m → win:m
            if (human.StartsWith("{Win}"))
                return "win:" + FromHuman(human.Substring(5));
            return human
                .Replace("{Ctrl}",  "^")
                .Replace("{Alt}",   "%")
                .Replace("{Shift}", "+");
        }

        /// <summary>
        /// Checks whether the currently selected word-prediction slot is already used
        /// by another key in the layout, and shows or hides the warning label accordingly.
        /// </summary>
        private void CheckWPDuplicate()
        {
            if (_lblWPDuplicate == null) return;
            int slot = (int)_nudWPSlot.Value;
            bool isDuplicate = _usedWpSlots.Contains(slot);
            _lblWPDuplicate.Text    = isDuplicate ? Lang.T("WP slot already in use") : "";
            _lblWPDuplicate.Visible = isDuplicate;
        }

        /// <summary>
        /// Determines the most appropriate <see cref="SendMode"/> for a given send string.
        /// Used both when loading an existing key and when processing a freshly recorded shortcut.
        /// </summary>
        /// <param name="send">The raw send value from the key properties (internal format).</param>
        /// <param name="label">The key's label, used to detect modifier keys that have an empty Send.</param>
        /// <returns>The <see cref="SendMode"/> that best describes the key.</returns>
        private SendMode DetectSendMode(string send, string label)
        {
            // A key with no Send value whose label matches a known modifier name is a modifier key
            if (string.IsNullOrEmpty(send) && _modifiers.Any(m => m.Label == label))
                return SendMode.Modifier;
            // Word-prediction keys store "wp:N" as their Send value
            if (!string.IsNullOrEmpty(send) && send.StartsWith("wp:", StringComparison.Ordinal))
                return SendMode.WordPrediction;
            // Layout-switch keys store "layout:filename.xml" as their Send value
            if (!string.IsNullOrEmpty(send) && send.StartsWith("layout:", StringComparison.Ordinal))
                return SendMode.Layout;
            // Any send string that contains SendKeys special characters is a key sequence
            if (!string.IsNullOrEmpty(send) && !SendKeysHelper.IsPlainText(send))
                return SendMode.KeySequence;
            return SendMode.Text;
        }
        // ── OPTION 3 END: mode selector and picker UI methods ─────────

        // ── Group panel helper ─────────────────────────────────────────

        /// <summary>
        /// Creates a card-style group panel with a colored header strip and adds it to the form.
        /// The panel's Paint event is wired to <see cref="FluentPainter.PaintCard"/> so the
        /// header text is drawn in the Fluent style. The panel is also registered in
        /// <see cref="_transGroups"/> so <see cref="OnLanguageChanged"/> can trigger a repaint
        /// when the language changes.
        /// </summary>
        /// <param name="getTitle">Lambda that returns the (possibly translated) panel title.</param>
        /// <param name="x">Left position on the form.</param>
        /// <param name="y">Top position on the form.</param>
        /// <param name="w">Panel width.</param>
        /// <param name="h">Panel height.</param>
        /// <param name="accentColor">Color of the header strip (different per section).</param>
        /// <returns>The new panel, already added to the form's Controls.</returns>
        private Panel AddGroup(Func<string> getTitle, int x, int y, int w, int h, Color accentColor)
        {
            var pnl = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = Fluent.BgPage };
            pnl.Paint += (s, e) =>
                FluentPainter.PaintCard(e.Graphics, pnl.Width, pnl.Height, getTitle(), accentColor, HDR_H);
            Controls.Add(pnl);
            _transGroups.Add((pnl, getTitle));
            return pnl;
        }

        // ── Color row helper ──────────────────────────────────────────

        /// <summary>
        /// Creates a color picker row consisting of a hex text box and a color swatch button.
        /// The swatch shows the current color and opens a <see cref="ColorDialog"/> when clicked.
        /// Typing a hex value in the text box updates the swatch color live.
        /// The swatch panel's Tag property stores a reference to the text box so
        /// <see cref="GetSwatchHex"/> and <see cref="SetSwatchHex"/> can reach it.
        /// </summary>
        /// <param name="parent">The parent panel to add the controls to.</param>
        /// <param name="x">Left position of the hex text box within the parent.</param>
        /// <param name="y">Top position.</param>
        /// <param name="totalW">Total width for both controls combined.</param>
        /// <returns>The swatch panel (use with GetSwatchHex/SetSwatchHex to read/write the color).</returns>
        private Panel AddColorRow(Panel parent, int x, int y, int totalW)
        {
            int sw = 32;  // swatch button width
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
            swatch.Tag = txtHex;  // store the textbox reference so helper methods can find it
            return swatch;
        }

        /// <summary>
        /// Returns the hex color string currently displayed in the text box that belongs to
        /// the given swatch panel. Returns an empty string if the swatch is not set up correctly.
        /// </summary>
        private string GetSwatchHex(Panel s) => (s.Tag is TextBox t) ? t.Text : "";

        /// <summary>
        /// Sets the hex color string in the text box that belongs to the given swatch panel,
        /// and updates the swatch's background color to match.
        /// </summary>
        private void SetSwatchHex(Panel s, string hex)
        {
            if (s.Tag is TextBox t) { t.Text = hex; s.BackColor = ParseColor(hex, s.BackColor); }
        }

        /// <summary>
        /// Creates a field label inside <paramref name="parent"/> and registers it in
        /// <see cref="_transLabels"/> so it is automatically re-translated on language change.
        /// </summary>
        /// <param name="parent">The group panel to add the label to.</param>
        /// <param name="getText">Lambda that returns the (possibly translated) label text.</param>
        /// <param name="x">Left position within the panel.</param>
        /// <param name="y">Top position of the row (the label is offset down by 4 px to align with input controls).</param>
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

        /// <summary>
        /// Creates a small hint label (smaller font, muted color) inside <paramref name="parent"/>.
        /// Used for explanatory text beneath fields. Not registered for translation updates,
        /// so only use for static or already-translated strings.
        /// </summary>
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

        /// <summary>
        /// Creates a styled single-line text input box and adds it to <paramref name="parent"/>.
        /// </summary>
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

        /// <summary>
        /// Creates a styled action button (using the Fluent button style) and adds it to the form.
        /// Returns a <see cref="Button"/> base reference; the actual type is <see cref="FluentButton"/>.
        /// </summary>
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

        /// <summary>
        /// Fills all form controls with the values from <paramref name="p"/>.
        /// Called once during construction after <see cref="BuildUI"/> creates the controls.
        /// Also handles mode detection so the correct mode button is highlighted and
        /// the correct picker panel is shown for the existing key type.
        /// </summary>
        /// <param name="p">The key properties to display.</param>
        private void PopulateFields(KeyProps p)
        {
            _txtLabel.Text      = p.Label      ?? "";
            _txtSend.Text       = p.Send       ?? "";

            // If the key already has a word-prediction Send value ("wp:N"), parse out
            // the slot number and pre-set the NUD. Otherwise default to slot 0.
            if (p.Send != null && p.Send.StartsWith("wp:") &&
                int.TryParse(p.Send.Substring(3), out int wpSlot))
                _nudWPSlot.Value = Math.Clamp(wpSlot, 0, 9);
            else
                _nudWPSlot.Value = 0;

            _txtShiftLabel.Text = p.ShiftLabel ?? "";
            _txtAltGrLabel.Text = p.AltGrLabel ?? "";

            // Strip "layout:" prefix from Shift/AltGr send values before showing them.
            // The flags track whether the prefix was stripped so Apply() can re-add it.
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

            // Resolve global appearance settings once (Owner is null at this point —
            // the form hasn't been shown via ShowDialog yet — so we use _ownerGlobal
            // that was cached in the constructor).
            var ownerG = _ownerGlobal;
            Color gFontColor   = ownerG?.FontColor   ?? ColorTranslator.FromHtml("#E0E0FF");
            Color gKeyColor    = ownerG?.KeyColor    ?? ColorTranslator.FromHtml("#2D2D4A");

            // If the key has no custom font, fall back to the global font
            string resolvedFont = string.IsNullOrEmpty(p.FontName)
                ? (ownerG?.FontName ?? "Arial") : p.FontName;
            int fi = _cmbFont.Items.IndexOf(resolvedFont);
            _cmbFont.SelectedIndex = fi >= 0 ? fi : (_cmbFont.Items.Count > 0 ? 0 : -1);

            // FontSize = 0 means "auto-size"; any positive value is an explicit size
            int clampedSize = Math.Clamp(p.FontSize, 0, (int)_nudFontSize.Maximum);
            if (clampedSize > 0)
            { _nudFontSize.Value = clampedSize; _chkAutoSize.Checked = false; _nudFontSize.Enabled = true; }
            else
            { _nudFontSize.Value = 0; _chkAutoSize.Checked = true; _nudFontSize.Enabled = false; }

            // For empty spacer keys (no label, no send) always show the current global values.
            // Spacer keys often have per-key styles set to match the background, but we want
            // the editor to reflect what they would look like with the current global settings.
            bool isEmptyKey = string.IsNullOrEmpty(p.Label) && string.IsNullOrEmpty(p.Send);

            // BorderThickness = -1 is the sentinel for "inherit from global"
            int globalBt = ownerG?.BorderThickness ?? 1;
            _nudBorderThickness.Value = Math.Clamp(
                (isEmptyKey || p.BorderThickness == -1) ? globalBt : p.BorderThickness,
                -1, (int)_nudBorderThickness.Maximum);

            // For each color: if the key has no per-key override (or is a spacer), show the global value
            SetSwatchHex(_pnlFontColor,   SettingsManager.Hex(
                (isEmptyKey || p.FontColor.IsEmpty)   ? gFontColor         : p.FontColor));
            SetSwatchHex(_pnlKeyColor,    SettingsManager.Hex(
                (isEmptyKey || p.KeyColor.IsEmpty)    ? gKeyColor          : p.KeyColor));
            SetSwatchHex(_pnlBorderColor, SettingsManager.Hex(
                (isEmptyKey || p.BorderColor.IsEmpty) ? _globalBorderColor : p.BorderColor));

            // Group selector: index 0 = no group. Find the group by name if one is set.
            if (_cmbGroup != null)
            {
                int gi = string.IsNullOrEmpty(p.GroupName)
                    ? 0
                    : _cmbGroup.Items.IndexOf(p.GroupName);
                _cmbGroup.SelectedIndex = gi >= 0 ? gi : 0;
            }

            // ── OPTION 3 BEGIN: detect and set initial send mode ──────
            // _initialising suppresses ApplyModChoice() while combo boxes are being populated.
            // Without it, the SelectedIndex assignment below fires the SelectedIndexChanged event
            // and ApplyModChoice() overwrites the Label and Send fields mid-initialisation.
            _initialising = true;
            var detectedMode = DetectSendMode(p.Send ?? "", p.Label ?? "");
            SetSendMode(detectedMode, applyPicker: false);
            if (detectedMode == SendMode.WordPrediction) CheckWPDuplicate();

            // Sync picker controls to match the actual key data
            if (detectedMode == SendMode.Modifier)
            {
                // Show the modifier label in the Send field in the {Label} format
                _txtSend.Text = "{" + (p.Label ?? "") + "}";
                // Select the matching modifier in the dropdown
                int mi = Array.FindIndex(_modifiers, m => m.Label == (p.Label ?? ""));
                if (mi >= 0) _cmbModChoice.SelectedIndex = mi;
            }
            else if (detectedMode == SendMode.KeySequence)
            {
                // Show the key sequence in human-readable form for easier editing
                _txtSend.Text = ToHuman(p.Send ?? "");
                _lblRecordHint.Text = Lang.T("Press Record to re-record, or edit directly");
            }
            else if (detectedMode == SendMode.Layout)
            {
                // Strip the "layout:" prefix — the active mode button makes the type obvious
                string raw = p.Send ?? "";
                _txtSend.Text = raw.StartsWith("layout:", StringComparison.Ordinal)
                    ? raw.Substring(7) : raw;
            }
            _initialising = false;
            // ── OPTION 3 END ──────────────────────────────────────────

            Refresh2();  // update the live preview to match the loaded values
        }

        // ── Live preview ──────────────────────────────────────────────

        /// <summary>
        /// Updates the live key preview panel to reflect the current control values.
        /// Called after every field change so the user always sees an up-to-date preview.
        /// </summary>
        private void Refresh2()
        {
            // Fall back to hard-coded defaults if no owner theme is available
            var ownerGlob = _ownerGlobal;
            Color gFc = ownerGlob?.FontColor   ?? ColorTranslator.FromHtml("#E0E0FF");
            Color gKc = ownerGlob?.KeyColor    ?? ColorTranslator.FromHtml("#2D2D4A");
            Color gBc = ownerGlob?.BorderColor ?? ColorTranslator.FromHtml("#3C3C5A");

            // Parse the current hex values, falling back to global colors on parse failure
            Color fc = ParseColor(GetSwatchHex(_pnlFontColor),   gFc);
            Color kc = ParseColor(GetSwatchHex(_pnlKeyColor),    gKc);
            Color bc = ParseColor(GetSwatchHex(_pnlBorderColor), gBc);
            string fn = _cmbFont.SelectedItem?.ToString() ?? ownerGlob?.FontName ?? "Arial";
            // Use a fixed preview size of 13 when auto-sizing is active (size isn't known at edit time)
            int    fs = (_chkAutoSize.Checked || _nudFontSize.Value == 0) ? 13 : (int)_nudFontSize.Value;
            int    btRaw = (int)_nudBorderThickness.Value;
            // -1 = use global (retrieve from owner); 0 = no border; n = explicit thickness
            int    bt = btRaw == -1
                ? (ownerGlob?.BorderThickness ?? 1)
                : btRaw;

            // "&&" escapes the ampersand so WinForms doesn't treat it as an accelerator key prefix
            _lblPreviewKey.Text      = (_txtLabel?.Text ?? "").Replace("&", "&&");
            _lblPreviewKey.ForeColor = fc;
            _lblPreviewKey.BackColor = kc;
            _pnlPreview.BackColor    = bc;
            _pnlPreview.Padding      = new Padding(Math.Max(0, bt));  // border simulated as padding
            try
            {
                var newFont = new Font(fn, fs, FontStyle.Bold);
                // Dispose the old font only if it is a dynamically created instance,
                // not one of the shared static fonts (which must not be disposed).
                var oldFont = _lblPreviewKey.Font;
                _lblPreviewKey.Font = newFont;
                if (oldFont != null && oldFont != F_LABEL && oldFont != F_INPUT &&
                    oldFont != F_HEADER && oldFont != F_BTN && oldFont != F_HINT)
                    oldFont.Dispose();
            }
            catch { }  // ignore invalid font names — the preview just keeps its current font
        }

        // ── Apply ─────────────────────────────────────────────────────

        /// <summary>
        /// Reads all form controls, builds a new <see cref="KeyProps"/> object, stores it in
        /// <see cref="Result"/>, updates <see cref="ResultColSpan"/> and <see cref="ResultRowSpan"/>,
        /// then closes the dialog with <see cref="DialogResult.OK"/>.
        /// </summary>
        private void Apply()
        {
            string label = _txtLabel.Text.Trim();

            // Use the cached owner theme (consistent with PopulateFields and Refresh2)
            var ownerGl = _ownerGlobal;

            // For each color: store Color.Empty when the chosen color matches the global value.
            // Color.Empty is the sentinel meaning "inherit from global" — this way, changing
            // the global color later will cascade to this key automatically.
            string fcHex = GetSwatchHex(_pnlFontColor).Trim();
            string kcHex = GetSwatchHex(_pnlKeyColor).Trim();
            string bcStr = GetSwatchHex(_pnlBorderColor).Trim();
            Color fc = HexMatchesGlobal(fcHex, ownerGl?.FontColor)
                ? Color.Empty : ParseColor(fcHex, Color.Empty);
            Color kc = HexMatchesGlobal(kcHex, ownerGl?.KeyColor)
                ? Color.Empty : ParseColor(kcHex, Color.Empty);
            Color bc = HexMatchesGlobal(bcStr, ownerGl?.BorderColor) || bcStr == ""
                ? Color.Empty : ParseColor(bcStr, Color.Empty);

            // ── OPTION 3 BEGIN: convert human-readable Send back to internal format ──
            // Each mode produces a different internal Send string format.
            string send;
            if (_sendMode == SendMode.Modifier)
            {
                // Modifier keys always have an empty Send value.
                // The keyboard engine detects modifier keys by their label alone.
                send = "";
            }
            else if (_sendMode == SendMode.WordPrediction)
            {
                // Word-prediction slot stored as "wp:N" (N = slot index 0-9)
                send = "wp:" + (int)_nudWPSlot.Value;
            }
            else if (_sendMode == SendMode.KeySequence)
            {
                // Convert the human-readable display form back to internal SendKeys syntax
                send = FromHuman(_txtSend.Text);
                // Do NOT escape with EscapeForSend — key sequences already use correct syntax
            }
            else if (_sendMode == SendMode.Layout)
            {
                // Prepend "layout:" to the path (which is stored without the prefix in the text field)
                string path = _txtSend.Text.Trim();
                send = string.IsNullOrEmpty(path) ? "" : "layout:" + path;
            }
            else
            {
                // Plain text mode: escape any characters that have special meaning in SendKeys
                send = SendKeysHelper.EscapeForSend(_txtSend.Text);
                // If both label and send would be empty after escaping, use the label as the send value
                if (string.IsNullOrEmpty(send)) send = label;
            }
            // ── OPTION 3 END ──────────────────────────────────────────

            // If a send value is set but the label is still empty, mirror the send as the label
            // so the key has something visible on screen.
            if (string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(send)) label = send;

            ResultColSpan = (int)_nudColSpan.Value;
            ResultRowSpan = (int)_nudRowSpan.Value;

            string fontName = _cmbFont.SelectedItem?.ToString() ?? "";
            // Store "" (meaning "use global") when the chosen font matches the global font
            if (fontName == (ownerGl?.FontName ?? "Arial")) fontName = "";

            // 0 = auto-size (store when the checkbox is checked or size is 0)
            int fontSize = (_chkAutoSize.Checked || _nudFontSize.Value == 0)
                ? 0 : (int)_nudFontSize.Value;

            // Index 0 in _cmbGroup = "(no group)" → store empty string
            string groupName = (_cmbGroup != null && _cmbGroup.SelectedIndex > 0)
                ? _cmbGroup.SelectedItem?.ToString() ?? ""
                : "";

            // Re-add "layout:" prefix to Shift/AltGr sends when the field was displaying a stripped path
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
                // Store -1 (inherit from global) when the value equals the global setting,
                // mirroring the same "inherit sentinel" logic used for colors above.
                BorderThickness = ((int)_nudBorderThickness.Value == -1 ||
                                   (int)_nudBorderThickness.Value == (ownerGl?.BorderThickness ?? 1))
                                  ? -1 : (int)_nudBorderThickness.Value,
                GroupName       = groupName,
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// Parses a hex color string into a <see cref="Color"/>.
        /// Delegates to <see cref="SettingsManager.ParseColor"/> which handles the "#RRGGBB" format.
        /// Returns <paramref name="fallback"/> if the string is invalid.
        /// </summary>
        private static Color ParseColor(string hex, Color fallback) => SettingsManager.ParseColor(hex, fallback);

        /// <summary>
        /// Returns true when the hex color string represents the same RGB color as the global value.
        /// Used in <see cref="Apply"/> to decide whether to store Color.Empty (inherit) or an explicit color.
        /// </summary>
        /// <param name="hex">Hex color string from the editor field.</param>
        /// <param name="globalColor">The current global color to compare against.</param>
        private static bool HexMatchesGlobal(string hex, Color? globalColor)
        {
            if (globalColor == null || string.IsNullOrEmpty(hex)) return false;
            Color parsed = SettingsManager.ParseColor(hex, Color.Empty);
            // Compare individual R/G/B channels (ignore alpha, which is always 255 for key colors)
            return !parsed.IsEmpty &&
                   parsed.R == globalColor.Value.R &&
                   parsed.G == globalColor.Value.G &&
                   parsed.B == globalColor.Value.B;
        }

        /// <summary>
        /// Returns an alphabetically sorted list of all font family names installed on the system.
        /// This is used to populate the font combo box in the Appearance section.
        /// </summary>
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

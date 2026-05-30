using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A modal dialog that lets the user edit all visual and behavioural
    /// settings for the on-screen keyboard in one place.
    ///
    /// When the user clicks "Apply", the dialog closes with
    /// <see cref="DialogResult.OK"/> and the caller can read back the
    /// updated values through <see cref="ResultTheme"/>,
    /// <see cref="ResultWindow"/>, <see cref="ResultMeta"/>,
    /// and <see cref="ResultGroups"/>.
    ///
    /// If the user clicks "Cancel" the dialog closes with
    /// <see cref="DialogResult.Cancel"/> and the original values are left
    /// unchanged.
    /// </summary>
    public class KeyboardEditorForm : FluentDialogBase
    {
        // ── Public results (read by the caller after DialogResult.OK) ────

        /// <summary>The edited visual theme (colors, font, border…).</summary>
        public VisualTheme   ResultTheme  { get; private set; }

        /// <summary>The edited window settings (always-on-top, opacity, size…).</summary>
        public WindowState   ResultWindow { get; private set; }

        /// <summary>The edited layout metadata (language, sticky modifiers…).</summary>
        public LayoutMeta    ResultMeta   { get; private set; }

        /// <summary>
        /// The (possibly edited) list of key groups.  Groups are always
        /// cloned on entry so the original list is never mutated directly.
        /// </summary>
        public List<KeyGroup> ResultGroups { get; private set; }

        // ── Snapshot of the original settings (updated after Load) ──────

        /// <summary>The theme as it was when the editor was opened (or last loaded) — passthrough fields in <see cref="Apply"/> are copied from here.</summary>
        private VisualTheme _srcTheme;
        /// <summary>The window state as it was when the editor was opened (or last loaded).</summary>
        private WindowState _srcWindow;
        /// <summary>The layout metadata as it was when the editor was opened (or last loaded).</summary>
        private LayoutMeta  _srcMeta;

        /// <summary>
        /// Working copy of the key-group list.  Never changed inside this dialog (group
        /// management moved to <see cref="KeyEditorForm"/>), but refreshed after a "Load"
        /// operation so <see cref="ResultGroups"/> reflects the newly-loaded layout.
        /// </summary>
        private List<KeyGroup> _groups;

        // ── UI controls ─────────────────────────────────────────────────

        // Language section
        private ComboBox      _cmbLanguage;

        // Window section
        private TrackBar      _trkOpacity;
        private Button        _pnlBgColor;

        // Window/accessibility checkboxes
        private CheckBox        _chkAlwaysOnTop;
        private CheckBox        _chkStickyMods;
        private CheckBox        _chkHoldToEdit;
        private CheckBox        _chkHideTitlebar;
        private ComboBox        _cmbToolbarTheme;

        // Accessibility — slow keys and dwell click (checkbox = enabled, NUD = duration)
        private CheckBox        _chkSlowKeys;
        private NumericUpDown   _nudSlowKeys;
        private CheckBox        _chkDwell;
        private NumericUpDown   _nudDwell;
        private CheckBox        _chkTimingAnimation;

        // File action delegates — called when Save/SaveAs/Load buttons are clicked
        private readonly Action _onSave;
        private readonly Action _onSaveAs;
        private readonly Action _onLoad;
        /// <summary>
        /// Retrieves the current key-group list from the main form after a
        /// "Load" operation so the editor can refresh its working copy.
        /// </summary>
        private readonly Func<List<KeyGroup>> _getGroups;

        /// <summary>
        /// Retrieves the freshly-loaded theme, window state, and layout metadata
        /// from the main form after a "Load" operation.
        /// </summary>
        private readonly Func<(VisualTheme, WindowState, LayoutMeta)> _getSettings;

        // File buttons — kept as fields so OnLanguageChanged() can update their text
        private Button _btnSaveFile, _btnSaveAsFile, _btnLoadFile;

        // _dark is inherited from FluentDialogBase.

        // Confirm / dismiss
        private Button _btnApply, _btnCancel;

        // ── Translation / tooltip / accessibility helpers ────────────────
        // _transLabels, _transGroups, _transTooltips, _tip, _err, _onPrefChanged,
        // _pendingAccessibleName — all inherited from FluentDialogBase.

        // ── Fluent / WinUI-3 colour and font shorthands ─────────────────
        // These are static properties so they always return the current
        // theme value even if the global theme is swapped at runtime.

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

        // Layout constants (pixels)
        private const int HDR_H = 42;   // height of a card's coloured header bar
        private const int ROW_H = 50;   // vertical space allocated for each setting row
        private const int PAD   = 20;   // inner horizontal padding inside a card

        // ════════════════════════════════════════════════════════════════
        // Constructor
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates the editor dialog and immediately builds all UI controls.
        /// </summary>
        /// <param name="theme">
        ///   The current visual theme.  A deep clone is made so the original
        ///   is never changed until the user confirms with Apply.
        /// </param>
        /// <param name="window">
        ///   The current window state (size, always-on-top flag, …).
        /// </param>
        /// <param name="meta">
        ///   The current layout metadata (language, sticky modifiers, …).
        /// </param>
        /// <param name="owner">
        ///   The parent form.  Used by <see cref="StartPosition"/> to centre
        ///   the dialog on screen (currently unused directly but kept for
        ///   future use).
        /// </param>
        /// <param name="onSave">
        ///   Callback invoked when the user clicks "Save".  Apply() is called
        ///   first so ResultTheme is up to date before the file is written.
        /// </param>
        /// <param name="onSaveAs">Callback for "Save As…".</param>
        /// <param name="onLoad">
        ///   Callback for "Load…".  After the callback returns the editor
        ///   refreshes its controls from the reloaded values.
        /// </param>
        /// <param name="groups">
        ///   Initial key-group list.  Each group is deep-cloned so edits
        ///   inside the sub-dialog do not affect the caller's list until OK.
        /// </param>
        /// <param name="getGroups">
        ///   Factory that returns the caller's current group list after a
        ///   successful Load operation so the editor can sync its copy.
        /// </param>
        public KeyboardEditorForm(VisualTheme theme, WindowState window, LayoutMeta meta,
                                  Form owner,
                                  Action onSave = null, Action onSaveAs = null, Action onLoad = null,
                                  List<KeyGroup> groups = null, Func<List<KeyGroup>> getGroups = null,
                                  Func<(VisualTheme, WindowState, LayoutMeta)> getSettings = null)
            : base(new Size(940, 560))
        {
            _srcTheme  = theme;
            _srcWindow = window;
            _srcMeta   = meta;

            ResultTheme  = theme.Clone();
            ResultWindow = window.Clone();
            ResultMeta   = meta.Clone();

            _groups      = groups?.Select(g => g.Clone()).ToList() ?? new List<KeyGroup>();
            ResultGroups = _groups;

            _onSave      = onSave;
            _onSaveAs    = onSaveAs;
            _onLoad      = onLoad;
            _getGroups   = getGroups;
            _getSettings = getSettings;

            Text = Lang.T("Edit Keyboard");

            BuildUI();
            PopulateFields(theme, window, meta);
            ActiveControl = _cmbLanguage;
            // Base FormClosed handles Lang.LanguageChanged, UserPreferenceChanged, and _err.
        }

        // ════════════════════════════════════════════════════════════════
        // Language-change handler
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Refreshes all translatable strings on the form when the language changes.
        /// Calls <see cref="FluentDialogBase.OnLanguageChanged"/> first (handles labels,
        /// group-panel headers, tooltips), then updates form-specific controls.
        /// </summary>
        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            Text                  = Lang.T("Edit Keyboard");
            _btnApply.Text        = Lang.T("Apply");
            _btnCancel.Text       = Lang.T("Cancel");
            _chkAlwaysOnTop.Text  = "&" + Lang.T("Always on top");
            _chkStickyMods.Text   = Lang.T("Sticky modifiers");
            _chkHoldToEdit.Text   = Lang.T("Hold to edit");
            _chkHideTitlebar.Text = Lang.T("Hide title bar");
            _chkSlowKeys.Text           = Lang.T("Slow keys");
            _chkDwell.Text              = Lang.T("Dwell click");
            _chkTimingAnimation.Text    = Lang.T("Show timing animation");
            _nudSlowKeys.AccessibleName = Lang.StripMnemonic(Lang.T("Slow keys"));
            _nudDwell.AccessibleName    = Lang.StripMnemonic(Lang.T("Dwell click"));
            _btnSaveFile.Text     = "&" + Lang.T("Save");
            _btnSaveAsFile.Text   = Lang.T("Save As…");
            _btnLoadFile.Text     = "&" + Lang.T("Load…");
        }

        // ════════════════════════════════════════════════════════════════
        // UI construction
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates and positions every control on the form.
        ///
        /// The layout is a two-column grid:
        /// <list type="bullet">
        ///   <item><description>
        ///     Left column — Language, Window settings
        ///   </description></item>
        ///   <item><description>
        ///     Right column — Layout File, Accessibility
        ///   </description></item>
        /// </list>
        ///
        /// Group management was moved to <see cref="KeyEditorForm"/> since groups are a
        /// per-key concern, not a keyboard-level one.
        ///
        /// At the bottom, Apply and Cancel buttons span the full width.
        /// The form's height is adjusted at the end to fit all content.
        /// </summary>
        private void BuildUI()
        {
            int margin = 16;    // gap between the form edge and the card columns
            int gap    = 14;    // gap between the two columns and between stacked cards

            // Divide the client area into two equal columns.
            int colW   = (ClientSize.Width - margin * 2 - gap) / 2;
            int leftW  = colW;
            int rightW = colW;
            int leftX  = margin;
            int rightX = margin + colW + gap;

            // ── LEFT COLUMN ───────────────────────────────────────────────

            int leftY = margin;  // tracks the next free vertical position in the left column

            // ── Language card ─────────────────────────────────────────────
            // Height: header bar + top padding + one combo-box row + bottom padding
            int langH = HDR_H + PAD + ROW_H + PAD - 4;
            var grpLang = AddGroup(() => Lang.T("Language"), leftX, leftY, colW, langH,
                                   Color.FromArgb(52, 73, 94));
            grpLang.TabIndex = 0;
            leftY += langH + gap;

            // Populate the language combo from all .json translation files that
            // were found at startup.
            var langs = Lang.GetAvailable();
            _cmbLanguage = new ComboBox
            {
                Left = PAD, Top = HDR_H + PAD, Width = colW - PAD * 2,
                DropDownStyle = ComboBoxStyle.DropDownList,   // no free-text entry
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
                TabIndex = 0,
                AccessibleName = Lang.StripMnemonic(Lang.T("Language")),
            };
            foreach (var (code, name) in langs)
                _cmbLanguage.Items.Add(new LangItem(code, name));

            // Pre-select the language that is currently active.
            for (int i = 0; i < _cmbLanguage.Items.Count; i++)
                if (((LangItem)_cmbLanguage.Items[i]).Code == Lang.CurrentCode)
                { _cmbLanguage.SelectedIndex = i; break; }

            // Fall back to the first item if the current language was not found
            // (e.g. after a language file was deleted).
            if (_cmbLanguage.SelectedIndex < 0 && _cmbLanguage.Items.Count > 0)
                _cmbLanguage.SelectedIndex = 0;

            // Switching the combo immediately applies the language — the rest of
            // the UI responds via the LanguageChanged event.
            _cmbLanguage.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbLanguage.SelectedItem is LangItem li) Lang.Load(li.Code);
            };
            grpLang.Controls.Add(_cmbLanguage);

            // ── Window card ───────────────────────────────────────────────
            // Contains: opacity slider, background colour, always-on-top,
            // hide-titlebar.  Heights are summed manually to fit everything.
            int wndH = HDR_H + PAD + 52       + ROW_H + ROW_H + ROW_H + ROW_H + PAD + 6;
            var grpWnd = AddGroup(() => Lang.T("Window"), leftX, leftY, colW, wndH,
                                  Color.FromArgb(41, 128, 185));
            grpWnd.TabIndex = 1;
            leftY += wndH + gap;

            // lx = label x, vx = value-control x, vw = value-control width
            int lx = PAD, vx = 195, vw = colW - lx - vx - PAD;
            int gy = HDR_H + PAD;  // running y position inside this card

            // ti = TabIndex counter within grpWnd; label.TabIndex = buddy.TabIndex − 1.
            int ti = 0;

            // Opacity trackbar — value 0 means fully opaque, value 80 means
            // 20 % opacity (the minimum we allow so the keyboard is still usable).
            AddFieldLabel(grpWnd, () => "&" + Lang.T("Opacity"), lx, gy).TabIndex = ti++;
            _trkOpacity = new TrackBar
            {
                Left = vx, Top = gy, Width = vw, Height = 45,
                Minimum = 0, Maximum = 80, TickFrequency = 10,
                SmallChange = 5, LargeChange = 10,
                TabIndex = ti++,
                AccessibleName = Lang.StripMnemonic(Lang.T("Opacity")),
            };
            SetTip(_trkOpacity, () => Lang.T("tip: Opacity"));
            _trkOpacity.ValueChanged += (s, e) => { };  // reserved for future live preview
            grpWnd.Controls.Add(_trkOpacity);
            gy += 52;  // trackbar is taller than a normal row

            // Background colour picker
            AddFieldLabel(grpWnd, () => "&" + Lang.T("Background"), lx, gy).TabIndex = ti++;
            _pnlBgColor = AddColorRow(grpWnd, vx, gy, vw, ref ti); gy += ROW_H;

            // "Always on top" keeps the keyboard window above all other windows.
            _chkAlwaysOnTop = new CheckBox
            {
                Text = "&" + Lang.T("Always on top"),
                Left = lx, Top = gy + 8, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
                Font = F_LABEL, TabIndex = ti++,
            };
            grpWnd.Controls.Add(_chkAlwaysOnTop); gy += ROW_H;

            // "Hide title bar" removes the window chrome so only the keys show.
            _chkHideTitlebar = new CheckBox
            {
                Text = Lang.T("Hide title bar"),
                Left = lx, Top = gy + 8, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
                Font = F_LABEL, TabIndex = ti++,
            };
            grpWnd.Controls.Add(_chkHideTitlebar); gy += ROW_H;

            // Toolbar theme selector
            AddFieldLabel(grpWnd, () => Lang.T("Toolbar theme"), lx, gy).TabIndex = ti++;
            _cmbToolbarTheme = new ComboBox
            {
                Left = vx, Top = gy + 2, Width = vw,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = F_LABEL, TabIndex = ti++,
                AccessibleName = Lang.StripMnemonic(Lang.T("Toolbar theme")),
            };
            _cmbToolbarTheme.Items.AddRange(new object[]
            {
                Lang.T("Dark"),
                Lang.T("Light"),
                Lang.T("System default"),
            });
            _cmbToolbarTheme.SelectedIndex = (int)ResultMeta.ToolbarTheme;
            grpWnd.Controls.Add(_cmbToolbarTheme); gy += ROW_H;

            // ── RIGHT COLUMN ──────────────────────────────────────────────

            int rightY = margin;  // tracks the next free vertical position in the right column

            // ── Layout file card ──────────────────────────────────────────
            // Three equal-width buttons: Save, Save As, Load.
            int fileH = HDR_H + PAD + ROW_H + PAD;
            var grpFile = AddGroup(() => Lang.T("Layout file"), rightX, rightY, rightW, fileH,
                                   Color.FromArgb(39, 174, 96));
            grpFile.TabIndex = 2;
            rightY += fileH + gap;

            // Calculate button width so three buttons + two gaps fill the card.
            int fbw = (rightW - PAD * 2 - gap * 2) / 3;
            // Alt+S (Save), Alt+V (Save As), Alt+L (Load) — mnemonics embedded via Lang.T().
            _btnSaveFile   = MakeFileBtn("&" + Lang.T("Save"),       grpFile, PAD,                    HDR_H + PAD, fbw); _btnSaveFile.TabIndex   = 0;
            _btnSaveAsFile = MakeFileBtn(Lang.T("Save As…"),         grpFile, PAD + fbw + gap,        HDR_H + PAD, fbw); _btnSaveAsFile.TabIndex = 1;
            _btnLoadFile   = MakeFileBtn("&" + Lang.T("Load…"),     grpFile, PAD + fbw * 2 + gap * 2, HDR_H + PAD, fbw); _btnLoadFile.TabIndex   = 2;

            // Save / Save As: first commit the current UI state to ResultTheme etc.,
            // then hand off to the caller's file-writing callback.
            _btnSaveFile.Click   += (s, e) => { Apply(); _onSave?.Invoke(); };
            _btnSaveAsFile.Click += (s, e) => { Apply(); _onSaveAs?.Invoke(); };

            // Load: let the caller read a file, then re-sync our controls to
            // whatever the caller has loaded.
            _btnLoadFile.Click += (s, e) =>
            {
                _onLoad?.Invoke();
                // Fetch the freshly loaded theme/window/meta so PopulateFields
                // shows the new file's values, not the pre-open snapshots.
                if (_getSettings != null)
                {
                    var (t, ws, m) = _getSettings();
                    _srcTheme  = t;
                    _srcWindow = ws;
                    _srcMeta   = m;
                }
                if (_getGroups != null)
                    _groups = _getGroups().Select(g => g.Clone()).ToList();
                PopulateFields(_srcTheme, _srcWindow, _srcMeta);
            };

            // ── Accessibility card ─────────────────────────────────────────
            int accH = HDR_H + PAD + ROW_H * 5 + PAD;
            var grpAcc = AddGroup(() => Lang.T("Accessibility"), rightX, rightY, rightW, accH,
                                  Color.FromArgb(155, 89, 182));
            grpAcc.TabIndex = 3;
            rightY += accH + gap;

            // Sticky modifiers: a modifier key (Shift, Ctrl, Alt) stays active
            // after being pressed once, so the user does not need to hold it.
            _chkStickyMods = new CheckBox
            {
                Text = Lang.T("Sticky modifiers"),
                Left = PAD, Top = HDR_H + PAD + 8, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = F_LABEL,
                TabIndex = 0,
            };
            grpAcc.Controls.Add(_chkStickyMods);

            // Hold to edit: the user must hold a key for a moment to open its
            // properties, preventing accidental edits while typing.
            _chkHoldToEdit = new CheckBox
            {
                Text = Lang.T("Hold to edit"),
                Left = PAD, Top = HDR_H + PAD + ROW_H + 8, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = F_LABEL,
                TabIndex = 1,
            };
            grpAcc.Controls.Add(_chkHoldToEdit);

            // Slow keys: key must be held for N ms before it registers.
            // Dwell click: hovering over a key for N ms auto-fires it.
            // The two are mutually exclusive; setting one > 0 clears the other.
            int nudW = 75;
            int nudX = rightW - PAD - nudW;
            int slowY = HDR_H + PAD + ROW_H * 2;
            int dwellY = HDR_H + PAD + ROW_H * 3;

            // Slow keys row: checking enables the feature; NUD greyed when unchecked.
            _chkSlowKeys = new CheckBox
            {
                Text = Lang.T("Slow keys"), Left = PAD, Top = slowY + 8, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = F_LABEL,
                TabIndex = 2,
            };
            grpAcc.Controls.Add(_chkSlowKeys);
            SetTip(_chkSlowKeys, () => Lang.T("tip: Slow keys"));

            _nudSlowKeys = new NumericUpDown
            {
                Left = nudX, Top = slowY + 4, Width = nudW, Height = 26,
                Minimum = 100, Maximum = 3000, Increment = 50, Value = 300,
                BackColor = C_INPUT_BG, ForeColor = C_LBL, Font = F_LABEL,
                TabIndex = 3, Enabled = false,
                AccessibleName = Lang.StripMnemonic(Lang.T("Slow keys")),
            };
            grpAcc.Controls.Add(_nudSlowKeys);
            SetTip(_nudSlowKeys, () => Lang.T("tip: Slow keys"));

            // Dwell click row: same pattern.
            _chkDwell = new CheckBox
            {
                Text = Lang.T("Dwell click"), Left = PAD, Top = dwellY + 8, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = F_LABEL,
                TabIndex = 4,
            };
            grpAcc.Controls.Add(_chkDwell);
            SetTip(_chkDwell, () => Lang.T("tip: Dwell click"));

            _nudDwell = new NumericUpDown
            {
                Left = nudX, Top = dwellY + 4, Width = nudW, Height = 26,
                Minimum = 100, Maximum = 5000, Increment = 100, Value = 1000,
                BackColor = C_INPUT_BG, ForeColor = C_LBL, Font = F_LABEL,
                TabIndex = 5, Enabled = false,
                AccessibleName = Lang.StripMnemonic(Lang.T("Dwell click")),
            };
            grpAcc.Controls.Add(_nudDwell);
            SetTip(_nudDwell, () => Lang.T("tip: Dwell click"));

            // Checking one feature auto-unchecks the other (mutually exclusive);
            // also enables/disables the paired NUD and the animation checkbox.
            _chkSlowKeys.CheckedChanged += (s, e) =>
            {
                _nudSlowKeys.Enabled        = _chkSlowKeys.Checked;
                if (_chkSlowKeys.Checked) _chkDwell.Checked = false;
                _chkTimingAnimation.Enabled = _chkSlowKeys.Checked || _chkDwell.Checked;
            };
            _chkDwell.CheckedChanged += (s, e) =>
            {
                _nudDwell.Enabled           = _chkDwell.Checked;
                if (_chkDwell.Checked) _chkSlowKeys.Checked = false;
                _chkTimingAnimation.Enabled = _chkSlowKeys.Checked || _chkDwell.Checked;
            };

            // Show timing animation: bottom-up fill on keys during countdown.
            int animY = HDR_H + PAD + ROW_H * 4;
            _chkTimingAnimation = new CheckBox
            {
                Text = Lang.T("Show timing animation"),
                Left = PAD, Top = animY + 8, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = F_LABEL,
                TabIndex = 6, Checked = true,
            };
            grpAcc.Controls.Add(_chkTimingAnimation);
            SetTip(_chkTimingAnimation, () => Lang.T("tip: Show timing animation"));

            // ── Bottom action buttons ─────────────────────────────────────
            // Place them below whichever column is taller.
            int btnTop = Math.Max(leftY, rightY) + gap;
            int bw     = (colW * 2 + gap - gap) / 2;  // each button is half the total column width

            _btnCancel = MakeActionBtn(Lang.T("Cancel"), margin,        btnTop, bw, 44); _btnCancel.TabIndex = 4;
            _btnApply  = MakeActionBtn(Lang.T("Apply"),  margin+bw+gap, btnTop, bw, 44); _btnApply.TabIndex  = 5;

            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            ClientSize = new Size(ClientSize.Width, btnTop + 44 + margin);

            WrapInScrollPanel(grpLang, grpWnd, grpFile, grpAcc, _btnCancel, _btnApply);
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;
        }

        // ════════════════════════════════════════════════════════════════
        // Helper methods for building UI sections
        // ════════════════════════════════════════════════════════════════

        // AddGroup, AddColorRow, GetSwatchHex (was GetHex), SetSwatchHex (was SetHex),
        // AddFieldLabel, SetTip, MakeActionBtn, ParseColor — all inherited from FluentDialogBase.

        /// <summary>
        /// Creates a small <see cref="FluentButton"/> suitable for file operations
        /// (Save, Save As, Load) and adds it to a parent panel.
        /// </summary>
        private Button MakeFileBtn(string text, Panel parent, int x, int y, int w)
        {
            var btn = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = ROW_H - 8,
                Style = FluentButton.Variant.Neutral,
                TabStop = true,
            };
            parent.Controls.Add(btn);
            return btn;
        }

        // ════════════════════════════════════════════════════════════════
        // Populating controls from data
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Pushes a set of theme / window / meta values into every UI
        /// control.  Called once after construction and again after a
        /// "Load" file operation.
        /// </summary>
        /// <param name="t">Visual theme to display.</param>
        /// <param name="ws">Window state to display.</param>
        /// <param name="m">Layout metadata to display.</param>
        private void PopulateFields(VisualTheme t, WindowState ws, LayoutMeta m)
        {
            // Convert the stored opacity fraction (0.2 – 1.0) to a slider
            // value (0 – 80).  Slider 0 = fully opaque (opacity 1.0).
            // Slider 80 = most transparent we allow (opacity 0.2).
            int opacitySlider = (int)Math.Round((1.0 - Math.Clamp(t.Opacity, 0.2, 1.0)) * 100);
            _trkOpacity.Value = Math.Clamp(opacitySlider, 0, 80);

            SetSwatchHex(_pnlBgColor, SettingsManager.Hex(t.BackgroundColor));

            _chkAlwaysOnTop.Checked  = ws.AlwaysOnTop;
            _chkStickyMods.Checked   = m.StickyModifiers;
            _chkHoldToEdit.Checked   = m.HoldToEdit;
            _chkHideTitlebar.Checked = ws.HideTitlebar;
            _cmbToolbarTheme.SelectedIndex = (int)m.ToolbarTheme;
            if (m.SlowKeysMs > 0)
            {
                _nudSlowKeys.Value   = Math.Clamp(m.SlowKeysMs, 100, 3000);
                _chkSlowKeys.Checked = true;
            }
            else
            {
                _chkSlowKeys.Checked = false;
            }
            _nudSlowKeys.Enabled = _chkSlowKeys.Checked;

            if (m.DwellMs > 0)
            {
                _nudDwell.Value   = Math.Clamp(m.DwellMs, 100, 5000);
                _chkDwell.Checked = true;
            }
            else
            {
                _chkDwell.Checked = false;
            }
            _nudDwell.Enabled           = _chkDwell.Checked;
            _chkTimingAnimation.Enabled = m.SlowKeysMs > 0 || m.DwellMs > 0;
            _chkTimingAnimation.Checked = m.ShowTimingAnimation;
        }

        // ════════════════════════════════════════════════════════════════
        // Apply / commit
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads every control, builds new <see cref="VisualTheme"/>,
        /// <see cref="WindowState"/>, and <see cref="LayoutMeta"/> objects,
        /// detects which fields changed, then closes the dialog with
        /// <see cref="DialogResult.OK"/>.
        ///
        /// Fields that this editor does not expose (e.g. window size) are
        /// copied verbatim from the original so they are not accidentally reset.
        /// </summary>
        private void Apply()
        {
            // Key style fields (font, colors, border) are now managed exclusively
            // through the standard group in GroupEditorForm.  Only window-level
            // theme fields (background color, opacity) are edited here; pass all
            // style fields through unchanged from the source theme.
            var theme = new VisualTheme
            {
                BackgroundColor = ParseColor(GetSwatchHex(_pnlBgColor), ColorTranslator.FromHtml("#1A1A2E")),

                // Convert slider value back to an opacity fraction.
                // Slider 0 → opacity 1.0 (opaque); slider 80 → opacity 0.2 (most transparent).
                Opacity = Math.Clamp((100 - _trkOpacity.Value) / 100.0, 0.2, 1.0),

                // Pass style fields through unchanged — they are no longer editable
                // in this dialog; the standard group is the authoritative source.
                FontName        = _srcTheme.FontName,
                FontSize        = _srcTheme.FontSize,
                FontColor       = _srcTheme.FontColor,
                KeyColor        = _srcTheme.KeyColor,
                BorderColor     = _srcTheme.BorderColor,
                BorderThickness = _srcTheme.BorderThickness,
            };

            var window = new WindowState
            {
                // WindowWidth / WindowHeight are not exposed in this editor,
                // so copy them unchanged from the original to avoid resetting
                // a window size the user previously set by dragging.
                WindowWidth  = _srcWindow.WindowWidth,
                WindowHeight = _srcWindow.WindowHeight,
                HideTitlebar = _chkHideTitlebar.Checked,
                AlwaysOnTop  = _chkAlwaysOnTop.Checked,
            };

            var meta = new LayoutMeta
            {
                // Language, LastFile, and gear-button position are managed
                // elsewhere; copy them through so we do not wipe them.
                Language        = _srcMeta.Language,
                LastFile        = _srcMeta.LastFile,
                GearRow         = _srcMeta.GearRow,
                GearCol         = _srcMeta.GearCol,
                StickyModifiers = _chkStickyMods.Checked,
                HoldToEdit      = _chkHoldToEdit.Checked,
                ToolbarTheme    = (ToolbarTheme)_cmbToolbarTheme.SelectedIndex,
                SlowKeysMs           = _chkSlowKeys.Checked ? (int)_nudSlowKeys.Value : 0,
                DwellMs              = _chkDwell.Checked    ? (int)_nudDwell.Value    : 0,
                ShowTimingAnimation  = _chkTimingAnimation.Checked,
            };

            ResultTheme  = theme;
            ResultWindow = window;
            ResultMeta   = meta;
            ResultGroups = _groups;

            DialogResult = DialogResult.OK;
            Close();
        }

        // ════════════════════════════════════════════════════════════════
        // Static utility methods
        // ════════════════════════════════════════════════════════════════

        // ParseColor inherited from FluentDialogBase.
        // GetInstalledFonts removed — use Fluent.InstalledFontNames() which caches the
        // result process-wide so the expensive GDI enumeration only runs once.

        // ════════════════════════════════════════════════════════════════
        // Helper class
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Wraps a language code and its human-readable display name for use
        /// as items in the language <see cref="ComboBox"/>.
        ///
        /// <see cref="ToString"/> returns only the name so the combo box
        /// shows "English" rather than "en – English".
        /// </summary>
        private class LangItem
        {
            /// <summary>The ISO / internal language code (e.g. "en", "nl").</summary>
            public string Code { get; }

            /// <summary>The human-readable name shown in the combo box (e.g. "English").</summary>
            public string Name { get; }

            /// <summary>
            /// Creates a new language item.
            /// </summary>
            /// <param name="code">Internal language code.</param>
            /// <param name="name">Display name shown in the combo box.</param>
            public LangItem(string code, string name) { Code = code; Name = name; }

            /// <summary>Returns the display name so the combo box shows readable text.</summary>
            public override string ToString() => Name;
        }
    }
}

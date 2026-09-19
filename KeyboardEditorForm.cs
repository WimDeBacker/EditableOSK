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

        // Word prediction database
        private CheckBox        _chkWPLearning;       // "Remember typed words"
        private ComboBox        _cmbWPDatabase;
        private Label           _lblWPInfo;
        private Button          _btnWPExport;
        private bool            _suppressWPChanged;   // re-entrancy guard

        // Word prediction — learning candidates (unknown words seen while typing,
        // not yet promoted to the real word list). See WordDatabase.RecordWord.
        private ListBox          _lstWPCandidates;
        private Button           _btnWPPromote;
        private Button           _btnWPReject;

        // Subscribed to WordDatabase.Loaded so the info label refreshes when a
        // background load completes while the editor is open (finding #6).
        // Named delegate stored so FormClosed can unsubscribe the same instance.
        private readonly Action _onWordDbLoaded;

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

            // Refresh the WP info label when a background database load completes
            // while this dialog is open (finding #6).
            _onWordDbLoaded = () =>
            {
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => { UpdateWPInfoLabel(); PopulateWPCandidates(); })); }
                catch (InvalidOperationException) { }
            };
            WordDatabase.Loaded += _onWordDbLoaded;
            FormClosed += (s, e) => WordDatabase.Loaded -= _onWordDbLoaded;
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
            _chkAlwaysOnTop.Text  = Lang.T("Always on top");
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
            if (_chkWPLearning != null) _chkWPLearning.Text = "&" + Lang.T("wp: Remember typed words");
            if (_btnWPExport   != null) _btnWPExport.Text   = Lang.T("wp: Export…");
            if (_btnWPPromote  != null) _btnWPPromote.Text  = Lang.T("wp: Promote");
            if (_btnWPReject   != null) _btnWPReject.Text   = Lang.T("wp: Reject");
            UpdateWPInfoLabel();
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
                Text = Lang.T("Always on top"),
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
            _btnSaveFile.Click   += (s, e) => { if (Apply()) _onSave?.Invoke(); };
            _btnSaveAsFile.Click += (s, e) => { if (Apply()) _onSaveAs?.Invoke(); };

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

            // ── Word Prediction card ──────────────────────────────────────
            // Rows: "Remember typed words" checkbox, combo (label + dropdown),
            // info label, export button, candidates label, candidates list,
            // promote/reject buttons.
            const int ChkRowH = 30;
            int wpH = HDR_H + PAD + ChkRowH + 24 + 46 + ROW_H + ROW_H + 24 + 74 + ROW_H + PAD;
            var grpWP = AddGroup(() => Lang.T("wp: Word prediction"), rightX, rightY, rightW, wpH,
                                 Color.FromArgb(22, 160, 133));
            grpWP.TabIndex = 4;
            rightY += wpH + gap;

            int wgy = HDR_H + PAD;   // running y inside the WP card

            // Master on/off switch for the learning engine. When off, RecordWord
            // is a no-op (WordDatabase.LearningEnabled) and nothing is written
            // to the overlay file — see WordDatabase's learning-engine remarks.
            _chkWPLearning = new CheckBox
            {
                Text = "&" + Lang.T("wp: Remember typed words"),
                Left = PAD, Top = wgy + 4, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = F_LABEL,
                TabIndex = 0, Checked = true,
            };
            grpWP.Controls.Add(_chkWPLearning);
            SetTip(_chkWPLearning, () => Lang.T("wp: tip remember"));
            _chkWPLearning.CheckedChanged += (s, e) =>
            {
                // Live feedback while the dialog is open — actual persistence
                // (LayoutMeta.WordLearningEnabled) only takes effect on Apply.
                WordDatabase.LearningEnabled = _chkWPLearning.Checked;
                UpdateWPInfoLabel();
                UpdateWPCandidateControlsEnabled();
            };
            wgy += ChkRowH;

            // Database selector — which base language file to use. No personal/
            // copy concept here: "Remember typed words" above handles learning
            // for whichever base file ends up loaded.
            AddFieldLabel(grpWP, () => Lang.T("wp: Database"), PAD, wgy + 2).TabIndex = 1;
            wgy += 24;
            _cmbWPDatabase = new ComboBox
            {
                Left = PAD, Top = wgy, Width = rightW - PAD * 2,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, FlatStyle = FlatStyle.Flat, TabIndex = 2,
                AccessibleName = Lang.StripMnemonic(Lang.T("wp: Database")),
            };
            grpWP.Controls.Add(_cmbWPDatabase);
            SetTip(_cmbWPDatabase, () => Lang.T("wp: tip database"));
            wgy += 46;

            // Info label: language · word count · learning on/off
            _lblWPInfo = new Label
            {
                Left = PAD, Top = wgy + 4, Width = rightW - PAD * 2, Height = 20,
                ForeColor = Fluent.TextHint, Font = F_HINT, AutoSize = false,
            };
            grpWP.Controls.Add(_lblWPInfo);
            wgy += ROW_H;

            _btnWPExport = MakeFileBtn(Lang.T("wp: Export…"), grpWP, PAD, wgy, rightW - PAD * 2);
            _btnWPExport.TabIndex = 3;
            SetTip(_btnWPExport, () => Lang.T("wp: tip export"));
            _btnWPExport.Click += (s, e) => WPExport();

            // ── Candidates: unknown words seen while typing, awaiting promotion ──
            wgy += ROW_H;
            AddFieldLabel(grpWP, () => Lang.T("wp: Candidates"), PAD, wgy + 2).TabIndex = 4;
            wgy += 24;

            _lstWPCandidates = new ListBox
            {
                Left = PAD, Top = wgy, Width = rightW - PAD * 2, Height = 70,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, BorderStyle = BorderStyle.FixedSingle, TabIndex = 5,
                AccessibleName = Lang.StripMnemonic(Lang.T("wp: Candidates")),
            };
            grpWP.Controls.Add(_lstWPCandidates);
            SetTip(_lstWPCandidates, () => Lang.T("wp: tip candidates"));
            _lstWPCandidates.SelectedIndexChanged += (s, e) => UpdateWPCandidateControlsEnabled();
            wgy += 74;

            int halfBw2 = (rightW - PAD * 2 - gap) / 2;
            _btnWPPromote = MakeFileBtn(Lang.T("wp: Promote"), grpWP, PAD, wgy, halfBw2);
            _btnWPPromote.TabIndex = 6;
            SetTip(_btnWPPromote, () => Lang.T("wp: tip promote"));
            _btnWPReject = MakeFileBtn(Lang.T("wp: Reject"), grpWP, PAD + halfBw2 + gap, wgy, halfBw2);
            _btnWPReject.TabIndex = 7;
            SetTip(_btnWPReject, () => Lang.T("wp: tip reject"));

            _btnWPPromote.Click += (s, e) => WPPromoteSelectedCandidate();
            _btnWPReject.Click  += (s, e) => WPRejectSelectedCandidate();

            PopulateWPCandidates();
            UpdateWPCandidateControlsEnabled();

            // Selection change: just refresh the info label — no copy dialog,
            // no revert logic; every entry in the (base-only) combo is directly
            // selectable.
            _cmbWPDatabase.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressWPChanged) return;
                UpdateWPInfoLabel();
            };

            // ── Bottom action buttons ─────────────────────────────────────
            // Place them below whichever column is taller.
            int btnTop = Math.Max(leftY, rightY) + gap;
            int bw     = (colW * 2 + gap - gap) / 2;  // each button is half the total column width

            _btnCancel = MakeActionBtn(Lang.T("Cancel"), margin,        btnTop, bw, 44); _btnCancel.TabIndex = 4;
            _btnApply  = MakeActionBtn(Lang.T("Apply"),  margin+bw+gap, btnTop, bw, 44); _btnApply.TabIndex  = 5;

            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            ClientSize = new Size(ClientSize.Width, btnTop + 44 + margin);

            WrapInScrollPanel(grpLang, grpWnd, grpFile, grpAcc, grpWP, _btnCancel, _btnApply);
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

            // Word prediction: learning toggle + database combo
            _chkWPLearning.Checked = m.WordLearningEnabled;
            PopulateWPDatabaseCombo(m.WordDatabase);
            UpdateWPCandidateControlsEnabled();
        }

        // ════════════════════════════════════════════════════════════════
        // Word prediction database helpers
        // ════════════════════════════════════════════════════════════════

        /// <summary>Combo-box item representing one base .wfq database file.</summary>
        private sealed class WPDbItem
        {
            public DatabaseInfo Info { get; }
            public WPDbItem(DatabaseInfo info) { Info = info; }
            public override string ToString()
            {
                string lang = string.IsNullOrEmpty(Info.Language)
                    ? "?" : Info.Language.ToUpper();
                return $"[{lang}]  {Info.DisplayName}";
            }
        }

        /// <summary>Combo-box item for the "auto-select" option.</summary>
        private sealed class WPDbAutoItem
        {
            public override string ToString() => Lang.T("wp: Auto");
        }

        /// <summary>
        /// Fills the word-prediction combo from the base .wfq files found in the
        /// app directory and pre-selects the entry that matches
        /// <paramref name="selectedFilename"/> (just the filename, no path).
        /// Selects "(auto)" when the string is empty or no match is found.
        /// </summary>
        /// <param name="selectedFilename">Filename to pre-select (no path).</param>
        private void PopulateWPDatabaseCombo(string selectedFilename)
        {
            _suppressWPChanged = true;
            try
            {
                _cmbWPDatabase.Items.Clear();
                _cmbWPDatabase.Items.Add(new WPDbAutoItem());

                string appDir = System.AppDomain.CurrentDomain.BaseDirectory;
                var registry  = new LanguageRegistry(appDir);
                foreach (var db in registry.All)
                    _cmbWPDatabase.Items.Add(new WPDbItem(db));

                // Pre-select the matching entry.
                int selectIdx = 0;  // default: "(auto)"
                if (!string.IsNullOrEmpty(selectedFilename))
                {
                    for (int i = 1; i < _cmbWPDatabase.Items.Count; i++)
                    {
                        if (_cmbWPDatabase.Items[i] is WPDbItem wpI &&
                            string.Equals(
                                System.IO.Path.GetFileName(wpI.Info.FilePath),
                                selectedFilename,
                                StringComparison.OrdinalIgnoreCase))
                        { selectIdx = i; break; }
                    }
                }
                _cmbWPDatabase.SelectedIndex = selectIdx;
            }
            finally { _suppressWPChanged = false; }

            UpdateWPInfoLabel();
        }

        /// <summary>
        /// Updates the info label below the combo (language, word count,
        /// learning on/off) and the Export button's enabled state.
        /// </summary>
        private void UpdateWPInfoLabel()
        {
            if (_lblWPInfo == null) return;

            string kind = _chkWPLearning.Checked
                ? Lang.T("wp: learning on") : Lang.T("wp: learning off");

            if (_cmbWPDatabase.SelectedItem is WPDbAutoItem)
            {
                _lblWPInfo.Text = WordDatabase.IsLoaded
                    ? $"{LangOrUnknown(WordDatabase.Language)}  ·  {WordDatabase.WordCount:N0} {Lang.T("wp: words")}  ·  {kind}"
                    : Lang.T("wp: No database loaded");
            }
            else if (_cmbWPDatabase.SelectedItem is WPDbItem item)
            {
                _lblWPInfo.Text = $"{LangOrUnknown(item.Info.Language)}  ·  {kind}";
            }

            UpdateWPExportEnabled();
        }

        private static string LangOrUnknown(string lang) =>
            string.IsNullOrEmpty(lang) ? "?" : lang.ToUpper();

        /// <summary>
        /// The Export button is only meaningful once something has actually
        /// been learned — enabled only when the selected (or auto-resolved)
        /// base database has a companion overlay file on disk.
        /// </summary>
        private void UpdateWPExportEnabled()
        {
            if (_btnWPExport == null) return;
            string basePath = SelectedOrLoadedBasePath();
            _btnWPExport.Enabled = basePath != null &&
                System.IO.File.Exists(WordDatabase.GetOverlayPath(basePath));
        }

        /// <summary>
        /// Full path of the base database the combo currently resolves to: the
        /// explicitly selected file, or (for "(auto)") whatever is actually
        /// loaded right now.
        /// </summary>
        private string SelectedOrLoadedBasePath()
        {
            if (_cmbWPDatabase.SelectedItem is WPDbItem item) return item.Info.FilePath;
            // "(auto)": WordDatabase doesn't expose its own load path, but
            // LastFile-style lookups aren't needed here — fall back to
            // re-resolving the same way KeyboardForm.LoadWordDatabase does,
            // via the registry, since the export button only needs a plausible
            // target, not perfect precision while the dialog is still open.
            var registry = new LanguageRegistry(System.AppDomain.CurrentDomain.BaseDirectory);
            var match = !string.IsNullOrEmpty(_srcMeta.Language)
                ? registry.GetForLanguage(_srcMeta.Language).FirstOrDefault()
                : null;
            return (match ?? registry.All.FirstOrDefault())?.FilePath;
        }

        /// <summary>
        /// Exports the currently selected base database's overlay file (the
        /// small file holding everything the learning engine has recorded) to
        /// a user-chosen location, for backup or transfer to another PC.
        /// </summary>
        private void WPExport()
        {
            string basePath = SelectedOrLoadedBasePath();
            if (basePath == null) return;
            string overlayPath = WordDatabase.GetOverlayPath(basePath);
            if (!System.IO.File.Exists(overlayPath)) return;

            using var dlg = new SaveFileDialog
            {
                Title      = Lang.T("wp: Export learned words"),
                Filter     = "Word database (*.wfq)|*.wfq|All files (*.*)|*.*",
                DefaultExt = "wfq",
                FileName   = System.IO.Path.GetFileName(overlayPath),
            };
            if (dlg.ShowDialog(this) != System.Windows.Forms.DialogResult.OK) return;

            try   { System.IO.File.Copy(overlayPath, dlg.FileName, overwrite: true); }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"{Lang.T("wp: Export failed")}\n{ex.Message}",
                    Lang.T("wp: Word prediction"),
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Candidates are shown as plain "word (count)" strings; the raw word
        /// is recovered by stripping the " (n)" suffix in
        /// <see cref="SelectedCandidateWord"/>. Refills <see cref="_lstWPCandidates"/>
        /// from <see cref="WordDatabase.GetCandidates"/>, most-seen first. Call
        /// after opening the dialog and after any promote/reject action.
        /// </summary>
        private void PopulateWPCandidates()
        {
            if (_lstWPCandidates == null) return;
            string previouslySelected = SelectedCandidateWord();

            _lstWPCandidates.Items.Clear();
            foreach (var (word, count) in WordDatabase.GetCandidates())
                _lstWPCandidates.Items.Add($"{word} ({count})");

            if (previouslySelected != null)
            {
                for (int i = 0; i < _lstWPCandidates.Items.Count; i++)
                    if (_lstWPCandidates.Items[i].ToString().StartsWith(previouslySelected + " (", StringComparison.Ordinal))
                    { _lstWPCandidates.SelectedIndex = i; break; }
            }

            UpdateWPCandidateControlsEnabled();
        }

        /// <summary>
        /// Enables the whole Candidates section only while "Remember typed
        /// words" is on, and the Promote/Reject buttons only while something
        /// is selected in the list.
        /// </summary>
        private void UpdateWPCandidateControlsEnabled()
        {
            bool learning = _chkWPLearning != null && _chkWPLearning.Checked;
            bool selected = _lstWPCandidates != null && _lstWPCandidates.SelectedIndex >= 0;

            if (_lstWPCandidates != null) _lstWPCandidates.Enabled = learning;
            if (_btnWPPromote    != null) _btnWPPromote.Enabled    = learning && selected;
            if (_btnWPReject     != null) _btnWPReject.Enabled     = learning && selected;
        }

        /// <summary>
        /// Extracts the raw word from the selected "word (count)" list entry,
        /// or <c>null</c> if nothing is selected.
        /// </summary>
        private string SelectedCandidateWord()
        {
            if (_lstWPCandidates?.SelectedItem is not string s) return null;
            int idx = s.LastIndexOf(" (", StringComparison.Ordinal);
            return idx > 0 ? s.Substring(0, idx) : s;
        }

        /// <summary>
        /// Promotes the selected candidate to a real, predictable word
        /// immediately, bypassing the usual occurrence-count threshold.
        /// </summary>
        private void WPPromoteSelectedCandidate()
        {
            string word = SelectedCandidateWord();
            if (word == null) return;
            WordDatabase.PromoteCandidate(word);
            PopulateWPCandidates();
        }

        /// <summary>
        /// Discards the selected candidate (e.g. a typo) without promoting it.
        /// </summary>
        private void WPRejectSelectedCandidate()
        {
            string word = SelectedCandidateWord();
            if (word == null) return;
            WordDatabase.RemoveCandidate(word);
            PopulateWPCandidates();
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
        private bool Apply()
        {
            // Refuse to proceed while any field is flagged invalid (e.g. bad background hex) —
            // the ErrorProvider icon already on that field is the feedback.
            if (HasPendingErrors()) return false;

            // Key style fields (font, colors, border) are now managed exclusively
            // through the standard group in GroupEditorForm.  Only window-level
            // theme fields (background color, opacity) are edited here; pass all
            // style fields through unchanged from the source theme.
            var theme = new VisualTheme
            {
                // Fall back to the prior background — never a hardcoded, unrelated colour —
                // so an invalid hex silently keeps the current look instead of jumping to
                // something the user never chose.
                BackgroundColor = ParseColor(GetSwatchHex(_pnlBgColor), _srcTheme.BackgroundColor),

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
                // Language, LastFile, gear position are managed elsewhere; copy through.
                Language        = _srcMeta.Language,
                LastFile        = _srcMeta.LastFile,
                GearRow         = _srcMeta.GearRow,
                GearCol         = _srcMeta.GearCol,
                // WordDatabase: use the filename of whatever is selected in the combo,
                // or empty string for "(auto)" which lets LoadWordDatabase() choose.
                WordDatabase    = _cmbWPDatabase.SelectedItem is WPDbItem selDb
                                  ? System.IO.Path.GetFileName(selDb.Info.FilePath)
                                  : "",
                WordLearningEnabled = _chkWPLearning.Checked,
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
            return true;
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

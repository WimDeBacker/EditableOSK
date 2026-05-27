using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

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
    public class KeyboardEditorForm : Form
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

        // ── Snapshot of the original settings (never mutated) ───────────

        /// <summary>The theme as it was when the editor was opened — used for change-detection in <see cref="Apply"/>.</summary>
        private readonly VisualTheme _srcTheme;
        /// <summary>The window state as it was when the editor was opened — fields not exposed in this editor are copied back verbatim.</summary>
        private readonly WindowState _srcWindow;
        /// <summary>The layout metadata as it was when the editor was opened.</summary>
        private readonly LayoutMeta  _srcMeta;

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
        private CheckBox      _chkAlwaysOnTop;
        private CheckBox      _chkStickyMods;
        private CheckBox      _chkHoldToEdit;
        private CheckBox      _chkHideTitlebar;
        private ComboBox      _cmbToolbarTheme;

        // File action delegates — called when Save/SaveAs/Load buttons are clicked
        private readonly Action _onSave;
        private readonly Action _onSaveAs;
        private readonly Action _onLoad;
        /// <summary>
        /// Retrieves the current key-group list from the main form after a
        /// "Load" operation so the editor can refresh its working copy.
        /// </summary>
        private readonly Func<List<KeyGroup>> _getGroups;

        // File buttons — kept as fields so <see cref="RelabelUI"/> can update their text
        private Button _btnSaveFile, _btnSaveAsFile, _btnLoadFile;

        // True when the toolbar is currently in dark mode — dialogs follow the same theme.
        private readonly bool _dark;

        // Confirm / dismiss
        private Button _btnApply, _btnCancel;

        // ── Translation helpers ──────────────────────────────────────────

        /// <summary>
        /// Labels whose text needs to be refreshed whenever the UI language
        /// changes.  Stored as (control, factory function) pairs so we can
        /// call the factory and push the new string on demand.
        /// </summary>
        private readonly List<(Label Ctrl, Func<string> GetText)> _transLabels
            = new List<(Label, Func<string>)>();

        /// <summary>
        /// Group panels whose header title needs to be redrawn on a
        /// language change.  Only the panel reference is needed here because
        /// the title factory is captured in the panel's Paint handler.
        /// </summary>
        private readonly List<(Panel Pnl, Func<string> GetTitle)> _transGroups
            = new List<(Panel, Func<string>)>();

        // ── Tooltip / accessibility helpers ──────────────────────────────
        /// <summary>Shared ToolTip component — one instance per form (WinForms best practice).</summary>
        private ToolTip _tip;

        /// <summary>
        /// Shared ErrorProvider — shows a field-level error icon and announces
        /// the error to screen readers when a hex colour field contains invalid text.
        /// </summary>
        private ErrorProvider _err;

        /// <summary>
        /// (Control, factory) pairs registered by <see cref="SetTip"/> so that
        /// <see cref="RelabelUI"/> can push refreshed tooltip strings on language switch.
        /// </summary>
        private readonly List<(Control Ctrl, Func<string> GetTip)> _transTooltips
            = new List<(Control, Func<string>)>();

        /// <summary>
        /// Set by <see cref="AddFieldLabel"/> and consumed by the next <see cref="AddColorRow"/>
        /// call so the colour row controls automatically receive the correct accessible name.
        /// </summary>
        private string _pendingAccessibleName;

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
                                  List<KeyGroup> groups = null, Func<List<KeyGroup>> getGroups = null)
        {
            // Store originals for change-detection and for fields we do not expose.
            _srcTheme  = theme;
            _srcWindow = window;
            _srcMeta   = meta;

            // Working copies — the user edits these; they are written back only on Apply().
            ResultTheme  = theme.Clone();
            ResultWindow = window.Clone();
            ResultMeta   = meta.Clone();

            // Deep-clone each group so the Group Editor sub-dialog cannot
            // accidentally modify the caller's list before OK is pressed.
            _groups   = groups?.Select(g => g.Clone()).ToList() ?? new List<KeyGroup>();
            ResultGroups = _groups;

            _onSave   = onSave;
            _onSaveAs = onSaveAs;
            _onLoad   = onLoad;
            _getGroups = getGroups;

            // Follow the toolbar theme so dialogs match the overall colour mode.
            _dark = !ToolbarButton.IsLightTheme;

            // Form chrome
            AutoScaleMode       = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            Text         = Lang.T("Edit Keyboard");
            BackColor    = _dark ? Fluent.DarkBg : Fluent.BgPage;
            FormBorderStyle = FormBorderStyle.FixedSingle;  // no resize handles
            MaximizeBox  = MinimizeBox = false;
            ShowIcon     = false;
            StartPosition = FormStartPosition.CenterParent;
            Size         = new Size(940, 560);  // final height is adjusted in BuildUI()
            TopMost      = true;
            Font         = F_LABEL;

            _tip = new ToolTip { InitialDelay = 400, AutoPopDelay = 10000, ShowAlways = true };
            _err = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.BlinkIfDifferentError };

            BuildUI();
            PopulateFields(theme, window, meta);
            // Apply dark/light theme to every control — after BuildUI so all controls exist.
            FluentPainter.ApplyDialogTheme(this, _dark);
            ActiveControl = _cmbLanguage;  // start keyboard focus on the language selector

            // Subscribe to the global language-change event so every label
            // updates automatically when the user picks a different language.
            Lang.LanguageChanged += RelabelUI;

            // Unsubscribe when the form closes.
            FormClosed += (s, e) => { Lang.LanguageChanged -= RelabelUI; _err?.Dispose(); };
        }

        // ════════════════════════════════════════════════════════════════
        // Language-change handler
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called automatically whenever <see cref="Lang.LanguageChanged"/>
        /// fires.  Re-translates every piece of text in the dialog so the
        /// user does not need to reopen it after switching language.
        /// </summary>
        private void RelabelUI()
        {
            Text                  = Lang.T("Edit Keyboard");
            _btnApply.Text        = Lang.T("Apply");
            _btnCancel.Text       = Lang.T("Cancel");
            _chkAlwaysOnTop.Text  = "&" + Lang.T("Always on top");
            _chkStickyMods.Text   = Lang.T("Sticky modifiers");
            _chkHoldToEdit.Text   = Lang.T("Hold to edit");
            _chkHideTitlebar.Text = Lang.T("Hide title bar");
            _btnSaveFile.Text   = "&" + Lang.T("Save");
            _btnSaveAsFile.Text = Lang.T("Save As…");
            _btnLoadFile.Text   = "&" + Lang.T("Load…");

            // Re-run each label's text factory and push the translated string.
            foreach (var (ctrl, getText) in _transLabels) ctrl.Text = getText();

            // Group panels paint their own header text via a custom Paint
            // handler, so invalidating them is enough to trigger a redraw.
            foreach (var (pnl,  _) in _transGroups) pnl.Invalidate();

            // Refresh all tooltips registered via SetTip() so they use the new language.
            foreach (var (ctrl, getTip) in _transTooltips) _tip.SetToolTip(ctrl, getTip());

            Invalidate(true);  // repaint everything else (e.g. the form background)
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
                // After the load, fetch the newly loaded groups (if the caller
                // provided a factory) and refresh all controls.
                if (_getGroups != null)
                    _groups = _getGroups().Select(g => g.Clone()).ToList();
                PopulateFields(_srcTheme, _srcWindow, _srcMeta);
            };

            // ── Accessibility card ─────────────────────────────────────────
            int accH = HDR_H + PAD + ROW_H * 2 + PAD;
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

            // ── Bottom action buttons ─────────────────────────────────────
            // Place them below whichever column is taller.
            int btnTop = Math.Max(leftY, rightY) + gap;
            int bw     = (colW * 2 + gap - gap) / 2;  // each button is half the total column width

            _btnCancel = MakeActionBtn(Lang.T("Cancel"), FluentButton.Variant.Neutral, margin,        btnTop, bw, 44); _btnCancel.TabIndex = 4;
            _btnApply  = MakeActionBtn(Lang.T("Apply"),  FluentButton.Variant.Neutral, margin+bw+gap, btnTop, bw, 44); _btnApply.TabIndex  = 5;

            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;

            // Shrink-wrap the form height so there is no empty space at the bottom.
            ClientSize = new Size(ClientSize.Width, btnTop + 44 + margin);
        }

        // ════════════════════════════════════════════════════════════════
        // Helper methods for building UI sections
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a styled card (a <see cref="Panel"/> with a coloured
        /// header bar drawn via <see cref="FluentPainter.PaintCard"/>) and
        /// adds it to the form.
        /// </summary>
        /// <param name="getTitle">
        ///   Factory that returns the translated title string.  Called each
        ///   time the panel is painted so language changes show immediately.
        /// </param>
        /// <param name="x">Left edge of the card in form coordinates.</param>
        /// <param name="y">Top edge of the card in form coordinates.</param>
        /// <param name="w">Width of the card in pixels.</param>
        /// <param name="h">Height of the card in pixels.</param>
        /// <param name="accentColor">
        ///   Colour used for the left accent stripe in the header bar.
        /// </param>
        /// <returns>The panel that acts as the card's content container.</returns>
        private Panel AddGroup(Func<string> getTitle, int x, int y, int w, int h, Color accentColor)
        {
            Color bg = _dark ? Color.FromArgb(48, 48, 48) : Fluent.BgCard;
            var pnl = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = bg };

            // The Paint handler delegates all drawing to FluentPainter so the
            // card automatically uses the current Fluent theme colours.
            bool dark = _dark;
            pnl.Paint += (s, e) =>
                FluentPainter.PaintCard(e.Graphics, pnl.Width, pnl.Height, getTitle(), accentColor, HDR_H, dark);

            Controls.Add(pnl);

            // Register in the translation list so RelabelUI() can trigger a
            // repaint when the language changes.
            _transGroups.Add((pnl, getTitle));
            return pnl;
        }

        /// <summary>
        /// Adds a colour-picker row to a parent panel.  The row consists of:
        /// <list type="number">
        ///   <item><description>
        ///     A <see cref="TextBox"/> for typing a hex colour code (#RRGGBB).
        ///   </description></item>
        ///   <item><description>
        ///     A small coloured swatch <see cref="Panel"/> that shows the
        ///     current colour and opens a <see cref="ColorDialog"/> on click.
        ///   </description></item>
        /// </list>
        /// The two controls are kept in sync: changing the hex text updates
        /// the swatch, and picking a colour from the dialog writes its hex
        /// back to the text box.
        /// </summary>
        /// <param name="parent">Panel that will own the new controls.</param>
        /// <param name="x">Left edge of the row inside <paramref name="parent"/>.</param>
        /// <param name="y">Top edge of the row inside <paramref name="parent"/>.</param>
        /// <param name="totalW">Total width for both controls combined.</param>
        /// <returns>
        ///   The swatch panel (used as a handle by <see cref="GetHex"/> and
        ///   <see cref="SetHex"/>).  The text box reference is stored in
        ///   <see cref="Control.Tag"/> so it can be retrieved from the swatch.
        /// </returns>
        private Button AddColorRow(Panel parent, int x, int y, int totalW, ref int ti)
        {
            int sw = 32;  // fixed width of the colour swatch square
            var txtHex = new TextBox
            {
                Left = x, Top = y, Width = totalW - sw - 5,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Fluent.FontCourier,  // monospace makes hex codes easier to read
                TabIndex = ti++,
            };
            // ColorSwatchButton participates in Tab order and responds to Space/Enter natively
            // (WCAG 2.1 A §2.1.1). It also draws a two-tone focus ring visible on any colour
            // (WCAG 2.1 AA §2.4.7).
            var swatch = new ColorSwatchButton
            {
                Left = x + totalW - sw, Top = y, Width = sw, Height = 26,
                BackColor = Color.Gray,
                TabIndex = ti++,
            };
            // Consume the name queued by the preceding AddFieldLabel call.
            string colorName = _pendingAccessibleName;
            _pendingAccessibleName = null;
            if (colorName != null)
            {
                txtHex.AccessibleName  = colorName + " hex";
                swatch.AccessibleName  = colorName + " swatch";
            }
            SetTip(txtHex, () => Lang.T("tip: Hex color"));
            SetTip(swatch, () => Lang.T("tip: Color swatch"));

            // Keep the swatch colour in sync as the user types a hex value.
            // ParseColor uses the current swatch colour as fallback so the
            // swatch does not flash to black on a partially typed hex string.
            txtHex.TextChanged += (s, e) =>
            {
                swatch.BackColor = ParseColor(txtHex.Text, swatch.BackColor);
                bool bad = !string.IsNullOrWhiteSpace(txtHex.Text)
                           && ParseColor(txtHex.Text, Color.Empty).IsEmpty;
                _err.SetError(txtHex, bad ? Lang.T("err: invalid hex") : "");
            };

            // Clicking (or pressing Space/Enter) the swatch opens the system colour picker.
            swatch.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { Color = swatch.BackColor };
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtHex.Text = SettingsManager.Hex(dlg.Color);  // writes hex → triggers TextChanged above
            };

            parent.Controls.Add(txtHex);
            parent.Controls.Add(swatch);

            // Store the text box in Tag so GetHex/SetHex can retrieve it
            // from just the swatch panel reference.
            swatch.Tag = txtHex;
            return swatch;
        }

        /// <summary>
        /// Reads the hex colour string from the text box that belongs to a
        /// colour-picker swatch panel created by <see cref="AddColorRow"/>.
        /// </summary>
        /// <param name="s">The swatch panel whose Tag holds the text box.</param>
        /// <returns>The current hex string, or an empty string if unavailable.</returns>
        private string GetHex(Button s) => (s.Tag is TextBox t) ? t.Text : "";

        /// <summary>
        /// Writes a hex colour string into the text box of a colour-picker
        /// swatch panel and updates the swatch background accordingly.
        /// </summary>
        /// <param name="s">The swatch panel whose Tag holds the text box.</param>
        /// <param name="hex">Hex colour string such as "#FF8800".</param>
        private void SetHex(Button s, string hex)
        {
            if (s.Tag is TextBox t) { t.Text = hex; s.BackColor = ParseColor(hex, s.BackColor); }
        }

        /// <summary>
        /// Adds a right-aligned field label to a card panel and registers it
        /// in the translation list so it updates when the language changes.
        /// </summary>
        /// <param name="parent">The card panel that will contain the label.</param>
        /// <param name="getText">Factory that returns the translated label text.</param>
        /// <param name="x">Left edge of the label inside <paramref name="parent"/>.</param>
        /// <param name="y">Top edge of the row; the label is offset down by 6 px to vertically align with adjacent inputs.</param>
        private Label AddFieldLabel(Panel parent, Func<string> getText, int x, int y)
        {
            var lbl = new Label
            {
                Text = getText(), Left = x, Top = y + 6, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            parent.Controls.Add(lbl);
            _transLabels.Add((lbl, getText));
            // Store stripped label text for the next AddColorRow call.
            _pendingAccessibleName = Lang.StripMnemonic(getText());
            return lbl;
        }

        /// <summary>
        /// Registers a tooltip on <paramref name="ctrl"/> and adds it to
        /// <see cref="_transTooltips"/> so <see cref="RelabelUI"/> can refresh it.
        /// </summary>
        private void SetTip(Control ctrl, Func<string> getTip)
        {
            _tip.SetToolTip(ctrl, getTip());
            _transTooltips.Add((ctrl, getTip));
        }

        /// <summary>
        /// Creates a small <see cref="FluentButton"/> suitable for file
        /// operations (Save, Save As, Load) and adds it to a parent panel.
        /// </summary>
        /// <param name="text">Button label text.</param>
        /// <param name="parent">Card panel that will own the button.</param>
        /// <param name="x">Left edge inside <paramref name="parent"/>.</param>
        /// <param name="y">Top edge inside <paramref name="parent"/>.</param>
        /// <param name="w">Button width in pixels.</param>
        /// <returns>The newly created button.</returns>
        private Button MakeFileBtn(string text, Panel parent, int x, int y, int w)
        {
            var btn = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = ROW_H - 8,
                Style = FluentButton.Variant.Neutral,
                TabStop = true,   // file buttons must be keyboard-reachable (WCAG 2.1 A §2.1.1)
            };
            parent.Controls.Add(btn);
            return btn;
        }

        /// <summary>
        /// Creates an action button (Apply / Cancel) and adds it directly to
        /// the form (not to a card panel) so it sits below all the cards.
        /// </summary>
        /// <param name="text">Button label text.</param>
        /// <param name="style">Visual variant from <see cref="FluentButton.Variant"/>.</param>
        /// <param name="x">Left edge in form coordinates.</param>
        /// <param name="y">Top edge in form coordinates.</param>
        /// <param name="w">Width in pixels.</param>
        /// <param name="h">Height in pixels.</param>
        /// <returns>The newly created button.</returns>
        private Button MakeActionBtn(string text, FluentButton.Variant style, int x, int y, int w, int h)
        {
            var btn = new FluentButton { Text = text, Left = x, Top = y, Width = w, Height = h, Style = style,
                                         TabStop = true };   // action buttons must be reachable by keyboard
            Controls.Add(btn);
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

            SetHex(_pnlBgColor, SettingsManager.Hex(t.BackgroundColor));

            _chkAlwaysOnTop.Checked  = ws.AlwaysOnTop;
            _chkStickyMods.Checked   = m.StickyModifiers;
            _chkHoldToEdit.Checked   = m.HoldToEdit;
            _chkHideTitlebar.Checked = ws.HideTitlebar;
            _cmbToolbarTheme.SelectedIndex = (int)m.ToolbarTheme;
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
                BackgroundColor = ParseColor(GetHex(_pnlBgColor), ColorTranslator.FromHtml("#1A1A2E")),

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

        /// <summary>
        /// Thin wrapper around <see cref="SettingsManager.ParseColor"/> that
        /// keeps call sites concise.
        /// </summary>
        /// <param name="hex">A hex colour string such as "#RRGGBB" or "#AARRGGBB".</param>
        /// <param name="fallback">Colour to return when <paramref name="hex"/> cannot be parsed.</param>
        /// <returns>The parsed colour, or <paramref name="fallback"/>.</returns>
        private static Color ParseColor(string hex, Color fallback) => SettingsManager.ParseColor(hex, fallback);

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

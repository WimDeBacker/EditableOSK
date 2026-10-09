// KeyboardEditorForm.cs — the Keyboard Editor rebuilt on the touch components (spec: keyboardeditor_spec.md).
//
// Same pattern as GroupEditorForm / KeyEditorForm: content-sized FluentDialogBase, a section bar with three sections
// (General, Accessibility, Word prediction), 44 px controls, table layout, no hand-positioned control, English and Dutch.
//
// What is different from the old dialog (see the inventory for the defects this fixes):
//   • Apply clones the source objects and overwrites only the fields it edits, so a field added to a model later is never reset.
//   • Save / Save As apply the edits first and set FileAction; the caller copies the results and then saves (the old dialog saved
//     the previous values). Load asks first and only refills the dialog when a file was really loaded.
//   • Language changes live and is restored on Cancel. "Remember typed words" only takes effect on Apply.
//   • Slow keys / Dwell click are a radio group (Off / Slow keys / Dwell click) instead of two exclusive check boxes.
//   • Controls that stand under each other share one width (OptionStack, ButtonRow).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>What the user asked the caller to do with the layout file after the dialog closed with OK.</summary>
    public enum KeyboardFileAction { None, Save, SaveAs }

    /// <summary>
    /// The word-prediction data the dialog reads and changes. Every member defaults to the static <see cref="WordDatabase"/> /
    /// <see cref="LanguageRegistry"/>; tests replace them so no real database is needed.
    /// </summary>
    internal sealed class WordPredictionBackend
    {
        public Func<IReadOnlyList<(string Word, int Count)>> Candidates = () => WordDatabase.GetCandidates();
        public Func<string, bool> Promote = w => WordDatabase.PromoteCandidate(w);
        public Func<string, bool> Reject  = w => WordDatabase.RemoveCandidate(w);
        public Func<IReadOnlyList<DatabaseInfo>> Databases = () => new LanguageRegistry(AppDomain.CurrentDomain.BaseDirectory).All;
        public Func<bool>   IsLoaded       = () => WordDatabase.IsLoaded;
        public Func<string> LoadedLanguage = () => WordDatabase.Language;
        public Func<int>    WordCount      = () => WordDatabase.WordCount;
        public Func<string, string> OverlayPath = p => WordDatabase.GetOverlayPath(p);
        public Func<string, bool>   FileExists  = p => File.Exists(p);
        public Func<bool>           IsLoading   = () => WordDatabase.IsLoading;
        /// <summary>Adds the learned words of a file to those of the database (base file, its language, the file to import).</summary>
        public Func<string, string, string, WordDatabase.LearnedImportResult> ImportLearned = (basePath, language, file) => WordDatabase.MergeLearned(basePath, language, file);
        /// <summary>Loads the database again when it is the one in memory, so the imported words count at once (in the background: it can be large).</summary>
        public Action<string> ReloadIfLoaded = basePath => { if (WordDatabase.IsLoadedFor(basePath)) System.Threading.Tasks.Task.Run(() => WordDatabase.Load(basePath)); };
    }

    /// <summary>
    /// A modal dialog for the settings of the whole keyboard: language, window, layout file, accessibility, word prediction.
    /// On OK the edited copies are in <see cref="ResultTheme"/>, <see cref="ResultWindow"/>, <see cref="ResultMeta"/> and
    /// <see cref="ResultGroups"/>, and <see cref="FileAction"/> says whether the caller should save the layout file next.
    /// </summary>
    public class KeyboardEditorForm : FluentDialogBase
    {
        // ── Results ──────────────────────────────────────────────────
        public VisualTheme ResultTheme  { get; private set; }
        public WindowState ResultWindow { get; private set; }
        public LayoutMeta  ResultMeta   { get; private set; }
        public List<KeyGroup> ResultGroups { get; private set; }

        /// <summary>Set when Save or Save As closed the dialog: the caller copies the results and then saves the layout file.</summary>
        public KeyboardFileAction FileAction { get; private set; } = KeyboardFileAction.None;

        /// <summary>Two columns (word prediction) and the widest label need a little more than the default.</summary>
        protected override int ContentMaxWidth => 920;

        // ── Source objects: Apply clones these and overwrites what the dialog edits ──
        private VisualTheme _sourceTheme;
        private WindowState _sourceWindow;
        private LayoutMeta  _sourceMeta;
        private List<KeyGroup> _groups;

        // ── Callbacks from the main form ─────────────────────────────
        private readonly Func<bool> _onLoad;
        private readonly Func<List<KeyGroup>> _getGroups;
        private readonly Func<(VisualTheme, WindowState, LayoutMeta)> _getSettings;
        private readonly WordPredictionBackend _backend;

        // ── Controls: General ────────────────────────────────────────
        private TouchChoiceButton _cmbLanguage, _cmbToolbarTheme;
        private TouchStepper      _stpOpacity;
        private ColorChip         _chipBackground;
        private TouchCheckBox     _chkAlwaysOnTop, _chkHideTitlebar;
        private FluentButton      _btnSaveFile, _btnSaveAsFile, _btnLoadFile;

        // ── Controls: Accessibility ──────────────────────────────────
        private TouchCheckBox    _chkStickyMods, _chkHoldToEdit, _chkTimingAnimation, _chkCornerLabels;
        private TouchRadioButton _optTimingOff, _optSlowKeys, _optDwell;
        private TouchStepper     _stpSlowKeys, _stpDwell;

        // ── Controls: Word prediction ────────────────────────────────
        private TouchCheckBox     _chkWPLearning;
        private TouchChoiceButton _cmbWPDatabase;
        private Label             _lblWPInfo, _lblExportHint, _lblImmediateHint;
        private FluentButton      _btnWPExport, _btnWPImport, _btnWPPromote, _btnWPReject;
        private TouchList         _lstWPCandidates;
        private Panel             _candidateFrame;

        private FluentButton _btnApply, _btnCancel;

        // ── State that is not a control ──────────────────────────────
        private bool   _loading;                 // set only while the code fills controls (read by change handlers, never by Apply)
        private string _languageOnOpen = "";     // restored when the dialog is cancelled
        private readonly List<string> _languageCodes = new List<string>();
        private readonly List<string> _databaseFiles = new List<string>();     // file name per chooser row; "" = automatic
        private readonly Action _onWordDbLoaded;
        private bool _released;

        // The enum values in the order of the chooser rows (explicit: no index = enum coupling).
        private static readonly ToolbarTheme[] ToolbarThemeOrder = { ToolbarTheme.Dark, ToolbarTheme.Light, ToolbarTheme.System };

        // ── Construction ─────────────────────────────────────────────

        public KeyboardEditorForm(VisualTheme theme, WindowState window, LayoutMeta meta, Form owner,
                                   Func<bool> onLoad = null, List<KeyGroup> groups = null,
                                   Func<List<KeyGroup>> getGroups = null,
                                   Func<(VisualTheme, WindowState, LayoutMeta)> getSettings = null)
            : this(theme, window, meta, owner, onLoad, groups, getGroups, getSettings, new WordPredictionBackend()) { }

        internal KeyboardEditorForm(VisualTheme theme, WindowState window, LayoutMeta meta, Form owner,
                                     Func<bool> onLoad, List<KeyGroup> groups, Func<List<KeyGroup>> getGroups,
                                     Func<(VisualTheme, WindowState, LayoutMeta)> getSettings, WordPredictionBackend backend)
        {
            _sourceTheme = theme; _sourceWindow = window; _sourceMeta = meta;
            _groups      = groups?.Select(g => g.Clone()).ToList() ?? new List<KeyGroup>();
            _onLoad = onLoad; _getGroups = getGroups; _getSettings = getSettings;
            _backend = backend ?? new WordPredictionBackend();

            ResultTheme = theme.Clone(); ResultWindow = window.Clone(); ResultMeta = meta.Clone(); ResultGroups = _groups;
            _languageOnOpen = Lang.CurrentCode;

            Text = Lang.T("Edit Keyboard");
            BuildUI();
            PopulateFields(_sourceTheme, _sourceWindow, _sourceMeta);
            ActiveControl = _cmbLanguage;

            // The info line and the candidates follow a background database load that finishes while the dialog is open.
            _onWordDbLoaded = () =>
            {
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => { if (!IsDisposed) { UpdateWPInfo(); PopulateCandidates(); } })); }
                catch (InvalidOperationException) { }
            };
            WordDatabase.Loaded += _onWordDbLoaded;
            FormClosed += (s, e) => { RestoreLanguageIfCancelled(); ReleaseFormResources(); };
        }

        /// <summary>Unsubscribes from the static word database event. Also run from Dispose: a form built and never shown never gets FormClosed.</summary>
        private void ReleaseFormResources()
        {
            if (_released) return;
            _released = true;
            if (_onWordDbLoaded != null) WordDatabase.Loaded -= _onWordDbLoaded;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseFormResources();
            base.Dispose(disposing);
        }

        /// <summary>Cancel does not keep a language change that was applied live while the dialog was open.</summary>
        private void RestoreLanguageIfCancelled()
        {
            if (DialogResult != DialogResult.OK && Lang.CurrentCode != _languageOnOpen) Lang.Load(_languageOnOpen);
        }

        // ── UI ───────────────────────────────────────────────────────

        private void BuildUI()
        {
            _btnCancel = MakeTouchButton(() => Lang.T("Cancel"));
            _btnApply  = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, _btnCancel, _btnApply), withSections: true);
            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;

            BuildGeneral(AddSection(() => Lang.T("General")));
            BuildAccessibility(AddSection(() => Lang.T("Accessibility")));
            BuildWordPrediction(AddSection(() => Lang.T("wp: Word prediction")));
        }

        // ── Section 1: General ──────────────────────────────────────

        private void BuildGeneral(TableLayoutPanel t)
        {
            // Language: live (the dialog and the app translate at once); restored on Cancel.
            // 280 wide when there is room, narrower when there is not (a long label next to it on a narrow window).
            _cmbLanguage = new TouchChoiceButton { RowHeight = 44, AutoSize = false, Size = new Size(280, Touch.Target), MinimumSize = new Size(Touch.InputMinWidth * 2 / 3, Touch.Target), MaximumSize = new Size(280, Touch.Target) };
            foreach (var (code, name) in Lang.GetAvailable()) _languageCodes.Add(code);
            _cmbLanguage.SetItems(Lang.GetAvailable().Select(l => new TouchChoice { Text = l.Name }), 0);
            _cmbLanguage.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                int i = _cmbLanguage.SelectedIndex;
                if (i >= 0 && i < _languageCodes.Count) Lang.Load(_languageCodes[i]);
            };
            SetTip(_cmbLanguage, () => Lang.T("tip: Language"));
            AddRow(t, () => Lang.T("Language"), _cmbLanguage, fill: false);
            _cmbLanguage.Anchor = AnchorStyles.Left | AnchorStyles.Right;       // takes the column's width, up to its maximum

            AddWideRow(t, Heading(() => Lang.T("Window")), fill: false);

            _cmbToolbarTheme = new TouchChoiceButton { RowHeight = 44, AutoSize = false, Size = new Size(280, Touch.Target), MinimumSize = new Size(Touch.InputMinWidth * 2 / 3, Touch.Target), MaximumSize = new Size(280, Touch.Target) };
            _cmbToolbarTheme.SetItems(ToolbarThemeItems(), 0);
            SetTip(_cmbToolbarTheme, () => Lang.T("tip: Toolbar theme"));
            AddRow(t, () => Lang.T("Toolbar theme"), _cmbToolbarTheme, fill: false);
            _cmbToolbarTheme.Anchor = AnchorStyles.Left | AnchorStyles.Right;   // same width as the language chooser at every window width

            // Transparency: 0 = opaque … 80 = nearly transparent (a stepper: a slider thumb is not a 44 px target).
            _stpOpacity = new TouchStepper { Minimum = 0, Maximum = 80, Increment = 5, Margin = new Padding(0, 0, Touch.Gap, 0) };
            _stpOpacity.AccessibleName = Lang.StripMnemonic(Lang.T("Opacity"));
            _stpOpacity.AccessibleDescription = Lang.T("tip: Opacity");
            _stpOpacity.ValueBox.AccessibleDescription = _stpOpacity.AccessibleDescription;
            SetTip(_stpOpacity.ValueBox, () => Lang.T("tip: Opacity"));
            var percent = new Label { Text = "%", AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false, Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent };
            AddRow(t, () => "&" + Lang.T("Opacity"), InlineRow(_stpOpacity, percent), fill: false);

            // Background chip, Always on top and Hide title bar: one stack, one width.
            _chipBackground = new ColorChip(Lang.T("Background"), Color.Black);
            _transTexts.Add((_chipBackground, () => Lang.T("Background")));
            SetTip(_chipBackground, () => Lang.T("tip: Background"));
            _chkAlwaysOnTop  = NewCheck(() => Lang.T("Always on top"));
            _chkHideTitlebar = NewCheck(() => Lang.T("Hide title bar"));
            SetTip(_chkAlwaysOnTop,  () => Lang.T("tip: Always on top"));
            SetTip(_chkHideTitlebar, () => Lang.T("tip: Hide title bar"));
            AddWideRow(t, OptionStack(_chipBackground, _chkAlwaysOnTop, _chkHideTitlebar), fill: false);

            AddWideRow(t, Heading(() => Lang.T("Layout file")), fill: false);

            // Save / Save As apply the edits and close (the caller then saves); Load asks first. One common width.
            _btnSaveFile   = MakeTouchButton(() => "&" + Lang.T("Save"));
            _btnSaveAsFile = MakeTouchButton(() => Lang.T("Save As…"));
            _btnLoadFile   = MakeTouchButton(() => "&" + Lang.T("Load…"));
            SetTip(_btnSaveFile,   () => Lang.T("tip: Save settings"));
            SetTip(_btnSaveAsFile, () => Lang.T("tip: Save settings as"));
            SetTip(_btnLoadFile,   () => Lang.T("tip: Load"));
            _btnSaveFile.Click   += (s, e) => ApplyWith(KeyboardFileAction.Save);
            _btnSaveAsFile.Click += (s, e) => ApplyWith(KeyboardFileAction.SaveAs);
            _btnLoadFile.Click   += (s, e) => LoadLayout();
            AddWideRow(t, ButtonRow(_btnSaveFile, _btnSaveAsFile, _btnLoadFile), fill: false);
        }

        private IEnumerable<TouchChoice> ToolbarThemeItems()
        {
            yield return new TouchChoice { Text = Lang.T("Dark") };
            yield return new TouchChoice { Text = Lang.T("Light") };
            yield return new TouchChoice { Text = Lang.T("System default") };
        }

        // ── Section 2: Accessibility ────────────────────────────────

        private void BuildAccessibility(TableLayoutPanel t)
        {
            _chkStickyMods = NewCheck(() => Lang.T("Sticky modifiers"));
            _chkHoldToEdit = NewCheck(() => Lang.T("Hold to edit"));
            SetTip(_chkStickyMods, () => Lang.T("tip: Sticky modifiers"));
            SetTip(_chkHoldToEdit, () => Lang.T("tip: Hold to edit"));

            // The timing aid: a framed group of three radio buttons (exactly one is on). Column 1 holds the three radio buttons
            // and the animation check box (one width, right edges aligned), column 2 the steppers, column 3 the unit.
            _optTimingOff = NewRadio(() => Lang.T("kbd: Off"));
            _optSlowKeys  = NewRadio(() => Lang.T("Slow keys"));
            _optDwell     = NewRadio(() => Lang.T("Dwell click"));
            _stpSlowKeys  = NewTimingStepper(100, 3000, 50, 300, "Slow keys");
            _stpDwell     = NewTimingStepper(100, 5000, 100, 1000, "Dwell click");
            _chkTimingAnimation = NewCheck(() => Lang.T("Show timing animation"));
            SetTip(_optTimingOff, () => Lang.T("tip: Timing off"));
            SetTip(_optSlowKeys,  () => Lang.T("tip: Slow keys"));
            SetTip(_optDwell,     () => Lang.T("tip: Dwell click"));
            SetTip(_stpSlowKeys.ValueBox, () => Lang.T("tip: Slow keys"));
            SetTip(_stpDwell.ValueBox,    () => Lang.T("tip: Dwell click"));
            SetTip(_chkTimingAnimation, () => Lang.T("tip: Show timing animation"));
            _chkTimingAnimation.AccessibleDescription = Lang.T("tip: Show timing animation");

            // Wide: radio | stepper | unit on one line each, the steppers aligned at the right of their column. Narrow: the stepper and its
            // unit go on a line of their own under the radio button.
            var grid = new AdaptiveTable { ColumnCount = 3 };
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Control Unit() => new Label { Text = "ms", AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false, Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Margin = new Padding(Touch.Gap, 0, 0, 0) };
            foreach (var r in new Control[] { _optTimingOff, _optSlowKeys, _optDwell, _chkTimingAnimation })
            { r.Dock = DockStyle.Fill; r.Margin = new Padding(0, 4, Touch.Gap, 4); }
            _optTimingOff.TabIndex = 0; _optSlowKeys.TabIndex = 1; _optDwell.TabIndex = 2;
            var unitSlow = Unit(); var unitDwell = Unit();
            var sep = new Panel { Height = 1, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8), Tag = "notheme", BackColor = _dark ? Fluent.DialogDarkBorder : Fluent.BorderCard };
            grid.AddVariant(() =>
            {
                _stpSlowKeys.Anchor = AnchorStyles.Right; _stpDwell.Anchor = AnchorStyles.Right;
                return new[]
                {
                    new AdaptiveTable.Cell(_optTimingOff, 0, 0),
                    new AdaptiveTable.Cell(_optSlowKeys, 0, 1), new AdaptiveTable.Cell(_stpSlowKeys, 1, 1), new AdaptiveTable.Cell(unitSlow, 2, 1),
                    new AdaptiveTable.Cell(_optDwell, 0, 2),    new AdaptiveTable.Cell(_stpDwell, 1, 2),    new AdaptiveTable.Cell(unitDwell, 2, 2),
                    new AdaptiveTable.Cell(sep, 0, 3, 3),
                    new AdaptiveTable.Cell(_chkTimingAnimation, 0, 4),      // the radio buttons' column: one width, one right edge
                };
            });
            grid.AddVariant(() =>
            {
                _stpSlowKeys.Anchor = AnchorStyles.Left; _stpDwell.Anchor = AnchorStyles.Left;
                return new[]
                {
                    new AdaptiveTable.Cell(_optTimingOff, 0, 0, 3),
                    new AdaptiveTable.Cell(_optSlowKeys, 0, 1, 3), new AdaptiveTable.Cell(_stpSlowKeys, 0, 2), new AdaptiveTable.Cell(unitSlow, 1, 2),
                    new AdaptiveTable.Cell(_optDwell, 0, 3, 3),    new AdaptiveTable.Cell(_stpDwell, 0, 4),    new AdaptiveTable.Cell(unitDwell, 1, 4),
                    new AdaptiveTable.Cell(sep, 0, 5, 3),
                    new AdaptiveTable.Cell(_chkTimingAnimation, 0, 6, 3),
                };
            });

            foreach (var r in new[] { _optTimingOff, _optSlowKeys, _optDwell })
                r.CheckedChanged += (s, e) => { if (((TouchRadioButton)s).Checked) OnTimingModeChanged(); };

            _chkCornerLabels = NewCheck(() => Lang.T("Show Shift and AltGr labels"));
            SetTip(_chkCornerLabels, () => Lang.T("tip: Show Shift and AltGr labels"));

            var group = MakeGroup(() => Lang.T("kbd: Timing aid"), grid);
            AddWideRow(t, OptionStack(_chkStickyMods, _chkHoldToEdit, group, _chkCornerLabels), fill: false);
        }

        private TouchStepper NewTimingStepper(int min, int max, int step, int value, string nameKey)
        {
            var st = new TouchStepper { Minimum = min, Maximum = max, Increment = step, Value = value, Margin = new Padding(0, 4, 0, 4) };
            st.AccessibleName = Lang.StripMnemonic(Lang.T(nameKey));
            st.AccessibleDescription = Lang.T("milliseconds");
            st.ValueBox.AccessibleDescription = st.AccessibleDescription;
            return st;
        }

        /// <summary>The timing aid chosen by the radio group.</summary>
        private enum TimingMode { Off, SlowKeys, Dwell }
        private TimingMode CurrentTimingMode => _optSlowKeys.Checked ? TimingMode.SlowKeys : _optDwell.Checked ? TimingMode.Dwell : TimingMode.Off;

        /// <summary>The animation applies while a timing aid is on; the steppers belong to the chosen option only.</summary>
        private bool TimingAvailable() => CurrentTimingMode != TimingMode.Off;

        private void OnTimingModeChanged()
        {
            _stpSlowKeys.Enabled = CurrentTimingMode == TimingMode.SlowKeys;
            _stpDwell.Enabled    = CurrentTimingMode == TimingMode.Dwell;
            _chkTimingAnimation.Enabled = TimingAvailable();      // its value is kept while it is disabled
        }

        // ── Section 3: Word prediction ──────────────────────────────

        private void BuildWordPrediction(TableLayoutPanel section)
        {
            // Side by side in two equal halves; on a narrow window the candidates go under the settings.
            var cols = new AdaptiveTable { ColumnCount = 2 };
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var settings = BuildWordSettings();
            var candidates = BuildCandidates();
            cols.AddVariant(() => new[] { new AdaptiveTable.Cell(settings, 0, 0), new AdaptiveTable.Cell(candidates, 1, 0) });
            cols.AddVariant(() => new[] { new AdaptiveTable.Cell(settings, 0, 0, 2), new AdaptiveTable.Cell(candidates, 0, 1, 2) });
            AddWideRow(section, cols);
        }

        private Control BuildWordSettings()
        {
            var t = NewTable();
            t.Padding = new Padding(0, 0, Touch.Gap * 2, 0);
            t.Dock = DockStyle.None; t.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _chkWPLearning = NewCheck(() => "&" + Lang.T("wp: Remember typed words"));
            SetTip(_chkWPLearning, () => Lang.T("wp: tip remember"));
            _chkWPLearning.CheckedChanged += (s, e) => { UpdateWPInfo(); UpdateCandidateState(); };
            AddWideRow(t, _chkWPLearning);                       // fills the column: same right edge as the chooser and Export

            _cmbWPDatabase = new TouchChoiceButton { RowHeight = 44 };
            SetTip(_cmbWPDatabase, () => Lang.T("wp: tip database"));
            _err.SetIconPadding(_cmbWPDatabase, -54); _fontWarn.SetIconPadding(_cmbWPDatabase, -54);
            _cmbWPDatabase.SelectedIndexChanged += (s, e) => { if (!_loading) { UpdateDatabaseWarning(); UpdateWPInfo(); } };
            _cmbWPDatabase.AccessibleName = Lang.StripMnemonic(Lang.T("wp: Database"));
            // The label sits above the chooser (not beside it): a long translation or a long file name then gets the whole column.
            var dbLabel = new AccelLabel { Text = Lang.T("wp: Database"), AutoSize = true, Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 0) };
            _transLabels.Add((dbLabel, () => Lang.T("wp: Database")));
            dbLabel.SetTargets(_cmbWPDatabase);
            AddWideRow(t, dbLabel, fill: false);
            AddWideRow(t, _cmbWPDatabase);

            _lblWPInfo = HintLabel(2);
            AddWideRow(t, _lblWPInfo, fill: false);
            _lblExportHint = HintLabel(1);
            AddWideRow(t, _lblExportHint, fill: false);

            _btnWPExport = MakeTouchButton(() => Lang.T("wp: Export…"));
            SetTip(_btnWPExport, () => Lang.T("wp: tip export"));
            _btnWPExport.Click += (s, e) => ExportLearnedWords();
            AddWideRow(t, _btnWPExport);

            _btnWPImport = MakeTouchButton(() => Lang.T("wp: Import…"));
            SetTip(_btnWPImport, () => Lang.T("wp: tip import"));
            _btnWPImport.Click += (s, e) => ImportLearnedWords();
            AddWideRow(t, _btnWPImport);
            return t;
        }

        private Control BuildCandidates()
        {
            var t = NewTable();
            t.Padding = Padding.Empty;
            t.Dock = DockStyle.None; t.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var candidatesHeading = Heading(() => Lang.T("wp: Candidates"));
            AddWideRow(t, candidatesHeading, fill: false);

            _lstWPCandidates = new TouchList { Dock = DockStyle.Fill, AccessibleName = Lang.StripMnemonic(Lang.T("wp: Candidates")), TabIndex = 0 };
            candidatesHeading.SetTargets(_lstWPCandidates);
            _candidateFrame = new Panel
            {
                Padding = new Padding(1), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4),
                MinimumSize = new Size(0, 4 * Touch.Target + 2), Height = 4 * Touch.Target + 2, Tag = "notheme",
                BackColor = _dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder,
            };
            _candidateFrame.Controls.Add(_lstWPCandidates);
            _lstWPCandidates.GotFocus  += (s, e) => _candidateFrame.BackColor = _dark ? Fluent.DialogDarkText : Fluent.Accent;
            _lstWPCandidates.LostFocus += (s, e) => _candidateFrame.BackColor = _dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder;
            _lstWPCandidates.SelectedIndexChanged += (s, e) => UpdateCandidateState();
            SetTip(_lstWPCandidates, () => Lang.T("wp: tip candidates"));
            AddWideRow(t, _candidateFrame);

            // Promote / Reject: two equal halves under the list, so the list and the buttons end at the same edge.
            _btnWPPromote = MakeTouchButton(() => Lang.T("wp: Promote"));
            _btnWPReject  = MakeTouchButton(() => Lang.T("wp: Reject"));
            foreach (var b in new[] { _btnWPPromote, _btnWPReject }) { b.Dock = DockStyle.Fill; b.MinimumSize = new Size(0, Touch.Target); b.Margin = new Padding(0, 0, 0, 0); }
            _btnWPPromote.Margin = new Padding(0, 0, Touch.Gap / 2, 0); _btnWPReject.Margin = new Padding(Touch.Gap / 2, 0, 0, 0);
            SetTip(_btnWPPromote, () => Lang.T("wp: tip promote"));
            SetTip(_btnWPReject,  () => Lang.T("wp: tip reject"));
            _btnWPPromote.Click += (s, e) => PromoteSelected();
            _btnWPReject.Click  += (s, e) => RejectSelected();
            var halves = new AdaptiveTable { ColumnCount = 2 };
            halves.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            halves.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            halves.AddVariant(() =>
            {
                _btnWPPromote.Margin = new Padding(0, 0, Touch.Gap / 2, 0); _btnWPReject.Margin = new Padding(Touch.Gap / 2, 0, 0, 0);
                return new[] { new AdaptiveTable.Cell(_btnWPPromote, 0, 0), new AdaptiveTable.Cell(_btnWPReject, 1, 0) };
            });
            halves.AddVariant(() =>
            {
                _btnWPPromote.Margin = new Padding(0, 0, 0, Touch.Gap / 2); _btnWPReject.Margin = Padding.Empty;
                return new[] { new AdaptiveTable.Cell(_btnWPPromote, 0, 0, 2), new AdaptiveTable.Cell(_btnWPReject, 0, 1, 2) };
            });
            AddWideRow(t, halves);

            _lblImmediateHint = HintLabel(2);
            AddWideRow(t, _lblImmediateHint, fill: false);
            return t;
        }

        /// <summary>A hint line that is always there (empty when there is nothing to say), with its height reserved, so the window is measured with it.</summary>
        private Label HintLabel(int lines) => Wrap(new Label
        {
            AutoSize = true, UseMnemonic = false, Font = Fluent.FontHint, BackColor = Color.Transparent, ForeColor = Fluent.TextPrimary,
            MaximumSize = new Size(Touch.LabelMaxWidth * 3 / 2 + 60, 0), MinimumSize = new Size(0, Fluent.FontHint.Height * lines + 4),
            Margin = new Padding(0, 4, 0, 4),
        });

        // ── Filling the controls ────────────────────────────────────

        /// <summary>
        /// Shows a set of values in every control. Called once after construction and again after Load. Nothing in here changes the
        /// values: they are shown as they are (a stepper widens its range to hold a loaded value outside the usual one).
        /// </summary>
        private void PopulateFields(VisualTheme t, WindowState ws, LayoutMeta m)
        {
            _loading = true;
            try
            {
                SyncLanguageChooser();
                _cmbToolbarTheme.SetItems(ToolbarThemeItems(), Math.Max(0, Array.IndexOf(ToolbarThemeOrder, m.ToolbarTheme)));

                _stpOpacity.Value = OpacityToStep(t.Opacity);
                _chipBackground.SetOwn(t.BackgroundColor);
                _chkAlwaysOnTop.Checked  = ws.AlwaysOnTop;
                _chkHideTitlebar.Checked = ws.HideTitlebar;

                _chkStickyMods.Checked = m.StickyModifiers;
                _chkHoldToEdit.Checked = m.HoldToEdit;
                WidenStepper(_stpSlowKeys, 100, 3000, m.SlowKeysMs);
                WidenStepper(_stpDwell, 100, 5000, m.DwellMs);
                if (m.SlowKeysMs > 0)      { _stpSlowKeys.Value = m.SlowKeysMs; _optSlowKeys.Checked = true; }   // both above 0: Slow keys (the first option)
                else if (m.DwellMs > 0)    { _stpDwell.Value = m.DwellMs;       _optDwell.Checked = true; }
                else                       _optTimingOff.Checked = true;
                _chkTimingAnimation.Checked = m.ShowTimingAnimation;
                _chkCornerLabels.Checked    = m.ShowCornerLabels;
                OnTimingModeChanged();

                _chkWPLearning.Checked = m.WordLearningEnabled;
                FillDatabaseChooser(m.WordDatabase);
            }
            finally { _loading = false; }
            UpdateDatabaseWarning();
            UpdateWPInfo();
            PopulateCandidates();
            UpdateImmediateHint();
        }

        private static int OpacityToStep(double opacity) =>
            Math.Clamp((int)Math.Round((1.0 - Math.Clamp(opacity, 0.2, 1.0)) * 100), 0, 80);

        private static void WidenStepper(TouchStepper st, int min, int max, int loaded)
        {
            // A loaded value outside the usual range is shown as it is (and stored as it is): the range grows to hold it.
            st.Minimum = loaded > 0 ? Math.Min(min, loaded) : min;
            st.Maximum = Math.Max(max, loaded);
        }

        private void SyncLanguageChooser()
        {
            int i = _languageCodes.IndexOf(Lang.CurrentCode);
            _cmbLanguage.SelectSilently(i >= 0 ? i : (_languageCodes.Count > 0 ? 0 : -1));
        }

        // ── Database chooser, info line, export ─────────────────────

        private void FillDatabaseChooser(string storedName)
        {
            var infos = _backend.Databases();
            var items = new List<TouchChoice> { new TouchChoice { Text = Lang.T("wp: Auto") } };
            _databaseFiles.Clear();
            _databaseFiles.Add("");
            foreach (var db in infos)
            {
                items.Add(new TouchChoice { Text = $"[{(string.IsNullOrEmpty(db.Language) ? "?" : db.Language.ToUpper())}]  {db.DisplayName}" });
                _databaseFiles.Add(Path.GetFileName(db.FilePath));
            }
            int select = 0;
            if (!string.IsNullOrEmpty(storedName))
            {
                select = _databaseFiles.FindIndex(f => string.Equals(f, storedName, StringComparison.OrdinalIgnoreCase));
                if (select < 0)
                {
                    // A stored name that is not found stays selected as an extra row (never rewritten silently).
                    items.Insert(1, new TouchChoice { Text = $"{Lang.T("wp: (not found)")}  {storedName}" });
                    _databaseFiles.Insert(1, storedName);
                    select = 1;
                }
            }
            _cmbWPDatabase.SetItems(items, select);
        }

        private string SelectedDatabaseFile =>
            _cmbWPDatabase.SelectedIndex >= 0 && _cmbWPDatabase.SelectedIndex < _databaseFiles.Count ? _databaseFiles[_cmbWPDatabase.SelectedIndex] : "";

        private bool SelectedDatabaseMissing
        {
            get
            {
                string f = SelectedDatabaseFile;
                return !string.IsNullOrEmpty(f) && !_backend.Databases().Any(d => string.Equals(Path.GetFileName(d.FilePath), f, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void UpdateDatabaseWarning()
        {
            string f = SelectedDatabaseFile;
            _fontWarn.SetError(_cmbWPDatabase, SelectedDatabaseMissing ? string.Format(Lang.T("warn: database not found"), f) : "");
        }

        private static string LangOrUnknown(string lang) => string.IsNullOrEmpty(lang) ? "?" : lang.ToUpper();

        private void UpdateWPInfo()
        {
            if (_lblWPInfo == null) return;
            string kind = _chkWPLearning.Checked ? Lang.T("wp: learning on") : Lang.T("wp: learning off");
            string file = SelectedDatabaseFile;
            if (string.IsNullOrEmpty(file))
            {
                _lblWPInfo.Text = _backend.IsLoaded()
                    ? $"{LangOrUnknown(_backend.LoadedLanguage())}  ·  {_backend.WordCount():N0} {Lang.T("wp: words")}  ·  {kind}"
                    : Lang.T("wp: No database loaded");
            }
            else
            {
                var db = _backend.Databases().FirstOrDefault(d => string.Equals(Path.GetFileName(d.FilePath), file, StringComparison.OrdinalIgnoreCase));
                _lblWPInfo.Text = db != null ? $"{LangOrUnknown(db.Language)}  ·  {kind}" : kind;
            }
            _cmbWPDatabase.AccessibleDescription = _lblWPInfo.Text;
            UpdateExportState();
        }

        /// <summary>Full path of the base database the chooser resolves to: the chosen file, or for "(auto)" the one for the current language.</summary>
        private string SelectedOrAutoBasePath()
        {
            var all = _backend.Databases();
            string file = SelectedDatabaseFile;
            if (!string.IsNullOrEmpty(file))
                return all.FirstOrDefault(d => string.Equals(Path.GetFileName(d.FilePath), file, StringComparison.OrdinalIgnoreCase))?.FilePath;
            var match = !string.IsNullOrEmpty(_sourceMeta.Language)
                ? all.FirstOrDefault(d => string.Equals(d.Language, _sourceMeta.Language, StringComparison.OrdinalIgnoreCase)) : null;
            return (match ?? all.FirstOrDefault())?.FilePath;
        }

        /// <summary>Export is only possible once something has been learned: the overlay file of the chosen database exists.</summary>
        private void UpdateExportState()
        {
            if (_btnWPExport == null) return;
            string basePath = SelectedOrAutoBasePath();
            bool can = basePath != null && _backend.FileExists(_backend.OverlayPath(basePath));
            _btnWPExport.Enabled = can;
            _lblExportHint.Text = can ? "" : Lang.T("wp: nothing to export");
            if (_btnWPImport != null) _btnWPImport.Enabled = basePath != null && !_backend.IsLoading();      // possible without anything learned yet
        }

        private void ExportLearnedWords()
        {
            string basePath = SelectedOrAutoBasePath();
            if (basePath == null) return;
            string overlay = _backend.OverlayPath(basePath);
            if (!_backend.FileExists(overlay)) return;
            using var dlg = new SaveFileDialog
            {
                Title = Lang.T("wp: Export learned words"), Filter = "Word database (*.wfq)|*.wfq|All files (*.*)|*.*",
                DefaultExt = "wfq", FileName = Path.GetFileName(overlay),
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { File.Copy(overlay, dlg.FileName, overwrite: true); }
            catch (Exception ex) { TouchMessage.Info(this, Lang.T("wp: Word prediction"), $"{Lang.T("wp: Export failed")}\n{ex.Message}"); }
        }

        // ── Import of learned words ──────────────────────────────────

        /// <summary>For the tests: answers the confirmation / shows the message instead of a window.</summary>
        internal Func<string, string, bool> ImportConfirm;
        internal Action<string, string>     ImportNotice;

        private void ImportLearnedWords()
        {
            if (SelectedOrAutoBasePath() == null) return;
            using var dlg = new OpenFileDialog
            {
                Title = Lang.T("wp: Import learned words"), Filter = "Word database (*.wfq)|*.wfq|All files (*.*)|*.*",
                DefaultExt = "wfq", CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            ImportLearnedWordsFrom(dlg.FileName);
        }

        /// <summary>
        /// Merges the learned words of <paramref name="file"/> into those of the chosen database, after asking: counts are added together and
        /// nothing learned here is lost (the old file stays as .bak). A file that is not learned words, is unreadable, empty, or of another
        /// language changes nothing and says why.
        /// </summary>
        internal void ImportLearnedWordsFrom(string file)
        {
            string basePath = SelectedOrAutoBasePath();
            if (basePath == null) return;
            string title = Lang.T("wp: Import learned words");
            string question = string.Format(Lang.T("wp: import confirm"), Path.GetFileName(file));
            bool yes = ImportConfirm != null ? ImportConfirm(title, question) : TouchMessage.Confirm(this, title, question);
            if (!yes) return;

            string language = _backend.Databases().FirstOrDefault(d => string.Equals(d.FilePath, basePath, StringComparison.OrdinalIgnoreCase))?.Language;
            var result = _backend.ImportLearned(basePath, language, file);
            if (!result.Ok)
            {
                string reason = result.Error switch
                {
                    "notOverlay"    => Lang.T("wp: import not valid"),
                    "empty"         => Lang.T("wp: import empty"),
                    "corrupt"       => Lang.T("wp: import corrupt"),
                    "missing"       => Lang.T("wp: import missing"),
                    "loading"       => Lang.T("wp: import loading"),
                    "targetCorrupt" => Lang.T("wp: import target corrupt"),
                    "language"      => string.Format(Lang.T("wp: import wrong language"), result.Detail, language ?? "?"),
                    _               => result.Detail ?? "",
                };
                ShowImportNotice(title, $"{Lang.T("wp: Import failed")}\n\n{reason}");
                return;
            }
            _backend.ReloadIfLoaded(basePath);            // the words count at once; the info line and the candidates follow when the load is done
            UpdateWPInfo(); PopulateCandidates();
            ShowImportNotice(title, string.Format(Lang.T("wp: Import done"), result.Words, result.Pairs, result.Candidates));
        }

        private void ShowImportNotice(string title, string text)
        {
            if (ImportNotice != null) ImportNotice(title, text); else TouchMessage.Info(this, title, text);
        }

        // ── Candidates ──────────────────────────────────────────────

        private void PopulateCandidates()
        {
            if (_lstWPCandidates == null) return;
            string previous = SelectedCandidateWord();
            _lstWPCandidates.Items.Clear();
            foreach (var (word, count) in _backend.Candidates()) _lstWPCandidates.Items.Add($"{word} ({count})");
            if (previous != null)
                for (int i = 0; i < _lstWPCandidates.Items.Count; i++)
                    if (_lstWPCandidates.Items[i].ToString().StartsWith(previous + " (", StringComparison.Ordinal)) { _lstWPCandidates.SelectedIndex = i; break; }
            UpdateCandidateState();
        }

        private void UpdateCandidateState()
        {
            if (_lstWPCandidates == null || _btnWPPromote == null) return;
            bool learning = _chkWPLearning != null && _chkWPLearning.Checked;
            bool selected = _lstWPCandidates.SelectedIndex >= 0;
            _lstWPCandidates.Enabled = learning;
            _lstWPCandidates.EmptyText = learning ? Lang.T("wp: no candidates") : Lang.T("wp: learning off hint");
            _btnWPPromote.Enabled = learning && selected;
            _btnWPReject.Enabled  = learning && selected;
        }

        private void UpdateImmediateHint() { if (_lblImmediateHint != null) _lblImmediateHint.Text = Lang.T("wp: immediate"); }

        private string SelectedCandidateWord()
        {
            if (_lstWPCandidates?.SelectedItem is not string s) return null;
            int i = s.LastIndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }

        private void PromoteSelected() { string w = SelectedCandidateWord(); if (w == null) return; _backend.Promote(w); PopulateCandidates(); }
        private void RejectSelected()  { string w = SelectedCandidateWord(); if (w == null) return; _backend.Reject(w);  PopulateCandidates(); }

        // ── Language change while open ──────────────────────────────

        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            Text = Lang.T("Edit Keyboard");
            if (_cmbToolbarTheme == null) return;
            bool was = _loading; _loading = true;
            try
            {
                _cmbToolbarTheme.SetItems(ToolbarThemeItems(), Math.Max(0, _cmbToolbarTheme.SelectedIndex));
                if (_cmbWPDatabase != null && _cmbWPDatabase.Items.Count > 0)
                {
                    _cmbWPDatabase.Items[0].Text = Lang.T("wp: Auto");
                    RelabelMissingDatabaseRow();
                    _cmbWPDatabase.ShowSelection();
                }
                _stpOpacity.AccessibleName = Lang.StripMnemonic(Lang.T("Opacity"));
                _cmbWPDatabase.AccessibleName = Lang.StripMnemonic(Lang.T("wp: Database"));
                _stpSlowKeys.AccessibleName = Lang.StripMnemonic(Lang.T("Slow keys")); _stpSlowKeys.AccessibleDescription = Lang.T("milliseconds");
                _stpDwell.AccessibleName = Lang.StripMnemonic(Lang.T("Dwell click"));  _stpDwell.AccessibleDescription = Lang.T("milliseconds");
                _lstWPCandidates.AccessibleName = Lang.StripMnemonic(Lang.T("wp: Candidates"));
                _chkTimingAnimation.AccessibleDescription = Lang.T("tip: Show timing animation");
            }
            finally { _loading = was; }
            SyncLanguageChooser();
            UpdateDatabaseWarning(); UpdateWPInfo(); UpdateCandidateState(); UpdateImmediateHint();
        }

        private void RelabelMissingDatabaseRow()
        {
            string stored = SelectedDatabaseFile;
            if (!SelectedDatabaseMissing) return;
            _cmbWPDatabase.Items[_cmbWPDatabase.SelectedIndex].Text = $"{Lang.T("wp: (not found)")}  {stored}";
        }

        // ── Apply, Save, Load ───────────────────────────────────────

        /// <summary>
        /// Builds the results and closes with OK. The source objects are cloned and only the edited fields are overwritten, so a
        /// field the dialog does not edit (language, last file, gear position, window size, key style) and any field added to a
        /// model later is never reset. <paramref name="action"/> says whether the caller should save the layout file next.
        /// </summary>
        private bool Apply() => ApplyWith(KeyboardFileAction.None);

        private bool ApplyWith(KeyboardFileAction action)
        {
            if (ShowFirstSectionWithError()) return false;

            ResultTheme = _sourceTheme.Clone();
            int srcStep = OpacityToStep(_sourceTheme.Opacity);
            if ((int)_stpOpacity.Value != srcStep)                         // an untouched value is kept as it is (0.755 stays 0.755)
                ResultTheme.Opacity = Math.Clamp((100 - (int)_stpOpacity.Value) / 100.0, 0.2, 1.0);
            ResultTheme.BackgroundColor = _chipBackground.Value;

            ResultWindow = _sourceWindow.Clone();
            ResultWindow.AlwaysOnTop  = _chkAlwaysOnTop.Checked;
            ResultWindow.HideTitlebar = _chkHideTitlebar.Checked;

            ResultMeta = _sourceMeta.Clone();
            ResultMeta.ToolbarTheme        = ToolbarThemeOrder[Math.Clamp(_cmbToolbarTheme.SelectedIndex, 0, ToolbarThemeOrder.Length - 1)];
            ResultMeta.StickyModifiers     = _chkStickyMods.Checked;
            ResultMeta.HoldToEdit          = _chkHoldToEdit.Checked;
            ResultMeta.SlowKeysMs          = CurrentTimingMode == TimingMode.SlowKeys ? (int)_stpSlowKeys.Value : 0;
            ResultMeta.DwellMs             = CurrentTimingMode == TimingMode.Dwell ? (int)_stpDwell.Value : 0;
            ResultMeta.ShowTimingAnimation = _chkTimingAnimation.Checked;     // kept while its check box is disabled
            ResultMeta.ShowCornerLabels    = _chkCornerLabels.Checked;
            ResultMeta.WordDatabase        = SelectedDatabaseFile;
            ResultMeta.WordLearningEnabled = _chkWPLearning.Checked;

            ResultGroups = _groups;
            FileAction   = action;
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        /// <summary>Load: asks first (it replaces the keyboard at once and Cancel cannot undo it), then refills the dialog from the loaded file.</summary>
        private void LoadLayout()
        {
            if (_onLoad == null) return;
            if (!TouchMessage.Confirm(this, Lang.T("kbd: Load title"), Lang.T("kbd: Load msg"))) return;
            LoadFromCaller();
        }

        /// <summary>The load itself, without the question (tests call this). Returns true when a file was loaded and the dialog was refilled.</summary>
        internal bool LoadFromCaller()
        {
            if (_onLoad == null || !_onLoad()) return false;           // the file dialog was cancelled: nothing changes, the edits stay
            if (_getSettings != null)
            {
                var (t, ws, m) = _getSettings();
                _sourceTheme = t; _sourceWindow = ws; _sourceMeta = m;
            }
            if (_getGroups != null) _groups = _getGroups().Select(g => g.Clone()).ToList();
            ResultGroups = _groups;
            _languageOnOpen = Lang.CurrentCode;                         // the loaded file may have changed the language: that is the new baseline
            PopulateFields(_sourceTheme, _sourceWindow, _sourceMeta);
            return true;
        }

        /// <summary>For tests: Apply with a file action.</summary>
        internal bool ApplyForTest(KeyboardFileAction action = KeyboardFileAction.None) => ApplyWith(action);
    }
}

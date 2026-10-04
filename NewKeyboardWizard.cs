using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    // ══════════════════════════════════════════════════════════════════════
    // NewKeyboardWizard — 5-page wizard that creates a new .kbl layout file.
    // ══════════════════════════════════════════════════════════════════════
    internal sealed class NewKeyboardWizard : FluentDialogBase
    {
        // ── Theme preset data ────────────────────────────────────────────

        internal sealed class ThemePreset
        {
            public string Id;
            public string DisplayName;
            public string Background, KeyColor, FontColor, BorderColor;
            public int    BorderThickness;
            public (string Name, string Key, string Font, string Border, int Thick)[] ExtraGroups;
        }

        internal static readonly ThemePreset[] Presets = new[]
        {
            // ── Dark ─────────────────────────────────────────────────────
            // Standard: dark navy.  Groups: subtle hue shifts — same near-white
            // font throughout.  Besturing uses a muted gray font so control
            // keys read as less prominent than letter keys.
            new ThemePreset { Id="dark",  DisplayName="Dark",
                Background="1C1C28", KeyColor="2C2C42", FontColor="EFEFFF",
                BorderColor="3C3C56", BorderThickness=1,
                ExtraGroups=new[]{
                    ("Klinkers",     "3A3A5A","EFEFFF","4A4A6A",1),   // slightly lighter navy
                    ("Medeklinkers", "2C2C42","EFEFFF","3C3C56",1),   // same as standard
                    ("Cijfers",      "243050","C0D0FF","344060",1),   // cool blue tint
                    ("Besturing",    "1C1C2E","B0B0C8","2A2A3E",1),   // darkest, muted font (~8:1)
                    ("Leestekens",   "38283C","D8C0F0","483848",1),   // slight purple tint
                    ("Woord",        "263826","B0D0B0","364836",1),   // slight green tint
                } },

            // ── Light ────────────────────────────────────────────────────
            // Standard: white keys on light-gray background.  Groups: very
            // subtle tints — the keyboard stays airy but categories are
            // distinguishable on closer inspection.
            new ThemePreset { Id="light", DisplayName="Light",
                Background="EBEBEB", KeyColor="FFFFFF", FontColor="1A1A1A",
                BorderColor="C0C0C0", BorderThickness=1,
                ExtraGroups=new[]{
                    ("Klinkers",     "DFF0FF","1A1A1A","A8C8E0",1),   // very light blue
                    ("Medeklinkers", "FFFFFF","1A1A1A","C0C0C0",1),   // same as standard
                    ("Cijfers",      "FFF3DC","1A1A1A","D8C898",1),   // very light amber
                    ("Besturing",    "EAEAEA","3C3C3C","B0B0B0",1),   // light gray, darker font (~9:1)
                    ("Leestekens",   "F4F0FF","1A1A1A","C4B8D8",1),   // very light lavender
                    ("Woord",        "EAFAEA","1A1A1A","A8C8B0",1),   // very light green
                } },

            // ── High Contrast ────────────────────────────────────────────
            // All keys use yellow-family colours; font and border always black;
            // border always 2 px — as specified by the user.
            new ThemePreset { Id="hc", DisplayName="High Contrast",
                Background="000000", KeyColor="FFFF00", FontColor="000000",
                BorderColor="000000", BorderThickness=2,
                ExtraGroups=new[]{
                    ("Klinkers",     "FFFF00","000000","000000",2),   // pure yellow (same as std)
                    ("Medeklinkers", "FFE535","000000","000000",2),   // slightly deeper yellow
                    ("Cijfers",      "FFD700","000000","000000",2),   // gold
                    ("Besturing",    "FFA500","000000","000000",2),   // amber
                    ("Leestekens",   "FF8C00","000000","000000",2),   // dark amber
                    ("Woord",        "F0E68C","000000","000000",2),   // khaki / pale yellow
                } },

            // ── Colorful ─────────────────────────────────────────────────
            // Based on the azertycolor keyboard; vivid saturated hues.
            new ThemePreset { Id="colorful", DisplayName="Colorful",
                Background="808080", KeyColor="C0C0C0", FontColor="000000",
                BorderColor="808000", BorderThickness=1,
                ExtraGroups=new[]{
                    ("Klinkers",     "4A8FD4","FFFFFF","0080FF",2),
                    ("Medeklinkers", "1A4E8A","FFFFFF","0080FF",2),
                    ("Cijfers",      "B52535","FFFFFF","91201A",2),
                    ("Besturing",    "2A6B35","FFFFFF","003700",2),
                    ("Leestekens",   "C86A00","FFFFFF","804000",2),
                    ("Woord",        "5B3080","E8D4FF","8000FF",0),
                } },
        };

        // ── Pages ────────────────────────────────────────────────────────
        private const int PAGE_START=0, PAGE_GRID=1,
                          PAGE_THEME=2, PAGE_SAVE=3, PAGE_COUNT=4;

        private const int MaxWidth = 900;
        /// <summary>Widest a line of text may be on a page (window minus the padding of frame and page), and inside a group.</summary>
        private const int PageTextWidth = MaxWidth - 4 * Fluent.Pad, GroupTextWidth = PageTextWidth - 36, SummaryTextWidth = GroupTextWidth - 24;
        protected override int ContentMaxWidth => MaxWidth;

        private int _currentPage = PAGE_START;
        private FluentButton _btnCancel, _btnBack, _btnNext, _btnCreate;
        private Label _lblStep;

        // Page 1: starting point
        private TouchRadioButton  _rbBlank, _rbPaste, _rbCopy;
        private TouchTextBox      _txtCopyFile;
        private FluentButton      _btnBrowseCopy;
        private Label             _lblCopyRow, _lblCopyErr;
        private Control           _copyRowInput;
        private TouchChoiceButton _cmbLanguage;

        // Page 2: grid and labels (what shows depends on page 1)
        private TouchStepper      _stpRows, _stpCols;
        private Label             _lblPasteSize, _lblCopyInfo, _lblPasteErr;
        private TextBox           _txtPaste;
        private WizardGridPreview _previewBlank, _previewPaste;
        private int _gridRows = 4, _gridCols = 8;

        // Page 3: theme
        private TouchTile[]      _tiles;
        private TouchTile        _tileFile;
        private TouchTextBox     _txtThemeFile;
        private FluentButton     _btnBrowseTheme;
        private Label            _lblThemeFile, _lblThemeErr;
        private Control          _themeFileInput;
        private SampleStrip      _strip;
        private int              _selectedPreset = 0;

        // Page 4: save
        private TouchTextBox _txtFileName, _txtFolder;
        private FluentButton _btnBrowseFolder;
        private Label        _lblSaveError;
        private Label[]      _sumLines;

        // ── Result ───────────────────────────────────────────────────────
        public string CreatedFilePath { get; private set; }

        // ── Constructor ──────────────────────────────────────────────────
        public NewKeyboardWizard()
        {
            Text            = Lang.T("New Keyboard");
            FormBorderStyle = FormBorderStyle.FixedDialog;

            BuildUI();
            // Everything is built visible, so the window is measured for the tallest case (copy row, paste box and blank
            // steppers together); the choice made on page 1 decides what is shown once the window has its size.
            Load += (s, e) => { ApplyStartMode(); ApplyThemeMode(); RefreshGrid(); ShowPage(PAGE_START); };
        }

        protected override void ApplyTheme()
        {
            base.ApplyTheme();
            // The theme pass makes every label plain text colour; the error lines keep theirs.
            Color err = _dark ? Fluent.DialogDarkDanger : Fluent.Danger;
            foreach (var l in new[] { _lblCopyErr, _lblPasteErr, _lblThemeErr, _lblSaveError })
                if (l != null) l.ForeColor = err;
        }

        // ── Build ────────────────────────────────────────────────────────

        private void BuildUI()
        {
            _lblStep = new Label { AutoSize = true, Font = Fluent.FontLabel, UseMnemonic = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, Touch.Gap, 0) };
            _btnCancel = MakeTouchButton(() => Lang.T("Cancel"));
            _btnBack   = MakeTouchButton(() => Lang.T("← Back"));
            _btnNext   = MakeTouchButton(() => Lang.T("Next →"), FluentButton.Variant.Primary);
            _btnCreate = MakeTouchButton(() => Lang.T("Create"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(_lblStep, _btnCancel, _btnBack, _btnNext, _btnCreate), withSections: false);
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnBack.Click   += (s, e) => Navigate(-1);
            _btnNext.Click   += (s, e) => Navigate(+1);
            _btnCreate.Click += (s, e) => TryCreate();
            CancelButton = _btnCancel;
            AcceptButton = _btnNext;
            EqualiseFooter();

            BuildStartPage(AddSection(() => Lang.T("wiz: p1 title")));
            BuildGridPages(AddSection(() => Lang.T("wiz: p2 title")), AddSection(() => Lang.T("wiz: p2 title")), AddSection(() => Lang.T("wiz: p2 title")));
            BuildThemePage(AddSection(() => Lang.T("wiz: p4 title")));
            BuildSavePage(AddSection(() => Lang.T("wiz: p5 title")));
        }

        /// <summary>The footer buttons side by side get one width, the widest of them (alignment rule D23); re-run when the language changes.</summary>
        private void EqualiseFooter()
        {
            var all = new[] { _btnCancel, _btnBack, _btnNext, _btnCreate };
            foreach (var b in all) b.MinimumSize = new Size(120, Touch.Target);
            int w = 120;
            foreach (var b in all) w = Math.Max(w, b.GetPreferredSize(Size.Empty).Width);
            foreach (var b in all) b.MinimumSize = new Size(w, Touch.Target);
        }

        private Label Hint(Func<string> text, int indent = 0)
        {
            var l = new Label
            {
                Text = text(), AutoSize = true, UseMnemonic = false, MaximumSize = new Size(indent > 0 ? GroupTextWidth : PageTextWidth, 0),
                Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
                Padding = new Padding(indent, 0, 0, 0), AccessibleName = text(),
            };
            _transLabels.Add((l, () => { string s = text(); l.AccessibleName = s; return s; }));
            return l;
        }

        private Label ErrorLine()
        {
            return new Label
            {
                Text = "", AutoSize = true, UseMnemonic = false, MaximumSize = new Size(PageTextWidth, 0),
                MinimumSize = new Size(0, 24), Font = Fluent.FontLabel, BackColor = Color.Transparent,
                ForeColor = _dark ? Fluent.DialogDarkDanger : Fluent.Danger, AccessibleRole = AccessibleRole.Alert,
            };
        }

        private void PageTitle(TableLayoutPanel t, Func<string> title, Func<string> sub)
        {
            AddWideRow(t, Heading(title));
            AddWideRow(t, Hint(sub));
        }

        /// <summary>A text box with a 44 px browse button to its right; the box fills the row.</summary>
        private TableLayoutPanel BrowseRow(TouchTextBox box, out FluentButton browse, Action onClick, Func<string> tip)
        {
            var b = new FluentButton
            {
                Text = "…", Style = FluentButton.Variant.Neutral, TabStop = true, AutoSize = false,
                Size = new Size(Touch.Target, Touch.Target), MinimumSize = new Size(Touch.Target, Touch.Target),
                Margin = new Padding(Touch.Gap, 0, 0, 0), AccessibleName = Lang.T("wiz: Browse"),
            };
            b.Click += (s, e) => onClick();
            SetTip(box, tip); SetTip(b, tip);
            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            box.Dock = DockStyle.Fill; box.Margin = Padding.Empty; box.MinimumSize = new Size(Touch.InputMinWidth, Touch.Target);
            row.Controls.Add(box, 0, 0);
            row.Controls.Add(b, 1, 0);
            browse = b;
            return row;
        }

        // ── Page 1: starting point ───────────────────────────────────────

        private void BuildStartPage(TableLayoutPanel t)
        {
            PageTitle(t, () => Lang.T("wiz: p1 title"), () => Lang.T("wiz: p1 sub"));

            _rbBlank = NewRadio(() => Lang.T("wiz: Blank grid"));
            _rbPaste = NewRadio(() => Lang.T("wiz: Paste labels"));
            _rbCopy  = NewRadio(() => Lang.T("wiz: Copy from file"));
            _rbBlank.Checked = true;
            var stack = OptionStack(
                _rbBlank, Hint(() => Lang.T("wiz: tip Blank grid"), 38),
                _rbPaste, Hint(() => Lang.T("wiz: tip Paste labels"), 38),
                _rbCopy,  Hint(() => Lang.T("wiz: tip Copy from file"), 38));
            AddWideRow(t, MakeGroup(() => Lang.T("wiz: Starting point"), stack));

            _txtCopyFile = new TouchTextBox();
            _copyRowInput = BrowseRow(_txtCopyFile, out _btnBrowseCopy, () =>
            {
                using var dlg = new OpenFileDialog { Title = Lang.T("wiz: Select layout"), Filter = "Keyboard layouts (*.kbl)|*.kbl|All files (*.*)|*.*" };
                if (dlg.ShowDialog(this) == DialogResult.OK) _txtCopyFile.Text = dlg.FileName;
            }, () => Lang.T("tip: Browse layout"));
            _lblCopyRow = AddRow(t, () => Lang.T("wiz: Layout file"), _copyRowInput);
            _lblCopyErr = ErrorLine();
            AddWideRow(t, _lblCopyErr);
            _txtCopyFile.TextChanged += (s, e) => _lblCopyErr.Text = "";

            // The language of the new keyboard's labels and tips (it also decides how pasted words such as "Space" are read).
            _cmbLanguage = new TouchChoiceButton { RowHeight = 44, AutoSize = false, Size = new Size(280, Touch.Target), MinimumSize = new Size(280, Touch.Target) };
            _cmbLanguage.SetItems(new[] { new TouchChoice { Text = "English (en)" }, new TouchChoice { Text = "Nederlands (nl)" } }, Lang.CurrentCode == "nl" ? 1 : 0);
            AddRow(t, () => Lang.T("wiz: Language"), _cmbLanguage, fill: false);
            SetTip(_cmbLanguage, () => Lang.T("tip: Language"));

            _rbBlank.CheckedChanged += (s, e) => ApplyStartMode();
            _rbPaste.CheckedChanged += (s, e) => ApplyStartMode();
            _rbCopy.CheckedChanged  += (s, e) => ApplyStartMode();
        }

        private void ApplyStartMode()
        {
            bool copy = _rbCopy.Checked;
            _lblCopyRow.Visible = _copyRowInput.Visible = _lblCopyErr.Visible = copy;
            if (!copy) _lblCopyErr.Text = "";
        }

        // ── Page 2: grid and labels (three pages, one per start mode) ────
        // Each start mode has its own page, so the window is as tall as the tallest of them, not the three added together.

        private void BuildGridPages(TableLayoutPanel blank, TableLayoutPanel paste, TableLayoutPanel copy)
        {
            // Blank: the size, with steppers, and the preview.
            PageTitle(blank, () => Lang.T("wiz: p2 title"), () => Lang.T("wiz: p2 sub blank"));
            _stpRows = new TouchStepper { Minimum = 1, Maximum = 30, Value = 4 };
            _stpCols = new TouchStepper { Minimum = 1, Maximum = 60, Value = 8 };
            AddRow(blank, () => Lang.T("wiz: Rows"),    _stpRows, fill: false);
            AddRow(blank, () => Lang.T("wiz: Columns"), _stpCols, fill: false);
            _stpRows.ValueChanged += (s, e) => RefreshGrid();
            _stpCols.ValueChanged += (s, e) => RefreshGrid();
            _previewBlank = new WizardGridPreview { Dock = DockStyle.Fill };
            AddWideRow(blank, MakeGroup(() => Lang.T("wiz: Preview"), _previewBlank));

            // Paste: the hint, the box, the size that follows from it, and the preview with the labels.
            PageTitle(paste, () => Lang.T("wiz: p2 title"), () => Lang.T("wiz: p2 sub paste"));
            AddWideRow(paste, Hint(() => Lang.T("wiz: paste hint 2")));
            AddWideRow(paste, Hint(() => Lang.T("wiz: paste hint 3")));
            _txtPaste = new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AcceptsTab = false,
                BorderStyle = BorderStyle.FixedSingle, Font = Fluent.FontInput, AccessibleName = Lang.StripMnemonic(Lang.T("wiz: Key labels")),
            };
            AddWideRow(paste, _txtPaste);
            _txtPaste.MinimumSize = new Size(Touch.InputMinWidth, 100);
            _txtPaste.Height = 100;
            SetTip(_txtPaste, () => Lang.T("wiz: tip paste"));
            _lblPasteSize = Hint(() => PasteSizeText());
            AddWideRow(paste, _lblPasteSize);
            _lblPasteErr = ErrorLine();
            AddWideRow(paste, _lblPasteErr);
            _txtPaste.TextChanged += (s, e) => { _lblPasteErr.Text = ""; RefreshGrid(); };
            _previewPaste = new WizardGridPreview { Dock = DockStyle.Fill };
            AddWideRow(paste, MakeGroup(() => Lang.T("wiz: Preview"), _previewPaste));

            // Copy: one line.
            PageTitle(copy, () => Lang.T("wiz: p2 title"), () => Lang.T("wiz: p2 sub copy"));
            _lblCopyInfo = Hint(() => CopyInfoText());
            AddWideRow(copy, _lblCopyInfo);
        }

        private string CopyInfoText() =>
            _txtCopyFile != null && File.Exists(_txtCopyFile.Text)
                ? string.Format(Lang.T("wiz: copy info"), Path.GetFileName(_txtCopyFile.Text)) : "";

        private string PasteSizeText()
        {
            if (_txtPaste == null) return "";
            var rows = WizardKeyParser.Parse(_txtPaste.Text, IsDutch());
            if (rows.Count == 0) return Lang.T("wiz: paste nothing");
            int cols = 0;
            foreach (var r in rows) cols = Math.Max(cols, r.Count);
            return string.Format(Lang.T("wiz: paste size"), rows.Count, cols);
        }

        /// <summary>Redraws the previews and the texts that depend on what was typed; remembers the size the summary reports.</summary>
        private void RefreshGrid()
        {
            if (_previewPaste == null || _previewBlank == null || _lblCopyInfo == null) return;
            _previewBlank.ShowBlank((int)_stpRows.Value, (int)_stpCols.Value);
            _previewPaste.ShowParsed(WizardKeyParser.Parse(_txtPaste.Text, IsDutch()));
            _lblPasteSize.Text = PasteSizeText();
            _lblCopyInfo.Text  = CopyInfoText();
            if (_rbPaste.Checked) { _gridRows = _previewPaste.GridRows; _gridCols = _previewPaste.GridCols; }
            else                  { _gridRows = (int)_stpRows.Value;    _gridCols = (int)_stpCols.Value; }
        }

        // ── Page 3: theme ────────────────────────────────────────────────

        private void BuildThemePage(TableLayoutPanel t)
        {
            PageTitle(t, () => Lang.T("wiz: p4 title"), () => Lang.T("wiz: p4 sub"));

            var tiles = new TableLayoutPanel { ColumnCount = Presets.Length + 1, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 0, 0, Touch.Gap) };
            _tiles = new TouchTile[Presets.Length];
            for (int i = 0; i <= Presets.Length; i++)
            {
                tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (Presets.Length + 1)));
                bool file = i == Presets.Length;
                var tile = new TouchTile { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i == Presets.Length ? 0 : Touch.Gap, 0) };
                if (!file)
                {
                    var p = Presets[i];
                    tile.Text = Lang.T("wiz: theme " + p.Id);
                    tile.Swatches = new[] { ParseColor(p.KeyColor, Color.Gray), ParseColor(p.ExtraGroups[0].Key, Color.Gray), ParseColor(p.ExtraGroups[2].Key, Color.Gray) };
                    _tiles[i] = tile;
                }
                else { tile.Text = Lang.T("wiz: From file…"); _tileFile = tile; }
                int idx = file ? -1 : i;
                tile.CheckedChanged += (s, e) => { if (((TouchTile)s).Checked) SelectPreset(idx); };
                tile.TabIndex = i;
                tiles.Controls.Add(tile, i, 0);
            }
            _tiles[0].Checked = true;
            AddWideRow(t, tiles);

            _txtThemeFile = new TouchTextBox();
            _themeFileInput = BrowseRow(_txtThemeFile, out _btnBrowseTheme, () =>
            {
                using var dlg = new OpenFileDialog { Title = Lang.T("wiz: Select theme file"), Filter = "Keyboard layouts (*.kbl)|*.kbl|All files (*.*)|*.*" };
                if (dlg.ShowDialog(this) == DialogResult.OK) _txtThemeFile.Text = dlg.FileName;
            }, () => Lang.T("tip: Browse layout"));
            _lblThemeFile = AddRow(t, () => Lang.T("wiz: Theme file"), _themeFileInput);
            _lblThemeErr = ErrorLine();
            AddWideRow(t, _lblThemeErr);
            // Repaint the sample keys once a theme file is chosen or typed.
            _txtThemeFile.TextChanged += (s, e) => { _lblThemeErr.Text = ""; _strip?.Invalidate(); };

            _strip = new SampleStrip(this) { Dock = DockStyle.Fill, Height = 100, MinimumSize = new Size(0, 100) };
            AddWideRow(t, MakeGroup(() => Lang.T("wiz: Sample keys"), _strip));
        }

        private void SelectPreset(int idx)
        {
            _selectedPreset = idx;
            ApplyThemeMode();
            _strip?.Invalidate();
        }

        private void ApplyThemeMode()
        {
            if (_lblThemeFile == null || _lblThemeErr == null) return;     // still building the page
            bool file = _selectedPreset == -1;
            _lblThemeFile.Visible = _themeFileInput.Visible = _lblThemeErr.Visible = file;
            if (!file) _lblThemeErr.Text = "";
        }

        /// <summary>The ten sample keys, one per group, in the colours of the chosen theme.</summary>
        private sealed class SampleStrip : Panel
        {
            private readonly NewKeyboardWizard _w;
            public SampleStrip(NewKeyboardWizard w)
            {
                _w = w; DoubleBuffered = true;
                AccessibleRole = AccessibleRole.Graphic;
            }
            protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); _w.PaintSamples(e.Graphics, ClientSize); }
        }

        private static readonly (string Label, string Group)[] SampleKeys =
        {
            ("a", "Klinkers"), ("e", "Klinkers"), ("b", "Medeklinkers"), ("n", "Medeklinkers"), ("1", "Cijfers"),
            ("↵", "Besturing"), ("⌫", "Besturing"), (".", "Leestekens"), ("abc", "Woord"), ("⚙", null),
        };

        private void PaintSamples(Graphics g, Size size)
        {
            Color bg = GetPreviewBgColor(), borC = GetPreviewBorderColor();
            int borT = GetPreviewBorderThick();
            g.Clear(bg);
            if (_strip != null) _strip.AccessibleName = Lang.T("wiz: Sample keys") + ": " + ThemeDisplayName();

            int n = SampleKeys.Length, gap = 4, kh = Math.Min(56, size.Height - 16);
            int kw = Math.Max(34, Math.Min(72, (size.Width - 16 - (n - 1) * gap) / n));
            int totalW = n * kw + (n - 1) * gap;
            int ox = Math.Max(4, (size.Width - totalW) / 2), oy = (size.Height - kh) / 2;
            using var borPen = borT > 0 ? new Pen(borC, borT) : null;
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle(ox + i * (kw + gap), oy, kw, kh);
                using (var b = new SolidBrush(GetGroupKeyColor(SampleKeys[i].Group))) g.FillRectangle(b, r);
                // The border lies inside the key, the same width on all four sides (a pen on the edge put the right and
                // bottom lines one pixel outside the fill).
                if (borPen != null) g.DrawRectangle(borPen, r.X + borT / 2f, r.Y + borT / 2f, r.Width - borT, r.Height - borT);
                TextRenderer.DrawText(g, SampleKeys[i].Label, Fluent.FontLabel, r, GetGroupFontColor(SampleKeys[i].Group),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        // ── Page 4: save ─────────────────────────────────────────────────

        private void BuildSavePage(TableLayoutPanel t)
        {
            PageTitle(t, () => Lang.T("wiz: p5 title"), () => Lang.T("wiz: p5 sub"));

            _txtFileName = new TouchTextBox { Text = Lang.T("wiz: default filename") };
            AddRow(t, () => Lang.T("wiz: File name"), _txtFileName);
            _txtFolder = new TouchTextBox { Text = DefaultFolder() };
            var folderRow = BrowseRow(_txtFolder, out _btnBrowseFolder, () =>
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = _txtFolder.Text };
                if (dlg.ShowDialog(this) == DialogResult.OK) _txtFolder.Text = dlg.SelectedPath;
            }, () => Lang.T("wiz: tip folder"));
            AddRow(t, () => Lang.T("wiz: Folder"), folderRow);
            _lblSaveError = ErrorLine();
            AddWideRow(t, _lblSaveError);
            _txtFileName.TextChanged += (s, e) => _lblSaveError.Text = "";
            _txtFolder.TextChanged   += (s, e) => _lblSaveError.Text = "";

            _sumLines = new Label[4];
            var sum = new TableLayoutPanel { ColumnCount = 1, RowCount = _sumLines.Length, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            sum.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < _sumLines.Length; i++)
            {
                _sumLines[i] = new Label { AutoSize = true, UseMnemonic = false, Font = Fluent.FontLabel, BackColor = Color.Transparent,
                    MaximumSize = new Size(SummaryTextWidth, 0), Margin = new Padding(0, 4, 0, 4) };
                sum.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                sum.Controls.Add(_sumLines[i], 0, i);
            }
            AddWideRow(t, MakeGroup(() => Lang.T("wiz: Summary"), sum));
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (_sumLines == null) return;
            _sumLines[0].Text = _rbCopy.Checked
                ? string.Format(Lang.T("wiz: sum copy"), Path.GetFileName(_txtCopyFile.Text))
                : string.Format(Lang.T("wiz: sum rows cols"), _gridRows, _gridCols + 1);
            _sumLines[1].Text = string.Format(Lang.T("wiz: sum theme"),    ThemeDisplayName());
            _sumLines[2].Text = string.Format(Lang.T("wiz: sum language"), LanguageCode());
            _sumLines[3].Text = Lang.T("wiz: sum window");
        }

        // ── Navigation ───────────────────────────────────────────────────

        // Host sections: 0 start, 1 grid (blank), 2 grid (paste), 3 grid (copy), 4 theme, 5 save. The wizard has four pages;
        // page 2 is one of the three grid sections, whichever start mode page 1 chose.
        private const int SEC_GRID_BLANK = 1, SEC_THEME = 4, SEC_SAVE = 5;

        private int SectionOfPage(int page) =>
            page == PAGE_GRID ? SEC_GRID_BLANK + (_rbPaste.Checked ? 1 : _rbCopy.Checked ? 2 : 0)
            : page == PAGE_THEME ? SEC_THEME : page == PAGE_SAVE ? SEC_SAVE : 0;

        private static int PageOfSection(int section) => section == 0 ? PAGE_START : section < SEC_THEME ? PAGE_GRID : section == SEC_THEME ? PAGE_THEME : PAGE_SAVE;

        /// <summary>Index of the host section on show.</summary>
        internal int CurrentSection { get; private set; }

        internal override void ShowSectionForGuard(int index) => ShowSection(index);

        private void ShowPage(int index) => ShowSection(SectionOfPage(index));

        private void ShowSection(int section)
        {
            int index = PageOfSection(section);
            _currentPage = index;
            CurrentSection = section;
            ShowHostSection(section);
            _btnBack.Visible   = index > 0;
            _btnNext.Visible   = index < PAGE_COUNT - 1;
            _btnCreate.Visible = index == PAGE_COUNT - 1;
            AcceptButton = index == PAGE_COUNT - 1 ? _btnCreate : _btnNext;
            _lblStep.Text = string.Format(Lang.T("wiz: Step {0} of {1}"), index + 1, PAGE_COUNT);

            if (index == PAGE_GRID) RefreshGrid();
            if (index == PAGE_SAVE) { _lblSaveError.Text = ""; UpdateSummary(); }

            Control focus = index switch
            {
                PAGE_START => _rbCopy.Checked ? _rbCopy : _rbPaste.Checked ? _rbPaste : _rbBlank,
                PAGE_GRID  => section == SEC_GRID_BLANK + 1 ? _txtPaste : section == SEC_GRID_BLANK + 2 ? _btnNext : (Control)_stpRows,
                PAGE_THEME => _selectedPreset == -1 ? _tileFile : _tiles[Math.Max(0, _selectedPreset)],
                _          => _txtFileName,
            };
            if (IsHandleCreated) focus.Focus();
        }

        private void Navigate(int delta)
        {
            int next = _currentPage + delta;
            if (next < 0 || next >= PAGE_COUNT) return;
            if (delta > 0 && !ValidatePage(_currentPage)) return;
            ShowPage(next);
        }

        /// <summary>Checks the page the user is leaving; the reason appears under the field, which gets the focus.</summary>
        private bool ValidatePage(int page)
        {
            if (page == PAGE_START && _rbCopy.Checked &&
                (string.IsNullOrWhiteSpace(_txtCopyFile.Text) || !File.Exists(_txtCopyFile.Text)))
            { _lblCopyErr.Text = Lang.T("wiz: err no copy file"); _txtCopyFile.Focus(); return false; }

            if (page == PAGE_GRID && _rbPaste.Checked &&
                WizardKeyParser.Parse(_txtPaste.Text, IsDutch()).Count == 0)
            { _lblPasteErr.Text = Lang.T("wiz: err empty paste"); _txtPaste.Focus(); return false; }

            if (page == PAGE_THEME && _selectedPreset == -1 &&
                (string.IsNullOrWhiteSpace(_txtThemeFile.Text) || !File.Exists(_txtThemeFile.Text)))
            { _lblThemeErr.Text = Lang.T("wiz: err no theme file"); _txtThemeFile.Focus(); return false; }

            return true;
        }

        private Color GetGroupKeyColor(string groupName)
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
            {
                if (groupName!=null)
                    foreach (var (name,key,font,border,thick) in Presets[_selectedPreset].ExtraGroups)
                        if (name==groupName) return ParseColor(key, GetPreviewKeyColor());
                return GetPreviewKeyColor();
            }
            if (_selectedPreset==-1&&File.Exists(_txtThemeFile?.Text??""))
                return LoadThemeGroupColor(_txtThemeFile.Text,groupName,"KeyColor",Color.DimGray);
            return Color.DimGray;
        }

        private Color GetGroupFontColor(string groupName)
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
            {
                if (groupName!=null)
                    foreach (var (name,key,font,border,thick) in Presets[_selectedPreset].ExtraGroups)
                        if (name==groupName) return ParseColor(font, GetPreviewFontColor());
                return GetPreviewFontColor();
            }
            if (_selectedPreset==-1&&File.Exists(_txtThemeFile?.Text??""))
                return LoadThemeGroupColor(_txtThemeFile.Text,groupName,"FontColor",Color.White);
            return Color.White;
        }


        // ── Theme preview helpers ─────────────────────────────────────────
        private Color GetPreviewBgColor()
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
                return ParseColor(Presets[_selectedPreset].Background, Color.Gray);
            if (_selectedPreset==-1&&File.Exists(_txtThemeFile?.Text??""))
                return LoadThemeColor(_txtThemeFile.Text,"BackgroundColor",Color.Gray);
            return _dark ? Color.FromArgb(28,28,40) : Color.FromArgb(235,235,235);
        }
        private Color GetPreviewKeyColor()
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
                return ParseColor(Presets[_selectedPreset].KeyColor, Color.DimGray);
            if (_selectedPreset==-1&&File.Exists(_txtThemeFile?.Text??""))
                return LoadThemeColor(_txtThemeFile.Text,"KeyColor",Color.DimGray);
            return Color.DimGray;
        }
        private Color GetPreviewFontColor()
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
                return ParseColor(Presets[_selectedPreset].FontColor, Color.White);
            if (_selectedPreset==-1&&File.Exists(_txtThemeFile?.Text??""))
                return LoadThemeColor(_txtThemeFile.Text,"FontColor",Color.White);
            return Color.White;
        }
        private Color GetPreviewBorderColor()
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
                return ParseColor(Presets[_selectedPreset].BorderColor, Color.Gray);
            return Color.Gray;
        }
        private int GetPreviewBorderThick()
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
                return Presets[_selectedPreset].BorderThickness;
            return 1;
        }
        private static Color LoadThemeColor(string path, string attr, Color fallback)
        {
            try
            {
                var doc=new System.Xml.XmlDocument();
                doc.Load(path);
                var node=doc.SelectSingleNode("/OnScreenKeyboard/Theme");
                if (node?.Attributes?[attr] is System.Xml.XmlAttribute a)
                    return SettingsManager.ParseColor(a.Value, fallback);
            }
            catch { }
            return fallback;
        }

        // Reads a per-group colour (e.g. Klinkers' KeyColor) from a theme file's
        // <Theme><Group Name="..."> entries. Falls back to the file's global <Theme>
        // colour when the group isn't present or the attribute is blank/missing —
        // same fallback shape as the built-in-preset branch (GetPreviewKeyColor()
        // for a group name with no matching ExtraGroups entry).
        private static Color LoadThemeGroupColor(string path, string groupName, string attr, Color fallback)
        {
            if (!string.IsNullOrEmpty(groupName))
            {
                try
                {
                    var doc=new System.Xml.XmlDocument();
                    doc.Load(path);
                    var groups=doc.SelectNodes("/OnScreenKeyboard/Theme/Group");
                    if (groups!=null)
                        foreach (System.Xml.XmlNode g in groups)
                            if (string.Equals(g.Attributes?["Name"]?.Value, groupName, StringComparison.Ordinal))
                            {
                                var val=g.Attributes?[attr]?.Value;
                                if (!string.IsNullOrEmpty(val))
                                    return SettingsManager.ParseColor(val, fallback);
                                break;
                            }
                }
                catch { }
            }
            return LoadThemeColor(path, attr, fallback);
        }

        // ── Create ────────────────────────────────────────────────────────

        /// <summary>Asks whether an existing file may be replaced (a seam so the tests do not open a dialog). "No" is the default.</summary>
        internal Func<string,bool> ConfirmReplace;

        private bool AskReplace(string fileName) =>
            (ConfirmReplace ?? (n => TouchMessage.Confirm(this, Lang.T("wiz: replace title"), string.Format(Lang.T("wiz: replace text"), n))))(fileName);

        private void TryCreate()
        {
            _lblSaveError.Text="";
            string name=_txtFileName.Text.Trim(), folder=_txtFolder.Text.Trim();

            if (string.IsNullOrEmpty(name))
            { _lblSaveError.Text=Lang.T("wiz: err no name"); _txtFileName.Focus(); return; }
            if (!Directory.Exists(folder))
            { _lblSaveError.Text=Lang.T("wiz: err bad folder"); _txtFolder.Focus(); return; }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
            { _lblSaveError.Text=Lang.T("wiz: err bad name"); _txtFileName.Focus(); return; }

            if (!name.EndsWith(".kbl",StringComparison.OrdinalIgnoreCase)) name+=".kbl";
            string path=Path.Combine(folder,name);
            if (File.Exists(path) && !AskReplace(name))
            { _txtFileName.Focus(); _txtFileName.SelectAll(); return; }
            try
            {
                var (layout,theme,window,meta)=BuildLayoutData();
                meta.LastFile=path;
                SettingsManager.SaveSettings(layout,theme,window,meta,path);
                CreatedFilePath=path;
                DialogResult=DialogResult.OK;
                Close();
            }
            catch (Exception ex) { _lblSaveError.Text=ex.Message; }
        }

        private (GridLayout layout, VisualTheme theme, WindowState window, LayoutMeta meta)
            BuildLayoutData()
        {
            GridLayout layout;
            if (_rbCopy.Checked && File.Exists(_txtCopyFile.Text))
            {
                var tmpT=new VisualTheme(); var tmpW=new WindowState(); var tmpM=new LayoutMeta();
                layout=SettingsManager.LoadSettings(tmpT,tmpW,tmpM,_txtCopyFile.Text);
            }
            else if (_rbPaste.Checked)
            {
                var rows=WizardKeyParser.Parse(_txtPaste.Text, IsDutch());
                layout=BuildGridFromRows(rows);
            }
            else
            {
                layout=BuildBlankGrid((int)_stpRows.Value, (int)_stpCols.Value+1);
            }

            var theme=new VisualTheme { FontName="Arial", FontSize=0 };

            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
            {
                ApplyPreset(Presets[_selectedPreset], theme, layout);
            }
            else if (_selectedPreset==-1&&File.Exists(_txtThemeFile.Text))
            {
                var tmpT=new VisualTheme(); var tmpW=new WindowState(); var tmpM=new LayoutMeta();
                var tmpL=SettingsManager.LoadSettings(tmpT,tmpW,tmpM,_txtThemeFile.Text);
                theme.CopyFrom(tmpT);
                foreach (var g in tmpL.Groups)
                {
                    // Re-style a group the layout already has (e.g. copying azerty.kbl and
                    // applying azertycolor.kbl's theme — both share group names) instead of
                    // skipping it, which used to silently discard the theme file's colours.
                    var existing = layout.Groups.Find(x=>string.Equals(x.Name,g.Name,StringComparison.Ordinal));
                    if (existing != null)
                    {
                        existing.KeyColor        = g.KeyColor;
                        existing.FontColor       = g.FontColor;
                        existing.BorderColor     = g.BorderColor;
                        existing.BorderThickness = g.BorderThickness;
                        existing.FontName        = g.FontName;
                        existing.FontSize        = g.FontSize;
                    }
                    else
                    {
                        layout.Groups.Add(g.Clone());
                    }
                }
            }

            // Auto-assign group names for paste-generated layouts, regardless of which
            // theme source was chosen. Blank and copied layouts are left as-is (blank has
            // no keys; copied layouts already carry their own group assignments).
            if (_rbPaste.Checked)
                AutoClassifyLayout(layout);

            var screen=System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int keyW=Math.Max(60,Math.Min(120,screen.Width/Math.Max(1,layout.Cols)));
            int keyH=Math.Max(52,Math.Min(90,screen.Height/Math.Max(1,layout.Rows+1)));
            var window=new WindowState
            {
                WindowWidth  = Math.Min(screen.Width-40,  layout.Cols*keyW),
                WindowHeight = Math.Min(screen.Height-80, layout.Rows*keyH+52),
                AlwaysOnTop  = true,
                HideTitlebar = false,
            };
            var meta=new LayoutMeta { Language=LanguageCode(), GearRow=0, GearCol=-1 };
            return (layout,theme,window,meta);
        }

        private GridLayout BuildGridFromRows(List<List<WizardKeyParser.KeySpec>> rows)
        {
            int maxCols=0;
            foreach (var r in rows) if (r.Count>maxCols) maxCols=r.Count;
            int totalCols=maxCols+1;  // always reserve last col for gear
            int totalRows=Math.Max(1,rows.Count);
            var layout=new GridLayout(totalRows,totalCols);
            for (int r=0; r<totalRows; r++)
                for (int c=0; c<totalCols; c++)
                {
                    if (r==0&&c==totalCols-1)
                    { layout.Cells.Add(new GridCell(r,c,new KeyProps("",""))); continue; }
                    string lbl="",snd="";
                    if (r<rows.Count&&c<rows[r].Count)
                    { var s=rows[r][c]; lbl=s.IsBlank?"":s.Label; snd=s.IsBlank?"":s.Send; }
                    layout.Cells.Add(new GridCell(r,c,new KeyProps(lbl,snd)));
                }
            return layout;
        }

        private static GridLayout BuildBlankGrid(int rows,int cols)
        {
            var layout=new GridLayout(rows,cols);
            for (int r=0;r<rows;r++) for (int c=0;c<cols;c++)
                layout.Cells.Add(new GridCell(r,c,new KeyProps("","")));
            return layout;
        }

        private static void ApplyPreset(ThemePreset p, VisualTheme theme, GridLayout layout)
        {
            theme.BackgroundColor = SettingsManager.ParseColor(p.Background, theme.BackgroundColor);
            theme.KeyColor        = SettingsManager.ParseColor(p.KeyColor,   theme.KeyColor);
            theme.FontColor       = SettingsManager.ParseColor(p.FontColor,  theme.FontColor);
            theme.BorderColor     = SettingsManager.ParseColor(p.BorderColor,theme.BorderColor);
            theme.BorderThickness = p.BorderThickness;

            var std=layout.Groups.Find(g=>g.Name==SettingsManager.StandardGroupName);
            if (std==null)
            { std=new KeyGroup{Name=SettingsManager.StandardGroupName}; layout.Groups.Insert(0,std); }
            std.KeyColor=theme.KeyColor; std.FontColor=theme.FontColor;
            std.BorderColor=theme.BorderColor; std.BorderThickness=p.BorderThickness;

            // Picking a preset means "restyle whatever groups exist to match it" — not "only
            // fill in groups that happen to be missing". A copied layout (e.g. azerty.kbl)
            // already has groups named Klinkers/Medeklinkers/etc.; those must take the preset's
            // colours too, not keep the copied file's original ones. Harmless for Blank/Paste,
            // where layout.Groups is still empty at this point — existing is always null there.
            foreach (var (name,key,font,border,thick) in p.ExtraGroups)
            {
                var existing = layout.Groups.Find(g=>string.Equals(g.Name,name,StringComparison.Ordinal));
                if (existing != null)
                {
                    existing.KeyColor        = SettingsManager.ParseColor(key,   Color.Empty);
                    existing.FontColor       = SettingsManager.ParseColor(font,  Color.Empty);
                    existing.BorderColor     = SettingsManager.ParseColor(border,Color.Empty);
                    existing.BorderThickness = thick;
                }
                else
                {
                    layout.Groups.Add(new KeyGroup
                    { Name=name,
                      KeyColor    =SettingsManager.ParseColor(key,   Color.Empty),
                      FontColor   =SettingsManager.ParseColor(font,  Color.Empty),
                      BorderColor =SettingsManager.ParseColor(border,Color.Empty),
                      BorderThickness=thick });
                }
            }
        }

        // ── Universal key classification (all themes) ─────────────────────
        // Called after ApplyPreset when paste mode is active.
        // Assigns one of six shared group names to every non-blank cell.
        // The groups exist in all four theme presets with theme-appropriate colours.

        private static void AutoClassifyLayout(GridLayout layout)
        {
            foreach (var cell in layout.Cells)
            {
                if (string.IsNullOrEmpty(cell.Props.Label) && string.IsNullOrEmpty(cell.Props.Send))
                    continue;   // blank spacer — leave unassigned
                string group = ClassifyKey(cell.Props.Label, cell.Props.Send);
                if (group != null) cell.Props.GroupName = group;
            }
        }

        // Returns one of: "Klinkers", "Medeklinkers", "Cijfers",
        //                 "Besturing", "Leestekens", "Woord", or null.
        internal static string ClassifyKey(string label, string send)
        {
            label = label ?? "";
            send  = send  ?? "";

            // ── Word-prediction slots ─────────────────────────────────
            if (send.StartsWith("wp:")) return "Woord";

            // ── Control / navigation — SendKeys { } format ────────────
            // Covers: {ENTER}, {BACKSPACE}, {TAB}, {ESC}, {DELETE},
            //         {UP}, {DOWN}, {LEFT}, {RIGHT}, {F1}-{F12},
            //         {SHIFT}, {CTRL}, {ALT}, {WIN}, {CAPSLOCK}, etc.
            if (send.Length > 2 && send[0] == '{') return "Besturing";

            // ── Space (send is a single literal space) ────────────────
            if (send == " " || label == "Space" || label == "Spatie")
                return "Besturing";

            // ── Symbol labels emitted by the wizard parser ────────────
            //   ↑↓←→  (arrow keys)
            //   ↵      (Enter)
            //   ⌫      (Backspace)
            //   ⇥      (Tab)
            const string ctrlSymbols = "↑↓←→↵⌫⇥";
            if (label.Length == 1 && ctrlSymbols.Contains(label)) return "Besturing";

            // Text labels for control-class keys
            if (label == "Esc" || label == "Del" || label == "Tab" ||
                label == "Shift" || label == "Ctrl" || label == "Alt" ||
                label == "AltGr" || label == "CapsLock")
                return "Besturing";

            // ── Classify by first significant character ────────────────
            // Only single-character keys get a category; multi-word
            // communication-board labels remain in the standard group.
            string text = label.Length > 0 ? label : send;
            if (text.Length != 1) return null;
            char c = text[0];

            if (char.IsDigit(c)) return "Cijfers";

            // Vowels — Latin base + common Western European accented forms
            const string vowels =
                "aeiouAEIOU" +
                "áàäâãåæéèëêíìïîóòöôõøúùüûÿý" +
                "ÁÀÄÂÃÅÆÉÈËÊÍÌÏÎÓÒÖÔÕØÚÙÜÛÝ";
            if (vowels.IndexOf(c) >= 0) return "Klinkers";

            if (char.IsLetter(c)) return "Medeklinkers";

            if (char.IsPunctuation(c) || char.IsSymbol(c)) return "Leestekens";

            return null;
        }

        // ── Misc helpers ──────────────────────────────────────────────────
        private bool   IsDutch()       => _cmbLanguage!=null&&_cmbLanguage.SelectedIndex==1;
        private string LanguageCode()  => IsDutch() ? "nl" : "en";
        private string ThemeDisplayName()
        {
            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
                return Lang.T("wiz: theme "+Presets[_selectedPreset].Id);
            if (_selectedPreset==-1) return Path.GetFileName(_txtThemeFile?.Text??"?");
            return "?";
        }
        private static string DefaultFolder()
        {
            string last=SettingsManager.DefaultPath;
            return File.Exists(last) ? Path.GetDirectoryName(last) : AppDomain.CurrentDomain.BaseDirectory;
        }

        // ── Language-change refresh ───────────────────────────────────────
        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            Text=Lang.T("New Keyboard");
            _lblStep.Text=string.Format(Lang.T("wiz: Step {0} of {1}"),_currentPage+1,PAGE_COUNT);
            for (int i=0;i<_tiles.Length;i++) _tiles[i].Text=Lang.T("wiz: theme "+Presets[i].Id);
            _tileFile.Text=Lang.T("wiz: From file…");
            foreach (var b in new[]{ _btnBrowseCopy, _btnBrowseTheme, _btnBrowseFolder }) b.AccessibleName=Lang.T("wiz: Browse");
            _txtPaste.AccessibleName=Lang.StripMnemonic(Lang.T("wiz: Key labels"));
            RefreshGrid();
            UpdateSummary();
            EqualiseFooter();
            _strip?.Invalidate();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // WizardThemeValidator — WCAG contrast ratio helper
    // ══════════════════════════════════════════════════════════════════════
    internal static class WizardThemeValidator
    {
        public static double ContrastRatio(Color fg, Color bg)
        {
            double l1=RelativeLuminance(fg), l2=RelativeLuminance(bg);
            if (l1<l2) (l1,l2)=(l2,l1);
            return (l1+0.05)/(l2+0.05);
        }
        private static double RelativeLuminance(Color c)
        {
            return 0.2126*Lin(c.R/255.0)+0.7152*Lin(c.G/255.0)+0.0722*Lin(c.B/255.0);
        }
        private static double Lin(double v)
            => v<=0.04045 ? v/12.92 : Math.Pow((v+0.055)/1.055, 2.4);
    }
}

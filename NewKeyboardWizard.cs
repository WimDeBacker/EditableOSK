using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    // ══════════════════════════════════════════════════════════════════════
    // NewKeyboardWizard — 5-page wizard that creates a new .xml layout file.
    // ══════════════════════════════════════════════════════════════════════
    internal sealed class NewKeyboardWizard : FluentDialogBase
    {
        // ── Theme preset data ────────────────────────────────────────────

        private sealed class ThemePreset
        {
            public string Id;
            public string DisplayName;
            public string Background, KeyColor, FontColor, BorderColor;
            public int    BorderThickness;
            public (string Name, string Key, string Font, string Border, int Thick)[] ExtraGroups;
        }

        private static readonly ThemePreset[] Presets = new[]
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

        // ── Page indices ─────────────────────────────────────────────────
        private const int PAGE_START=0, PAGE_GRID=1,
                          PAGE_THEME=2, PAGE_SAVE=3, PAGE_COUNT=4;

        // ── Infrastructure ───────────────────────────────────────────────
        private readonly List<(Control Ctrl, Func<string> GetText)> _transControls
            = new List<(Control, Func<string>)>();

        private int    _currentPage = PAGE_START;
        private Panel  _pageArea;
        private Panel  _navBar;
        private Panel[] _pages;
        private FluentButton _btnBack, _btnNext, _btnCreate;
        private Label  _lblStep;

        // ── Page 1 ───────────────────────────────────────────────────────
        private RadioButton _rbBlank, _rbPaste, _rbCopy;
        private TextBox     _txtCopyFile;
        private Button      _btnBrowseCopy;
        private Label       _lblCopyRow;

        // ── Page 2 ───────────────────────────────────────────────────────
        private NumericUpDown _nudRows, _nudCols;
        private TextBox       _txtPaste;
        private Panel         _pnlPreview;
        private Label         _lblPasteSection, _lblPasteHint, _lblSizeSection, _lblPreviewSection;
        private Label         _lblCopyInfo;
        private int _gridRows=4, _gridCols=8;

        // ── Page 1 (language) / Page 2 (row-col labels) ─────────────────────────────
        private ComboBox _cmbLanguage;
        private Label    _lblRows, _lblCols;

        // ── Page 4 (now page 3) ───────────────────────────────────────────────────────
        private FluentButton[] _themeBtns;
        private FluentButton   _btnFromFile;
        private Label[]        _themeCheckMarks;   // ✓ indicators
        private TextBox        _txtThemeFile;
        private Button         _btnBrowseTheme;
        private Panel          _pnlFromFile;
        private Panel          _pnlThemePreview;
        private int            _selectedPreset = 0;

        // ── Page 5 ───────────────────────────────────────────────────────
        private TextBox _txtFileName, _txtFolder;
        private Button  _btnBrowseFolder;
        private Label   _lblSaveError;
        private Panel   _pnlSummary;

        // ── Result ───────────────────────────────────────────────────────
        public string CreatedFilePath { get; private set; }

        // ── Constructor ──────────────────────────────────────────────────
        public NewKeyboardWizard() : base(new Size(880, 820))
        {
            Text            = Lang.T("New Keyboard");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = false;
            MinimumSize     = new Size(720, 580);

            BuildUI();
            ShowPage(PAGE_START);
        }

        // Skip _navBar so it keeps its intentional dark-navy background.
        // Also re-positions nav buttons, since the first Resize fires before
        // the handler is registered (during Controls.Add in the constructor).
        protected override void ApplyTheme()
        {
            FluentPainter.ApplyDialogTheme(this, _dark, _navBar);

            // ApplyThemeChildren walks every Label and sets ForeColor to the global fg,
            // which overwrites the accent color on the ✓ checkmarks below each theme tile.
            // Re-apply the correct accent color here, with HC-mode support.
            if (_themeCheckMarks != null)
            {
                Color ckColor = SystemInformation.HighContrast
                    ? SystemColors.Highlight
                    : _dark ? Color.FromArgb(100, 180, 255)   // bright enough on dark panels
                            : Color.FromArgb(0,  120, 212);   // accent blue on light panels
                foreach (var lbl in _themeCheckMarks)
                    if (lbl != null) lbl.ForeColor = ckColor;
            }

            PositionNavButtons();
        }

        private void PositionNavButtons()
        {
            int rx = _navBar.ClientSize.Width - 6;
            if (rx < 100) return;   // handle not yet created
            _btnCreate.Left = rx - _btnCreate.Width;
            _btnNext.Left   = rx - _btnNext.Width;
            _btnBack.Left   = _btnNext.Left - 6 - _btnBack.Width;
        }

        // ── BuildUI ───────────────────────────────────────────────────────
        private void BuildUI()
        {
            _navBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 52,
                BackColor = _dark ? Color.FromArgb(36,36,52) : Color.FromArgb(220,220,228),
            };
            Controls.Add(_navBar);

            _lblStep = new Label
            {
                Left=12, Top=16, Width=240, Height=22,
                Font      = Fluent.FontLabel,
                ForeColor = _dark ? Color.FromArgb(160,160,200) : Color.FromArgb(80,80,100),
                BackColor = Color.Transparent,
            };
            _navBar.Controls.Add(_lblStep);

            _btnCreate = new FluentButton
            { Text=Lang.T("Create"),  Left=0, Top=8, Width=110, Height=36,
              Style=FluentButton.Variant.Primary,  TabStop=true, Visible=false };
            _btnNext = new FluentButton
            { Text=Lang.T("Next →"),  Left=0, Top=8, Width=110, Height=36,
              Style=FluentButton.Variant.Primary,  TabStop=true };
            _btnBack = new FluentButton
            { Text=Lang.T("← Back"),  Left=0, Top=8, Width=110, Height=36,
              Style=FluentButton.Variant.Neutral,  TabStop=true };
            _navBar.Controls.Add(_btnCreate);
            _navBar.Controls.Add(_btnNext);
            _navBar.Controls.Add(_btnBack);

            _btnBack.Click   += (s,e) => Navigate(-1);
            _btnNext.Click   += (s,e) => Navigate(+1);
            _btnCreate.Click += (s,e) => TryCreate();

            // Reposition nav buttons when form resizes
            _navBar.Resize += (s,e) => PositionNavButtons();

            _pageArea = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = _dark ? Fluent.DarkBg : Fluent.BgPage,
            };
            Controls.Add(_pageArea);

            _pages = new Panel[PAGE_COUNT];
            _pages[PAGE_START]  = BuildPage1();
            _pages[PAGE_GRID]   = BuildPage2();
            _pages[PAGE_THEME]  = BuildPage4();
            _pages[PAGE_SAVE]   = BuildPage5();
            foreach (var p in _pages) _pageArea.Controls.Add(p);

            AcceptButton = _btnCreate;
        }

        // ── Page helpers ──────────────────────────────────────────────────

        private Panel MakePage()
        {
            return new Panel
            {
                Dock        = DockStyle.Fill,
                AutoScroll  = true,
                BackColor   = _dark ? Fluent.DarkBg : Fluent.BgPage,
                Visible     = false,
            };
        }

        private void AddPageTitle(Panel pg, Func<string> getTitle, Func<string> getSub)
        {
            var lblTitle = new Label
            {
                Left=20, Top=18, Width=840, Height=34, AutoSize=false,
                Font=new Font("Arial",16f,FontStyle.Bold),
                ForeColor=Fluent.TextPrimary, BackColor=Color.Transparent,
                Text=getTitle(),
            };
            var lblSub = new Label
            {
                Left=20, Top=58, Width=840, Height=22, AutoSize=false,
                Font=Fluent.FontLabel,
                ForeColor=_dark ? Color.FromArgb(150,150,180) : Color.FromArgb(90,90,110),
                BackColor=Color.Transparent,
                Text=getSub(),
            };
            _transLabels.Add((lblTitle, getTitle));
            _transLabels.Add((lblSub,   getSub));
            pg.Controls.Add(lblTitle);
            pg.Controls.Add(lblSub);
            var sep = new Panel { Left=20, Top=88, Width=840, Height=1,
                BackColor=_dark ? Color.FromArgb(60,60,80) : Color.FromArgb(200,200,210) };
            pg.Controls.Add(sep);
        }

        private Label AddSectionLabel(Panel pg, Func<string> getText, int y)
        {
            var lbl = new Label
            {
                Left=28, Top=y, Width=824, Height=20, AutoSize=false,
                Font=new Font("Arial",9f,FontStyle.Bold),
                ForeColor=_dark ? Color.FromArgb(100,160,255) : Color.FromArgb(60,60,140),
                BackColor=Color.Transparent, Text=getText(),
            };
            _transLabels.Add((lbl, getText));
            pg.Controls.Add(lbl);
            return lbl;
        }

        private Label AddFieldLabel(Panel pg, Func<string> getText, int x, int y, int w=160)
        {
            var lbl = new Label
            {
                Left=x, Top=y+5, Width=w, Height=20, AutoSize=false,
                Font=Fluent.FontLabel,
                ForeColor=Fluent.TextPrimary, BackColor=Color.Transparent,
                Text=getText(),
            };
            _transLabels.Add((lbl, getText));
            pg.Controls.Add(lbl);
            _pendingAccessibleName = Lang.StripMnemonic(getText());
            return lbl;
        }

        private RadioButton MakeRadio(Panel pg, Func<string> getText, Func<string> getTip, int y)
        {
            var rb = new RadioButton
            {
                Left=40, Top=y, Width=800, Height=28,
                Text=getText(),
                ForeColor=Fluent.TextPrimary, BackColor=Color.Transparent,
                Font=Fluent.FontLabel,
            };
            _transControls.Add((rb, getText));
            SetTip(rb, getTip);
            pg.Controls.Add(rb);
            return rb;
        }

        private Button MakeBrowse(Panel pg, int x, int y, Action onClick)
        {
            var btn = new Button
            {
                Text="…", Left=x, Top=y, Width=30, Height=25,
                BackColor=_dark ? Color.FromArgb(60,60,80) : Color.FromArgb(200,200,210),
                ForeColor=Fluent.TextPrimary, FlatStyle=FlatStyle.Flat,
                TabStop=true, AccessibleName="…",
            };
            btn.FlatAppearance.BorderSize=1;
            btn.Click += (s,e) => onClick();
            pg.Controls.Add(btn);
            return btn;
        }

        private TextBox MakeTextBox(Panel pg, int x, int y, int w, int tabIdx)
        {
            var txt = new TextBox
            {
                Left=x, Top=y, Width=w, Height=25,
                BackColor=Fluent.BgInput, ForeColor=Fluent.TextPrimary,
                BorderStyle=BorderStyle.FixedSingle, Font=Fluent.FontLabel,
                TabIndex=tabIdx,
            };
            if (_pendingAccessibleName != null)
            { txt.AccessibleName = _pendingAccessibleName; _pendingAccessibleName=null; }
            pg.Controls.Add(txt);
            return txt;
        }

        // ── Page 1 — Starting point ───────────────────────────────────────
        private Panel BuildPage1()
        {
            var pg = MakePage();
            AddPageTitle(pg, ()=>Lang.T("wiz: p1 title"), ()=>Lang.T("wiz: p1 sub"));

            _rbBlank = MakeRadio(pg, ()=>Lang.T("wiz: Blank grid"),     ()=>Lang.T("wiz: tip Blank grid"),     110);
            _rbPaste = MakeRadio(pg, ()=>Lang.T("wiz: Paste labels"),   ()=>Lang.T("wiz: tip Paste labels"),   150);
            _rbCopy  = MakeRadio(pg, ()=>Lang.T("wiz: Copy from file"), ()=>Lang.T("wiz: tip Copy from file"), 190);
            _rbBlank.Checked = true;

            // Copy file row
            _lblCopyRow = AddFieldLabel(pg, ()=>Lang.T("wiz: Layout file"), 40, 234);
            _txtCopyFile = MakeTextBox(pg, 200, 234, 606, 10);
            _btnBrowseCopy = MakeBrowse(pg, 812, 234, () =>
            {
                using var dlg = new OpenFileDialog { Title=Lang.T("wiz: Select layout"),
                    Filter="XML files (*.xml)|*.xml|All files (*.*)|*.*" };
                if (dlg.ShowDialog(this)==DialogResult.OK) _txtCopyFile.Text=dlg.FileName;
            });
            SetTip(_txtCopyFile,  ()=>Lang.T("tip: Browse layout"));
            SetTip(_btnBrowseCopy,()=>Lang.T("tip: Browse layout"));

            void UpdateCopyRow()
            {
                bool v=_rbCopy.Checked;
                _txtCopyFile.Visible=v; _btnBrowseCopy.Visible=v; _lblCopyRow.Visible=v;
            }
            _rbBlank.CheckedChanged+=(s,e)=>UpdateCopyRow();
            _rbPaste.CheckedChanged+=(s,e)=>UpdateCopyRow();
            _rbCopy.CheckedChanged +=(s,e)=>UpdateCopyRow();
            UpdateCopyRow();

            // Language selector — always visible
            int langY = 282;
            AddFieldLabel(pg, ()=>Lang.T("wiz: Language"), 40, langY, 120);
            _cmbLanguage = new ComboBox
            {
                Left=170, Top=langY, Width=220, Height=25,
                DropDownStyle=ComboBoxStyle.DropDownList,
                BackColor=Fluent.BgInput, ForeColor=Fluent.TextPrimary,
                Font=Fluent.FontLabel, TabIndex=11,
                AccessibleName=Lang.StripMnemonic(Lang.T("wiz: Language")),
            };
            _cmbLanguage.Items.AddRange(new object[]{"English (en)","Nederlands (nl)"});
            _cmbLanguage.SelectedIndex = Lang.CurrentCode=="nl" ? 1 : 0;
            pg.Controls.Add(_cmbLanguage);
            SetTip(_cmbLanguage, ()=>Lang.T("tip: Language"));

            return pg;
        }

        // ── Page 2 — Grid & labels ────────────────────────────────────────
        private Panel BuildPage2()
        {
            var pg = MakePage();
            AddPageTitle(pg, ()=>Lang.T("wiz: p2 title"), ()=>Lang.T("wiz: p2 sub"));

            // Info label for copy mode
            _lblCopyInfo = new Label
            {
                Left=28, Top=106, Width=824, Height=24,
                Font=Fluent.FontLabel, ForeColor=Fluent.TextPrimary, BackColor=Color.Transparent,
                Visible=false,
            };
            pg.Controls.Add(_lblCopyInfo);

            // Paste section
            _lblPasteSection = AddSectionLabel(pg, ()=>Lang.T("wiz: Key labels"), 106);

            _lblPasteHint = new Label
            {
                Left=28, Top=128, Width=824, Height=50,
                Font=Fluent.FontLabel, AutoSize=false,
                ForeColor=_dark ? Color.FromArgb(130,130,160) : Color.FromArgb(100,100,120),
                BackColor=Color.Transparent, Text=Lang.T("wiz: paste hint"),
            };
            _transLabels.Add((_lblPasteHint, ()=>Lang.T("wiz: paste hint")));
            pg.Controls.Add(_lblPasteHint);

            _txtPaste = new TextBox
            {
                Left=28, Top=182, Width=824, Height=220,
                Multiline=true, ScrollBars=ScrollBars.Vertical,
                BackColor=Fluent.BgInput, ForeColor=Fluent.TextPrimary,
                BorderStyle=BorderStyle.FixedSingle, Font=Fluent.FontLabel,
                AcceptsReturn=true, AcceptsTab=false, TabIndex=0,
                AccessibleName=Lang.StripMnemonic(Lang.T("wiz: Key labels")),
            };
            pg.Controls.Add(_txtPaste);
            SetTip(_txtPaste, ()=>Lang.T("wiz: tip paste"));
            _txtPaste.TextChanged+=(s,e)=>UpdateGridFromPaste();

            // Grid size section
            _lblSizeSection = AddSectionLabel(pg, ()=>Lang.T("wiz: Grid size"), 418);

            _lblRows = AddFieldLabel(pg, ()=>Lang.T("wiz: Rows"),    28, 446, 120);
            _nudRows = new NumericUpDown
            {
                Left=152, Top=446, Width=70, Height=25,
                Minimum=1, Maximum=30, Value=4,
                BackColor=Fluent.BgInput, ForeColor=Fluent.TextPrimary,
                Font=Fluent.FontLabel, TabIndex=1,
                AccessibleName=Lang.StripMnemonic(Lang.T("wiz: Rows")),
            };
            pg.Controls.Add(_nudRows);

            _lblCols = AddFieldLabel(pg, ()=>Lang.T("wiz: Columns"), 252, 446, 130);
            _nudCols = new NumericUpDown
            {
                Left=386, Top=446, Width=70, Height=25,
                Minimum=1, Maximum=60, Value=8,
                BackColor=Fluent.BgInput, ForeColor=Fluent.TextPrimary,
                Font=Fluent.FontLabel, TabIndex=2,
                AccessibleName=Lang.StripMnemonic(Lang.T("wiz: Columns")),
            };
            pg.Controls.Add(_nudCols);

            _nudRows.ValueChanged+=(s,e)=>{ _gridRows=(int)_nudRows.Value; UpdatePreviewSize(); _pnlPreview?.Invalidate(); };
            _nudCols.ValueChanged+=(s,e)=>{ _gridCols=(int)_nudCols.Value; _pnlPreview?.Invalidate(); };

            // Preview section
            _lblPreviewSection = AddSectionLabel(pg, ()=>Lang.T("wiz: Preview"), 490);
            _pnlPreview = new Panel
            {
                Left=28, Top=514, Width=824, Height=Math.Max(180, _gridRows*34),
                BackColor=_dark ? Color.FromArgb(24,24,36) : Color.FromArgb(230,230,238),
                BorderStyle=BorderStyle.FixedSingle,
            };
            _pnlPreview.Paint+=OnPreviewPaint;
            pg.Controls.Add(_pnlPreview);

            return pg;
        }

        // ── Page 3 — Theme (was page 4) ──────────────────────────────────
        private Panel BuildPage4()
        {
            var pg = MakePage();
            AddPageTitle(pg, ()=>Lang.T("wiz: p4 title"), ()=>Lang.T("wiz: p4 sub"));

            // Preset tiles: 5 buttons, each 158px wide, 8px gap, starting at x=28
            const int BW=158, BH=72, BGAP=8;
            _themeBtns      = new FluentButton[Presets.Length];
            _themeCheckMarks = new Label[Presets.Length+1];     // +1 for "from file"

            for (int i=0; i<Presets.Length; i++)
            {
                int bx = 28 + i*(BW+BGAP);
                var preset=Presets[i];
                var btn = new FluentButton
                {
                    Text=Lang.T("wiz: theme "+preset.Id),
                    Left=bx, Top=108, Width=BW, Height=BH,
                    Style= i==0 ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral,
                    TabStop=true, TabIndex=i,
                };
                int cap=i;
                btn.Click+=(s,e)=>SelectPreset(cap);
                _themeBtns[i]=btn;
                pg.Controls.Add(btn);

                var chk = new Label
                {
                    Left=bx, Top=108+BH+2, Width=BW, Height=20,
                    TextAlign=ContentAlignment.MiddleCenter,
                    Font=new Font("Arial",10f,FontStyle.Bold),
                    ForeColor=Color.FromArgb(0,120,212),
                    BackColor=Color.Transparent, Text="",
                };
                _themeCheckMarks[i]=chk;
                pg.Controls.Add(chk);
            }

            // "From file" button (5th tile)
            int ffx = 28 + Presets.Length*(BW+BGAP);
            _btnFromFile = new FluentButton
            {
                Text=Lang.T("wiz: From file…"),
                Left=ffx, Top=108, Width=BW, Height=BH,
                Style=FluentButton.Variant.Neutral,
                TabStop=true, TabIndex=Presets.Length,
            };
            _btnFromFile.Click+=(s,e)=>SelectPreset(-1);
            pg.Controls.Add(_btnFromFile);

            var ffChk = new Label
            {
                Left=ffx, Top=108+BH+2, Width=BW, Height=20,
                TextAlign=ContentAlignment.MiddleCenter,
                Font=new Font("Arial",10f,FontStyle.Bold),
                ForeColor=Color.FromArgb(0,120,212),
                BackColor=Color.Transparent, Text="",
            };
            _themeCheckMarks[Presets.Length]=ffChk;
            pg.Controls.Add(ffChk);

            // Show initial checkmark on preset 0
            _themeCheckMarks[0].Text="✓";

            // From-file picker (hidden by default)
            _pnlFromFile = new Panel
            {
                Left=28, Top=200, Width=824, Height=34,
                BackColor=Color.Transparent, Visible=false,
            };
            var lblTF = new Label
            {
                Left=0, Top=6, Width=140, Height=22,
                Text=Lang.T("wiz: Theme file"),
                Font=Fluent.FontLabel, ForeColor=Fluent.TextPrimary, BackColor=Color.Transparent,
            };
            _transLabels.Add((lblTF, ()=>Lang.T("wiz: Theme file")));
            _txtThemeFile = new TextBox
            {
                Left=144, Top=2, Width=616, Height=25,
                BackColor=Fluent.BgInput, ForeColor=Fluent.TextPrimary,
                BorderStyle=BorderStyle.FixedSingle, Font=Fluent.FontLabel,
                TabIndex=20, AccessibleName=Lang.StripMnemonic(Lang.T("wiz: Theme file")),
            };
            _btnBrowseTheme = new Button
            {
                Text="…", Left=766, Top=2, Width=30, Height=25,
                BackColor=_dark ? Color.FromArgb(60,60,80) : Color.FromArgb(200,200,210),
                ForeColor=Fluent.TextPrimary, FlatStyle=FlatStyle.Flat,
                TabIndex=21, TabStop=true,
            };
            _btnBrowseTheme.FlatAppearance.BorderSize=1;
            _pnlFromFile.Controls.Add(lblTF);
            _pnlFromFile.Controls.Add(_txtThemeFile);
            _pnlFromFile.Controls.Add(_btnBrowseTheme);
            pg.Controls.Add(_pnlFromFile);

            _btnBrowseTheme.Click+=(s,e)=>
            {
                using var dlg=new OpenFileDialog { Title=Lang.T("wiz: Select theme file"),
                    Filter="XML files (*.xml)|*.xml|All files (*.*)|*.*" };
                if (dlg.ShowDialog(this)==DialogResult.OK) _txtThemeFile.Text=dlg.FileName;
            };

            // Theme preview strip
            _pnlThemePreview = new Panel
            {
                Left=28, Top=378, Width=824, Height=190,
                BorderStyle=BorderStyle.FixedSingle,
            };
            _pnlThemePreview.Paint+=OnThemePreviewPaint;
            pg.Controls.Add(_pnlThemePreview);

            return pg;
        }

        // ── Page 5 — Save ─────────────────────────────────────────────────
        private Panel BuildPage5()
        {
            var pg=MakePage();
            AddPageTitle(pg, ()=>Lang.T("wiz: p5 title"), ()=>Lang.T("wiz: p5 sub"));
            int ti=0;

            AddFieldLabel(pg, ()=>Lang.T("wiz: File name"), 40, 116, 150);
            _txtFileName = MakeTextBox(pg, 200, 116, 580, ti++);
            _txtFileName.Text=Lang.T("wiz: default filename");

            AddFieldLabel(pg, ()=>Lang.T("wiz: Folder"), 40, 158, 150);
            _txtFolder = MakeTextBox(pg, 200, 158, 546, ti++);
            _txtFolder.Text=DefaultFolder();
            _btnBrowseFolder = MakeBrowse(pg, 752, 158, () =>
            {
                using var dlg=new FolderBrowserDialog { SelectedPath=_txtFolder.Text };
                if (dlg.ShowDialog(this)==DialogResult.OK) _txtFolder.Text=dlg.SelectedPath;
            });
            SetTip(_txtFolder,      ()=>Lang.T("wiz: tip folder"));
            SetTip(_btnBrowseFolder,()=>Lang.T("wiz: tip folder"));

            _lblSaveError = new Label
            {
                Left=40, Top=204, Width=800, Height=22,
                Font=Fluent.FontLabel,
                ForeColor=Color.FromArgb(220,80,80), BackColor=Color.Transparent, Text="",
            };
            pg.Controls.Add(_lblSaveError);

            _pnlSummary = new Panel
            {
                Left=40, Top=238, Width=800, Height=200,
                BackColor=_dark ? Color.FromArgb(32,32,48) : Color.FromArgb(238,238,246),
                BorderStyle=BorderStyle.FixedSingle,
            };
            _pnlSummary.Paint+=OnSummaryPaint;
            pg.Controls.Add(_pnlSummary);

            return pg;
        }

        // ── Navigation ────────────────────────────────────────────────────
        private void ShowPage(int index)
        {
            _currentPage=index;
            for (int i=0; i<_pages.Length; i++) _pages[i].Visible=(i==index);

            _btnBack.Visible   = index>0;
            _btnNext.Visible   = index<PAGE_COUNT-1;
            _btnCreate.Visible = index==PAGE_COUNT-1;

            if (index==PAGE_GRID)   RefreshPage2Visibility();
            if (index==PAGE_SAVE)   { _pnlSummary?.Invalidate(); _lblSaveError.Text=""; }

            _lblStep.Text=string.Format(Lang.T("wiz: Step {0} of {1}"), index+1, PAGE_COUNT);

            switch (index)
            {
                case PAGE_START:  _cmbLanguage?.Focus(); break;
                case PAGE_GRID:   (_rbPaste.Checked ? (Control)_txtPaste : _nudRows).Focus(); break;
                case PAGE_THEME:  _themeBtns[0].Focus(); break;
                case PAGE_SAVE:   _txtFileName.Focus(); break;
            }
        }

        private void Navigate(int delta)
        {
            int next=_currentPage+delta;
            if (next<0||next>=PAGE_COUNT) return;
            if (delta>0 && !ValidatePage(_currentPage)) return;
            ShowPage(next);
        }

        private bool ValidatePage(int page)
        {
            if (page==PAGE_START && _rbCopy.Checked && string.IsNullOrWhiteSpace(_txtCopyFile.Text))
            { MessageBox.Show(Lang.T("wiz: err no copy file"),"",MessageBoxButtons.OK,MessageBoxIcon.Warning);
              _txtCopyFile.Focus(); return false; }

            if (page==PAGE_THEME && _selectedPreset==-1 && string.IsNullOrWhiteSpace(_txtThemeFile.Text))
            { MessageBox.Show(Lang.T("wiz: err no theme file"),"",MessageBoxButtons.OK,MessageBoxIcon.Warning);
              _txtThemeFile.Focus(); return false; }

            return true;
        }

        // ── Page 2 helpers ────────────────────────────────────────────────
        private void RefreshPage2Visibility()
        {
            bool isPaste=_rbPaste.Checked, isCopy=_rbCopy.Checked;

            _lblCopyInfo.Visible     = isCopy;
            if (isCopy && File.Exists(_txtCopyFile.Text))
            {
                _lblCopyInfo.Text = string.Format(Lang.T("wiz: copy info"), Path.GetFileName(_txtCopyFile.Text));
            }

            _lblPasteSection.Visible = isPaste;
            _lblPasteHint.Visible    = isPaste;
            _txtPaste.Visible        = isPaste;
            _lblSizeSection.Visible  = !isCopy;
            _lblRows.Visible         = !isCopy;
            _nudRows.Visible         = !isCopy;
            _lblCols.Visible         = !isCopy;
            _nudCols.Visible         = !isCopy;
            _lblPreviewSection.Visible = !isCopy;
            _pnlPreview.Visible      = !isCopy;
        }

        private void UpdateGridFromPaste()
        {
            if (!_rbPaste.Checked) return;
            var rows=WizardKeyParser.Parse(_txtPaste.Text, IsDutch());
            if (rows.Count==0) { _pnlPreview?.Invalidate(); return; }
            int maxCols=0;
            foreach (var r in rows) if (r.Count>maxCols) maxCols=r.Count;
            _gridRows=rows.Count; _gridCols=maxCols;

            _nudRows.ValueChanged-=NudChanged; _nudCols.ValueChanged-=NudChanged;
            _nudRows.Value=Math.Max(1,Math.Min(_nudRows.Maximum,_gridRows));
            _nudCols.Value=Math.Max(1,Math.Min(_nudCols.Maximum,_gridCols));
            _nudRows.ValueChanged+=NudChanged; _nudCols.ValueChanged+=NudChanged;

            UpdatePreviewSize();
            _pnlPreview?.Invalidate();
        }

        private void NudChanged(object s, EventArgs e)
        { _gridRows=(int)_nudRows.Value; _gridCols=(int)_nudCols.Value; UpdatePreviewSize(); _pnlPreview?.Invalidate(); }

        private void UpdatePreviewSize()
        {
            if (_pnlPreview == null) return;
            _pnlPreview.Height = Math.Max(180, _gridRows * 34);
        }

        // ── Page 4 helpers ────────────────────────────────────────────────
        private void SelectPreset(int idx)
        {
            _selectedPreset=idx;

            for (int i=0; i<_themeBtns.Length; i++)
            {
                _themeBtns[i].Style = (i==idx) ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
                _themeCheckMarks[i].Text = (i==idx) ? "✓" : "";
                _themeBtns[i].Invalidate();
            }
            bool isFile=(idx==-1);
            _btnFromFile.Style = isFile ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            _themeCheckMarks[Presets.Length].Text = isFile ? "✓" : "";
            _btnFromFile.Invalidate();

            _pnlFromFile.Visible   = isFile;
            _pnlThemePreview?.Invalidate();
        }

        // ── Paint handlers ────────────────────────────────────────────────
        private void OnPreviewPaint(object sender, PaintEventArgs e)
        {
            var g=(Graphics)e.Graphics;
            var pnl=(Panel)sender;
            int rows=_gridRows, cols=_gridCols+1; // +1 for gear col
            if (rows<1||cols<1) return;

            float cw=(float)(pnl.Width-2)/cols;
            float rh=(float)(pnl.Height-2)/rows;

            Color keyClr=GetPreviewKeyColor();
            Color fntClr=GetPreviewFontColor();
            Color gearClr=_dark ? Color.FromArgb(80,80,100) : Color.FromArgb(180,180,200);

            bool isPaste=_rbPaste.Checked;
            List<List<WizardKeyParser.KeySpec>> parsed=null;
            if (isPaste) parsed=WizardKeyParser.Parse(_txtPaste.Text, IsDutch());

            using var keyBrush  = new SolidBrush(keyClr);
            using var gearBrush = new SolidBrush(gearClr);
            using var fntBrush  = new SolidBrush(fntClr);
            using var borPen    = new Pen(_dark ? Color.FromArgb(60,60,80) : Color.FromArgb(180,180,200));
            using var fnt       = new Font("Arial", Math.Max(5f,Math.Min(10f,rh*0.38f)));
            var sf = new StringFormat
            { Alignment=StringAlignment.Center, LineAlignment=StringAlignment.Center,
              Trimming=StringTrimming.EllipsisCharacter };

            for (int r=0; r<rows; r++)
            {
                for (int c=0; c<cols; c++)
                {
                    bool isGear=(r==0 && c==cols-1);
                    var rect=new RectangleF(1+c*cw, 1+r*rh, cw-1, rh-1);
                    g.FillRectangle(isGear ? gearBrush : keyBrush, rect);
                    g.DrawRectangle(borPen, rect.X, rect.Y, rect.Width, rect.Height);

                    if (isGear)
                        g.DrawString("⚙", fnt, fntBrush, rect, sf);
                    else if (isPaste && parsed!=null && r<parsed.Count && c<parsed[r].Count)
                    {
                        var spec=parsed[r][c];
                        if (!spec.IsBlank && spec.Label.Length>0)
                            g.DrawString(spec.Label, fnt, fntBrush, rect, sf);
                    }
                }
            }
        }

        private void OnThemePreviewPaint(object sender, PaintEventArgs e)
        {
            var g=e.Graphics;
            var pnl=(Panel)sender;
            Color bg=GetPreviewBgColor(), borC=GetPreviewBorderColor();
            int borT=GetPreviewBorderThick();
            g.Clear(bg);

            // One representative key per group so the user sees the full colour palette.
            var samples = new (string Label, string Group)[]
            {
                ("a",   "Klinkers"),
                ("e",   "Klinkers"),
                ("b",   "Medeklinkers"),
                ("n",   "Medeklinkers"),
                ("1",   "Cijfers"),
                ("↵",   "Besturing"),
                ("⌫",   "Besturing"),
                (".",   "Leestekens"),
                ("abc", "Woord"),
                ("⚙",   null),        // standard group / gear
            };

            int n=samples.Length, kw=72, kh=54, gap=4;
            int totalW=n*kw+(n-1)*gap;
            int ox=Math.Max(4,(pnl.Width-totalW)/2);
            int oy=(pnl.Height-kh)/2;

            using var borPen=(borT>0) ? new Pen(borC,borT) : null;
            using var fnt=new Font("Arial",9f);
            var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};

            for (int i=0; i<n && ox+i*(kw+gap)+kw<=pnl.Width-4; i++)
            {
                Color keyC = GetGroupKeyColor(samples[i].Group);
                Color fntC = GetGroupFontColor(samples[i].Group);
                var r=new Rectangle(ox+i*(kw+gap),oy,kw,kh);
                using var keyBrush=new SolidBrush(keyC);
                using var fntBrush=new SolidBrush(fntC);
                g.FillRectangle(keyBrush,r);
                if (borPen!=null) g.DrawRectangle(borPen,r);
                g.DrawString(samples[i].Label,fnt,fntBrush,r,sf);
            }
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
                return LoadThemeColor(_txtThemeFile.Text,"KeyColor",Color.DimGray);
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
                return LoadThemeColor(_txtThemeFile.Text,"FontColor",Color.White);
            return Color.White;
        }

        private void OnSummaryPaint(object sender, PaintEventArgs e)
        {
            var g=e.Graphics; var pnl=(Panel)sender;
            g.Clear(pnl.BackColor);
            var lines=new[]
            {
                string.Format(Lang.T("wiz: sum rows cols"), _gridRows, _gridCols+1),
                string.Format(Lang.T("wiz: sum theme"),     ThemeDisplayName()),
                string.Format(Lang.T("wiz: sum language"),  LanguageCode()),
                string.Format(Lang.T("wiz: sum always on top"), Lang.T("Yes")),
            };
            var fnt=Fluent.FontLabel;
            using var br=new SolidBrush(_dark ? Color.FromArgb(230,230,230) : Fluent.TextPrimary);
            for (int i=0; i<lines.Length; i++) g.DrawString(lines[i],fnt,br,14,12+i*28);
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

        // ── Create ────────────────────────────────────────────────────────
        private void TryCreate()
        {
            _lblSaveError.Text="";
            string name=_txtFileName.Text.Trim(), folder=_txtFolder.Text.Trim();

            if (string.IsNullOrEmpty(name))
            { _lblSaveError.Text=Lang.T("wiz: err no name"); _txtFileName.Focus(); return; }
            if (!Directory.Exists(folder))
            { _lblSaveError.Text=Lang.T("wiz: err bad folder"); _txtFolder.Focus(); return; }

            if (!name.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)) name+=".xml";
            string path=Path.Combine(folder,name);
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
                layout=BuildBlankGrid((int)_nudRows.Value, (int)_nudCols.Value+1);
            }

            var theme=new VisualTheme { FontName="Arial", FontSize=0 };

            if (_selectedPreset>=0&&_selectedPreset<Presets.Length)
            {
                ApplyPreset(Presets[_selectedPreset], theme, layout);
                // Auto-assign group names for paste-generated layouts.
                // Blank and copied layouts are left as-is (blank has no keys;
                // copied layouts already carry their own group assignments).
                if (_rbPaste.Checked)
                    AutoClassifyLayout(layout);
            }
            else if (_selectedPreset==-1&&File.Exists(_txtThemeFile.Text))
            {
                var tmpT=new VisualTheme(); var tmpW=new WindowState(); var tmpM=new LayoutMeta();
                var tmpL=SettingsManager.LoadSettings(tmpT,tmpW,tmpM,_txtThemeFile.Text);
                theme.CopyFrom(tmpT);
                foreach (var g in tmpL.Groups)
                    if (!layout.Groups.Exists(x=>string.Equals(x.Name,g.Name,StringComparison.Ordinal)))
                        layout.Groups.Add(g.Clone());
            }

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

            foreach (var (name,key,font,border,thick) in p.ExtraGroups)
                if (!layout.Groups.Exists(g=>string.Equals(g.Name,name,StringComparison.Ordinal)))
                    layout.Groups.Add(new KeyGroup
                    { Name=name,
                      KeyColor    =SettingsManager.ParseColor(key,   Color.Empty),
                      FontColor   =SettingsManager.ParseColor(font,  Color.Empty),
                      BorderColor =SettingsManager.ParseColor(border,Color.Empty),
                      BorderThickness=thick });
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
            foreach (var (ctrl,getText) in _transControls) ctrl.Text=getText();
            Text=Lang.T("New Keyboard");
            _btnNext.Text=Lang.T("Next →"); _btnBack.Text=Lang.T("← Back"); _btnCreate.Text=Lang.T("Create");
            _lblStep.Text=string.Format(Lang.T("wiz: Step {0} of {1}"),_currentPage+1,PAGE_COUNT);
            // Refresh theme button labels (selected one keeps ✓ prefix)
            for (int i=0;i<_themeBtns.Length;i++)
                _themeBtns[i].Text=Lang.T("wiz: theme "+Presets[i].Id);
            _btnFromFile.Text=Lang.T("wiz: From file…");
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

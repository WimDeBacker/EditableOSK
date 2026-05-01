using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public class KeyboardEditorForm : Form
    {
        public GlobalSettings ResultGlobal { get; private set; }
        public bool           ApplyToKeys  { get; private set; }

        private readonly GlobalSettings _src;

        // Controls
        private ComboBox      _cmbLanguage;
        private TrackBar      _trkOpacity;
        private Panel         _pnlBgColor;
        private ComboBox      _cmbFont;
        private NumericUpDown _nudFontSize;
        private CheckBox      _chkAutoSize;
        private Panel         _pnlFontColor, _pnlKeyColor, _pnlBorderColor;
        private NumericUpDown _nudBorderThickness;
        private CheckBox      _chkApplyToKeys;
        private CheckBox      _chkAlwaysOnTop;
        private CheckBox      _chkStickyMods;
        private CheckBox      _chkHideTitlebar;

        // File action delegates — called when Save/SaveAs/Load buttons are clicked
        private readonly Action _onSave;
        private readonly Action _onSaveAs;
        private readonly Action _onLoad;
        // File buttons — kept for relabelling on language change
        private Button _btnSaveFile, _btnSaveAsFile, _btnLoadFile;
        private Panel         _pnlPreview;
        private Label         _lblPreviewKey;
        private Button        _btnApply, _btnCancel;

        // Translatable references
        private readonly List<(Label Ctrl, Func<string> GetText)> _transLabels
            = new List<(Label, Func<string>)>();
        private readonly List<(Panel Pnl, Func<string> GetTitle)> _transGroups
            = new List<(Panel, Func<string>)>();

        // ── Theme ─────────────────────────────────────────────────────
        private static readonly Color C_BG        = Color.FromArgb(240, 242, 246);
        private static readonly Color C_PANEL_BG  = Color.White;
        private static readonly Color C_BORDER    = Color.FromArgb(210, 215, 220);
        private static readonly Color C_LBL       = Color.FromArgb(70, 80, 95);
        private static readonly Color C_HINT      = Color.FromArgb(160, 165, 170);
        private static readonly Color C_BTN_OK    = Color.FromArgb(39, 174, 96);
        private static readonly Color C_BTN_CANCEL= Color.FromArgb(192, 57, 43);
        private static readonly Color C_INPUT_BG  = Color.FromArgb(250, 252, 255);
        private static readonly Font  F_LABEL     = new Font("Segoe UI", 12.5f);
        private static readonly Font  F_INPUT     = new Font("Segoe UI", 12.5f);
        private static readonly Font  F_HEADER    = new Font("Segoe UI", 12f, FontStyle.Bold);
        private static readonly Font  F_BTN       = new Font("Segoe UI", 13f, FontStyle.Bold);
        private static readonly Font  F_HINT      = new Font("Segoe UI", 10.5f);
        private const int HDR_H = 36;
        private const int ROW_H = 48;   // slightly more breathing room per row
        private const int PAD   = 16;

        // ── Constructor ───────────────────────────────────────────────
        public KeyboardEditorForm(GlobalSettings global, Form owner,
            Action onSave = null, Action onSaveAs = null, Action onLoad = null)
        {
            _src         = global;
            ResultGlobal = Clone(global);
            _onSave   = onSave;
            _onSaveAs = onSaveAs;
            _onLoad   = onLoad;
            Text         = Lang.T("Edit Keyboard");
            BackColor    = C_BG;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox  = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size         = new Size(1060, 560);
            TopMost      = true;
            Font         = F_LABEL;

            BuildUI();
            PopulateFields(global);
            Lang.LanguageChanged += RelabelUI;
            FormClosed += (s, e) =>
            {
                Lang.LanguageChanged -= RelabelUI;
                _previewFont?.Dispose();
            };
        }

        private void RelabelUI()
        {
            Text                 = Lang.T("Edit Keyboard");
            _btnApply.Text       = Lang.T("✔ Apply");
            _btnCancel.Text      = Lang.T("✖ Cancel");
            _chkAutoSize.Text    = Lang.T("Auto");
            _chkApplyToKeys.Text  = Lang.T("Apply to all keys");
            _chkAlwaysOnTop.Text   = Lang.T("Always on top");
            _chkStickyMods.Text    = Lang.T("Sticky modifiers");
            _chkHideTitlebar.Text  = Lang.T("Hide title bar");
            _btnSaveFile.Text      = Lang.T("💾 Save");
            _btnSaveAsFile.Text    = Lang.T("💾 Save As…");
            _btnLoadFile.Text      = Lang.T("📂 Load…");
            foreach (var (ctrl, getText) in _transLabels) ctrl.Text = getText();
            foreach (var (pnl,  _)       in _transGroups) pnl.Invalidate();
            Invalidate(true);
        }

        // ══════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            int margin = 14;
            int formW  = ClientSize.Width - margin * 2;
            int gap    = 12;
            int colW   = (formW - gap) / 2;
            int leftX  = margin;
            int rightX = margin + colW + gap;

            // ── LEFT COLUMN: Language + Window ────────────────────────
            int leftY = margin;

            // Language group
            int langH = HDR_H + PAD + ROW_H + PAD - 4;
            var grpLang = AddGroup(() => Lang.T("Language"), leftX, leftY, colW, langH,
                                   Color.FromArgb(52, 73, 94));
            leftY += langH + gap;

            var langs = Lang.GetAvailable();
            _cmbLanguage = new ComboBox
            {
                Left = PAD, Top = HDR_H + PAD, Width = colW - PAD * 2,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Color.FromArgb(30,40,50),
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
            };
            foreach (var (code, name) in langs)
                _cmbLanguage.Items.Add(new LangItem(code, name));
            for (int i = 0; i < _cmbLanguage.Items.Count; i++)
                if (((LangItem)_cmbLanguage.Items[i]).Code == Lang.CurrentCode)
                { _cmbLanguage.SelectedIndex = i; break; }
            if (_cmbLanguage.SelectedIndex < 0 && _cmbLanguage.Items.Count > 0)
                _cmbLanguage.SelectedIndex = 0;
            _cmbLanguage.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbLanguage.SelectedItem is LangItem li) Lang.Load(li.Code);
            };
            grpLang.Controls.Add(_cmbLanguage);

            // Window group — Opacity + Background + Paste delay + hint
            // Row heights: 52 (opacity trackbar) + 18 (opacity hint) + ROW_H (background)
            //              + 45 (delay trackbar) + 18 (delay hint) + PAD*2
            int wndH = HDR_H + PAD + 52       + ROW_H + ROW_H + ROW_H + PAD + 6;
            var grpWnd = AddGroup(() => Lang.T("Window"), leftX, leftY, colW, wndH,
                                  Color.FromArgb(41, 128, 185));
            leftY += wndH + gap;

            int lx = PAD, vx = 165, vw = colW - lx - vx - PAD;
            int gy = HDR_H + PAD;

            AddFieldLabel(grpWnd, () => Lang.T("Opacity"), lx, gy);
            _trkOpacity = new TrackBar
            {
                Left = vx, Top = gy, Width = vw, Height = 45,
                Minimum = 0, Maximum = 80, TickFrequency = 10,
                SmallChange = 5, LargeChange = 10,
            };
            _trkOpacity.ValueChanged += (s, e) => Refresh2();
            grpWnd.Controls.Add(_trkOpacity);
            gy += 52;

            AddFieldLabel(grpWnd, () => Lang.T("Background"), lx, gy);
            _pnlBgColor = AddColorRow(grpWnd, vx, gy, vw); gy += ROW_H;

            _chkAlwaysOnTop = new CheckBox
            {
                Text = Lang.T("Always on top"),
                Left = lx, Top = gy + 6, AutoSize = true,
                ForeColor = Color.FromArgb(41, 128, 185), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            };
            grpWnd.Controls.Add(_chkAlwaysOnTop); gy += ROW_H;



            _chkHideTitlebar = new CheckBox
            {
                Text = Lang.T("Hide title bar"),
                Left = lx, Top = gy + 6, AutoSize = true,
                ForeColor = Color.FromArgb(41, 128, 185), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            };
            grpWnd.Controls.Add(_chkHideTitlebar); gy += ROW_H;



            // ── Layout file group ─────────────────────────────────────
            int fileH = HDR_H + PAD + ROW_H + PAD;
            var grpFile = AddGroup(() => Lang.T("Layout file"), leftX, leftY, colW, fileH,
                                   Color.FromArgb(39, 174, 96));
            leftY += fileH + gap;

            int fbw = (colW - PAD * 2 - gap * 2) / 3;
            _btnSaveFile   = MakeFileBtn(Lang.T("💾 Save"),     grpFile, PAD,                   HDR_H + PAD, fbw);
            _btnSaveAsFile = MakeFileBtn(Lang.T("💾 Save As…"), grpFile, PAD + fbw + gap,       HDR_H + PAD, fbw);
            _btnLoadFile   = MakeFileBtn(Lang.T("📂 Load…"),    grpFile, PAD + fbw * 2 + gap*2, HDR_H + PAD, fbw);
            _btnSaveFile.Click   += (s, e) => { Apply(); _onSave?.Invoke(); };
            _btnSaveAsFile.Click += (s, e) => { Apply(); _onSaveAs?.Invoke(); };
            _btnLoadFile.Click   += (s, e) => { _onLoad?.Invoke(); };

            // ── Accessibility group ───────────────────────────────────
            int accH = HDR_H + PAD + ROW_H + PAD;
            var grpAcc = AddGroup(() => Lang.T("Accessibility"), leftX, leftY, colW, accH,
                                  Color.FromArgb(155, 89, 182));
            leftY += accH + gap;

            _chkStickyMods = new CheckBox
            {
                Text = Lang.T("Sticky modifiers"),
                Left = PAD, Top = HDR_H + PAD + 6, AutoSize = true,
                ForeColor = Color.FromArgb(155, 89, 182), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            };
            grpAcc.Controls.Add(_chkStickyMods);

            // ── RIGHT COLUMN: Key Style + Border ──────────────────────
            int rightY = margin;

            // Key Style: Font, Font size, Font color, Key color, Apply checkbox
            int styleH = HDR_H + PAD + 7 * ROW_H + 34 + PAD;  // +2 rows for border
            var grpStyle = AddGroup(() => Lang.T("Default Key Style"), rightX, rightY, colW, styleH,
                                     Color.FromArgb(39, 174, 96));
            rightY += styleH + gap;

            int slx = PAD, svx = 165, svw = colW - slx - svx - PAD;
            gy = HDR_H + PAD;

            AddFieldLabel(grpStyle, () => Lang.T("Font"), slx, gy);
            _cmbFont = new ComboBox
            {
                Left = svx, Top = gy, Width = svw,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Color.FromArgb(30,40,50),
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
            };
            _cmbFont.Items.AddRange(GetInstalledFonts().ToArray<object>());
            _cmbFont.SelectedIndexChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_cmbFont); gy += ROW_H;

            AddFieldLabel(grpStyle, () => Lang.T("Font size"), slx, gy);
            _nudFontSize = new NumericUpDown
            {
                Left = svx, Top = gy, Width = 65, Minimum = 0, Maximum = 72,
                BackColor = C_INPUT_BG, ForeColor = Color.FromArgb(30,40,50), Font = F_INPUT,
            };
            _nudFontSize.ValueChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_nudFontSize);
            _chkAutoSize = new CheckBox
            {
                Text = Lang.T("Auto"), Left = svx + 71, Top = gy + 6,
                AutoSize = true, ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            _chkAutoSize.CheckedChanged += (s, e) => { _nudFontSize.Enabled = !_chkAutoSize.Checked; Refresh2(); };
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
                Left = svx, Top = gy, Width = 65, Minimum = 0, Maximum = 10,
                BackColor = C_INPUT_BG, ForeColor = Color.FromArgb(30,40,50), Font = F_INPUT,
            };
            _nudBorderThickness.ValueChanged += (s, e) => Refresh2();
            grpStyle.Controls.Add(_nudBorderThickness);
            gy += ROW_H;

            // Preview inside the style group — label + key-sized panel
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
                Text = "Abc", TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill, Font = new Font("Arial", 13f, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#E0E0FF"),
                BackColor = ColorTranslator.FromHtml("#2D2D4A"),
            };
            _pnlPreview.Controls.Add(_lblPreviewKey);
            gy += ROW_H;

            _chkApplyToKeys = new CheckBox
            {
                Text = Lang.T("Apply to all keys"),
                Left = slx, Top = gy, AutoSize = true, Checked = true,
                ForeColor = Color.FromArgb(39, 174, 96), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            };
            grpStyle.Controls.Add(_chkApplyToKeys);



            // ── Action buttons ────────────────────────────────────────
            int btnTop = Math.Max(leftY, rightY) + gap;
            int bw     = (formW - gap) / 2;
            _btnCancel = MakeActionBtn(Lang.T("✖ Cancel"), C_BTN_CANCEL, margin,        btnTop, bw, 40);
            _btnApply  = MakeActionBtn(Lang.T("✔ Apply"),  C_BTN_OK,     margin+bw+gap, btnTop, bw, 40);
            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            ClientSize = new Size(ClientSize.Width, btnTop + 40 + margin);
        }

        // ── Helpers ───────────────────────────────────────────────────
        private Panel AddGroup(Func<string> getTitle, int x, int y, int w, int h, Color hdrColor)
        {
            var pnl = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = C_PANEL_BG };
            pnl.Paint += (s, e) =>
            {
                e.Graphics.FillRectangle(new SolidBrush(hdrColor), 0, 0, pnl.Width, HDR_H);
                e.Graphics.DrawString(getTitle(), F_HEADER, Brushes.White, 10, (HDR_H - F_HEADER.Height) / 2);
                e.Graphics.DrawRectangle(new Pen(C_BORDER), 0, 0, pnl.Width - 1, pnl.Height - 1);
            };
            Controls.Add(pnl);
            _transGroups.Add((pnl, getTitle));
            return pnl;
        }

        private Panel AddColorRow(Panel parent, int x, int y, int totalW)
        {
            int sw = 32;
            var txtHex = new TextBox
            {
                Left = x, Top = y, Width = totalW - sw - 5,
                BackColor = C_INPUT_BG, ForeColor = Color.FromArgb(30,40,50),
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

        private string GetHex(Panel s) => (s.Tag is TextBox t) ? t.Text : "";
        private void SetHex(Panel s, string hex)
        {
            if (s.Tag is TextBox t) { t.Text = hex; s.BackColor = ParseColor(hex, s.BackColor); }
        }

        private void AddFieldLabel(Panel parent, Func<string> getText, int x, int y)
        {
            var lbl = new Label
            {
                Text = getText(), Left = x, Top = y + 6, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            parent.Controls.Add(lbl);
            _transLabels.Add((lbl, getText));
        }

        private Button MakeFileBtn(string text, Panel parent, int x, int y, int w)
        {
            var btn = new Button
            {
                Text = text, Left = x, Top = y, Width = w, Height = ROW_H - 8,
                BackColor = Color.FromArgb(39, 174, 96), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TabStop = false,
            };
            btn.FlatAppearance.BorderSize = 0;
            parent.Controls.Add(btn);
            return btn;
        }

        private Button MakeActionBtn(string text, Color bg, int x, int y, int w, int h)
        {
            var btn = new Button
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                BackColor = bg, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = F_BTN, TabStop = false,
            };
            btn.FlatAppearance.BorderSize = 0;
            Controls.Add(btn);
            return btn;
        }

        // ── Populate ──────────────────────────────────────────────────
        private void PopulateFields(GlobalSettings g)
        {
            // Opacity: slider 0 = opaque (Opacity=1.0), slider 80 = 20% opacity (minimum)
            int opacitySlider = (int)Math.Round((1.0 - Math.Clamp(g.Opacity, 0.2, 1.0)) * 100);
            _trkOpacity.Value   = Math.Clamp(opacitySlider, 0, 80);
            SetHex(_pnlBgColor,    SettingsManager.Hex(g.BackgroundColor));


            _chkAlwaysOnTop.Checked  = g.AlwaysOnTop;
            _chkStickyMods.Checked   = g.StickyModifiers;
            _chkHideTitlebar.Checked = g.HideTitlebar;

            int fi = _cmbFont.Items.IndexOf(g.FontName);
            _cmbFont.SelectedIndex = fi >= 0 ? fi : 0;

            if (g.FontSize > 0)
            { _nudFontSize.Value = g.FontSize; _chkAutoSize.Checked = false; _nudFontSize.Enabled = true; }
            else
            { _nudFontSize.Value = 0; _chkAutoSize.Checked = true; _nudFontSize.Enabled = false; }

            SetHex(_pnlFontColor,   SettingsManager.Hex(g.FontColor));
            SetHex(_pnlKeyColor,    SettingsManager.Hex(g.KeyColor));
            SetHex(_pnlBorderColor, SettingsManager.Hex(g.BorderColor));
            _nudBorderThickness.Value = g.BorderThickness;
            Refresh2();
        }

        // ── Live preview ──────────────────────────────────────────────
        private Font _previewFont;

        private void Refresh2()
        {
            Color fc = ParseColor(GetHex(_pnlFontColor),  ColorTranslator.FromHtml("#E0E0FF"));
            Color kc = ParseColor(GetHex(_pnlKeyColor),   ColorTranslator.FromHtml("#2D2D4A"));
            Color bc = ParseColor(GetHex(_pnlBorderColor),ColorTranslator.FromHtml("#3C3C5A"));
            string fn = _cmbFont.SelectedItem?.ToString() ?? "Arial";
            int    fs = (_chkAutoSize.Checked || _nudFontSize.Value == 0) ? 13 : (int)_nudFontSize.Value;
            int    bt = (int)_nudBorderThickness.Value;

            _lblPreviewKey.ForeColor = fc;
            _lblPreviewKey.BackColor = kc;
            _pnlPreview.BackColor    = bc;
            _pnlPreview.Padding      = new Padding(Math.Max(0, bt));
            try
            {
                var newFont = new Font(fn, fs, FontStyle.Bold);
                _previewFont?.Dispose();
                _previewFont = newFont;
                _lblPreviewKey.Font = _previewFont;
            }
            catch { }
        }

        // ── Apply ─────────────────────────────────────────────────────
        private void Apply()
        {
            // Collect the fields this editor controls
            ResultGlobal = new GlobalSettings
            {
                BackgroundColor = ParseColor(GetHex(_pnlBgColor),    ColorTranslator.FromHtml("#1A1A2E")),
                // Opacity: slider 0 = opaque (1.0), slider 80 = 20% opacity (minimum 0.2)
                Opacity         = Math.Clamp((100 - _trkOpacity.Value) / 100.0, 0.2, 1.0),
                FontName        = _cmbFont.SelectedItem?.ToString() ?? "Arial",
                FontSize        = (_chkAutoSize.Checked || _nudFontSize.Value == 0) ? 0 : (int)_nudFontSize.Value,
                FontColor       = ParseColor(GetHex(_pnlFontColor),  ColorTranslator.FromHtml("#E0E0FF")),
                KeyColor        = ParseColor(GetHex(_pnlKeyColor),   ColorTranslator.FromHtml("#2D2D4A")),
                BorderColor     = ParseColor(GetHex(_pnlBorderColor),ColorTranslator.FromHtml("#3C3C5A")),
                BorderThickness = (int)_nudBorderThickness.Value,
                // Preserve fields that this editor does not expose
                Language        = _src.Language,
                LastFile        = _src.LastFile,
                WindowWidth     = _src.WindowWidth,
                WindowHeight    = _src.WindowHeight,
                HideTitlebar    = _chkHideTitlebar.Checked,
                AlwaysOnTop     = _chkAlwaysOnTop.Checked,
                StickyModifiers = _chkStickyMods.Checked,
            };
            ApplyToKeys  = _chkApplyToKeys.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Color ParseColor(string hex, Color fallback) => SettingsManager.ParseColor(hex, fallback);

        private static GlobalSettings Clone(GlobalSettings g) => new GlobalSettings
        {
            BackgroundColor = g.BackgroundColor, Opacity         = g.Opacity,
            FontName        = g.FontName,         FontSize        = g.FontSize,
            FontColor       = g.FontColor,        KeyColor        = g.KeyColor,
            BorderColor     = g.BorderColor,      BorderThickness = g.BorderThickness,
            HideTitlebar    = g.HideTitlebar,
            AlwaysOnTop     = g.AlwaysOnTop,
            StickyModifiers = g.StickyModifiers,
        };

        private static List<string> GetInstalledFonts()
        {
            var list = new List<string>();
            using (var ifc = new InstalledFontCollection())
                foreach (var ff in ifc.Families) list.Add(ff.Name);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private class LangItem
        {
            public string Code { get; }
            public string Name { get; }
            public LangItem(string code, string name) { Code = code; Name = name; }
            public override string ToString() => Name;
        }
    }
}

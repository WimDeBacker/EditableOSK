using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Modal dialog for creating, renaming, deleting and styling named key groups.
    /// Returns the modified list via <see cref="ResultGroups"/> on OK.
    /// </summary>
    public class GroupEditorForm : Form
    {
        public List<KeyGroup> ResultGroups { get; private set; }

        // ── Theme (WinUI 3) ───────────────────────────────────────────
        private static Color C_BG       => Fluent.BgPage;
        private static Color C_PANEL_BG => Fluent.BgCard;
        private static Color C_BORDER   => Fluent.BorderCard;
        private static Color C_LBL      => Fluent.TextPrimary;
        private static Color C_INPUT_BG => Fluent.BgInput;
        private static Color C_OK       => Fluent.Success;
        private static Color C_CANCEL   => Fluent.Danger;
        private static Color C_ADD      => Fluent.Accent;
        private static Color C_DEL      => Fluent.Danger;
        private static Font  F_LABEL    => Fluent.FontLabel;
        private static Font  F_INPUT    => Fluent.FontInput;
        private static Font  F_HEADER   => Fluent.FontTitle;
        private static Font  F_BTN      => Fluent.FontBtnLg;
        private const int PAD = 14;
        private const int ROW = 50;

        // ── Working copy of groups ────────────────────────────────────
        // Each entry is a clone; changes are applied only on OK.
        private readonly List<KeyGroup> _groups;

        // ── Controls ─────────────────────────────────────────────────
        private ListBox       _lstGroups;
        private Button        _btnAdd, _btnDelete;
        private TextBox       _txtName;
        private Panel         _pnlKeyColor, _pnlFontColor, _pnlBorderColor;
        private NumericUpDown _nudBorderThickness;
        private ComboBox      _cmbFont;
        private NumericUpDown _nudFontSize;
        private Button        _btnOK, _btnCancel, _btnImport;

        // Suppress detail-panel SelectedIndexChanged during programmatic list rebuild
        private bool _loading = false;

        // Index of the group whose settings are currently shown in the detail panel.
        // Used to commit edits back to the correct group before switching to another.
        private int _prevIdx = -1;

        // ── Constructor ───────────────────────────────────────────────
        public GroupEditorForm(List<KeyGroup> groups)
        {
            _groups = groups.Select(g => g.Clone()).ToList();

            Text            = Lang.T("Manage Groups");
            BackColor       = C_BG;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            ShowIcon    = false;
            StartPosition   = FormStartPosition.CenterParent;
            Size            = new Size(880, 610);
            TopMost         = true;
            Font            = F_LABEL;

            BuildUI();
            RebuildList(0);
        }

        // ── UI construction ───────────────────────────────────────────
        private void BuildUI()
        {
            int formW  = ClientSize.Width  - PAD * 2;
            int listW  = 300;
            int detailX = PAD + listW + PAD;
            int detailW = formW - listW - PAD;
            int btnAreaH = 46;
            int innerH   = ClientSize.Height - PAD * 2 - btnAreaH - PAD;

            // ── List panel (left) ─────────────────────────────────────
            var pnlList = AddPanel(PAD, PAD, listW, innerH, Lang.T("Groups"), Color.FromArgb(41, 128, 185));

            _lstGroups = new ListBox
            {
                Left = PAD, Top = 36 + PAD,
                Width = listW - PAD * 2,
                Height = innerH - 36 - PAD * 2 - 34 - 4 - 34 - PAD,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, BorderStyle = BorderStyle.FixedSingle,
            };
            _lstGroups.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                CommitTo(_prevIdx);   // persist edits on the group that was shown
                LoadDetail();
            };
            pnlList.Controls.Add(_lstGroups);

            int btnY = _lstGroups.Bottom + PAD;
            int halfW = (listW - PAD * 2 - 4) / 2;
            _btnAdd    = MakeSmallBtn(Lang.T("+ Add group"),    PAD,             btnY, halfW, 34);
            _btnDelete = MakeSmallBtn(Lang.T("− Delete group"), PAD + halfW + 4, btnY, halfW, 34);
            _btnAdd.Click    += OnAdd;
            _btnDelete.Click += OnDelete;
            pnlList.Controls.Add(_btnAdd);
            pnlList.Controls.Add(_btnDelete);

            int btnImportY = btnY + 34 + 4;
            _btnImport = MakeSmallBtn(Lang.T("Import..."), PAD, btnImportY, listW - PAD * 2, 34);
            _btnImport.Click += OnImport;
            pnlList.Controls.Add(_btnImport);

            // ── Detail panel (right) ──────────────────────────────────
            var pnlDetail = AddPanel(detailX, PAD, detailW, innerH, Lang.T("Style"), Color.FromArgb(39, 174, 96));

            int lx = PAD, vx = 180, vw = detailW - lx - vx - PAD;
            int gy = 36 + PAD;

            AddLabel(pnlDetail, Lang.T("Name"), lx, gy);
            _txtName = new TextBox
            {
                Left = vx, Top = gy, Width = vw,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, BorderStyle = BorderStyle.FixedSingle,
            };
            _txtName.TextChanged += (s, e) => SaveCurrentName();
            pnlDetail.Controls.Add(_txtName); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Key color"), lx, gy);
            _pnlKeyColor = AddColorSwatch(pnlDetail, vx, gy, vw); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Font color"), lx, gy);
            _pnlFontColor = AddColorSwatch(pnlDetail, vx, gy, vw); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Border color"), lx, gy);
            _pnlBorderColor = AddColorSwatch(pnlDetail, vx, gy, vw); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Border thickness"), lx, gy);
            _nudBorderThickness = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = -1, Maximum = 10,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            AddSmallHint(pnlDetail, Lang.T("-1 = inherit global"), vx + 71, gy);
            pnlDetail.Controls.Add(_nudBorderThickness); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Font"), lx, gy);
            _cmbFont = new ComboBox
            {
                Left = vx, Top = gy, Width = vw,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
            };
            _cmbFont.Items.Add(Lang.T("(inherit global)"));
            foreach (var fn in GetInstalledFonts()) _cmbFont.Items.Add(fn);
            _cmbFont.SelectedIndex = 0;
            pnlDetail.Controls.Add(_cmbFont); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Font size"), lx, gy);
            _nudFontSize = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 0, Maximum = 72,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            AddSmallHint(pnlDetail, Lang.T("0 = auto / inherit"), vx + 71, gy);
            pnlDetail.Controls.Add(_nudFontSize);

            // ── OK / Cancel ───────────────────────────────────────────
            int btnY2 = ClientSize.Height - PAD - 40;
            int bw    = (formW - PAD) / 2;
            _btnCancel = MakeBigBtn(Lang.T("Cancel"), PAD,            btnY2, bw, 40);
            _btnOK     = MakeBigBtn(Lang.T("Apply"),  PAD + bw + PAD, btnY2, bw, 40);
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnOK.Click     += (s, e) => { CommitCurrent(); ResultGroups = _groups; DialogResult = DialogResult.OK; Close(); };
        }

        // ── List management ───────────────────────────────────────────
        private void RebuildList(int selectIndex)
        {
            _loading = true;
            _lstGroups.Items.Clear();
            foreach (var g in _groups) _lstGroups.Items.Add(g.Name);
            _loading = false;

            if (_groups.Count > 0)
            {
                _lstGroups.SelectedIndex = Math.Clamp(selectIndex, 0, _groups.Count - 1);
                LoadDetail();
            }
            else
            {
                ClearDetail();
            }
            UpdateEnabled();
        }

        private void LoadDetail()
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) { ClearDetail(); _prevIdx = -1; return; }
            _prevIdx = idx;   // remember which group is now displayed
            var g = _groups[idx];

            _loading = true;
            _txtName.Text = g.Name;
            SetSwatchColor(_pnlKeyColor,    g.KeyColor.IsEmpty   ? Color.Empty : g.KeyColor);
            SetSwatchColor(_pnlFontColor,   g.FontColor.IsEmpty  ? Color.Empty : g.FontColor);
            SetSwatchColor(_pnlBorderColor, g.BorderColor.IsEmpty? Color.Empty : g.BorderColor);
            _nudBorderThickness.Value = Math.Clamp(g.BorderThickness, -1, 10);

            int fi = _cmbFont.Items.IndexOf(g.FontName ?? "");
            _cmbFont.SelectedIndex = fi > 0 ? fi : 0;

            _nudFontSize.Value = Math.Clamp(g.FontSize, 0, 72);
            _loading = false;

            SetDetailEnabled(true);
        }

        private void ClearDetail()
        {
            _txtName.Text = "";
            SetSwatchColor(_pnlKeyColor, Color.Empty);
            SetSwatchColor(_pnlFontColor, Color.Empty);
            SetSwatchColor(_pnlBorderColor, Color.Empty);
            _nudBorderThickness.Value = -1;
            _cmbFont.SelectedIndex = 0;
            _nudFontSize.Value = 0;
            SetDetailEnabled(false);
        }

        // Commits the currently displayed UI state to the group at the given index.
        // Called with _prevIdx before switching to another group, or with the
        // current index just before closing with OK.
        private void CommitTo(int idx)
        {
            if (idx < 0 || idx >= _groups.Count) return;
            var g = _groups[idx];
            g.Name            = _txtName.Text.Trim();
            g.KeyColor        = GetSwatchColor(_pnlKeyColor);
            g.FontColor       = GetSwatchColor(_pnlFontColor);
            g.BorderColor     = GetSwatchColor(_pnlBorderColor);
            g.BorderThickness = (int)_nudBorderThickness.Value;
            string fname      = _cmbFont.SelectedIndex > 0 ? _cmbFont.SelectedItem?.ToString() ?? "" : "";
            g.FontName        = fname;
            g.FontSize        = (int)_nudFontSize.Value;
            // Keep list display in sync with any name edits
            _loading = true;
            if (idx < _lstGroups.Items.Count) _lstGroups.Items[idx] = g.Name;
            _loading = false;
        }

        // Commits the currently displayed group (convenience wrapper).
        private void CommitCurrent() => CommitTo(_prevIdx);

        private void SaveCurrentName()
        {
            if (_loading) return;
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return;
            string newName = _txtName.Text.Trim();
            _groups[idx].Name = newName;
            _loading = true;
            _lstGroups.Items[idx] = newName;
            _loading = false;
        }

        private void OnAdd(object sender, EventArgs e)
        {
            CommitCurrent();
            string name = GetNewName();
            if (name == null) return;
            _groups.Add(new KeyGroup { Name = name, BorderThickness = -1 });
            RebuildList(_groups.Count - 1);
        }

        private void OnDelete(object sender, EventArgs e)
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return;
            string name = _groups[idx].Name;
            if (MessageBox.Show(string.Format(Lang.T("Delete group msg"), name),
                    Lang.T("Delete Group"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _groups.RemoveAt(idx);
            _prevIdx = -1;   // group is gone — nothing to commit
            RebuildList(Math.Max(0, idx - 1));
        }

        private string GetNewName()
        {
            using var dlg = new Form
            {
                Text = Lang.T("New Group"), Size = new Size(340, 130),
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false, MinimizeBox = false, ShowIcon = false,
                StartPosition = FormStartPosition.CenterParent,
                TopMost = true, BackColor = C_BG,
            };
            var txt = new TextBox { Left = 12, Top = 14, Width = 300, Font = F_INPUT, BorderStyle = BorderStyle.FixedSingle };
            var ok = new FluentButton { Text = Lang.T("Apply"),  Left = 12,  Top = 52, Width = 140, Height = 32, Style = FluentButton.Variant.Neutral };
            var cn = new FluentButton { Text = Lang.T("Cancel"), Left = 172, Top = 52, Width = 140, Height = 32, Style = FluentButton.Variant.Neutral };
            ok.Click += (s, e2) => { if (!string.IsNullOrWhiteSpace(txt.Text)) { dlg.DialogResult = DialogResult.OK; dlg.Close(); } };
            cn.Click += (s, e2) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };
            dlg.AcceptButton = ok;
            dlg.Controls.AddRange(new Control[] { txt, ok, cn });
            return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text.Trim() : null;
        }

        private void UpdateEnabled()
        {
            bool any = _groups.Count > 0;
            _btnDelete.Enabled = any;
            SetDetailEnabled(any);
        }

        private void SetDetailEnabled(bool en)
        {
            _txtName.Enabled = _pnlKeyColor.Enabled = _pnlFontColor.Enabled =
            _pnlBorderColor.Enabled = _nudBorderThickness.Enabled =
            _cmbFont.Enabled = _nudFontSize.Enabled = en;
        }

        // ── Color swatch helpers ──────────────────────────────────────
        private Panel AddColorSwatch(Panel parent, int x, int y, int w)
        {
            var pnl = new Panel
            {
                Left = x, Top = y + 4, Width = Math.Min(w, 120), Height = 32,
                BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand,
                BackColor = Fluent.Neutral,
            };
            var lbl = new Label
            {
                Text = Lang.T("(inherit)"), Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Fluent.TextHint, BackColor = Color.Transparent,
                Font = Fluent.FontHint,
            };
            pnl.Controls.Add(lbl);

            pnl.Click += (s, e) => PickColor(pnl);
            lbl.Click += (s, e) => PickColor(pnl);

            // Right-click to clear (revert to "inherit")
            var ctxMenu = new ContextMenuStrip();
            ctxMenu.Items.Add(Lang.T("Clear (inherit global)")).Click += (s, e2) => SetSwatchColor(pnl, Color.Empty);
            pnl.ContextMenuStrip = lbl.ContextMenuStrip = ctxMenu;

            parent.Controls.Add(pnl);
            return pnl;
        }

        private void PickColor(Panel pnl)
        {
            using var cd = new ColorDialog { FullOpen = true };
            Color current = GetSwatchColor(pnl);
            if (!current.IsEmpty) cd.Color = current;
            if (cd.ShowDialog(this) == DialogResult.OK)
                SetSwatchColor(pnl, cd.Color);
        }

        private static void SetSwatchColor(Panel pnl, Color c)
        {
            var lbl = pnl.Controls.OfType<Label>().FirstOrDefault();
            if (c.IsEmpty)
            {
                pnl.BackColor = Fluent.Neutral;
                if (lbl != null) { lbl.Visible = true; lbl.ForeColor = Fluent.TextHint; }
            }
            else
            {
                pnl.BackColor = c;
                if (lbl != null) lbl.Visible = false;
            }
            pnl.Tag = c.IsEmpty ? null : (object)c;
        }

        private static Color GetSwatchColor(Panel pnl) =>
            pnl.Tag is Color c ? c : Color.Empty;

        // ── UI helpers ────────────────────────────────────────────────
        private Panel AddPanel(int x, int y, int w, int h, string title, Color accentColor)
        {
            var pnl = new Panel
            {
                Left = x, Top = y, Width = w, Height = h,
                BackColor = Fluent.BgPage,
                BorderStyle = BorderStyle.None,
            };
            pnl.Paint += (s, e) =>
                FluentPainter.PaintCard(e.Graphics, pnl.Width, pnl.Height, title, accentColor, 36);
            Controls.Add(pnl);
            return pnl;
        }

        private void AddLabel(Panel parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text, Left = x, Top = y + 6, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            });
        }

        private void AddSmallHint(Panel parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text, Left = x, Top = y + 14, AutoSize = true,
                ForeColor = Fluent.TextHint, BackColor = Color.Transparent,
                Font = Fluent.FontHint,
            });
        }

        private Button MakeSmallBtn(string text, int x, int y, int w, int h)
        {
            var b = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                Style = FluentButton.Variant.Neutral,
            };
            return b;
        }

        private Button MakeBigBtn(string text, int x, int y, int w, int h)
        {
            var b = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                Style = FluentButton.Variant.Neutral,
            };
            Controls.Add(b);
            return b;
        }

        private static List<string> GetInstalledFonts()
        {
            var result = new List<string>();
            using var ifc = new System.Drawing.Text.InstalledFontCollection();
            foreach (var ff in ifc.Families) result.Add(ff.Name);
            return result;
        }

        // ── Import groups ─────────────────────────────────────────────
        private enum ImportAction { Add, Overwrite, AddNew, Skip }

        private void OnImport(object sender, EventArgs e)
        {
            CommitCurrent();

            using var ofd = new OpenFileDialog
            {
                Title = Lang.T("Select a layout file to import groups from"),
                Filter = "Keyboard layout (*.xml)|*.xml|All files (*.*)|*.*",
                RestoreDirectory = true,
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            var imported = SettingsManager.LoadGroupsFromFile(ofd.FileName);
            if (imported.Count == 0)
            {
                MessageBox.Show(
                    Lang.T("No groups found in the selected file."),
                    Lang.T("Import Groups"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var existing = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            var decisions = ShowImportResolutionDialog(imported, existing);
            if (decisions == null) return;

            var usedNames = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var (group, action) in decisions)
            {
                switch (action)
                {
                    case ImportAction.Overwrite:
                    {
                        int idx = _groups.FindIndex(g =>
                            string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0) _groups[idx] = group.Clone();
                        break;
                    }
                    case ImportAction.AddNew:
                    {
                        string newName = GetUniqueName(group.Name, usedNames);
                        var clone = group.Clone();
                        clone.Name = newName;
                        _groups.Add(clone);
                        usedNames.Add(newName);
                        break;
                    }
                    case ImportAction.Add:
                    {
                        _groups.Add(group.Clone());
                        usedNames.Add(group.Name);
                        break;
                    }
                }
            }

            RebuildList(Math.Max(0, _lstGroups.SelectedIndex));
        }

        private List<(KeyGroup group, ImportAction action)> ShowImportResolutionDialog(
            List<KeyGroup> imported, HashSet<string> existing)
        {
            string actAdd       = Lang.T("Add");
            string actOverwrite = Lang.T("Overwrite");
            string actAddNew    = Lang.T("Add as new");
            string actSkip      = Lang.T("Skip");

            bool anyConflict = imported.Any(g => existing.Contains(g.Name));

            const int DLG_W = 580;
            int dgvH  = Math.Clamp(imported.Count * 34 + 38, 160, 380);
            int dlgH  = PAD + 30 + PAD + dgvH + PAD + 44 + PAD + 16;

            using var dlg = new Form
            {
                Text = Lang.T("Import Groups"),
                Size = new Size(DLG_W, dlgH),
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false, MinimizeBox = false, ShowIcon = false,
                StartPosition = FormStartPosition.CenterParent,
                TopMost = true, BackColor = C_BG, Font = F_LABEL,
            };

            string info = anyConflict
                ? string.Format(Lang.T("{0} groups found — choose action for each conflict:"), imported.Count)
                : string.Format(Lang.T("{0} groups found — all new, no conflicts."), imported.Count);
            dlg.Controls.Add(new Label
            {
                Text = info, Left = PAD, Top = PAD,
                Width = DLG_W - PAD * 2, Height = 26,
                Font = F_LABEL, ForeColor = C_LBL, BackColor = Color.Transparent,
            });

            var dgv = new DataGridView
            {
                Left = PAD, Top = PAD + 30,
                Width = DLG_W - PAD * 2, Height = dgvH,
                RowHeadersVisible = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = C_PANEL_BG, BorderStyle = BorderStyle.FixedSingle,
                Font = F_INPUT, EditMode = DataGridViewEditMode.EditOnEnter,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ScrollBars = ScrollBars.Vertical,
            };
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Fluent.Accent;
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font      = F_HEADER;
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dgv.RowTemplate.Height = 38;
            dgv.DataError += (s, ev) => ev.Cancel = true;

            var colName   = new DataGridViewTextBoxColumn    { HeaderText = Lang.T("Group"),  ReadOnly = true, FillWeight = 35 };
            var colStatus = new DataGridViewTextBoxColumn    { HeaderText = Lang.T("Status"), ReadOnly = true, FillWeight = 18 };
            var colAction = new DataGridViewComboBoxColumn   { HeaderText = Lang.T("Action"), FillWeight = 47,
                                                               DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
            dgv.Columns.AddRange(colName, colStatus, colAction);

            foreach (var g in imported)
            {
                bool conflict = existing.Contains(g.Name);
                var row = new DataGridViewRow();
                row.CreateCells(dgv);
                row.Cells[0].Value = g.Name;
                row.Cells[1].Value = conflict ? Lang.T("Conflict") : Lang.T("New");
                row.Tag = g;

                var cb = (DataGridViewComboBoxCell)row.Cells[2];
                if (conflict)
                {
                    cb.Items.AddRange(new[] { actOverwrite, actAddNew, actSkip });
                    cb.Value = actSkip;
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 243, 228);
                }
                else
                {
                    cb.Items.Add(actAdd);
                    cb.Value = actAdd;
                    row.DefaultCellStyle.BackColor = Color.FromArgb(235, 252, 240);
                }
                dgv.Rows.Add(row);
            }
            dlg.Controls.Add(dgv);

            int btnY3 = PAD + 30 + dgvH + PAD;
            int bw    = (DLG_W - PAD * 3) / 2;

            var btnCancel3 = new FluentButton { Text = Lang.T("Cancel"), Left = PAD,          Top = btnY3, Width = bw, Height = 40, Style = FluentButton.Variant.Neutral };
            var btnOK3     = new FluentButton { Text = Lang.T("Import"), Left = PAD * 2 + bw, Top = btnY3, Width = bw, Height = 40, Style = FluentButton.Variant.Neutral };
            btnCancel3.Click += (s, ev) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };
            btnOK3.Click     += (s, ev) => { dlg.DialogResult = DialogResult.OK;     dlg.Close(); };
            dlg.AcceptButton = btnOK3;
            dlg.Controls.Add(btnCancel3);
            dlg.Controls.Add(btnOK3);

            if (dlg.ShowDialog(this) != DialogResult.OK) return null;

            var result = new List<(KeyGroup, ImportAction)>();
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                var g       = (KeyGroup)row.Tag;
                bool conflict = existing.Contains(g.Name);
                string val  = row.Cells[2].Value?.ToString() ?? "";

                ImportAction action;
                if (!conflict)             action = ImportAction.Add;
                else if (val == actOverwrite) action = ImportAction.Overwrite;
                else if (val == actAddNew)    action = ImportAction.AddNew;
                else                          action = ImportAction.Skip;

                result.Add((g, action));
            }
            return result;
        }

        private static string GetUniqueName(string baseName, HashSet<string> usedNames)
        {
            if (!usedNames.Contains(baseName)) return baseName;
            int n = 2;
            while (usedNames.Contains($"{baseName} {n}")) n++;
            return $"{baseName} {n}";
        }
    }
}

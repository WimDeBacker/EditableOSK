using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A modal dialog that lets the user create, rename, delete, and style named key groups.
    ///
    /// A "key group" is a named category that can be assigned to one or more keys on the
    /// on-screen keyboard layout. Each group can carry its own colours, font, and border
    /// settings so that visually related keys share a consistent look without having to
    /// set those properties key by key.
    ///
    /// The dialog works on a private working copy of the group list so that cancelling
    /// leaves the original data untouched. When the user clicks "Apply", the modified
    /// list is exposed through <see cref="ResultGroups"/> and the dialog closes with
    /// <see cref="DialogResult.OK"/>.
    /// </summary>
    public class GroupEditorForm : Form
    {
        /// <summary>
        /// The modified list of groups that should replace the caller's original list.
        /// This property is only set when the dialog closes with <see cref="DialogResult.OK"/>;
        /// if the user cancels it remains <c>null</c>.
        /// </summary>
        public List<KeyGroup> ResultGroups { get; private set; }

        // ── Theme (WinUI 3) ───────────────────────────────────────────
        // All colours and fonts are sourced from the static Fluent helper so that
        // every dialog in the application shares the same WinUI 3-inspired palette.
        // True when the toolbar is in dark mode — dialogs follow the same theme.
        private readonly bool _dark;

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

        /// <summary>Standard padding (in pixels) used between all controls and panel edges.</summary>
        private const int PAD = 14;

        /// <summary>
        /// Vertical spacing (in pixels) between successive rows in the detail panel.
        /// Each label + its input control occupies one ROW of vertical space.
        /// </summary>
        private const int ROW = 50;

        // ── Working copy of groups ────────────────────────────────────
        /// <summary>
        /// Private working copy of the group list. Every entry is a deep clone of the
        /// caller's original, so cancelling the dialog leaves the source data unchanged.
        /// Edits are only written back to the caller via <see cref="ResultGroups"/> when
        /// the user clicks "Apply".
        /// </summary>
        private readonly List<KeyGroup> _groups;

        // ── Controls ─────────────────────────────────────────────────
        /// <summary>Shows the names of all groups so the user can select one to edit.</summary>
        private ListBox       _lstGroups;

        /// <summary>Buttons that add a brand-new group or delete the selected one.</summary>
        private Button        _btnAdd, _btnDelete;

        /// <summary>Editable name of the currently selected group.</summary>
        private TextBox       _txtName;

        /// <summary>
        /// Clickable colour swatches for the key background, key label font, and key border.
        /// Each panel shows the chosen colour as its background, or a neutral "(inherit)"
        /// label when no colour is set for this group.
        /// </summary>
        private Panel         _pnlKeyColor, _pnlFontColor, _pnlBorderColor;
        /// <summary>Hex text boxes paired with each colour swatch; kept as fields so
        /// <see cref="SetDetailEnabled"/> can enable/disable them alongside the swatches.</summary>
        private TextBox       _txtKeyColorHex, _txtFontColorHex, _txtBorderColorHex;

        /// <summary>
        /// Spinner for the border thickness in pixels.
        /// The special value <c>-1</c> means "inherit the global setting".
        /// </summary>
        private NumericUpDown _nudBorderThickness;

        /// <summary>Drop-down listing every font installed on the system, plus an "(inherit global)" option at index 0.</summary>
        private ComboBox      _cmbFont;

        /// <summary>
        /// Spinner for the font size in points.
        /// The value <c>0</c> means "auto / inherit from the global setting".
        /// </summary>
        private NumericUpDown _nudFontSize;

        /// <summary>The main action buttons: confirm changes, discard changes, or import groups from a file.</summary>
        private Button        _btnOK, _btnCancel, _btnImport;

        /// <summary>
        /// Set to <c>true</c> while the code is programmatically rebuilding the list or
        /// loading a group's data into the detail panel. Event handlers that would normally
        /// save or commit UI changes check this flag and return early to avoid overwriting
        /// data with half-initialised values.
        /// </summary>
        private bool _loading = false;

        /// <summary>
        /// Tracks the index (in <see cref="_groups"/>) of whichever group is currently
        /// shown in the detail panel. Before switching to a different group, we call
        /// <see cref="CommitTo"/> with this index to write the UI values back to that
        /// group object. <c>-1</c> means nothing is currently displayed.
        /// </summary>
        private int _prevIdx = -1;

        // ── Constructor ───────────────────────────────────────────────

        /// <summary>
        /// Initialises the dialog and immediately shows the first group (if any).
        /// </summary>
        /// <param name="groups">
        /// The caller's current list of groups. The dialog clones every entry so the
        /// original objects are never modified until the user confirms with "Apply".
        /// </param>
        public GroupEditorForm(List<KeyGroup> groups)
        {
            // Deep-clone so cancelling truly discards all changes.
            _groups = groups.Select(g => g.Clone()).ToList();

            _dark = !ToolbarButton.IsLightTheme;

            AutoScaleMode       = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            Text            = Lang.T("Manage Groups");
            BackColor       = _dark ? Fluent.DarkBg : Fluent.BgPage;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            ShowIcon    = false;
            StartPosition   = FormStartPosition.CenterParent;
            Size            = new Size(880, 610);
            TopMost         = true;
            Font            = F_LABEL;

            BuildUI();
            FluentPainter.ApplyDialogTheme(this, _dark);
            // Select the first group so the detail panel is populated from the start.
            RebuildList(0);
            ActiveControl = _txtName;  // start keyboard focus on the group name field
        }

        // ── UI construction ───────────────────────────────────────────

        /// <summary>
        /// Creates and wires up every control in the dialog: the group list panel on the
        /// left, the style detail panel on the right, and the OK / Cancel buttons at the
        /// bottom. No business logic lives here — it is purely layout code.
        /// </summary>
        private void BuildUI()
        {
            int formW  = ClientSize.Width  - PAD * 2;
            int listW  = 300;                          // fixed width of the left panel
            int detailX = PAD + listW + PAD;           // x-origin of the right panel
            int detailW = formW - listW - PAD;         // remaining width for detail
            int btnAreaH = 46;                         // height reserved for OK/Cancel row
            int innerH   = ClientSize.Height - PAD * 2 - btnAreaH - PAD; // panel height

            // ── List panel (left) ─────────────────────────────────────
            var pnlList = AddPanel(PAD, PAD, listW, innerH, Lang.T("Groups"), Color.FromArgb(41, 128, 185));

            _lstGroups = new ListBox
            {
                Left = PAD, Top = 36 + PAD,
                Width = listW - PAD * 2,
                // Calculate height: panel height minus header row, two button rows, and padding.
                Height = innerH - 36 - PAD * 2 - 34 - 4 - 34 - PAD,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, BorderStyle = BorderStyle.FixedSingle,
            };
            _lstGroups.SelectedIndexChanged += (s, e) =>
            {
                // Do nothing while the code itself is programmatically selecting items.
                if (_loading) return;
                // Save any pending edits for the group that was just visible…
                CommitTo(_prevIdx);
                // …then load the newly selected group into the detail panel.
                LoadDetail();
            };
            pnlList.Controls.Add(_lstGroups);

            // Add / Delete buttons sit directly below the list box.
            int btnY = _lstGroups.Bottom + PAD;
            int halfW = (listW - PAD * 2 - 4) / 2; // split width with a 4 px gap between the two buttons
            _btnAdd    = MakeSmallBtn(Lang.T("+ Add group"),    PAD,             btnY, halfW, 34);
            _btnDelete = MakeSmallBtn(Lang.T("− Delete group"), PAD + halfW + 4, btnY, halfW, 34);
            _btnAdd.Click    += OnAdd;
            _btnDelete.Click += OnDelete;
            pnlList.Controls.Add(_btnAdd);
            pnlList.Controls.Add(_btnDelete);

            // Import button spans the full list width, one row below Add/Delete.
            int btnImportY = btnY + 34 + 4;
            _btnImport = MakeSmallBtn(Lang.T("Import..."), PAD, btnImportY, listW - PAD * 2, 34);
            _btnImport.Click += OnImport;
            pnlList.Controls.Add(_btnImport);

            // ── Detail panel (right) ──────────────────────────────────
            var pnlDetail = AddPanel(detailX, PAD, detailW, innerH, Lang.T("Style"), Color.FromArgb(39, 174, 96));

            // Layout constants for the detail rows:
            //   lx = left margin for labels
            //   vx = x-position where input controls start (after the label column)
            //   vw = width of input controls
            int lx = PAD, vx = 180, vw = detailW - lx - vx - PAD;
            int gy = 36 + PAD; // current vertical position, incremented by ROW after each field

            // ── Field order matches the Appearance column in KeyEditorForm ──────────────
            // Name → Font → Font size → Font color → Key color → Border color → Border thickness

            // Name field
            AddLabel(pnlDetail, Lang.T("Name"), lx, gy);
            _txtName = new TextBox
            {
                Left = vx, Top = gy, Width = vw,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, BorderStyle = BorderStyle.FixedSingle,
            };
            _txtName.TextChanged += (s, e) => SaveCurrentName();
            pnlDetail.Controls.Add(_txtName); gy += ROW;

            // Font family: index 0 is the special "(inherit global)" entry; real font names follow.
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

            // Font size: 0 means "auto / inherit".
            AddLabel(pnlDetail, Lang.T("Font size"), lx, gy);
            _nudFontSize = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 0, Maximum = 72,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            AddSmallHint(pnlDetail, Lang.T("0 = auto / inherit"), vx + 71, gy);
            pnlDetail.Controls.Add(_nudFontSize); gy += ROW;

            // Colour rows: hex text box + swatch button, matching KeyEditorForm's AddColorRow layout.
            // Right-click the swatch to clear the colour (revert to "inherit global").
            AddLabel(pnlDetail, Lang.T("Font color"), lx, gy);
            (_pnlFontColor, _txtFontColorHex) = AddColorRow(pnlDetail, vx, gy, vw); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Key color"), lx, gy);
            (_pnlKeyColor, _txtKeyColorHex) = AddColorRow(pnlDetail, vx, gy, vw); gy += ROW;

            AddLabel(pnlDetail, Lang.T("Border color"), lx, gy);
            (_pnlBorderColor, _txtBorderColorHex) = AddColorRow(pnlDetail, vx, gy, vw); gy += ROW;

            // Border thickness: -1 is the sentinel for "inherit from global settings".
            AddLabel(pnlDetail, Lang.T("Border thickness"), lx, gy);
            _nudBorderThickness = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = -1, Maximum = 10,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
            };
            AddSmallHint(pnlDetail, Lang.T("-1 = inherit global"), vx + 71, gy);
            pnlDetail.Controls.Add(_nudBorderThickness);

            // ── OK / Cancel ───────────────────────────────────────────
            int btnY2 = ClientSize.Height - PAD - 40;
            int bw    = (formW - PAD) / 2; // each button takes roughly half the form width
            _btnCancel = MakeBigBtn(Lang.T("Cancel"), PAD,            btnY2, bw, 40);
            _btnOK     = MakeBigBtn(Lang.T("Apply"),  PAD + bw + PAD, btnY2, bw, 40);
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnOK.Click     += (s, e) =>
            {
                // Make sure the currently displayed group's UI values are saved before
                // exposing the list to the caller.
                CommitCurrent();
                ResultGroups = _groups;
                DialogResult = DialogResult.OK;
                Close();
            };
            AcceptButton = _btnOK;
            CancelButton = _btnCancel;
        }

        // ── List management ───────────────────────────────────────────

        /// <summary>
        /// Repopulates the group list box from <see cref="_groups"/> and selects the
        /// entry at <paramref name="selectIndex"/>. If the list is empty the detail
        /// panel is cleared and the Delete button is disabled.
        /// </summary>
        /// <param name="selectIndex">
        /// The index to select after rebuilding. Automatically clamped to the valid
        /// range so callers do not have to guard against off-by-one situations (e.g.
        /// when the last item is deleted).
        /// </param>
        private void RebuildList(int selectIndex)
        {
            // Suppress SelectedIndexChanged while we repopulate to avoid spurious commits.
            _loading = true;
            _lstGroups.Items.Clear();
            foreach (var g in _groups) _lstGroups.Items.Add(g.Name);
            _loading = false;

            if (_groups.Count > 0)
            {
                // Clamp prevents an out-of-range index, e.g. after deleting the last item.
                _lstGroups.SelectedIndex = Math.Clamp(selectIndex, 0, _groups.Count - 1);
                LoadDetail();
            }
            else
            {
                // No groups left — reset the detail panel and disable editing controls.
                ClearDetail();
            }
            UpdateEnabled();
        }

        /// <summary>
        /// Reads the selected group from <see cref="_groups"/> and populates every
        /// control in the detail panel with its current values. Also records the
        /// selected index in <see cref="_prevIdx"/> so that <see cref="CommitTo"/> can
        /// write changes back to the correct group later.
        /// </summary>
        private void LoadDetail()
        {
            int idx = _lstGroups.SelectedIndex;
            // Guard: nothing selected or index out of range — just blank the panel.
            if (idx < 0 || idx >= _groups.Count) { ClearDetail(); _prevIdx = -1; return; }
            _prevIdx = idx;   // remember which group is now displayed
            var g = _groups[idx];

            // Use _loading to stop SaveCurrentName() and other change handlers from
            // writing half-loaded values back into the group while we are filling in controls.
            _loading = true;
            _txtName.Text = g.Name;
            // Color.Empty means "no colour set for this group — inherit from global settings".
            SetSwatchColor(_pnlKeyColor,    g.KeyColor.IsEmpty   ? Color.Empty : g.KeyColor);
            SetSwatchColor(_pnlFontColor,   g.FontColor.IsEmpty  ? Color.Empty : g.FontColor);
            SetSwatchColor(_pnlBorderColor, g.BorderColor.IsEmpty? Color.Empty : g.BorderColor);
            _nudBorderThickness.Value = Math.Clamp(g.BorderThickness, -1, 10);

            // IndexOf returns -1 if the font name isn't in the list (e.g. font was uninstalled).
            // In that case we fall back to index 0 ("inherit global").
            int fi = _cmbFont.Items.IndexOf(g.FontName ?? "");
            _cmbFont.SelectedIndex = fi > 0 ? fi : 0;

            _nudFontSize.Value = Math.Clamp(g.FontSize, 0, 72);
            _loading = false;

            SetDetailEnabled(true);
        }

        /// <summary>
        /// Resets every control in the detail panel to its blank/default state and
        /// disables all inputs. Called when no group is selected (e.g. the list is empty).
        /// </summary>
        private void ClearDetail()
        {
            _txtName.Text = "";
            // Color.Empty tells SetSwatchColor to show the "(inherit)" placeholder text.
            SetSwatchColor(_pnlKeyColor, Color.Empty);
            SetSwatchColor(_pnlFontColor, Color.Empty);
            SetSwatchColor(_pnlBorderColor, Color.Empty);
            _nudBorderThickness.Value = -1; // -1 = inherit global
            _cmbFont.SelectedIndex = 0;     // 0 = "(inherit global)"
            _nudFontSize.Value = 0;         // 0 = auto / inherit
            SetDetailEnabled(false);
        }

        /// <summary>
        /// Writes the current values of every detail-panel control back into the group
        /// at position <paramref name="idx"/> in <see cref="_groups"/>.
        ///
        /// This is the core "save" operation. It is called:
        /// <list type="bullet">
        ///   <item>with <see cref="_prevIdx"/> just before the user switches to a different group, and</item>
        ///   <item>with the current index just before the dialog closes with OK.</item>
        /// </list>
        /// </summary>
        /// <param name="idx">
        /// Index of the group to update. If out of range the method returns without
        /// doing anything, which is safe (e.g. when <c>_prevIdx</c> is <c>-1</c>).
        /// </param>
        private void CommitTo(int idx)
        {
            if (idx < 0 || idx >= _groups.Count) return;
            var g = _groups[idx];
            g.Name            = _txtName.Text.Trim();
            g.KeyColor        = GetSwatchColor(_pnlKeyColor);
            g.FontColor       = GetSwatchColor(_pnlFontColor);
            g.BorderColor     = GetSwatchColor(_pnlBorderColor);
            g.BorderThickness = (int)_nudBorderThickness.Value;
            // Index 0 is the special "(inherit global)" placeholder — map it to an empty string.
            string fname      = _cmbFont.SelectedIndex > 0 ? _cmbFont.SelectedItem?.ToString() ?? "" : "";
            g.FontName        = fname;
            g.FontSize        = (int)_nudFontSize.Value;
            // Keep the list box display in sync with any name edits the user typed.
            _loading = true;
            if (idx < _lstGroups.Items.Count) _lstGroups.Items[idx] = g.Name;
            _loading = false;
        }

        /// <summary>
        /// Convenience wrapper around <see cref="CommitTo"/> that always targets the
        /// group currently displayed in the detail panel (<see cref="_prevIdx"/>).
        /// </summary>
        private void CommitCurrent() => CommitTo(_prevIdx);

        /// <summary>
        /// Called every time the user changes the text in the Name field.
        /// Immediately mirrors the new name into both the <see cref="_groups"/> list and
        /// the list box so the display stays in sync without needing a full rebuild.
        /// </summary>
        private void SaveCurrentName()
        {
            // Don't run while LoadDetail() is filling in controls programmatically.
            if (_loading) return;
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return;
            string newName = _txtName.Text.Trim();
            _groups[idx].Name = newName;
            // Temporarily set _loading so the SelectedIndexChanged handler ignores
            // this programmatic update to the list box item text.
            _loading = true;
            _lstGroups.Items[idx] = newName;
            _loading = false;
        }

        /// <summary>
        /// Handles the "Add group" button. Prompts for a name, creates a new
        /// <see cref="KeyGroup"/> with default settings, appends it, and selects it.
        /// </summary>
        private void OnAdd(object sender, EventArgs e)
        {
            // Save any unsaved edits on the currently displayed group first.
            CommitCurrent();
            string name = GetNewName();
            if (name == null) return; // user cancelled the name prompt
            // BorderThickness = -1 means "inherit global border setting" by default.
            _groups.Add(new KeyGroup { Name = name, BorderThickness = -1 });
            // Select the newly added item (last index).
            RebuildList(_groups.Count - 1);
        }

        /// <summary>
        /// Handles the "Delete group" button. Asks for confirmation and, if granted,
        /// removes the selected group and refreshes the list.
        /// </summary>
        private void OnDelete(object sender, EventArgs e)
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return;
            string name = _groups[idx].Name;
            // Show a localised "are you sure?" prompt before destroying data.
            if (MessageBox.Show(string.Format(Lang.T("Delete group msg"), name),
                    Lang.T("Delete Group"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _groups.RemoveAt(idx);
            // Reset _prevIdx so CommitTo won't try to write into the now-deleted slot.
            _prevIdx = -1;
            // After deletion, select the item just before the deleted one (or item 0).
            RebuildList(Math.Max(0, idx - 1));
        }

        /// <summary>
        /// Shows a small inline dialog that asks the user to type a name for a new group.
        /// </summary>
        /// <returns>
        /// The trimmed name string the user entered, or <c>null</c> if the user cancelled
        /// or left the field blank.
        /// </returns>
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
            // Only allow OK when there is some text — prevents empty group names.
            ok.Click += (s, e2) => { if (!string.IsNullOrWhiteSpace(txt.Text)) { dlg.DialogResult = DialogResult.OK; dlg.Close(); } };
            cn.Click += (s, e2) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };
            dlg.AcceptButton = ok;  // Enter triggers OK
            dlg.CancelButton = cn;  // Escape closes the dialog
            dlg.Controls.AddRange(new Control[] { txt, ok, cn });
            return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text.Trim() : null;
        }

        /// <summary>
        /// Enables or disables the Delete button and all detail-panel controls based on
        /// whether any groups currently exist. Called after every list change.
        /// </summary>
        private void UpdateEnabled()
        {
            bool any = _groups.Count > 0;
            _btnDelete.Enabled = any;
            SetDetailEnabled(any);
        }

        /// <summary>
        /// Enables or disables all of the editable controls in the right-hand detail
        /// panel as a group. Used to prevent the user from typing into an empty panel
        /// when there are no groups.
        /// </summary>
        /// <param name="en"><c>true</c> to enable all detail controls; <c>false</c> to disable them.</param>
        private void SetDetailEnabled(bool en)
        {
            _txtName.Enabled =
            _txtFontColorHex.Enabled = _pnlFontColor.Enabled =
            _txtKeyColorHex.Enabled  = _pnlKeyColor.Enabled =
            _txtBorderColorHex.Enabled = _pnlBorderColor.Enabled =
            _nudBorderThickness.Enabled = _cmbFont.Enabled = _nudFontSize.Enabled = en;
        }

        // ── Color row helpers ─────────────────────────────────────────

        /// <summary>
        /// Creates a colour picker row consisting of a hex <see cref="TextBox"/> and a
        /// clickable swatch <see cref="Panel"/>, matching the layout of
        /// <c>KeyEditorForm.AddColorRow</c>.
        /// <list type="bullet">
        ///   <item>Left-clicking the swatch opens the system colour picker.</item>
        ///   <item>Right-clicking the swatch shows "Clear (inherit global)".</item>
        ///   <item>Typing a hex value in the text box updates the swatch live.</item>
        ///   <item>Empty text box = no colour set = "inherit from global settings".</item>
        /// </list>
        /// The swatch's <see cref="Control.Tag"/> stores a reference to the text box so
        /// <see cref="GetSwatchColor"/> and <see cref="SetSwatchColor"/> can reach it.
        /// </summary>
        private (Panel swatch, TextBox hexBox) AddColorRow(Panel parent, int x, int y, int totalW)
        {
            int sw = 32;  // swatch button width — matches KeyEditorForm
            var txt = new TextBox
            {
                Left = x, Top = y, Width = totalW - sw - 5,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle, Font = Fluent.FontCourier,
            };
            var swatch = new Panel
            {
                Left = x + totalW - sw, Top = y, Width = sw, Height = 26,
                BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand,
                BackColor = Fluent.Neutral,
            };
            swatch.Tag = txt;  // link used by GetSwatchColor / SetSwatchColor

            txt.TextChanged += (s, e) =>
            {
                if (_loading) return;
                Color c = TryParseHex(txt.Text);
                swatch.BackColor = c.IsEmpty ? Fluent.Neutral : c;
            };
            swatch.Click += (s, e) => PickColor(swatch);

            var ctxMenu = new ContextMenuStrip();
            ctxMenu.Items.Add(Lang.T("Clear (inherit global)")).Click +=
                (s, e2) => SetSwatchColor(swatch, Color.Empty);
            swatch.ContextMenuStrip = ctxMenu;

            parent.Controls.Add(txt);
            parent.Controls.Add(swatch);
            return (swatch, txt);
        }

        /// <summary>
        /// Attempts to parse a hex colour string (<c>"#RRGGBB"</c> or <c>"RRGGBB"</c>).
        /// Returns <see cref="Color.Empty"/> for empty or invalid input.
        /// </summary>
        private static Color TryParseHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.Empty;
            try
            {
                string h = hex.Trim();
                if (!h.StartsWith("#")) h = "#" + h;
                return ColorTranslator.FromHtml(h);
            }
            catch { return Color.Empty; }
        }

        /// <summary>
        /// Opens the system colour-picker dialog pre-filled with <paramref name="pnl"/>'s
        /// current colour. If the user confirms a selection, the swatch is updated.
        /// </summary>
        /// <param name="pnl">The swatch panel whose colour should be changed.</param>
        private void PickColor(Panel pnl)
        {
            using var cd = new ColorDialog { FullOpen = true }; // FullOpen shows the full custom-colour grid
            Color current = GetSwatchColor(pnl);
            if (!current.IsEmpty) cd.Color = current; // pre-select the current colour in the picker
            if (cd.ShowDialog(this) == DialogResult.OK)
                SetSwatchColor(pnl, cd.Color);
        }

        /// <summary>
        /// Updates a swatch panel to display <paramref name="c"/>. If the colour is
        /// <see cref="Color.Empty"/> the panel reverts to the neutral "(inherit)" state.
        /// The colour is also stored in <see cref="Control.Tag"/> so it can be read back
        /// with <see cref="GetSwatchColor"/>.
        /// </summary>
        /// <param name="pnl">The swatch panel to update.</param>
        /// <param name="c">
        /// The colour to display, or <see cref="Color.Empty"/> to revert to "inherit".
        /// </param>
        /// <summary>
        /// Sets a swatch panel and its paired hex text box to display <paramref name="c"/>.
        /// <see cref="Color.Empty"/> reverts both to the "inherit" (no-colour) state.
        /// Uses <see cref="_loading"/> to suppress the TextBox's <c>TextChanged</c> handler
        /// while writing programmatically.
        /// </summary>
        private void SetSwatchColor(Panel pnl, Color c)
        {
            if (pnl.Tag is TextBox txt)
            {
                _loading = true;
                txt.Text = c.IsEmpty ? "" : SettingsManager.Hex(c);
                _loading = false;
            }
            pnl.BackColor = c.IsEmpty ? Fluent.Neutral : c;
        }

        /// <summary>
        /// Retrieves the colour from the hex text box paired with <paramref name="pnl"/>,
        /// or <see cref="Color.Empty"/> if the text box is empty or contains invalid hex.
        /// </summary>
        private static Color GetSwatchColor(Panel pnl) =>
            pnl.Tag is TextBox txt ? TryParseHex(txt.Text) : Color.Empty;

        // ── UI helpers ────────────────────────────────────────────────

        /// <summary>
        /// Creates a styled card panel with a painted title header and adds it to the form.
        /// The actual drawing is delegated to <see cref="FluentPainter.PaintCard"/> in the
        /// <c>Paint</c> event so the header redraws correctly on every resize or theme change.
        /// </summary>
        /// <param name="x">Left position on the form.</param>
        /// <param name="y">Top position on the form.</param>
        /// <param name="w">Panel width in pixels.</param>
        /// <param name="h">Panel height in pixels.</param>
        /// <param name="title">Text displayed in the card's header bar.</param>
        /// <param name="accentColor">Colour used for the header bar background.</param>
        /// <returns>The newly created panel (not yet containing any child controls).</returns>
        private Panel AddPanel(int x, int y, int w, int h, string title, Color accentColor)
        {
            Color bg = _dark ? Color.FromArgb(48, 48, 48) : Fluent.BgCard;
            var pnl = new Panel
            {
                Left = x, Top = y, Width = w, Height = h,
                BackColor = bg,
                BorderStyle = BorderStyle.None,
            };
            // 36 is the height of the painted header strip at the top of the card.
            bool dark = _dark;
            pnl.Paint += (s, e) =>
                FluentPainter.PaintCard(e.Graphics, pnl.Width, pnl.Height, title, accentColor, 36, dark);
            Controls.Add(pnl);
            return pnl;
        }

        /// <summary>
        /// Adds a right-aligned descriptive label to <paramref name="parent"/> at the
        /// given position. The label uses the standard label font and primary text colour.
        /// </summary>
        /// <param name="parent">The panel that will own the label.</param>
        /// <param name="text">The text to display.</param>
        /// <param name="x">Left position inside <paramref name="parent"/>.</param>
        /// <param name="y">Top position inside <paramref name="parent"/> (shifted down 6 px to align with input controls).</param>
        private void AddLabel(Panel parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text, Left = x, Top = y + 6, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            });
        }

        /// <summary>
        /// Adds a small greyed-out hint label next to an input control — used to
        /// explain sentinel values such as "-1 = inherit global" or "0 = auto".
        /// </summary>
        /// <param name="parent">The panel that will own the hint.</param>
        /// <param name="text">The hint text to display.</param>
        /// <param name="x">Left position inside <paramref name="parent"/>.</param>
        /// <param name="y">Top position inside <paramref name="parent"/> (shifted down 14 px to vertically centre within a spinner).</param>
        private void AddSmallHint(Panel parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text, Left = x, Top = y + 14, AutoSize = true,
                ForeColor = Fluent.TextHint, BackColor = Color.Transparent,
                Font = Fluent.FontHint,
            });
        }

        /// <summary>
        /// Creates a compact <see cref="FluentButton"/> with the Neutral style, suitable
        /// for use inside a panel (e.g. "Add group" or "Delete group"). The button is
        /// <em>not</em> added to the form's control collection here — the caller is
        /// responsible for adding it to the appropriate parent.
        /// </summary>
        /// <param name="text">Button label.</param>
        /// <param name="x">Left position.</param>
        /// <param name="y">Top position.</param>
        /// <param name="w">Button width.</param>
        /// <param name="h">Button height.</param>
        /// <returns>The new button, ready to be added to a parent control.</returns>
        private Button MakeSmallBtn(string text, int x, int y, int w, int h)
        {
            var b = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                Style = FluentButton.Variant.Neutral,
            };
            return b;
        }

        /// <summary>
        /// Creates a full-width <see cref="FluentButton"/> with the Neutral style, used
        /// for the main dialog actions (OK / Cancel). Unlike <see cref="MakeSmallBtn"/>,
        /// this method also adds the button directly to the <em>form</em> (not a panel).
        /// </summary>
        /// <param name="text">Button label.</param>
        /// <param name="x">Left position on the form.</param>
        /// <param name="y">Top position on the form.</param>
        /// <param name="w">Button width.</param>
        /// <param name="h">Button height.</param>
        /// <returns>The new button (already added to <c>Controls</c>).</returns>
        private Button MakeBigBtn(string text, int x, int y, int w, int h)
        {
            var b = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                Style = FluentButton.Variant.Neutral,
                TabStop = true,   // action buttons must be reachable by keyboard
            };
            Controls.Add(b);
            return b;
        }

        /// <summary>
        /// Returns the names of all font families installed on the current system,
        /// sorted alphabetically by the OS. Used to populate the font drop-down.
        /// </summary>
        /// <returns>A list of font family name strings.</returns>
        private static List<string> GetInstalledFonts()
        {
            var result = new List<string>();
            using var ifc = new System.Drawing.Text.InstalledFontCollection();
            foreach (var ff in ifc.Families) result.Add(ff.Name);
            return result;
        }

        // ── Import groups ─────────────────────────────────────────────

        /// <summary>
        /// Describes how a single imported group should be handled when its name
        /// conflicts with — or is absent from — the existing group list.
        /// </summary>
        private enum ImportAction
        {
            /// <summary>The group is new; add it as-is.</summary>
            Add,
            /// <summary>A group with this name already exists; replace it.</summary>
            Overwrite,
            /// <summary>A group with this name already exists; add the import under a new unique name.</summary>
            AddNew,
            /// <summary>Skip this group entirely — do not import it.</summary>
            Skip
        }

        /// <summary>
        /// Handles the "Import..." button. Lets the user pick an XML layout file, reads
        /// its group definitions, resolves any name conflicts via an interactive dialog,
        /// and merges the chosen groups into <see cref="_groups"/>.
        /// </summary>
        private void OnImport(object sender, EventArgs e)
        {
            // Persist any in-progress edits before changing the list.
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

            // Build a case-insensitive set of existing names for quick conflict detection.
            var existing = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            var decisions = ShowImportResolutionDialog(imported, existing);
            if (decisions == null) return; // user cancelled the resolution dialog

            // usedNames starts as a copy of existing names and grows as we add imports,
            // so that GetUniqueName can avoid duplicate names even within the same import batch.
            var usedNames = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var (group, action) in decisions)
            {
                switch (action)
                {
                    case ImportAction.Overwrite:
                    {
                        // Find the existing group with the same name (case-insensitive) and replace it.
                        int idx = _groups.FindIndex(g =>
                            string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0) _groups[idx] = group.Clone();
                        break;
                    }
                    case ImportAction.AddNew:
                    {
                        // Add the group under a unique name (e.g. "MyGroup 2") to avoid collision.
                        string newName = GetUniqueName(group.Name, usedNames);
                        var clone = group.Clone();
                        clone.Name = newName;
                        _groups.Add(clone);
                        usedNames.Add(newName); // register so subsequent groups don't get the same number
                        break;
                    }
                    case ImportAction.Add:
                    {
                        // No conflict — add a clone to avoid sharing references with the imported data.
                        _groups.Add(group.Clone());
                        usedNames.Add(group.Name);
                        break;
                    }
                    // ImportAction.Skip: do nothing — the group is intentionally omitted.
                }
            }

            // Refresh the list, keeping the selection as close as possible to where it was.
            RebuildList(Math.Max(0, _lstGroups.SelectedIndex));
        }

        /// <summary>
        /// Shows a modal dialog containing a data-grid where every imported group is
        /// listed together with its conflict status and a per-row action combo box.
        /// The user reviews the list and either confirms or cancels the import.
        /// </summary>
        /// <param name="imported">Groups read from the selected file.</param>
        /// <param name="existing">
        /// Case-insensitive set of names already in <see cref="_groups"/>, used to
        /// detect conflicts and colour-code rows.
        /// </param>
        /// <returns>
        /// A list of (group, action) pairs reflecting the user's per-row decisions, or
        /// <c>null</c> if the user cancelled the dialog.
        /// </returns>
        private List<(KeyGroup group, ImportAction action)> ShowImportResolutionDialog(
            List<KeyGroup> imported, HashSet<string> existing)
        {
            // Localised strings for the action combo box items.
            string actAdd       = Lang.T("Add");
            string actOverwrite = Lang.T("Overwrite");
            string actAddNew    = Lang.T("Add as new");
            string actSkip      = Lang.T("Skip");

            bool anyConflict = imported.Any(g => existing.Contains(g.Name));

            const int DLG_W = 580;
            // Row height is 34 px; clamp total grid height so it doesn't overflow the screen.
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

            // Info line at the top adapts its message depending on whether conflicts exist.
            string info = anyConflict
                ? string.Format(Lang.T("{0} groups found — choose action for each conflict:"), imported.Count)
                : string.Format(Lang.T("{0} groups found — all new, no conflicts."), imported.Count);
            dlg.Controls.Add(new Label
            {
                Text = info, Left = PAD, Top = PAD,
                Width = DLG_W - PAD * 2, Height = 26,
                Font = F_LABEL, ForeColor = C_LBL, BackColor = Color.Transparent,
            });

            // DataGridView with three columns: group name, conflict status, and action choice.
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
            dgv.EnableHeadersVisualStyles = false; // allow custom header colours
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Fluent.Accent;
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font      = F_HEADER;
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dgv.RowTemplate.Height = 38;
            // Suppress the built-in error dialog for invalid combo box values.
            dgv.DataError += (s, ev) => ev.Cancel = true;

            // FillWeight controls relative column widths when AutoSizeColumnsMode = Fill.
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
                // Store the KeyGroup object on the row so we can recover it when reading results.
                row.Tag = g;

                var cb = (DataGridViewComboBoxCell)row.Cells[2];
                if (conflict)
                {
                    // Conflicting groups default to Skip so the user must explicitly choose
                    // Overwrite or Add as new — avoiding accidental data loss.
                    cb.Items.AddRange(new[] { actOverwrite, actAddNew, actSkip });
                    cb.Value = actSkip;
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 243, 228); // warm amber = warning
                }
                else
                {
                    // New groups default to Add — they have no risk of overwriting anything.
                    cb.Items.Add(actAdd);
                    cb.Value = actAdd;
                    row.DefaultCellStyle.BackColor = Color.FromArgb(235, 252, 240); // light green = safe
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
            dlg.CancelButton = btnCancel3;
            dlg.Controls.Add(btnCancel3);
            dlg.Controls.Add(btnOK3);

            if (dlg.ShowDialog(this) != DialogResult.OK) return null;

            // Walk every data row and convert the user's combo-box selection into an ImportAction enum value.
            var result = new List<(KeyGroup, ImportAction)>();
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue; // the DataGridView may append an empty placeholder row — skip it
                var g       = (KeyGroup)row.Tag;
                bool conflict = existing.Contains(g.Name);
                string val  = row.Cells[2].Value?.ToString() ?? "";

                ImportAction action;
                if (!conflict)                action = ImportAction.Add;
                else if (val == actOverwrite) action = ImportAction.Overwrite;
                else if (val == actAddNew)    action = ImportAction.AddNew;
                else                          action = ImportAction.Skip;

                result.Add((g, action));
            }
            return result;
        }

        /// <summary>
        /// Returns a name that is not already in <paramref name="usedNames"/>. If
        /// <paramref name="baseName"/> is free it is returned as-is; otherwise a
        /// numeric suffix is appended and incremented until a free slot is found
        /// (e.g. "MyGroup", "MyGroup 2", "MyGroup 3", …).
        /// </summary>
        /// <param name="baseName">The preferred name.</param>
        /// <param name="usedNames">The set of names that are already taken (case-insensitive).</param>
        /// <returns>A unique name derived from <paramref name="baseName"/>.</returns>
        private static string GetUniqueName(string baseName, HashSet<string> usedNames)
        {
            if (!usedNames.Contains(baseName)) return baseName;
            int n = 2; // start at 2 so the first alternative reads "MyGroup 2", not "MyGroup 1"
            while (usedNames.Contains($"{baseName} {n}")) n++;
            return $"{baseName} {n}";
        }
    }
}

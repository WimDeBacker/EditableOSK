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

        /// <summary>
        /// The name of the group that is currently selected in the list box.
        /// Exposed so callers and tests can verify which group is initially shown.
        /// Returns <c>null</c> when the list is empty.
        /// </summary>
        public string SelectedGroupName =>
            _lstGroups.SelectedIndex >= 0 && _lstGroups.SelectedIndex < _groups.Count
                ? _groups[_lstGroups.SelectedIndex].Name
                : null;

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
        private Button        _pnlKeyColor, _pnlFontColor, _pnlBorderColor;
        /// <summary>Hex text boxes paired with each colour swatch; kept as fields so
        /// <see cref="SetDetailEnabled"/> can enable/disable them alongside the swatches.</summary>
        private TextBox       _txtKeyColorHex, _txtFontColorHex, _txtBorderColorHex;

        /// <summary>
        /// Spinner for the border thickness in pixels.
        /// The special value <c>-1</c> means "inherit the global setting".
        /// </summary>
        private NumericUpDown _nudBorderThickness;

        /// <summary>Drop-down listing every font installed on the system, plus an "(inherit standard)" option at index 0.</summary>
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

        /// <summary>
        /// <c>true</c> when the group currently shown in the detail panel is the
        /// protected standard group. Used by protection helpers to lock name / delete.
        /// </summary>
        private bool _isStandard;

        /// <summary>
        /// Reference to the border-thickness hint label so its text can be toggled
        /// between "-1 = inherit standard" (non-standard groups) and hidden
        /// (standard group, where -1 is not a valid value).
        /// </summary>
        private Label _lblBorderHint;

        /// <summary>
        /// "Clear" context-menu items for each colour swatch. Their Text is updated
        /// dynamically by <see cref="ApplyStandardProtections"/> to reflect whether the
        /// current group inherits from the standard group or has no parent.
        /// </summary>
        private ToolStripMenuItem _ctxClearFontColor, _ctxClearKeyColor, _ctxClearBorderColor;

        /// <summary>
        /// Small error label shown beneath the Name field when the user types a reserved
        /// name such as "standard". Hidden at all other times.
        /// </summary>
        private Label _lblNameError;

        // ── Tooltip / accessibility helpers ───────────────────────────
        /// <summary>Shared ToolTip component — one instance per form (WinForms best practice).</summary>
        private ToolTip _tip;

        /// <summary>
        /// Set by <see cref="AddLabel"/> and consumed by the next <see cref="AddColorRow"/> call
        /// so the colour row controls automatically receive the correct <see cref="Control.AccessibleName"/>.
        /// </summary>
        private string _pendingAccessibleName;

        // ── Reserved-name guard ───────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> when <paramref name="name"/> is a name that cannot be
        /// assigned to a user-created group.  Currently "standard" is the only reserved
        /// name because it is the root of the style-resolution chain and must always be
        /// uniquely identifiable.
        /// </summary>
        private static bool IsReservedGroupName(string name) =>
            string.Equals(name?.Trim(), SettingsManager.StandardGroupName,
                          StringComparison.OrdinalIgnoreCase);

        // ── Constructor ───────────────────────────────────────────────

        /// <summary>
        /// Initialises the dialog and immediately shows the first group (if any).
        /// </summary>
        /// <param name="groups">
        /// The caller's current list of groups. The dialog clones every entry so the
        /// original objects are never modified until the user confirms with "Apply".
        /// </param>
        /// <param name="initialGroupName">
        /// When non-null the dialog pre-selects the group with this name instead of
        /// the first group.  Used by "Edit standard group style…" to jump directly to
        /// the protected standard group.
        /// </param>
        public GroupEditorForm(List<KeyGroup> groups, string initialGroupName = null)
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

            _tip = new ToolTip { InitialDelay = 400, AutoPopDelay = 10000, ShowAlways = true };

            BuildUI();
            FluentPainter.ApplyDialogTheme(this, _dark);
            // The three ContextMenuStrip instances created in AddColorRow are not owned by
            // any component container, so they must be explicitly disposed on close.
            // We reach them through the ToolStripMenuItem.Owner back-reference, which is
            // always valid because the field references keep the items (and therefore their
            // parent menus) alive for the lifetime of this form.
            FormClosed += (s, e) =>
            {
                (_ctxClearFontColor?.Owner   as ContextMenuStrip)?.Dispose();
                (_ctxClearKeyColor?.Owner    as ContextMenuStrip)?.Dispose();
                (_ctxClearBorderColor?.Owner as ContextMenuStrip)?.Dispose();
            };
            // Select the first group so the detail panel is populated from the start.
            // If a specific group was requested, jump to it instead.
            int initialIdx = 0;
            if (initialGroupName != null)
            {
                int found = _groups.FindIndex(g => g.Name == initialGroupName);
                if (found >= 0) initialIdx = found;
            }
            RebuildList(initialIdx);
            ActiveControl = _lstGroups;  // list is the natural entry point — pick a group first, then edit it
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
            pnlList.TabIndex = 0;  // list panel first in form-level tab order

            // The list box sits inside a thin wrapper Panel that acts as a 2 px coloured focus ring.
            // The ListBox itself carries no border (BorderStyle.None) so the wrapper is the only
            // visible border. On GotFocus the wrapper turns accent-blue; on LostFocus it reverts
            // to the standard input-border grey — giving the same clear focus cue as FluentButton.
            int lstW = listW - PAD * 2;
            int lstH = innerH - 36 - PAD * 2 - 34 - 4 - 34 - PAD;
            var focusWrap = new Panel
            {
                Left = PAD, Top = 36 + PAD,
                Width = lstW, Height = lstH,
                BackColor = Fluent.BorderInput,   // default: subtle grey border
            };
            _lstGroups = new ListBox
            {
                Left = 2, Top = 2,
                Width = lstW - 4, Height = lstH - 4,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, BorderStyle = BorderStyle.None,
                TabIndex = 0,
                AccessibleName = Lang.StripMnemonic(Lang.T("Groups")),
            };
            _lstGroups.GotFocus  += (s, e) => focusWrap.BackColor = Fluent.Accent;
            _lstGroups.LostFocus += (s, e) => focusWrap.BackColor = Fluent.BorderInput;
            _lstGroups.SelectedIndexChanged += (s, e) =>
            {
                // Do nothing while the code itself is programmatically selecting items.
                if (_loading) return;
                // Save any pending edits for the group that was just visible…
                CommitTo(_prevIdx);
                // …then load the newly selected group into the detail panel.
                LoadDetail();
            };
            // Keyboard shortcuts on the list box (WCAG 2.1 A §2.1.1):
            //   Delete — remove the selected group (same as the Delete button, blocked for standard group)
            //   F2     — move focus to the Name field to begin renaming (same as clicking into _txtName)
            _lstGroups.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !_isStandard && _lstGroups.SelectedIndex >= 0)
                {
                    OnDelete(s, e);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.F2 && !_isStandard && _lstGroups.SelectedIndex >= 0)
                {
                    _txtName.Focus();
                    _txtName.SelectAll();
                    e.Handled = true;
                }
            };
            focusWrap.Controls.Add(_lstGroups);
            pnlList.Controls.Add(focusWrap);

            // Add / Delete buttons sit directly below the focus wrapper.
            int btnY = focusWrap.Bottom + PAD;
            int halfW = (listW - PAD * 2 - 4) / 2; // split width with a 4 px gap between the two buttons
            _btnAdd    = MakeSmallBtn(Lang.T("+ Add group"),    PAD,             btnY, halfW, 34); _btnAdd.TabIndex    = 1;
            _btnDelete = MakeSmallBtn(Lang.T("− Delete group"), PAD + halfW + 4, btnY, halfW, 34); _btnDelete.TabIndex = 2;
            _btnAdd.Click    += OnAdd;
            _btnDelete.Click += OnDelete;
            _tip.SetToolTip(_btnAdd,    Lang.T("tip: Add group"));
            _tip.SetToolTip(_btnDelete, Lang.T("tip: Delete group"));
            pnlList.Controls.Add(_btnAdd);
            pnlList.Controls.Add(_btnDelete);

            // Import button spans the full list width, one row below Add/Delete.
            int btnImportY = btnY + 34 + 4;
            _btnImport = MakeSmallBtn("&" + Lang.T("Import..."), PAD, btnImportY, listW - PAD * 2, 34); _btnImport.TabIndex = 3;
            _btnImport.Click += OnImport;
            _tip.SetToolTip(_btnImport, Lang.T("tip: Import groups"));
            pnlList.Controls.Add(_btnImport);

            // ── Detail panel (right) ──────────────────────────────────
            var pnlDetail = AddPanel(detailX, PAD, detailW, innerH, Lang.T("Style"), Color.FromArgb(39, 174, 96));
            pnlDetail.TabIndex = 1;  // detail panel second in form-level tab order

            // Layout constants for the detail rows:
            //   lx = left margin for labels
            //   vx = x-position where input controls start (after the label column)
            //   vw = width of input controls
            int lx = PAD, vx = 180, vw = detailW - lx - vx - PAD;
            int gy = 36 + PAD; // current vertical position, incremented by ROW after each field

            // ti = TabIndex counter within pnlDetail; label.TabIndex = buddy.TabIndex − 1.
            int ti = 0;

            // ── Field order matches the Appearance column in KeyEditorForm ──────────────
            // Name → Font → Font size → Font color → Key color → Border color → Border thickness

            // Name field
            AddLabel(pnlDetail, "&" + Lang.T("Name"), lx, gy).TabIndex = ti++;
            _txtName = new TextBox
            {
                Left = vx, Top = gy, Width = vw,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, BorderStyle = BorderStyle.FixedSingle,
                TabIndex = ti++,
                AccessibleName = Lang.StripMnemonic(Lang.T("Name")),
            };
            _txtName.TextChanged += (s, e) => SaveCurrentName();
            pnlDetail.Controls.Add(_txtName);
            // Error label shown when the user types a reserved name such as "standard".
            _lblNameError = new Label
            {
                Left = vx, Top = gy + 28, AutoSize = true,
                ForeColor = Color.FromArgb(200, 30, 30),
                BackColor = Color.Transparent, Font = Fluent.FontHint,
                Visible = false,
            };
            pnlDetail.Controls.Add(_lblNameError);
            gy += ROW;

            // Font family: index 0 is the special "(inherit standard)" entry for non-standard groups,
            // swapped to "(none / auto)" for the standard group by ApplyStandardProtections.
            AddLabel(pnlDetail, "&" + Lang.T("Font"), lx, gy).TabIndex = ti++;
            _cmbFont = new ComboBox
            {
                Left = vx, Top = gy, Width = vw,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                Font = F_INPUT, FlatStyle = FlatStyle.Flat,
                TabIndex = ti++,
                AccessibleName = Lang.StripMnemonic(Lang.T("Font")),
            };
            _cmbFont.Items.Add(Lang.T("(inherit standard)"));
            foreach (var fn in GetInstalledFonts()) _cmbFont.Items.Add(fn);
            _cmbFont.SelectedIndex = 0;
            pnlDetail.Controls.Add(_cmbFont); gy += ROW;

            // Font size: 0 means "auto / inherit".
            AddLabel(pnlDetail, Lang.T("Font size"), lx, gy).TabIndex = ti++;
            _nudFontSize = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = 0, Maximum = 72,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
                TabIndex = ti++,
                AccessibleName        = Lang.StripMnemonic(Lang.T("Font size")),
                AccessibleDescription = Lang.T("0 = auto / inherit"),
            };
            _tip.SetToolTip(_nudFontSize, Lang.T("tip: Font size"));
            AddSmallHint(pnlDetail, Lang.T("0 = auto / inherit"), vx + 71, gy);
            pnlDetail.Controls.Add(_nudFontSize); gy += ROW;

            // Colour rows: hex text box + swatch button, matching KeyEditorForm's AddColorRow layout.
            // Right-click the swatch to show the "Clear" context menu item.
            AddLabel(pnlDetail, Lang.T("Font color"), lx, gy).TabIndex = ti++;
            (_pnlFontColor, _txtFontColorHex, _ctxClearFontColor) = AddColorRow(pnlDetail, vx, gy, vw, ref ti); gy += ROW;

            AddLabel(pnlDetail, "&" + Lang.T("Key color"), lx, gy).TabIndex = ti++;
            (_pnlKeyColor, _txtKeyColorHex, _ctxClearKeyColor) = AddColorRow(pnlDetail, vx, gy, vw, ref ti); gy += ROW;

            AddLabel(pnlDetail, "&" + Lang.T("Border color"), lx, gy).TabIndex = ti++;
            (_pnlBorderColor, _txtBorderColorHex, _ctxClearBorderColor) = AddColorRow(pnlDetail, vx, gy, vw, ref ti); gy += ROW;

            // Border thickness: -1 means "inherit from standard group" for non-standard groups.
            // The standard group itself uses Minimum=0 (no inheritance) and the hint is hidden.
            AddLabel(pnlDetail, "&" + Lang.T("Border thickness"), lx, gy).TabIndex = ti++;
            _nudBorderThickness = new NumericUpDown
            {
                Left = vx, Top = gy, Width = 65, Minimum = -1, Maximum = 10,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary, Font = F_INPUT,
                TabIndex = ti++,
                AccessibleName        = Lang.StripMnemonic(Lang.T("Border thickness")),
                AccessibleDescription = Lang.T("-1 = inherit standard"),
            };
            _tip.SetToolTip(_nudBorderThickness, Lang.T("tip: Border thickness"));
            _lblBorderHint = AddSmallHint(pnlDetail, Lang.T("-1 = inherit standard"), vx + 71, gy);
            pnlDetail.Controls.Add(_nudBorderThickness);

            // ── OK / Cancel ───────────────────────────────────────────
            int btnY2 = ClientSize.Height - PAD - 40;
            int bw    = (formW - PAD) / 2; // each button takes roughly half the form width
            _btnCancel = MakeBigBtn(Lang.T("Cancel"), PAD,            btnY2, bw, 40); _btnCancel.TabIndex = 2;
            _btnOK     = MakeBigBtn(Lang.T("Apply"),  PAD + bw + PAD, btnY2, bw, 40); _btnOK.TabIndex     = 3;
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
        /// Returns the display name for a group in the list box.
        /// The standard group gets a lock prefix to signal it is protected.
        /// </summary>
        private static string GroupDisplayName(KeyGroup g) =>
            g.Name == SettingsManager.StandardGroupName ? "🔒 " + g.Name : g.Name;

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
            foreach (var g in _groups) _lstGroups.Items.Add(GroupDisplayName(g));
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
            _isStandard = g.Name == SettingsManager.StandardGroupName;
            _lblNameError.Visible = false;  // clear any prior validation error

            // Use _loading to stop SaveCurrentName() and other change handlers from
            // writing half-loaded values back into the group while we are filling in controls.
            _loading = true;
            _txtName.Text = g.Name;
            // Color.Empty means "no colour set for this group — inherit from standard group".
            SetSwatchColor(_pnlKeyColor,    g.KeyColor.IsEmpty   ? Color.Empty : g.KeyColor);
            SetSwatchColor(_pnlFontColor,   g.FontColor.IsEmpty  ? Color.Empty : g.FontColor);
            SetSwatchColor(_pnlBorderColor, g.BorderColor.IsEmpty? Color.Empty : g.BorderColor);
            // Minimum must be set before Value to avoid clamping to the wrong range.
            _nudBorderThickness.Minimum = _isStandard ? 0 : -1;
            _nudBorderThickness.Value   = Math.Clamp(g.BorderThickness, _isStandard ? 0 : -1, 10);

            // IndexOf returns -1 if the font name isn't in the list (e.g. font was uninstalled).
            // In that case we fall back to index 0 ("inherit standard" or "none / auto").
            int fi = _cmbFont.Items.IndexOf(g.FontName ?? "");
            _cmbFont.SelectedIndex = fi > 0 ? fi : 0;

            _nudFontSize.Value = Math.Clamp(g.FontSize, 0, 72);
            _loading = false;

            SetDetailEnabled(true);
            ApplyStandardProtections();
        }

        /// <summary>
        /// Resets every control in the detail panel to its blank/default state and
        /// disables all inputs. Called when no group is selected (e.g. the list is empty).
        /// </summary>
        private void ClearDetail()
        {
            _isStandard = false;
            _txtName.Text = "";
            // Color.Empty tells SetSwatchColor to show the "(inherit)" placeholder text.
            SetSwatchColor(_pnlKeyColor, Color.Empty);
            SetSwatchColor(_pnlFontColor, Color.Empty);
            SetSwatchColor(_pnlBorderColor, Color.Empty);
            _nudBorderThickness.Minimum = -1;
            _nudBorderThickness.Value = -1; // -1 = inherit standard
            _cmbFont.SelectedIndex = 0;     // 0 = "(inherit standard)"
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
            // Standard group name is protected — never overwrite it from the (disabled) text box.
            // Also reject any attempted rename to a reserved name (safety net behind SaveCurrentName).
            if (g.Name != SettingsManager.StandardGroupName)
            {
                string proposed = _txtName.Text.Trim();
                if (!IsReservedGroupName(proposed))
                    g.Name = proposed;
            }
            g.KeyColor        = GetSwatchColor(_pnlKeyColor);
            g.FontColor       = GetSwatchColor(_pnlFontColor);
            g.BorderColor     = GetSwatchColor(_pnlBorderColor);
            g.BorderThickness = (int)_nudBorderThickness.Value;
            // Index 0 is the special "(inherit standard)" / "(none / auto)" placeholder — map to "".
            string fname      = _cmbFont.SelectedIndex > 0 ? _cmbFont.SelectedItem?.ToString() ?? "" : "";
            g.FontName        = fname;
            g.FontSize        = (int)_nudFontSize.Value;
            // Keep the list box display in sync (standard group keeps its lock prefix).
            _loading = true;
            if (idx < _lstGroups.Items.Count) _lstGroups.Items[idx] = GroupDisplayName(g);
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
            // Standard group name is protected — the text box is disabled, but guard anyway.
            if (_groups[idx].Name == SettingsManager.StandardGroupName) return;
            string newName = _txtName.Text.Trim();
            // Block reserved names — show an inline error and refuse to update.
            if (IsReservedGroupName(newName))
            {
                _lblNameError.Text    = Lang.T("Name 'standard' is reserved.");
                _lblNameError.Visible = true;
                return;
            }
            _lblNameError.Visible = false;
            // Only update the list-box for live display feedback.
            // The actual write to _groups[idx].Name is deferred to CommitTo() so that
            // switching away with a partially-typed or reserved name never corrupts
            // _groups — the original committed name is always restored on switch-back.
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
            // Start the new group as a copy of the standard group so the user sees
            // concrete colour values immediately rather than empty/white "inherit" swatches,
            // which are confusing for users unfamiliar with the inheritance model.
            var std      = _groups.Find(g => g.Name == SettingsManager.StandardGroupName);
            var newGroup = std?.Clone() ?? new KeyGroup { BorderThickness = -1 };
            newGroup.Name = name;
            _groups.Add(newGroup);
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
                Text            = Lang.T("New Group"),
                Size            = new Size(340, 155),
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox     = false, MinimizeBox = false, ShowIcon = false,
                StartPosition   = FormStartPosition.CenterParent,
                TopMost         = true,
                BackColor       = C_BG,
                Font            = F_LABEL,
                AutoScaleMode       = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96f, 96f),
            };

            var lbl = new Label
            {
                Text = Lang.T("Name"), Left = 12, Top = 16, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
                TabIndex = 0,
            };
            var txt = new TextBox
            {
                Left = 12, Top = 36, Width = 300, Font = F_INPUT,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor   = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                TabIndex    = 1,
                AccessibleName = Lang.StripMnemonic(Lang.T("Name")),
            };
            var errLbl = new Label
            {
                Left = 12, Top = 62, Width = 300, Height = 18,
                ForeColor   = Color.FromArgb(200, 30, 30),
                BackColor   = Color.Transparent, Font = Fluent.FontHint,
                Visible     = false,
            };
            var ok = new FluentButton
            {
                Text = Lang.T("Apply"),  Left = 12,  Top = 84, Width = 140, Height = 32,
                Style = FluentButton.Variant.Neutral, TabStop = true, TabIndex = 2,
            };
            var cn = new FluentButton
            {
                Text = Lang.T("Cancel"), Left = 172, Top = 84, Width = 140, Height = 32,
                Style = FluentButton.Variant.Neutral, TabStop = true, TabIndex = 3,
            };

            ok.Click += (s, e2) =>
            {
                string name = txt.Text.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;
                if (IsReservedGroupName(name))
                {
                    errLbl.Text    = Lang.T("Name 'standard' is reserved.");
                    errLbl.Visible = true;
                    return;
                }
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };
            cn.Click += (s, e2) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

            dlg.AcceptButton = ok;   // Enter triggers OK
            dlg.CancelButton = cn;   // Escape closes the dialog
            dlg.Controls.AddRange(new Control[] { lbl, txt, errLbl, ok, cn });
            dlg.ActiveControl = txt; // keyboard focus lands on the name field immediately
            FluentPainter.ApplyDialogTheme(dlg, _dark);

            // Shrink form height to fit the extra label row
            dlg.ClientSize = new Size(dlg.ClientSize.Width, ok.Bottom + 12);

            return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text.Trim() : null;
        }

        /// <summary>
        /// Enables or disables the Delete button and all detail-panel controls based on
        /// whether any groups currently exist. Called after every list change.
        /// </summary>
        private void UpdateEnabled()
        {
            bool any = _groups.Count > 0;
            // Standard group cannot be deleted even when groups exist.
            _btnDelete.Enabled = any && !_isStandard;
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
            // Standard group name is always read-only regardless of the enable state.
            if (en && _isStandard) _txtName.Enabled = false;
        }

        /// <summary>
        /// Adjusts the detail panel to reflect the protection rules for the standard group:
        /// <list type="bullet">
        ///   <item>Standard group — name and delete locked; border Minimum=0; hint hidden;
        ///         font combo shows "(none / auto)"; colour swatch right-click menus removed
        ///         (the root group must always carry concrete colour values — "inherit" / "clear"
        ///         is meaningless when there is no parent to fall back to).</item>
        ///   <item>Non-standard group — border Minimum=-1; hint visible with
        ///         "-1 = inherit standard"; font combo shows "(inherit standard)";
        ///         colour clear items show "Clear (inherit standard)".</item>
        /// </list>
        /// Must be called after <see cref="SetDetailEnabled"/> so its targeted lock-downs
        /// are not overwritten by the blanket enable/disable pass.
        /// </summary>
        private void ApplyStandardProtections()
        {
            if (_isStandard)
            {
                // Root of the chain — nothing to inherit, so hide all "inherit" affordances.
                _txtName.Enabled   = false;
                _btnDelete.Enabled = false;
                _nudBorderThickness.Minimum = 0;
                if (_nudBorderThickness.Value < 0) _nudBorderThickness.Value = 0;
                _lblBorderHint.Visible = false;
                _cmbFont.Items[0] = Lang.T("(none / auto)");
                // Remove the right-click "Clear" menus from the colour swatches.
                // The standard group must always have concrete colour values; Color.Empty
                // would silently fall through to _theme fallback values, creating a
                // confusing disconnect between what the editor shows and what renders.
                _pnlFontColor.ContextMenuStrip   = null;
                _pnlKeyColor.ContextMenuStrip    = null;
                _pnlBorderColor.ContextMenuStrip = null;
            }
            else
            {
                // Non-standard group inherits from the standard group.
                _btnDelete.Enabled = true;
                _nudBorderThickness.Minimum = -1;
                _lblBorderHint.Text    = Lang.T("-1 = inherit standard");
                _lblBorderHint.Visible = true;
                _cmbFont.Items[0] = Lang.T("(inherit standard)");
                string cl = Lang.T("Clear (inherit standard)");
                _ctxClearFontColor.Text   = cl;
                _ctxClearKeyColor.Text    = cl;
                _ctxClearBorderColor.Text = cl;
                // Restore the right-click "Clear" menus that were removed for the standard group.
                _pnlFontColor.ContextMenuStrip   = _ctxClearFontColor.Owner   as ContextMenuStrip;
                _pnlKeyColor.ContextMenuStrip    = _ctxClearKeyColor.Owner    as ContextMenuStrip;
                _pnlBorderColor.ContextMenuStrip = _ctxClearBorderColor.Owner as ContextMenuStrip;
            }
        }

        // ── Color row helpers ─────────────────────────────────────────

        /// <summary>
        /// Creates a colour picker row consisting of a hex <see cref="TextBox"/> and a
        /// clickable swatch <see cref="Panel"/>, matching the layout of
        /// <c>KeyEditorForm.AddColorRow</c>.
        /// <list type="bullet">
        ///   <item>Left-clicking the swatch opens the system colour picker.</item>
        ///   <item>Right-clicking the swatch shows a "Clear" context menu item whose text
        ///         is updated dynamically by <see cref="ApplyStandardProtections"/>.</item>
        ///   <item>Typing a hex value in the text box updates the swatch live.</item>
        ///   <item>Empty text box = no colour set = "inherit from standard group".</item>
        /// </list>
        /// The swatch's <see cref="Control.Tag"/> stores a reference to the text box so
        /// <see cref="GetSwatchColor"/> and <see cref="SetSwatchColor"/> can reach it.
        /// The returned <see cref="ToolStripMenuItem"/> lets callers update the menu text.
        /// </summary>
        private (Button swatch, TextBox hexBox, ToolStripMenuItem clearItem) AddColorRow(Panel parent, int x, int y, int totalW, ref int ti)
        {
            int sw = 32;  // swatch width — matches KeyEditorForm
            var txt = new TextBox
            {
                Left = x, Top = y, Width = totalW - sw - 5,
                BackColor = C_INPUT_BG, ForeColor = Fluent.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle, Font = Fluent.FontCourier,
                TabIndex = ti++,
            };
            // ColorSwatchButton participates in Tab order and responds to Space/Enter natively
            // (WCAG 2.1 A §2.1.1). It also draws a two-tone focus ring visible on any colour
            // (WCAG 2.1 AA §2.4.7).
            var swatch = new ColorSwatchButton
            {
                Left = x + totalW - sw, Top = y, Width = sw, Height = 26,
                BackColor = Fluent.Neutral,
                TabIndex = ti++,
            };
            swatch.Tag = txt;  // link used by GetSwatchColor / SetSwatchColor
            // Consume the name queued by the preceding AddLabel call.
            string colorName = _pendingAccessibleName;
            _pendingAccessibleName = null;
            if (colorName != null)
            {
                txt.AccessibleName    = colorName + " hex";
                swatch.AccessibleName = colorName + " swatch";
            }
            _tip.SetToolTip(txt,    Lang.T("tip: Hex color"));
            _tip.SetToolTip(swatch, Lang.T("tip: Color swatch"));

            txt.TextChanged += (s, e) =>
            {
                if (_loading) return;
                Color c = TryParseHex(txt.Text);
                swatch.BackColor = c.IsEmpty ? Fluent.Neutral : c;
            };
            swatch.Click += (s, e) => PickColor(swatch);

            var clearItem = new ToolStripMenuItem(Lang.T("Clear (inherit standard)"));
            clearItem.Click += (s, e2) => SetSwatchColor(swatch, Color.Empty);
            var ctxMenu = new ContextMenuStrip();
            ctxMenu.Items.Add(clearItem);
            swatch.ContextMenuStrip = ctxMenu;

            parent.Controls.Add(txt);
            parent.Controls.Add(swatch);
            return (swatch, txt, clearItem);
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
        private void PickColor(Control pnl)
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
        private void SetSwatchColor(Control pnl, Color c)
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
        private static Color GetSwatchColor(Control pnl) =>
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
        private Label AddLabel(Panel parent, string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text, Left = x, Top = y + 6, AutoSize = true,
                ForeColor = C_LBL, BackColor = Color.Transparent, Font = F_LABEL,
            };
            parent.Controls.Add(lbl);
            // Store the stripped label text so the next AddColorRow call can assign
            // AccessibleName to both the hex text box and the colour swatch.
            _pendingAccessibleName = Lang.StripMnemonic(text);
            return lbl;
        }

        /// <summary>
        /// Adds a small greyed-out hint label next to an input control — used to
        /// explain sentinel values such as "-1 = inherit standard" or "0 = auto".
        /// </summary>
        /// <param name="parent">The panel that will own the hint.</param>
        /// <param name="text">The hint text to display.</param>
        /// <param name="x">Left position inside <paramref name="parent"/>.</param>
        /// <param name="y">Top position inside <paramref name="parent"/> (shifted down 14 px to vertically centre within a spinner).</param>
        private Label AddSmallHint(Panel parent, string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text, Left = x, Top = y + 14, AutoSize = true,
                ForeColor = Fluent.TextHint, BackColor = Color.Transparent,
                Font = Fluent.FontHint,
            };
            parent.Controls.Add(lbl);
            return lbl;
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
                TabStop = true,   // list-action buttons must be keyboard-reachable (WCAG 2.1 A §2.1.1)
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
        internal enum ImportAction
        {
            /// <summary>The group is new; add it as-is.</summary>
            Add,
            /// <summary>A group with this name already exists; replace it.</summary>
            Overwrite,
            /// <summary>
            /// The imported group is named "standard" and the user chose to apply it,
            /// replacing the local standard group's style values.
            /// </summary>
            UpdateStandard,
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

            ApplyImportDecisions(decisions);
        }

        /// <summary>
        /// Applies a pre-built list of import decisions to <see cref="_groups"/> and
        /// refreshes the list box.  Separated from <see cref="OnImport"/> so that tests
        /// can exercise the merge logic without showing any file or resolution dialogs.
        /// </summary>
        internal void ApplyImportDecisions(IEnumerable<(KeyGroup group, ImportAction action)> decisions)
        {
            // usedNames grows as we add imports so GetUniqueName avoids duplicates within the batch.
            var usedNames = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var (group, action) in decisions)
            {
                switch (action)
                {
                    case ImportAction.UpdateStandard:
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

            // Reset _prevIdx so that the CommitTo(_prevIdx) call fired by SelectedIndexChanged
            // inside RebuildList does nothing — otherwise it would read stale UI control values
            // and overwrite the groups that were just updated by this import operation.
            _prevIdx = -1;
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
            string actUpdateStd = Lang.T("Update standard group style");

            // Determine whether any regular (non-standard) group conflicts with an existing name.
            bool anyConflict = imported.Any(g =>
                !string.Equals(g.Name, SettingsManager.StandardGroupName, StringComparison.OrdinalIgnoreCase) &&
                existing.Contains(g.Name));

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
                bool isStdGroup = string.Equals(g.Name, SettingsManager.StandardGroupName,
                                                StringComparison.OrdinalIgnoreCase);
                bool conflict = !isStdGroup && existing.Contains(g.Name);

                var row = new DataGridViewRow();
                row.CreateCells(dgv);
                row.Cells[0].Value = g.Name;
                // Store the KeyGroup object on the row so we can recover it when reading results.
                row.Tag = g;

                var cb = (DataGridViewComboBoxCell)row.Cells[2];
                if (isStdGroup)
                {
                    // The standard group is the style-resolution root and cannot be added as a
                    // regular new group.  Offer "Update standard group style" (replaces it in
                    // place) or "Skip" — defaulting to Skip to avoid accidental overwrite.
                    row.Cells[1].Value = Lang.T("Protected");
                    cb.Items.AddRange(new[] { actUpdateStd, actSkip });
                    cb.Value = actSkip;
                    row.DefaultCellStyle.BackColor = Color.FromArgb(220, 235, 255); // light blue = protected
                }
                else if (conflict)
                {
                    // Conflicting groups default to Skip so the user must explicitly choose
                    // Overwrite or Add as new — avoiding accidental data loss.
                    row.Cells[1].Value = Lang.T("Conflict");
                    cb.Items.AddRange(new[] { actOverwrite, actAddNew, actSkip });
                    cb.Value = actSkip;
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 243, 228); // warm amber = warning
                }
                else
                {
                    // New groups default to Add — they have no risk of overwriting anything.
                    row.Cells[1].Value = Lang.T("New");
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
                var g         = (KeyGroup)row.Tag;
                bool isStdRow = string.Equals(g.Name, SettingsManager.StandardGroupName,
                                              StringComparison.OrdinalIgnoreCase);
                bool conflict = !isStdRow && existing.Contains(g.Name);
                string val    = row.Cells[2].Value?.ToString() ?? "";

                ImportAction action;
                if (isStdRow)
                    action = val == actUpdateStd ? ImportAction.UpdateStandard : ImportAction.Skip;
                else if (!conflict)            action = ImportAction.Add;
                else if (val == actOverwrite)  action = ImportAction.Overwrite;
                else if (val == actAddNew)     action = ImportAction.AddNew;
                else                           action = ImportAction.Skip;

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

        // ── Test seams ────────────────────────────────────────────────
        // These internal methods expose just enough of the Add / Rename logic for
        // headless unit tests to verify reservation enforcement without showing dialogs.

        /// <summary>
        /// For testing: commits the current detail-panel edits and exposes the result
        /// list via <see cref="ResultGroups"/> without showing or closing the dialog.
        /// </summary>
        internal void CommitToResult()
        {
            CommitCurrent();
            ResultGroups = _groups;
        }

        /// <summary>
        /// For testing: attempts to programmatically add a new group with the given name.
        /// Returns <c>false</c> without modifying state if the name is reserved or blank.
        /// </summary>
        internal bool TryAddGroup(string name)
        {
            name = name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (IsReservedGroupName(name)) return false;
            CommitCurrent();
            var std      = _groups.Find(g => g.Name == SettingsManager.StandardGroupName);
            var newGroup = std?.Clone() ?? new KeyGroup { BorderThickness = -1 };
            newGroup.Name = name;
            _groups.Add(newGroup);
            RebuildList(_groups.Count - 1);
            return true;
        }

        /// <summary>
        /// For testing: attempts to rename the currently selected group to
        /// <paramref name="name"/>.  Returns <c>false</c> without modifying state when
        /// the rename is blocked — either because the selected group IS the standard group
        /// (cannot be renamed away) or because <paramref name="name"/> IS "standard"
        /// (cannot rename to a reserved name).
        /// </summary>
        internal bool TryRenameCurrentGroup(string name)
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return false;
            name = name?.Trim() ?? "";
            // Block renaming the standard group to anything else.
            if (_groups[idx].Name == SettingsManager.StandardGroupName) return false;
            // Block renaming any group to the reserved name "standard".
            if (IsReservedGroupName(name)) return false;
            if (string.IsNullOrWhiteSpace(name)) return false;
            _groups[idx].Name = name;
            _loading = true;
            _lstGroups.Items[idx] = name;
            _loading = false;
            return true;
        }
    }
}

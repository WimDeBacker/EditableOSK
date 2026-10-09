using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A modal dialog that lets the user create, rename, delete, import and style named key groups.
    ///
    /// A "key group" is a named category that can be assigned to one or more keys on the
    /// on-screen keyboard layout. Each group can carry its own colours, font, and border
    /// settings so that visually related keys share a consistent look without having to
    /// set those properties key by key. Every value a group does not set is inherited from the
    /// protected "standard" group (the root of the style chain).
    ///
    /// The dialog works on a private working copy of the group list so that cancelling
    /// leaves the original data untouched. When the user clicks "Apply", the modified
    /// list is exposed through <see cref="ResultGroups"/> and the dialog closes with
    /// <see cref="DialogResult.OK"/>.
    ///
    /// Layout (touch-friendly, content-sized, like the Key Editor): a master list of groups on the left with
    /// its buttons below, the style of the selected group on the right (name, font, font size, three colour
    /// chips, border thickness) and a labelled preview. "Inherit" is an explicit choice on each value (a check
    /// box, or a button in the colour flyout) instead of a hidden -1 / empty value or a right-click menu.
    /// </summary>
    public class GroupEditorForm : FluentDialogBase
    {
        /// <summary>
        /// The modified list of groups that should replace the caller's original list.
        /// This property is only set when the dialog closes with <see cref="DialogResult.OK"/>;
        /// if the user cancels it remains <c>null</c>.
        /// </summary>
        public List<KeyGroup> ResultGroups { get; private set; }

        /// <summary>
        /// The name of the group that is currently selected in the list.
        /// Exposed so callers and tests can verify which group is initially shown.
        /// Returns <c>null</c> when the list is empty.
        /// </summary>
        public string SelectedGroupName =>
            _lstGroups.SelectedIndex >= 0 && _lstGroups.SelectedIndex < _groups.Count
                ? _groups[_lstGroups.SelectedIndex].Name
                : null;

        // What a style value falls back to when even the standard group does not set it (matches the Key Editor).
        private static readonly Color DefaultFontColor   = ColorTranslator.FromHtml("#E0E0FF");
        private static readonly Color DefaultKeyColor    = ColorTranslator.FromHtml("#2D2D4A");
        private static readonly Color DefaultBorderColor = ColorTranslator.FromHtml("#3C3C5A");
        private const string DefaultFontName = "Arial";
        private const int    DefaultBorderThickness = 1;

        /// <summary>Width of the list column in design pixels (the right-hand column takes the rest).</summary>
        private const int ListColumnWidth = 250;

        /// <summary>Two columns need more room than a one-column dialog: wide enough for the three colour chips on one row.</summary>
        protected override int ContentMaxWidth => 980;

        // ── Working copy of groups ────────────────────────────────────
        /// <summary>
        /// Private working copy of the group list. Every entry is a deep clone of the
        /// caller's original, so cancelling the dialog leaves the source data unchanged.
        /// Edits are only written back to the caller via <see cref="ResultGroups"/> when
        /// the user clicks "Apply".
        /// </summary>
        private readonly List<KeyGroup> _groups;

        // The name every group of the working copy had when the dialog opened, by group object (not by name: a name can change).
        private readonly Dictionary<KeyGroup, string> _origName = new Dictionary<KeyGroup, string>(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// What the dialog renamed and deleted, for the keys that point at those groups by name (see <see cref="GroupRenames"/>).
        /// Set when the dialog closes with OK; null before that.
        /// </summary>
        internal GroupRenames Changes { get; private set; }

        /// <summary>Works out which groups were renamed and which were deleted since the dialog opened.</summary>
        private GroupRenames ComputeChanges()
        {
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var g in _groups)
                if (_origName.TryGetValue(g, out string orig) && orig != g.Name) renames[orig] = g.Name;
            var deleted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in _origName)
            {
                if (_groups.Any(g => ReferenceEquals(g, kv.Key))) continue;
                // A group deleted and added again under the same name in one session: the keys stay with the name. (A group that was
                // renamed to that name is not a new one: the keys of the deleted group do not follow it.)
                bool addedAgain = _groups.Any(g => !_origName.ContainsKey(g) && g.Name == kv.Value);
                if (!addedAgain) deleted.Add(kv.Value);
            }
            return new GroupRenames(renames, deleted);
        }

        // ── Controls ─────────────────────────────────────────────────
        private TouchList         _lstGroups;
        private Panel             _listFrame;                 // draws the list's border and its focus colour
        private FluentButton      _btnAdd, _btnDelete, _btnImport, _btnApply, _btnCancel;
        private TouchTextBox      _txtName;
        private Label             _lblNameError;              // reserved / duplicate name, shown under the Name field
        private TouchChoiceButton _cmbFont;                   // index 0 is the "(inherit standard)" placeholder
        private TouchStepper      _stpFontSize, _stpBorder;
        private TouchCheckBox     _chkAutoSize, _chkBorderInherit;
        private ColorChip         _chipFont, _chipKey, _chipBorder;
        private KeyPreviewCard    _preview;

        /// <summary>
        /// Set while the code itself fills the list or the detail controls. Change handlers that would
        /// normally save or commit UI changes return early so half-loaded values never overwrite data.
        /// </summary>
        private bool _loading;

        /// <summary>
        /// Index (in <see cref="_groups"/>) of the group currently shown in the detail panel. Before
        /// switching to another group <see cref="CommitTo"/> writes the controls back to that group.
        /// <c>-1</c> means nothing is displayed.
        /// </summary>
        private int _prevIdx = -1;

        /// <summary><c>true</c> while the group shown is the protected standard group.</summary>
        private bool _isStandard;

        // ── Reserved-name guard ───────────────────────────────────────

        /// <summary>
        /// <c>true</c> when <paramref name="name"/> cannot be given to a user-created group: "standard" is the root
        /// of the style-resolution chain and must always be uniquely identifiable.
        /// </summary>
        private static bool IsReservedGroupName(string name) =>
            string.Equals(name?.Trim(), SettingsManager.StandardGroupName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// <c>true</c> when <paramref name="name"/> case-insensitively matches another existing group (any group but
        /// the one at <paramref name="excludeIdx"/>). Without this, "klinkers" next to "Klinkers" would let two groups
        /// coexist that every case-sensitive lookup elsewhere in the app treats as unrelated.
        /// </summary>
        private bool NameCollides(string name, int excludeIdx = -1)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (i == excludeIdx) continue;
                if (string.Equals(_groups[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>The reason <paramref name="name"/> cannot be used for a group (excluding the one at <paramref name="excludeIdx"/>), or null.</summary>
        private string NameProblem(string name, int excludeIdx = -1)
        {
            if (IsReservedGroupName(name))        return Lang.T("Name 'standard' is reserved.");
            if (NameCollides(name, excludeIdx))   return Lang.T("A group with this name already exists.");
            return null;
        }

        // ── Constructor ───────────────────────────────────────────────

        /// <summary>Initialises the dialog and immediately shows the first group (if any).</summary>
        /// <param name="groups">
        /// The caller's current list of groups. The dialog clones every entry so the original objects are
        /// never modified until the user confirms with "Apply".
        /// </param>
        /// <param name="initialGroupName">
        /// When non-null the dialog pre-selects the group with this name instead of the first group
        /// (used by "Edit standard group style…" to jump to the protected standard group).
        /// </param>
        public GroupEditorForm(List<KeyGroup> groups, string initialGroupName = null)
        {
            // Deep-clone so cancelling truly discards all changes.
            _groups = groups.Select(g => g.Clone()).ToList();
            foreach (var g in _groups) _origName[g] = g.Name;          // to tell afterwards what was renamed and what was deleted

            Text = Lang.T("Manage Groups");
            BuildUI();

            int initialIdx = 0;
            if (initialGroupName != null)
            {
                int found = _groups.FindIndex(g => g.Name == initialGroupName);
                if (found >= 0) initialIdx = found;
            }
            RebuildList(initialIdx);
            ActiveControl = _lstGroups;
        }

        /// <summary>Re-applies the theme, then restores the controls that carry their own colours.</summary>
        protected override void ApplyTheme()
        {
            base.ApplyTheme();
            // The theme pass makes every Label the plain text colour; a validation message must stay red (>= 7 : 1 on either theme).
            if (_lblNameError != null) _lblNameError.ForeColor = _dark ? Fluent.DialogDarkDanger : Fluent.Danger;
        }

        /// <summary>Refreshes every string that is not registered with the base class when the language changes.</summary>
        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            Text = Lang.T("Manage Groups");
            _btnAdd.Text    = Lang.T("+ Add group");
            _btnDelete.Text = Lang.T("− Delete group");
            _btnImport.Text = "&" + Lang.T("Import...");
            _btnAdd.AccessibleName = Lang.StripMnemonic(_btnAdd.Text);
            _chkBorderInherit.AccessibleName = Lang.T("Inherit from standard");
            ApplyStandardProtections();                       // placeholder text of the font chooser and the inherit texts
        }

        // ── UI construction ───────────────────────────────────────────

        /// <summary>Creates every control: the list column, the style column, the preview and the footer. Layout only.</summary>
        private void BuildUI()
        {
            _btnCancel = MakeTouchButton(() => Lang.T("Cancel"));
            _btnApply  = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, _btnCancel, _btnApply), withSections: false);
            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;

            var body = AddSection(() => Lang.T("Manage Groups"));

            // Two columns: the list of groups (fixed width) and, on the right, the preview in the top right corner (where the
            // Key Editor has it) with the style of the selected group below it.
            // On a narrow window the two columns do not fit side by side: the style (with the preview) goes under the list.
            var md = new AdaptiveTable { ColumnCount = 2 };
            md.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ListColumnWidth));
            md.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var list = BuildListColumn();
            var right = BuildRightColumn();
            md.AddVariant(() => new[] { new AdaptiveTable.Cell(list, 0, 0), new AdaptiveTable.Cell(right, 1, 0) });
            md.AddVariant(() => new[] { new AdaptiveTable.Cell(list, 0, 0, 2), new AdaptiveTable.Cell(right, 0, 1, 2) });
            AddWideRow(body, md);
        }

        /// <summary>The preview card in the top right corner, the "Style" heading and the style rows below it.</summary>
        private Control BuildRightColumn()
        {
            var col = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            col.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // The labelled preview: same card, same place as in the Key Editor (top right of the dialog).
            _preview = new KeyPreviewCard { Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(Touch.Gap, 0, 0, Touch.Gap) };
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.Controls.Add(_preview, 0, 0);

            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.Controls.Add(Heading(() => Lang.T("Style")), 0, 1);

            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.Controls.Add(BuildStyleColumn(), 0, 2);
            return col;
        }

        /// <summary>The list of groups with the Add / Delete / Import buttons below it.</summary>
        private Control BuildListColumn()
        {
            var col = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 0, Fluent.Pad, 0) };
            col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // The list sits in a 1 px frame: its colour is the control boundary (3 : 1) and shows the focus.
            _lstGroups = new TouchList { Dock = DockStyle.Fill, AccessibleName = Lang.StripMnemonic(Lang.T("Groups")), TabIndex = 0 };
            _listFrame = new Panel
            {
                Padding = new Padding(1), Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Touch.Gap),
                MinimumSize = new Size(0, 6 * Touch.Target + 2), Height = 6 * Touch.Target + 2, Tag = "notheme",
                BackColor = _dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder,
            };
            _listFrame.Controls.Add(_lstGroups);
            // Focus: the accent on light, a light grey on dark (the dark AAA accent would not show on a dark card).
            _lstGroups.GotFocus  += (s, e) => _listFrame.BackColor = _dark ? Fluent.DialogDarkText : Fluent.Accent;
            _lstGroups.LostFocus += (s, e) => _listFrame.BackColor = _dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder;
            _lstGroups.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;            // the code itself is selecting items
                CommitTo(_prevIdx);              // save the edits of the group that was just visible…
                LoadDetail();                    // …then show the newly selected one
            };
            // Keyboard (WCAG 2.1.1): Delete removes the selected group, F2 starts renaming it.
            _lstGroups.KeyDown += (s, e) =>
            {
                if (_lstGroups.SelectedIndex < 0 || _isStandard) return;
                if (e.KeyCode == Keys.Delete)     { OnDelete(s, e); e.Handled = true; }
                else if (e.KeyCode == Keys.F2)    { _txtName.Focus(); _txtName.SelectAll(); e.Handled = true; }
            };
            col.RowCount = 6;
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.Controls.Add(Heading(() => Lang.T("Groups")), 0, 0);
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.Controls.Add(_listFrame, 0, 1);

            _btnAdd    = ListButton(() => Lang.T("+ Add group"), "tip: Add group", 1);
            _btnDelete = ListButton(() => Lang.T("− Delete group"), "tip: Delete group", 2);
            _btnImport = ListButton(() => "&" + Lang.T("Import..."), "tip: Import groups", 3);
            _btnAdd.Click    += OnAdd;
            _btnDelete.Click += OnDelete;
            _btnImport.Click += OnImport;
            int r = 2;
            foreach (var b in new[] { _btnAdd, _btnDelete, _btnImport })
            {
                col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                col.Controls.Add(b, 0, r++);
            }
            // A filler row takes whatever height the style column makes available, so the buttons stay together.
            col.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return col;
        }

        private FluentButton ListButton(Func<string> text, string tip, int tabIndex)
        {
            var b = MakeTouchButton(text);
            b.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            b.Margin = new Padding(0, 0, 0, Touch.Gap);
            b.TabIndex = tabIndex;
            b.AccessibleName = Lang.StripMnemonic(text());
            SetTip(b, () => Lang.T(tip));
            return b;
        }

        /// <summary>Name, font, font size, colours, border thickness and the preview of the selected group.</summary>
        private Control BuildStyleColumn()
        {
            var t = NewTable();
            t.Padding = Padding.Empty;
            // Anchored to the top, left and right, not docked: it keeps its own (content) height. A table docked in a cell that is
            // taller than it needs (the list column is) hands the extra height to its last row, which opened a big gap.
            t.Dock   = DockStyle.None;
            t.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // Name, with the reason it is refused (reserved / duplicate) under the field.
            _txtName = new TouchTextBox();
            _txtName.TextChanged += (s, e) => SaveCurrentName();
            AddRow(t, () => Lang.T("Name"), _txtName);
            // Always present (empty when there is nothing to say), so a message appearing later never moves the rows below it.
            _lblNameError = Wrap(new Label
            {
                AutoSize = true, UseMnemonic = false, MaximumSize = new Size(Touch.LabelMaxWidth * 2, 0),
                MinimumSize = new Size(0, Fluent.FontHint.Height + 4),
                Font = Fluent.FontHint, BackColor = Color.Transparent, ForeColor = _dark ? Fluent.DialogDarkDanger : Fluent.Danger,
                Margin = Padding.Empty,
            });
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(_lblNameError, 1, r);

            // Font: index 0 is the "(inherit standard)" placeholder ("(none / auto)" for the standard group).
            _cmbFont = new TouchChoiceButton { RowHeight = 44, Searchable = true };
            _cmbFont.SetItems(new[] { new TouchChoice { Text = Lang.T("(inherit standard)") } }
                .Concat(Fluent.InstalledFontNames().Select(n => new TouchChoice { Text = n })), 0);
            _cmbFont.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                UpdateFontAvailabilityWarning(_cmbFont, _cmbFont.SelectedIndex > 0 ? _cmbFont.SelectedItem?.Text ?? "" : "");
                RefreshPreview();
            };
            _err.SetIconPadding(_cmbFont, -54);
            _fontWarn.SetIconPadding(_cmbFont, -54);       // left of the button's arrow
            AddRow(t, () => "&" + Lang.T("Font"), _cmbFont);

            // Font size: a stepper plus "Auto" (the size is decided when a key is drawn; stored as 0).
            _stpFontSize = new TouchStepper { Minimum = 0, Maximum = 72, Margin = new Padding(0, 0, Touch.Gap, 0) };
            _stpFontSize.AccessibleDescription = Lang.T("0 = auto / inherit");
            _stpFontSize.ValueBox.AccessibleDescription = _stpFontSize.AccessibleDescription;
            SetTip(_stpFontSize.ValueBox, () => Lang.T("tip: Font size"));
            _stpFontSize.ValueChanged += (s, e) => { if (!_loading) RefreshPreview(); };
            _chkAutoSize = NewCheck(() => Lang.T("Auto"));
            _chkAutoSize.CheckedChanged += (s, e) => { _stpFontSize.Enabled = !_chkAutoSize.Checked; if (!_loading) RefreshPreview(); };
            var sizeRow = InlineRow(_stpFontSize, _chkAutoSize);
            AddRow(t, () => Lang.T("Font size"), sizeRow, fill: false);

            // Colours on one row: three labelled chips. A chip can inherit its colour (dashed outline, shows the standard group's colour).
            _chipFont   = new ColorChip(Lang.T("chip: Font"),   DefaultFontColor);
            _chipKey    = new ColorChip(Lang.T("chip: Key"),    DefaultKeyColor);
            _chipBorder = new ColorChip(Lang.T("chip: Border"), DefaultBorderColor);
            _transTexts.Add((_chipFont, () => Lang.T("chip: Font")));
            _transTexts.Add((_chipKey, () => Lang.T("chip: Key")));
            _transTexts.Add((_chipBorder, () => Lang.T("chip: Border")));
            var chips = InlineRow(_chipFont, _chipKey, _chipBorder);
            _chipFont.TabIndex = 0; _chipKey.TabIndex = 1; _chipBorder.TabIndex = 2;
            foreach (var chip in new[] { _chipFont, _chipKey, _chipBorder })
            {
                var c = chip;
                c.ValueChanged += (s, e) =>
                {
                    if (_loading) return;
                    if (c.Inherited) c.SetInherited(InheritedColorFor(c));      // show what the standard group provides
                    RefreshPreview();
                };
                SetTip(c, () => Lang.T("tip: Color swatch"));
            }
            AddRow(t, () => Lang.T("Colors"), chips, fill: false);

            // Border thickness: a stepper plus "Inherit" (stored as -1). The standard group has no parent, so no check box there.
            _stpBorder = new TouchStepper { Minimum = 0, Maximum = 10, Margin = new Padding(0, 0, Touch.Gap, 0) };
            _stpBorder.AccessibleDescription = Lang.T("-1 = inherit standard");
            _stpBorder.ValueBox.AccessibleDescription = _stpBorder.AccessibleDescription;
            SetTip(_stpBorder.ValueBox, () => Lang.T("tip: Border thickness"));
            _stpBorder.ValueChanged += (s, e) => { if (!_loading) RefreshPreview(); };
            // A short text (it must fit next to the stepper in every language); the accessible name and the tooltip say the whole thing.
            _chkBorderInherit = NewCheck(() => Lang.T("Inherit"));
            _chkBorderInherit.AccessibleName = Lang.T("Inherit from standard");
            SetTip(_chkBorderInherit, () => Lang.T("Inherit from standard"));
            _chkBorderInherit.CheckedChanged += (s, e) =>
            {
                _stpBorder.Enabled = !_chkBorderInherit.Checked;
                if (_loading) return;
                if (_chkBorderInherit.Checked) { _loading = true; _stpBorder.Value = StandardBorderThickness(); _loading = false; }
                RefreshPreview();
            };
            var borderRow = InlineRow(_stpBorder, _chkBorderInherit);
            AddRow(t, () => Lang.T("Border thickness"), borderRow, fill: false);
            return t;
        }

        // ── What a group inherits ─────────────────────────────────────

        private KeyGroup Standard => _groups.Find(g => g.Name == SettingsManager.StandardGroupName);

        private Color StandardColor(Func<KeyGroup, Color> pick, Color fallback)
        {
            var c = Standard != null ? pick(Standard) : Color.Empty;
            return c.IsEmpty ? fallback : c;
        }

        /// <summary>The colour an inheriting chip shows: what the standard group provides, else the built-in default.</summary>
        private Color InheritedColorFor(ColorChip chip) =>
            chip == _chipFont ? StandardColor(g => g.FontColor, DefaultFontColor)
          : chip == _chipKey  ? StandardColor(g => g.KeyColor, DefaultKeyColor)
          :                     StandardColor(g => g.BorderColor, DefaultBorderColor);

        private int StandardBorderThickness() =>
            Standard != null && Standard.BorderThickness >= 0 ? Standard.BorderThickness : DefaultBorderThickness;

        // ── Apply ─────────────────────────────────────────────────────

        private void Apply()
        {
            // Refuse to close while a field is flagged invalid; the icon on that field is the feedback.
            if (ShowFirstSectionWithError()) return;
            CommitCurrent();
            ResultGroups = _groups;
            Changes = ComputeChanges();
            DialogResult = DialogResult.OK;
            Close();
        }

        // ── List management ───────────────────────────────────────────

        /// <summary>The display name of a group in the list: the standard group gets a lock prefix to show it is protected.</summary>
        private static string GroupDisplayName(KeyGroup g) =>
            g.Name == SettingsManager.StandardGroupName ? "🔒 " + g.Name : g.Name;

        /// <summary>
        /// Repopulates the list from <see cref="_groups"/> and selects the entry at <paramref name="selectIndex"/>
        /// (clamped, so deleting the last item is safe). With no groups left the detail panel is cleared.
        /// </summary>
        private void RebuildList(int selectIndex)
        {
            _loading = true;                      // no spurious commits while the list is repopulated
            _lstGroups.Items.Clear();
            foreach (var g in _groups) _lstGroups.Items.Add(GroupDisplayName(g));
            _loading = false;

            if (_groups.Count > 0)
            {
                _lstGroups.SelectedIndex = Math.Clamp(selectIndex, 0, _groups.Count - 1);
                LoadDetail();
            }
            else ClearDetail();
            UpdateEnabled();
        }

        /// <summary>
        /// Shows the selected group in the detail panel and records its index in <see cref="_prevIdx"/>, so that
        /// <see cref="CommitTo"/> writes later edits back to the right group.
        /// </summary>
        private void LoadDetail()
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) { ClearDetail(); _prevIdx = -1; return; }
            _prevIdx = idx;
            var g = _groups[idx];
            _isStandard = g.Name == SettingsManager.StandardGroupName;
            _lblNameError.Text = "";

            _loading = true;                      // the change handlers must not write half-loaded values back
            try
            {
                _txtName.Text = g.Name;
                ApplyStandardProtections();

                ShowColor(_chipKey,    g.KeyColor);
                ShowColor(_chipFont,   g.FontColor);
                ShowColor(_chipBorder, g.BorderColor);

                // A font that isn't installed must not be silently replaced by the placeholder: that would clear the
                // group's real FontName the moment the dialog is applied. It is kept (see SelectOrInsertFont).
                if (string.IsNullOrEmpty(g.FontName)) _cmbFont.SelectSilently(0);
                else SelectOrInsertFont(_cmbFont, g.FontName);
                UpdateFontAvailabilityWarning(_cmbFont, g.FontName ?? "");

                int size = Math.Clamp(g.FontSize, 0, (int)_stpFontSize.Maximum);
                _stpFontSize.Value   = size;
                _chkAutoSize.Checked = size == 0;
                _stpFontSize.Enabled = size > 0;

                if (!_isStandard && g.BorderThickness < 0)
                {
                    _chkBorderInherit.Checked = true;
                    _stpBorder.Value   = StandardBorderThickness();
                    _stpBorder.Enabled = false;
                }
                else
                {
                    _chkBorderInherit.Checked = false;
                    _stpBorder.Value   = Math.Clamp(g.BorderThickness, 0, (int)_stpBorder.Maximum);
                    _stpBorder.Enabled = true;
                }
            }
            finally { _loading = false; }

            SetDetailEnabled(true);
            RefreshPreview();
        }

        /// <summary>Shows a stored colour on a chip: an own colour as it is, an empty one as inherited (the standard group's colour).</summary>
        private void ShowColor(ColorChip chip, Color stored)
        {
            if (stored.IsEmpty) chip.SetInherited(InheritedColorFor(chip));
            else chip.SetOwn(stored);
        }

        /// <summary>Blanks the detail panel and disables its inputs (no group selected, e.g. the list is empty).</summary>
        private void ClearDetail()
        {
            _isStandard = false;
            _loading = true;
            try
            {
                _txtName.Text = "";
                ApplyStandardProtections();
                _chipKey.SetInherited(DefaultKeyColor);
                _chipFont.SetInherited(DefaultFontColor);
                _chipBorder.SetInherited(DefaultBorderColor);
                _cmbFont.SelectSilently(0);
                _stpFontSize.Value = 0; _chkAutoSize.Checked = true;
                _stpBorder.Value = DefaultBorderThickness; _chkBorderInherit.Checked = true;
            }
            finally { _loading = false; }
            SetDetailEnabled(false);
            RefreshPreview();
        }

        /// <summary>
        /// Writes the detail controls back into the group at <paramref name="idx"/>. Called with <see cref="_prevIdx"/> just
        /// before the user switches to another group, and for the current group just before the dialog closes with OK.
        /// An index out of range does nothing (e.g. <c>_prevIdx</c> is -1).
        /// </summary>
        private void CommitTo(int idx)
        {
            if (idx < 0 || idx >= _groups.Count) return;
            var g = _groups[idx];
            // The standard group's name is protected. Any other rename to a reserved or already used name is
            // refused here too (safety net behind SaveCurrentName).
            if (g.Name != SettingsManager.StandardGroupName)
            {
                string proposed = _txtName.Text.Trim();
                if (!IsReservedGroupName(proposed) && !NameCollides(proposed, idx))
                    g.Name = proposed;
            }
            bool std = g.Name == SettingsManager.StandardGroupName;
            // An inherited chip stores "no colour" (empty), an own colour is stored as it is.
            g.KeyColor    = _chipKey.Inherited    ? Color.Empty : _chipKey.Value;
            g.FontColor   = _chipFont.Inherited   ? Color.Empty : _chipFont.Value;
            g.BorderColor = _chipBorder.Inherited ? Color.Empty : _chipBorder.Value;
            g.BorderThickness = !std && _chkBorderInherit.Checked ? -1 : (int)_stpBorder.Value;
            // Index 0 is the placeholder: stored as "" (inherit / auto).
            g.FontName = _cmbFont.SelectedIndex > 0 ? _cmbFont.SelectedItem?.Text ?? "" : "";
            g.FontSize = _chkAutoSize.Checked ? 0 : (int)_stpFontSize.Value;
            // Keep the list text in sync (the standard group keeps its lock prefix).
            _loading = true;
            if (idx < _lstGroups.Items.Count) _lstGroups.Items[idx] = GroupDisplayName(g);
            _loading = false;
        }

        private void CommitCurrent() => CommitTo(_prevIdx);

        /// <summary>
        /// Called each time the Name field changes: mirrors a valid new name into the list for live feedback. The actual
        /// write to the group is deferred to <see cref="CommitTo"/> so a half-typed or reserved name never corrupts it.
        /// </summary>
        private void SaveCurrentName()
        {
            if (_loading) return;
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return;
            if (_groups[idx].Name == SettingsManager.StandardGroupName) return;     // read-only, but guard anyway
            string newName = _txtName.Text.Trim();
            string problem = NameProblem(newName, idx);
            if (problem != null) { _lblNameError.Text = problem; return; }
            _lblNameError.Text = "";
            _loading = true;
            _lstGroups.Items[idx] = newName;
            _loading = false;
        }

        /// <summary>Handles the "Add group" button: asks for a name, adds a copy of the standard group under it and selects it.</summary>
        private void OnAdd(object sender, EventArgs e)
        {
            CommitCurrent();                      // save the edits of the group that is displayed first
            string name = GetNewName();
            if (name == null) return;             // cancelled
            AddGroup(name);
        }

        /// <summary>
        /// Adds a group named <paramref name="name"/>. It starts as a copy of the standard group so the user sees concrete
        /// values straight away rather than a row of "inherit" markers, which are confusing without the inheritance model.
        /// </summary>
        private void AddGroup(string name)
        {
            var std      = _groups.Find(g => g.Name == SettingsManager.StandardGroupName);
            var newGroup = std?.Clone() ?? new KeyGroup { BorderThickness = -1 };
            newGroup.Name = name;
            _groups.Add(newGroup);
            RebuildList(_groups.Count - 1);
        }

        /// <summary>Handles the "Delete group" button: asks for confirmation, then removes the selected group.</summary>
        private void OnDelete(object sender, EventArgs e)
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return;
            string name = _groups[idx].Name;
            if (!TouchMessage.Confirm(this, Lang.T("Delete Group"), string.Format(Lang.T("Delete group msg"), name))) return;
            RemoveSelectedGroup();
        }

        /// <summary>Removes the selected group (never the standard group) and selects the one before it. Separate from the confirmation so tests can run it.</summary>
        internal bool RemoveSelectedGroup()
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count || _groups[idx].Name == SettingsManager.StandardGroupName) return false;
            _groups.RemoveAt(idx);
            _prevIdx = -1;                        // CommitTo must not write into the now-deleted slot
            RebuildList(Math.Max(0, idx - 1));
            return true;
        }

        /// <summary>Asks for the name of a new group; null if cancelled.</summary>
        private string GetNewName()
        {
            using var dlg = new NameDialog(Lang.T("New Group"), name => NameProblem(name));
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.ResultName : null;
        }

        /// <summary>Enables or disables the Delete button and the detail controls depending on whether any group exists.</summary>
        private void UpdateEnabled()
        {
            bool any = _groups.Count > 0;
            _btnDelete.Enabled = any && !_isStandard;      // the standard group cannot be deleted
            SetDetailEnabled(any);
        }

        /// <summary>Enables or disables the editable controls of the detail panel as a group.</summary>
        private void SetDetailEnabled(bool en)
        {
            _txtName.Enabled = en && !_isStandard;          // the standard group's name is always read-only
            _cmbFont.Enabled = _chipFont.Enabled = _chipKey.Enabled = _chipBorder.Enabled = en;
            _chkAutoSize.Enabled = en;
            _stpFontSize.Enabled = en && !_chkAutoSize.Checked;
            _chkBorderInherit.Enabled = en && !_isStandard;
            _stpBorder.Enabled = en && !(!_isStandard && _chkBorderInherit.Checked);
        }

        /// <summary>
        /// Adjusts the detail panel to the protection rules of the standard group. It is the root of the chain: there is
        /// nothing to inherit, so the inherit check box and the "inherit" button of the colour flyout are not offered, the
        /// font placeholder reads "(none / auto)", and its name and Delete are locked.
        /// </summary>
        private void ApplyStandardProtections()
        {
            string inherit = Lang.T("Inherit from standard");
            _chkBorderInherit.Visible = !_isStandard;
            _chipFont.InheritText = _chipKey.InheritText = _chipBorder.InheritText = _isStandard ? null : inherit;
            _cmbFont.Items[0].Text = Lang.T(_isStandard ? "(none / auto)" : "(inherit standard)");
            _cmbFont.ShowSelection();
            _txtName.Enabled   = !_isStandard && _groups.Count > 0;
            _btnDelete.Enabled = !_isStandard && _groups.Count > 0;
        }

        /// <summary>Shows what the group looks like now, with every inherited value resolved through the standard group.</summary>
        private void RefreshPreview()
        {
            if (_preview == null || _chipKey == null || _stpBorder == null) return;
            string std = Standard != null && !string.IsNullOrEmpty(Standard.FontName) ? Standard.FontName : DefaultFontName;
            string fn  = _cmbFont.SelectedIndex > 0 ? _cmbFont.SelectedItem?.Text ?? std : std;
            int fs     = _chkAutoSize.Checked || _stpFontSize.Value == 0 ? 13 : (int)_stpFontSize.Value;   // 13 stands in for "auto"
            int bt     = !_isStandard && _chkBorderInherit.Checked ? StandardBorderThickness() : (int)_stpBorder.Value;
            _preview.Set("Aa", _chipKey.Value, _chipFont.Value, _chipBorder.Value, fn, fs, bt);
            _preview.AccessibleName = string.Format(
                Lang.T("preview: key '{0}', key colour {1}, font colour {2}, {3} {4} pt"),
                "Aa", SettingsManager.Hex(_chipKey.Value), SettingsManager.Hex(_chipFont.Value), fn, fs);
        }

        // ── Import groups ─────────────────────────────────────────────

        /// <summary>How a single imported group is handled when its name conflicts with — or is absent from — the existing list.</summary>
        internal enum ImportAction
        {
            /// <summary>The group is new; add it as-is.</summary>
            Add,
            /// <summary>A group with this name already exists; replace it.</summary>
            Overwrite,
            /// <summary>The imported group is named "standard" and the user chose to apply it, replacing the local standard group's style values.</summary>
            UpdateStandard,
            /// <summary>A group with this name already exists; add the import under a new unique name.</summary>
            AddNew,
            /// <summary>Skip this group entirely — do not import it.</summary>
            Skip
        }

        /// <summary>
        /// Handles the "Import..." button: lets the user pick a layout file, reads its groups, resolves name conflicts in a
        /// dialog and merges the chosen groups into <see cref="_groups"/>.
        /// </summary>
        private void OnImport(object sender, EventArgs e)
        {
            CommitCurrent();                      // persist in-progress edits before changing the list

            using var ofd = new OpenFileDialog
            {
                Title = Lang.T("Select a layout file to import groups from"),
                Filter = "Keyboard layout (*.kbl)|*.kbl|All files (*.*)|*.*",
                RestoreDirectory = true,
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            var imported = SettingsManager.LoadGroupsFromFile(ofd.FileName);
            if (imported.Count == 0)
            {
                TouchMessage.Info(this, Lang.T("Import Groups"), Lang.T("No groups found in the selected file."));
                return;
            }

            var existing = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            using var dlg = new ImportDialog(imported, existing);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;      // cancelled
            ApplyImportDecisions(dlg.Decisions);
        }

        /// <summary>
        /// Applies a list of import decisions to <see cref="_groups"/> and refreshes the list. Separate from
        /// <see cref="OnImport"/> so tests can exercise the merge logic without showing any dialog.
        /// </summary>
        internal void ApplyImportDecisions(IEnumerable<(KeyGroup group, ImportAction action)> decisions)
        {
            // usedNames grows as imports are added so GetUniqueName avoids duplicates within the batch.
            var usedNames = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var (group, action) in decisions)
            {
                switch (action)
                {
                    case ImportAction.UpdateStandard:
                    case ImportAction.Overwrite:
                    {
                        int idx = _groups.FindIndex(g => string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            var clone = group.Clone();
                            // Overwrite means "restyle this existing group", not "rename it" — keep the local entry's own
                            // casing. This matters most for the protected standard group: every other lookup in the app
                            // compares StandardGroupName case-sensitively, so adopting an imported file's casing
                            // (e.g. "Standard") would let this entry silently lose its protected status.
                            clone.Name = _groups[idx].Name;
                            if (_origName.TryGetValue(_groups[idx], out string orig)) { _origName.Remove(_groups[idx]); _origName[clone] = orig; }   // still the same group, restyled
                            _groups[idx] = clone;
                        }
                        break;
                    }
                    case ImportAction.AddNew:
                    {
                        string newName = GetUniqueName(group.Name, usedNames);
                        var clone = group.Clone();
                        clone.Name = newName;
                        _groups.Add(clone);
                        usedNames.Add(newName);        // so later groups in the batch don't get the same number
                        break;
                    }
                    case ImportAction.Add:
                    {
                        // A clone: no shared references with the imported data. The name is made unique here as well, so a group is never
                        // added under a name that is taken, even if the dialog did not flag it.
                        var clone = group.Clone();
                        clone.Name = GetUniqueName(group.Name, usedNames);
                        _groups.Add(clone);
                        usedNames.Add(clone.Name);
                        break;
                    }
                    // ImportAction.Skip: the group is intentionally omitted.
                }
            }

            // Reset _prevIdx so the CommitTo(_prevIdx) fired by SelectedIndexChanged inside RebuildList does nothing —
            // otherwise it would read stale control values and overwrite the groups the import just updated.
            _prevIdx = -1;
            RebuildList(Math.Max(0, _lstGroups.SelectedIndex));
        }

        /// <summary>A name not in <paramref name="usedNames"/>: <paramref name="baseName"/> if free, else "MyGroup 2", "MyGroup 3", …</summary>
        private static string GetUniqueName(string baseName, HashSet<string> usedNames)
        {
            if (!usedNames.Contains(baseName)) return baseName;
            int n = 2;                            // the first alternative reads "MyGroup 2", not "MyGroup 1"
            while (usedNames.Contains($"{baseName} {n}")) n++;
            return $"{baseName} {n}";
        }

        /// <summary>Layout numbers of every container, for diagnosing spacing problems (written by the gallery).</summary>
        internal string DiagnoseLayout()
        {
            var sb = new System.Text.StringBuilder();
            void Dump(Control c, int depth)
            {
                sb.AppendLine($"{new string(' ', depth * 2)}{c.GetType().Name} '{c.Text}' vis={c.Visible} bounds={c.Bounds}");
                if (c is TableLayoutPanel tl)
                    sb.AppendLine($"{new string(' ', depth * 2)}  rows: {string.Join(",", tl.GetRowHeights())} cols: {string.Join(",", tl.GetColumnWidths())}");
                if (depth < 6) foreach (Control ch in c.Controls) Dump(ch, depth + 1);
            }
            foreach (Control c in Controls) Dump(c, 0);
            return sb.ToString();
        }

        // ── Test seams ────────────────────────────────────────────────
        // Internal methods that expose just enough of the add / rename logic for headless tests.

        /// <summary>For testing: commits the current edits and exposes the result list via <see cref="ResultGroups"/> without closing the dialog.</summary>
        internal void CommitToResult()
        {
            CommitCurrent();
            ResultGroups = _groups;
            Changes = ComputeChanges();
        }

        /// <summary>For testing: adds a group with the given name; false (and nothing changes) when the name is blank, reserved or taken.</summary>
        internal bool TryAddGroup(string name)
        {
            name = name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (NameProblem(name) != null) return false;
            CommitCurrent();
            AddGroup(name);
            return true;
        }

        /// <summary>
        /// For testing: renames the selected group. False (and nothing changes) when the selected group is the standard group,
        /// the new name is blank, "standard", or collides case-insensitively with another group.
        /// </summary>
        internal bool TryRenameCurrentGroup(string name)
        {
            int idx = _lstGroups.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return false;
            name = name?.Trim() ?? "";
            if (_groups[idx].Name == SettingsManager.StandardGroupName) return false;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (NameProblem(name, idx) != null) return false;
            _groups[idx].Name = name;
            _loading = true;
            _lstGroups.Items[idx] = name;
            _loading = false;
            return true;
        }
    }
}

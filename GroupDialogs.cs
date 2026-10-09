// GroupDialogs.cs — the small dialogs of the Group Editor, on the same touch-friendly base as the editors:
//
//   • TouchMessage  a question or a notice with 44 px buttons (replaces MessageBox, whose buttons are small)
//   • NameDialog    asks for the name of a new group, with the reason a name is refused shown under the field
//   • ImportDialog  one row per group found in a file: its status and what to do with it (replaces a DataGridView)
//
// All three are content-sized: the window opens at the size its content needs, in every language.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>A message or a yes / no question in a touch-sized dialog.</summary>
    internal sealed class TouchMessage : FluentDialogBase
    {
        internal TouchMessage(string title, string text, bool question)      // internal: the gallery photographs it
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            var no  = question ? MakeTouchButton(() => Lang.T("No")) : null;
            var yes = MakeTouchButton(() => question ? Lang.T("Yes") : Lang.T("OK"), question ? FluentButton.Variant.Danger : FluentButton.Variant.Success);
            BuildFrame(question ? MakeFooter(null, no, yes) : MakeFooter(null, yes), withSections: false);

            var t = AddSection(() => title);
            var msg = new Label
            {
                Text = text, AutoSize = true, MaximumSize = new Size(Touch.LabelMaxWidth * 3, 0), UseMnemonic = false,
                Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
                AccessibleName = text,
            };
            AddWideRow(t, msg, fill: false);

            yes.Click += (s, e) => { DialogResult = DialogResult.Yes; Close(); };
            if (no != null) no.Click += (s, e) => { DialogResult = DialogResult.No; Close(); };
            AcceptButton = question ? no : yes;          // for a question the safe answer is the default
            CancelButton = question ? no : yes;
            ActiveControl = question ? no : yes;
        }

        /// <summary>Asks a yes / no question; <c>true</c> for Yes. "No" is the default, so a stray Enter does not delete anything.</summary>
        public static bool Confirm(IWin32Window owner, string title, string text)
        {
            using var d = new TouchMessage(title, text, question: true);
            return d.ShowDialog(owner) == DialogResult.Yes;
        }

        /// <summary>Shows a notice with an OK button.</summary>
        public static void Info(IWin32Window owner, string title, string text)
        {
            using var d = new TouchMessage(title, text, question: false);
            d.ShowDialog(owner);
        }
    }

    /// <summary>Asks for a name. <paramref name="validate"/> returns the reason a name is refused, or null when it is fine.</summary>
    internal sealed class NameDialog : FluentDialogBase
    {
        private readonly Func<string, string> _validate;
        private readonly TouchTextBox _txt = new TouchTextBox();
        private readonly Label _lblErr;

        /// <summary>The trimmed name the user entered; null unless the dialog closed with OK.</summary>
        public string ResultName { get; private set; }

        public NameDialog(string title, Func<string, string> validate)
        {
            _validate = validate;
            Text = title;
            var cancel = MakeTouchButton(() => Lang.T("Cancel"));
            var ok     = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, cancel, ok), withSections: false);

            var t = AddSection(() => title);
            AddRow(t, () => Lang.T("Name"), _txt);
            // Always present (empty when there is nothing to say): its room is part of the window's size from the start, so a
            // message appearing later is not cut off by a window that was sized without it.
            _lblErr = new Label
            {
                AutoSize = true, UseMnemonic = false, MaximumSize = new Size(Touch.LabelMaxWidth * 2, 0),
                MinimumSize = new Size(0, Fluent.FontHint.Height * 2 + 4),
                Font = Fluent.FontHint, BackColor = Color.Transparent,
                ForeColor = _dark ? Fluent.DialogDarkDanger : Fluent.Danger,
            };
            AddWideRow(t, _lblErr, fill: false);

            _txt.TextChanged += (s, e) => ShowReason(validateOnlyIfTyped: true);
            ok.Click     += (s, e) => Accept();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = ok;
            CancelButton = cancel;
            ActiveControl = _txt;
        }

        protected override void ApplyTheme()
        {
            base.ApplyTheme();
            if (_lblErr != null) _lblErr.ForeColor = _dark ? Fluent.DialogDarkDanger : Fluent.Danger;   // the theme pass makes every label plain text colour
        }

        private void ShowReason(bool validateOnlyIfTyped)
        {
            string name = _txt.Text.Trim();
            string reason = name.Length == 0 && validateOnlyIfTyped ? null : _validate(name);
            _lblErr.Text = reason ?? "";
        }

        private void Accept()
        {
            string name = _txt.Text.Trim();
            if (name.Length == 0) return;
            string reason = _validate(name);
            if (reason != null) { _lblErr.Text = reason; return; }
            ResultName   = name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// Lists the groups found in a layout file, each with its status (new, in conflict, protected) and a chooser for what to do
    /// with it. The decisions are exposed in <see cref="Decisions"/> when the dialog closes with OK.
    /// </summary>
    internal sealed class ImportDialog : FluentDialogBase
    {
        private readonly List<(KeyGroup Group, bool Standard, bool Conflict, TouchChoiceButton Chooser, TouchChoice[] Choices)> _rows
            = new List<(KeyGroup, bool, bool, TouchChoiceButton, TouchChoice[])>();

        /// <summary>One entry per imported group, with the chosen action; set when the dialog closes with OK.</summary>
        public List<(KeyGroup group, GroupEditorForm.ImportAction action)> Decisions { get; private set; }

        public ImportDialog(List<KeyGroup> imported, HashSet<string> existing)
        {
            Text = Lang.T("Import Groups");
            var cancel = MakeTouchButton(() => Lang.T("Cancel"));
            var import = MakeTouchButton(() => Lang.T("Import"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, cancel, import), withSections: false);

            // A group is in conflict when the local list has that name, or when an earlier group in this same file already has it (a file
            // written by hand or merged from two): both would be added under one name otherwise. Names compare without regard to case.
            var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var conflicts = imported.Select(g => !IsStandard(g) && (existing.Contains(g.Name) | !seenInFile.Add(g.Name))).ToList();
            bool anyConflict = conflicts.Any(c => c);
            string info = anyConflict
                ? string.Format(Lang.T("{0} groups found — choose action for each conflict:"), imported.Count)
                : string.Format(Lang.T("{0} groups found — all new, no conflicts."), imported.Count);

            var t = AddSection(() => Lang.T("Import Groups"));
            var infoLabel = new Label
            {
                Text = info, AutoSize = true, MaximumSize = new Size(Touch.LabelMaxWidth * 3, 0), UseMnemonic = false,
                Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
            };
            AddWideRow(t, infoLabel, fill: false);

            // Group | Status | Action: the labels size to their text, the chooser takes the rest.
            var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddHeader(grid, 0, () => Lang.T("Group"));
            AddHeader(grid, 1, () => Lang.T("Status"));
            AddHeader(grid, 2, () => Lang.T("Action"));

            string actAdd = Lang.T("Add"), actOverwrite = Lang.T("Overwrite"), actAddNew = Lang.T("Add as new"),
                   actSkip = Lang.T("Skip"), actUpdateStd = Lang.T("Update standard group style");

            int tab = 0;
            for (int gi = 0; gi < imported.Count; gi++)
            {
                var g = imported[gi];
                bool std = IsStandard(g);
                bool conflict = conflicts[gi];
                string status; string[] actions; int defaultIdx;
                if (std)           { status = Lang.T("Protected"); actions = new[] { actUpdateStd, actSkip };            defaultIdx = 1; }   // default Skip: no accidental overwrite
                else if (conflict) { status = Lang.T("Conflict");  actions = new[] { actOverwrite, actAddNew, actSkip }; defaultIdx = 2; }
                else               { status = Lang.T("New");       actions = new[] { actAdd };                            defaultIdx = 0; }

                int r = grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var name = new Label
                {
                    Text = g.Name, AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false, Font = Fluent.FontBtnLg,
                    ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Margin = new Padding(0, 4, Fluent.Pad, 4),
                    MaximumSize = new Size(Touch.LabelMaxWidth, 0),
                };
                var st = new Label
                {
                    Text = status, AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false, Font = Fluent.FontLabel,
                    ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Margin = new Padding(0, 4, Fluent.Pad, 4),
                };
                var choices = actions.Select(a => new TouchChoice { Text = a }).ToArray();
                var chooser = new TouchChoiceButton { RowHeight = 44, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4), TabIndex = tab++ };
                chooser.SetItems(choices, defaultIdx);
                chooser.AccessibleName = $"{Lang.T("Action")} {g.Name}";
                chooser.AccessibleDescription = string.Format(Lang.T(
                    std      ? "import row: {0}: Protected — choose Update or Skip" :
                    conflict ? "import row: {0}: Conflict — choose Overwrite, Add as new, or Skip" :
                               "import row: {0}: New — will be added"), g.Name);
                grid.Controls.Add(name, 0, r);
                grid.Controls.Add(st, 1, r);
                grid.Controls.Add(chooser, 2, r);
                _rows.Add((g, std, conflict, chooser, choices));
            }
            AddWideRow(t, grid);

            import.Click += (s, e) => Accept(actUpdateStd, actOverwrite, actAddNew);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = import;
            CancelButton = cancel;
        }

        private static bool IsStandard(KeyGroup g) =>
            string.Equals(g.Name, SettingsManager.StandardGroupName, StringComparison.OrdinalIgnoreCase);

        private void AddHeader(TableLayoutPanel grid, int col, Func<string> text)
        {
            var lbl = new Label
            {
                Text = text(), AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false, Font = Fluent.FontHint,
                ForeColor = Fluent.TextHint, BackColor = Color.Transparent,
            };
            _transLabels.Add((lbl, text));
            grid.Controls.Add(lbl, col, 0);
            if (col == 0) { grid.RowCount = 1; grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
        }

        /// <summary>Turns each row's choice into an <see cref="GroupEditorForm.ImportAction"/>, as the old grid did.</summary>
        private void Accept(string actUpdateStd, string actOverwrite, string actAddNew)
        {
            var result = new List<(KeyGroup, GroupEditorForm.ImportAction)>();
            foreach (var row in _rows)
            {
                string val = row.Chooser.SelectedItem?.Text ?? "";
                GroupEditorForm.ImportAction action;
                if (row.Standard)        action = val == actUpdateStd ? GroupEditorForm.ImportAction.UpdateStandard : GroupEditorForm.ImportAction.Skip;
                else if (!row.Conflict)  action = GroupEditorForm.ImportAction.Add;
                else if (val == actOverwrite) action = GroupEditorForm.ImportAction.Overwrite;
                else if (val == actAddNew)    action = GroupEditorForm.ImportAction.AddNew;
                else                          action = GroupEditorForm.ImportAction.Skip;
                result.Add((row.Group, action));
            }
            Decisions    = result;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

// GroupEditorTests.cs — the touch-friendly Group Editor and its small dialogs (NameDialog, ImportDialog, TouchMessage):
//
//   • strict UI guards (44 px targets, no clipped text, nothing sticking out, fits a 1366 x 768 screen) in English, Dutch and
//     two long-language stress cases, light and dark theme
//   • inheritance: "inherit" is an explicit state on each value and round-trips without a hidden -1 / empty value changing
//   • editing: what the controls write back, switching groups, delete, language change
//   • the sub-dialogs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static List<KeyGroup> SampleGroups() => new List<KeyGroup>
        {
            new KeyGroup { Name = SettingsManager.StandardGroupName, FontName = "Arial", KeyColor = Color.FromArgb(45, 45, 74),
                           FontColor = Color.FromArgb(224, 224, 255), BorderColor = Color.FromArgb(120, 120, 160), BorderThickness = 1 },
            new KeyGroup { Name = "Klinkers", KeyColor = Color.FromArgb(74, 143, 212), FontSize = 18, BorderThickness = -1 },
            new KeyGroup { Name = "Medeklinkers", FontColor = Color.White, BorderThickness = 2, FontName = "Consolas" },
            new KeyGroup { Name = "Cijfers", BorderThickness = -1 },
        };

        private static void T_GroupEditorGuards()
        {
            Section("Group Editor and its dialogs — strict UI guards (languages and themes)");

            var imported = new List<KeyGroup> { new KeyGroup { Name = "standard" }, new KeyGroup { Name = "Klinkers" }, new KeyGroup { Name = "Pijlen" } };
            var existing = new HashSet<string>(SampleGroups().Select(g => g.Name), StringComparer.OrdinalIgnoreCase);

            CheckDialogGuards("GroupEditor (a group that inherits)", () => new GroupEditorForm(SampleGroups(), "Klinkers"));
            CheckDialogGuards("GroupEditor (standard group)", () => new GroupEditorForm(SampleGroups(), SettingsManager.StandardGroupName));
            CheckDialogGuards("GroupEditor (no groups)", () => new GroupEditorForm(new List<KeyGroup>()));
            CheckDialogGuards("NameDialog", () => new NameDialog(Lang.T("New Group"), n => null));
            CheckDialogGuards("ImportDialog", () => new ImportDialog(imported, existing));
            CheckDialogGuards("TouchMessage (question)", () => new TouchMessage(Lang.T("Delete Group"), string.Format(Lang.T("Delete group msg"), "Klinkers"), question: true));
            CheckDialogGuards("TouchMessage (notice)", () => new TouchMessage(Lang.T("Import Groups"), Lang.T("No groups found in the selected file."), question: false));

            // The list rows are touch targets too: a plain ListBox row is as tall as its text.
            using var f = new GroupEditorForm(SampleGroups());
            DevGallery.Show(f);
            var list = Priv<TouchList>(f, "_lstGroups");
            Assert(list.ItemHeight >= 44, $"Group Editor: list rows are at least 44 px tall ({list.ItemHeight})");
        }

        private static void T_GroupEditor()
        {
            Section("Group Editor — inherit is explicit and nothing changes without a user edit");

            ColorChip Chip(GroupEditorForm f, string n) => Priv<ColorChip>(f, n);
            TouchStepper Stp(GroupEditorForm f, string n) => Priv<TouchStepper>(f, n);
            TouchCheckBox Chk(GroupEditorForm f, string n) => Priv<TouchCheckBox>(f, n);
            TouchChoiceButton Font(GroupEditorForm f) => Priv<TouchChoiceButton>(f, "_cmbFont");
            TouchList Lst(GroupEditorForm f) => Priv<TouchList>(f, "_lstGroups");
            bool Eq(Color a, Color b) => a.ToArgb() == b.ToArgb();
            KeyGroup Result(GroupEditorForm f, string name) { f.CommitToResult(); return f.ResultGroups.Find(g => g.Name == name); }

            // ── Nothing touched: every group comes back exactly as it went in ──
            foreach (var original in SampleGroups())
            {
                var all = SampleGroups();
                using var f = new GroupEditorForm(all, original.Name);
                var r = Result(f, original.Name);
                bool same = Eq(r.KeyColor, original.KeyColor) && Eq(r.FontColor, original.FontColor) && Eq(r.BorderColor, original.BorderColor)
                         && r.BorderThickness == original.BorderThickness && r.FontName == original.FontName && r.FontSize == original.FontSize;
                Assert(same, $"'{original.Name}': opened and applied without a change, every value comes back as it was " +
                             $"(key {r.KeyColor.IsEmpty}/{original.KeyColor.IsEmpty}, font {r.FontColor.IsEmpty}/{original.FontColor.IsEmpty}, " +
                             $"border {r.BorderColor.IsEmpty}/{original.BorderColor.IsEmpty}, thickness {r.BorderThickness}/{original.BorderThickness}, " +
                             $"font '{r.FontName}'/'{original.FontName}', size {r.FontSize}/{original.FontSize})");
            }

            // ── The chips show inheritance: own colour as it is, empty = inherited and shows the standard group's colour ──
            using (var f = new GroupEditorForm(SampleGroups(), "Klinkers"))
            {
                var std = SampleGroups()[0];
                Assert(!Chip(f, "_chipKey").Inherited && Eq(Chip(f, "_chipKey").Value, Color.FromArgb(74, 143, 212)), "Klinkers: its own key colour is shown as set");
                Assert(Chip(f, "_chipFont").Inherited && Eq(Chip(f, "_chipFont").Value, std.FontColor), "Klinkers: an unset font colour is inherited and shows the standard group's colour");
                Assert(Chip(f, "_chipBorder").Inherited && Eq(Chip(f, "_chipBorder").Value, std.BorderColor), "Klinkers: an unset border colour is inherited and shows the standard group's colour");
                Assert(Chip(f, "_chipFont").AccessibleName.Contains(Lang.T("inherited")), "an inherited chip says so in its accessible name");
                Assert(Chip(f, "_chipFont").InheritText != null, "a normal group offers 'inherit' in the colour flyout");
            }
            using (var f = new GroupEditorForm(SampleGroups(), SettingsManager.StandardGroupName))
            {
                Assert(Chip(f, "_chipKey").InheritText == null && Chip(f, "_chipFont").InheritText == null && Chip(f, "_chipBorder").InheritText == null,
                    "the standard group has no parent: 'inherit' is not offered on its colours");
                Assert(!Chk(f, "_chkBorderInherit").Enabled, "the standard group has no 'inherit' check box for the border (hidden and disabled)");
                Assert(Font(f).Items[0].Text == Lang.T("(none / auto)"), "the standard group's font placeholder reads '(none / auto)'");
                Assert(!f.GetType().GetMethod("RemoveSelectedGroup", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null).Equals(true),
                    "the standard group cannot be removed");
            }
            // A standard group with no colours set (an old layout): untouched, it stays unset instead of being written with defaults.
            using (var f = new GroupEditorForm(new List<KeyGroup> { new KeyGroup { Name = SettingsManager.StandardGroupName, BorderThickness = -1 } }))
            {
                var r = Result(f, SettingsManager.StandardGroupName);
                Assert(r.KeyColor.IsEmpty && r.FontColor.IsEmpty && r.BorderColor.IsEmpty, "an unset standard group stays unset when applied untouched (no default colours written)");
            }

            // ── What the controls write back ──
            using (var f = new GroupEditorForm(SampleGroups(), "Klinkers"))
            {
                Chip(f, "_chipFont").SetOwn(Color.Red);
                Chip(f, "_chipKey").SetInherited(Color.Gray);
                var r = Result(f, "Klinkers");
                Assert(Eq(r.FontColor, Color.Red), "choosing a colour stores it");
                Assert(r.KeyColor.IsEmpty, "choosing 'inherit' stores no colour (empty)");
            }
            using (var f = new GroupEditorForm(SampleGroups(), "Klinkers"))
            {
                // Klinkers: font size 18, border inherited.
                Assert(!Chk(f, "_chkAutoSize").Checked && Stp(f, "_stpFontSize").Value == 18 && Stp(f, "_stpFontSize").Enabled, "Klinkers: font size 18 shows the stepper, Auto is off");
                Assert(Chk(f, "_chkBorderInherit").Checked && !Stp(f, "_stpBorder").Enabled, "Klinkers: an inherited border has the check on and the stepper off");
                Assert(Stp(f, "_stpBorder").Value == 1, "…and the stepper shows the standard group's thickness");

                Chk(f, "_chkAutoSize").Checked = true;
                Chk(f, "_chkBorderInherit").Checked = false;
                Stp(f, "_stpBorder").Value = 4;
                var r = Result(f, "Klinkers");
                Assert(r.FontSize == 0, "Auto stores font size 0");
                Assert(r.BorderThickness == 4, "an own border thickness is stored");

                Chk(f, "_chkBorderInherit").Checked = true;
                Assert(Result(f, "Klinkers").BorderThickness == -1, "'Inherit from standard' stores -1");
                Chk(f, "_chkAutoSize").Checked = false;
                Stp(f, "_stpFontSize").Value = 24;
                Assert(Result(f, "Klinkers").FontSize == 24, "Auto off stores the stepper's size");
            }
            using (var f = new GroupEditorForm(SampleGroups(), "Medeklinkers"))
            {
                Assert(Font(f).SelectedItem.Text == "Consolas", "a group's font is selected in the chooser");
                Font(f).SelectedIndex = 0;
                Assert(Result(f, "Medeklinkers").FontName == "", "choosing the placeholder stores no font (inherit)");
            }

            // ── A font that is not installed survives (informational warning, never cleared) ──
            using (var f = new GroupEditorForm(new List<KeyGroup> { new KeyGroup { Name = SettingsManager.StandardGroupName }, new KeyGroup { Name = "Odd", FontName = "NoSuchFont-1234" } }, "Odd"))
            {
                Assert(Font(f).SelectedItem.Text == "NoSuchFont-1234", "an uninstalled font is shown, not replaced by the placeholder");
                Assert(Result(f, "Odd").FontName == "NoSuchFont-1234", "…and kept when applied");
            }

            // ── Switching groups saves the edits of the group that was left ──
            using (var f = new GroupEditorForm(SampleGroups(), "Klinkers"))
            {
                Chip(f, "_chipFont").SetOwn(Color.Lime);
                Lst(f).SelectedIndex = 3;                       // Cijfers
                Assert(f.SelectedGroupName == "Cijfers", "selecting another row shows that group");
                Assert(Chip(f, "_chipFont").Inherited, "…whose own values replace the previous group's on the chips");
                Lst(f).SelectedIndex = 1;
                Assert(Eq(Chip(f, "_chipFont").Value, Color.Lime) && !Chip(f, "_chipFont").Inherited, "coming back: the edit made before leaving was kept");
            }

            // ── The preview follows the controls ──
            using (var f = new GroupEditorForm(SampleGroups(), "Klinkers"))
            {
                var name = Priv<KeyPreviewCard>(f, "_preview").AccessibleName;
                Chip(f, "_chipKey").SetOwn(Color.FromArgb(1, 2, 3));
                typeof(GroupEditorForm).GetMethod("RefreshPreview", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
                Assert(Priv<KeyPreviewCard>(f, "_preview").AccessibleName != name && Priv<KeyPreviewCard>(f, "_preview").AccessibleName.Contains("010203"),
                    "the preview's accessible name follows the key colour");
            }

            // ── Add and delete ──
            using (var f = new GroupEditorForm(SampleGroups(), "Cijfers"))
            {
                Assert(f.TryAddGroup("Pijlen") && f.SelectedGroupName == "Pijlen", "a new group is added and selected");
                var added = Result(f, "Pijlen");
                var std = f.ResultGroups.Find(g => g.Name == SettingsManager.StandardGroupName);
                Assert(Eq(added.KeyColor, std.KeyColor) && added.FontName == std.FontName, "a new group starts as a copy of the standard group (concrete values, not a row of 'inherit')");
                Assert((bool)f.GetType().GetMethod("RemoveSelectedGroup", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null), "the selected group is removed");
                Assert(f.SelectedGroupName == "Cijfers" && f.ResultGroups.Count == 4, "…and the one before it is selected");
            }
            using (var f = new GroupEditorForm(new List<KeyGroup>()))
            {
                Assert(f.SelectedGroupName == null, "no groups: nothing is selected");
                Assert(!Priv<TouchTextBox>(f, "_txtName").Enabled && !Priv<ColorChip>(f, "_chipKey").Enabled && !Font(f).Enabled,
                    "no groups: the style controls are disabled");
                Assert(f.TryAddGroup("First") && Priv<TouchTextBox>(f, "_txtName").Enabled, "adding a group enables them");
            }

            // ── Language change updates the placeholders and the inherit texts ──
            try
            {
                using var f = new GroupEditorForm(SampleGroups(), "Klinkers");
                Lang.Load("nl");
                Application.DoEvents();
                Assert(Font(f).Items[0].Text == Lang.T("(inherit standard)") && Font(f).Items[0].Text != "(inherit standard)", "Dutch: the font placeholder is translated");
                Assert(Chip(f, "_chipFont").InheritText == Lang.T("Inherit from standard") && Chip(f, "_chipFont").InheritText != "Inherit from standard", "Dutch: the flyout's inherit text is translated");
                Assert(Chk(f, "_chkBorderInherit").Text == "Overnemen" && Chk(f, "_chkBorderInherit").AccessibleName == "Standaard overnemen",
                    "Dutch: the border's inherit check box is translated (short text, full accessible name)");
            }
            finally { Lang.Load("en"); }
        }

        private static void T_GroupDialogs()
        {
            Section("Group dialogs — name, import resolution, confirmation");

            // ── NameDialog ──
            using (var d = new NameDialog(Lang.T("New Group"), n => n.Equals("standard", StringComparison.OrdinalIgnoreCase) ? "reserved" : null))
            {
                var txt = Priv<TouchTextBox>(d, "_txt");
                var lbl = Priv<Label>(d, "_lblErr");
                var accept = typeof(NameDialog).GetMethod("Accept", NonPublic);

                txt.Text = "";
                accept.Invoke(d, null);
                Assert(d.ResultName == null && d.DialogResult != DialogResult.OK, "an empty name is not accepted");
                txt.Text = "Standard";
                Assert(lbl.Text == "reserved", "a refused name shows the reason as soon as it is typed");
                accept.Invoke(d, null);
                Assert(d.ResultName == null && d.DialogResult != DialogResult.OK, "a refused name is not accepted");
                txt.Text = "  Pijlen  ";
                Assert(lbl.Text == "", "a good name clears the reason");
                accept.Invoke(d, null);
                Assert(d.ResultName == "Pijlen" && d.DialogResult == DialogResult.OK, "a good name is accepted, trimmed");
            }
            using (var d = new NameDialog(Lang.T("New Group"), n => null))
            {
                var lbl = Priv<Label>(d, "_lblErr");
                Assert(lbl.MinimumSize.Height >= Fluent.FontHint.Height * 2, "the reason's room is reserved from the start, so a message never makes the window grow");
            }

            // ── ImportDialog ──
            string actUpdate = Lang.T("Update standard group style"), actOverwrite = Lang.T("Overwrite"), actAddNew = Lang.T("Add as new");
            var imported = new List<KeyGroup> { new KeyGroup { Name = "standard" }, new KeyGroup { Name = "Klinkers" }, new KeyGroup { Name = "Pijlen" } };
            var existing = new HashSet<string>(new[] { "standard", "Klinkers" }, StringComparer.OrdinalIgnoreCase);
            var accepts = typeof(ImportDialog).GetMethod("Accept", NonPublic);
            using (var d = new ImportDialog(imported, existing))
            {
                var rows = Priv<System.Collections.IList>(d, "_rows");
                TouchChoiceButton Chooser(int i) => (TouchChoiceButton)rows[i].GetType().GetField("Item4").GetValue(rows[i]);
                Assert(Chooser(0).SelectedItem.Text == Lang.T("Skip") && Chooser(1).SelectedItem.Text == Lang.T("Skip") && Chooser(2).SelectedItem.Text == Lang.T("Add"),
                    "defaults: the protected group and a conflict are skipped, a new group is added");
                Assert(Chooser(0).Items.Count == 2 && Chooser(1).Items.Count == 3 && Chooser(2).Items.Count == 1, "choices: 2 for the standard group, 3 for a conflict, 1 for a new group");
                accepts.Invoke(d, new object[] { actUpdate, actOverwrite, actAddNew });
                var dec = d.Decisions;
                Assert(dec.Count == 3 && dec[0].action == GroupEditorForm.ImportAction.Skip && dec[1].action == GroupEditorForm.ImportAction.Skip && dec[2].action == GroupEditorForm.ImportAction.Add,
                    "the default decisions: Skip, Skip, Add");
                Assert(d.DialogResult == DialogResult.OK, "Import closes the dialog with OK");
            }
            using (var d = new ImportDialog(imported, existing))
            {
                var rows = Priv<System.Collections.IList>(d, "_rows");
                TouchChoiceButton Chooser(int i) => (TouchChoiceButton)rows[i].GetType().GetField("Item4").GetValue(rows[i]);
                Chooser(0).SelectedIndex = Chooser(0).Items.FindIndex(i => i.Text == actUpdate);
                Chooser(1).SelectedIndex = Chooser(1).Items.FindIndex(i => i.Text == actAddNew);
                accepts.Invoke(d, new object[] { actUpdate, actOverwrite, actAddNew });
                Assert(d.Decisions[0].action == GroupEditorForm.ImportAction.UpdateStandard && d.Decisions[1].action == GroupEditorForm.ImportAction.AddNew,
                    "chosen decisions: Update standard, Add as new");
            }
            using (var d = new ImportDialog(imported, existing))
            {
                var rows = Priv<System.Collections.IList>(d, "_rows");
                TouchChoiceButton Chooser(int i) => (TouchChoiceButton)rows[i].GetType().GetField("Item4").GetValue(rows[i]);
                Chooser(1).SelectedIndex = Chooser(1).Items.FindIndex(i => i.Text == actOverwrite);
                accepts.Invoke(d, new object[] { actUpdate, actOverwrite, actAddNew });
                Assert(d.Decisions[1].action == GroupEditorForm.ImportAction.Overwrite, "chosen decision: Overwrite");
            }
            using (var d = new ImportDialog(imported, existing))
                Assert(d.Decisions == null, "cancelling gives no decisions");

            // ── TouchMessage: the safe answer is the default ──
            using (var d = new TouchMessage("t", "Delete it?", question: true))
            {
                Assert(d.AcceptButton == d.CancelButton, "a question: Enter and Escape both mean the safe answer");
                Assert(((FluentButton)d.AcceptButton).Text == Lang.T("No"), "a question: the default button is No, so a stray Enter deletes nothing");
            }
            using (var d = new TouchMessage("t", "Done.", question: false))
                Assert(((FluentButton)d.AcceptButton).Text == Lang.T("OK"), "a notice: the default button is OK");
        }
    }
}

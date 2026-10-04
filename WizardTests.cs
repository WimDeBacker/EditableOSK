// WizardTests.cs — the New Keyboard Wizard: UI guards on every page and start mode, navigation, inline validation,
// the live preview, theme tiles, and the Create step (invalid names, overwrite check).

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static NewKeyboardWizard Wizard() => new NewKeyboardWizard();

        private static void WizSet(NewKeyboardWizard w, string field, string text) =>
            ((TextBox)typeof(NewKeyboardWizard).GetField(field, NonPublic).GetValue(w)).Text = text;

        private static T WizCall<T>(NewKeyboardWizard w, string method, params object[] args) =>
            (T)typeof(NewKeyboardWizard).GetMethod(method, NonPublic).Invoke(w, args);

        private static int WizPage(NewKeyboardWizard w) => (int)typeof(NewKeyboardWizard).GetField("_currentPage", NonPublic).GetValue(w);

        private static void T_WizardGuards()
        {
            Section("New Keyboard Wizard — strict UI guards (every page, start mode and theme choice)");
            string tmp = Path.Combine(Path.GetTempPath(), "wizard_guard_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                CheckDialogGuards("Wizard (blank grid)", Wizard);

                CheckDialogGuards("Wizard (pasted labels)", () =>
                {
                    var w = Wizard();
                    Priv<TouchRadioButton>(w, "_rbPaste").Checked = true;
                    WizSet(w, "_txtPaste", "q w e r t y u i o p\r\na s d f g h j k l\r\nz x c v b n m [Backspace]\r\n[Space] \"good morning\"");
                    return w;
                });

                CheckDialogGuards("Wizard (copy from file)", () =>
                {
                    var w = Wizard();
                    Priv<TouchRadioButton>(w, "_rbCopy").Checked = true;
                    WizSet(w, "_txtCopyFile", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "azerty.kbl"));
                    return w;
                });

                CheckDialogGuards("Wizard (theme from file, wrong path)", () =>
                {
                    var w = Wizard();
                    Priv<TouchTile>(w, "_tileFile").Checked = true;
                    WizSet(w, "_txtThemeFile", Path.Combine(tmp, "a rather long folder name", "and_a_long_file_name_for_a_theme.kbl"));
                    return w;
                });
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        private static void T_Wizard()
        {
            Section("New Keyboard Wizard — navigation, validation, preview, theme tiles, create");
            Lang.Load("en");
            string tmp = Path.Combine(Path.GetTempPath(), "wizard_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                // ── Frame and navigation ──────────────────────────────────
                using (var w = Wizard())
                {
                    DevGallery.Show(w);
                    var back = Priv<FluentButton>(w, "_btnBack"); var next = Priv<FluentButton>(w, "_btnNext");
                    var create = Priv<FluentButton>(w, "_btnCreate"); var cancel = Priv<FluentButton>(w, "_btnCancel");
                    Assert(WizPage(w) == 0 && !back.Visible && next.Visible && !create.Visible, "page 1: Next only (no Back, no Create)");
                    Assert(w.AcceptButton == next, "Enter means Next on page 1");
                    Assert(w.CancelButton == cancel && cancel.Visible, "Escape means Cancel, and there is a Cancel button");
                    Assert(Priv<Label>(w, "_lblStep").Text == "Step 1 of 4", "the footer says which step this is");

                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 1 && back.Visible && next.Visible && !create.Visible, "page 2: Back and Next");
                    WizCall<object>(w, "Navigate", 1);
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 3 && back.Visible && !next.Visible && create.Visible, "page 4: Back and Create");
                    Assert(w.AcceptButton == create, "Enter means Create on the last page");
                    Assert(Priv<Label>(w, "_lblStep").Text == "Step 4 of 4", "step label on the last page");
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 3, "Next past the last page does nothing");
                    WizCall<object>(w, "Navigate", -1); WizCall<object>(w, "Navigate", -1); WizCall<object>(w, "Navigate", -1);
                    Assert(WizPage(w) == 0, "Back returns to page 1");
                    WizCall<object>(w, "Navigate", -1);
                    Assert(WizPage(w) == 0, "Back before the first page does nothing");

                    ClickButton(cancel);
                    Assert(w.DialogResult == DialogResult.Cancel, "Cancel closes the wizard with Cancel");
                }

                // Footer buttons side by side have one width, in both languages (rule D23).
                foreach (string code in new[] { "en", "nl" })
                {
                    Lang.Load(code);
                    using var w = Wizard();
                    DevGallery.Show(w);
                    var widths = new[] { "_btnCancel", "_btnBack", "_btnNext", "_btnCreate" }.Select(n => Priv<FluentButton>(w, n).MinimumSize.Width).Distinct().ToList();
                    Assert(widths.Count == 1, $"footer buttons share one width ({code})");
                }
                Lang.Load("en");

                // ── Inline validation: no message box, the page stays, the reason shows under the field ──
                using (var w = Wizard())
                {
                    DevGallery.Show(w);
                    Priv<TouchRadioButton>(w, "_rbCopy").Checked = true;
                    WizSet(w, "_txtCopyFile", Path.Combine(tmp, "nothing.kbl"));
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 0, "copy: a file that does not exist keeps the wizard on page 1");
                    Assert(Priv<Label>(w, "_lblCopyErr").Text == Lang.T("wiz: err no copy file"), "copy: the reason appears under the field");
                    WizSet(w, "_txtCopyFile", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "azerty.kbl"));
                    Assert(Priv<Label>(w, "_lblCopyErr").Text == "", "copy: the reason disappears when the path is edited");
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 1, "copy: an existing file moves on");
                    Assert(Priv<Label>(w, "_lblCopyInfo").Text.Contains("azerty.kbl"), "copy: page 2 names the file the keys come from");
                    Assert(w.CurrentSection == 3, "copy start: page 2 is the copy section (no preview, no steppers)");

                    Priv<TouchRadioButton>(w, "_rbPaste").Checked = true;
                    WizCall<object>(w, "Navigate", -1);
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 1, "paste: page 2 is reached");
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 1, "paste: nothing typed keeps the wizard on page 2");
                    Assert(Priv<Label>(w, "_lblPasteErr").Text == Lang.T("wiz: err empty paste"), "paste: the reason appears under the box");
                    WizSet(w, "_txtPaste", "a b");
                    Assert(Priv<Label>(w, "_lblPasteErr").Text == "", "paste: the reason disappears when the text is edited");
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 2, "paste: keys typed move on");

                    Priv<TouchTile>(w, "_tileFile").Checked = true;
                    Assert(Priv<int>(w, "_selectedPreset") == -1, "the From file tile selects theme source -1");
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 2 && Priv<Label>(w, "_lblThemeErr").Text == Lang.T("wiz: err no theme file"), "theme: From file without a file stays on page 3 with the reason under the field");
                    Priv<TouchTile[]>(w, "_tiles")[3].Checked = true;
                    Assert(Priv<int>(w, "_selectedPreset") == 3, "choosing a preset tile sets the preset index");
                    Assert(!Priv<TouchTile>(w, "_tileFile").Checked, "the tiles are one group: the From file tile is cleared");
                    WizCall<object>(w, "Navigate", 1);
                    Assert(WizPage(w) == 3, "theme: a preset moves on");
                }

                // ── Live preview and size text for pasted labels ──────────
                using (var w = Wizard())
                {
                    DevGallery.Show(w);
                    var blank = Priv<WizardGridPreview>(w, "_previewBlank");
                    var pv    = Priv<WizardGridPreview>(w, "_previewPaste");
                    WizCall<object>(w, "Navigate", 1);
                    Assert(w.CurrentSection == 1, "blank start: page 2 is the grid-size section");
                    Assert(blank.GridRows == 4 && blank.GridCols == 8, "blank: the preview shows 4 x 8 (plus the gear column)");
                    Priv<TouchStepper>(w, "_stpRows").Value = 6;
                    Priv<TouchStepper>(w, "_stpCols").Value = 10;
                    Assert(blank.GridRows == 6 && blank.GridCols == 10, "blank: the steppers resize the preview");

                    WizCall<object>(w, "Navigate", -1);
                    Priv<TouchRadioButton>(w, "_rbPaste").Checked = true;
                    WizCall<object>(w, "Navigate", 1);
                    Assert(w.CurrentSection == 2, "paste start: page 2 is the key-label section (no steppers: the keys decide the size)");
                    WizSet(w, "_txtPaste", "q w e\na s");
                    Assert(pv.GridRows == 2 && pv.GridCols == 3 && pv.LabelAt(0, 1) == "w" && pv.LabelAt(1, 1) == "s",
                        "paste: the labels typed so far show in the preview at once");
                    Assert(Priv<Label>(w, "_lblPasteSize").Text.Contains("2") && Priv<Label>(w, "_lblPasteSize").Text.Contains("3"),
                        "paste: the size is shown as text");
                    WizSet(w, "_txtPaste", "q w e\na s\nz");
                    Assert(pv.GridRows == 3, "paste: another line adds a row");

                    WizCall<object>(w, "Navigate", -1);
                    Priv<TouchRadioButton>(w, "_rbBlank").Checked = true;
                    WizCall<object>(w, "Navigate", 1);
                    Assert(w.CurrentSection == 1 && blank.GridRows == 6, "going back to blank shows the size section again, with the previous size");
                }

                // ── Create ────────────────────────────────────────────────
                using (var w = Wizard())
                {
                    DevGallery.Show(w);
                    var err = Priv<Label>(w, "_lblSaveError");
                    WizSet(w, "_txtFolder", tmp);

                    WizSet(w, "_txtFileName", "  ");
                    WizCall<object>(w, "TryCreate");
                    Assert(err.Text == Lang.T("wiz: err no name") && w.CreatedFilePath == null, "create: an empty name is refused inline");

                    foreach (string bad in new[] { "a/b", "a:b", "a?b", "a*b", "a\"b" })
                    {
                        WizSet(w, "_txtFileName", bad);
                        WizCall<object>(w, "TryCreate");
                        Assert(err.Text == Lang.T("wiz: err bad name") && w.CreatedFilePath == null, $"create: the name '{bad}' is refused with a clear reason");
                    }

                    WizSet(w, "_txtFileName", "ok");
                    WizSet(w, "_txtFolder", Path.Combine(tmp, "missing"));
                    WizCall<object>(w, "TryCreate");
                    Assert(err.Text == Lang.T("wiz: err bad folder") && w.CreatedFilePath == null, "create: a missing folder is refused");
                    WizSet(w, "_txtFolder", tmp);
                    Assert(err.Text == "", "create: the reason disappears when a field is edited");

                    // An existing file is only replaced after the owner says so.
                    string existing = Path.Combine(tmp, "keep.kbl");
                    File.WriteAllText(existing, "KEEP ME");
                    int asked = 0; string askedName = null;
                    w.ConfirmReplace = n => { asked++; askedName = n; return false; };
                    WizSet(w, "_txtFileName", "keep");
                    WizCall<object>(w, "TryCreate");
                    Assert(asked == 1 && askedName == "keep.kbl", "create: an existing file is asked about, by name");
                    Assert(File.ReadAllText(existing) == "KEEP ME" && w.CreatedFilePath == null, "create: answering No leaves the file alone and the wizard open");

                    w.ConfirmReplace = n => true;
                    WizCall<object>(w, "TryCreate");
                    Assert(w.CreatedFilePath == existing && File.ReadAllText(existing) != "KEEP ME" && w.DialogResult == DialogResult.OK,
                        "create: answering Yes replaces the file and closes with OK");
                }

                using (var w = Wizard())
                {
                    DevGallery.Show(w);
                    WizSet(w, "_txtFolder", tmp);
                    WizSet(w, "_txtFileName", "fresh");
                    w.ConfirmReplace = n => { Assert(false, "a new file must not ask for confirmation"); return false; };
                    WizCall<object>(w, "TryCreate");
                    Assert(w.CreatedFilePath == Path.Combine(tmp, "fresh.kbl") && File.Exists(w.CreatedFilePath), "create: a new name is written with the .kbl extension, without asking");
                }
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }
}

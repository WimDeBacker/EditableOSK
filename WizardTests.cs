// WizardTests.cs — the New Keyboard Wizard: UI guards on every page and start mode, navigation, inline validation,
// the live preview, theme tiles, and the Create step (invalid names, overwrite check).

using System;
using System.Collections.Generic;
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
                // Every page is a host section that always exists, so what differs between runs is only which optional rows show:
                // the defaults hide the copy-file row and the theme-file row; "copy + theme from file" shows both (and the paste
                // box holds text, which the preview draws). Two dialogs therefore cover what seven used to (blank, pasted, copy,
                // theme from file, and two help windows); the saving was 58 s of a 131 s suite.
                CheckDialogGuards("Wizard (defaults)", Wizard);

                CheckDialogGuards("Wizard (copy file and theme file rows shown, labels pasted)", () =>
                {
                    var w = Wizard();
                    WizSet(w, "_txtPaste", "q w e r t y u i o p\r\na s d f g h j k l\r\nz x c v b n m [Backspace]\r\n[Space] \"good morning\"");
                    Priv<TouchRadioButton>(w, "_rbCopy").Checked = true;
                    WizSet(w, "_txtCopyFile", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "azerty.kbl"));
                    Priv<TouchTile>(w, "_tileFile").Checked = true;
                    WizSet(w, "_txtThemeFile", Path.Combine(tmp, "a rather long folder name", "and_a_long_file_name_for_a_theme.kbl"));
                    return w;
                });

                CheckDialogGuards("Special keys window", () => new SpecialKeysDialog(false));
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        private static void T_WizardSpecialKeys()
        {
            Section("Wizard paste text — modifier, navigation and function keys");
            WizardKeyParser.KeySpec One(string text, bool dutch = false) => WizardKeyParser.Parse(text, dutch)[0][0];

            // Modifier keys: the label must be one the keyboard recognises as a modifier, the send text the one the stock layouts use.
            foreach (var (token, label, send) in new[] {
                ("shift", "Shift", ""), ("ctrl", "Ctrl", "^"), ("alt", "Alt", "%"), ("altgr", "AltGr", ""),
                ("win", "Win", "win:"), ("caps", "Caps", "{CAPSLOCK}") })
            {
                var k = One("[" + token + "]");
                Assert(k.Label == label && k.Send == send && !k.IsBlank, $"[{token}] is the {label} key (send '{send}')");
                Assert(KeyLayout.ModifierLabels.Contains(k.Label), $"[{token}]: the keyboard treats the key as a modifier");
                Assert(NewKeyboardWizard.ClassifyKey(k.Label, k.Send) == "Besturing", $"[{token}] is put in the Besturing group");
            }
            Assert(One("[SHIFT]").Label == "Shift" && One("[AltGr]").Label == "AltGr" && One("[Windows]").Label == "Win" && One("[Control]").Label == "Ctrl",
                "key names are not case sensitive and have common aliases");

            // Navigation and editing keys use SendKeys names.
            foreach (var (token, label, send) in new[] {
                ("home", "Home", "{HOME}"), ("end", "End", "{END}"), ("pageup", "PgUp", "{PGUP}"), ("PgDn", "PgDn", "{PGDN}"),
                ("insert", "Ins", "{INSERT}"), ("prtsc", "PrtSc", "{PRTSC}"), ("numlock", "NumLk", "{NUMLOCK}"),
                ("scrolllock", "ScrLk", "{SCROLLLOCK}"), ("pause", "Pause", "{BREAK}") })
            {
                var k = One("[" + token + "]");
                Assert(k.Label == label && k.Send == send, $"[{token}] is {label} (send {send})");
                Assert(NewKeyboardWizard.ClassifyKey(k.Label, k.Send) == "Besturing", $"[{token}] is put in the Besturing group");
            }
            Assert(One("[einde]", dutch: true).Send == "{END}" && One("[invoegen]", dutch: true).Send == "{INSERT}", "Dutch aliases for End and Insert");

            // Function keys F1 to F16, nothing else.
            for (int n = 1; n <= 16; n++)
                Assert(One($"[f{n}]").Send == "{F" + n + "}" && One($"[F{n}]").Label == "F" + n, $"[f{n}] is the F{n} key");
            foreach (string bad in new[] { "f0", "f17", "f01", "f", "fx", "f1x" })
                Assert(One("[" + bad + "]").Send == bad, $"[{bad}] is not a function key: it stays ordinary text");

            // Dead keys: the accent as label, "dead:X" as send text; only the five accents SendKeysHelper can compose.
            foreach (char ch in WizardKeyParser.DeadChars)
            {
                var k = One("[dead:" + ch + "]");
                Assert(k.Label == ch.ToString() && k.Send == "dead:" + ch, $"[dead:{ch}] is a dead key ('{ch}')");
                Assert(NewKeyboardWizard.ClassifyKey(k.Label, k.Send) == "Leestekens", $"[dead:{ch}] is put in the Leestekens group");
            }
            Assert(One("[tilde]").Send == "dead:~" && One("[Grave]").Send == "dead:`" && One("[acute]").Send == "dead:´" &&
                   One("[circumflex]").Send == "dead:^" && One("[umlaut]").Send == "dead:¨" && One("[trema]").Send == "dead:¨" && One("[DEAD:~]").Send == "dead:~",
                "dead keys by name, in any case");
            Assert(One("[dead:x]").Send == "dead:x" && One("[dead:x]").Label == "dead:x" && One("[dead:]").Send == "dead:", "[dead:x] with a character that cannot be composed stays ordinary text");

            // The "?" window lists exactly what the parser understands: every spelling parses to a key, and no spelling is missing.
            var inHelp = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var helpRow in WizardKeyParser.Help)
                foreach (var token in helpRow.Tokens)
                {
                    inHelp.Add(token);
                    Assert(WizardKeyParser.LabelOf(token, false) != null, $"help: [{token}] is a key the parser knows");
                }
            var table = (System.Collections.IDictionary)typeof(WizardKeyParser).GetField("SpecialKeys", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (string key in table.Keys)
                Assert(inHelp.Contains(key), $"help: the parser's [{key}] is listed in the window");
            foreach (string name in new[] { "circumflex", "diaeresis", "umlaut", "trema", "tilde", "grave", "acute" })
                Assert(inHelp.Contains(name), $"help: the dead key name [{name}] is listed in the window");
            foreach (char ch in WizardKeyParser.DeadChars)
                Assert(inHelp.Contains("dead:" + ch), $"help: [dead:{ch}] is listed in the window");

            using (var dlg = new SpecialKeysDialog(false))
            {
                DevGallery.Show(dlg);
                var shown = UiGuard.All(dlg).OfType<Label>().Select(l => l.Text).ToList();
                foreach (var helpRow in WizardKeyParser.Help)
                    Assert(shown.Contains(helpRow.Display), $"help window shows '{helpRow.Display}'");
            }
            Assert(Priv<FluentButton>(Wizard(), "_btnKeyHelp").Text == "?", "the paste page has a ? button");

            // A row mixing them with letters keeps every column.
            var row = WizardKeyParser.Parse("[shift] a b [altgr] [win] [alt] [ctrl] [home] [end]", false)[0];
            Assert(row.Count == 9 && row[1].Label == "a" && row[4].Label == "Win" && row[8].Label == "End", "a mixed row keeps one key per token");

            // Through the wizard: the created layout holds these keys, and the modifiers are recognised as modifiers.
            using var w = Wizard();
            Priv<TouchRadioButton>(w, "_rbPaste").Checked = true;
            WizSet(w, "_txtPaste", "[shift] q [altgr]\r\n[ctrl] [win] [alt] [home] [f5]");
            var (layout, _, _, _) = WizCall<(GridLayout, VisualTheme, WindowState, LayoutMeta)>(w, "BuildLayoutData");
            string LabelAtCell(int r, int c) => layout.Cells.First(x => x.Row == r && x.Col == c).Props.Label;
            Assert(LabelAtCell(0, 0) == "Shift" && LabelAtCell(0, 2) == "AltGr" && LabelAtCell(1, 1) == "Win" && LabelAtCell(1, 3) == "Home" && LabelAtCell(1, 4) == "F5",
                "the created layout has the special keys in their cells");
            Assert(layout.Cells.First(x => x.Row == 1 && x.Col == 2).Props.Send == "%" && layout.Cells.First(x => x.Row == 1 && x.Col == 3).Props.GroupName == "Besturing",
                "the created keys carry the send text and group of the stock layouts");
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
                    Assert(w.FormBorderStyle == FormBorderStyle.Sizable, "the wizard can be resized, like the editors");
                    int built = (int)Math.Round(480 * w.DeviceDpi / 96.0);
                    Assert(w.MinimumSize.Width == Math.Min(w.Width, built),
                        $"the wizard cannot be made narrower than the 480 design px it is built for (minimum {w.MinimumSize.Width}px, window {w.Width}px)");
                    var back = Priv<FluentButton>(w, "_btnBack"); var next = Priv<FluentButton>(w, "_btnNext");
                    var create = Priv<FluentButton>(w, "_btnCreate"); var cancel = Priv<FluentButton>(w, "_btnCancel");
                    Assert(WizPage(w) == 0 && !back.Visible && next.Visible && !create.Visible, "page 1: Next only (no Back, no Create)");
                    Assert(w.AcceptButton == next, "Enter means Next on page 1");
                    Assert(w.CancelButton == cancel && cancel.Visible, "Escape means Cancel, and there is a Cancel button");
                    Assert(Priv<Label>(w, "_lblStep").Text == "Step 1 of 4", $"the footer says which step this is (it says '{Priv<Label>(w, "_lblStep").Text}')");

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

                // The ten sample keys of the theme page: one line when there is room, two lines of five when there is not, and every key
                // inside the strip (they used to be painted in one line whatever the width, and the right-hand ones fell off the window).
                using (var w = Wizard())
                {
                    DevGallery.Show(w);
                    w.MinimumSize = Size.Empty;
                    var strip = Priv<Control>(w, "_strip");
                    List<Control> Keys() => UiGuard.All(w).Where(c => c.GetType().Name == "SampleKey").ToList();
                    void Layout(int width, int section) { w.ClientSize = new Size(width, w.ClientSize.Height); w.ShowSectionForGuard(section); Application.DoEvents(); w.PerformLayout(); Application.DoEvents(); w.PerformLayout(); }
                    Layout(1000, 4);
                    var wide = Keys();
                    Assert(wide.Count == 10, "the theme page has ten sample keys");
                    Assert(wide.Select(k => k.Top).Distinct().Count() == 1, "wide window: the sample keys are on one line");
                    Layout(480, 4);
                    var narrow = Keys();
                    Assert(narrow.Select(k => k.Top).Distinct().Count() == 2, "narrow window: the sample keys take two lines");
                    Assert(narrow.All(k => k.Left >= 0 && k.Right <= strip.ClientSize.Width), "narrow window: every sample key lies inside the strip");
                    Assert(NarrowProblemAt(w, 480) == null, "narrow window: nothing on the theme page is cut off");
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

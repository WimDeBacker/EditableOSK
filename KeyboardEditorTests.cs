// KeyboardEditorTests.cs — the redesigned Keyboard Editor (KeyboardEditorForm, spec keyboardeditor_spec.md).
//
//   • strict UI guards (44 px, no clipped text, nothing sticks out, fits 1366 x 768, stacked controls share one width)
//   • Apply keeps every value it does not edit (reflection over all properties, so a field added later is covered)
//   • every control writes its field; the timing aid radio group; Save / Save As / Load; Cancel and the language
//   • database chooser, candidates, export (through the WordPredictionBackend seam: no real database is touched)
//   • alignment: the right edges of stacked controls line up

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
        private static WordPredictionBackend FakeBackend(Func<IReadOnlyList<(string Word, int Count)>> candidates = null, bool overlayExists = false) =>
            new WordPredictionBackend
            {
                Candidates = candidates ?? (() => new List<(string Word, int Count)>()),
                Databases  = () => new List<DatabaseInfo> { new DatabaseInfo(@"C:\fake\worddb_NL.wfq", "nl"), new DatabaseInfo(@"C:\fake\worddb_EN.wfq", "en") },
                IsLoaded = () => true, LoadedLanguage = () => "nl", WordCount = () => 1234,
                OverlayPath = p => p + ".learned", FileExists = p => overlayExists,
                Promote = w => true, Reject = w => true,
            };

        private static KeyboardEditorForm KeyboardEditor(LayoutMeta meta = null, WordPredictionBackend backend = null, VisualTheme theme = null, WindowState window = null,
            Func<bool> onLoad = null, Func<(VisualTheme, WindowState, LayoutMeta)> getSettings = null, Func<List<KeyGroup>> getGroups = null) =>
            new KeyboardEditorForm(theme ?? new VisualTheme(), window ?? new WindowState(), meta ?? new LayoutMeta(), null,
                onLoad, null, getGroups, getSettings, backend ?? FakeBackend());

        /// <summary>Compares every readable and writable public property; reports the first one that differs.</summary>
        private static string FirstDifference(object expected, object actual)
        {
            foreach (var p in expected.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                object a = p.GetValue(expected), b = p.GetValue(actual);
                bool same = a is Color ca && b is Color cb ? ca.ToArgb() == cb.ToArgb() : Equals(a, b);
                if (!same) return $"{p.Name}: {a} became {b}";
            }
            return null;
        }

        private static int RightEdge(Control c) => c.RectangleToScreen(c.ClientRectangle).Right;

        private static void T_KeyboardEditorGuards()
        {
            Section("Keyboard Editor (new) — strict UI guards (all sections, languages and themes)");

            Func<IReadOnlyList<(string Word, int Count)>> manyCandidates = () =>
                new List<(string Word, int Count)> { ("kapstok", 2), ("tuinhek", 2), ("een-heel-lang-kandidaatwoord-dat-niet-past", 1), ("fietspomp", 1), ("wasbeer", 1) };

            // (a) everything on and filled, an explicit database, candidates
            CheckDialogGuards("KeyboardEditor (everything on)", () => KeyboardEditor(
                new LayoutMeta { StickyModifiers = true, HoldToEdit = true, SlowKeysMs = 300, ShowTimingAnimation = true, ShowCornerLabels = false, WordDatabase = "worddb_NL.wfq", WordLearningEnabled = true },
                FakeBackend(manyCandidates, overlayExists: true), window: new WindowState { AlwaysOnTop = true, HideTitlebar = true }));
            // (b) defaults
            CheckDialogGuards("KeyboardEditor (defaults)", () => KeyboardEditor());
            // (c) a stored database that is not found, learning off, dwell click
            CheckDialogGuards("KeyboardEditor (database not found)", () => KeyboardEditor(
                new LayoutMeta { WordDatabase = "old_db.wfq", WordLearningEnabled = false, DwellMs = 1000 }, FakeBackend(manyCandidates)));

            using var f = KeyboardEditor(backend: FakeBackend(manyCandidates));
            DevGallery.Show(f);
            f.SectionBarAccess.Select(2, focus: false); Application.DoEvents(); f.PerformLayout();
            var list = Priv<TouchList>(f, "_lstWPCandidates");
            Assert(list.ItemHeight >= 44, $"Keyboard Editor: candidate rows are at least 44 px tall ({list.ItemHeight})");
            var named = UiGuard.All(f).Where(Touch.IsPointerControl).Where(c => string.IsNullOrWhiteSpace(c.AccessibleName) && string.IsNullOrWhiteSpace(c.Text)).ToList();
            Assert(named.Count == 0, $"Keyboard Editor: every pointer control has a name ({(named.Count > 0 ? named[0].GetType().Name : "")})");
            CheckInteractiveAccessibility(f, "KeyboardEditorForm");
            CheckTabIndexUnique(f, "KeyboardEditorForm");
            CheckTooltipsRegistered(f, "KeyboardEditorForm");
        }

        private static void T_KeyboardEditor()
        {
            Section("Keyboard Editor (new) — Apply keeps what it does not edit, every control writes its field");

            // ── Untouched: every field comes back as it went in (odd values on purpose) ──
            foreach (var (slow, dwell) in new[] { (50, 0), (0, 7000), (0, 0), (3000, 0) })
            {
                var theme = new VisualTheme { Opacity = 0.755, BackgroundColor = Color.FromArgb(10, 20, 30), FontName = "Odd Font", FontSize = 17,
                    FontColor = Color.FromArgb(1, 2, 3), KeyColor = Color.FromArgb(4, 5, 6), BorderColor = Color.FromArgb(7, 8, 9), BorderThickness = 3 };
                var window = new WindowState { WindowWidth = 800, WindowHeight = 300, AlwaysOnTop = false, HideTitlebar = true };
                var meta = new LayoutMeta { Language = "nl", LastFile = "x.kbl", GearRow = 2, GearCol = 5, ToolbarTheme = ToolbarTheme.Light, StickyModifiers = false, HoldToEdit = true,
                    SlowKeysMs = slow, DwellMs = dwell, ShowTimingAnimation = false, ShowCornerLabels = false, WordDatabase = "missing.wfq", WordLearningEnabled = false };
                using var f = KeyboardEditor(meta, theme: theme, window: window);
                Assert(f.ApplyForTest(), $"slow {slow} / dwell {dwell}: Apply succeeds");
                string d1 = FirstDifference(theme, f.ResultTheme), d2 = FirstDifference(window, f.ResultWindow), d3 = FirstDifference(meta, f.ResultMeta);
                Assert(d1 == null, $"slow {slow} / dwell {dwell}: the theme comes back identical {(d1 != null ? "— " + d1 : "")}");
                Assert(d2 == null, $"slow {slow} / dwell {dwell}: the window state comes back identical {(d2 != null ? "— " + d2 : "")}");
                Assert(d3 == null, $"slow {slow} / dwell {dwell}: the layout meta comes back identical {(d3 != null ? "— " + d3 : "")}");
            }
            using (var f = KeyboardEditor())
            {
                f.ApplyForTest();
                Assert(!ReferenceEquals(f.ResultMeta, null) && f.FileAction == KeyboardFileAction.None && f.DialogResult == DialogResult.OK, "Apply: OK and no file action");
            }

            // ── Each control writes its field ──
            foreach (bool v in new[] { true, false })
            {
                using var f = KeyboardEditor();
                Chk(f, "_chkAlwaysOnTop").Checked = v; Chk(f, "_chkHideTitlebar").Checked = v; Chk(f, "_chkStickyMods").Checked = v; Chk(f, "_chkHoldToEdit").Checked = v;
                Chk(f, "_chkTimingAnimation").Checked = v; Chk(f, "_chkCornerLabels").Checked = v; Chk(f, "_chkWPLearning").Checked = v;
                f.ApplyForTest();
                Assert(f.ResultWindow.AlwaysOnTop == v && f.ResultWindow.HideTitlebar == v, $"check boxes {v}: always on top and hide title bar");
                Assert(f.ResultMeta.StickyModifiers == v && f.ResultMeta.HoldToEdit == v && f.ResultMeta.ShowTimingAnimation == v && f.ResultMeta.ShowCornerLabels == v && f.ResultMeta.WordLearningEnabled == v,
                    $"check boxes {v}: sticky, hold, animation, corner labels, remember typed words");
            }
            for (int i = 0; i < 3; i++)
            {
                using var f = KeyboardEditor();
                Priv<TouchChoiceButton>(f, "_cmbToolbarTheme").SelectedIndex = i;
                f.ApplyForTest();
                Assert((int)f.ResultMeta.ToolbarTheme == i, $"toolbar theme row {i} is stored as {(ToolbarTheme)i} (explicit, not by index luck)");
            }
            using (var f = KeyboardEditor())
            {
                Priv<TouchStepper>(f, "_stpOpacity").Value = 25;
                Priv<ColorChip>(f, "_chipBackground").SetOwn(Color.FromArgb(200, 10, 20));
                f.ApplyForTest();
                Assert(Math.Abs(f.ResultTheme.Opacity - 0.75) < 1e-9, $"opacity stepper 25 → opacity 0.75 ({f.ResultTheme.Opacity})");
                Assert(f.ResultTheme.BackgroundColor.ToArgb() == Color.FromArgb(200, 10, 20).ToArgb(), "the background chip writes the background colour");
            }
            using (var f = KeyboardEditor()) { Priv<TouchStepper>(f, "_stpOpacity").Value = 80; f.ApplyForTest(); Assert(Math.Abs(f.ResultTheme.Opacity - 0.2) < 1e-9, "opacity stepper 80 → 0.2"); }
            using (var f = KeyboardEditor(theme: new VisualTheme { Opacity = 1.0 })) { f.ApplyForTest(); Assert(f.ResultTheme.Opacity == 1.0, "an untouched opaque window stays opaque"); }
            using (var f = KeyboardEditor(theme: new VisualTheme { Opacity = 0.755 })) { f.ApplyForTest(); Assert(f.ResultTheme.Opacity == 0.755, "an untouched 0.755 is not rounded to 0.76 (kept as it is)"); }

            Section("Keyboard Editor (new) — the timing aid radio group");
            using (var f = KeyboardEditor())
            {
                var off = Priv<TouchRadioButton>(f, "_optTimingOff"); var slow = Priv<TouchRadioButton>(f, "_optSlowKeys"); var dwell = Priv<TouchRadioButton>(f, "_optDwell");
                var stpS = Priv<TouchStepper>(f, "_stpSlowKeys"); var stpD = Priv<TouchStepper>(f, "_stpDwell"); var anim = Chk(f, "_chkTimingAnimation");
                Assert(off.Checked && !slow.Checked && !dwell.Checked, "default: Off is selected");
                Assert(!stpS.Enabled && !stpD.Enabled && !anim.Enabled, "Off: both steppers and the animation check box are disabled");
                slow.Checked = true;
                Assert(!off.Checked && slow.Checked && !dwell.Checked && stpS.Enabled && !stpD.Enabled && anim.Enabled, "Slow keys: its stepper and the animation check box are enabled, Dwell's stepper is not");
                dwell.Checked = true;
                Assert(!slow.Checked && dwell.Checked && !stpS.Enabled && stpD.Enabled && anim.Enabled, "Dwell click: selecting it clears Slow keys; the steppers swap");
                stpD.Value = 2500; stpS.Value = 400;
                anim.Checked = false;
                off.Checked = true;
                Assert(!anim.Enabled && !anim.Checked, "Off: the animation check box is disabled and keeps its value");
                f.ApplyForTest();
                Assert(f.ResultMeta.SlowKeysMs == 0 && f.ResultMeta.DwellMs == 0, "Off stores 0 and 0 (the steppers' values are not stored)");
                Assert(!f.ResultMeta.ShowTimingAnimation, "the animation setting is stored even while its check box is disabled");
            }
            using (var f = KeyboardEditor()) { Priv<TouchRadioButton>(f, "_optSlowKeys").Checked = true; Priv<TouchStepper>(f, "_stpSlowKeys").Value = 450; f.ApplyForTest();
                Assert(f.ResultMeta.SlowKeysMs == 450 && f.ResultMeta.DwellMs == 0, "Slow keys 450 is stored, Dwell is 0"); }
            using (var f = KeyboardEditor()) { Priv<TouchRadioButton>(f, "_optDwell").Checked = true; Priv<TouchStepper>(f, "_stpDwell").Value = 1800; f.ApplyForTest();
                Assert(f.ResultMeta.DwellMs == 1800 && f.ResultMeta.SlowKeysMs == 0, "Dwell click 1800 is stored, Slow keys is 0"); }
            using (var f = KeyboardEditor(new LayoutMeta { SlowKeysMs = 800, DwellMs = 1200 }))
            {
                Assert(Priv<TouchRadioButton>(f, "_optSlowKeys").Checked && !Priv<TouchRadioButton>(f, "_optDwell").Checked, "a file with both values: Slow keys is shown (the first option)");
                Assert(Priv<TouchRadioButton>(f, "_optSlowKeys").Checked && Priv<TouchStepper>(f, "_stpSlowKeys").Value == 800, "…with its own value");
            }
            using (var f = KeyboardEditor(new LayoutMeta { SlowKeysMs = 50 }))
            {
                Assert(Priv<TouchStepper>(f, "_stpSlowKeys").Value == 50 && Priv<TouchStepper>(f, "_stpSlowKeys").Minimum <= 50, "a loaded value below the usual range is shown as it is (the range widens)");
                f.ApplyForTest();
                Assert(f.ResultMeta.SlowKeysMs == 50, "…and stored as it is (not silently clamped to 100)");
            }
            // The radio buttons move with the arrow keys and are one group.
            using (var f = KeyboardEditor())
            {
                DevGallery.Show(f);
                f.SectionBarAccess.Select(1, focus: false); Application.DoEvents(); f.PerformLayout();
                var off = Priv<TouchRadioButton>(f, "_optTimingOff"); var slow = Priv<TouchRadioButton>(f, "_optSlowKeys");
                Key(off, Keys.Down);
                Assert(slow.Checked, "arrow Down on Off selects Slow keys");
                Assert(off.Parent == slow.Parent && off.Parent == Priv<TouchRadioButton>(f, "_optDwell").Parent, "the three radio buttons are one group (one parent)");
            }

            Section("Keyboard Editor (new) — Save, Save As, Load, Cancel, language");
            using (var f = KeyboardEditor()) { Chk(f, "_chkHoldToEdit").Checked = true; Assert(f.ApplyForTest(KeyboardFileAction.Save) && f.FileAction == KeyboardFileAction.Save && f.ResultMeta.HoldToEdit,
                "Save: the file action is set and the result already holds the edit made just before (the old dialog saved the previous values)"); }
            using (var f = KeyboardEditor()) { Assert(f.ApplyForTest(KeyboardFileAction.SaveAs) && f.FileAction == KeyboardFileAction.SaveAs, "Save As: the file action is set"); }
            using (var f = KeyboardEditor()) { DevGallery.Show(f); ClickButton(Priv<FluentButton>(f, "_btnSaveFile")); Assert(f.FileAction == KeyboardFileAction.Save && f.DialogResult == DialogResult.OK, "the Save button applies and closes with OK"); }
            using (var f = KeyboardEditor()) { DevGallery.Show(f); ClickButton(Priv<FluentButton>(f, "_btnApply")); Assert(f.FileAction == KeyboardFileAction.None && f.DialogResult == DialogResult.OK, "the Apply button: OK, no file action"); }

            var loadedTheme = new VisualTheme { BackgroundColor = Color.FromArgb(9, 9, 9), Opacity = 0.5 };
            var loadedWindow = new WindowState { AlwaysOnTop = true, HideTitlebar = true };
            var loadedMeta = new LayoutMeta { StickyModifiers = false, DwellMs = 1500, WordDatabase = "worddb_EN.wfq", Language = "en" };
            using (var f = KeyboardEditor(onLoad: () => false, getSettings: () => (loadedTheme, loadedWindow, loadedMeta)))
            {
                Chk(f, "_chkHoldToEdit").Checked = true;
                Assert(!f.LoadFromCaller(), "Load: a cancelled file dialog loads nothing");
                Assert(Chk(f, "_chkHoldToEdit").Checked, "Load: …and the edits made in the dialog stay (the old dialog wiped them)");
            }
            using (var f = KeyboardEditor(onLoad: () => true, getSettings: () => (loadedTheme, loadedWindow, loadedMeta), getGroups: () => new List<KeyGroup> { new KeyGroup { Name = "standard" } }))
            {
                Chk(f, "_chkHoldToEdit").Checked = true;
                Assert(f.LoadFromCaller(), "Load: a loaded file refills the dialog");
                Assert(Priv<TouchRadioButton>(f, "_optDwell").Checked && Priv<TouchStepper>(f, "_stpDwell").Value == 1500 && !Chk(f, "_chkStickyMods").Checked && !Chk(f, "_chkHoldToEdit").Checked && Chk(f, "_chkAlwaysOnTop").Checked,
                    "Load: every control shows the loaded values");
                Assert(Priv<ColorChip>(f, "_chipBackground").Value.ToArgb() == Color.FromArgb(9, 9, 9).ToArgb() && Priv<TouchStepper>(f, "_stpOpacity").Value == 50, "Load: background and transparency show the loaded values");
                Assert(f.ResultGroups.Count == 1, "Load: the groups are the loaded layout's");
                f.ApplyForTest();
                Assert(f.ResultMeta.WordDatabase == "worddb_EN.wfq" && f.ResultMeta.DwellMs == 1500, "Load: Apply stores the loaded file's values, not the old ones");
            }

            string was = Lang.CurrentCode;
            try
            {
                Lang.Load("en");
                int nl = Lang.GetAvailable().FindIndex(l => l.Code == "nl");
                if (nl >= 0)
                {
                    using (var f = KeyboardEditor())
                    {
                        DevGallery.Show(f);
                        Priv<TouchChoiceButton>(f, "_cmbLanguage").SelectedIndex = nl;
                        Assert(Lang.CurrentCode == "nl", "the language changes live");
                        Assert(f.Text == Lang.T("Edit Keyboard") && f.Text != "Edit Keyboard", "…and the dialog translates itself (title)");
                        Assert(f.SectionBarAccess.Tabs[0].Text == "Algemeen" && f.SectionBarAccess.Tabs[1].Text == "Toegankelijkheid", "…and the section titles");
                        Assert(Priv<TouchChoiceButton>(f, "_cmbToolbarTheme").Items[0].Text == "Donker", "…and the toolbar theme rows (the old dialog translated them once)");
                        Assert(Priv<TouchChoiceButton>(f, "_cmbWPDatabase").Items[0].Text == "(automatisch)", "…and the '(auto)' database row");
                        Assert(Priv<Label>(f, "_lblImmediateHint").Text.StartsWith("Toevoegen"), "…and the hint labels");
                        f.DialogResult = DialogResult.Cancel; f.Close();
                        Assert(Lang.CurrentCode == "en", "Cancel restores the language that was active when the dialog opened");
                    }
                    using (var f = KeyboardEditor())
                    {
                        DevGallery.Show(f);
                        Priv<TouchChoiceButton>(f, "_cmbLanguage").SelectedIndex = nl;
                        f.ApplyForTest();
                        Assert(Lang.CurrentCode == "nl", "Apply keeps the language that was chosen");
                    }
                }
            }
            finally { Lang.Load(was); }

            Section("Keyboard Editor (new) — word database chooser, candidates, export");
            bool learning = WordDatabase.LearningEnabled;
            try
            {
                WordDatabase.LearningEnabled = true;
                using (var f = KeyboardEditor(new LayoutMeta { WordLearningEnabled = true }))
                {
                    Chk(f, "_chkWPLearning").Checked = false;
                    Assert(WordDatabase.LearningEnabled, "toggling 'Remember typed words' does not switch the engine while the dialog is open");
                    f.ApplyForTest();
                    Assert(WordDatabase.LearningEnabled && !f.ResultMeta.WordLearningEnabled, "Apply stores the setting; the engine is switched by the caller after OK");
                }
            }
            finally { WordDatabase.LearningEnabled = learning; }

            using (var f = KeyboardEditor(new LayoutMeta { WordDatabase = "" }))
            {
                var db = Priv<TouchChoiceButton>(f, "_cmbWPDatabase");
                Assert(db.Items.Count == 3 && db.SelectedIndex == 0, "database chooser: '(auto)' and the two base files, auto selected");
                db.SelectedIndex = 2; f.ApplyForTest();
                Assert(f.ResultMeta.WordDatabase == "worddb_EN.wfq", "choosing a file stores its file name");
            }
            using (var f = KeyboardEditor(new LayoutMeta { WordDatabase = "worddb_nl.WFQ" }))
            {
                f.ApplyForTest();
                Assert(f.ResultMeta.WordDatabase == "worddb_NL.wfq", "a stored name is matched ignoring case (and stored as the real file name)");
            }
            using (var f = KeyboardEditor(new LayoutMeta { WordDatabase = "gone.wfq" }))
            {
                var db = Priv<TouchChoiceButton>(f, "_cmbWPDatabase");
                Assert(db.Items.Count == 4 && db.SelectedIndex == 1 && db.SelectedItem.Text.Contains("gone.wfq"), "a stored database that is not found stays as an extra, selected row");
                var warn = (ErrorProvider)typeof(FluentDialogBase).GetField("_fontWarn", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
                Assert(!string.IsNullOrEmpty(warn.GetError(db)), "…with a warning on it");
                bool pending = (bool)typeof(FluentDialogBase).GetMethod("HasPendingErrors", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, new object[] { null });
                Assert(!pending, "…which never blocks Apply");
                f.ApplyForTest();
                Assert(f.ResultMeta.WordDatabase == "gone.wfq", "…and Apply stores it unchanged (the old dialog silently rewrote it to '(auto)')");
            }

            var words = new List<(string Word, int Count)> { ("kapstok", 2), ("tuinhek", 1) };
            var promoted = new List<string>(); var rejected = new List<string>();
            var backend = FakeBackend(() => words.ToList());
            backend.Promote = w => { promoted.Add(w); words.RemoveAll(x => x.Word == w); return true; };
            backend.Reject  = w => { rejected.Add(w);  words.RemoveAll(x => x.Word == w); return true; };
            using (var f = KeyboardEditor(new LayoutMeta { WordLearningEnabled = true }, backend))
            {
                var list = Priv<TouchList>(f, "_lstWPCandidates"); var pro = Priv<FluentButton>(f, "_btnWPPromote"); var rej = Priv<FluentButton>(f, "_btnWPReject");
                Assert(list.Items.Count == 2 && list.Items[0].ToString() == "kapstok (2)", "candidates: listed as 'word (count)'");
                Assert(!pro.Enabled && !rej.Enabled, "candidates: Promote and Reject need a selection");
                list.SelectedIndex = 1;
                Assert(pro.Enabled && rej.Enabled, "candidates: …and are enabled once a row is selected");
                ClickButton(pro);
                Assert(promoted.SequenceEqual(new[] { "tuinhek" }) && list.Items.Count == 1, "Promote: the plain word is promoted and the list refreshes");
                list.SelectedIndex = 0;
                ClickButton(rej);
                Assert(rejected.SequenceEqual(new[] { "kapstok" }) && list.Items.Count == 0, "Reject: the word is rejected and the list refreshes");
                Assert(list.EmptyText == Lang.T("wp: no candidates"), "an empty list says there are no candidates yet");
                Chk(f, "_chkWPLearning").Checked = false;
                Assert(!list.Enabled && !pro.Enabled && list.EmptyText == Lang.T("wp: learning off hint"), "learning off: the list and the buttons are disabled and the list says why");
            }
            words = new List<(string Word, int Count)> { ("kapstok", 2), ("tuinhek", 1), ("wasbeer", 1) };
            using (var f = KeyboardEditor(new LayoutMeta { WordLearningEnabled = true }, backend))
            {
                var list = Priv<TouchList>(f, "_lstWPCandidates");
                list.SelectedIndex = 1;
                words = new List<(string Word, int Count)> { ("wasbeer", 3), ("tuinhek", 1) };       // a refresh with another order
                typeof(KeyboardEditorForm).GetMethod("PopulateCandidates", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
                Assert(list.SelectedItem?.ToString() == "tuinhek (1)", "the selection is kept by word when the list is refreshed");
            }
            foreach (bool exists in new[] { false, true })
                using (var f = KeyboardEditor(new LayoutMeta { WordDatabase = "worddb_NL.wfq" }, FakeBackend(overlayExists: exists)))
                {
                    var exp = Priv<FluentButton>(f, "_btnWPExport"); var hint = Priv<Label>(f, "_lblExportHint");
                    Assert(exp.Enabled == exists, $"export is {(exists ? "enabled when learned words exist" : "disabled while nothing was learned")}");
                    Assert((hint.Text == "") == exists, "the export hint is shown exactly while Export is disabled");
                }

            Section("Keyboard Editor (new) — errors switch to the section that holds them; resources");
            var holders = new[] { "_cmbLanguage", "_chkStickyMods", "_chkWPLearning" };
            for (int s = 0; s < holders.Length; s++)
            {
                using var f = KeyboardEditor();
                DevGallery.Show(f);
                var err = (ErrorProvider)typeof(FluentDialogBase).GetField("_err", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
                var ctrl = (Control)typeof(KeyboardEditorForm).GetField(holders[s], BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
                f.SectionBarAccess.Select((s + 1) % 3, focus: false);
                err.SetError(ctrl, "x");
                Assert(!f.ApplyForTest() && f.DialogResult != DialogResult.OK, $"section {s + 1}: an error blocks Apply");
                Assert(f.SectionBarAccess.SelectedIndex == s && f.SectionBarAccess.HasError(s), $"section {s + 1}: the dialog switches to it and marks it");
                err.SetError(ctrl, "");
                Assert(f.ApplyForTest() && !f.SectionBarAccess.HasError(s), $"section {s + 1}: once the error is gone Apply works and the mark clears");
            }
            using (var f = KeyboardEditor()) Assert(f.Text == "Edit Keyboard" && f.SectionBarAccess.Count == 3, "window title and three sections");

            // The static event handler is removed when the form is disposed, even when it was never shown.
            var loadedField = typeof(WordDatabase).GetField("Loaded", BindingFlags.NonPublic | BindingFlags.Static);
            int Subscribers() => ((Delegate)loadedField?.GetValue(null))?.GetInvocationList().Length ?? 0;
            int before = Subscribers();
            var tmp = KeyboardEditor();
            Assert(Subscribers() == before + 1, "the form subscribes to WordDatabase.Loaded");
            tmp.Dispose();
            Assert(Subscribers() == before, "…and unsubscribes when disposed (even if never shown)");
        }

        private static TouchCheckBox Chk(KeyboardEditorForm f, string name) => Priv<TouchCheckBox>(f, name);

        private static void T_KeyboardEditorAlignment()
        {
            Section("Keyboard Editor (new) — stacked controls share one width, right edges line up");

            using var f = KeyboardEditor(new LayoutMeta { SlowKeysMs = 300, WordDatabase = "worddb_NL.wfq" }, FakeBackend(() => new List<(string Word, int Count)> { ("kapstok", 2) }));
            DevGallery.Show(f);
            void GoTo(int i) { f.SectionBarAccess.Select(i, focus: false); Application.DoEvents(); f.PerformLayout(); }
            bool Same(string what, params Control[] cs) { int lo = cs.Min(RightEdge), hi = cs.Max(RightEdge); Assert(hi - lo <= 1, $"{what}: right edges {string.Join(", ", cs.Select(RightEdge))}"); return hi - lo <= 1; }

            GoTo(0);
            Same("General: Background, Always on top and Hide title bar", Priv<ColorChip>(f, "_chipBackground"), Chk(f, "_chkAlwaysOnTop"), Chk(f, "_chkHideTitlebar"));
            Assert(Priv<TouchChoiceButton>(f, "_cmbLanguage").Width == Priv<TouchChoiceButton>(f, "_cmbToolbarTheme").Width, "General: the two choosers have one width");
            var s1 = Priv<FluentButton>(f, "_btnSaveFile"); var s2 = Priv<FluentButton>(f, "_btnSaveAsFile"); var s3 = Priv<FluentButton>(f, "_btnLoadFile");
            Assert(s1.Width == s2.Width && s2.Width == s3.Width, $"General: Save, Save As and Load have one width ({s1.Width}, {s2.Width}, {s3.Width})");

            GoTo(1);
            var group = (Control)Priv<TouchRadioButton>(f, "_optSlowKeys").Parent.Parent;     // the TouchGroup around the grid
            Same("Accessibility: Sticky modifiers, Hold to enter edit mode, the timing group and the corner labels",
                Chk(f, "_chkStickyMods"), Chk(f, "_chkHoldToEdit"), group, Chk(f, "_chkCornerLabels"));
            Same("Accessibility: Off, Slow keys, Dwell click and the animation check box", Priv<TouchRadioButton>(f, "_optTimingOff"), Priv<TouchRadioButton>(f, "_optSlowKeys"), Priv<TouchRadioButton>(f, "_optDwell"), Chk(f, "_chkTimingAnimation"));
            Same("Accessibility: the two steppers", Priv<TouchStepper>(f, "_stpSlowKeys"), Priv<TouchStepper>(f, "_stpDwell"));

            GoTo(2);
            Same("Word prediction: Remember typed words, the database chooser and Export", Chk(f, "_chkWPLearning"), Priv<TouchChoiceButton>(f, "_cmbWPDatabase"), Priv<FluentButton>(f, "_btnWPExport"));
            Same("Word prediction: the candidate list and Reject", Priv<Panel>(f, "_candidateFrame"), Priv<FluentButton>(f, "_btnWPReject"));
            var pro = Priv<FluentButton>(f, "_btnWPPromote"); var rej = Priv<FluentButton>(f, "_btnWPReject");
            Assert(Math.Abs(pro.Width - rej.Width) <= 1, $"Word prediction: Promote and Reject have one width ({pro.Width}, {rej.Width})");
            Assert(UiGuard.StackedEdges(f, visibleOnly: false).Count == 0, "the alignment guard finds nothing in any section");
        }
    }
}

// ScanningTests.cs — scanning: the settings (kept in the user's file), the state machine (Scanner), the switch keys (ScanInput) and the dialog.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static GridCell ScanCell(int row, int col, string label) => new GridCell(row, col, new KeyProps(label, label));

        private static void T_Scanning()
        {
            Section("Scanning — settings of the user");
            string saved = UserData.DirectoryOverride;
            string dir = Path.Combine(Path.GetTempPath(), "osk_scan_" + Guid.NewGuid().ToString("N"));
            try
            {
                UserData.DirectoryOverride = dir;
                UserSettings.Reset();
                Assert(!UserSettings.Scan.Enabled && !UserSettings.Scan.TwoSwitch && UserSettings.Scan.ScanMs == 1000 && UserSettings.Scan.Rounds == 3 &&
                       UserSettings.Scan.SelectKey == ScanSettings.VkF8 && UserSettings.Scan.StepKey == ScanSettings.VkF9 && UserSettings.Scan.SoundOnSelect && !UserSettings.Scan.SoundOnStep,
                    "no file: scanning is off and has the proposed defaults");
                UserSettings.InterfaceLanguage = "nl";
                var s = UserSettings.Scan.Clone();
                s.Enabled = true; s.TwoSwitch = true; s.SelectKey = 0x70; s.StepKey = 0x71; s.ScanMs = 1700; s.FirstExtraMs = 900; s.Rounds = 5; s.IgnoreRepeatMs = 450; s.SoundOnStep = true; s.SoundOnSelect = false;
                s.RowColor = System.Drawing.Color.FromArgb(10, 200, 30); s.KeyColor = System.Drawing.Color.FromArgb(250, 20, 200);
                Assert(UserSettings.Scan.RowColor.ToArgb() != UserSettings.Scan.KeyColor.ToArgb(), "the row ring and the key ring have different colours by default");
                UserSettings.SetScan(s);
                UserSettings.Reset();
                Assert(UserSettings.Scan.SameAs(s) && UserSettings.Scan.RowColor.ToArgb() == s.RowColor.ToArgb() && UserSettings.Scan.KeyColor.ToArgb() == s.KeyColor.ToArgb(),
                    "the scan settings (colours too) come back after a restart");
                Assert(UserSettings.InterfaceLanguage == "nl", "…and saving them keeps the language of the screens");
                UserSettings.InterfaceLanguage = "en";
                UserSettings.Reset();
                Assert(UserSettings.Scan.SameAs(s), "…and saving the language keeps the scan settings");

                File.WriteAllText(Path.Combine(dir, "user.xml"), "<UserSettings><Scanning Enabled=\"1\" ScanMs=\"5\" Rounds=\"99\" SelectKey=\"27\" StepKey=\"27\" IgnoreRepeatMs=\"-3\" /></UserSettings>");
                UserSettings.Reset();
                var c = UserSettings.Scan;
                Assert(c.Enabled && c.ScanMs == ScanSettings.MinScanMs && c.Rounds == ScanSettings.MaxRounds && c.IgnoreRepeatMs == 0, "hand-edited values are brought inside their range");
                Assert(c.SelectKey != ScanSettings.VkEscape && c.StepKey != ScanSettings.VkEscape && c.SelectKey != c.StepKey, "Esc cannot be a switch and the two switches differ");
                File.WriteAllText(Path.Combine(dir, "user.xml"), "not xml");
                UserSettings.Reset();
                Assert(!UserSettings.Scan.Enabled, "a damaged file switches scanning off instead of stopping the program");

                // The language stays "not chosen" (so Windows may decide) while only scan settings were saved.
                File.Delete(Path.Combine(dir, "user.xml"));
                UserSettings.Reset();
                UserSettings.SetScan(new ScanSettings { Enabled = true });
                Assert(!File.ReadAllText(Path.Combine(dir, "user.xml")).Contains("InterfaceLanguage"), "saving scan settings does not fix the language of the screens");
                Assert(ScanSettings.KeyName(ScanSettings.VkReturn) == "Enter" && ScanSettings.KeyName(0x77) == "F8", "key names");
            }
            finally { UserData.DirectoryOverride = saved; UserSettings.Reset(); try { Directory.Delete(dir, true); } catch { } }

            Section("Scanning — Scanner (rows, keys, rounds)");
            var rows = Scanner.BuildRows(new[] { ScanCell(2, 1, "f"), ScanCell(0, 2, "c"), ScanCell(0, 0, "a"), ScanCell(2, 0, "e"), ScanCell(1, 5, "d"), ScanCell(0, 1, "b") });
            Assert(rows.Count == 3 && string.Concat(rows[0].Select(x => x.Props.Label)) == "abc" && rows[1].Count == 1 && string.Concat(rows[2].Select(x => x.Props.Label)) == "ef",
                "rows go top to bottom, keys left to right; the gaps do not matter");
            Assert(Scanner.BuildRows(Array.Empty<GridCell>()).Count == 0, "no keys: no rows");

            ScanSettings Auto() => new ScanSettings { Enabled = true, ScanMs = 1000, FirstExtraMs = 500, Rounds = 2, IgnoreRepeatMs = 300 };
            Scanner Make(ScanSettings st, List<GridCell> picked = null, Action stopped = null)
            {
                var sc = new Scanner(st);
                sc.SetRows(rows);
                if (picked != null) sc.Selected += picked.Add;
                if (stopped != null) sc.Stopped += stopped;
                return sc;
            }

            { // automatic: start, move, first item has extra time
                var sc = Make(Auto());
                sc.Tick(5000);
                Assert(sc.Level == ScanLevel.Idle, "idle: time does not start scanning");
                sc.Press();
                Assert(sc.Level == ScanLevel.Rows && sc.Row == 0 && sc.CurrentRow.Count == 3, "the select switch starts at the first row");
                sc.Tick(1400); Assert(sc.Row == 0, "the first row stays 1.5 s (scan time + extra time)");
                sc.Tick(100);  Assert(sc.Row == 1, "…then the highlight moves");
                sc.Tick(1000); Assert(sc.Row == 2, "later rows stay 1 s");
                sc.Tick(1000); Assert(sc.Row == 0 && sc.Passes == 1 && sc.Level == ScanLevel.Rows, "after the last row it starts again");
            }
            { // automatic: pick a row, then a key
                var picked = new List<GridCell>();
                var sc = Make(Auto(), picked);
                sc.Press(); sc.Tick(1500); Assert(sc.Row == 1, "(row 1)");
                sc.Tick(1000); Assert(sc.Row == 2, "(row 2)");
                sc.Tick(400);                                                  // far enough from the first press
                sc.Press();
                Assert(sc.Level == ScanLevel.Keys && sc.CurrentKey.Props.Label == "e", "a press in a row goes to its first key");
                sc.Tick(1500); Assert(sc.CurrentKey.Props.Label == "f", "the highlight moves over the keys");
                sc.Press();
                Assert(picked.Count == 1 && picked[0].Props.Label == "f", "a press on a key selects it");
                Assert(sc.Level == ScanLevel.Rows && sc.Row == 0, "after a selection scanning goes on from the first row");
            }
            { // a row with one key selects it at once
                var picked = new List<GridCell>();
                var sc = Make(Auto(), picked);
                sc.Press(); sc.Tick(1500); sc.Tick(400); sc.Press();
                Assert(picked.Count == 1 && picked[0].Props.Label == "d", "a row of one key: the press selects the key");
            }
            { // rounds
                bool stopped = false;
                var sc = Make(Auto(), stopped: () => stopped = true);
                sc.Press();
                sc.Tick(3500); Assert(sc.Level == ScanLevel.Rows && !stopped, "one round without a press: it goes on");
                sc.Tick(3500); Assert(sc.Level == ScanLevel.Idle && stopped, "two rounds (the setting) without a press: it stops");
                sc.Press(); Assert(sc.Level == ScanLevel.Rows, "…and the select switch starts it again");
            }
            { // keys of a row: no press → back to the rows
                var sc = Make(Auto());
                sc.Press(); sc.Tick(400); sc.Press();
                Assert(sc.Level == ScanLevel.Keys, "(in the keys of row 0)");
                sc.Tick(1500 + 1000 + 1000); Assert(sc.Level == ScanLevel.Keys && sc.Passes == 1, "one round over the keys: it goes on");
                sc.Tick(1500 + 1000 + 1000); Assert(sc.Level == ScanLevel.Rows && sc.Row == 0, "two rounds over the keys without a press: back to the rows");
            }
            { // repeated presses
                var picked = new List<GridCell>();
                var sc = Make(Auto(), picked);
                sc.Press(); sc.Tick(100); sc.Press();
                Assert(sc.Level == ScanLevel.Rows, "a second press within 300 ms counts as the first one (a bounce)");
                sc.Tick(300); sc.Press();
                Assert(sc.Level == ScanLevel.Keys, "…a press after that counts");
                var zero = Auto(); zero.IgnoreRepeatMs = 0;
                var sz = Make(zero); sz.Press(); sz.Press();
                Assert(sz.Level == ScanLevel.Keys, "ignoring set to 0: every press counts");
            }
            { // two switches
                var picked = new List<GridCell>();
                var st = Auto(); st.TwoSwitch = true; st.IgnoreRepeatMs = 0;
                var sc = Make(st, picked);
                sc.Step();
                Assert(sc.Level == ScanLevel.Rows && sc.Row == 0, "two switches: the step switch starts at the first row");
                sc.Tick(60000); Assert(sc.Row == 0 && sc.Level == ScanLevel.Rows, "…and the highlight never moves or stops by itself");
                sc.Step(); sc.Step(); sc.Step(); Assert(sc.Row == 0, "stepping goes round the rows");
                sc.Step(); sc.Press();
                Assert(sc.Level == ScanLevel.Rows && picked.Count == 1 && picked[0].Props.Label == "d", "a press on a row of one key selects it");
                sc.Step(); sc.Step(); sc.Step(); sc.Press(); sc.Step(); sc.Press();
                Assert(picked.Count == 2 && picked[1].Props.Label == "b", "row, step to the key, select");
                var one = Make(Auto()); one.Step();
                Assert(one.Level == ScanLevel.Idle, "with one switch the step switch does nothing");
            }
            { // the keys change while scanning
                var sc = Make(Auto());
                sc.Press(); sc.Tick(1500); sc.Tick(1000);
                Assert(sc.Row == 2, "(row 2)");
                sc.SetRows(rows.Take(2).Select(r => r.ToList()).ToList());
                Assert(sc.Level == ScanLevel.Rows && sc.Row == 0, "fewer rows than the highlight is on: it starts again at the first");
                sc.SetRows(new List<List<GridCell>>());
                Assert(sc.Level == ScanLevel.Idle, "no keys left: scanning stops");
                sc.Press(); Assert(sc.Level == ScanLevel.Idle, "…and cannot start");
            }
            { Assert(Make(Auto(), null).CurrentKey == null && Make(Auto()).CurrentRow.Count == 0, "idle: nothing is highlighted"); }

            Section("Scanning — switch keys (ScanInput)");
            {
                var st = new ScanSettings { Enabled = true, SelectKey = 0x20, StepKey = 0x0D };
                int press = 0, step = 0, esc = 0;
                var inp = new ScanInput(() => st, () => press++, () => step++, () => esc++);
                Assert(!inp.Handle(0x41, true, false) && !inp.Handle(0x41, false, false), "another key passes");
                Assert(inp.Handle(0x20, true, false) && press == 1, "the select key is taken and counts");
                Assert(inp.Handle(0x20, true, false) && press == 1, "…its auto-repeat is taken and does not count again");
                Assert(inp.Handle(0x20, false, false), "…and so is its key-up");
                Assert(!inp.Handle(0x20, false, false), "(a key-up of a key that was not taken passes)");
                Assert(!inp.Handle(0x20, true, true) && press == 1, "a key sent by this program itself (injected) always passes");
                Assert(!inp.Handle(0x0D, true, false) && step == 0, "one switch: the step key is not ours");
                st.TwoSwitch = true;
                Assert(inp.Handle(0x0D, true, false) && step == 1 && inp.Handle(0x0D, false, false), "two switches: the step key is taken");
                Assert(inp.Handle(0x1B, true, false) && esc == 1 && inp.Handle(0x1B, false, false), "Esc is taken and stops scanning");
                bool running = false; int esc2 = 0;
                var inp2 = new ScanInput(() => st, () => { }, () => { }, () => esc2++, () => running);
                Assert(!inp2.Handle(0x1B, true, false) && !inp2.Handle(0x1B, false, false) && esc2 == 0, "no scan running: Esc is a normal key and passes");
                running = true;
                Assert(inp2.Handle(0x1B, true, false) && esc2 == 1 && inp2.Handle(0x1B, false, false), "a scan running: Esc is taken and pauses it");
                running = false;
                Assert(inp2.Handle(0x20, true, false), "…the select key is taken also when the scan is paused (it starts the scan again)");
                inp.Handle(0x20, true, false);
                inp.Uninstall();
                Assert(!inp.Handle(0x20, false, false), "uninstalling forgets the keys that were held");
                ScanInput.HookDisabledForTest = true;
                Assert(!inp.Install() && !inp.Installed, "the tests never set the system-wide hook");
            }

            Section("Scanning — sounds");
            {
                var wav = ScanSounds.Wave(28, 1500, 0.55, 0.55, 7);
                Assert(wav.Length == 44 + 22050 * 28 / 1000 * 2 && wav[0] == 'R' && wav[8] == 'W', "the select sound is a short WAV (28 ms), not a bell");
                Assert(wav.Skip(44).Any(b => b != 0), "…and it is not silent");
            }

            Section("Scanning — dialog");
            Lang.Load("en");
            using (var d = new ScanSettingsDialog(new ScanSettings()))
            {
                var r = d.Result;
                Assert(!r.Enabled && !r.TwoSwitch && r.ScanMs == 1000 && r.FirstExtraMs == 800 && r.Rounds == 3 && r.IgnoreRepeatMs == 300 && r.SoundOnSelect && !r.SoundOnStep,
                    "the dialog opens on the settings it was given");
                Assert(!d.TryCapture(Keys.A), "a key is not taken while no switch is being chosen");
            }
            var given = new ScanSettings { Enabled = true, TwoSwitch = true, SelectKey = 0x70, StepKey = 0x71, ScanMs = 2500, FirstExtraMs = 0, Rounds = 7, IgnoreRepeatMs = 600, SoundOnStep = true, SoundOnSelect = false };
            using (var d = new ScanSettingsDialog(given))
                Assert(d.Result.SameAs(given), "…every value comes back unchanged");
            using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta { SlowKeysMs = 400 }, owner: null))
            {
                var radio = Priv<TouchRadioButton>(f, "_optScanning");
                Assert(!radio.Checked && Priv<TouchRadioButton>(f, "_optSlowKeys").Checked, "access control: Slow keys is shown when the keyboard has it");
                radio.Checked = true;
                Assert(f.ApplyForTest() && f.ResultScan.Enabled && f.ResultMeta.SlowKeysMs == 0 && f.ResultMeta.DwellMs == 0,
                    "choosing Scanning turns scanning on and slow keys and dwell off (one aid at a time)");
            }
            using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta { DwellMs = 900 }, owner: null))
            {
                Priv<TouchRadioButton>(f, "_optDwell").Checked = true;
                Assert(f.ApplyForTest() && !f.ResultScan.Enabled && f.ResultMeta.DwellMs == 900, "choosing another aid leaves scanning off");
            }
            using (var d = new ScanSettingsDialog(new ScanSettings { SelectKey = 0x20, StepKey = 0x0D }))
            {
                d.BeginCapture(1);
                Assert(d.TryCapture(Keys.F9) && d.Result.SelectKey == 0x78, "the key pressed after choosing becomes the select key");
                Assert(!d.TryCapture(Keys.F9), "…then keys are not taken any more");
                d.BeginCapture(2);
                Assert(d.TryCapture(Keys.F9) && d.Result.StepKey == 0x0D, "the other switch's key cannot be chosen again");
                Assert(d.TryCapture(Keys.Escape) && d.Result.StepKey == 0x0D && !d.TryCapture(Keys.A), "Esc cancels the choice (and can never be a switch)");
                d.BeginCapture(2);
                Assert(d.TryCapture(Keys.F10) && d.Result.StepKey == 0x79, "the step key can be chosen");
            }
        }
    }
}

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
                s.SelectOnRelease = true; s.BackStep = false; s.BackSwitch = true; s.BackKey = 0x7A; s.BackSwitchStepsBack = true; s.BackLongPress = true; s.LongPressStepsBack = false; s.BackHoldMs = 2200;
                s.BackColor = System.Drawing.Color.FromArgb(1, 2, 3);                s.RowColor = System.Drawing.Color.FromArgb(10, 200, 30); s.KeyColor = System.Drawing.Color.FromArgb(250, 20, 200);
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

            ScanSettings Auto() => new ScanSettings { Enabled = true, ScanMs = 1000, FirstExtraMs = 500, Rounds = 2, IgnoreRepeatMs = 300, BackStep = false };
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

            Section("Scanning — one level back (back step, back switch, long press)");
            { // the back step: after the last key of the row
                var picked = new List<GridCell>();
                var st = Auto(); st.BackStep = true;
                var sc = Make(st, picked);
                sc.Press(); sc.Tick(400); sc.Press();
                Assert(sc.Level == ScanLevel.Keys && sc.CurrentKey.Props.Label == "a" && !sc.OnBack, "(in the keys of row 0)");
                sc.Tick(1500); sc.Tick(1000);
                Assert(sc.CurrentKey.Props.Label == "c" && !sc.OnBack, "(on the last key)");
                sc.Tick(1000);
                Assert(sc.OnBack && sc.CurrentKey == null && sc.CurrentRow.Count == 3, "after the last key the highlight is on the back step (the whole row, no key)");
                sc.Press();
                Assert(sc.Level == ScanLevel.Rows && sc.Row == 0 && picked.Count == 0, "selecting the back step goes back to the rows and selects nothing");
                sc.Tick(1500); sc.Tick(400); sc.Press();                    // row 1 is one key: with a back step it is entered like any row
                Assert(sc.Level == ScanLevel.Keys && picked.Count == 0, "a row of one key is entered, so it can be left again");
                sc.Tick(1500); Assert(sc.OnBack, "…its key, then the back step");
            }
            { // the back step in two-switch mode: stepping goes round the keys and the back step
                var st = Auto(); st.BackStep = true; st.TwoSwitch = true; st.IgnoreRepeatMs = 0;
                var sc = Make(st);
                sc.Step(); sc.Press();
                sc.Step(); sc.Step(); sc.Step();
                Assert(sc.OnBack, "two switches: stepping reaches the back step after the last key");
                sc.Step(); Assert(sc.Level == ScanLevel.Keys && sc.CurrentKey.Props.Label == "a", "…and goes round to the first key");
            }
            { // the back switch / long press: Back() from the keys, nothing at the rows
                var pickedNoBack = new List<GridCell>();
                var sc = Make(Auto(), pickedNoBack);
                sc.Back(); Assert(sc.Level == ScanLevel.Idle, "back while nothing scans: nothing happens");
                sc.Press(); sc.Back(); Assert(sc.Level == ScanLevel.Rows && sc.Row == 0, "back on the first row stays there");
                sc.Tick(1500); sc.Tick(1000); Assert(sc.Row == 2, "(row 2)");
                sc.Back(); Assert(sc.Level == ScanLevel.Rows && sc.Row == 0, "back at the rows goes to the first row (quicker than waiting for the round)");
                sc.Tick(1400); Assert(sc.Row == 0, "…and the first row gets its full time again");
                sc.Tick(100); sc.Tick(400); sc.Press();
                Assert(pickedNoBack.Count == 1 && pickedNoBack[0].Props.Label == "d", "(no way back is on: a row of one key is selected at once)");
                var st = Auto(); st.BackSwitch = true;
                var sd = Make(st);
                sd.Press(); sd.Tick(400); sd.Press();
                Assert(sd.Level == ScanLevel.Keys, "(in the keys of row 0)");
                sd.Tick(1500);
                sd.Back();
                Assert(sd.Level == ScanLevel.Rows && sd.Row == 0, "the back switch goes back to the rows, to the row that was entered");
                sd.Tick(1500 + 1000); sd.Press();
                Assert(sd.Level == ScanLevel.Keys && sd.Row == 2, "(a later row can be entered)");
                sd.Back();
                Assert(sd.Level == ScanLevel.Rows && sd.Row == 2, "back stays on the row it left, so the next row is one step away");
            }
            { // the long press shows when it is long enough: the whole row dashed, like the back step
                var st = Auto(); st.BackLongPress = true; st.BackHoldMs = 1500;
                var sc = Make(st);
                int changes = 0; sc.Changed += () => changes++;
                sc.Press(); sc.Tick(400); sc.Press();
                Assert(sc.Level == ScanLevel.Keys && sc.CurrentKey.Props.Label == "a", "(in the keys of row 0)");
                sc.SetHolding(true);
                sc.Tick(5000);
                Assert(sc.CurrentKey.Props.Label == "a" && sc.Level == ScanLevel.Keys, "while the select switch is held the highlight stays where it is");
                Assert(sc.BackPreview && !sc.OnBack, "…and after the hold time the back ring is shown (the press is long enough)");
                int before = changes; sc.Tick(100); Assert(changes == before, "…it is shown once");
                sc.SetHolding(false);
                Assert(!sc.BackPreview, "letting go takes the ring away");
                var shortHold = Make(st); shortHold.Press(); shortHold.Tick(400); shortHold.Press();
                shortHold.SetHolding(true); shortHold.Tick(1000);
                Assert(!shortHold.BackPreview, "a press shorter than the hold time shows no back ring");
                shortHold.SetHolding(false);
                var atRows = Make(st); atRows.Press(); atRows.Tick(1500); atRows.Tick(1000); atRows.SetHolding(true); atRows.Tick(3000);
                Assert(atRows.BackPreview && atRows.Row == 2 && atRows.Level == ScanLevel.Rows, "at the rows the long press shows the ring too (the row waits while the switch is held)");
                atRows.Back(); Assert(atRows.Row == 0 && !atRows.BackPreview, "…and letting go goes to the first row");
                var noLong = Make(Auto()); noLong.Press(); noLong.Tick(400); noLong.Press(); noLong.SetHolding(true); noLong.Tick(5000);
                Assert(!noLong.BackPreview, "without the long press option the ring never shows");
            }

            Section("Scanning — the long press: restarts scanning, or steps back (the other way round)");
            ScanSettings LongPress(bool stepsBack, bool backStep = false) => new ScanSettings
            { Enabled = true, ScanMs = 1000, FirstExtraMs = 500, Rounds = 2, IgnoreRepeatMs = 0, BackStep = backStep, BackLongPress = true, BackHoldMs = 1500, LongPressStepsBack = stepsBack };
            { // steps back at the rows: the scan goes upwards, and jumps from the first row to the last
                var sc = Make(LongPress(true));
                sc.Press(); sc.Tick(1500); sc.Tick(1000);
                Assert(sc.Row == 2 && !sc.Backwards, "(row 2, going forwards)");
                sc.SetHolding(true); sc.Tick(1500);
                Assert(sc.BackPreview, "(held long enough: the ring shows)");
                sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(sc.Row == 1 && sc.Backwards && !sc.BackPreview, "letting go steps back one row, and the scan now goes the other way round");
                sc.Tick(1000); Assert(sc.Row == 0, "…upwards");
                sc.Tick(1000); Assert(sc.Row == 2 && sc.Level == ScanLevel.Rows, "…from the first row it jumps to the last");
                sc.SetHolding(true); sc.Tick(1500); sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(!sc.Backwards, "another long press turns it round again");
            }
            { // steps back in a row: right to left, from the first key to the end of the row through the back step
                var sc = Make(LongPress(true, backStep: true));
                sc.Press(); sc.Tick(400); sc.Press();
                Assert(sc.Level == ScanLevel.Keys && sc.CurrentKey.Props.Label == "a", "(in the keys of row 0)");
                sc.SetHolding(true); sc.Tick(1500); sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(sc.OnBack && sc.Backwards, "from the first key a step back jumps to the end of the row: the back step comes in between");
                sc.Tick(1500); Assert(sc.CurrentKey?.Props.Label == "c", "…then the last key");
                sc.Tick(1000); Assert(sc.CurrentKey?.Props.Label == "b", "…right to left");
                sc.Press();
            }
            { // a selection starts forwards again
                var picked = new List<GridCell>();
                var sc = Make(LongPress(true), picked);
                sc.Press(); sc.Tick(400); sc.Press();
                sc.SetHolding(true); sc.Tick(1500); sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(sc.Backwards, "(going the other way round in the keys of row 0)");
                sc.Press();
                Assert(!sc.Backwards && sc.Level == ScanLevel.Rows && picked.Count == 1, "a key chosen: the scan starts forwards again");
            }
            { // "restarts scanning": a level back, the direction is not changed
                var sc = Make(LongPress(false));
                sc.Press(); sc.Tick(400); sc.Press();
                Assert(sc.Level == ScanLevel.Keys, "(in the keys of row 0)");
                sc.SetHolding(true); sc.Tick(1500); sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(sc.Level == ScanLevel.Rows && !sc.Backwards, "restarts scanning: back at the rows");
                sc.SetHolding(true); sc.Tick(1500);
                sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(sc.Level == ScanLevel.Rows && sc.Row == 0, "…and at the rows it goes to the first row");
            }
            { // the back switch: its own choice, independent of the long press
                var st = new ScanSettings { Enabled = true, ScanMs = 1000, FirstExtraMs = 500, Rounds = 2, IgnoreRepeatMs = 0, BackStep = false, BackSwitch = true, BackSwitchStepsBack = true };
                var sc = Make(st);
                sc.Press(); sc.Tick(1500); sc.Tick(1000);
                Assert(sc.Row == 2 && !sc.Backwards, "(row 2, going forwards)");
                sc.BackSwitchPressed();
                Assert(sc.Row == 1 && sc.Backwards, "the back switch \"steps back\": one row up, and the scan goes the other way round");
                sc.Tick(1000); Assert(sc.Row == 0, "…upwards");
                sc.BackSwitchPressed(); Assert(!sc.Backwards, "another press turns it round again");

                st.BackSwitchStepsBack = false;
                var sc2 = Make(st);
                sc2.Press(); sc2.Tick(400); sc2.Press();
                Assert(sc2.Level == ScanLevel.Keys, "(in the keys of row 0)");
                sc2.BackSwitchPressed();
                Assert(sc2.Level == ScanLevel.Rows && !sc2.Backwards, "the back switch \"restarts scanning\": back at the rows");

                // Different settings: the switch restarts, the long press steps back (and the other way round).
                var mix = new ScanSettings { Enabled = true, ScanMs = 1000, FirstExtraMs = 500, Rounds = 2, IgnoreRepeatMs = 0, BackStep = false,
                    BackSwitch = true, BackSwitchStepsBack = false, BackLongPress = true, LongPressStepsBack = true, BackHoldMs = 1500 };
                var m = Make(mix);
                m.Press(); m.Tick(400); m.Press();
                m.SetHolding(true); m.Tick(1500); m.ReleaseSelect(); m.SetHolding(false);
                Assert(m.Level == ScanLevel.Keys && m.Backwards, "the long press steps back…");
                m.BackSwitchPressed();
                Assert(m.Level == ScanLevel.Rows && !m.Backwards, "…while the back switch restarts scanning");
                mix.BackSwitchStepsBack = true; mix.LongPressStepsBack = false;
                var m2 = Make(mix);
                m2.Press(); m2.Tick(400); m2.Press();
                m2.BackSwitchPressed();
                Assert(m2.Level == ScanLevel.Keys && m2.Backwards, "turned round: the switch steps back…");
                m2.SetHolding(true); m2.Tick(1500); m2.ReleaseSelect(); m2.SetHolding(false);
                Assert(m2.Level == ScanLevel.Rows && !m2.Backwards, "…while the long press restarts scanning");
            }
            { // a short press still selects
                var sc = Make(LongPress(true));
                sc.Press(); sc.SetHolding(true); sc.Tick(500); sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(sc.Level == ScanLevel.Keys && !sc.Backwards, "a press shorter than the hold time chooses the row as usual");
            }
            { // two switches: the long press is one step back, the direction stays
                var st = LongPress(true); st.TwoSwitch = true;
                var sc = Make(st);
                sc.Step(); sc.Step();
                Assert(sc.Row == 1, "(row 1)");
                sc.SetHolding(true); sc.Tick(1500); sc.ReleaseSelect(); sc.SetHolding(false);
                Assert(sc.Row == 0 && !sc.Backwards, "two switches: a long press is one step back");
            }

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
                // Select on release: the key-down is taken and waits; the key-up makes the press. Repeats and the step key are not affected.
                st.TwoSwitch = false; st.SelectOnRelease = true; press = 0;
                var inp3 = new ScanInput(() => st, () => press++, () => step++, () => esc++);
                Assert(inp3.Handle(0x20, true, false) && press == 0, "select on release: the key-down is taken and does nothing yet");
                Assert(inp3.Handle(0x20, true, false) && press == 0, "…its auto-repeat does nothing either (a held switch is not a press)");
                Assert(inp3.Handle(0x20, false, false) && press == 1, "…the key-up is the press");
                Assert(!inp3.Handle(0x20, false, false) && press == 1, "…and a key-up of a key that was not taken does nothing");
                Assert(!inp3.Handle(0x20, true, true) && !inp3.Handle(0x20, false, true) && press == 1, "keys sent by this program still pass");
                st.TwoSwitch = true; step = 0;
                Assert(inp3.Handle(0x0D, true, false) && step == 1, "the step key still acts on the press");
                st.SelectOnRelease = false; st.TwoSwitch = false;
                // The back switch (its own key) and the long press (the select switch held).
                int backs = 0; press = 0;
                var sb = new ScanSettings { Enabled = true, SelectKey = 0x20, StepKey = 0x0D, BackKey = 0x79, BackSwitch = false };
                var inp4 = new ScanInput(() => sb, () => press++, () => { }, () => { }, null, () => backs++);
                Assert(!inp4.Handle(0x79, true, false) && backs == 0, "the back key is not ours while the back switch is off");
                sb.BackSwitch = true;
                Assert(inp4.Handle(0x79, true, false) && backs == 1 && inp4.Handle(0x79, true, false) && backs == 1 && inp4.Handle(0x79, false, false), "the back switch goes back once per press (repeats do not count)");
                sb.BackLongPress = true; sb.BackHoldMs = 1500;
                Assert(inp4.Handle(0x20, true, false) && press == 0 && backs == 1, "long press on: the select key-down does nothing yet");
                Assert(inp4.Handle(0x20, false, false) && press == 1 && backs == 1, "…letting go is the press (the scanner knows whether it was long: it showed the ring)");
                sb.BackLongPress = false; sb.SelectOnRelease = false; press = 0;
                Assert(inp4.Handle(0x20, true, false) && press == 1 && inp4.Handle(0x20, false, false), "long press off: the press acts at once again");
                // The scanner is told when the select switch is held (it waits) and let go.
                var holds = new List<bool>();
                var inp5 = new ScanInput(() => sb, () => { }, () => { }, () => { }, null, null, holds.Add);
                sb.BackLongPress = true;
                inp5.Handle(0x20, true, false); inp5.Handle(0x20, true, false); inp5.Handle(0x20, false, false);
                Assert(holds.Count == 2 && holds[0] && !holds[1], "held with a long press on: down, then up (repeats do not count)");
                sb.BackLongPress = false; sb.SelectOnRelease = false; holds.Clear();
                inp5.Handle(0x20, true, false); inp5.Handle(0x20, false, false);
                Assert(holds.Count == 1 && !holds[0], "acting on the press: the switch is never held back (only the let-go is reported)");
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

            Section("Scanning — gamepad as a switch");
            {
                Lang.Load("en");
                int A = Gamepad.XInputBase + 0x1000, B = Gamepad.XInputBase + 0x2000, LT = Gamepad.TriggerBase + 1, J3 = Gamepad.JoyBase + 3;
                Assert(Gamepad.IsButton(A) && Gamepad.IsButton(J3) && !Gamepad.IsButton(0x20) && !Gamepad.IsButton(0x77) && !Gamepad.IsButton(0), "a gamepad code is never a key code");
                Assert(Gamepad.Name(A) == "Pad A" && Gamepad.Name(LT) == "Pad LT" && Gamepad.Name(J3) == "Pad button 3" && Gamepad.Name(0) == "—" && ScanSettings.KeyName(B) == "Pad B",
                    "the names of the buttons");

                var events = new List<(int, bool)>();
                using (var poller = new GamepadPoller((c, d) => events.Add((c, d)), () => new HashSet<int>()))
                {
                    poller.Update(new HashSet<int> { A });
                    poller.Update(new HashSet<int> { A });
                    Assert(events.Count == 1 && events[0] == (A, true), "a button going down is reported once");
                    poller.Update(new HashSet<int> { A, B });
                    Assert(events.Count == 2 && events[1] == (B, true), "…another one too");
                    poller.Update(new HashSet<int> { B });
                    Assert(events.Count == 3 && events[2] == (A, false), "a button let go is reported");
                    poller.Update(new HashSet<int>());
                    Assert(events.Count == 4 && events[3] == (B, false), "…and the last one");
                }
                Assert(new GamepadReader().Read() != null, "asking the system for controllers does not fail (none may be connected)");

                // A switch is a key or a button: the same decisions. Here the switches are buttons.
                var st = new ScanSettings { Enabled = true, SelectKey = A, StepKey = B, BackKey = J3 };
                int press = 0, step = 0, back = 0;
                var inp = new ScanInput(() => st, () => press++, () => step++, () => { }, null, () => back++);
                Assert(inp.Handle(A, true, false) && press == 1 && inp.Handle(A, true, false) && press == 1 && inp.Handle(A, false, false), "the select button acts like a select key (a held button is one press)");
                Assert(!inp.Handle(0x77, true, false), "a key that is not a switch passes (the keyboard keys are free)");
                Assert(!inp.Handle(B, true, false) && step == 0, "one switch: the step button is not used");
                st.TwoSwitch = true;
                Assert(inp.Handle(B, true, false) && step == 1 && inp.Handle(B, false, false), "two switches: the step button steps");
                Assert(!inp.Handle(J3, true, false) && back == 0, "the back button does nothing while back with switch is off");
                st.BackSwitch = true;
                Assert(inp.Handle(J3, true, false) && back == 1 && inp.Handle(J3, false, false), "back with switch on: the back button goes back");
                Assert(!inp.Handle(Gamepad.XInputBase + 0x4000, true, false), "any other button passes");
                st.SelectOnRelease = true; press = 0;
                Assert(inp.Handle(A, true, false) && press == 0 && inp.Handle(A, false, false) && press == 1, "select on release works for the button too");

                // Settings: a switch may be a button; the three differ.
                var c = new ScanSettings { SelectKey = A, StepKey = A, BackKey = J3 };
                c.Clamp();
                Assert(c.SelectKey == A && c.StepKey != A && c.BackKey == J3, "a button can be a switch; two switches never share one");
            }
            {
                string saved2 = UserData.DirectoryOverride;
                string dir2 = Path.Combine(Path.GetTempPath(), "osk_pad_" + Guid.NewGuid().ToString("N"));
                try
                {
                    UserData.DirectoryOverride = dir2; UserSettings.Reset();
                    var s = new ScanSettings { SelectKey = Gamepad.XInputBase + 0x1000, StepKey = Gamepad.XInputBase + 0x2000, BackKey = Gamepad.JoyBase + 2 };
                    UserSettings.SetScan(s); UserSettings.Reset();
                    Assert(UserSettings.Scan.SameAs(s) && UserSettings.Scan.BackKey == Gamepad.JoyBase + 2 && UserSettings.Scan.SelectKey == Gamepad.XInputBase + 0x1000,
                        "the gamepad buttons come back after a restart");
                }
                finally { UserData.DirectoryOverride = saved2; UserSettings.Reset(); try { Directory.Delete(dir2, true); } catch { } }
            }
            using (var d = new ScanSettingsDialog(new ScanSettings()))
            {
                int A = Gamepad.XInputBase + 0x1000, B = Gamepad.XInputBase + 0x2000;
                Assert(!d.TryCapturePad(A), "a button is not taken while no switch is being chosen");
                d.BeginCapture(1);
                Assert(!d.TryCapturePad(0x20), "a key code is not a button");
                Assert(d.TryCapturePad(A) && d.Result.SelectKey == A, "one choice for a key or a button: a button pressed after choosing becomes the select switch");
                d.BeginCapture(2);
                Assert(d.TryCapturePad(A) && d.Result.StepKey != A, "the same button cannot be two switches");
                Assert(d.TryCapturePad(B) && d.Result.StepKey == B, "…another one can be the step switch");
                d.BeginCapture(3); d.TryCapturePad(Gamepad.JoyBase + 1);
                Assert(d.Result.BackKey == Gamepad.JoyBase + 1, "…and the back switch");
                d.BeginCapture(1);
                Assert(d.TryCapture(Keys.F6) && d.Result.SelectKey == 0x75, "…the same choice also takes a key of the keyboard");
                d.BeginCapture(2);
                Assert(d.TryCapture(Keys.Escape) && d.Result.StepKey == B && !d.TryCapturePad(A), "Esc cancels the choice");
            }

            Section("Scanning — dialog");
            Lang.Load("en");
            using (var d = new ScanSettingsDialog(new ScanSettings()))
            {
                var r = d.Result;
                Assert(!r.Enabled && !r.TwoSwitch && r.ScanMs == 1000 && r.FirstExtraMs == 800 && r.Rounds == 3 && r.IgnoreRepeatMs == 300 && r.SoundOnSelect && !r.SoundOnStep,
                    "the dialog opens on the settings it was given");
                Assert(!d.TryCapture(Keys.A), "a key is not taken while no switch is being chosen");
                Assert(d.HostSectionCount == 4, "the window has four tabs (switches, timing, back, feedback)");
                Assert(d.OnReleaseEnabled, "select on release can be chosen");
                Priv<TouchChoiceButton>(d, "_cmbLong").SelectedIndex = 1;
                Assert(!d.OnReleaseEnabled, "…but not while a long press is in use (the select switch then acts on release by itself)");
                Assert(Priv<TouchCheckBox>(d, "_chkOnRelease").Checked, "…it then shows as on (greyed): that is what happens");
                Assert(!d.Result.SelectOnRelease, "…while the person's own choice (off) is kept");
                Assert(Priv<Label>(d, "_hint").Text.StartsWith(Lang.T("tip: scan on release forced")), "…and the reason is written under it (a disabled control shows no tip)");
                var lp = Priv<TouchChoiceButton>(d, "_cmbLong");
                Assert(d.Result.BackLongPress && !d.Result.LongPressStepsBack, "the long press \"restarts scanning\" (the first of the two choices)");
                lp.SelectedIndex = 2;
                Assert(d.Result.BackLongPress && d.Result.LongPressStepsBack && !d.OnReleaseEnabled, "…or \"steps back\" (one of the two, not both)");
                lp.SelectedIndex = 0;
                Assert(!d.Result.BackLongPress && d.OnReleaseEnabled && !Priv<TouchCheckBox>(d, "_chkOnRelease").Checked && Priv<Label>(d, "_hint").Text == Lang.T("scan: hint"), "…and it is off again, and free, when the long press is off (the reason is gone)");
                Priv<TouchCheckBox>(d, "_chkOnRelease").Checked = true;
                lp.SelectedIndex = 1; lp.SelectedIndex = 0;
                Assert(Priv<TouchCheckBox>(d, "_chkOnRelease").Checked && d.Result.SelectOnRelease, "a choice made (on) is still there after the long press was switched on and off");
                var bs = Priv<TouchChoiceButton>(d, "_cmbBackSwitch");
                Assert(!d.Result.BackSwitch && !Priv<FluentButton>(d, "_btnBack").Enabled, "the back switch is off: its key cannot be chosen");
                bs.SelectedIndex = 1;
                Assert(d.Result.BackSwitch && !d.Result.BackSwitchStepsBack && Priv<FluentButton>(d, "_btnBack").Enabled, "the back switch \"restarts scanning\"");
                bs.SelectedIndex = 2;
                Assert(d.Result.BackSwitch && d.Result.BackSwitchStepsBack && !d.Result.BackLongPress, "…or \"steps back\", whatever the long press is set to");
            }
            var given = new ScanSettings { Enabled = true, TwoSwitch = true, SelectKey = 0x70, StepKey = 0x71, ScanMs = 2500, FirstExtraMs = 0, Rounds = 7, IgnoreRepeatMs = 600, SoundOnStep = true, SoundOnSelect = false, SelectOnRelease = true,
                BackStep = false, BackSwitch = true, BackKey = 0x7A, BackSwitchStepsBack = true, BackLongPress = true, LongPressStepsBack = false, BackHoldMs = 2000 };
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
                d.BeginCapture(3);
                Assert(d.TryCapture(Keys.F9) && d.Result.BackKey == 0x79, "the back key cannot be a key that is a switch already");
                Assert(d.TryCapture(Keys.F11) && d.Result.BackKey == 0x7A, "…another key can be chosen as the back key");
                d.BeginCapture(2);
                Assert(d.TryCapture(Keys.Escape) && d.Result.StepKey == 0x0D && !d.TryCapture(Keys.A), "Esc cancels the choice (and can never be a switch)");
                d.BeginCapture(2);
                Assert(d.TryCapture(Keys.F10) && d.Result.StepKey == 0x79, "the step key can be chosen");
            }
        }
    }
}

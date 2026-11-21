// ProgramResetTests.cs — Priority 6: the reset of the program to its first-start state. What is deleted and what is not, the question
// asked first, and the way the program starts itself again.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_ProgramReset()
        {
            Section("Reset — what is removed, what is kept");

            string dir = Path.Combine(Path.GetTempPath(), "osk_reset_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string[] settingsFiles = { "settings.xml", "settings.xml.bak", "settings.xml.tmp", "settings.xml.recovery", "settings.xml.recovery.bak", "user.xml", "user.xml.bak", "user.xml.tmp" };
                string[] learnedFiles  = { "worddb_NL.learned.wfq", "worddb_NL.learned.wfq.bak", "worddb_FR.learned.wfq", "worddb_FR.learned.wfq.tmp" };
                string[] keep          = { "OnScreenKeyboard_error.log", "OnScreenKeyboard_error.log.old", "mine.kbl", "notes.txt", "mysettings.xml", "worddb_NL.wfq" };
                foreach (string n in settingsFiles.Concat(learnedFiles).Concat(keep)) File.WriteAllText(Path.Combine(dir, n), "x");
                string sub = Path.Combine(dir, "sub");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "settings.xml"), "x");

                var names = ProgramReset.FilesToDelete(dir, learnedWords: false).Select(Path.GetFileName).ToList();
                Assert(settingsFiles.All(names.Contains) && names.Count == settingsFiles.Length,
                    "reset: the settings and the person's settings go, with their backup, temporary and recovery copies" + (names.Count == settingsFiles.Length ? "" : " — " + string.Join(", ", names)));
                Assert(!names.Any(n => n.Contains("learned")), "reset: the learned words stay when the person did not ask for them to go");

                names = ProgramReset.FilesToDelete(dir, learnedWords: true).Select(Path.GetFileName).ToList();
                Assert(learnedFiles.All(names.Contains) && names.Count == settingsFiles.Length + learnedFiles.Length,
                    "reset: the learned words of every language go too when asked (with their copies)");
                Assert(!keep.Any(names.Contains), "reset: never the error log, a keyboard (.kbl), the word list itself or any other file");
                Assert(ProgramReset.FilesToDelete(dir, true).All(f => Path.GetDirectoryName(f) == dir), "reset: nothing in a subfolder");
                Assert(ProgramReset.FilesToDelete(Path.Combine(dir, "nope"), true).Count == 0 && ProgramReset.FilesToDelete(null, true).Count == 0,
                    "reset: a folder that is not there is no problem");

                // A file that is locked is reported; the others are removed.
                string locked = Path.Combine(dir, "user.xml");
                List<string> failed;
                using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                    failed = ProgramReset.DeleteData(dir, learnedWords: false);
                Assert(failed.Count == 1 && failed[0] == locked, "reset: a file that cannot be deleted is reported");
                Assert(File.Exists(locked) && !File.Exists(Path.Combine(dir, "settings.xml")), "reset: the others were deleted all the same");
                failed = ProgramReset.DeleteData(dir, learnedWords: false);
                Assert(failed.Count == 0 && !File.Exists(locked), "reset: once nothing holds the file, it goes");
                Assert(File.Exists(Path.Combine(dir, "worddb_NL.learned.wfq")) && File.Exists(Path.Combine(dir, "mine.kbl")) && File.Exists(Path.Combine(dir, "OnScreenKeyboard_error.log"))
                       && File.Exists(Path.Combine(sub, "settings.xml")),
                    "reset: the learned words, the keyboard file, the error log and the subfolder are still there");
                ProgramReset.DeleteData(dir, learnedWords: true);
                Assert(Directory.GetFiles(dir, "*.learned.wfq*").Length == 0 && File.Exists(Path.Combine(dir, "worddb_NL.wfq")), "reset: the learned words go on request, the word list itself never");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }

            Section("Reset — does the keyboard in use survive?");
            string settings = @"C:\Users\x\AppData\Roaming\EditableOSK\settings.xml";
            Assert(!ProgramReset.IsInOwnFile(null, settings, p => true), "reset: no file at all: the keyboard is only in the settings");
            Assert(!ProgramReset.IsInOwnFile(settings, settings, p => true), "reset: the settings file itself is not a file of the person's own");
            Assert(!ProgramReset.IsInOwnFile(settings.ToUpperInvariant(), settings, p => true), "reset: the same file in another spelling is the same file");
            Assert(!ProgramReset.IsInOwnFile(@"C:\Program Files\EditableOSK\azerty.kbl", settings, p => false), "reset: a layout installed with the program (not writable) is not");
            Assert(ProgramReset.IsInOwnFile(@"C:\Users\x\Documents\mine.kbl", settings, p => true), "reset: a writable file elsewhere is");
            bool threw = false;
            try { ProgramReset.IsInOwnFile("a<b|c", settings, p => throw new IOException("no")); } catch { threw = true; }
            Assert(!threw && !ProgramReset.IsInOwnFile("a<b|c", settings, p => throw new IOException("no")), "reset: a path that cannot be checked does not throw and counts as not in a file of its own");

            Section("Reset — starting again");
            Assert(ProgramReset.ParseAfterReset(new[] { "--after-reset=1234" }) == 1234, "reset: the process to wait for is read from the argument");
            Assert(ProgramReset.ParseAfterReset(new[] { "a.kbl", "--after-reset=77" }) == 77, "reset: also after another argument");
            Assert(ProgramReset.ParseAfterReset(new string[0]) == 0 && ProgramReset.ParseAfterReset(new[] { "--after-reset=abc" }) == 0
                   && ProgramReset.ParseAfterReset(new[] { "--after-reset=-5" }) == 0 && ProgramReset.ParseAfterReset(new[] { "--after-reset=" }) == 0,
                "reset: no argument, no number or a number that is no process: 0 (nothing to wait for)");
            Assert(LayoutLaunch.FindLayoutArgument(new[] { "--after-reset=1234" }, Path.GetTempPath()) == null,
                "reset: the argument is not taken for a layout file to open");
            ProgramReset.WaitForEnd(int.MaxValue - 1, 10);      // a process that does not exist: returns at once
            Assert(true, "reset: waiting for a process that is not there does not block or throw");

            Section("Reset — the question (ResetDialog)");
            {
                int saves = 0;
                using var d = new ResetDialog(keyboardOnlyInSettings: true, saveAs: () => { saves++; return true; });
                DevGallery.Show(d);
                var chk = Priv<TouchCheckBox>(d, "_chkLearned");
                Assert(!chk.Checked && !d.ForgetLearnedWords, "reset dialog: the learned words are kept unless the person ticks the box");
                chk.Checked = true;
                Assert(d.ForgetLearnedWords, "reset dialog: ticking the box says the words go too");
                var warning = Priv<Label>(d, "_lblUnsaved");
                var saveAs  = Priv<FluentButton>(d, "_btnSaveAs");
                Assert(warning.Visible && saveAs.Visible, "reset dialog: a keyboard that is only in the settings gets a warning and a Save As… button");
                ClickButton(saveAs);
                Assert(saves == 1 && !warning.Visible && !saveAs.Visible, "reset dialog: after Save As… the warning and the button are gone");
                Assert(d.AcceptButton != null && ((FluentButton)d.AcceptButton).Text == Lang.T("Cancel"), "reset dialog: Enter means Cancel (the safe answer)");
                ClickButton(Priv<FluentButton>(d, "_btnReset"));
                Assert(d.DialogResult == DialogResult.OK, "reset dialog: the Reset button closes with OK");
            }
            {
                int saves = 0;
                using var d = new ResetDialog(true, () => { saves++; return false; });
                DevGallery.Show(d);
                ClickButton(Priv<FluentButton>(d, "_btnSaveAs"));
                Assert(Priv<Label>(d, "_lblUnsaved").Visible, "reset dialog: a Save As… that was cancelled keeps the warning");
            }
            {
                using var d = new ResetDialog(false, () => true);
                DevGallery.Show(d);
                Assert(Priv<FluentButton>(d, "_btnSaveAs") == null && Priv<Label>(d, "_lblUnsaved") == null, "reset dialog: a keyboard in a file of its own: no warning, no Save As…");
                ClickButton(Priv<FluentButton>(d, "_btnCancel"));
                Assert(d.DialogResult == DialogResult.Cancel && !d.ForgetLearnedWords, "reset dialog: Cancel closes with Cancel");
            }

            Section("Reset — the button in Edit Keyboard › Info");
            using (var f = KeyboardEditor())
            {
                DevGallery.Show(f);
                Assert(!f.ResetRequested, "Info: a reset is not requested by default");
                ClickButton(Priv<FluentButton>(f, "_btnReset"));
                Assert(f.ResetRequested && f.DialogResult == DialogResult.Cancel, "Info: the Reset button closes the window without applying anything and asks the caller to reset");
            }
        }
    }
}

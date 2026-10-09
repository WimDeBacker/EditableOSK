// UserDataTests.cs — the program must not write into its own (installed, read-only) folder (todo Priority 0): settings, learned words and
// the error log live in the user's folder.

using System;
using System.IO;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_UserData()
        {
            Section("User data — settings, learned words and error log go to the user's folder");

            string saved = UserData.DirectoryOverride;
            string root  = Path.Combine(Path.GetTempPath(), "osk_ud_" + Guid.NewGuid().ToString("N"));
            string user  = Path.Combine(root, "user");            // does not exist yet: the first write must create it
            string app   = Path.Combine(root, "app");
            Directory.CreateDirectory(app);
            string appBase = Path.Combine(UserData.AppFolder, "osk_ud_" + Guid.NewGuid().ToString("N") + ".wfq");
            try
            {
                UserData.DirectoryOverride = user;

                // Paths
                string settings = SettingsManager.DefaultPath;
                Assert(settings == Path.Combine(user, "settings.xml"), "settings.xml is in the user folder");
                Assert(Directory.Exists(user), "asking for a path creates the user folder");

                Assert(UserData.IsInFolder(Path.Combine(app, "a.wfq"), app), "a file in the folder is in the folder");
                Assert(UserData.IsInFolder(Path.Combine(app, "x", "..", "a.wfq"), app), "the path is normalised first");
                Assert(!UserData.IsInFolder(Path.Combine(app, "sub", "a.wfq"), app), "a file in a subfolder is not");
                Assert(!UserData.IsInFolder(Path.Combine(root, "a.wfq"), app), "a file elsewhere is not");
                Assert(!UserData.IsInFolder(null, app) && !UserData.IsInFolder("a<b|c", app), "nothing and an invalid path do not throw");

                Assert(UserData.CanWrite(Path.Combine(app, "new.kbl")), "a writable folder: the file can be written");
                Assert(!Directory.EnumerateFileSystemEntries(app).GetEnumerator().MoveNext(), "the write test leaves nothing behind");
                Assert(!UserData.CanWrite(Path.Combine(root, "missing folder", "x.kbl")), "a folder that does not exist: cannot write");

                // Learned words of a database in the program folder go to the user folder; of one elsewhere they stay next to it.
                Assert(WordDatabase.GetOverlayPath(Path.Combine(UserData.AppFolder, "worddb_NL.wfq")) == Path.Combine(user, "worddb_NL.learned.wfq"),
                       "learned words of an installed database are kept in the user folder");
                string other = Path.Combine(app, "mijn.wfq");
                Assert(WordDatabase.GetOverlayPath(other) == Path.Combine(app, "mijn.learned.wfq"),
                       "learned words of a database elsewhere stay next to it");

                // A real save: the user folder is created, nothing is written into the program folder.
                Directory.Delete(user, true);
                File.WriteAllText(appBase, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<WordDatabase version=\"1\" language=\"nl\">\r\n" +
                                           "  <Word value=\"de\" frequency=\"100\" />\r\n</WordDatabase>");
                WordDatabase.LearningEnabled = true;
                WordDatabase.Load(appBase);
                WordDatabase.RecordWord(null, "de");
                WordDatabase.SaveNow();
                string overlay = Path.Combine(user, Path.GetFileNameWithoutExtension(appBase) + ".learned.wfq");
                Assert(File.Exists(overlay), "saving the learned words creates the user folder and the file in it");
                Assert(!File.Exists(Path.Combine(UserData.AppFolder, Path.GetFileNameWithoutExtension(appBase) + ".learned.wfq")),
                       "nothing is written next to the installed database");
                WordDatabase.Load(appBase);
                Assert(WordDatabase.GetPredictions("", "d", false, 1).Count == 1, "the saved words load again");

                // Error log
                ErrorLog.Reset();
                Program.LogError("user data test", new InvalidOperationException("x"));
                Assert(File.Exists(Path.Combine(user, "OnScreenKeyboard_error.log")), "the error log is written in the user folder");

                // One-time move from earlier versions
                string old1 = Path.Combine(app, "settings.xml"), old2 = Path.Combine(app, "worddb_NL.learned.wfq");
                File.WriteAllText(old1, "old settings"); File.WriteAllText(old2, "old words");
                Directory.Delete(user, true);
                var copied = UserData.MigrateFrom(app);
                Assert(copied.Count == 2 && File.ReadAllText(Path.Combine(user, "settings.xml")) == "old settings"
                       && File.ReadAllText(Path.Combine(user, "worddb_NL.learned.wfq")) == "old words", "settings and learned words of an earlier version are copied");
                Assert(File.Exists(old1) && File.Exists(old2), "the old files are left in place");
                File.WriteAllText(old1, "newer old");
                Assert(UserData.MigrateFrom(app).Count == 0 && File.ReadAllText(Path.Combine(user, "settings.xml")) == "old settings",
                       "a file that already exists in the user folder is never overwritten");
                Assert(UserData.MigrateFrom(Path.Combine(root, "nope")).Count == 0, "a folder that does not exist is no problem");
            }
            finally
            {
                UserData.DirectoryOverride = saved;
                try { File.Delete(appBase); } catch { }
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}

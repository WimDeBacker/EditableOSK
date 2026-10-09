// UserData.cs — where the program keeps what it writes while it runs.
//
// The installer puts the program in "C:\Program Files\EditableOSK", where a standard user cannot write. Everything the program
// writes therefore lives in the user's own folder, %AppData%\EditableOSK:
//   settings.xml                the automatic save (the layout the next start opens)
//   worddb_XX.learned.wfq       the words learned on a word database that is installed with the program
//   OnScreenKeyboard_error.log  the error log
// Layouts the user saves go where the user chooses (the Documents folder by default). A word database somewhere else than in the
// program's folder keeps its learned words next to itself, as before.

using System;
using System.IO;

namespace OnScreenKeyboard
{
    internal static class UserData
    {
        /// <summary>For the tests: use this folder instead of %AppData%\EditableOSK, so a test run never touches real user data.</summary>
        internal static string DirectoryOverride;

        /// <summary>The folder for the settings, learned words and error log of this user.</summary>
        internal static string Directory =>
            DirectoryOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EditableOSK");

        /// <summary>The folder the program is installed in.</summary>
        internal static string AppFolder => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>The folder a layout is offered for saving in when it has no better place: the user's Documents.</summary>
        internal static string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>The path of <paramref name="fileName"/> in the user folder; the folder is created (best effort) so the caller can write at once.</summary>
        internal static string FileFor(string fileName)
        {
            try { System.IO.Directory.CreateDirectory(Directory); } catch { }
            return Path.Combine(Directory, fileName);
        }

        /// <summary>True when <paramref name="path"/> is a file directly in <paramref name="folder"/> (default: the program's folder).</summary>
        internal static bool IsInFolder(string path, string folder = null)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                string app = Path.GetFullPath(folder ?? AppFolder);
                return string.Equals(dir?.TrimEnd('\\', '/'), app.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// True when a file can be created or replaced at <paramref name="path"/>: a save writes a temporary file next to it and swaps it in,
        /// so what counts is the folder. The probe file is removed at once.
        /// </summary>
        internal static bool CanWrite(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".osk_write_" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// One-time move from earlier versions, which wrote next to the exe: copies <c>settings.xml</c> and the <c>*.learned.wfq</c> files of
        /// <paramref name="appFolder"/> into the user folder, but never over a file that is already there. The old files stay (they may not be
        /// removable without administrator rights). Returns the names that were copied.
        /// </summary>
        internal static System.Collections.Generic.List<string> MigrateFrom(string appFolder)
        {
            var copied = new System.Collections.Generic.List<string>();
            try
            {
                if (string.IsNullOrEmpty(appFolder) || !System.IO.Directory.Exists(appFolder)) return copied;
                if (string.Equals(Path.GetFullPath(appFolder).TrimEnd('\\'), Path.GetFullPath(Directory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return copied;
                var names = new System.Collections.Generic.List<string> { "settings.xml" };
                foreach (string f in System.IO.Directory.GetFiles(appFolder, "*.learned.wfq")) names.Add(Path.GetFileName(f));
                foreach (string name in names)
                {
                    string from = Path.Combine(appFolder, name);
                    if (!File.Exists(from)) continue;
                    string to = FileFor(name);
                    if (File.Exists(to)) continue;
                    try { File.Copy(from, to); copied.Add(name); } catch { }
                }
            }
            catch { }
            return copied;
        }
    }
}

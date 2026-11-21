// ProgramReset.cs — Priority 6: put the program back to its first-start state.
//
// What a reset removes is decided here, apart from any window, so it can be tested: the files the program writes in the user's data
// folder (see UserData): the settings and the keyboard that was in use (settings.xml), the person's own settings (user.xml), their backup and
// recovery copies and — only when asked — the words word prediction learned (worddb_XX.learned.wfq). The error log stays (it is for finding
// faults), and so does everything the person saved themselves: keyboard (.kbl) files are never touched, wherever they are.
//
// The program then starts itself again: the new process is told (--after-reset=<pid>) to wait until this one has ended, so the two never
// run side by side and the first-start questions come up on a clean folder.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OnScreenKeyboard
{
    internal static class ProgramReset
    {
        internal const string AfterResetArgument = "--after-reset=";

        /// <summary>
        /// The files in <paramref name="dir"/> (not in its subfolders) a reset removes: settings.xml and user.xml with their .bak, .tmp and
        /// .recovery copies, and, when <paramref name="learnedWords"/>, every learned-words file (<c>*.learned.wfq</c> with its copies).
        /// </summary>
        internal static List<string> FilesToDelete(string dir, bool learnedWords)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
            foreach (string file in Directory.GetFiles(dir))
            {
                string name = Path.GetFileName(file);
                if (IsCopyOf(name, "settings.xml") || IsCopyOf(name, "user.xml") || (learnedWords && IsLearnedWords(name))) list.Add(file);
            }
            return list;
        }

        // settings.xml, settings.xml.bak, settings.xml.tmp, settings.xml.recovery, settings.xml.recovery.bak: the name, or the name and a dot.
        private static bool IsCopyOf(string name, string baseName) =>
            string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase) || name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase);

        private static bool IsLearnedWords(string name) => name.Contains(".learned.wfq", StringComparison.OrdinalIgnoreCase);

        /// <summary>Deletes <see cref="FilesToDelete"/>; returns the files that could not be deleted (empty: all gone).</summary>
        internal static List<string> DeleteData(string dir, bool learnedWords)
        {
            var failed = new List<string>();
            foreach (string file in FilesToDelete(dir, learnedWords))
            {
                try { File.Delete(file); }
                catch (Exception ex) { Program.LogError("reset: deleting " + Path.GetFileName(file), ex); failed.Add(file); }
            }
            return failed;
        }

        /// <summary>
        /// True when the keyboard in use is stored in a file of the person's own: a file that is not the program's settings file and that can
        /// be written (a layout installed with the program cannot; its changes go to the settings file). A reset loses a keyboard that is not.
        /// </summary>
        internal static bool IsInOwnFile(string currentFile, string settingsFile, Func<string, bool> mayWrite)
        {
            if (string.IsNullOrEmpty(currentFile)) return false;
            try
            {
                if (string.Equals(Path.GetFullPath(currentFile), Path.GetFullPath(settingsFile), StringComparison.OrdinalIgnoreCase)) return false;
                return mayWrite(currentFile);
            }
            catch { return false; }
        }

        /// <summary>Starts this program again, to wait for the current process to end first. False when it could not be started.</summary>
        internal static bool Relaunch()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath)
                {
                    Arguments = AfterResetArgument + Environment.ProcessId, UseShellExecute = false,
                });
                return true;
            }
            catch (Exception ex) { Program.LogError("reset: starting the program again", ex); return false; }
        }

        /// <summary>The process id after <c>--after-reset=</c>, or 0 when the argument is not there (or not a number).</summary>
        internal static int ParseAfterReset(string[] args)
        {
            foreach (string a in args)
                if (a != null && a.StartsWith(AfterResetArgument, StringComparison.Ordinal)
                    && int.TryParse(a.Substring(AfterResetArgument.Length), out int pid) && pid > 0) return pid;
            return 0;
        }

        /// <summary>Waits (at most <paramref name="timeoutMs"/>) until the process that asked for the reset has ended.</summary>
        internal static void WaitForEnd(int pid, int timeoutMs = 15000)
        {
            try { using var p = Process.GetProcessById(pid); p.WaitForExit(timeoutMs); }
            catch { /* it is gone already (or cannot be reached): nothing to wait for */ }
        }
    }
}

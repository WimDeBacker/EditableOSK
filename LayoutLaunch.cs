// LayoutLaunch.cs — Opening a .kbl layout from outside: a double-click in Explorer starts
// "OnScreenKeyboard.exe <file>". When a keyboard is already running the file is handed to it (one keyboard, not two
// always-on-top windows); otherwise the new process loads it at start-up.

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace OnScreenKeyboard
{
    internal static class LayoutLaunch
    {
        internal const string Extension = ".kbl";
        private const int MaxPathChars = 32768;

        /// <summary>
        /// The layout file named on the command line: the first argument that is not an option (<c>--test</c>, <c>--gallery</c>),
        /// made absolute against <paramref name="currentDirectory"/>. Null when there is none, or when that file does not exist.
        /// </summary>
        internal static string FindLayoutArgument(string[] args, string currentDirectory)
        {
            foreach (string raw in args)
            {
                string a = raw?.Trim().Trim('"');
                if (string.IsNullOrEmpty(a) || a.StartsWith("--", StringComparison.Ordinal)) continue;
                try
                {
                    string full = Path.GetFullPath(a, currentDirectory);
                    return File.Exists(full) ? full : null;
                }
                catch { return null; }                       // an invalid path
            }
            return null;
        }

        /// <summary>True for a path another process may ask this keyboard to open: an existing, absolute <c>.kbl</c> file.</summary>
        internal static bool IsAcceptable(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path)
                    && string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path);
            }
            catch { return false; }
        }

        // One pipe per Windows user and per data folder: the keyboard of another user on the same machine is not ours to steer, and a test
        // version (its own data folder, see UserData) does not take files meant for the installed program, nor the other way round.
        internal static string PipeName(string suffix = "") =>
            "EditableOSK.OpenLayout." + Environment.UserName + "." + DataTag() + suffix;

        private static string DataTag()
        {
            byte[] h = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(UserData.Directory.ToLowerInvariant()));
            return Convert.ToHexString(h, 0, 4);
        }

        /// <summary>Asks a running keyboard to open <paramref name="path"/>. False when none answers within <paramref name="timeoutMs"/>.</summary>
        internal static bool TrySendToRunning(string path, int timeoutMs = 600, string pipeSuffix = "")
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName(pipeSuffix), PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(timeoutMs);
                using var w = new StreamWriter(client, new UTF8Encoding(false));
                w.Write(path);
                w.Flush();
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Listens (on a background thread) for layout files other processes hand over, and calls <paramref name="onPath"/> with each
        /// acceptable path (on that background thread). Only the first keyboard that starts listens; a second one quietly does not.
        /// </summary>
        internal static Thread Listen(Action<string> onPath, string pipeSuffix = "")
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    NamedPipeServerStream server;
                    try
                    {
                        server = new NamedPipeServerStream(PipeName(pipeSuffix), PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    }
                    catch { return; }                    // the name is taken (another keyboard listens) or pipes are unavailable

                    string path = null;
                    using (server)
                    {
                        try
                        {
                            server.WaitForConnection();
                            using var rd = new StreamReader(server, new UTF8Encoding(false));
                            var buf = new char[MaxPathChars];
                            int n = rd.ReadBlock(buf, 0, buf.Length);
                            path = new string(buf, 0, n).Trim();
                        }
                        catch { }                        // a connection that broke: wait for the next one
                    }
                    if (IsAcceptable(path))
                        try { onPath(path); } catch (Exception ex) { Program.LogError("open layout from another process", ex); }
                }
            }) { IsBackground = true, Name = "OpenLayoutListener" };
            t.Start();
            return t;
        }
    }
}

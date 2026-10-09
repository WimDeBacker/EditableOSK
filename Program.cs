// Program.cs — Entry point for the OnScreenKeyboard application.
//
// This file contains the very first code that runs when the application starts.
// Its only job is to decide which "mode" to run in:
//   - Normal mode: opens the on-screen keyboard window for the user to interact with.
//   - Test mode:   runs automated tests in a console window (useful during development).

using System;
using System.Runtime.InteropServices;  // Needed for DllImport (calling Windows API functions)
using System.Windows.Forms;            // Needed for Application.Run (starting the GUI)

namespace OnScreenKeyboard
{
    /// <summary>
    /// The application entry point. Decides whether to run the graphical keyboard
    /// or the automated test suite, based on the command-line arguments provided.
    /// </summary>
    static class Program
    {
        // ── Windows API declarations ──────────────────────────────────────────
        // Windows GUI applications do not normally have a console window.
        // These two functions let us attach or detach a console on demand.
        // DllImport tells C# that the actual code lives inside "kernel32.dll",
        // which is part of Windows itself — we are just declaring the signature here.

        /// <summary>
        /// Detaches (hides) the console window from this process.
        /// Called when running in normal GUI mode so no black terminal window appears.
        /// </summary>
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();

        /// <summary>
        /// Creates and attaches a new console window to this process.
        /// Used as a fallback in test mode when there is no parent console to attach to
        /// (e.g. the exe is launched by double-clicking rather than from a terminal).
        /// </summary>
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();

        /// <summary>
        /// Attaches this process to the console of another process.
        /// Pass <c>uint.MaxValue</c> (= <c>ATTACH_PARENT_PROCESS</c>) to reuse the
        /// console of the process that launched us (e.g. the <c>dotnet run</c> terminal),
        /// so test output appears inline without opening a new window.
        /// </summary>
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint dwProcessId);

        // ── Entry point ───────────────────────────────────────────────────────

        /// <summary>
        /// The first method that runs when the application starts.
        /// Checks for the <c>--test</c> command-line argument to decide the run mode.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments passed when launching the application.
        /// Pass <c>--test</c> to run the automated test suite instead of the GUI.
        /// Example: <c>dotnet run -- --test</c>
        /// </param>
        /// <returns>
        /// An exit code: 0 means success, non-zero means one or more tests failed.
        /// The GUI path always returns 0.
        /// </returns>
        [STAThread]  // Required by Windows Forms: the main thread must be a Single-Threaded Apartment
        static int Main(string[] args)
        {
            // Developer tool: "--gallery <folder>" saves PNGs of the editor dialogs (see DevGallery).
            int galleryAt = Array.IndexOf(args, "--gallery");
            if (galleryAt >= 0)
                return DevGallery.Run(galleryAt + 1 < args.Length ? args[galleryAt + 1] : "gallery");

            // Check whether the user launched the app with the "--test" flag.
            // Array.IndexOf returns -1 when the item is not found, so >= 0 means "found".
            if (Array.IndexOf(args, "--test") >= 0)
            {
                // Test mode: write output to the console.
                // With OutputType=WinExe the process has no console by default, so we must
                // attach one explicitly.  Prefer reusing the parent process's console
                // (e.g. the "dotnet run" terminal) so output appears inline without
                // popping up a new window.  Fall back to AllocConsole only when there is
                // no parent console to attach to (e.g. launched by double-click).
                bool attachedToParent = AttachConsole(uint.MaxValue); // ATTACH_PARENT_PROCESS
                if (!attachedToParent) AllocConsole();

                // Run all tests and capture how many failed (0 = all passed).
                int result = TestRunner.Run();

                // When we opened our own console window (no parent terminal) pause so the
                // user can read the results before the window closes.  When we attached to
                // an existing terminal there is no need — the caller's prompt reappears
                // immediately after the process exits.
                if (!attachedToParent)
                {
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                }

                // Return the failure count as the exit code.
                // CI systems (like GitHub Actions) can inspect this value to know if tests passed.
                return result;
            }

            // Normal GUI mode: remove the console window that might have been
            // inherited from the terminal the user launched from.
            FreeConsole();

            // Standard Windows Forms startup sequence:
            Application.EnableVisualStyles();                    // Use the OS's current visual theme (rounded buttons, etc.)
            Application.SetCompatibleTextRenderingDefault(false); // Use GDI+ text rendering (looks better on modern Windows)

            // An exception on the UI thread is logged and the app carries on, instead of the standard "unhandled exception"
            // dialog: that dialog is a window of its own that can sit hidden behind others (this window is always on top)
            // and then keeps the process alive after the keyboard window has closed.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogError("UI thread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError("unhandled", e.ExceptionObject as Exception);

            Application.Run(new KeyboardForm());                  // Create the main window and start the event loop

            // Reaching here means the user closed the keyboard window normally. End the process now, whatever else is still
            // alive (a stray foreground thread, a hook): a closed keyboard must never leave OnScreenKeyboard.exe running,
            // since that locks the exe for the next build and for the next start.
            Environment.Exit(0);
            return 0;
        }

        /// <summary>Appends an error to <c>OnScreenKeyboard_error.log</c> next to the exe (best effort, never throws; see <see cref="ErrorLog"/>).</summary>
        internal static void LogError(string where, Exception ex) =>
            ErrorLog.Write(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OnScreenKeyboard_error.log"), where, ex, DateTime.Now);
    }

    /// <summary>
    /// The error log. An exception in a paint handler comes back on every repaint, so the log must neither grow without bound nor be written
    /// again and again: the same error within a few seconds is written once, and a log past <see cref="MaxBytes"/> is moved to ".old"
    /// (one generation is kept) before the next entry.
    /// </summary>
    internal static class ErrorLog
    {
        internal const long MaxBytes = 256 * 1024;
        internal static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(5);

        private static string _lastText;
        private static DateTime _lastAt;
        private static readonly object _lock = new object();

        /// <summary>Forgets the last error (for the tests, which write the same error on purpose).</summary>
        internal static void Reset() { lock (_lock) { _lastText = null; _lastAt = default; } }

        /// <summary>Writes one entry to <paramref name="file"/>; true when it was written, false when it was left out as a repeat or failed.</summary>
        internal static bool Write(string file, string where, Exception ex, DateTime now, long maxBytes = MaxBytes)
        {
            try
            {
                lock (_lock)
                {
                    string text = $"[{where}] {ex}";
                    if (text == _lastText && now - _lastAt < RepeatWindow) return false;     // the same error again: not logged
                    _lastText = text; _lastAt = now;

                    if (System.IO.File.Exists(file) && new System.IO.FileInfo(file).Length > maxBytes)
                        System.IO.File.Move(file, file + ".old", overwrite: true);            // keep one earlier generation
                    System.IO.File.AppendAllText(file, $"{now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}{Environment.NewLine}");
                    return true;
                }
            }
            catch { return false; }
        }
    }
}

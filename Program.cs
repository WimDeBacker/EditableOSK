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
        /// Called when running in test mode so test output can be printed as text.
        /// </summary>
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();

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
            // Check whether the user launched the app with the "--test" flag.
            // Array.IndexOf returns -1 when the item is not found, so >= 0 means "found".
            if (Array.IndexOf(args, "--test") >= 0)
            {
                // Test mode: open a console so we can print results as plain text.
                AllocConsole();

                // Run all tests and capture how many failed (0 = all passed).
                int result = TestRunner.Run();

                // Wait for the user to press a key before closing the console window,
                // so they have time to read the test output.
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();

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
            Application.Run(new KeyboardForm());                  // Create the main window and start the event loop

            return 0; // Reaching here means the user closed the keyboard window normally.
        }
    }
}

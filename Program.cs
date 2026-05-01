using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();

        [STAThread]
        static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--test") >= 0)
            {
                AllocConsole();
                int result = TestRunner.Run();
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return result;
            }

            // Normal mode
            FreeConsole();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new KeyboardForm());
            return 0;
        }
    }
}

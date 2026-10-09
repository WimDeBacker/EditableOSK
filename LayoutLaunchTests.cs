// LayoutLaunchTests.cs — opening a .kbl by double-click (todo Priority 5): reading the file from the command line and handing it
// to a keyboard that is already running.

using System;
using System.IO;
using System.Threading;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_LayoutLaunch()
        {
            Section("Open a .kbl layout — command line and hand-over to a running keyboard");

            string dir = Path.Combine(Path.GetTempPath(), "osk layout launch " + Guid.NewGuid().ToString("N"));   // a folder with spaces
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "mijn toetsenbord.kbl");
            File.WriteAllText(file, "<x/>");
            try
            {
                Assert(LayoutLaunch.FindLayoutArgument(new[] { file }, dir) == file, "an absolute path with spaces is found");
                Assert(LayoutLaunch.FindLayoutArgument(new[] { "\"" + file + "\"" }, dir) == file, "a path in quotes is found");
                Assert(LayoutLaunch.FindLayoutArgument(new[] { "mijn toetsenbord.kbl" }, dir) == file, "a relative path is made absolute against the current folder");
                Assert(LayoutLaunch.FindLayoutArgument(new string[0], dir) == null, "no argument: nothing to open");
                Assert(LayoutLaunch.FindLayoutArgument(new[] { "--test" }, dir) == null, "an option is not a file");
                Assert(LayoutLaunch.FindLayoutArgument(new[] { "--gallery", file }, dir) == file, "an option is skipped, the file after it is taken");
                Assert(LayoutLaunch.FindLayoutArgument(new[] { Path.Combine(dir, "bestaat niet.kbl") }, dir) == null, "a file that does not exist is ignored");
                Assert(LayoutLaunch.FindLayoutArgument(new[] { "a<b|c.kbl" }, dir) == null, "an invalid file name does not throw");
                Assert(LayoutLaunch.FindLayoutArgument(new[] { "", "  ", file }, dir) == file, "empty arguments are skipped");

                Assert(LayoutLaunch.IsAcceptable(file), "an existing absolute .kbl file is acceptable");
                Assert(LayoutLaunch.IsAcceptable(file.ToUpperInvariant().Replace(dir.ToUpperInvariant(), dir)), "the extension is matched without regard to case");
                string txt = Path.Combine(dir, "x.txt"); File.WriteAllText(txt, "x");
                Assert(!LayoutLaunch.IsAcceptable(txt), "another file type is refused");
                Assert(!LayoutLaunch.IsAcceptable("mijn toetsenbord.kbl"), "a relative path is refused");
                Assert(!LayoutLaunch.IsAcceptable(null) && !LayoutLaunch.IsAcceptable(""), "nothing is refused");
                Assert(!LayoutLaunch.IsAcceptable(Path.Combine(dir, "weg.kbl")), "a missing file is refused");

                // Hand-over through the pipe, on a name of its own so a keyboard that happens to run does not take part.
                string suffix = "." + Guid.NewGuid().ToString("N");
                Assert(!LayoutLaunch.TrySendToRunning(file, 100, suffix), "no running keyboard: the hand-over fails, the caller starts its own");

                string got = null; var received = new ManualResetEventSlim(false);
                var listener = LayoutLaunch.Listen(p => { got = p; received.Set(); }, suffix);
                Assert(LayoutLaunch.TrySendToRunning(file, 3000, suffix), "a running keyboard takes the file");
                Assert(received.Wait(3000) && got == file, "the keyboard receives exactly that path");

                got = null; received.Reset();
                LayoutLaunch.TrySendToRunning(txt, 3000, suffix);
                Assert(!received.Wait(400) && got == null, "a path that is not a .kbl file is not passed on");

                received.Reset();
                Assert(LayoutLaunch.TrySendToRunning(file, 3000, suffix) && received.Wait(3000) && got == file, "a second file is taken as well");

                var second = LayoutLaunch.Listen(p => { }, suffix);
                Assert(second.Join(2000), "a second listener on the same name gives up quietly");
                Assert(listener.IsAlive, "the first listener keeps running");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}

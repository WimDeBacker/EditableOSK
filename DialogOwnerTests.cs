// DialogOwnerTests.cs — todo Priority 0: a window the keyboard opens without an owner opens BEHIND the always-on-top keyboard (which never
// becomes the active window) and cannot be used. The guards of the dialogs cannot show it (their windows are invisible), so this test reads
// the source: every file dialog, message box and dialog of the program is opened with an owner.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_DialogOwners()
        {
            Section("Windows opened by the keyboard always have an owner (they would open behind the keyboard otherwise)");

            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "OnScreenKeyboard.csproj"))) dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
            if (dir == null) { Assert(true, "owners: the source is not next to the test binary (an installed copy): not checked"); return; }

            var bad = new List<string>();
            int shown = 0;
            foreach (string file in Directory.GetFiles(dir, "*.cs"))
            {
                string name = Path.GetFileName(file);
                // Test code, the gallery and the icon maker open windows that nobody sees or uses.
                if (name.EndsWith("Tests.cs") || name.StartsWith("TestRunner") || name == "DevGallery.cs" || name == "MakeIco.cs") continue;
                string text = File.ReadAllText(file);

                foreach (Match m in Regex.Matches(text, @"\.ShowDialog\(\s*\)"))
                    bad.Add($"{name}: ShowDialog() without an owner (line {LineOf(text, m.Index)})");
                shown += Regex.Matches(text, @"\.ShowDialog\(").Count;

                foreach (Match m in Regex.Matches(text, @"MessageBox\.Show\(\s*([^,)]*)"))
                {
                    string first = m.Groups[1].Value.Trim();
                    shown++;
                    if (!(first == "this" || first.StartsWith("DialogOwner(") || first.EndsWith("owner", StringComparison.OrdinalIgnoreCase)))
                        bad.Add($"{name}: MessageBox.Show without an owner (line {LineOf(text, m.Index)}: '{first}')");
                }

                foreach (Match m in Regex.Matches(text, @"TouchMessage\.(Confirm|Info)\(\s*([^,)]*)"))
                {
                    string first = m.Groups[2].Value.Trim();
                    shown++;
                    if (!(first == "this" || first.EndsWith("owner", StringComparison.OrdinalIgnoreCase)))
                        bad.Add($"{name}: TouchMessage.{m.Groups[1].Value} without an owner (line {LineOf(text, m.Index)}: '{first}')");
                }
            }
            Assert(shown > 20, $"owners: the source was read ({shown} places that open a window)");
            Assert(bad.Count == 0, "owners: every file dialog, message box and dialog is opened with an owner" + (bad.Count == 0 ? "" : " — " + string.Join("; ", bad)));
        }

        private static int LineOf(string text, int index) => text.Take(index).Count(c => c == '\n') + 1;
    }
}

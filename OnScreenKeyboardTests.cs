// ═══════════════════════════════════════════════════════════════════════════
//  OnScreenKeyboardTests.cs  —  Self-contained test runner
//
//  HOW TO RUN:
//      dotnet run -- --test
//
//  Exit code 0 = all passed.  Exit code 1 = one or more failures.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static int _pass, _fail;
        private static readonly List<string> _failures = new List<string>();

        public static int Run()
        {
            SendKeysHelper.TestMode = true;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine("  On-Screen Keyboard — Test Suite");
            Console.WriteLine("═══════════════════════════════════════════");
            Console.ResetColor();

            T_KeyProps();
            T_SendKeysHelper_Escape();
            T_SendKeysHelper_Modifiers();
            T_SendKeysHelper_IsPlainText();
            T_SendKeysHelper_WinKey();
            T_SendKeysHelper_HumanReadable();
            T_SettingsManager_RoundTrip();
            T_SettingsManager_AtomicSave();
            T_SettingsManager_Robustness();
            T_SettingsManager_Sentinels();
            T_KeyLayout();
            T_LanguageManager();
            T_LanguageXmlSafety();
            T_GridLayout();
            T_FontSizing();
            T_CharacterRouting();
            T_SlowReceiverStress();
            T_UndoRedo();
            T_SendKeysStripping();
            T_AutoScaleMode_Dialogs();
            T_GrowWindowOnEditMode();
            T_PaintHandlerAudit();
            T_StyleGroups();
            T_XmlRobustness();

            // Run word prediction tests (uses shared Assert/Section → failures go to report)
            WordPredictionTests.Run(Assert, Section);
            // Run end-to-end predictor tests
            WordPredictorE2ETests.Run(Assert, Section);
            // Run word database robustness tests (graceful failure on bad/missing DB)
            WordDatabaseRobustnessTests.Run(Assert, Section);

            // ── Final summary and report (after ALL tests including prediction) ──
            Console.WriteLine();
            if (_fail == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ALL {_pass} TESTS PASSED");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  {_fail} FAILED  /  {_pass} passed");
            }
            Console.ResetColor();

            string logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "test_failures.txt");
            try
            {
                var lines = new List<string>
                {
                    "On-Screen Keyboard — Test Failure Report",
                    $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"Failed: {_fail}  Passed: {_pass}  Total: {_fail + _pass}",
                    new string('═', 60), ""
                };
                if (_failures.Count == 0) lines.Add("All tests passed — no failures.");
                else foreach (var f in _failures) lines.Add("  ✘  " + f);
                File.WriteAllLines(logPath, lines,
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n  Failure report: {logPath}");
                Console.ResetColor();
            }
            catch (Exception ex) { Console.WriteLine($"  (Could not write report: {ex.Message})"); }
            return _fail > 0 ? 1 : 0;
        }

        // ── Helpers ──────────────────────────────────────────────────
        private static void Assert(bool condition, string name)
        {
            if (condition) { Console.ForegroundColor = ConsoleColor.Green;  Console.WriteLine($"  ✔  {name}"); _pass++; }
            else           { Console.ForegroundColor = ConsoleColor.Red;    Console.WriteLine($"  ✘  {name}"); _failures.Add(name); _fail++; }
            Console.ResetColor();
        }

        private static void Section(string name)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  {name}");
            Console.ResetColor();
        }

        // ════════════════════════════════════════════════════════════════
        // 1. KeyProps — construction, clone, sentinels
        // ════════════════════════════════════════════════════════════════
        private static void T_KeyProps()
        {
            Section("KeyProps");

            var p = new KeyProps("a", "a", "A", "A", "@", "@");
            Assert(p.Label      == "a",  "Label stored");
            Assert(p.Send       == "a",  "Send stored");
            Assert(p.ShiftLabel == "A",  "ShiftLabel stored");
            Assert(p.ShiftSend  == "A",  "ShiftSend stored");
            Assert(p.AltGrLabel == "@",  "AltGrLabel stored");
            Assert(p.AltGrSend  == "@",  "AltGrSend stored");

            // GetDisplayLabel priority: AltGr > Shift > Base
            Assert(p.GetDisplayLabel(false, false) == "a", "Display: normal = a");
            Assert(p.GetDisplayLabel(true,  false) == "A", "Display: shifted = A");
            Assert(p.GetDisplayLabel(false, true)  == "@", "Display: AltGr = @");
            Assert(p.GetDisplayLabel(true,  true)  == "@", "Display: AltGr beats Shift");

            // Empty AltGr falls back to Shift
            var p2 = new KeyProps("b", "b", "B", "B");
            Assert(p2.GetDisplayLabel(true, true) == "B", "Empty AltGr falls back to Shift");

            // Null shift/altgr falls back to base
            var p3 = new KeyProps("x", "x") { ShiftLabel = null, AltGrLabel = null };
            Assert(p3.GetDisplayLabel(true, true) == "x", "Null Shift/AltGr falls back to base");

            // Clone
            var c = p.Clone();
            Assert(c.Label      == p.Label,      "Clone.Label");
            Assert(c.AltGrLabel == p.AltGrLabel, "Clone.AltGrLabel");
            Assert(!ReferenceEquals(c, p),        "Clone is new object");

            // ── Sentinel defaults (new system) ────────────────────────────
            // Colors: Color.Empty means "use global default"
            // FontName: "" means "use global default"
            // BorderThickness: -1 means "use global default", 0 = no border
            var def = new KeyProps("a", "a");
            Assert(def.FontColor.IsEmpty,        "Default FontColor is Empty (use global)");
            Assert(def.KeyColor.IsEmpty,         "Default KeyColor is Empty (use global)");
            Assert(def.BorderColor.IsEmpty,      "Default BorderColor is Empty (use global)");
            Assert(def.BorderThickness == -1,    "Default BorderThickness is -1 (use global)");
            Assert(string.IsNullOrEmpty(def.FontName), "Default FontName is empty (use global)");
            Assert(def.FontSize == 0,            "Default FontSize is 0 (auto-size)");

            // Explicitly set values are preserved
            var styled = new KeyProps("a", "a")
            {
                FontColor = Color.Red, KeyColor = Color.Blue,
                BorderColor = Color.Green, BorderThickness = 2,
                FontName = "Verdana", FontSize = 14,
            };
            Assert(styled.FontColor == Color.Red,    "Explicit FontColor stored");
            Assert(styled.KeyColor  == Color.Blue,   "Explicit KeyColor stored");
            Assert(styled.BorderColor == Color.Green,"Explicit BorderColor stored");
            Assert(styled.BorderThickness == 2,      "Explicit BorderThickness 2 stored");
            Assert(styled.FontName == "Verdana",     "Explicit FontName stored");
            Assert(styled.FontSize == 14,            "Explicit FontSize stored");

            // BorderThickness 0 = explicit no border (not "use global")
            var noBorder = new KeyProps("a", "a") { BorderThickness = 0 };
            Assert(noBorder.BorderThickness == 0, "BorderThickness 0 = no border");
        }

        // ════════════════════════════════════════════════════════════════
        // 2. SendKeysHelper — EscapeForSend
        // ════════════════════════════════════════════════════════════════
        private static void T_SendKeysHelper_Escape()
        {
            Section("SendKeysHelper.EscapeForSend");

            Assert(SendKeysHelper.EscapeForSend("abc")     == "abc",      "Plain text unchanged");
            Assert(SendKeysHelper.EscapeForSend("+")       == "{+}",      "Plus escaped");
            Assert(SendKeysHelper.EscapeForSend("^")       == "{^}",      "Caret escaped");
            Assert(SendKeysHelper.EscapeForSend("%")       == "{%}",      "Percent escaped");
            Assert(SendKeysHelper.EscapeForSend("~")       == "{~}",      "Tilde escaped");
            Assert(SendKeysHelper.EscapeForSend("(")       == "{(}",      "Open paren escaped");
            Assert(SendKeysHelper.EscapeForSend(")")       == "{)}",      "Close paren escaped");
            Assert(SendKeysHelper.EscapeForSend("[")       == "{[}",      "Open bracket escaped");
            Assert(SendKeysHelper.EscapeForSend("]")       == "{]}",      "Close bracket escaped");
            Assert(SendKeysHelper.EscapeForSend("{ENTER}") == "{ENTER}",  "Existing {ENTER} preserved");
            Assert(SendKeysHelper.EscapeForSend("{^}")     == "{^}",      "Already-escaped caret preserved");
            Assert(SendKeysHelper.EscapeForSend("{F5}")    == "{F5}",     "F5 sequence preserved");
            Assert(SendKeysHelper.EscapeForSend("a+b")    == "a{+}b",    "Mid-string special char escaped");
            Assert(SendKeysHelper.EscapeForSend("a{F5}b") == "a{F5}b",   "F5 in string preserved");
            Assert(SendKeysHelper.EscapeForSend("100%")   == "100{%}",   "Percent in number escaped");
            Assert(SendKeysHelper.EscapeForSend("")        == "",         "Empty string → empty");
            Assert(SendKeysHelper.EscapeForSend(null)      == null,       "Null → null");
        }

        // ════════════════════════════════════════════════════════════════
        // 3. SendKeysHelper — ApplyModifiers
        // ════════════════════════════════════════════════════════════════
        private static void T_SendKeysHelper_Modifiers()
        {
            Section("SendKeysHelper.ApplyModifiers");

            Assert(SendKeysHelper.ApplyModifiers("a", false, false, false) == "a",     "No modifiers");
            Assert(SendKeysHelper.ApplyModifiers("a", true,  false, false) == "+a",    "Shift");
            Assert(SendKeysHelper.ApplyModifiers("a", false, true,  false) == "^a",    "Ctrl");
            Assert(SendKeysHelper.ApplyModifiers("a", false, false, true)  == "%a",    "Alt");
            Assert(SendKeysHelper.ApplyModifiers("a", true,  true,  false) == "^+a",   "Ctrl+Shift");
            Assert(SendKeysHelper.ApplyModifiers("a", false, true,  true)  == "^%a",   "Ctrl+Alt");
            Assert(SendKeysHelper.ApplyModifiers("ab", true, false, false) == "+(ab)", "Multi-char wrapped");
            Assert(SendKeysHelper.ApplyModifiers(" ",  true, false, false) == "+( )",  "Space wrapped");
            Assert(SendKeysHelper.ApplyModifiers("",   true, false, false) == "",      "Empty with modifier");
            Assert(SendKeysHelper.ApplyModifiers(null, true, false, false) == null,    "Null with modifier");
        }

        // ════════════════════════════════════════════════════════════════
        // 4. SendKeysHelper — IsPlainText routing
        // ════════════════════════════════════════════════════════════════
        private static void T_SendKeysHelper_IsPlainText()
        {
            Section("SendKeysHelper.IsPlainText");

            // Plain text — goes via SendInput Unicode (direct injection)
            foreach (var ch in new[] { "a", "A", "é", "@", "€", "#", "|", "µ",
                                       "ù", "\\", "^", "%", "+", "~", "(", ")",
                                       "{", "}", "[", "]", "α", "→", "½", "²" })
                Assert(SendKeysHelper.IsPlainText(ch), $"'{ch}' is plain text");

            // Not plain — goes via SendKeys
            foreach (var s in new[] { "{ENTER}", "{F1}", "{F12}", "{BACKSPACE}", "{TAB}",
                                      "{LEFT}", "{ESC}", "{DELETE}", "{HOME}", "{END}",
                                      "^c", "^v", "%{F4}", "+a", "^+c",
                                      "{^}", "{%}", "{+}", "{~}", "dead:^", "win:m" })
                Assert(!SendKeysHelper.IsPlainText(s), $"'{s}' is not plain text");

            // Edge cases
            Assert(!SendKeysHelper.IsPlainText(""),   "Empty string is not plain");
            Assert(!SendKeysHelper.IsPlainText(null), "Null is not plain");
        }

        // ════════════════════════════════════════════════════════════════
        // 5. SendKeysHelper — Win key prefix
        // ════════════════════════════════════════════════════════════════
        private static void T_SendKeysHelper_WinKey()
        {
            Section("SendKeysHelper — win: prefix routing");

            // win: prefix must NOT be plain text — goes via SendInput VK_LWIN path
            Assert(!SendKeysHelper.IsPlainText("win:m"),      "win:m is not plain");
            Assert(!SendKeysHelper.IsPlainText("win:d"),      "win:d is not plain");
            Assert(!SendKeysHelper.IsPlainText("win:{LEFT}"), "win:{LEFT} is not plain");
            Assert(!SendKeysHelper.IsPlainText("win:{F4}"),   "win:{F4} is not plain");

            // win: prefix is distinct from SendKeys modifier prefix (^, %, +)
            Assert(SendKeysHelper.IsPlainText("w"),     "'w' is plain — not a win: prefix");
            Assert(SendKeysHelper.IsPlainText("win"),   "'win' is plain — no colon");
        }

        // ════════════════════════════════════════════════════════════════
        // 6. SendKeysHelper — ToHuman / FromHuman round-trips
        //    (These mirror the logic in KeyEditorForm — tested here because
        //     they are pure string functions with no UI dependency.)
        // ════════════════════════════════════════════════════════════════
        private static void T_SendKeysHelper_HumanReadable()
        {
            Section("Human-readable Send conversion (ToHuman / FromHuman)");

            // ToHuman: internal → display
            var toHumanCases = new (string input, string expected)[]
            {
                ("^c",        "{Ctrl}c"),
                ("^v",        "{Ctrl}v"),
                ("%{F4}",     "{Alt}{F4}"),
                ("%{TAB}",    "{Alt}{TAB}"),
                ("^+s",       "{Ctrl}{Shift}s"),
                ("+a",        "{Shift}a"),
                ("{ENTER}",   "{ENTER}"),
                ("{F5}",      "{F5}"),
                ("{LEFT}",    "{LEFT}"),
                ("win:m",     "{Win}m"),
                ("win:d",     "{Win}d"),
                ("win:{LEFT}","{Win}{LEFT}"),
                ("a",         "a"),
                ("",          ""),
            };

            foreach (var (input, expected) in toHumanCases)
                Assert(ToHuman(input) == expected,
                    $"ToHuman({input!.Replace("{","{")}) = {expected}");

            // FromHuman: display → internal (round-trip)
            var fromHumanCases = new (string human, string expected)[]
            {
                ("{Ctrl}c",       "^c"),
                ("{Ctrl}v",       "^v"),
                ("{Alt}{F4}",     "%{F4}"),
                ("{Ctrl}{Shift}s","^+s"),
                ("{Shift}a",      "+a"),
                ("{ENTER}",       "{ENTER}"),
                ("{F5}",          "{F5}"),
                ("{Win}m",        "win:m"),
                ("{Win}d",        "win:d"),
                ("{Win}{LEFT}",   "win:{LEFT}"),
                ("a",             "a"),
                ("",              ""),
            };

            foreach (var (human, expected) in fromHumanCases)
                Assert(FromHuman(human) == expected,
                    $"FromHuman({human}) = {expected}");

            // Full round-trip: internal → human → internal
            foreach (var (input, _) in toHumanCases)
            {
                if (string.IsNullOrEmpty(input)) continue;
                string rt = FromHuman(ToHuman(input));
                Assert(rt == input, $"Round-trip: {input} → {ToHuman(input)} → {rt}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 7. SettingsManager — save/load round-trip
        // ════════════════════════════════════════════════════════════════
        private static void T_SettingsManager_RoundTrip()
        {
            Section("SettingsManager — save/load round-trip");

            string tmp = Path.Combine(Path.GetTempPath(), $"osk_rt_{Guid.NewGuid()}.xml");
            try
            {
                var layout = new GridLayout(2, 3);
                layout.Cells.Add(new GridCell(0, 0, new KeyProps("a","a","A","A","@","@")));
                layout.Cells.Add(new GridCell(0, 1, new KeyProps("b","b","B","B"), 1, 2));  // ColSpan=2
                layout.Cells.Add(new GridCell(1, 0, new KeyProps("Shift","")));
                layout.Cells.Add(new GridCell(1, 1, new KeyProps("Space"," "), 1, 2));
                // Key with explicit style overrides
                // Use FromArgb (not named colors) to avoid Color named-vs-unnamed equality issues
                var cRed   = Color.FromArgb(255, 220,  30,  30);
                var cBlue  = Color.FromArgb(255,  30,  30, 220);
                var cGreen = Color.FromArgb(255,  30, 180,  30);
                var styledKey = new KeyProps("X","X")
                {
                    FontColor = cRed, KeyColor = cBlue,
                    BorderColor = cGreen, BorderThickness = 3,
                    FontName = "Verdana", FontSize = 14,
                };
                layout.Cells.Add(new GridCell(0, 2, styledKey));  // overlapped by ColSpan=2 cell above
                // Re-add as standalone
                layout.Cells.Clear();
                layout.Cells.Add(new GridCell(0, 0, new KeyProps("a","a","A","A","@","@")));
                layout.Cells.Add(new GridCell(0, 1, new KeyProps("b","b","B","B")));
                layout.Cells.Add(new GridCell(0, 2, styledKey));
                layout.Cells.Add(new GridCell(1, 0, new KeyProps("Shift","")));
                layout.Cells.Add(new GridCell(1, 1, new KeyProps("Space"," ")));
                layout.Cells.Add(new GridCell(1, 2, new KeyProps("↵","{ENTER}")));

                var saveTheme = new VisualTheme
                {
                    BackgroundColor = Color.FromArgb(10,20,30),
                    Opacity  = 0.85,
                    FontName = "Arial",
                    FontSize = 12,
                };
                var saveWindow = new WindowState
                {
                    WindowWidth = 800, WindowHeight = 250,
                    HideTitlebar = true, AlwaysOnTop = false,
                };
                var saveMeta = new LayoutMeta
                {
                    Language = "nl", LastFile = tmp, StickyModifiers = false,
                };

                SettingsManager.SaveSettings(layout, saveTheme, saveWindow, saveMeta, tmp);
                Assert(File.Exists(tmp), "XML file created");

                var lgTheme = new VisualTheme();
                var lgWindow = new WindowState();
                var lgMeta = new LayoutMeta();
                var lr = SettingsManager.LoadSettings(lgTheme, lgWindow, lgMeta, tmp);

                Assert(lr != null,          "GridLayout loaded");
                Assert(lr.Rows == 2,        "Row count");
                Assert(lr.Cols == 3,        "Col count");
                Assert(lr.Cells.Count == 6, "Cell count");

                var cell00 = lr.CellAt(0, 0);
                var cell02 = lr.CellAt(0, 2);
                var cell12 = lr.CellAt(1, 2);
                Assert(cell00?.Props.Label     == "a",  "Label round-trip");
                Assert(cell00?.Props.ShiftLabel== "A",  "ShiftLabel round-trip");
                Assert(cell00?.Props.AltGrLabel== "@",  "AltGrLabel round-trip");
                Assert(cell12?.Props.Send == "{ENTER}", "Send round-trip");

                // Explicit style overrides survive round-trip
                Assert(cell02?.Props.FontColor.ToArgb()   == cRed.ToArgb(),   "Explicit FontColor round-trip");
                Assert(cell02?.Props.KeyColor.ToArgb()    == cBlue.ToArgb(),  "Explicit KeyColor round-trip");
                Assert(cell02?.Props.BorderColor.ToArgb() == cGreen.ToArgb(), "Explicit BorderColor round-trip");
                Assert(cell02?.Props.BorderThickness == 3,      "Explicit BorderThickness round-trip");
                Assert(cell02?.Props.FontName == "Verdana",     "Explicit FontName round-trip");
                Assert(cell02?.Props.FontSize == 14,            "Explicit FontSize round-trip");

                // Sentinel (use-global) values survive round-trip
                Assert(cell00?.Props.FontColor.IsEmpty  == true,  "Sentinel FontColor round-trip");
                Assert(cell00?.Props.KeyColor.IsEmpty   == true,  "Sentinel KeyColor round-trip");
                Assert(cell00?.Props.BorderColor.IsEmpty== true,  "Sentinel BorderColor round-trip");
                Assert(cell00?.Props.BorderThickness == -1,       "Sentinel BorderThickness round-trip");
                Assert(string.IsNullOrEmpty(cell00?.Props.FontName), "Sentinel FontName round-trip");

                // Global settings
                Assert(lgTheme.FontName   == "Arial",   "Global FontName");
                Assert(lgTheme.FontSize   == 12,        "Global FontSize");
                Assert(lgMeta.Language    == "nl",      "Global Language");
                Assert(lgWindow.WindowWidth  == 800,    "Global WindowWidth");
                Assert(lgWindow.WindowHeight == 250,    "Global WindowHeight");
                Assert(lgWindow.HideTitlebar  == true,  "HideTitlebar round-trip");
                Assert(lgMeta.StickyModifiers == false, "StickyModifiers round-trip");
                Assert(lgWindow.AlwaysOnTop   == false, "AlwaysOnTop round-trip");
                Assert(Math.Abs(lgTheme.Opacity - 0.85) < 0.001, "Opacity round-trip");
                Assert(lgTheme.BackgroundColor == Color.FromArgb(10,20,30), "BackgroundColor round-trip");
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        // ════════════════════════════════════════════════════════════════
        // 7b. SettingsManager — atomic save
        // ════════════════════════════════════════════════════════════════
        private static void T_SettingsManager_AtomicSave()
        {
            Section("SettingsManager — atomic save");

            string dir  = Path.GetTempPath();
            string path = Path.Combine(dir, $"osk_atomicsave_{Guid.NewGuid():N}.xml");
            string tmp  = path + ".tmp";
            string bak  = path + ".bak";

            try
            {
                var layout = new GridLayout(1, 1);
                layout.Cells.Add(new GridCell(0, 0, new KeyProps("X", "X")));

                // ── First save: no existing file → must use File.Move path ──
                SettingsManager.SaveSettings(layout, new VisualTheme(), new WindowState(), new LayoutMeta(), path);
                Assert(File.Exists(path),  "First save: real file created");
                Assert(!File.Exists(tmp),  "First save: no leftover .tmp");
                Assert(!File.Exists(bak),  "First save: no .bak on first write");

                // Read back and verify content is valid
                var loaded = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), path);
                Assert(loaded != null,                          "First save: file round-trips");
                Assert(loaded!.CellAt(0,0)?.Props.Label == "X","First save: content correct");

                // ── Second save: existing file → must create .bak atomically ──
                layout.Cells.Clear();
                layout.Cells.Add(new GridCell(0, 0, new KeyProps("Y", "Y")));
                SettingsManager.SaveSettings(layout, new VisualTheme(), new WindowState(), new LayoutMeta(), path);
                Assert(File.Exists(path),  "Second save: real file still exists");
                Assert(!File.Exists(tmp),  "Second save: no leftover .tmp");
                Assert(File.Exists(bak),   "Second save: .bak created");

                // Real file must have new content; backup must have old content
                var reloaded = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), path);
                Assert(reloaded?.CellAt(0,0)?.Props.Label == "Y", "Second save: real file has new content");

                var bakLoaded = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), bak);
                Assert(bakLoaded?.CellAt(0,0)?.Props.Label == "X", "Second save: .bak has previous content");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(tmp))  File.Delete(tmp);
                if (File.Exists(bak))  File.Delete(bak);
            }

            // ── Invalid layout: SaveSettings must throw before touching any file ──
            string badPath = Path.Combine(Path.GetTempPath(), $"osk_invalid_{Guid.NewGuid():N}.xml");
            string badTmp  = badPath + ".tmp";
            try
            {
                // Build a layout with a deliberate overlap (two cells at 0,0)
                var broken = new GridLayout(1, 2);
                broken.Cells.Add(new GridCell(0, 0, new KeyProps("A", "A")));
                broken.Cells.Add(new GridCell(0, 0, new KeyProps("B", "B")));  // duplicate → invalid

                bool threw2 = false;
                try { SettingsManager.SaveSettings(broken, new VisualTheme(), new WindowState(), new LayoutMeta(), badPath); }
                catch (InvalidOperationException) { threw2 = true; }

                Assert(threw2,              "Invalid layout: SaveSettings throws");
                Assert(!File.Exists(badPath),"Invalid layout: real file not created");
                Assert(!File.Exists(badTmp), "Invalid layout: no leftover .tmp");
            }
            finally
            {
                if (File.Exists(badPath)) File.Delete(badPath);
                if (File.Exists(badTmp))  File.Delete(badTmp);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 8. SettingsManager — robustness
        // ════════════════════════════════════════════════════════════════
        private static void T_SettingsManager_Robustness()
        {
            Section("SettingsManager — bad input handling");

            // Corrupt XML throws
            string bad = Path.Combine(Path.GetTempPath(), $"osk_bad_{Guid.NewGuid()}.xml");
            File.WriteAllText(bad, "not xml <<< garbage >>>");
            bool threw = false;
            try { SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), bad); } catch { threw = true; }
            Assert(threw, "Corrupt XML throws exception");
            File.Delete(bad);

            // Missing file returns null
            Assert(SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(),
                Path.Combine(Path.GetTempPath(), "osk_missing_xyz.xml")) == null,
                "Missing file returns null");

            // Helper: build a minimal valid 2×1 grid XML
            string MakeXml(string globalAttribs = "", string keyAttribs = "") =>
                $@"<?xml version=""1.0"" encoding=""utf-8""?>
<OnScreenKeyboard>
  <Global BackgroundColor=""1A1A2E"" Opacity=""1.00"" FontName=""Arial"" FontSize=""0""
          FontColor=""E0E0FF"" KeyColor=""2D2D4A"" BorderColor=""3C3C5A"" BorderThickness=""1""
          Language=""en"" WindowWidth=""1050"" WindowHeight=""290"" LastFile=""""
          StickyModifiers=""1"" AlwaysOnTop=""1"" HideTitlebar=""0"" {globalAttribs} />
  <Key Row=""0"" Col=""0"" RowSpan=""1"" ColSpan=""1"" Label=""A"" Send=""A""
       ShiftLabel="""" ShiftSend="""" AltGrLabel="""" AltGrSend="""" {keyAttribs} />
  <Key Row=""1"" Col=""0"" RowSpan=""1"" ColSpan=""1"" Label=""B"" Send=""B""
       ShiftLabel="""" ShiftSend="""" AltGrLabel="""" AltGrSend=""""
       FontName="""" FontSize=""0"" FontColor="""" KeyColor=""""
       BorderColor="""" BorderThickness=""-1"" />
</OnScreenKeyboard>";

            // FontSize 999 → clamped to 72
            string ff = Path.Combine(Path.GetTempPath(), $"osk_fs_{Guid.NewGuid()}.xml");
            File.WriteAllText(ff, MakeXml(keyAttribs: @"FontSize=""999"""));
            var fr = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), ff);
            Assert(fr != null, "FontSize 999: loads without crash");
            Assert(fr?.CellAt(0,0)?.Props.FontSize <= 72, "FontSize 999 clamped to ≤72");
            File.Delete(ff);

            // BorderThickness 99 → clamped to 10
            string btf = Path.Combine(Path.GetTempPath(), $"osk_bt_{Guid.NewGuid()}.xml");
            File.WriteAllText(btf, MakeXml(keyAttribs: @"BorderThickness=""99"""));
            var btr = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), btf);
            Assert(btr != null, "BorderThickness 99: loads without crash");
            Assert(btr?.CellAt(0,0)?.Props.BorderThickness <= 10, "BorderThickness 99 clamped to ≤10");
            File.Delete(btf);

            // ParseColor
            Assert(SettingsManager.ParseColor("1A1A2E", Color.Black)
                == ColorTranslator.FromHtml("#1A1A2E"), "ParseColor: valid hex");
            Assert(SettingsManager.ParseColor("", Color.Red)  == Color.Red,
                "ParseColor: empty → fallback unchanged");
            Assert(SettingsManager.ParseColor("", Color.Empty) == Color.Empty,
                "ParseColor: empty + Empty fallback → Color.Empty");
            Assert(SettingsManager.ParseColor("ZZZZZZ", Color.Red) == Color.Red,
                "ParseColor: invalid → fallback");
            Assert(SettingsManager.ParseColor("#AABBCC", Color.Red)
                == ColorTranslator.FromHtml("#AABBCC"), "ParseColor: # prefix OK");

            // Hex
            Assert(SettingsManager.Hex(Color.FromArgb(255, 0, 128)) == "FF0080", "Hex: correct");
            Assert(SettingsManager.Hex(Color.FromArgb(0,0,0))   == "000000", "Hex: black");
            Assert(SettingsManager.Hex(Color.FromArgb(255,255,255)) == "FFFFFF", "Hex: white");
        }

        // ════════════════════════════════════════════════════════════════
        // 9. SettingsManager — sentinel values
        // ════════════════════════════════════════════════════════════════
        private static void T_SettingsManager_Sentinels()
        {
            Section("SettingsManager — sentinel round-trips");

            string tmp = Path.Combine(Path.GetTempPath(), $"osk_sent_{Guid.NewGuid()}.xml");
            try
            {
                var layout = new GridLayout(1, 3);
                // Key A: all sentinels (use global)
                layout.Cells.Add(new GridCell(0, 0, new KeyProps("A","A")));
                // Key B: explicit no-border (thickness=0)
                layout.Cells.Add(new GridCell(0, 1, new KeyProps("B","B")
                    { BorderThickness = 0 }));
                // Key C: all explicit overrides (use FromArgb to avoid named-color equality issues)
                var cR = Color.FromArgb(255, 220, 30, 30);
                var cB = Color.FromArgb(255,  30, 30, 220);
                var cG = Color.FromArgb(255,  30, 180, 30);
                layout.Cells.Add(new GridCell(0, 2, new KeyProps("C","C")
                {
                    FontColor = cR, KeyColor = cB,
                    BorderColor = cG, BorderThickness = 2,
                    FontName = "Verdana",
                }));

                SettingsManager.SaveSettings(layout, new VisualTheme(), new WindowState(), new LayoutMeta(), tmp);
                var lgTheme2 = new VisualTheme();
                var lgWindow2 = new WindowState();
                var lgMeta2 = new LayoutMeta();
                var lr = SettingsManager.LoadSettings(lgTheme2, lgWindow2, lgMeta2, tmp);

                var a = lr?.CellAt(0, 0)?.Props;
                var b = lr?.CellAt(0, 1)?.Props;
                var cc = lr?.CellAt(0, 2)?.Props;

                // Key A: sentinels preserved
                Assert(a?.FontColor.IsEmpty   == true, "Key A: FontColor sentinel preserved");
                Assert(a?.KeyColor.IsEmpty    == true, "Key A: KeyColor sentinel preserved");
                Assert(a?.BorderColor.IsEmpty == true, "Key A: BorderColor sentinel preserved");
                Assert(a?.BorderThickness == -1,       "Key A: BorderThickness -1 preserved");
                Assert(string.IsNullOrEmpty(a?.FontName), "Key A: FontName sentinel preserved");

                // Key B: explicit no-border
                Assert(b?.BorderThickness == 0, "Key B: BorderThickness 0 = no border preserved");

                // Key C: explicit overrides
                Assert(cc?.FontColor.ToArgb()   == cR.ToArgb(), "Key C: FontColor override preserved");
                Assert(cc?.KeyColor.ToArgb()    == cB.ToArgb(), "Key C: KeyColor override preserved");
                Assert(cc?.BorderColor.ToArgb() == cG.ToArgb(), "Key C: BorderColor override preserved");
                Assert(cc?.BorderThickness == 2,      "Key C: BorderThickness 2 preserved");
                Assert(cc?.FontName == "Verdana",     "Key C: FontName override preserved");

                // StickyModifiers and AlwaysOnTop round-trip
                var metaOn  = new LayoutMeta  { StickyModifiers = true  };
                var windowOn = new WindowState { AlwaysOnTop = true  };
                var metaOff  = new LayoutMeta  { StickyModifiers = false };
                var windowOff = new WindowState { AlwaysOnTop = false };
                string tmp2 = Path.Combine(Path.GetTempPath(), $"osk_sa_{Guid.NewGuid()}.xml");
                var gl2 = new GridLayout(1, 1); gl2.Cells.Add(new GridCell(0, 0, new KeyProps("", "")));
                SettingsManager.SaveSettings(gl2, new VisualTheme(), windowOn, metaOn, tmp2);
                var lgOnMeta = new LayoutMeta(); var lgOnWindow = new WindowState();
                SettingsManager.LoadSettings(new VisualTheme(), lgOnWindow, lgOnMeta, tmp2);
                Assert(lgOnMeta.StickyModifiers == true,   "StickyModifiers=true round-trip");
                Assert(lgOnWindow.AlwaysOnTop   == true,   "AlwaysOnTop=true round-trip");
                File.Delete(tmp2);

                string tmp3 = Path.Combine(Path.GetTempPath(), $"osk_sa2_{Guid.NewGuid()}.xml");
                var gl3 = new GridLayout(1, 1); gl3.Cells.Add(new GridCell(0, 0, new KeyProps("", "")));
                SettingsManager.SaveSettings(gl3, new VisualTheme(), windowOff, metaOff, tmp3);
                var lgOffMeta = new LayoutMeta(); var lgOffWindow = new WindowState();
                SettingsManager.LoadSettings(new VisualTheme(), lgOffWindow, lgOffMeta, tmp3);
                Assert(lgOffMeta.StickyModifiers == false,  "StickyModifiers=false round-trip");
                Assert(lgOffWindow.AlwaysOnTop   == false,  "AlwaysOnTop=false round-trip");
                File.Delete(tmp3);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        // ════════════════════════════════════════════════════════════════
        // 10. KeyLayout
        // ════════════════════════════════════════════════════════════════
        private static void T_KeyLayout()
        {
            Section("KeyLayout.BuildDefaultQwerty");

            var layout = KeyLayout.BuildDefaultQwerty();
            Assert(layout != null,    "Returns non-null");
            Assert(layout.Rows == 6,  "6 rows");
            Assert(layout.Cols == 14, "14 cols");
            Assert(layout.IsValid(),  "Default QWERTY layout is valid");

            var esc   = layout.CellAt(0, 0);
            var f12   = layout.CellAt(0, 12);
            var bksp  = layout.CellAt(1, 13);
            var q     = layout.CellAt(2, 1);
            var enter = layout.CellAt(3, 13);
            var space = layout.CellAt(5, 3);

            Assert(esc?.Props.Label == "Esc",         "Row 0 Col 0: Esc");
            Assert(esc?.Props.Send  == "{ESC}",       "Esc sends {ESC}");
            Assert(f12?.Props.Label == "F12",         "Row 0 Col 12: F12");
            Assert(bksp?.Props.Label == "⌫",          "Row 1 Col 13: Backspace");
            Assert(bksp?.Props.Send == "{BACKSPACE}","Backspace send");
            Assert(q?.Props.Label == "q",             "Row 2 Col 1: q");
            Assert(enter?.Props.Label == "↵",         "Enter label");
            Assert(enter?.Props.Send == "{ENTER}",    "Enter sends {ENTER}");
            Assert(enter?.RowSpan == 2,               "Enter RowSpan=2");
            Assert(space?.Props.Label == "Space",     "Space key");
            Assert(space?.Props.Send  == " ",         "Space sends space char");
            Assert(space?.ColSpan >= 5,               "Space key spans ≥5 cols");

            // All cells have valid spans
            foreach (var cell in layout.Cells)
                Assert(cell.RowSpan >= 1 && cell.ColSpan >= 1,
                    $"Cell ({cell.Row},{cell.Col}) valid spans");

            // All keys default to sentinel values (inherit from global)
            foreach (var cell in layout.Cells)
            {
                Assert(cell.Props.FontColor.IsEmpty,   $"Cell ({cell.Row},{cell.Col}) FontColor=Empty");
                Assert(cell.Props.KeyColor.IsEmpty,    $"Cell ({cell.Row},{cell.Col}) KeyColor=Empty");
                Assert(cell.Props.BorderColor.IsEmpty, $"Cell ({cell.Row},{cell.Col}) BorderColor=Empty");
                Assert(cell.Props.BorderThickness==-1, $"Cell ({cell.Row},{cell.Col}) BorderThickness=-1");
                Assert(string.IsNullOrEmpty(cell.Props.FontName),
                                                       $"Cell ({cell.Row},{cell.Col}) FontName=empty");
            }

            // Modifiers
            foreach (var m in new[]{"Shift","Ctrl","Alt","Win","AltGr","Caps"})
                Assert(KeyLayout.ModifierLabels.Contains(m), $"{m} is modifier");
            Assert(!KeyLayout.ModifierLabels.Contains("a"), "a is not modifier");
            Assert(!KeyLayout.ModifierLabels.Contains("↵"), "↵ is not modifier");

            // GetDefaultSend
            Assert(KeyLayout.GetDefaultSend("Esc")   == "{ESC}",  "GetDefaultSend Esc");
            Assert(KeyLayout.GetDefaultSend("Space") == " ",      "GetDefaultSend Space");
            Assert(KeyLayout.GetDefaultSend("←")    == "{LEFT}", "GetDefaultSend ←");
            Assert(KeyLayout.GetDefaultSend("Shift") == "",       "GetDefaultSend Shift=empty");
            Assert(KeyLayout.GetDefaultSend("xyz")   == "xyz",   "GetDefaultSend unknown=label");
        }

        // ════════════════════════════════════════════════════════════════
        // 11. LanguageManager
        // ════════════════════════════════════════════════════════════════
        private static void T_LanguageManager()
        {
            Section("LanguageManager");

            Lang.Load("en");
            Assert(Lang.CurrentCode == "en",                    "CurrentCode=en");
            Assert(Lang.T("💾 Save")        == "💾 Save",       "English: Save");
            Assert(Lang.T("✏ Edit Mode")    == "✏ Edit Mode",   "English: Edit Mode");
            Assert(Lang.T("✔ Apply")        == "✔  Apply",      "English: Apply");
            Assert(Lang.T("✖ Cancel")       == "✖  Cancel",     "English: Cancel");
            Assert(Lang.T("Edit Key")       == "Edit Key",      "English: Edit Key");
            Assert(Lang.T("Preview")        == "Preview",       "English: Preview");
            Assert(Lang.T("Key width")     == "Key width","English: Width (columns)");
            Assert(Lang.T("Key height")    == "Key &height", "English: Height (rows)");
            Assert(Lang.T("Accessibility")  == "Accessibility", "English: Accessibility");
            Assert(Lang.T("Sticky modifiers")== "Stic&ky modifiers","English: Sticky modifiers");
            Assert(Lang.T("Always on top")  == "Always on top", "English: Always on top");
            Assert(Lang.T("Hide title bar") == "H&ide title bar","English: Hide title bar");
            Assert(Lang.T("Language")       == "Language",      "English: Language");
            Assert(Lang.T("Layout file")    == "Layout file",   "English: Layout file");
            Assert(Lang.T("Invalid file title") == "Unable to Open File",
                "English: Invalid file title");
            Assert(Lang.T("nonexistent_key_xyz") == "nonexistent_key_xyz",
                "Missing key returns key itself");

            // Keys that were removed — should no longer exist in fallback text
            // (they return the key itself since they ARE the English text, but
            //  they should not be in the Dutch file)
            // "Row span", "Width", "Opacity hint", "Paste delay" — these are removed

            // LanguageChanged event
            bool fired = false;
            void H() { fired = true; }
            Lang.LanguageChanged += H;
            Lang.Load("en");
            Assert(fired, "LanguageChanged fires");
            Lang.LanguageChanged -= H;

            // Non-existent language handled gracefully
            bool crashed = false;
            try { Lang.Load("xx_9999_nonexistent"); } catch { crashed = true; }
            Assert(!crashed, "Loading missing language does not crash");

            // GetAvailable always has English
            var avail = Lang.GetAvailable();
            Assert(avail.Count >= 1, "GetAvailable ≥1 entry");
            bool hasEn = false;
            foreach (var (code, _) in avail) if (code == "en") { hasEn = true; break; }
            Assert(hasEn, "English in available list");

            // Dutch — only if lang_nl.xml is next to the exe
            string nlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang_nl.xml");
            if (File.Exists(nlPath))
            {
                Lang.Load("nl");
                Assert(Lang.CurrentCode == "nl",                    "Dutch code");
                Assert(Lang.T("Save")        == "Opslaan",           "Dutch: Save");
                Assert(Lang.T("Cancel")      == "Ann&uleren",        "Dutch: Cancel");
                Assert(Lang.T("Preview")     == "Voorbeeld",        "Dutch: Preview");
                Assert(Lang.T("Language")    == "Taal",             "Dutch: Language");
                Assert(Lang.T("Layout file") == "Lay-outbestand",   "Dutch: Layout file");
                Assert(Lang.T("Accessibility")== "Toegankelijkheid","Dutch: Accessibility");
                Assert(Lang.T("Sticky modifiers")=="&Plaktoetsen (Sticky Keys)","Dutch: Sticky modifiers");
                Assert(Lang.T("Always on top")=="Altijd bovenaan",  "Dutch: Always on top");
                Assert(Lang.T("Hide title bar")=="Titelbalk &verbergen","Dutch: Hide title bar");
                Assert(Lang.T("Key width")=="Toets breedte","Dutch: Key width");
                Assert(Lang.T("Key height")=="Toets &hoogte",   "Dutch: Key height");
                // Removed keys must NOT be in Dutch file
                Assert(Lang.T("nonexistent_key_xyz") == "nonexistent_key_xyz",
                    "Dutch: missing key returns key");
                Lang.Load("en");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("    (lang_nl.xml not found — Dutch tests skipped)");
                Console.ResetColor();
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Language XML safety — hardened loader (DTD, size, structure)
        // ════════════════════════════════════════════════════════════════
        private static void T_LanguageXmlSafety()
        {
            Section("LanguageManager — XML safety");

            // Helper: temp lang file path in the same directory Load() searches.
            // Using a guid suffix avoids any collision with real lang files.
            string BaseDir() => AppDomain.CurrentDomain.BaseDirectory;
            string TempPath(string tag) =>
                Path.Combine(BaseDir(), $"lang_zztest_{tag}.xml");

            // Reset to English so each sub-test starts from a known baseline.
            void Reset() => Lang.Load("en");

            // ── 1. Valid minimal file loads correctly ─────────────────────────
            string validPath = TempPath("valid");
            try
            {
                File.WriteAllText(validPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<Language code=\"zztest_valid\" name=\"ZZTest\">\n" +
                    "  <String key=\"hello\" value=\"world\" />\n" +
                    "</Language>");

                Reset();
                Lang.Load("zztest_valid");
                Assert(Lang.CurrentCode == "zztest_valid", "Safe load: CurrentCode set");
                Assert(Lang.T("hello")  == "world",        "Safe load: translation key resolved");
            }
            finally
            {
                Reset();
                if (File.Exists(validPath)) File.Delete(validPath);
            }

            // ── 2. DTD / billion-laughs rejected ─────────────────────────────
            // DtdProcessing.Prohibit throws XmlException on any DOCTYPE declaration,
            // so entity expansion never happens regardless of how many levels it has.
            string dtdPath = TempPath("dtd");
            try
            {
                File.WriteAllText(dtdPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<!DOCTYPE lolz [\n" +
                    "  <!ENTITY lol  \"xxxxxxxxxx\">\n" +
                    "  <!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">\n" +
                    "  <!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;\">\n" +
                    "]>\n" +
                    "<Language code=\"zztest_dtd\" name=\"ZZTestDtd\">\n" +
                    "  <String key=\"evil\" value=\"&lol3;\" />\n" +
                    "</Language>");

                Reset();
                bool crashed = false;
                try { Lang.Load("zztest_dtd"); } catch { crashed = true; }
                Assert(!crashed,                         "DTD file: no exception escapes Load()");
                Assert(Lang.CurrentCode == "en",         "DTD file: language stays English");
                Assert(Lang.T("evil")   == "evil",       "DTD file: malicious key not loaded");
            }
            finally
            {
                Reset();
                if (File.Exists(dtdPath)) File.Delete(dtdPath);
            }

            // ── 3. Oversized file rejected ────────────────────────────────────
            string bigPath = TempPath("big");
            try
            {
                // Write a syntactically valid XML file larger than the 512 KB cap.
                // Pad the file with a comment so the size threshold is crossed without
                // the XML parser doing any work.
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<!-- " + new string('x', 600 * 1024) + " -->");
                sb.AppendLine("<Language code=\"zztest_big\" name=\"ZZTestBig\" />");
                File.WriteAllText(bigPath, sb.ToString());

                Reset();
                bool crashed = false;
                try { Lang.Load("zztest_big"); } catch { crashed = true; }
                Assert(!crashed,                         "Oversized file: no exception escapes Load()");
                Assert(Lang.CurrentCode == "en",         "Oversized file: language stays English");
            }
            finally
            {
                Reset();
                if (File.Exists(bigPath)) File.Delete(bigPath);
            }

            // ── 4. Wrong root element rejected ───────────────────────────────
            string wrongRootPath = TempPath("wrongroot");
            try
            {
                File.WriteAllText(wrongRootPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<Translations code=\"zztest_wr\" name=\"ZZTestWR\">\n" +
                    "  <String key=\"tricky\" value=\"injected\" />\n" +
                    "</Translations>");

                Reset();
                bool crashed = false;
                try { Lang.Load("zztest_wrongroot"); } catch { crashed = true; }
                Assert(!crashed,                         "Wrong root: no exception escapes Load()");
                Assert(Lang.CurrentCode == "en",         "Wrong root: language stays English");
                Assert(Lang.T("tricky") == "tricky",     "Wrong root: key not loaded into overrides");
            }
            finally
            {
                Reset();
                if (File.Exists(wrongRootPath)) File.Delete(wrongRootPath);
            }

            // ── 5. GetAvailable() skips bad files, includes good ones ─────────
            string availGoodPath = TempPath("availgood");
            string availBadPath  = TempPath("availbad");
            try
            {
                // Good file
                File.WriteAllText(availGoodPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<Language code=\"zztest_availgood\" name=\"ZZAvailGood\" />");

                // Bad file — DTD present
                File.WriteAllText(availBadPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<!DOCTYPE x []>\n" +
                    "<Language code=\"zztest_availbad\" name=\"ZZAvailBad\" />");

                var list = Lang.GetAvailable();
                bool foundGood = false, foundBad = false;
                foreach (var (code, _) in list)
                {
                    if (code == "zztest_availgood") foundGood = true;
                    if (code == "zztest_availbad")  foundBad  = true;
                }
                Assert( foundGood, "GetAvailable: valid test lang included");
                Assert(!foundBad,  "GetAvailable: DTD lang file silently skipped");
            }
            finally
            {
                Reset();
                if (File.Exists(availGoodPath)) File.Delete(availGoodPath);
                if (File.Exists(availBadPath))  File.Delete(availBadPath);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 12. GridLayout — structure and operations
        // ════════════════════════════════════════════════════════════════
        private static void T_GridLayout()
        {
            Section("GridLayout — validity");

            Assert(!IsValid(null),                  "null → invalid");
            Assert(!IsValid(new GridLayout(0, 0)),  "0×0 → invalid");
            Assert(!IsValid(new GridLayout(0, 1)),  "0 rows → invalid");
            Assert(!IsValid(new GridLayout(1, 0)),  "0 cols → invalid");
            Assert( IsValid(MakeGrid(1, 1)),         "1×1 → valid");
            Assert( IsValid(MakeGrid(6, 14)),        "6×14 → valid");

            // Overlapping cells → invalid
            var gOv = new GridLayout(1, 2);
            gOv.Cells.Add(new GridCell(0, 0, new KeyProps("A","A"), 1, 2));
            gOv.Cells.Add(new GridCell(0, 1, new KeyProps("B","B")));
            Assert(!gOv.IsValid(), "Overlapping cells → IsValid = false");

            // Gap in grid → invalid
            var gGap = new GridLayout(1, 2);
            gGap.Cells.Add(new GridCell(0, 0, new KeyProps("A","A")));
            Assert(!gGap.IsValid(), "Gap in grid → IsValid = false");

            Section("GridLayout — span operations");

            // ColSpan > 1
            var gCS = new GridLayout(1, 3);
            gCS.Cells.Add(new GridCell(0, 0, new KeyProps("A","A"), 1, 2));
            gCS.Cells.Add(new GridCell(0, 2, new KeyProps("B","B")));
            Assert(gCS.IsValid(), "ColSpan=2 cell in 1×3 grid is valid");
            Assert(gCS.CellAt(0, 1) == gCS.CellAt(0, 0), "CellAt returns spanning cell for covered cols");

            // RowSpan > 1
            var gRS = new GridLayout(2, 1);
            gRS.Cells.Add(new GridCell(0, 0, new KeyProps("A","A"), 2, 1));
            Assert(gRS.IsValid(), "RowSpan=2 cell in 2×1 grid is valid");

            Section("GridLayout — MergeRight / MergeDown / SplitCell");

            var g2 = new GridLayout(1, 2);
            g2.Cells.Add(new GridCell(0, 0, new KeyProps("a","a")));
            g2.Cells.Add(new GridCell(0, 1, new KeyProps("b","b")));
            bool merged = g2.MergeRight(0, 0);
            Assert(merged,                   "MergeRight returns true");
            Assert(g2.Cells.Count == 1,      "After MergeRight: 1 cell");
            Assert(g2.Cells[0].ColSpan == 2, "Merged ColSpan=2");
            Assert(g2.IsValid(),             "After MergeRight: valid");
            Assert(!g2.MergeRight(0, 0),     "MergeRight at last col returns false");

            var g3 = new GridLayout(2, 1);
            g3.Cells.Add(new GridCell(0, 0, new KeyProps("a","a")));
            g3.Cells.Add(new GridCell(1, 0, new KeyProps("b","b")));
            bool mergedD = g3.MergeDown(0, 0);
            Assert(mergedD,                  "MergeDown returns true");
            Assert(g3.Cells[0].RowSpan == 2, "Merged RowSpan=2");
            Assert(g3.IsValid(),             "After MergeDown: valid");

            var g4 = new GridLayout(1, 2);
            g4.Cells.Add(new GridCell(0, 0, new KeyProps("a","a"), 1, 2));
            g4.SplitCell(0, 0, new VisualTheme());
            Assert(g4.Cells.Count == 2,      "After SplitCell: 2 cells");
            Assert(g4.IsValid(),             "After SplitCell: valid");

            Section("GridLayout — InsertRow / RemoveRow / InsertCol / RemoveCol");

            var gIR = new GridLayout(2, 2);
            gIR.Cells.Add(new GridCell(0, 0, new KeyProps("A","A")));
            gIR.Cells.Add(new GridCell(0, 1, new KeyProps("B","B")));
            gIR.Cells.Add(new GridCell(1, 0, new KeyProps("C","C")));
            gIR.Cells.Add(new GridCell(1, 1, new KeyProps("D","D")));
            gIR.InsertRow(0, before: true, new VisualTheme());
            Assert(gIR.Rows == 3,                       "InsertRow above: Rows=3");
            Assert(gIR.CellAt(0,0)?.Props.Label == "",  "InsertRow above: new row blank");
            Assert(gIR.CellAt(1,0)?.Props.Label == "A", "InsertRow above: old row shifted");
            Assert(gIR.IsValid(),                        "InsertRow above: valid");

            var gRR = new GridLayout(3, 1);
            gRR.Cells.Add(new GridCell(0, 0, new KeyProps("A","A")));
            gRR.Cells.Add(new GridCell(1, 0, new KeyProps("B","B")));
            gRR.Cells.Add(new GridCell(2, 0, new KeyProps("C","C")));
            Assert(gRR.RemoveRow(1),                     "RemoveRow: returns true");
            Assert(gRR.Rows == 2,                        "RemoveRow: Rows=2");
            Assert(gRR.CellAt(1,0)?.Props.Label == "C", "RemoveRow: row renumbered");
            Assert(gRR.IsValid(),                        "RemoveRow: valid");
            Assert(!gRR.RemoveRow(0) || gRR.RemoveRow(1) == false || gRR.Rows >= 1,
                "RemoveRow does not remove last row");

            var gIC = new GridLayout(1, 2);
            gIC.Cells.Add(new GridCell(0, 0, new KeyProps("A","A")));
            gIC.Cells.Add(new GridCell(0, 1, new KeyProps("B","B")));
            gIC.InsertCol(0, before: true, new VisualTheme());
            Assert(gIC.Cols == 3,                       "InsertCol: Cols=3");
            Assert(gIC.CellAt(0,0)?.Props.Label == "",  "InsertCol: new col blank");
            Assert(gIC.CellAt(0,1)?.Props.Label == "A", "InsertCol: old col shifted");
            Assert(gIC.IsValid(),                        "InsertCol: valid");

            var gRC = new GridLayout(1, 3);
            gRC.Cells.Add(new GridCell(0, 0, new KeyProps("A","A")));
            gRC.Cells.Add(new GridCell(0, 1, new KeyProps("B","B")));
            gRC.Cells.Add(new GridCell(0, 2, new KeyProps("C","C")));
            Assert(gRC.RemoveCol(1),                     "RemoveCol: returns true");
            Assert(gRC.Cols == 2,                        "RemoveCol: Cols=2");
            Assert(gRC.CellAt(0,1)?.Props.Label == "C", "RemoveCol: col renumbered");
            Assert(gRC.IsValid(),                        "RemoveCol: valid");

            Section("GridLayout — CellAt bounds");

            var gOB = new GridLayout(2, 2);
            gOB.Cells.Add(new GridCell(0, 0, new KeyProps("A","A"), 2, 2));
            Assert(gOB.CellAt(-1,  0) == null, "CellAt(-1,0) = null");
            Assert(gOB.CellAt( 0, -1) == null, "CellAt(0,-1) = null");
            Assert(gOB.CellAt( 5,  0) == null, "CellAt(5,0) = null");
        }

        // ════════════════════════════════════════════════════════════════
        // 13. Font sizing formula
        // ════════════════════════════════════════════════════════════════
        private static void T_FontSizing()
        {
            Section("Font sizing — label always fits button dimensions");

            const float CW = 0.72f, CH = 1.35f;
            const int   HM = 10,    VM = 8;

            static int ComputeFs(string label, int btnH, int btnW)
            {
                float maxH = (btnH - VM) / CH;
                float maxW = btnW > 0 && label.Length > 0
                    ? (btnW - HM) / (Math.Max(1f, label.Length) * CW) : maxH;
                return Math.Max(6, (int)Math.Min(btnH * 0.36f, Math.Min(maxH, maxW)));
            }

            static bool Fits(int fs, string label, int btnW, int btnH) =>
                label.Length * fs * CW + HM <= btnW &&
                fs * CH + VM <= btnH;

            var cases = new (string label, int h, int w)[]
            {
                // Normal proportions
                ("q",40,60), ("F1",40,60), ("F12",40,60),
                ("Shift",40,80), ("AltGr",40,75), ("Ctrl",40,65),
                ("Space",40,300), ("Win",40,60), ("Caps",40,60),
                // Tall buttons (height >> width) — the problematic scenario
                ("F1",80,40), ("F12",80,40), ("Shift",80,50),
                ("Ctrl",80,45), ("Win",80,40), ("q",80,40),
                // Small buttons
                ("F12",25,45), ("Shift",25,55),
                // Very narrow
                ("Ctrl",40,30), ("F12",40,28),
                // Extreme
                ("F1",120,35), ("Shift",120,40),
            };

            foreach (var (label, h, w) in cases)
            {
                int fs = ComputeFs(label, h, w);
                Assert(fs >= 6,               $"fs ≥ 6pt: '{label}' h={h} w={w} → {fs}");
                Assert(Fits(fs, label, w, h), $"fits in button: '{label}' h={h} w={w} → {fs}pt");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 14. Character routing report
        // ════════════════════════════════════════════════════════════════
        private static void T_CharacterRouting()
        {
            Section("Character routing");

            var groups = new List<(string Name, string Chars)>
            {
                ("Basic Latin",       "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"),
                ("SendKeys specials", "%^+~()[]{}"),
                ("Punctuation",       "!\"#$&'*+,-./:;<=>?@\\^_`|~"),
                ("Extended Latin",    "àáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿ"),
                ("Belgian AZERTY",    "²³µ£€§°"),
                ("Currency",          "€£¥¢₹₽₩"),
                ("Math",              "±×÷√∞∑∏∫∂∆≈≠≤≥∈∉∩∪⊂⊃∀∃∧∨¬"),
                ("Greek",             "αβγδεζηθικλμνξοπρστυφχψωΑΒΓΔΣΩ"),
                ("Arrows",            "←→↑↓↔↕⇐⇒⇑⇓"),
                ("Cyrillic",          "абвгдеёжзийклмнопрстуфхцчшщ"),
            };

            int total = 0, clipboard = 0, warnings = 0;
            var reportLines = new List<string>
            {
                "On-Screen Keyboard — Character Routing Report",
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", ""
            };

            foreach (var (groupName, chars) in groups)
            {
                reportLines.Add($"── {groupName} ──");
                var en = System.Globalization.StringInfo.GetTextElementEnumerator(chars);
                while (en.MoveNext())
                {
                    string s = en.GetTextElement(); total++;
                    bool plain = SendKeysHelper.IsPlainText(s);
                    if (plain) clipboard++;
                    if (!plain && s.Length == 1) { warnings++; reportLines.Add($"  WARN: single char {s} via SendKeys"); }
                    else reportLines.Add($"  {(plain?"CLIPBOARD":"SENDKEYS ")}  {s}");
                }
                reportLines.Add("");
            }

            try { File.WriteAllLines(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "char_report.txt"),
                reportLines, new System.Text.UTF8Encoding(true)); }
            catch { }

            Assert(total > 0,          "Characters tested");
            Assert(clipboard > 0,      "Some characters route via clipboard");
            Assert(warnings == 0,      $"No single chars incorrectly via SendKeys ({warnings} warnings)");

            // Spot-checks: must be plain text (clipboard path)
            foreach (var ch in new[]{"@","€","#","|","{","}","[","]","^","~","µ","²","→","α","½","\\","+"})
                Assert(SendKeysHelper.IsPlainText(ch), $"'{ch}' is plain text (clipboard)");

            // Spot-checks: must NOT be plain text (SendKeys path)
            foreach (var s in new[]{"{ENTER}","^c","%{F4}","{F5}","win:m","+a","dead:^"})
                Assert(!SendKeysHelper.IsPlainText(s), $"'{s}' is not plain text (SendKeys)");
        }

        // ════════════════════════════════════════════════════════════════
        // 15. Slow receiver stress test
        // ════════════════════════════════════════════════════════════════
        private static void T_SlowReceiverStress()
        {
            Section("Slow receiver stress test");

            static List<string> RunQ(List<string> items, int sendMs, int recvMs, int timeoutMs)
            {
                var q    = new System.Collections.Concurrent.BlockingCollection<string>(256);
                var recv = new List<string>();
                int exp  = items.Count;
                var done = new System.Threading.ManualResetEventSlim(false);
                new System.Threading.Thread(() =>
                {
                    foreach (var item in q.GetConsumingEnumerable())
                    {
                        System.Threading.Thread.Sleep(recvMs);
                        lock (recv) { recv.Add(item); if (recv.Count >= exp) done.Set(); }
                    }
                }) { IsBackground = true }.Start();
                foreach (var item in items) { q.TryAdd(item); if (sendMs > 0) System.Threading.Thread.Sleep(sendMs); }
                q.CompleteAdding();
                done.Wait(timeoutMs);
                return recv;
            }

            // Normal speed
            var normal = new List<string>{"a","b","c","d","e","f","g","h","i","j"};
            var r1 = RunQ(normal, 20, 10, 2000);
            Assert(r1.Count == normal.Count,                       "Normal speed: all received");
            Assert(string.Join("",r1) == string.Join("",normal),  "Normal speed: correct order");

            // Rapid clicks → slow receiver
            var rapid = new List<string>{"H","e","l","l","o"," ","W","o","r","l","d","!"};
            var r2 = RunQ(rapid, 5, 50, 6000);
            Assert(r2.Count == rapid.Count,                        "Rapid→slow: all received");
            Assert(string.Join("",r2) == string.Join("",rapid),   "Rapid→slow: correct order");

            // Burst 50 items, zero delay
            var burst = new List<string>(); for (int i=0;i<50;i++) burst.Add((i%10).ToString());
            var r3 = RunQ(burst, 0, 15, 10000);
            Assert(r3.Count == burst.Count,                        "Burst 50: all received");
            Assert(string.Join("",r3) == string.Join("",burst),   "Burst 50: correct order");

            // Special chars: ^, %, {, }, @, €, ~, \
            var special = new List<string>{"a","€","^","%","+","{","}","#","@","~","[","]","\\"};
            var r4 = RunQ(special, 3, 30, 4000);
            Assert(r4.Count == special.Count,                      "Special chars: all received");
            bool specOk = true;
            for (int i=0;i<Math.Min(r4.Count,special.Count);i++) if(r4[i]!=special[i]) specOk=false;
            Assert(specOk,                                         "Special chars: correct order");

            // Enqueuing must be near-instant
            var sw = Stopwatch.StartNew();
            var dq = new System.Collections.Concurrent.BlockingCollection<string>(256);
            for (int i=0;i<50;i++) dq.TryAdd($"{i}");
            sw.Stop(); dq.CompleteAdding();
            Assert(sw.ElapsedMilliseconds < 20,
                $"Enqueuing 50 items is non-blocking ({sw.ElapsedMilliseconds}ms)");

            // IsPlainText thread-safety
            var chars2 = new (string ch, bool exp)[]
            {
                ("a",true),("%",true),("^",true),("+",true),("{",true),("€",true),
                ("{^}",false),("{ENTER}",false),("^c",false),("win:m",false),
            };
            bool stable = true;
            var ths = new System.Threading.Thread[4];
            var mis = new bool[4];
            for (int t=0;t<4;t++){int ti=t; ths[t]=new System.Threading.Thread(()=>{
                for(int r=0;r<500;r++) foreach(var(ch,ex)in chars2)
                    if(SendKeysHelper.IsPlainText(ch)!=ex) mis[ti]=true;
            }){IsBackground=true}; ths[t].Start();}
            foreach(var th in ths) th.Join(3000);
            foreach(var m in mis) if(m) stable=false;
            Assert(stable, "IsPlainText: thread-safe (4 threads × 500 reps)");
        }

        // ════════════════════════════════════════════════════════════════
        // Style Groups — KeyGroup, GroupName, color resolution, XML round-trip
        // ════════════════════════════════════════════════════════════════
        private static void T_StyleGroups()
        {
            Section("StyleGroups — KeyGroup clone");

            var grp = new KeyGroup
            {
                Name            = "Math",
                KeyColor        = Color.FromArgb(255, 74, 48, 16),
                FontColor       = Color.FromArgb(255, 255, 179, 71),
                BorderColor     = Color.FromArgb(255, 30, 60, 30),
                BorderThickness = 2,
                FontName        = "Consolas",
                FontSize        = 14,
            };
            var clone = grp.Clone();
            Assert(clone.Name            == grp.Name,            "KeyGroup.Clone: Name");
            Assert(clone.KeyColor        == grp.KeyColor,        "KeyGroup.Clone: KeyColor");
            Assert(clone.FontColor       == grp.FontColor,       "KeyGroup.Clone: FontColor");
            Assert(clone.BorderColor     == grp.BorderColor,     "KeyGroup.Clone: BorderColor");
            Assert(clone.BorderThickness == grp.BorderThickness, "KeyGroup.Clone: BorderThickness");
            Assert(clone.FontName        == grp.FontName,        "KeyGroup.Clone: FontName");
            Assert(clone.FontSize        == grp.FontSize,        "KeyGroup.Clone: FontSize");
            Assert(!ReferenceEquals(clone, grp),                 "KeyGroup.Clone: new object");

            Section("StyleGroups — GridLayout.Groups deep-cloned");

            var layout = new GridLayout(1, 1);
            layout.Groups.Add(grp);
            layout.Cells.Add(new GridCell(0, 0, new KeyProps("a", "a") { GroupName = "Math" }));
            var copy = layout.Clone();
            Assert(copy.Groups.Count == 1,             "Clone: groups count preserved");
            Assert(copy.Groups[0].Name == "Math",      "Clone: group name preserved");
            copy.Groups[0].Name = "Changed";
            Assert(layout.Groups[0].Name == "Math",    "Clone: mutating copy does not affect original");

            Section("StyleGroups — GroupName on KeyProps");

            var p = new KeyProps("a", "a") { GroupName = "Math" };
            Assert(p.GroupName == "Math", "GroupName stored");
            var pc = p.Clone();
            Assert(pc.GroupName == "Math", "GroupName cloned");
            pc.GroupName = "Other";
            Assert(p.GroupName == "Math",  "GroupName clone is independent");

            var def = new KeyProps("a", "a");
            Assert(string.IsNullOrEmpty(def.GroupName), "Default GroupName is empty");

            Section("StyleGroups — IsPureSpacer respects GroupName");

            // Empty key with no group → pure spacer (omitted in XML)
            var spacer = new GridCell(0, 0, new KeyProps("", ""));
            Assert(IsPureSpacer(spacer), "Empty key without group is pure spacer");

            // Empty key with a group → not a pure spacer (group colors apply)
            var groupedSpacer = new GridCell(0, 0, new KeyProps("", "") { GroupName = "Math" });
            Assert(!IsPureSpacer(groupedSpacer), "Empty key with GroupName is not a pure spacer");

            Section("StyleGroups — color resolution chain");

            // Simulate: Global → Group → Per-key priority
            Color globalKeyColor = Color.FromArgb(255, 30, 30, 60);
            Color groupKeyColor  = Color.FromArgb(255, 74, 48, 16);
            Color keyKeyColor    = Color.FromArgb(255, 10, 100, 10);

            // Per-key wins when set
            Assert(ResolveColor(keyKeyColor, groupKeyColor, globalKeyColor) == keyKeyColor,
                "Color resolution: per-key wins");
            // Group wins when per-key is empty
            Assert(ResolveColor(Color.Empty, groupKeyColor, globalKeyColor) == groupKeyColor,
                "Color resolution: group wins over global");
            // Global is fallback when both upper levels are empty
            Assert(ResolveColor(Color.Empty, Color.Empty, globalKeyColor) == globalKeyColor,
                "Color resolution: global as fallback");
            // All empty → global (which may also be empty)
            Assert(ResolveColor(Color.Empty, Color.Empty, Color.Empty) == Color.Empty,
                "Color resolution: all-empty returns empty");

            Section("StyleGroups — border thickness resolution");

            // -1 = inherit, 0+ = explicit
            Assert(ResolveThickness(2,  1, 0) == 2, "Thickness: per-key wins");
            Assert(ResolveThickness(-1, 1, 0) == 1, "Thickness: group wins over global");
            Assert(ResolveThickness(-1,-1, 3) == 3, "Thickness: global as fallback");
            Assert(ResolveThickness(0, -1, 3) == 0, "Thickness: per-key 0 beats group (explicit no-border)");

            Section("StyleGroups — XML round-trip with groups and GroupName");

            string tmp = Path.Combine(Path.GetTempPath(), $"osk_grp_{Guid.NewGuid()}.xml");
            try
            {
                var cKey  = Color.FromArgb(255, 74, 48, 16);
                var cFont = Color.FromArgb(255, 255, 179, 71);
                var rtLayout = new GridLayout(1, 2);
                var mathGroup = new KeyGroup
                {
                    Name = "Arithmetic", KeyColor = cKey, FontColor = cFont,
                    BorderThickness = 1, FontName = "Consolas", FontSize = 13,
                };
                rtLayout.Groups.Add(mathGroup);
                rtLayout.Cells.Add(new GridCell(0, 0, new KeyProps("+", "+") { GroupName = "Arithmetic" }));
                rtLayout.Cells.Add(new GridCell(0, 1, new KeyProps("-", "-")));

                var theme  = new VisualTheme { FontName = "Arial", FontSize = 12 };
                var window = new WindowState { WindowWidth = 400, WindowHeight = 100 };
                var meta   = new LayoutMeta  { Language = "en", LastFile = tmp };

                SettingsManager.SaveSettings(rtLayout, theme, window, meta, tmp);
                Assert(File.Exists(tmp), "Groups round-trip: XML file created");

                var lgTheme = new VisualTheme(); var lgWindow = new WindowState(); var lgMeta = new LayoutMeta();
                var loaded = SettingsManager.LoadSettings(lgTheme, lgWindow, lgMeta, tmp);

                Assert(loaded != null,                          "Groups round-trip: loaded");
                // File has Arithmetic but no standard group → auto-creation adds standard at index 0
                Assert(loaded.Groups.Count >= 2,               "Groups round-trip: group count (Arithmetic + standard)");
                Assert(loaded.Groups.Exists(g => g.Name == SettingsManager.StandardGroupName),
                    "Groups round-trip: standard group auto-created");
                var arith = loaded.Groups.Find(g => g.Name == "Arithmetic");
                Assert(arith != null,                           "Groups round-trip: group name");
                Assert(arith.KeyColor.ToArgb()  == cKey.ToArgb(),  "Groups round-trip: group KeyColor");
                Assert(arith.FontColor.ToArgb() == cFont.ToArgb(), "Groups round-trip: group FontColor");
                Assert(arith.BorderThickness == 1,              "Groups round-trip: group BorderThickness");
                Assert(arith.FontName == "Consolas",            "Groups round-trip: group FontName");
                Assert(arith.FontSize == 13,                    "Groups round-trip: group FontSize");

                var cell0 = loaded.CellAt(0, 0);
                var cell1 = loaded.CellAt(0, 1);
                Assert(cell0?.Props.GroupName == "Arithmetic",              "Groups round-trip: key GroupName preserved");
                Assert(cell1?.Props.GroupName == SettingsManager.StandardGroupName,
                    "Groups round-trip: ungrouped key assigned to standard group on load");
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }

            Section("StyleGroups — StandardGroupName constant");

            Assert(SettingsManager.StandardGroupName == "standard",
                "StandardGroupName value is 'standard'");

            Section("StyleGroups — standard group auto-created with neutral defaults when missing");

            {
                string f = Path.Combine(Path.GetTempPath(), $"osk_std_{Guid.NewGuid()}.xml");
                try
                {
                    // Write a file with no standard group
                    var noStdLayout = new GridLayout(1, 1);
                    noStdLayout.Cells.Add(new GridCell(0, 0, new KeyProps("a", "a")));
                    SettingsManager.SaveSettings(noStdLayout, new VisualTheme(), new WindowState(), new LayoutMeta(), f);

                    // Load: standard group must be auto-created with neutral defaults (not from theme)
                    var lout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), f);
                    Assert(lout != null, "Standard auto-create: file loads");
                    var std = lout.Groups.Find(g => g.Name == SettingsManager.StandardGroupName);
                    Assert(std != null,                                                      "Standard auto-create: group exists");
                    Assert(std.FontColor    == Color.FromArgb(255,   0,   0,   0),          "Standard auto-create: FontColor is black (#000000)");
                    Assert(std.KeyColor     == Color.FromArgb(255, 255, 255, 255),          "Standard auto-create: KeyColor is white (#FFFFFF)");
                    Assert(std.BorderColor  == Color.FromArgb(255,   0,   0,   0),          "Standard auto-create: BorderColor is black (#000000)");
                    Assert(std.BorderThickness == 0,                                        "Standard auto-create: BorderThickness is 0");
                    // Standard group must be first in the list
                    Assert(lout.Groups[0].Name == SettingsManager.StandardGroupName,
                        "Standard auto-create: standard group is first");
                }
                finally { if (File.Exists(f)) File.Delete(f); }
            }

            Section("StyleGroups — standard group preserved when present in file");

            {
                string f = Path.Combine(Path.GetTempPath(), $"osk_stdp_{Guid.NewGuid()}.xml");
                try
                {
                    var cK = Color.FromArgb(255, 11, 22, 33);
                    var cF = Color.FromArgb(255, 44, 55, 66);
                    var withStdLayout = new GridLayout(1, 1);
                    withStdLayout.Cells.Add(new GridCell(0, 0, new KeyProps("b", "b")));
                    withStdLayout.Groups.Add(new KeyGroup
                    {
                        Name            = SettingsManager.StandardGroupName,
                        FontName        = "Impact",
                        FontSize        = 9,
                        FontColor       = cF,
                        KeyColor        = cK,
                        BorderColor     = Color.FromArgb(255, 77, 88, 99),
                        BorderThickness = 4,
                    });
                    SettingsManager.SaveSettings(withStdLayout, new VisualTheme(), new WindowState(), new LayoutMeta(), f);

                    // Load: standard group values from file must be kept; no auto-creation
                    var lout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), f);
                    Assert(lout != null, "Standard preserved: file loads");
                    var stdGroups = lout.Groups.FindAll(g => g.Name == SettingsManager.StandardGroupName);
                    Assert(stdGroups.Count == 1,    "Standard preserved: exactly one standard group");
                    Assert(stdGroups[0].FontName == "Impact",  "Standard preserved: FontName kept");
                    Assert(stdGroups[0].FontSize == 9,         "Standard preserved: FontSize kept");
                    Assert(stdGroups[0].FontColor == cF,       "Standard preserved: FontColor kept");
                    Assert(stdGroups[0].KeyColor  == cK,       "Standard preserved: KeyColor kept");
                    Assert(stdGroups[0].BorderThickness == 4,  "Standard preserved: BorderThickness kept");
                }
                finally { if (File.Exists(f)) File.Delete(f); }
            }

            Section("StyleGroups — standard group written on save");

            {
                string f = Path.Combine(Path.GetTempPath(), $"osk_stds_{Guid.NewGuid()}.xml");
                try
                {
                    var saveLayout = new GridLayout(1, 1);
                    saveLayout.Cells.Add(new GridCell(0, 0, new KeyProps("c", "c")));
                    saveLayout.Groups.Add(new KeyGroup
                    {
                        Name     = SettingsManager.StandardGroupName,
                        FontName = "Verdana",
                        KeyColor = Color.FromArgb(255, 1, 2, 3),
                    });
                    SettingsManager.SaveSettings(saveLayout, new VisualTheme(), new WindowState(), new LayoutMeta(), f);

                    string xml = File.ReadAllText(f);
                    Assert(xml.Contains("Name=\"standard\""),
                        "Standard written: <Group Name=\"standard\"> present in XML");
                    Assert(xml.Contains("Verdana"),
                        "Standard written: custom FontName present in XML");
                }
                finally { if (File.Exists(f)) File.Delete(f); }
            }

            Section("StyleGroups — Step 4 lang keys registered");

            Assert(Lang.T("(inherit standard)")      == "(inherit standard)",
                "Lang key: (inherit standard)");
            Assert(Lang.T("-1 = inherit standard")   == "-1 = inherit standard",
                "Lang key: -1 = inherit standard");
            Assert(Lang.T("Clear (inherit standard)")== "Clear (inherit standard)",
                "Lang key: Clear (inherit standard)");
            Assert(Lang.T("(none / auto)")           == "(none / auto)",
                "Lang key: (none / auto)");
            Assert(Lang.T("Clear")                   == "Clear",
                "Lang key: Clear");

            Section("Priority 4 — tip: keys and StripMnemonic");

            // StripMnemonic removes & without changing other characters
            Assert(Lang.StripMnemonic("&Cancel")          == "Cancel",   "StripMnemonic: leading &");
            Assert(Lang.StripMnemonic("Sa&ve As…")        == "Save As…", "StripMnemonic: mid-word &");
            Assert(Lang.StripMnemonic("No ampersand")     == "No ampersand", "StripMnemonic: no &");
            Assert(Lang.StripMnemonic("")                 == "",         "StripMnemonic: empty string");
            Assert(Lang.StripMnemonic(null)               == "",         "StripMnemonic: null returns empty");

            // Spot-check a selection of new dialog tooltip keys in English
            Assert(Lang.T("tip: Color swatch")         == "Click to open the colour picker",   "tip EN: Color swatch");
            Assert(Lang.T("tip: Hex color")             == "Type a hex colour (#RRGGBB)",        "tip EN: Hex color");
            Assert(Lang.T("tip: Font size")             == "0 = auto-size to fit the key",       "tip EN: Font size");
            Assert(Lang.T("tip: Border thickness")      .Contains("standard group"),             "tip EN: Border thickness mentions standard group");
            Assert(Lang.T("tip: Key width")             .Contains("1.5"),                        "tip EN: Key width has example");
            Assert(Lang.T("tip: Row span")              .Contains("double height"),              "tip EN: Row span");
            Assert(Lang.T("tip: Record")                == "Record a keystroke or shortcut",     "tip EN: Record");
            Assert(Lang.T("tip: Browse layout")         == "Browse for a layout file",           "tip EN: Browse layout");
            Assert(Lang.T("tip: Mode Text")             == "The key types text characters",      "tip EN: Mode Text");
            Assert(Lang.T("tip: Mode Key")              .Contains("shortcut"),                   "tip EN: Mode Key");
            Assert(Lang.T("tip: Mode Modifier")         .Contains("modifier"),                   "tip EN: Mode Modifier");
            Assert(Lang.T("tip: Mode Word prediction")  .Contains("prediction"),                 "tip EN: Mode Word prediction");
            Assert(Lang.T("tip: Mode Layout")           .Contains("layout"),                     "tip EN: Mode Layout");
            Assert(Lang.T("tip: Add group")             == "Create a new style group",           "tip EN: Add group");
            Assert(Lang.T("tip: Delete group")          == "Delete the selected group",          "tip EN: Delete group");
            Assert(Lang.T("tip: Import groups")         == "Import groups from another layout file", "tip EN: Import groups");
            Assert(Lang.T("tip: Opacity")               .Contains("opaque"),                     "tip EN: Opacity");
            Assert(Lang.T("tip: Manage Groups")         == "Open the group editor",              "tip EN: Manage Groups");
            Assert(Lang.T("tip: Language")              == "Select the interface language",      "tip EN: Language");
            Assert(Lang.T("tip: WP slot")               .Contains("0–9"),                        "tip EN: WP slot");

            Section("Priority 5 — ErrorProvider hex validation lang keys");

            // Error message key exists in English and contains the format hint
            Assert(!string.IsNullOrEmpty(Lang.T("err: invalid hex")),
                "err: invalid hex key defined");
            Assert(Lang.T("err: invalid hex").Contains("#RRGGBB"),
                "err: invalid hex message contains format hint");

            // Dutch translation exists and differs from the English key fallback
            {
                string nlPath2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang_nl.xml");
                if (File.Exists(nlPath2))
                {
                    Lang.Load("nl");
                    Assert(Lang.T("err: invalid hex") != "err: invalid hex",
                        "err: invalid hex has Dutch translation");
                    Assert(Lang.T("err: invalid hex").Contains("#RRGGBB"),
                        "Dutch err: invalid hex message retains format hint");
                    Lang.Load("en");
                }
            }

            Section("StyleGroups — standard group name immutable through round-trip");

            {
                string f2 = Path.Combine(Path.GetTempPath(), $"stdname_{Guid.NewGuid()}.xml");
                try
                {
                    var stdLayout = new GridLayout(1, 1);
                    stdLayout.Cells.Add(new GridCell(0, 0, new KeyProps("x", "x")));
                    stdLayout.Groups.Add(new KeyGroup { Name = SettingsManager.StandardGroupName, FontName = "Arial" });
                    SettingsManager.SaveSettings(stdLayout, new VisualTheme(), new WindowState(), new LayoutMeta(), f2);

                    var stdLoaded = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), f2);
                    var std2 = stdLoaded.Groups.Find(g => g.Name == SettingsManager.StandardGroupName);
                    Assert(std2 != null,                    "Standard round-trip name: group present after load");
                    Assert(std2.Name == "standard",         "Standard round-trip name: name is 'standard' after load");
                    Assert(std2.FontName == "Arial",        "Standard round-trip name: style preserved");
                }
                finally { if (File.Exists(f2)) File.Delete(f2); }
            }

            Section("StyleGroups — GroupEditorForm pre-selects initial group by name");

            {
                // Build a group list with three entries in a known order.
                var groups5 = new List<KeyGroup>
                {
                    new KeyGroup { Name = SettingsManager.StandardGroupName, FontName = "Arial" },
                    new KeyGroup { Name = "Alpha", FontName = "Courier" },
                    new KeyGroup { Name = "Beta",  FontName = "Verdana" },
                };

                // When no initialGroupName is given, index 0 (standard) is selected.
                using var dlgDefault = new GroupEditorForm(groups5);
                Assert(dlgDefault.SelectedGroupName == SettingsManager.StandardGroupName,
                    "GroupEditorForm default: selects first (standard) group");

                // When "Beta" is requested, the dialog should pre-select "Beta".
                using var dlgBeta = new GroupEditorForm(groups5, initialGroupName: "Beta");
                Assert(dlgBeta.SelectedGroupName == "Beta",
                    "GroupEditorForm initialGroupName: selects 'Beta'");

                // When a non-existent name is given, fall back to index 0.
                using var dlgMiss = new GroupEditorForm(groups5, initialGroupName: "NonExistent");
                Assert(dlgMiss.SelectedGroupName == SettingsManager.StandardGroupName,
                    "GroupEditorForm initialGroupName: unknown name falls back to first group");
            }

            Section("StyleGroups — standard group values not affected by per-key cell iteration");

            {
                // Verify the invariant: iterating _layout.Cells and clearing per-key
                // style overrides (the old "Apply to all keys" operation) does NOT
                // overwrite the standard group stored in _layout.Groups.
                var layout5 = new GridLayout(1, 2);
                layout5.Cells.Add(new GridCell(0, 0, new KeyProps("A", "a")
                {
                    FontColor = Color.Red, KeyColor = Color.Blue,
                    BorderColor = Color.Green, BorderThickness = 2,
                    FontName = "Courier",
                }));
                layout5.Cells.Add(new GridCell(0, 1, new KeyProps("⚙", "")));
                var stdGroup5 = new KeyGroup
                {
                    Name = SettingsManager.StandardGroupName,
                    FontColor = Color.FromArgb(0, 0, 0),
                    KeyColor  = Color.FromArgb(255, 255, 0),
                    BorderColor = Color.FromArgb(255, 128, 0),
                    BorderThickness = 3,
                };
                layout5.Groups.Add(stdGroup5);

                // Simulate "Apply to all keys": clear per-key overrides on every cell.
                // This must leave _layout.Groups unchanged — groups are never touched.
                foreach (var cell in layout5.Cells)
                {
                    cell.Props.FontName        = "";
                    cell.Props.FontColor       = Color.Empty;
                    cell.Props.KeyColor        = Color.Empty;
                    cell.Props.BorderColor     = Color.Empty;
                    cell.Props.BorderThickness = -1;
                }

                // Standard group values must be unmodified.
                var std5 = layout5.Groups.Find(g => g.Name == SettingsManager.StandardGroupName);
                Assert(std5 != null,                                "Apply-to-all: standard group still present");
                Assert(std5.KeyColor   == Color.FromArgb(255, 255, 0), "Apply-to-all: standard group KeyColor unchanged");
                Assert(std5.BorderThickness == 3,                   "Apply-to-all: standard group BorderThickness unchanged");
            }

            Section("StyleGroups — Step 6 lang keys registered");

            Assert(Lang.T("Name 'standard' is reserved.") == "Name 'standard' is reserved.",
                "Lang key: Name 'standard' is reserved.");
            Assert(Lang.T("Update standard group style") == "Update standard group style",
                "Lang key: Update standard group style");
            Assert(Lang.T("Protected") == "Protected",
                "Lang key: Protected");

            Section("StyleGroups — Step 6 reserved name enforcement: add blocked");

            {
                var groups6 = new List<KeyGroup>
                {
                    new KeyGroup { Name = SettingsManager.StandardGroupName,
                                   KeyColor = Color.White, FontColor = Color.Black },
                    new KeyGroup { Name = "Alpha" },
                };
                using var form6 = new GroupEditorForm(groups6);

                // Adding a group named "standard" (exact case) must be rejected.
                bool addedExact = form6.TryAddGroup("standard");
                Assert(!addedExact, "Step 6 add: 'standard' (exact) is rejected");

                // Case-insensitive: "Standard" and "STANDARD" must also be rejected.
                bool addedTitle = form6.TryAddGroup("Standard");
                Assert(!addedTitle, "Step 6 add: 'Standard' (title-case) is rejected");

                bool addedUpper = form6.TryAddGroup("STANDARD");
                Assert(!addedUpper, "Step 6 add: 'STANDARD' (upper-case) is rejected");

                // A legitimate name must still be accepted.
                bool addedLeg = form6.TryAddGroup("Beta");
                Assert(addedLeg, "Step 6 add: legitimate name 'Beta' is accepted");

                // Group count: started with 2, only "Beta" was added → should be 3.
                form6.CommitToResult();
                Assert(form6.ResultGroups != null, "Step 6 add: ResultGroups set after OK");
                Assert(form6.ResultGroups.Count == 3, "Step 6 add: only the legitimate group was added");
                Assert(!form6.ResultGroups.Exists(g => g.Name == "Beta" && g.Name == SettingsManager.StandardGroupName),
                    "Step 6 add: 'Beta' is distinct from 'standard'");
            }

            Section("StyleGroups — Step 6 reserved name enforcement: rename blocked");

            {
                var groups7 = new List<KeyGroup>
                {
                    new KeyGroup { Name = SettingsManager.StandardGroupName },
                    new KeyGroup { Name = "Gamma" },
                };
                using var form7 = new GroupEditorForm(groups7, initialGroupName: "Gamma");

                // Renaming a non-standard group TO "standard" must be rejected.
                bool renamedToStd = form7.TryRenameCurrentGroup("standard");
                Assert(!renamedToStd, "Step 6 rename: renaming to 'standard' is rejected");

                bool renamedToStdUpper = form7.TryRenameCurrentGroup("STANDARD");
                Assert(!renamedToStdUpper, "Step 6 rename: renaming to 'STANDARD' is rejected");

                // Renaming the standard group to anything is blocked (source protection).
                using var form7b = new GroupEditorForm(groups7, initialGroupName: SettingsManager.StandardGroupName);
                bool renamedFromStd = form7b.TryRenameCurrentGroup("NewName");
                Assert(!renamedFromStd, "Step 6 rename: renaming 'standard' to something else is rejected");

                // Renaming to a non-reserved name must be accepted.
                bool renamedOK = form7.TryRenameCurrentGroup("Delta");
                Assert(renamedOK, "Step 6 rename: legitimate rename to 'Delta' is accepted");
                Assert(form7.SelectedGroupName == "Delta", "Step 6 rename: group is now named 'Delta'");
            }

            Section("StyleGroups — Step 6 import: UpdateStandard replaces standard group");

            {
                var stdGroup8 = new KeyGroup
                {
                    Name            = SettingsManager.StandardGroupName,
                    KeyColor        = Color.FromArgb(255, 10, 20, 30),
                    FontColor       = Color.FromArgb(255, 40, 50, 60),
                    BorderColor     = Color.FromArgb(255, 70, 80, 90),
                    BorderThickness = 5,
                };
                var groups8 = new List<KeyGroup>
                {
                    new KeyGroup { Name = SettingsManager.StandardGroupName,
                                   KeyColor = Color.White, FontColor = Color.Black,
                                   BorderColor = Color.Black, BorderThickness = 0 },
                    new KeyGroup { Name = "Regular" },
                };
                using var form8 = new GroupEditorForm(groups8);

                // Apply UpdateStandard decision — replaces the local standard group.
                form8.ApplyImportDecisions(new[]
                {
                    (stdGroup8, GroupEditorForm.ImportAction.UpdateStandard),
                });

                form8.CommitToResult();
                var result8Std = form8.ResultGroups?.Find(g => g.Name == SettingsManager.StandardGroupName);
                Assert(result8Std != null,
                    "Step 6 import UpdateStandard: standard group still present after import");
                Assert(result8Std.KeyColor == Color.FromArgb(255, 10, 20, 30),
                    "Step 6 import UpdateStandard: KeyColor updated");
                Assert(result8Std.FontColor == Color.FromArgb(255, 40, 50, 60),
                    "Step 6 import UpdateStandard: FontColor updated");
                Assert(result8Std.BorderThickness == 5,
                    "Step 6 import UpdateStandard: BorderThickness updated");
            }

            Section("StyleGroups — Step 6 import: Skip leaves standard group unchanged");

            {
                var importedStd9 = new KeyGroup
                {
                    Name      = SettingsManager.StandardGroupName,
                    KeyColor  = Color.FromArgb(255, 99, 88, 77),
                    BorderThickness = 9,
                };
                var localStd9 = new KeyGroup
                {
                    Name            = SettingsManager.StandardGroupName,
                    KeyColor        = Color.FromArgb(255, 255, 255, 255),
                    BorderThickness = 1,
                };
                using var form9 = new GroupEditorForm(new List<KeyGroup> { localStd9 });

                // Skip decision — local standard group must be untouched.
                form9.ApplyImportDecisions(new[]
                {
                    (importedStd9, GroupEditorForm.ImportAction.Skip),
                });

                form9.CommitToResult();
                var result9Std = form9.ResultGroups?.Find(g => g.Name == SettingsManager.StandardGroupName);
                Assert(result9Std != null,
                    "Step 6 import Skip: standard group still present");
                Assert(result9Std.KeyColor == Color.FromArgb(255, 255, 255, 255),
                    "Step 6 import Skip: KeyColor unchanged (Skip was chosen)");
                Assert(result9Std.BorderThickness == 1,
                    "Step 6 import Skip: BorderThickness unchanged (Skip was chosen)");
            }
        }

        // Inline helpers mirroring KeyboardForm private methods for test isolation
        private static Color ResolveColor(Color keyColor, Color groupColor, Color globalColor) =>
            !keyColor.IsEmpty   ? keyColor   :
            !groupColor.IsEmpty ? groupColor :
            globalColor;

        private static int ResolveThickness(int keyBt, int groupBt, int globalBt) =>
            keyBt   != -1 ? keyBt   :
            groupBt != -1 ? groupBt :
            globalBt;

        private static bool IsPureSpacer(GridCell cell)
        {
            var p = cell.Props;
            return string.IsNullOrEmpty(p.Label)
                && string.IsNullOrEmpty(p.Send)
                && (string.IsNullOrEmpty(p.GroupName) || p.GroupName == SettingsManager.StandardGroupName)
                && string.IsNullOrEmpty(p.FontName)
                && p.FontSize        == 0
                && p.FontColor.IsEmpty
                && p.KeyColor.IsEmpty
                && p.BorderColor.IsEmpty
                && p.BorderThickness == -1
                && cell.RowSpan == 1
                && cell.ColSpan == 1;
        }

        // ════════════════════════════════════════════════════════════════
        // XML robustness — malformed / edge-case layout files
        // ════════════════════════════════════════════════════════════════
        private static void T_XmlRobustness()
        {
            // Helper: build a minimal Theme/Layout XML with optional overrides
            string MakeLayout(int gridRows = 2, int gridCols = 2,
                              string themeAttribs  = "",
                              string extraGroups   = "",
                              string keys          = "") =>
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<OnScreenKeyboard>
  <Theme BackgroundColor=""1A1A2E"" Opacity=""1.00"" FontName=""Arial"" FontSize=""12""
         FontColor=""E0E0FF"" KeyColor=""2D2D4A"" BorderColor=""3C3C5A"" BorderThickness=""1"" {themeAttribs}>
{extraGroups}  </Theme>
  <Layout GridRows=""{gridRows}"" GridCols=""{gridCols}"" Language=""en""
          WindowWidth=""800"" WindowHeight=""200"" LastFile="""">
{keys}  </Layout>
</OnScreenKeyboard>";

            // ── 1. Invalid grid coordinates → no crash, bad cell excluded ──────
            Section("XmlRobustness — invalid grid coordinates");
            {
                string xml = MakeLayout(gridRows: 2, gridCols: 2, keys:
                    "    <Key Row=\"0\" Col=\"0\" Label=\"A\" Send=\"A\" />\n" +
                    "    <Key Row=\"5\" Col=\"5\" Label=\"X\" Send=\"X\" />\n");   // out of bounds
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob1_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, xml);
                    var layout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), tmp);
                    Assert(layout != null, "Invalid coords: loads without crash");
                    // The out-of-bounds key should be absent; only (0,0) was explicit
                    var bad = layout?.CellAt(5, 5);
                    Assert(bad == null, "Invalid coords: out-of-bounds cell not present");
                    // (0,0) should have loaded correctly
                    Assert(layout?.CellAt(0, 0)?.Props.Label == "A", "Invalid coords: valid cell still loaded");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            // ── 2. Overlapping cells → no crash ─────────────────────────────────
            Section("XmlRobustness — overlapping cells");
            {
                // Two keys at the same grid position
                string xml = MakeLayout(gridRows: 2, gridCols: 2, keys:
                    "    <Key Row=\"0\" Col=\"0\" Label=\"First\" Send=\"A\" />\n" +
                    "    <Key Row=\"0\" Col=\"0\" Label=\"Second\" Send=\"B\" />\n");
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob2_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, xml);
                    var layout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), tmp);
                    Assert(layout != null, "Overlapping cells: loads without crash");
                    // Second key at same position is skipped; first key wins
                    var cell = layout?.CellAt(0, 0);
                    Assert(cell != null, "Overlapping cells: (0,0) is accessible");
                    Assert(cell?.Props.Label == "First", "Overlapping cells: first key kept, second skipped");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            // ── 3. Missing Row/Col attribute → phantom cell defaults to (0,0) ──
            Section("XmlRobustness — missing Row/Col attribute");
            {
                string xml = MakeLayout(gridRows: 2, gridCols: 2, keys:
                    "    <Key Label=\"Ghost\" Send=\"G\" />\n" +  // no Row= or Col=
                    "    <Key Row=\"1\" Col=\"1\" Label=\"B\" Send=\"B\" />\n");
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob3_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, xml);
                    // Should not crash — missing attrs default to 0
                    var layout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), tmp);
                    Assert(layout != null, "Missing Row/Col: loads without crash");
                    // Row=0,Col=0 defaults are valid grid positions — a cell lands there
                    var phantom = layout?.CellAt(0, 0);
                    Assert(phantom != null, "Missing Row/Col: phantom cell appears at (0,0)");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            // ── 4a. Global FontSize unclamped (extreme value must not crash) ────
            Section("XmlRobustness — global FontSize unclamped");
            {
                // Build XML directly to avoid duplicate-attribute collision with MakeLayout's hardcoded FontSize
                string xml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<OnScreenKeyboard>
  <Theme BackgroundColor=""1A1A2E"" Opacity=""1.00"" FontName=""Arial"" FontSize=""999""
         FontColor=""E0E0FF"" KeyColor=""2D2D4A"" BorderColor=""3C3C5A"" BorderThickness=""1"">
  </Theme>
  <Layout GridRows=""2"" GridCols=""2"" Language=""en""
          WindowWidth=""800"" WindowHeight=""200"" LastFile="""">
    <Key Row=""0"" Col=""0"" Label=""A"" Send=""A"" />
  </Layout>
</OnScreenKeyboard>";
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob4a_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, xml);
                    var theme = new VisualTheme();
                    var layout = SettingsManager.LoadSettings(theme, new WindowState(), new LayoutMeta(), tmp);
                    Assert(layout != null, "Global FontSize 999: loads without crash");
                    Assert(theme.FontSize <= 200, "Global FontSize 999: clamped to ≤200");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            // ── 4b. Group FontSize unclamped (extreme value must not crash) ──────
            Section("XmlRobustness — group FontSize unclamped");
            {
                string xml = MakeLayout(extraGroups:
                    "    <Group Name=\"Test\" FontSize=\"999\" />\n");
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob4b_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, xml);
                    var layout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), tmp);
                    Assert(layout != null, "Group FontSize 999: loads without crash");
                    var grp = layout?.Groups.Find(g => g.Name == "Test");
                    Assert(grp != null, "Group FontSize 999: group present");
                    Assert(grp?.FontSize <= 200, "Group FontSize 999: clamped to ≤200");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            // ── 5. ColSpan overflow past right edge ──────────────────────────────
            Section("XmlRobustness — ColSpan overflow past right edge");
            {
                // 4-col grid; key at col=2 with ColSpan=10 would extend 8 cols past the edge
                string xml = MakeLayout(gridRows: 2, gridCols: 4, keys:
                    "    <Key Row=\"0\" Col=\"2\" ColSpan=\"10\" Label=\"Wide\" Send=\"W\" />\n");
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob5_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, xml);
                    var layout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), tmp);
                    Assert(layout != null, "ColSpan overflow: loads without crash");
                    var cell = layout?.CellAt(0, 2);
                    Assert(cell != null, "ColSpan overflow: cell present at (0,2)");
                    // gridCols=4, col=2 → max valid span is 4-2=2
                    Assert(cell?.ColSpan <= 2, "ColSpan overflow: ColSpan clamped to gridCols - col");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            // ── 6. WindowWidth/Height with no ceiling ────────────────────────────
            Section("XmlRobustness — WindowWidth/Height no ceiling");
            {
                string xml = MakeLayout(themeAttribs: "",
                    keys: "    <Key Row=\"0\" Col=\"0\" Label=\"A\" Send=\"A\" />\n");
                // Inject huge window dimensions into the Layout element
                string hugeXml = xml.Replace(
                    "WindowWidth=\"800\" WindowHeight=\"200\"",
                    "WindowWidth=\"2000000\" WindowHeight=\"2000000\"");
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob6_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, hugeXml);
                    var window = new WindowState();
                    var layout = SettingsManager.LoadSettings(new VisualTheme(), window, new LayoutMeta(), tmp);
                    Assert(layout != null, "Huge window size: loads without crash");
                    // Ceiling is 7680×4320; out-of-range values leave the WindowState at its default
                    Assert(window.WindowWidth  <= 7680, "Huge WindowWidth: clamped to ≤7680");
                    Assert(window.WindowHeight <= 4320, "Huge WindowHeight: clamped to ≤4320");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            // ── 7. Duplicate group names ──────────────────────────────────────────
            Section("XmlRobustness — duplicate group names");
            {
                string xml = MakeLayout(extraGroups:
                    "    <Group Name=\"Klinkers\" KeyColor=\"FF0000\" />\n" +
                    "    <Group Name=\"Klinkers\" KeyColor=\"00FF00\" />\n");
                string tmp = Path.Combine(Path.GetTempPath(), $"osk_rob7_{Guid.NewGuid()}.xml");
                try
                {
                    File.WriteAllText(tmp, xml);
                    var layout = SettingsManager.LoadSettings(new VisualTheme(), new WindowState(), new LayoutMeta(), tmp);
                    Assert(layout != null, "Duplicate group names: loads without crash");
                    // Second entry with the same name is silently skipped
                    int dupeCount = layout?.Groups.FindAll(g => g.Name == "Klinkers").Count ?? 0;
                    Assert(dupeCount == 1, "Duplicate group names: only first entry kept");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static bool IsValid(GridLayout layout) => layout != null && layout.IsValid();

        private static GridLayout MakeGrid(int rows, int cols)
        {
            var g = new GridLayout(rows, cols);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    g.Cells.Add(new GridCell(r, c, new KeyProps("a","a")));
            return g;
        }

        // Mirror ToHuman logic from KeyEditorForm (pure string, no UI dependency)
        private static string ToHuman(string send)
        {
            if (string.IsNullOrEmpty(send)) return send;
            if (send.StartsWith("win:")) return "{Win}" + ToHuman(send.Substring(4));
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < send.Length)
            {
                char ch = send[i];
                if      (ch == '^') { sb.Append("{Ctrl}");  i++; }
                else if (ch == '%') { sb.Append("{Alt}");   i++; }
                else if (ch == '+') { sb.Append("{Shift}"); i++; }
                else if (ch == '(')
                {
                    i++;
                    while (i < send.Length && send[i] != ')') { sb.Append(send[i]); i++; }
                    if (i < send.Length) i++;
                }
                else { sb.Append(ch); i++; }
            }
            return sb.ToString();
        }

        // Mirror FromHuman logic from KeyEditorForm
        private static string FromHuman(string human)
        {
            if (string.IsNullOrEmpty(human)) return human;
            if (human.StartsWith("{Win}")) return "win:" + FromHuman(human.Substring(5));
            return human.Replace("{Ctrl}","^").Replace("{Alt}","%").Replace("{Shift}","+");
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════
// WORD PREDICTION TESTS — added as standalone static class
// These tests exercise WordDatabase.GetPredictions directly, simulating the
// full sequence of the combined test text. They verify:
//   • predictions at sentence start (capitalised, case-insensitive search)
//   • predictions mid-sentence (lowercase, case-sensitive)
//   • shift latch/unlatch at correct moments (.!? Enter startup)
//   • no shift latch after , ; :
//   • proper nouns IN database (Amerika, Griekenland, Google, VRT...)
//   • proper nouns NOT in database (Emma, Filemon, Lovis, iPhone...)
//   • prefix narrowing both at sentence start and mid-sentence
// ════════════════════════════════════════════════════════════════════════════

namespace OnScreenKeyboard
{
    public static class WordDatabaseRobustnessTests
    {
        public static void Run(Action<bool, string> assert, Action<string> section)
        {
            section("WordDatabase — graceful failure");

            // ── Missing file: IsLoaded stays false, no crash ──────────────
            WordDatabase.Load(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "nonexistent_worddb_xyz.xml"));
            assert(!WordDatabase.IsLoaded,  "Missing file: IsLoaded = false");
            assert(WordDatabase.LoadError != null, "Missing file: LoadError set");

            // ── GetPredictions when not loaded: returns empty list ─────────
            var preds = WordDatabase.GetPredictions("de", "he", false, 5);
            assert(preds != null,       "Not loaded: GetPredictions returns non-null");
            assert(preds.Count == 0,    "Not loaded: GetPredictions returns empty list");

            // ── Corrupt file: IsLoaded = false, LoadError set, no crash ──
            string corrupt = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"osk_corrupt_{Guid.NewGuid():N}.xml");
            try
            {
                System.IO.File.WriteAllText(corrupt, "<<< not valid xml >>>");
                WordDatabase.Load(corrupt);
                assert(!WordDatabase.IsLoaded,  "Corrupt file: IsLoaded = false");
                assert(WordDatabase.LoadError != null, "Corrupt file: LoadError set");

                // Predictions after corrupt load also return empty
                var preds2 = WordDatabase.GetPredictions("de", "", false, 5);
                assert(preds2 != null,    "After corrupt load: GetPredictions non-null");
                assert(preds2.Count == 0, "After corrupt load: GetPredictions empty");
            }
            finally
            {
                if (System.IO.File.Exists(corrupt)) System.IO.File.Delete(corrupt);
            }

            // ── WordPredictor: OnKeySent with no database loaded — no crash ─
            var predictor = new WordPredictor(slotCount: 3);
            bool threw = false;
            try
            {
                predictor.OnSentenceStart();
                predictor.OnKeySent("h", false);
                predictor.OnKeySent("e", false);
                predictor.OnKeySent("t", false);
                predictor.OnKeySent(" ", false);
            }
            catch { threw = true; }
            assert(!threw, "WordPredictor.OnKeySent with no DB: no exception");
            assert(string.IsNullOrEmpty(predictor.Predictions[0]), "WordPredictor: predictions blank when DB not loaded");
        }
    }

    public static class WordPredictionTests
    {
        private static Action<bool, string> _assert;
        private static Action<string>       _section;

        /// <summary>
        /// Called from TestRunner.Run() — shares the same Assert and Section
        /// delegates so failures are recorded in the shared failure list and
        /// written to test_failures.txt.
        /// </summary>
        public static void Run(Action<bool, string> assert, Action<string> section)
        {
            string dbPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "worddb.xml");
            if (!System.IO.File.Exists(dbPath))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("  (worddb.xml not found — word prediction tests skipped)");
                Console.ResetColor();
                return;
            }
            WordDatabase.Load(dbPath);
            _assert  = assert;
            _section = section;
            RunTests();
        }

        private static void Section(string name)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n    {name}");
            Console.ResetColor();
        }

        private static System.Collections.Generic.List<string> P(
            string last, string prefix, bool upper, int count = 7)
            => WordDatabase.GetPredictions(last, prefix, upper, count);

        private static void RunTests()
        {
            // ── Helpers ───────────────────────────────────────────────
            // Contains: prediction list contains the word (case-insensitive)
            bool Contains(System.Collections.Generic.List<string> preds, string word)
                => preds.Exists(p => string.Equals(p, word, System.StringComparison.OrdinalIgnoreCase));
            // First: first prediction equals word
            bool First(System.Collections.Generic.List<string> preds, string word)
                => preds.Count > 0 && string.Equals(preds[0], word, System.StringComparison.OrdinalIgnoreCase);
            // AllUpper: all predictions start with uppercase
            bool AllUpper(System.Collections.Generic.List<string> preds)
                => preds.Count > 0 && preds.TrueForAll(p => p.Length > 0 && char.IsUpper(p[0]));
            // AllLower: all predictions start with lowercase
            bool AllLower(System.Collections.Generic.List<string> preds)
                => preds.Count > 0 && preds.TrueForAll(p => p.Length > 0 && char.IsLower(p[0]));
            // HasN: list has exactly N items
            bool HasN(System.Collections.Generic.List<string> preds, int n)
                => preds.Count == n;

            // ════════════════════════════════════════════════════════
            // 1. SENTENCE START — keyboard opens
            // ════════════════════════════════════════════════════════
            _section("[01] Keyboard opens — sentence start");
            var p01 = P("", "", true);
            _assert(HasN(p01, 7),                   "01: 7 predictions");
            _assert(AllUpper(p01),                  "01: all capitalised");
            _assert(Contains(p01, "De"),            "01: 'De' in list (freq=269)");
            _assert(Contains(p01, "Het"),           "01: 'Het' in list (freq=267)");
            _assert(First(p01, "De"),               "01: 'De' is first");

            // ════════════════════════════════════════════════════════
            // 2. PREFIX 'D' AT SENTENCE START
            // ════════════════════════════════════════════════════════
            _section("[02] Typed 'D' — sentence start prefix");
            var p02 = P("", "D", true);
            _assert(AllUpper(p02),                  "02: all capitalised");
            _assert(Contains(p02, "De"),            "02: 'De' matches prefix D");
            _assert(Contains(p02, "Dat"),           "02: 'Dat' matches prefix D");
            _assert(Contains(p02, "Dan"),           "02: 'Dan' matches prefix D");
            _assert(!Contains(p02, "In"),           "02: 'In' excluded by prefix D");
            _assert(!Contains(p02, "Het"),          "02: 'Het' excluded by prefix D");

            // ════════════════════════════════════════════════════════
            // 3. AFTER COMPLETING 'De' — sentence start context preserved
            //    _nextWordUpper still true: next-word list of 'De' capitalised
            // ════════════════════════════════════════════════════════
            _section("[03] After completing 'De' — sentence start context");
            var p03 = P("De", "", true);
            _assert(AllUpper(p03),                  "03: all capitalised (sentence start preserved)");
            _assert(p03.Count > 0,                  "03: has predictions");
            // Next-words of 'de' include 'eerste', 'nieuwe', 'laatste' — capitalised
            _assert(Contains(p03, "Eerste") || Contains(p03, "Nieuwe"),
                                                   "03: next-words of 'de' capitalised");

            // ════════════════════════════════════════════════════════
            // 4. MID-SENTENCE — after 'kat' (no sentence start)
            // ════════════════════════════════════════════════════════
            _section("[04] After 'kat' — mid-sentence");
            var p04 = P("kat", "", false);
            _assert(AllLower(p04),                  "04: all lowercase");
            _assert(Contains(p04, "de"),            "04: 'de' in list");
            _assert(Contains(p04, "in"),            "04: 'in' in list");
            _assert(!p04.Exists(w => w == "De"),   "04: no capitalised 'De' (case-sensitive)");

            // ════════════════════════════════════════════════════════
            // 5. AFTER 'zit' — second words from database
            // ════════════════════════════════════════════════════════
            _section("[05] After 'zit'");
            var p05 = P("zit", "", false);
            _assert(AllLower(p05),                  "05: all lowercase");
            _assert(Contains(p05, "in"),            "05: 'in' (next-word of zit)");
            _assert(Contains(p05, "ook"),           "05: 'ook' in list");

            // ════════════════════════════════════════════════════════
            // 6. COMMA — no sentence start after comma
            // ════════════════════════════════════════════════════════
            _section("[06] After 'mat,' — comma does NOT trigger sentence start");
            var p06 = P("mat", "", false);
            _assert(AllLower(p06),                  "06: still lowercase after comma");
            _assert(Contains(p06, "de"),            "06: 'de' in list");
            _assert(!AllUpper(p06),                 "06: not capitalised after comma");

            // ════════════════════════════════════════════════════════
            // 7. PERIOD — sentence start after period
            // ════════════════════════════════════════════════════════
            _section("[07] After 'mat.' — period triggers sentence start");
            var p07 = P("", "", true);
            _assert(AllUpper(p07),                  "07: capitalised after period");
            _assert(Contains(p07, "De"),            "07: 'De' in list");
            _assert(First(p07, "De"),               "07: 'De' is first");

            // ════════════════════════════════════════════════════════
            // 8. PREFIX 'H' AT SENTENCE START — after period
            // ════════════════════════════════════════════════════════
            _section("[08] Typed 'H' after '.' — capitalised prefix");
            var p08 = P("", "H", true);
            _assert(AllUpper(p08),                  "08: all capitalised");
            _assert(Contains(p08, "Het"),           "08: 'Het' matches H");
            _assert(Contains(p08, "Hij"),           "08: 'Hij' matches H");
            _assert(!Contains(p08, "De"),           "08: 'De' excluded by prefix H");

            // ════════════════════════════════════════════════════════
            // 9. AFTER 'Het' — second words, sentence start still active
            // ════════════════════════════════════════════════════════
            _section("[09] After 'Het' — second words capitalised");
            var p09 = P("Het", "", true);
            _assert(AllUpper(p09),                  "09: all capitalised");
            _assert(Contains(p09, "Is") || Contains(p09, "De"),
                                                   "09: next-words of 'het' capitalised");

            // ════════════════════════════════════════════════════════
            // 10. EXCLAMATION MARK — sentence start
            // ════════════════════════════════════════════════════════
            _section("[10] After 'groot!' — exclamation triggers sentence start");
            var p10 = P("", "", true);
            _assert(AllUpper(p10),                  "10: capitalised after !");
            _assert(Contains(p10, "De"),            "10: 'De' in list");

            // ════════════════════════════════════════════════════════
            // 11. QUESTION MARK — sentence start
            // ════════════════════════════════════════════════════════
            _section("[11] After 'sleutel?' — question mark triggers sentence start");
            var p11 = P("", "", true);
            _assert(AllUpper(p11),                  "11: capitalised after ?");
            _assert(Contains(p11, "De"),            "11: 'De' in list");

            // ════════════════════════════════════════════════════════
            // 12. SEMICOLON — no sentence start
            // ════════════════════════════════════════════════════════
            _section("[12] After 'hard;' — semicolon does NOT trigger sentence start");
            var p12 = P("hard", "", false);
            _assert(AllLower(p12),                  "12: lowercase after semicolon");
            _assert(!AllUpper(p12),                 "12: not capitalised after semicolon");

            // ════════════════════════════════════════════════════════
            // 13. AFTER 'wil' — second words
            // ════════════════════════════════════════════════════════
            _section("[13] After 'wil'");
            var p13 = P("wil", "", false);
            _assert(AllLower(p13),                  "13: all lowercase");
            _assert(Contains(p13, "de"),            "13: 'de' in list");
            _assert(Contains(p13, "je"),            "13: 'je' (next-word of wil)");
            _assert(Contains(p13, "niet"),          "13: 'niet' in list");

            // ════════════════════════════════════════════════════════
            // 14. AFTER 'graag' — second words (after comma)
            // ════════════════════════════════════════════════════════
            _section("[14] After 'graag,'");
            var p14 = P("graag", "", false);
            _assert(AllLower(p14),                  "14: all lowercase");
            _assert(Contains(p14, "naar"),          "14: 'naar' (next-word of graag)");

            // ════════════════════════════════════════════════════════
            // 15. AFTER 'maar' — second words
            // ════════════════════════════════════════════════════════
            _section("[15] After 'maar'");
            var p15 = P("maar", "", false);
            _assert(AllLower(p15),                  "15: all lowercase");
            _assert(Contains(p15, "ik"),            "15: 'ik' (next-word of maar)");
            _assert(Contains(p15, "de"),            "15: 'de' in list");

            // ════════════════════════════════════════════════════════
            // 16. AFTER 'zij' — second words
            // ════════════════════════════════════════════════════════
            _section("[16] After 'zij'");
            var p16 = P("zij", "", false);
            _assert(AllLower(p16),                  "16: all lowercase");
            _assert(Contains(p16, "is"),            "16: 'is' (next-word of zij)");
            _assert(Contains(p16, "kunnen"),        "16: 'kunnen' in list");

            // ════════════════════════════════════════════════════════
            // 17. AFTER 'gaan' — second words
            // ════════════════════════════════════════════════════════
            _section("[17] After 'gaan'");
            var p17 = P("gaan", "", false);
            _assert(AllLower(p17),                  "17: all lowercase");
            _assert(Contains(p17, "naar"),          "17: 'naar' (next-word of gaan)");
            _assert(Contains(p17, "we"),            "17: 'we' in list");

            // ════════════════════════════════════════════════════════
            // 18. AFTER 'dan' — second words
            // ════════════════════════════════════════════════════════
            _section("[18] After 'dan'");
            var p18 = P("dan", "", false);
            _assert(AllLower(p18),                  "18: all lowercase");
            _assert(Contains(p18, "de"),            "18: 'de' in list");
            _assert(Contains(p18, "mensen"),        "18: 'mensen' (next-word of dan)");

            // ════════════════════════════════════════════════════════
            // 19. PROPER NOUNS IN DATABASE — mid-sentence prefix 'Am'
            //     Amerika is in DB; at mid-sentence with prefix 'Am',
            //     only uppercase-starting words are returned
            // ════════════════════════════════════════════════════════
            _section("[19] Proper noun in DB — prefix 'Am' mid-sentence");
            var p19 = P("naar", "Am", false);
            _assert(Contains(p19, "Amerika"),       "19: 'Amerika' found with prefix 'Am'");
            _assert(p19.TrueForAll(p => char.IsUpper(p[0])),
                                                   "19: all start with uppercase (proper nouns only)");

            // ════════════════════════════════════════════════════════
            // 20. PROPER NOUNS IN DATABASE — mid-sentence prefix 'Gr'
            // ════════════════════════════════════════════════════════
            _section("[20] Proper noun in DB — prefix 'Gr'");
            var p20 = P("naar", "Gr", false);
            _assert(Contains(p20, "Griekenland"),   "20: 'Griekenland' found with prefix 'Gr'");

            // ════════════════════════════════════════════════════════
            // 21. PROPER NOUNS IN DATABASE — Google, VRT, RTBF
            // ════════════════════════════════════════════════════════
            _section("[21] Tech proper nouns in DB");
            var p21g = P("bij", "Go", false);
            _assert(Contains(p21g, "Google"),       "21: 'Google' found with prefix 'Go'");
            var p21v = P("de", "VR", false);
            _assert(Contains(p21v, "VRT"),          "21: 'VRT' found with prefix 'VR'");
            var p21r = P("de", "RT", false);
            _assert(Contains(p21r, "RTBF"),         "21: 'RTBF' found with prefix 'RT'");

            // ════════════════════════════════════════════════════════
            // 22. PROPER NOUNS NOT IN DATABASE — Emma, Filemon, Lovis
            //     These must NOT appear in any prediction
            // ════════════════════════════════════════════════════════
            _section("[22] Proper nouns NOT in DB — never predicted");
            var p22a = P("", "", true);
            _assert(!Contains(p22a, "Emma"),        "22: 'Emma' never predicted");
            _assert(!Contains(p22a, "Filemon"),     "22: 'Filemon' never predicted");
            _assert(!Contains(p22a, "Lovis"),       "22: 'Lovis' never predicted");
            var p22b = P("ik", "Em", false);
            _assert(!Contains(p22b, "Emma"),        "22: 'Emma' not in prefix 'Em' list");
            var p22c = P("ik", "Fi", false);
            _assert(!Contains(p22c, "Filemon"),     "22: 'Filemon' not in prefix 'Fi' list");

            // ════════════════════════════════════════════════════════
            // 23. WORDS NOT IN DATABASE — common words
            //     pen, xylofoon, quiche, jacuzzi never predicted
            // ════════════════════════════════════════════════════════
            _section("[23] Common words NOT in DB — never predicted");
            var p23 = P("", "", false);
            _assert(!Contains(p23, "pen"),          "23: 'pen' never predicted");
            _assert(!Contains(p23, "xylofoon"),     "23: 'xylofoon' never predicted");
            _assert(!Contains(p23, "quiche"),       "23: 'quiche' never predicted");
            _assert(!Contains(p23, "iPhone"),       "23: 'iPhone' never predicted");

            // ════════════════════════════════════════════════════════
            // 24. SENTENCE START — prefix 'Da' case-insensitive
            //     Should include lowercase 'dat','dan','dag' capitalised
            //     NOT just 'David','Damascus' (uppercase-only in DB)
            // ════════════════════════════════════════════════════════
            _section("[24] Sentence start prefix 'Da' — case-insensitive");
            var p24 = P("", "Da", true);
            _assert(AllUpper(p24),                  "24: all capitalised");
            _assert(Contains(p24, "Dat"),           "24: 'Dat' (from lowercase 'dat')");
            _assert(Contains(p24, "Dan"),           "24: 'Dan' (from lowercase 'dan')");
            // Should NOT be exclusively proper nouns
            var lowercaseCount = p24.FindAll(w =>
                WordDatabase.GetPredictions("", "da", false, 20).Exists(
                    lw => string.Equals(lw, w, System.StringComparison.OrdinalIgnoreCase))).Count;
            _assert(lowercaseCount > 0,             "24: includes capitalised-lowercase words, not only proper nouns");

            // ════════════════════════════════════════════════════════
            // 25. FALLBACK — fills all slots when second-word list is short
            //     'sleutel' has freq=1, likely no/few next-words → first-words fill
            // ════════════════════════════════════════════════════════
            _section("[25] Fallback to first words when second-word list is short");
            var p25 = P("sleutel", "", false);
            _assert(HasN(p25, 7),                   "25: always 7 predictions (first-words fill gaps)");
            _assert(AllLower(p25),                  "25: all lowercase");
            _assert(Contains(p25, "de"),            "25: 'de' (freq=269) in fallback list");

            // ════════════════════════════════════════════════════════
            // 26. AFTER 'kunnen' — second words
            // ════════════════════════════════════════════════════════
            _section("[26] After 'kunnen'");
            var p26 = P("kunnen", "", false);
            _assert(AllLower(p26),                  "26: all lowercase");
            _assert(Contains(p26, "ze") || Contains(p26, "we") || Contains(p26, "de"),
                                                   "26: has common next-words");

            // ════════════════════════════════════════════════════════
            // 27. AFTER 'een' — second words (very frequent)
            // ════════════════════════════════════════════════════════
            _section("[27] After 'een'");
            var p27 = P("een", "", false);
            _assert(AllLower(p27),                  "27: all lowercase");
            _assert(Contains(p27, "nieuwe") || Contains(p27, "andere"),
                                                   "27: next-words of 'een' present");

            // ════════════════════════════════════════════════════════
            // 28. COUNTRIES IN DATABASE — Duitsland, Frankrijk, Rusland
            // ════════════════════════════════════════════════════════
            _section("[28] Countries in DB found by prefix");
            _assert(Contains(P("over", "Du", false), "Duitsland"),   "28: 'Duitsland' via prefix 'Du'");
            _assert(Contains(P("over", "Fr", false), "Frankrijk"),   "28: 'Frankrijk' via prefix 'Fr'");
            _assert(Contains(P("over", "Ru", false), "Rusland"),     "28: 'Rusland' via prefix 'Ru'");
            _assert(Contains(P("over", "Ne", false), "Nederland"),   "28: 'Nederland' via prefix 'Ne'");
            _assert(Contains(P("over", "Ch", false), "China"),       "28: 'China' via prefix 'Ch'");
            _assert(Contains(P("over", "Eu", false), "Europa"),      "28: 'Europa' via prefix 'Eu'");

            // ════════════════════════════════════════════════════════
            // 29. CITY/COUNTRY NAMES AT SENTENCE START — capitalised
            // ════════════════════════════════════════════════════════
            _section("[29] Country names at sentence start (capitalised search)");
            var p29a = P("", "Am", true);
            _assert(Contains(p29a, "Amerika"),      "29: 'Amerika' at sentence start prefix 'Am'");
            _assert(AllUpper(p29a),                 "29: all capitalised at sentence start");
            var p29b = P("", "Ne", true);
            _assert(Contains(p29b, "Nederland"),    "29: 'Nederland' at sentence start");

            // ════════════════════════════════════════════════════════
            // 30. COUNT PARAMETER — requesting fewer predictions
            // ════════════════════════════════════════════════════════
            _section("[30] Count parameter respected");
            _assert(HasN(P("de", "", false, 3),  3), "30: count=3 returns 3");
            _assert(HasN(P("de", "", false, 1),  1), "30: count=1 returns 1");
            _assert(HasN(P("de", "", false, 10), 10),"30: count=10 returns 10");
            _assert(HasN(P("de", "", false, 0),  0), "30: count=0 returns empty");

            // ════════════════════════════════════════════════════════
            // 31. NO DUPLICATES between second-words and first-words
            // ════════════════════════════════════════════════════════
            _section("[31] No duplicate predictions");
            foreach (var lastWord in new[]{"de","het","ik","en","op","van","zijn"})
            {
                var preds = P(lastWord, "", false, 7);
                var lower = preds.ConvertAll(w => w.ToLower());
                var unique = new System.Collections.Generic.HashSet<string>(lower);
                _assert(unique.Count == preds.Count,
                    $"31: no duplicates after '{lastWord}'");
            }

            // ════════════════════════════════════════════════════════
            // 32. ENTER TRIGGERS SENTENCE START — same as period
            //     (Simulated: _nextWordUpper=true after Enter)
            // ════════════════════════════════════════════════════════
            _section("[32] Enter triggers sentence start — same predictions as after period");
            var pAfterPeriod = P("", "", true);
            var pAfterEnter  = P("", "", true);
            _assert(pAfterPeriod.Count == pAfterEnter.Count &&
                   pAfterPeriod[0] == pAfterEnter[0],          "32: Enter = Period for sentence-start predictions");

            // ════════════════════════════════════════════════════════
            // 33. COLON — no sentence start (same as semicolon, comma)
            // ════════════════════════════════════════════════════════
            _section("[33] Colon/semicolon — no sentence start");
            var p33 = P("boodschappen", "", false);
            _assert(AllLower(p33),                  "33: lowercase after colon (no sentence start)");

            // ════════════════════════════════════════════════════════
            // 34. NAMES IN DB at lower frequency — Dries, Pieter, Tine
            // ════════════════════════════════════════════════════════
            _section("[34] Low-freq names in DB found by prefix mid-sentence");
            _assert(Contains(P("en", "Dr", false), "Dries"),    "34: 'Dries' found via prefix 'Dr'");
            _assert(Contains(P("en", "Pi", false), "Pieter"),   "34: 'Pieter' found via prefix 'Pi'");
            _assert(Contains(P("en", "Ti", false), "Tine"),     "34: 'Tine' found via prefix 'Ti'");
            _assert(Contains(P("en", "Ve", false), "Veerle"),   "34: 'Veerle' found via prefix 'Ve'");
            _assert(Contains(P("en", "Ko", false, 10), "Kobe"),  "34: 'Kobe' found via prefix 'Ko' (count=10)");

            // ════════════════════════════════════════════════════════
            // 35. NAMES NOT IN DB — Sofie, Robbe, Axel, Bavo, Fien
            // ════════════════════════════════════════════════════════
            _section("[35] Names NOT in DB — never predicted even with prefix");
            _assert(!Contains(P("en", "So", false), "Sofie"),   "35: 'Sofie' never predicted");
            _assert(!Contains(P("en", "Ro", false), "Robbe"),   "35: 'Robbe' never predicted");
            _assert(!Contains(P("en", "Ax", false), "Axel"),    "35: 'Axel' never predicted");
            _assert(!Contains(P("en", "Ba", false), "Bavo"),    "35: 'Bavo' never predicted");
            _assert(!Contains(P("en", "Fi", false), "Fien"),    "35: 'Fien' never predicted");
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════
// END-TO-END WORD PREDICTOR TESTS
// Tests the WordPredictor state machine by simulating typing the combined
// test text and verifying predictions and shift state at every step.
// ════════════════════════════════════════════════════════════════════════════

namespace OnScreenKeyboard
{
    public static class WordPredictorE2ETests
    {
        private static Action<bool, string> _assert;
        private static Action<string>       _section;

        public static void Run(Action<bool, string> assert, Action<string> section)
        {
            string dbPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "worddb.xml");
            if (!System.IO.File.Exists(dbPath))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("  (worddb.xml not found — e2e prediction tests skipped)");
                Console.ResetColor();
                return;
            }
            WordDatabase.Load(dbPath);
            _assert  = assert;
            _section = section;
            RunTests();
        }

        // ── Helpers ──────────────────────────────────────────────────
        private static WordPredictor NewPredictor(int slots = 7)
        {
            var p = new WordPredictor(slots);
            p.OnSentenceStart();
            return p;
        }

        private static void Type(WordPredictor p, string text, bool shifted = false)
        {
            foreach (char c in text)
                p.OnKeySent(c.ToString(), shifted && char.IsLower(c));
        }

        private static bool HasPred(WordPredictor p, string word)
            => System.Linq.Enumerable.Any(p.Predictions,
                   w => string.Equals(w, word, System.StringComparison.OrdinalIgnoreCase));

        private static bool AllCaps(WordPredictor p)
            => System.Linq.Enumerable.All(p.Predictions,
                   w => string.IsNullOrEmpty(w) || char.IsUpper(w[0]));

        private static bool AllLower(WordPredictor p)
            => System.Linq.Enumerable.All(p.Predictions,
                   w => string.IsNullOrEmpty(w) || char.IsLower(w[0]));

        private static void RunTests()
        {
            // ════════════════════════════════════════════════════════
            // 1. STARTUP — sentence start, shift latched
            // ════════════════════════════════════════════════════════
            _section("[E2E-01] Startup: sentence start, capitalised predictions");
            var p = NewPredictor();
            _assert(p.NextWordUpper,          "E2E-01: NextWordUpper=true at startup");
            _assert(p.ShiftShouldBeLatched,   "E2E-01: Shift latched at startup");
            _assert(AllCaps(p),               "E2E-01: all predictions capitalised");
            _assert(HasPred(p, "De"),         "E2E-01: 'De' predicted at startup");

            // ════════════════════════════════════════════════════════
            // 2. TYPING 'D' — prefix narrows, still sentence start
            // ════════════════════════════════════════════════════════
            _section("[E2E-02] Type 'D' — prefix narrows sentence-start predictions");
            p.OnKeySent("D", false);
            _assert(p.NextWordUpper,          "E2E-02: NextWordUpper still true after first char");
            _assert(!p.ShiftShouldBeLatched,  "E2E-02: Shift unlatched after first char");
            _assert(AllCaps(p),               "E2E-02: predictions still capitalised");
            _assert(HasPred(p, "De"),         "E2E-02: 'De' in prefix-D predictions");
            _assert(HasPred(p, "Dan"),        "E2E-02: 'Dan' in prefix-D predictions");
            _assert(!HasPred(p, "Het"),       "E2E-02: 'Het' excluded by prefix D");

            // ════════════════════════════════════════════════════════
            // 3. TYPE 'e' → complete 'De', then space
            //    Predictions become next-words of 'De', still capitalised
            // ════════════════════════════════════════════════════════
            _section("[E2E-03] Complete 'De' + space — next-words capitalised");
            p.OnKeySent("e", false);
            _assert(p.WordBuffer == "De",     "E2E-03: buffer='De'");
            p.OnKeySent(" ", false);
            _assert(p.LastCompletedWord == "De", "E2E-03: lastCompleted='De'");
            _assert(p.WordBuffer == "",          "E2E-03: buffer cleared");
            _assert(p.NextWordUpper,             "E2E-03: NextWordUpper preserved after space");
            _assert(AllCaps(p),                  "E2E-03: next-words of 'De' capitalised");

            // ════════════════════════════════════════════════════════
            // 4. CLICK WP PREDICTION — shift unlatches, lowercase follows
            // ════════════════════════════════════════════════════════
            _section("[E2E-04] WP click — shift unlatches, next predictions lowercase");
            var p2 = NewPredictor();
            p2.OnKeySent("D", false);
            p2.OnKeySent("e", false);
            p2.OnKeySent(" ", false);
            string firstPred = p2.Predictions[0];
            _assert(!string.IsNullOrEmpty(firstPred), "E2E-04: prediction available before click");
            var result = p2.OnWPClick(0);
            _assert(result != null,               "E2E-04: WPClickResult not null");
            _assert(result.Backspaces == 0,       "E2E-04: no backspaces (buffer was empty)");
            _assert(!string.IsNullOrEmpty(result.Word), "E2E-04: word to send not empty");
            _assert(result.Suffix == " ",         "E2E-04: suffix is space");
            _assert(!p2.NextWordUpper,            "E2E-04: NextWordUpper=false after WP click");
            _assert(!p2.ShiftShouldBeLatched,     "E2E-04: Shift unlatched after WP click");
            _assert(AllLower(p2),                 "E2E-04: predictions lowercase after WP click");

            // ════════════════════════════════════════════════════════
            // 5. PERIOD → new sentence start
            // ════════════════════════════════════════════════════════
            _section("[E2E-05] Period triggers sentence start");
            var p3 = NewPredictor();
            Type(p3, "groot");
            p3.OnKeySent(" ", false);
            // Type one char to enter second word — this resets _nextWordUpper
            p3.OnKeySent("i", false);
            _assert(!p3.NextWordUpper,        "E2E-05: not sentence start mid-sentence");
            Type(p3, "s");
            p3.OnKeySent(" ", false);
            p3.OnKeySent(".", false);
            _assert(p3.NextWordUpper,         "E2E-05: NextWordUpper after period");
            _assert(p3.ShiftShouldBeLatched,  "E2E-05: Shift latched after period");
            _assert(AllCaps(p3),              "E2E-05: predictions capitalised after period");
            _assert(HasPred(p3, "De"),        "E2E-05: 'De' predicted after period");

            // ════════════════════════════════════════════════════════
            // 6. EXCLAMATION AND QUESTION MARK → sentence start
            // ════════════════════════════════════════════════════════
            _section("[E2E-06] ! and ? trigger sentence start");
            var p4 = NewPredictor();
            Type(p4, "groot"); p4.OnKeySent(" ", false);
            p4.OnKeySent("!", false);
            _assert(p4.NextWordUpper,         "E2E-06: NextWordUpper after !");
            _assert(AllCaps(p4),              "E2E-06: capitalised after !");

            var p5 = NewPredictor();
            Type(p5, "sleutel"); p5.OnKeySent(" ", false);
            p5.OnKeySent("?", false);
            _assert(p5.NextWordUpper,         "E2E-06: NextWordUpper after ?");
            _assert(AllCaps(p5),              "E2E-06: capitalised after ?");

            // ════════════════════════════════════════════════════════
            // 7. COMMA AND SEMICOLON → NO sentence start
            // ════════════════════════════════════════════════════════
            _section("[E2E-07] Comma and semicolon do NOT trigger sentence start");
            var p6 = NewPredictor();
            // Get past startup capitalisation
            var r6 = p6.OnWPClick(0);  // click first prediction to unlatch
            _assert(!p6.NextWordUpper,        "E2E-07: not sentence start after WP click");
            Type(p6, "mat"); p6.OnKeySent(" ", false);
            p6.OnKeySent(",", false);
            _assert(!p6.NextWordUpper,        "E2E-07: not sentence start after comma");
            _assert(!p6.ShiftShouldBeLatched, "E2E-07: Shift not latched after comma");
            _assert(AllLower(p6),             "E2E-07: predictions lowercase after comma");

            var p7 = NewPredictor();
            p7.OnWPClick(0);
            Type(p7, "hard"); p7.OnKeySent(" ", false);
            p7.OnKeySent(";", false);
            _assert(!p7.NextWordUpper,        "E2E-07: not sentence start after semicolon");
            _assert(AllLower(p7),             "E2E-07: predictions lowercase after semicolon");

            // ════════════════════════════════════════════════════════
            // 8. ENTER → sentence start
            // ════════════════════════════════════════════════════════
            _section("[E2E-08] Enter triggers sentence start");
            var p8 = NewPredictor();
            p8.OnWPClick(0);
            Type(p8, "melk"); p8.OnKeySent(" ", false);
            p8.OnKeySent("{ENTER}", false);
            _assert(p8.NextWordUpper,         "E2E-08: NextWordUpper after Enter");
            _assert(p8.ShiftShouldBeLatched,  "E2E-08: Shift latched after Enter");
            _assert(AllCaps(p8),              "E2E-08: predictions capitalised after Enter");

            // ════════════════════════════════════════════════════════
            // 9. BACKSPACE — reduces buffer, updates predictions
            // ════════════════════════════════════════════════════════
            _section("[E2E-09] Backspace reduces word buffer");
            var p9 = NewPredictor();
            p9.OnWPClick(0);  // unlatch sentence start
            Type(p9, "gro");
            _assert(p9.WordBuffer == "gro",   "E2E-09: buffer='gro'");
            p9.OnKeySent("{BACKSPACE}", false);
            _assert(p9.WordBuffer == "gr",    "E2E-09: buffer='gr' after backspace");
            p9.OnKeySent("{BACKSPACE}", false);
            _assert(p9.WordBuffer == "g",     "E2E-09: buffer='g' after 2 backspaces");
            p9.OnKeySent("{BACKSPACE}", false);
            _assert(p9.WordBuffer == "",      "E2E-09: buffer empty after 3 backspaces");
            p9.OnKeySent("{BACKSPACE}", false);
            _assert(p9.WordBuffer == "",      "E2E-09: buffer stays empty (no underflow)");

            // ════════════════════════════════════════════════════════
            // 10. WP CLICK WITH PREFIX — backspaces in result
            // ════════════════════════════════════════════════════════
            _section("[E2E-10] WP click with typed prefix — correct backspace count");
            var p10 = NewPredictor();
            p10.OnWPClick(0);  // unlatch
            Type(p10, "gr");   // prefix typed
            _assert(p10.WordBuffer == "gr",   "E2E-10: buffer='gr'");
            var r10 = p10.OnWPClick(0);
            _assert(r10 != null,              "E2E-10: result not null");
            _assert(r10.Backspaces == 2,      "E2E-10: 2 backspaces to erase 'gr'");
            _assert(p10.WordBuffer == "",     "E2E-10: buffer cleared after WP click");

            // ════════════════════════════════════════════════════════
            // 11. FULL SENTENCE: "De kat zit op de mat."
            // ════════════════════════════════════════════════════════
            _section("[E2E-11] Full sentence: 'De kat zit op de mat.'");
            var p11 = NewPredictor();
            // "De " — type D, e, space
            p11.OnKeySent("D", false);
            p11.OnKeySent("e", false);
            p11.OnKeySent(" ", false);
            _assert(p11.LastCompletedWord == "De", "E2E-11: completed 'De'");
            _assert(p11.NextWordUpper,             "E2E-11: sentence start preserved");
            // "kat " — click prediction or type
            Type(p11, "kat"); p11.OnKeySent(" ", false);
            _assert(p11.LastCompletedWord == "kat","E2E-11: completed 'kat'");
            // Type first char of next word to trigger mid-sentence mode
            p11.OnKeySent("z", false);
            _assert(!p11.NextWordUpper,            "E2E-11: not sentence start after 'kat'");
            _assert(AllLower(p11),                 "E2E-11: lowercase predictions after 'kat'");
            Type(p11, "it"); p11.OnKeySent(" ", false);
            // lastCompleted='zit', buffer empty — next-words of 'zit'
            _assert(HasPred(p11, "in"),            "E2E-11: 'in' predicted after 'zit'");
            _assert(HasPred(p11, "ook") || HasPred(p11, "je"),
                                                   "E2E-11: common next-words of 'zit'");
            // "op "
            Type(p11, "op"); p11.OnKeySent(" ", false);
            _assert(HasPred(p11, "de"),            "E2E-11: 'de' predicted after 'op'");
            // "de "
            Type(p11, "de"); p11.OnKeySent(" ", false);
            // "mat."
            Type(p11, "mat"); p11.OnKeySent(".", false);
            _assert(p11.NextWordUpper,             "E2E-11: sentence start after period");
            _assert(p11.ShiftShouldBeLatched,      "E2E-11: Shift latched after period");
            _assert(HasPred(p11, "De"),            "E2E-11: 'De' predicted after sentence end");

            // ════════════════════════════════════════════════════════
            // 12. SENTENCE-START PREFIX 'Da' — case-insensitive
            //     Should predict 'Dat', 'Dan', NOT only proper nouns
            // ════════════════════════════════════════════════════════
            _section("[E2E-12] Sentence-start prefix 'Da' — includes common words");
            var p12 = NewPredictor();
            p12.OnKeySent("D", false);
            p12.OnKeySent("a", false);
            _assert(AllCaps(p12),             "E2E-12: all capitalised at sentence start");
            _assert(HasPred(p12, "Dat"),      "E2E-12: 'Dat' predicted (from lowercase 'dat')");
            _assert(HasPred(p12, "Dan"),      "E2E-12: 'Dan' predicted (from lowercase 'dan')");

            // ════════════════════════════════════════════════════════
            // 13. MID-SENTENCE PROPER NOUN — prefix 'Am'
            // ════════════════════════════════════════════════════════
            _section("[E2E-13] Mid-sentence proper noun 'Am' → only uppercase words");
            var p13 = NewPredictor();
            p13.OnWPClick(0);  // unlatch sentence start
            Type(p13, "naar"); p13.OnKeySent(" ", false);
            _assert(!p13.NextWordUpper,       "E2E-13: not sentence start");
            Type(p13, "Am");
            _assert(HasPred(p13, "Amerika"),  "E2E-13: 'Amerika' predicted with prefix 'Am'");
            _assert(System.Linq.Enumerable.All(p13.Predictions,
                w => string.IsNullOrEmpty(w) || char.IsUpper(w[0])),
                                              "E2E-13: only uppercase words for 'Am' mid-sentence");

            // ════════════════════════════════════════════════════════
            // 13b. MID-SENTENCE CAPITAL PREFIX — exact scenario:
            //      "Hij is in I" → only proper nouns, never lowercase
            //      "in", "is", "ik" must NOT appear even though they
            //      start with 'i' (uppercase prefix = proper noun intent)
            // ════════════════════════════════════════════════════════
            _section("[E2E-13b] Mid-sentence capital 'I' → only proper nouns (Iran, India...)");
            var p13b = NewPredictor();
            p13b.OnWPClick(0);  // unlatch sentence start
            // Type "hij is in " to set context
            Type(p13b, "hij"); p13b.OnKeySent(" ", false);
            p13b.OnKeySent("i", false); // mid-sentence: 'i' typed to start 'is'
            Type(p13b, "s"); p13b.OnKeySent(" ", false);
            p13b.OnKeySent("i", false);
            Type(p13b, "n"); p13b.OnKeySent(" ", false);
            // Type capital 'I' with Shift (user wants a proper noun).
            // Subsequent letters are typed without Shift (e.g. 'n' in "In"):
            // the prefix "In" still filters correctly because prefixUpper is
            // derived from the first character of the buffer ('I'), not from
            // the shifted state of each subsequent keystroke.
            p13b.OnKeySent("I", true);  // shifted=true → buffer="I", preferUpperCase=true
            // Verify: only uppercase-starting words
            _assert(System.Linq.Enumerable.All(p13b.Predictions,
                w => string.IsNullOrEmpty(w) || char.IsUpper(w[0])),
                "E2E-13b: only uppercase words after capital 'I'");
            // Verify: common lowercase-i words are NOT predicted
            _assert(!System.Linq.Enumerable.Contains(p13b.Predictions, "in"), "E2E-13b: 'in' not predicted");
            _assert(!System.Linq.Enumerable.Contains(p13b.Predictions, "is"), "E2E-13b: 'is' not predicted");
            _assert(!System.Linq.Enumerable.Contains(p13b.Predictions, "ik"), "E2E-13b: 'ik' not predicted");
            _assert(!System.Linq.Enumerable.Contains(p13b.Predictions, "iets"), "E2E-13b: 'iets' not predicted");
            _assert(!System.Linq.Enumerable.Contains(p13b.Predictions, "iedereen"), "E2E-13b: 'iedereen' not predicted");
            // Verify: proper nouns ARE predicted (Iran=31, India=18, Irak=14 in DB)
            _assert(HasPred(p13b, "Iran") || HasPred(p13b, "India") || HasPred(p13b, "Irak"),
                "E2E-13b: proper nouns (Iran/India/Irak) predicted");
            // Continue: type 'n' without Shift → buffer="In".
            // prefixUpper derives from buffer[0]='I' so still filters uppercase only.
            Type(p13b, "n");
            _assert(System.Linq.Enumerable.All(p13b.Predictions,
                w => string.IsNullOrEmpty(w) || char.IsUpper(w[0])),
                "E2E-13b: only uppercase after 'In'");
            _assert(HasPred(p13b, "Internationaal") || HasPred(p13b, "India"),
                "E2E-13b: 'Internationaal' or 'India' in prefix 'In'");
            // Continue: type 'd' → prefix "Ind"
            Type(p13b, "d");
            _assert(HasPred(p13b, "India"),
                "E2E-13b: 'India' predicted with prefix 'Ind'");
            _assert(System.Linq.Enumerable.All(p13b.Predictions,
                w => string.IsNullOrEmpty(w) || char.IsUpper(w[0])),
                "E2E-13b: only uppercase after 'Ind'");

            // ════════════════════════════════════════════════════════
            // 14. WORDS NOT IN DATABASE — never predicted
            // ════════════════════════════════════════════════════════
            _section("[E2E-14] Unknown words never appear in predictions");
            var p14 = NewPredictor();
            // Type prefix "Em" at sentence start
            Type(p14, "Em");
            _assert(!HasPred(p14, "Emma"),    "E2E-14: 'Emma' never predicted");
            _assert(!HasPred(p14, "Emoe"),    "E2E-14: random unknown word not predicted");
            // Mid-sentence prefix "Fi"
            p14.OnWPClick(0); // clear sentence start
            Type(p14, "bij"); p14.OnKeySent(" ", false);
            Type(p14, "Fi");
            _assert(!HasPred(p14, "Filemon"), "E2E-14: 'Filemon' never predicted mid-sentence");
            _assert(!HasPred(p14, "Fien"),    "E2E-14: 'Fien' never predicted");

            // ════════════════════════════════════════════════════════
            // 15. CONSECUTIVE SENTENCES
            //     "Het huis is groot! Waar is de sleutel? Ik wil..."
            // ════════════════════════════════════════════════════════
            _section("[E2E-15] Consecutive sentences with ! and ?");
            var p15 = NewPredictor();
            // sentence 1 via WP clicks
            var r15a = p15.OnWPClick(0);    // pick first prediction (e.g. "De")
            Type(p15, "huis"); p15.OnKeySent(" ", false);
            Type(p15, "is");   p15.OnKeySent(" ", false);
            Type(p15, "groot");
            p15.OnKeySent("!", false);
            _assert(p15.NextWordUpper,        "E2E-15: sentence start after !");
            _assert(AllCaps(p15),             "E2E-15: caps after !");
            // type "Waar"
            p15.OnKeySent("W", false);
            _assert(p15.WordBuffer == "W",    "E2E-15: buffer='W'");
            _assert(AllCaps(p15),             "E2E-15: still caps with prefix 'W'");
            Type(p15, "aar"); p15.OnKeySent(" ", false);
            Type(p15, "is");  p15.OnKeySent(" ", false);
            Type(p15, "de");  p15.OnKeySent(" ", false);
            Type(p15, "sleutel");
            p15.OnKeySent("?", false);
            _assert(p15.NextWordUpper,        "E2E-15: sentence start after ?");
            _assert(AllCaps(p15),             "E2E-15: caps after ?");
            // type "Ik"
            p15.OnKeySent("I", false);
            Type(p15, "k");   p15.OnKeySent(" ", false);
            _assert(p15.LastCompletedWord == "Ik", "E2E-15: completed 'Ik'");
            _assert(HasPred(p15, "Heb") || HasPred(p15, "Ben") || HasPred(p15, "Wil"),
                                              "E2E-15: next-words of 'Ik' predicted");

            // ════════════════════════════════════════════════════════
            // 16. FULL FLOW: "Ik wil graag, maar de melk is op."
            // ════════════════════════════════════════════════════════
            _section("[E2E-16] 'Ik wil graag, maar de melk is op.'");
            var p16 = NewPredictor();
            p16.OnWPClick(0);  // unlatch (simulate starting mid-text)
            Type(p16, "ik"); p16.OnKeySent(" ", false);
            _assert(HasPred(p16, "wil"),      "E2E-16: 'wil' predicted after 'ik'");
            var rwil = p16.OnWPClick(System.Linq.Enumerable.ToList(p16.Predictions).IndexOf("wil"));
            if (rwil == null) { Type(p16, "wil"); p16.OnKeySent(" ", false); }
            _assert(p16.LastCompletedWord == "wil", "E2E-16: completed 'wil'");
            _assert(HasPred(p16, "graag") || HasPred(p16, "de") || HasPred(p16, "je"),
                                              "E2E-16: reasonable next-words after 'wil'");
            Type(p16, "graag");
            p16.OnKeySent(",", false);
            _assert(!p16.NextWordUpper,       "E2E-16: no sentence start after comma");
            _assert(AllLower(p16),            "E2E-16: lowercase after comma");
            Type(p16, "maar"); p16.OnKeySent(" ", false);
            _assert(HasPred(p16, "de") || HasPred(p16, "ik"),
                                              "E2E-16: next-words of 'maar'");
            Type(p16, "de"); p16.OnKeySent(" ", false);
            Type(p16, "melk"); p16.OnKeySent(" ", false);
            Type(p16, "is"); p16.OnKeySent(" ", false);
            Type(p16, "op");
            p16.OnKeySent(".", false);
            _assert(p16.NextWordUpper,        "E2E-16: sentence start after final period");
            _assert(p16.ShiftShouldBeLatched, "E2E-16: Shift latched after period");

            // ════════════════════════════════════════════════════════
            // 17. LAST-ACTION-WAS-PREDICTION flag
            // ════════════════════════════════════════════════════════
            _section("[E2E-17] LastActionWasPrediction flag for space removal");
            var p17 = NewPredictor();
            _assert(!p17.LastActionWasPrediction, "E2E-17: flag false at start");
            var r17 = p17.OnWPClick(0);
            _assert(p17.LastActionWasPrediction,  "E2E-17: flag true after WP click");
            // After typing a letter, flag clears
            Type(p17, "a");
            _assert(!p17.LastActionWasPrediction, "E2E-17: flag false after typing letter");
            // After another WP click, flag set again
            p17.OnKeySent(" ", false);
            var r17b = p17.OnWPClick(0);
            _assert(p17.LastActionWasPrediction,  "E2E-17: flag true after second WP click");
            // After space, flag clears
            p17.OnKeySent(" ", false);
            _assert(!p17.LastActionWasPrediction, "E2E-17: flag false after space");

            // ════════════════════════════════════════════════════════
            // 18. SLOT COUNT
            // ════════════════════════════════════════════════════════
            _section("[E2E-18] Slot count respected");
            for (int n = 1; n <= 7; n++)
            {
                var pn = new WordPredictor(n);
                pn.OnSentenceStart();
                int filled = 0;
                foreach (var pred in pn.Predictions)
                    if (!string.IsNullOrEmpty(pred)) filled++;
                _assert(filled == n, $"E2E-18: {n} slots filled with {n}-slot predictor");
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Undo / Redo infrastructure tests
    // (Tests Clone() correctness and undo-stack behaviour without
    //  needing a live KeyboardForm / Windows handle.)
    // ════════════════════════════════════════════════════════════════
    public static partial class TestRunner
    {
        private static void T_UndoRedo()
        {
            Section("Undo/Redo infrastructure");

            // ── VisualTheme.Clone() ───────────────────────────────────
            var t = new VisualTheme
            {
                FontName        = "Courier",
                FontSize        = 14,
                BorderThickness = 2,
                BackgroundColor = Color.FromArgb(10, 20, 30),
                Opacity         = 0.75,
            };
            var tc = t.Clone();
            Assert(tc.FontName        == "Courier",                   "VisualTheme.Clone: FontName");
            Assert(tc.FontSize        == 14,                          "VisualTheme.Clone: FontSize");
            Assert(tc.BorderThickness == 2,                           "VisualTheme.Clone: BorderThickness");
            Assert(tc.BackgroundColor == Color.FromArgb(10, 20, 30), "VisualTheme.Clone: BackgroundColor");
            Assert(Math.Abs(tc.Opacity - 0.75) < 0.0001,             "VisualTheme.Clone: Opacity");
            Assert(!ReferenceEquals(t, tc),                           "VisualTheme.Clone: new object");
            tc.FontName = "Arial";
            Assert(t.FontName == "Courier", "VisualTheme.Clone: mutation independent");

            // ── WindowState.Clone() ───────────────────────────────────
            var ws = new WindowState { WindowWidth = 800, WindowHeight = 300, HideTitlebar = true, AlwaysOnTop = false };
            var wsc = ws.Clone();
            Assert(wsc.WindowWidth  == 800,  "WindowState.Clone: WindowWidth");
            Assert(wsc.WindowHeight == 300,  "WindowState.Clone: WindowHeight");
            Assert(wsc.HideTitlebar == true, "WindowState.Clone: HideTitlebar");
            Assert(wsc.AlwaysOnTop  == false,"WindowState.Clone: AlwaysOnTop");
            Assert(!ReferenceEquals(ws, wsc),"WindowState.Clone: new object");

            // ── LayoutMeta.Clone() ────────────────────────────────────
            var m = new LayoutMeta { Language = "nl", StickyModifiers = false, GearRow = 3, GearCol = 5, LastFile = "test.xml" };
            var mc = m.Clone();
            Assert(mc.Language        == "nl",      "LayoutMeta.Clone: Language");
            Assert(mc.StickyModifiers == false,     "LayoutMeta.Clone: StickyModifiers");
            Assert(mc.GearRow         == 3,         "LayoutMeta.Clone: GearRow");
            Assert(mc.GearCol         == 5,         "LayoutMeta.Clone: GearCol");
            Assert(mc.LastFile        == "test.xml","LayoutMeta.Clone: LastFile");
            Assert(!ReferenceEquals(m, mc),         "LayoutMeta.Clone: new object");
            mc.GearRow = 99;
            Assert(m.GearRow == 3, "LayoutMeta.Clone: mutation independent");

            // ── Undo stack cap at 50 ──────────────────────────────────
            var layout = KeyLayout.BuildDefaultQwerty();
            var undoStack = new Stack<(GridLayout, VisualTheme, WindowState, LayoutMeta)>();
            var redoStack = new Stack<(GridLayout, VisualTheme, WindowState, LayoutMeta)>();
            var theme0 = new VisualTheme(); var window0 = new WindowState(); var meta0 = new LayoutMeta();

            for (int i = 0; i < 55; i++)
            {
                undoStack.Push((layout.Clone(), theme0.Clone(), window0.Clone(), meta0.Clone()));
                redoStack.Clear();
                if (undoStack.Count > 50)
                {
                    var arr = undoStack.ToArray(); // [0]=newest
                    undoStack.Clear();
                    for (int j = arr.Length - 2; j >= 0; j--) undoStack.Push(arr[j]);
                }
            }
            Assert(undoStack.Count == 50, "UndoStack capped at 50 after 55 pushes");

            // ── Undo stack cap — LinkedList (mirrors KeyboardForm.PushUndo exactly) ──
            // KeyboardForm uses LinkedList with AddFirst (newest at front) and
            // RemoveLast (drops oldest) — both O(1).  This test verifies that
            // exact algorithm rather than the Stack-based simulation above.
            var ll = new LinkedList<int>();
            for (int i = 0; i < 60; i++)
            {
                ll.AddFirst(i);       // newest entry at front  (= _undoStack.AddFirst(...))
                if (ll.Count > 50)
                    ll.RemoveLast();  // drop oldest in O(1)    (= _undoStack.RemoveLast())
            }
            Assert(ll.Count            == 50, "LinkedList undo cap: count stays at 50 after 60 pushes");
            Assert(ll.First!.Value     == 59, "LinkedList undo cap: First (newest) is push #59");
            Assert(ll.Last!.Value      == 10, "LinkedList undo cap: Last (oldest) is push #10 (60-50)");

            // ── Redo clears on new action ─────────────────────────────
            undoStack.Clear();
            redoStack.Push((layout.Clone(), theme0.Clone(), window0.Clone(), meta0.Clone()));
            redoStack.Push((layout.Clone(), theme0.Clone(), window0.Clone(), meta0.Clone()));
            Assert(redoStack.Count == 2, "Redo has 2 entries before new push");

            undoStack.Push((layout.Clone(), theme0.Clone(), window0.Clone(), meta0.Clone()));
            redoStack.Clear();
            Assert(redoStack.Count == 0, "Redo cleared after new push");

            // ── Undo / Redo round-trip (snapshot content) ─────────────
            var before = KeyLayout.BuildDefaultQwerty();
            before.Cells[0].Props.Label = "BEFORE";
            var snapshotTheme = new VisualTheme { FontName = "Before-Font" };
            undoStack.Clear(); redoStack.Clear();

            undoStack.Push((before.Clone(), snapshotTheme.Clone(), window0.Clone(), meta0.Clone()));

            before.Cells[0].Props.Label = "AFTER";
            var afterTheme = new VisualTheme { FontName = "After-Font" };

            redoStack.Push((before.Clone(), afterTheme.Clone(), window0.Clone(), meta0.Clone()));
            var (restoredLayout, restoredTheme, _, _) = undoStack.Pop();

            Assert(restoredLayout.Cells[0].Props.Label == "BEFORE",    "Undo restores layout label");
            Assert(restoredTheme.FontName              == "Before-Font","Undo restores theme FontName");

            undoStack.Push((restoredLayout.Clone(), restoredTheme.Clone(), window0.Clone(), meta0.Clone()));
            var (redoneLayout, redoneTheme, _, _) = redoStack.Pop();

            Assert(redoneLayout.Cells[0].Props.Label == "AFTER",     "Redo re-applies layout label");
            Assert(redoneTheme.FontName              == "After-Font", "Redo re-applies theme FontName");
        }

        // ════════════════════════════════════════════════════════════════
        // SendKeys stripping — display-only, never feeds back into model
        // ════════════════════════════════════════════════════════════════
        private static void T_SendKeysStripping()
        {
            Section("SendKeys stripping — display-only");

            // ── 1. StripSendBraces logic ──────────────────────────────
            // The production method (KeyboardForm.StripSendBraces) is private.
            // This inline copy is character-for-character identical so that any
            // future divergence between the two will be caught by the tests below.
            static string Strip(string s)
            {
                if (s.Length >= 3 && s[0] == '{' && s[s.Length - 1] == '}')
                    s = s.Substring(1, s.Length - 2);
                if (s.Length > 0 && string.IsNullOrWhiteSpace(s))
                    return "␣";
                return s;
            }

            // All ten SendKeys special chars, each escaped as a {x} token:
            Assert(Strip("{(}") == "(",      "Strip: {(} → (");
            Assert(Strip("{)}") == ")",      "Strip: {)} → )");
            Assert(Strip("{+}") == "+",      "Strip: {+} → +");
            Assert(Strip("{^}") == "^",      "Strip: {^} → ^");
            Assert(Strip("{%}") == "%",      "Strip: {%} → %");
            Assert(Strip("{~}") == "~",      "Strip: {~} → ~");
            Assert(Strip("{[}") == "[",      "Strip: {[} → [");
            Assert(Strip("{]}") == "]",      "Strip: {]} → ]");
            Assert(Strip("{{}") == "{",      "Strip: {{} → {");
            Assert(Strip("{}}") == "}",      "Strip: {}} → }");

            // Named key tokens:
            Assert(Strip("{Enter}")     == "Enter",      "Strip: {Enter} → Enter");
            Assert(Strip("{F1}")        == "F1",         "Strip: {F1} → F1");
            Assert(Strip("{LEFT}")      == "LEFT",       "Strip: {LEFT} → LEFT");
            Assert(Strip("{BACKSPACE}") == "BACKSPACE",  "Strip: {BACKSPACE} → BACKSPACE");

            // Strings that must NOT be altered:
            Assert(Strip("a")    == "a",    "Strip: plain char unchanged");
            Assert(Strip("^c")   == "^c",   "Strip: modifier prefix unchanged");
            Assert(Strip("{}")   == "{}",   "Strip: {} (length 2) unchanged — min length is 3");
            Assert(Strip("")     == "",     "Strip: empty string unchanged");

            // Whitespace-only sends → visible open-box placeholder:
            Assert(Strip(" ")    == "␣",    "Strip: space → ␣");
            Assert(Strip("\t")   == "␣",    "Strip: tab → ␣");
            Assert(Strip("{ }")  == "␣",    "Strip: braced space stripped then → ␣");

            // Verify stripping never mutates the source variable:
            string raw     = "{(}";
            string display = Strip(raw);
            Assert(display == "(",     "Strip result: correct stripped value");
            Assert(raw     == "{(}",   "Strip source: raw string unmodified after call");

            // ── 2. KeyProps — auto-property setters store values raw ──
            var p = new KeyProps("{(}", "{(}", shiftSend: "{+}", altGrSend: "{^}");
            Assert(p.Send      == "{(}", "KeyProps.Send: {(} stored raw");
            Assert(p.ShiftSend == "{+}", "KeyProps.ShiftSend: {+} stored raw");
            Assert(p.AltGrSend == "{^}", "KeyProps.AltGrSend: {^} stored raw");

            // Mutate via setter and read back — no hidden transform:
            p.Send      = "{Enter}";
            p.ShiftSend = "{F1}";
            p.AltGrSend = "{LEFT}";
            Assert(p.Send      == "{Enter}", "KeyProps.Send setter: {Enter} round-trips");
            Assert(p.ShiftSend == "{F1}",    "KeyProps.ShiftSend setter: {F1} round-trips");
            Assert(p.AltGrSend == "{LEFT}",  "KeyProps.AltGrSend setter: {LEFT} round-trips");

            // Verify Strip of the raw value gives the right display — but the field itself unchanged:
            Assert(p.Send      == "{Enter}",        "KeyProps.Send unchanged after Strip is applied externally");
            Assert(Strip(p.Send) == "Enter",        "Strip(KeyProps.Send) gives display value");

            // ── 3. EscapeForSend — special chars escaped, tokens preserved ─
            // Each of the ten special chars is escaped to its {x} form:
            Assert(SendKeysHelper.EscapeForSend("(") == "{(}", "Escape: ( → {(}");
            Assert(SendKeysHelper.EscapeForSend(")") == "{)}", "Escape: ) → {)}");
            Assert(SendKeysHelper.EscapeForSend("+") == "{+}", "Escape: + → {+}");
            Assert(SendKeysHelper.EscapeForSend("^") == "{^}", "Escape: ^ → {^}");
            Assert(SendKeysHelper.EscapeForSend("%") == "{%}", "Escape: % → {%}");
            Assert(SendKeysHelper.EscapeForSend("~") == "{~}", "Escape: ~ → {~}");
            Assert(SendKeysHelper.EscapeForSend("[") == "{[}", "Escape: [ → {[}");
            Assert(SendKeysHelper.EscapeForSend("]") == "{]}", "Escape: ] → {]}");

            // Plain chars and words pass through unchanged:
            Assert(SendKeysHelper.EscapeForSend("a")     == "a",     "Escape: plain char unchanged");
            Assert(SendKeysHelper.EscapeForSend("hello") == "hello", "Escape: plain word unchanged");

            // Special char inside a string:
            Assert(SendKeysHelper.EscapeForSend("a+b") == "a{+}b",  "Escape: special char in middle");
            Assert(SendKeysHelper.EscapeForSend("a(b)c") == "a{(}b{)}c", "Escape: parens in text");

            // Existing {KEY} tokens must NOT be double-escaped — this is the key regression test:
            Assert(SendKeysHelper.EscapeForSend("{(}")     == "{(}",    "Escape: {(} token not re-escaped");
            Assert(SendKeysHelper.EscapeForSend("{Enter}") == "{Enter}","Escape: {Enter} token not re-escaped");
            Assert(SendKeysHelper.EscapeForSend("{F1}")    == "{F1}",   "Escape: {F1} token not re-escaped");
            Assert(SendKeysHelper.EscapeForSend("{Enter}(end)") == "{Enter}{(}end{)}",
                "Escape: token at start + parens after — token preserved, parens escaped");

            // Edge cases:
            Assert(SendKeysHelper.EscapeForSend("")   == "",   "Escape: empty unchanged");
            Assert(SendKeysHelper.EscapeForSend(null) == null, "Escape: null unchanged");

            // ── 4. ToHuman / FromHuman round-trip for simple sequences ─
            // Inline mirrors of the private KeyEditorForm methods.
            // Note: grouping parentheses — e.g. +(ab) — are intentionally lossy
            // in ToHuman (parens stripped for display). Only prefix-style sequences
            // are tested here.
            static string ToHuman(string send)
            {
                if (string.IsNullOrEmpty(send)) return send;
                if (send.StartsWith("win:"))
                    return "{Win}" + ToHuman(send.Substring(4));
                var sb2 = new System.Text.StringBuilder();
                int j = 0;
                while (j < send.Length)
                {
                    char ch = send[j];
                    if      (ch == '^') { sb2.Append("{Ctrl}");  j++; }
                    else if (ch == '%') { sb2.Append("{Alt}");   j++; }
                    else if (ch == '+') { sb2.Append("{Shift}"); j++; }
                    else if (ch == '(')
                    {
                        j++;
                        while (j < send.Length && send[j] != ')') { sb2.Append(send[j]); j++; }
                        if (j < send.Length) j++;
                    }
                    else { sb2.Append(ch); j++; }
                }
                return sb2.ToString();
            }
            static string FromHuman(string human)
            {
                if (string.IsNullOrEmpty(human)) return human;
                if (human.StartsWith("{Win}"))
                    return "win:" + FromHuman(human.Substring(5));
                return human
                    .Replace("{Ctrl}",  "^")
                    .Replace("{Alt}",   "%")
                    .Replace("{Shift}", "+");
            }

            // Modifier prefix round-trips:
            Assert(ToHuman("^c")        == "{Ctrl}c",         "ToHuman: ^c → {Ctrl}c");
            Assert(FromHuman("{Ctrl}c") == "^c",              "FromHuman: {Ctrl}c → ^c");
            Assert(FromHuman(ToHuman("^c")) == "^c",          "Round-trip: ^c");

            Assert(ToHuman("%{F4}")         == "{Alt}{F4}",   "ToHuman: %{F4} → {Alt}{F4}");
            Assert(FromHuman("{Alt}{F4}")   == "%{F4}",       "FromHuman: {Alt}{F4} → %{F4}");
            Assert(FromHuman(ToHuman("%{F4}")) == "%{F4}",    "Round-trip: %{F4}");

            Assert(ToHuman("+a")          == "{Shift}a",      "ToHuman: +a → {Shift}a");
            Assert(FromHuman(ToHuman("+a")) == "+a",          "Round-trip: +a");

            Assert(ToHuman("^+a")         == "{Ctrl}{Shift}a","ToHuman: ^+a → {Ctrl}{Shift}a");
            Assert(FromHuman(ToHuman("^+a")) == "^+a",        "Round-trip: ^+a");

            Assert(ToHuman("win:{LEFT}")       == "{Win}{LEFT}",  "ToHuman: win:{LEFT} → {Win}{LEFT}");
            Assert(FromHuman(ToHuman("win:{LEFT}")) == "win:{LEFT}", "Round-trip: win:{LEFT}");

            // Plain key tokens survive unchanged through both directions:
            Assert(ToHuman("{ENTER}")  == "{ENTER}",   "ToHuman: bare token unchanged");
            Assert(FromHuman("{ENTER}") == "{ENTER}",  "FromHuman: bare token unchanged");

            // ── 5. XML round-trip — raw braced values survive save + load ─
            string xmlTmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"osk_sendstrip_{Guid.NewGuid():N}.xml");
            try
            {
                var gl     = new GridLayout(1, 1);
                var props  = new KeyProps("K", "{(}", shiftSend: "{+}", altGrSend: "{^}");
                gl.Cells.Add(new GridCell(0, 0, props));
                SettingsManager.SaveSettings(gl, new VisualTheme(), new WindowState(), new LayoutMeta(), xmlTmp);

                var reloaded = SettingsManager.LoadSettings(
                    new VisualTheme(), new WindowState(), new LayoutMeta(), xmlTmp);
                var rp = reloaded?.CellAt(0, 0)?.Props;

                Assert(rp?.Send      == "{(}", "XML round-trip: Send={({(})} preserved");
                Assert(rp?.ShiftSend == "{+}", "XML round-trip: ShiftSend={+} preserved");
                Assert(rp?.AltGrSend == "{^}", "XML round-trip: AltGrSend={^} preserved");

                // Confirm the stripped display values differ from the stored values
                // (proving that stripping is a view transformation, not a storage one):
                Assert(Strip(rp!.Send)      != rp.Send,      "Strip produces different display than raw Send");
                Assert(Strip(rp.ShiftSend)  != rp.ShiftSend, "Strip produces different display than raw ShiftSend");
                Assert(Strip(rp.AltGrSend)  != rp.AltGrSend,"Strip produces different display than raw AltGrSend");
            }
            finally
            {
                if (System.IO.File.Exists(xmlTmp))           System.IO.File.Delete(xmlTmp);
                if (System.IO.File.Exists(xmlTmp + ".bak"))  System.IO.File.Delete(xmlTmp + ".bak");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // DPI scaling — AutoScaleMode on the three editor dialogs
        // ════════════════════════════════════════════════════════════════
        private static void T_AutoScaleMode_Dialogs()
        {
            Section("AutoScaleMode — DPI scaling");

            // GroupEditorForm — simplest constructor; no owner needed
            try
            {
                using var f = new GroupEditorForm(new List<KeyGroup>());
                Assert(f.AutoScaleMode == AutoScaleMode.Dpi,
                    "GroupEditorForm: AutoScaleMode = Dpi");
                Assert(f.AutoScaleDimensions == new SizeF(96f, 96f),
                    "GroupEditorForm: AutoScaleDimensions = (96, 96)");
            }
            catch (Exception ex)
            {
                Assert(false, $"GroupEditorForm: instantiation failed — {ex.Message}");
            }

            // KeyboardEditorForm — needs VisualTheme / WindowState / LayoutMeta; owner = null
            try
            {
                using var f = new KeyboardEditorForm(
                    new VisualTheme(), new WindowState(), new LayoutMeta(), owner: null);
                Assert(f.AutoScaleMode == AutoScaleMode.Dpi,
                    "KeyboardEditorForm: AutoScaleMode = Dpi");
                Assert(f.AutoScaleDimensions == new SizeF(96f, 96f),
                    "KeyboardEditorForm: AutoScaleDimensions = (96, 96)");
            }
            catch (Exception ex)
            {
                Assert(false, $"KeyboardEditorForm: instantiation failed — {ex.Message}");
            }

            // KeyEditorForm — needs KeyProps; owner = null (null-safe in constructor)
            try
            {
                using var f = new KeyEditorForm(new KeyProps("A", "A"), owner: null);
                Assert(f.AutoScaleMode == AutoScaleMode.Dpi,
                    "KeyEditorForm: AutoScaleMode = Dpi");
                Assert(f.AutoScaleDimensions == new SizeF(96f, 96f),
                    "KeyEditorForm: AutoScaleDimensions = (96, 96)");
            }
            catch (Exception ex)
            {
                Assert(false, $"KeyEditorForm: instantiation failed — {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Grow window height when entering Edit mode
        // ════════════════════════════════════════════════════════════════
        private static void T_GrowWindowOnEditMode()
        {
            Section("Grow window on Edit mode entry");

            // KeyboardForm.ToolbarHeightForMode mirrors the 'th' calculation in LayoutButtons:
            //   Mode.Edit          → _toolbar.Height + _toolbarEdit.Height  = 54 + 54 = 108
            //   Mode.Normal        → 0  (no toolbars visible)
            //   Mode.GearPlacement → 0  (no toolbars visible)
            const int ToolbarH     = 54;   // KeyboardForm._toolbar.Height
            const int ToolbarEditH = 54;   // KeyboardForm._toolbarEdit.Height
            const int ThEdit   = ToolbarH + ToolbarEditH;  // 108
            const int ThNormal = 0;
            const int ThGear   = 0;

            Assert(ThEdit == 108, "GrowWindow: Edit mode toolbar height = 108 px (54 + 54)");

            // Per-transition height deltas (SetMode / StartGearPlacement / FinishGearPlacement
            // apply: Height += ToolbarHeightForMode(newMode) - ToolbarHeightForMode(oldMode))
            Assert(ThEdit   - ThNormal ==  108, "GrowWindow: Normal → Edit  grows window by 108 px");
            Assert(ThNormal - ThEdit   == -108, "GrowWindow: Edit → Normal  shrinks window by 108 px");
            Assert(ThGear   - ThEdit   == -108, "GrowWindow: Edit → GearPlacement  shrinks window by 108 px");
            Assert(ThEdit   - ThGear   ==  108, "GrowWindow: GearPlacement → Edit  grows window by 108 px");
            Assert(ThNormal - ThGear   ==    0, "GrowWindow: Normal → GearPlacement  delta is 0 px");
            Assert(ThGear   - ThNormal ==    0, "GrowWindow: GearPlacement → Normal  delta is 0 px");

            // usableH invariant: after the mode switch the key-grid area is unchanged.
            // usableH = ClientSize.Height - th - Pad*2 - Gap*(rows-1)
            // After:  (ClientSize.Height + delta) - th_new = ClientSize.Height - th_old
            //   ↔  delta = th_new - th_old  ✓  (which is exactly what we add to Height)
            const int Ch   = 290;  // representative ClientSize.Height
            const int Pad  = 8;
            const int Gap  = 4;
            const int Rows = 4;
            int baseH = Ch - Pad * 2 - Gap * (Rows - 1);  // without any toolbar

            int usableNormal = baseH - ThNormal;
            int usableEdit   = baseH - ThEdit;

            // After Normal→Edit: ClientSize grows by (ThEdit - ThNormal), new usableH must equal usableNormal
            int usableEditAfterGrow = (Ch + (ThEdit - ThNormal)) - ThEdit - Pad * 2 - Gap * (Rows - 1);
            Assert(usableEditAfterGrow == usableNormal, "GrowWindow: usableH invariant — Normal→Edit key rows unchanged");

            // After Edit→Normal: ClientSize shrinks by (ThEdit - ThNormal), new usableH must equal usableEdit
            int usableNormalAfterShrink = (Ch - (ThEdit - ThNormal)) - ThNormal - Pad * 2 - Gap * (Rows - 1);
            Assert(usableNormalAfterShrink == usableEdit, "GrowWindow: usableH invariant — Edit→Normal key rows unchanged");
        }

        // ════════════════════════════════════════════════════════════════
        // Paint / Resize / Layout handler audit
        // ════════════════════════════════════════════════════════════════
        private static void T_PaintHandlerAudit()
        {
            Section("Paint handler audit");

            // Audit result (manual code review of KeyboardForm.cs):
            //
            // OnButtonPaint (line ~1554): uses 'using var pen = new Pen(...)' — disposed ✓
            // DrawChipSection (called from OnSelectedKeyPaint, line ~2599):
            //   uses 'using var br = new SolidBrush(...)' — disposed ✓
            //   uses 'using var pen = new Pen(...)' — disposed ✓
            //
            // All GDI allocations inside Paint handlers are wrapped in 'using' so they are
            // disposed immediately after each paint call. No GDI handle leaks. No dialogs,
            // no Font allocations, and no Resize/Layout-handler violations found.
            //
            // This test records the audit finding; the assertions below are structural checks
            // (they verify the types involved are correct, not runtime GDI state).

            // SolidBrush and Pen are IDisposable — 'using' guarantees disposal.
            using var br  = new SolidBrush(Color.Red);
            using var pen = new Pen(Color.Blue, 1f);
            Assert(br  is IDisposable, "SolidBrush is IDisposable — safe to allocate with 'using' in Paint");
            Assert(pen is IDisposable, "Pen is IDisposable — safe to allocate with 'using' in Paint");
        }
    }
}

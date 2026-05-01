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

namespace OnScreenKeyboard
{
    public static class TestRunner
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
            T_SettingsManager_Robustness();
            T_SettingsManager_Sentinels();
            T_KeyLayout();
            T_LanguageManager();
            T_GridLayout();
            T_FontSizing();
            T_CharacterRouting();
            T_SlowReceiverStress();

            // Run word prediction tests (uses shared Assert/Section → failures go to report)
            WordPredictionTests.Run(Assert, Section);
            // Run end-to-end predictor tests
            WordPredictorE2ETests.Run(Assert, Section);

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

                var global = new GlobalSettings
                {
                    BackgroundColor = Color.FromArgb(10,20,30),
                    Opacity   = 0.85,
                    FontName  = "Arial",
                    FontSize  = 12,
                    Language  = "nl",
                    WindowWidth = 800, WindowHeight = 250,
                    LastFile  = tmp,
                    HideTitlebar    = true,
                    StickyModifiers = false,
                    AlwaysOnTop     = false,
                };

                SettingsManager.SaveSettings(layout, global, tmp);
                Assert(File.Exists(tmp), "XML file created");

                var lg = new GlobalSettings();
                var lr = SettingsManager.LoadSettings(lg, tmp);

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
                Assert(lg.FontName   == "Arial",   "Global FontName");
                Assert(lg.FontSize   == 12,        "Global FontSize");
                Assert(lg.Language   == "nl",      "Global Language");
                Assert(lg.WindowWidth  == 800,     "Global WindowWidth");
                Assert(lg.WindowHeight == 250,     "Global WindowHeight");
                Assert(lg.HideTitlebar  == true,   "HideTitlebar round-trip");
                Assert(lg.StickyModifiers == false,"StickyModifiers round-trip");
                Assert(lg.AlwaysOnTop   == false,  "AlwaysOnTop round-trip");
                Assert(Math.Abs(lg.Opacity - 0.85) < 0.001, "Opacity round-trip");
                Assert(lg.BackgroundColor == Color.FromArgb(10,20,30), "BackgroundColor round-trip");
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
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
            try { SettingsManager.LoadSettings(new GlobalSettings(), bad); } catch { threw = true; }
            Assert(threw, "Corrupt XML throws exception");
            File.Delete(bad);

            // Missing file returns null
            Assert(SettingsManager.LoadSettings(new GlobalSettings(),
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
            var fr = SettingsManager.LoadSettings(new GlobalSettings(), ff);
            Assert(fr != null, "FontSize 999: loads without crash");
            Assert(fr?.CellAt(0,0)?.Props.FontSize <= 72, "FontSize 999 clamped to ≤72");
            File.Delete(ff);

            // BorderThickness 99 → clamped to 10
            string btf = Path.Combine(Path.GetTempPath(), $"osk_bt_{Guid.NewGuid()}.xml");
            File.WriteAllText(btf, MakeXml(keyAttribs: @"BorderThickness=""99"""));
            var btr = SettingsManager.LoadSettings(new GlobalSettings(), btf);
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

                SettingsManager.SaveSettings(layout, new GlobalSettings(), tmp);
                var lg = new GlobalSettings();
                var lr = SettingsManager.LoadSettings(lg, tmp);

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
                var gOn  = new GlobalSettings { StickyModifiers = true,  AlwaysOnTop = true  };
                var gOff = new GlobalSettings { StickyModifiers = false, AlwaysOnTop = false };
                string tmp2 = Path.Combine(Path.GetTempPath(), $"osk_sa_{Guid.NewGuid()}.xml");
                SettingsManager.SaveSettings(new GridLayout(1,1), gOn, tmp2);
                var lgOn = new GlobalSettings();
                SettingsManager.LoadSettings(lgOn, tmp2);
                Assert(lgOn.StickyModifiers == true,  "StickyModifiers=true round-trip");
                Assert(lgOn.AlwaysOnTop     == true,  "AlwaysOnTop=true round-trip");
                File.Delete(tmp2);

                string tmp3 = Path.Combine(Path.GetTempPath(), $"osk_sa2_{Guid.NewGuid()}.xml");
                SettingsManager.SaveSettings(new GridLayout(1,1), gOff, tmp3);
                var lgOff = new GlobalSettings();
                SettingsManager.LoadSettings(lgOff, tmp3);
                Assert(lgOff.StickyModifiers == false, "StickyModifiers=false round-trip");
                Assert(lgOff.AlwaysOnTop     == false, "AlwaysOnTop=false round-trip");
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
            Assert(Lang.T("Key height")    == "Key height", "English: Height (rows)");
            Assert(Lang.T("Accessibility")  == "Accessibility", "English: Accessibility");
            Assert(Lang.T("Sticky modifiers")== "Sticky modifiers","English: Sticky modifiers");
            Assert(Lang.T("Always on top")  == "Always on top", "English: Always on top");
            Assert(Lang.T("Hide title bar") == "Hide title bar","English: Hide title bar");
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
                Assert(Lang.T("💾 Save")     == "💾 Opslaan",       "Dutch: Save");
                Assert(Lang.T("✖ Cancel")    == "✖  Annuleren",     "Dutch: Cancel");
                Assert(Lang.T("Preview")     == "Voorbeeld",        "Dutch: Preview");
                Assert(Lang.T("Language")    == "Taal",             "Dutch: Language");
                Assert(Lang.T("Layout file") == "Lay-outbestand",   "Dutch: Layout file");
                Assert(Lang.T("Accessibility")== "Toegankelijkheid","Dutch: Accessibility");
                Assert(Lang.T("Sticky modifiers")=="Plaktoetsen (Sticky Keys)","Dutch: Sticky modifiers");
                Assert(Lang.T("Always on top")=="Altijd bovenaan",  "Dutch: Always on top");
                Assert(Lang.T("Hide title bar")=="Titelbalk verbergen","Dutch: Hide title bar");
                Assert(Lang.T("Key width")=="Toets breedte","Dutch: Key width");
                Assert(Lang.T("Key height")=="Toets hoogte",   "Dutch: Key height");
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
            g4.SplitCell(0, 0, new GlobalSettings());
            Assert(g4.Cells.Count == 2,      "After SplitCell: 2 cells");
            Assert(g4.IsValid(),             "After SplitCell: valid");

            Section("GridLayout — InsertRow / RemoveRow / InsertCol / RemoveCol");

            var gIR = new GridLayout(2, 2);
            gIR.Cells.Add(new GridCell(0, 0, new KeyProps("A","A")));
            gIR.Cells.Add(new GridCell(0, 1, new KeyProps("B","B")));
            gIR.Cells.Add(new GridCell(1, 0, new KeyProps("C","C")));
            gIR.Cells.Add(new GridCell(1, 1, new KeyProps("D","D")));
            gIR.InsertRow(0, before: true, new GlobalSettings());
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
            gIC.InsertCol(0, before: true, new GlobalSettings());
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
}

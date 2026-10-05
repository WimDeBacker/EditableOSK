// TestRunnerOptions.cs — which groups of tests a run executes, how long each took, and the quick layout-guard mode.
//
//   OnScreenKeyboard.exe --test                    every group (what must pass before a commit)
//   OnScreenKeyboard.exe --test Wizard             only the groups whose name contains "Wizard" (case does not matter)
//   OnScreenKeyboard.exe --test Wizard,Sticky      several terms, separated by commas
//   OnScreenKeyboard.exe --test Wizard --quick     the layout guards check English and +80 % text in the light theme only
//
// A filtered or quick run says so at the end ("PARTIAL RUN"): it is for working on one part, not for deciding that the suite passes.
// Every run prints its slowest groups and writes all timings to test_timing.txt next to the program.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static readonly List<string> _filter = new List<string>();
        private static bool _optionsRead;
        private static int _ran, _skipped;
        private static readonly List<(string Name, long Ms, int Checks)> _timings = new List<(string, long, int)>();

        /// <summary>True when the layout guards should run a reduced matrix (English and +80 %, light theme only).</summary>
        internal static bool Quick { get; private set; }

        /// <summary>Reads the words after <c>--test</c> once: filter terms, and the <c>--quick</c> flag.</summary>
        private static void ReadOptions()
        {
            if (_optionsRead) return;
            _optionsRead = true;
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--test");
            for (int i = at + 1; at >= 0 && i < args.Length; i++)
            {
                if (args[i] == "--quick") { Quick = true; continue; }
                if (args[i].StartsWith("--")) continue;
                foreach (var term in args[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    _filter.Add(term);
            }
        }

        private static void Step(Action group) => Step(group.Method.Name, group);

        /// <summary>A group that only runs when a filter names it (a slow report, not a test): <c>--test NarrowWidthReport</c>.</summary>
        private static void StepOnDemand(Action group)
        {
            ReadOptions();
            if (_filter.Count == 0) return;
            Step(group.Method.Name, group);
        }

        /// <summary>Runs one group of tests when it matches the filter (all groups when there is none), and times it.</summary>
        private static void Step(string name, Action group)
        {
            ReadOptions();
            if (_filter.Count > 0 && !_filter.Any(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) { _skipped++; return; }
            _ran++;
            int before = _pass + _fail;
            var sw = Stopwatch.StartNew();
            group();
            sw.Stop();
            _timings.Add((name, sw.ElapsedMilliseconds, _pass + _fail - before));
        }

        /// <summary>Prints the slowest groups and writes every group's time to test_timing.txt.</summary>
        private static void WriteTimings()
        {
            if (_filter.Count > 0 && _ran == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  No test group matches '{string.Join(",", _filter)}'.");
                Console.ResetColor();
                _fail++;
                _failures.Add($"No test group matches '{string.Join(",", _filter)}'");
                return;
            }
            var sorted = _timings.OrderByDescending(t => t.Ms).ToList();
            long total = sorted.Sum(t => t.Ms);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  Slowest groups (of {sorted.Count}, {total / 1000.0:F1} s in all):");
            foreach (var t in sorted.Take(8))
                Console.WriteLine($"    {t.Ms / 1000.0,6:F1} s  {t.Checks,5} checks  {t.Name}");
            Console.ResetColor();
            try
            {
                File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_timing.txt"),
                    sorted.Select(t => $"{t.Ms / 1000.0,7:F1} s  {t.Checks,5} checks  {t.Name}"));
            }
            catch (Exception) { /* a read-only folder must not fail the run */ }
        }
    }
}

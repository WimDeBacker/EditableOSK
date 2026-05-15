// WordDatabase.cs — loads worddb.xml and provides word predictions
//
// Prediction strategy:
//   1. Second words (Next entries of the last completed word), optionally
//      filtered by the typed prefix — most contextually relevant.
//   2. First words (top-level Word entries by frequency), filtered by prefix
//      if typing, otherwise unfiltered — fills any remaining slots so
//      prediction keys are never empty.
//   Duplicates between step 1 and 2 are excluded.
//
// Case-sensitivity rule:
//   When a prefix is being typed, its first character determines case.
//   Otherwise, the upperCase flag (set from Shift/Caps state) determines it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace OnScreenKeyboard
{
    public static class WordDatabase
    {
        // ── Data model ───────────────────────────────────────────────
        private class WordEntry
        {
            public string       Word      { get; }
            public int          Frequency { get; }
            public List<string> NextWords { get; } = new List<string>();

            public WordEntry(string word, int frequency)
            { Word = word; Frequency = frequency; }
        }

        // ── State ────────────────────────────────────────────────────
        private static readonly Dictionary<string, WordEntry> _byExact
            = new Dictionary<string, WordEntry>(StringComparer.Ordinal);

        // All first-words sorted by frequency descending
        private static List<WordEntry> _byFrequency = new List<WordEntry>();

        public static bool   IsLoaded  { get; private set; } = false;
        public static string LoadError { get; private set; } = null;

        // ── Load ─────────────────────────────────────────────────────
        public static void Load(string path)
        {
            try
            {
                _byExact.Clear();
                _byFrequency.Clear();

                var doc = new XmlDocument();
                doc.Load(path);

                foreach (XmlNode node in doc.SelectNodes("/WordDatabase/Word"))
                {
                    string word = node.Attributes?["value"]?.Value;
                    if (string.IsNullOrEmpty(word)) continue;
                    int.TryParse(node.Attributes?["frequency"]?.Value, out int freq);

                    var entry = new WordEntry(word, freq);

                    // Store top 10 next-words — sufficient for any slot count (max 10)
                    int taken = 0;
                    foreach (XmlNode next in node.SelectNodes("Next"))
                    {
                        if (taken >= 10) break;
                        string nv = next.Attributes?["value"]?.Value;
                        if (!string.IsNullOrEmpty(nv)) { entry.NextWords.Add(nv); taken++; }
                    }

                    _byExact[word] = entry;
                }

                _byFrequency = _byExact.Values
                    .OrderByDescending(e => e.Frequency)
                    .ToList();

                IsLoaded  = true;
                LoadError = null;
            }
            catch (Exception ex)
            {
                IsLoaded  = false;
                LoadError = ex.Message;
            }
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Returns up to <paramref name="count"/> word predictions.
        ///
        /// Priority:
        ///   1. Second words (Next entries of <paramref name="lastCompletedWord"/>),
        ///      filtered by <paramref name="currentPrefix"/> if non-empty.
        ///   2. First words (by frequency), filtered by prefix if non-empty,
        ///      used to fill any slots not filled by step 1.
        ///
        /// This guarantees prediction keys are never empty as long as the
        /// first-words list contains enough words (it has 10,988).
        /// </summary>
        /// <summary>
        /// When <paramref name="upperCase"/> is true (sentence start or Caps Lock),
        /// predictions are searched case-insensitively so common lowercase words
        /// like "de", "het", "een" are included. The caller capitalises them before
        /// display and sending.
        /// </summary>
        public static List<string> GetPredictions(
            string lastCompletedWord,
            string currentPrefix,
            bool   upperCase,
            int    count,
            bool   preferUpperCase = false)
        {
            if (!IsLoaded || count <= 0) return new List<string>();

            bool hasPrefix = !string.IsNullOrEmpty(currentPrefix);

            // When at sentence start (upperCase=true) and no prefix typed yet,
            // search both cases — the caller will capitalise the result.
            // When a prefix is being typed, derive case from the prefix itself.
            // When upperCase=true (sentence start / Caps Lock), always search
            // case-insensitively regardless of what the prefix looks like.
            // "Da" at sentence start should match "de", "dat", "dan", not just
            // "David", "Damascus" etc.
            bool sentenceStart = upperCase;   // true = capitalise results, search all cases
            // prefixUpper is derived from the FIRST character of the prefix only.
            // This means camelCase words (e.g. iPhone, deBruyne) are not handled:
            // 'i' in "iPhone" is lowercase so prefixUpper=false (treated as normal word),
            // and uppercase letters later in the buffer are ignored for filtering.
            // This is acceptable for standard Dutch text — camelCase is not standard.
            bool prefixUpper   = hasPrefix && !sentenceStart && char.IsUpper(currentPrefix[0]);
            bool filterUpper   = !sentenceStart && hasPrefix && prefixUpper;

            var result = new List<string>(count);
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Step 1: Second words ──────────────────────────────────
            if (!string.IsNullOrEmpty(lastCompletedWord))
            {
                // Try exact match first; also try lowercased key
                WordEntry lastEntry = null;
                _byExact.TryGetValue(lastCompletedWord, out lastEntry);
                if (lastEntry == null)
                    _byExact.TryGetValue(lastCompletedWord.ToLower(), out lastEntry);

                if (lastEntry != null)
                {
                    foreach (string w in lastEntry.NextWords)
                    {
                        if (result.Count >= count) break;
                        if (hasPrefix && !MatchesPrefix(w, currentPrefix, !sentenceStart && prefixUpper)) continue;
                        // Mid-sentence: apply the same case filter as Step 2 so results are
                        // consistent. At sentence start the filter is skipped — all next-words
                        // are included and then capitalised by the block below.
                        if (!sentenceStart && w.Length > 0)
                        {
                            bool wUpper = char.IsUpper(w[0]);
                            if ( filterUpper && !wUpper) continue;  // uppercase prefix → proper nouns only
                            if (!filterUpper &&  wUpper) continue;  // normal mid-sentence → lowercase only
                        }
                        if (seen.Add(w)) result.Add(w);
                    }
                }
            }

            // ── Step 2: First words ───────────────────────────────────
            if (result.Count < count)
            {
                if (preferUpperCase && hasPrefix)
                {
                    // Uppercase prefix mid-sentence: ONLY return uppercase-starting words.
                    // The user deliberately pressed Shift — they want a proper noun.
                    // Use case-sensitive prefix match so "I" matches "Iran" not "in".
                    foreach (var entry in _byFrequency)
                    {
                        if (result.Count >= count) break;
                        if (!MatchesPrefix(entry.Word, currentPrefix, true)) continue;
                        if (!StartsWithCase(entry.Word, true)) continue;
                        if (seen.Add(entry.Word)) result.Add(entry.Word);
                    }
                }
                else if (preferUpperCase)
                {
                    // Shift active, no prefix yet: uppercase words first, then lowercase
                    foreach (var entry in _byFrequency)
                    {
                        if (result.Count >= count) break;
                        if (!StartsWithCase(entry.Word, true)) continue;
                        if (seen.Add(entry.Word)) result.Add(entry.Word);
                    }
                    foreach (var entry in _byFrequency)
                    {
                        if (result.Count >= count) break;
                        if (!StartsWithCase(entry.Word, false)) continue;
                        if (seen.Add(entry.Word)) result.Add(entry.Word);
                    }
                }
                else
                {
                    foreach (var entry in _byFrequency)
                    {
                        if (result.Count >= count) break;
                        if (hasPrefix && !MatchesPrefix(entry.Word, currentPrefix, !sentenceStart && prefixUpper)) continue;
                        if (!sentenceStart && !StartsWithCase(entry.Word, filterUpper)) continue;
                        if (seen.Add(entry.Word)) result.Add(entry.Word);
                    }
                }
            }

            // Capitalise results when at sentence start
            if (sentenceStart)
                for (int i = 0; i < result.Count; i++)
                    if (result[i].Length > 0)
                        result[i] = char.ToUpper(result[i][0]) + result[i].Substring(1);

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────
        private static bool StartsWithCase(string word, bool upper)
        {
            if (string.IsNullOrEmpty(word)) return false;
            return upper ? char.IsUpper(word[0]) : char.IsLower(word[0]);
        }

        private static bool MatchesPrefix(string word, string prefix, bool upper)
        {
            return word.StartsWith(prefix,
                upper ? StringComparison.Ordinal
                      : StringComparison.OrdinalIgnoreCase);
        }
    }
}

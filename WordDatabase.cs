// WordDatabase.cs — loads worddb.xml and provides word predictions
//
// What does this file do?
//   It reads a word-frequency XML file into memory and answers the question:
//   "Given what the user just typed and which word they finished last, what are
//   the most likely next words?"
//
// Prediction strategy (two-step):
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
    /// <summary>
    /// A static (shared, no instance needed) in-memory word database that
    /// powers the keyboard's word-prediction feature.
    ///
    /// <para>
    /// Call <see cref="Load"/> once at startup to read the XML word list into
    /// memory. After that, call <see cref="GetPredictions"/> whenever the
    /// keyboard needs to update its suggestion buttons.
    /// </para>
    ///
    /// <para>
    /// "Static" means there is only one copy of this data shared by the whole
    /// app — you never write <c>new WordDatabase()</c>; you just call
    /// <c>WordDatabase.Load(...)</c> directly.
    /// </para>
    /// </summary>
    public static class WordDatabase
    {
        // ── Data model ───────────────────────────────────────────────
        //
        // WordEntry represents one row in the XML file:
        //   <Word value="de" frequency="987654">
        //     <Next value="beste" />
        //     <Next value="eerste" />
        //   </Word>
        //
        // Frequency is how often the word appears in the training corpus.
        // NextWords are the most common words that follow this one.
        private class WordEntry
        {
            public string       Word      { get; }
            public int          Frequency { get; }
            public List<string> NextWords { get; } = new List<string>();

            public WordEntry(string word, int frequency)
            { Word = word; Frequency = frequency; }
        }

        // ── State ────────────────────────────────────────────────────

        // Dictionary for O(1) exact-word lookup: "de" → its WordEntry.
        // StringComparer.Ordinal means the comparison is byte-for-byte,
        // which is the fastest possible string comparison.
        private static readonly Dictionary<string, WordEntry> _byExact
            = new Dictionary<string, WordEntry>(StringComparer.Ordinal);

        // The same entries, sorted by frequency (most common first).
        // Used in Step 2 of prediction to fill remaining slots.
        private static List<WordEntry> _byFrequency = new List<WordEntry>();

        /// <summary>
        /// True after a successful call to <see cref="Load"/>; false if the
        /// database has not yet been loaded or if loading failed.
        /// </summary>
        public static bool   IsLoaded  { get; private set; } = false;

        /// <summary>
        /// Contains the error message from the last failed <see cref="Load"/>
        /// call, or <c>null</c> if the most recent load succeeded.
        /// </summary>
        public static string LoadError { get; private set; } = null;

        // ── Load ─────────────────────────────────────────────────────

        /// <summary>
        /// Reads the XML word-frequency database from disk into memory.
        /// Call this once when the application starts, before calling
        /// <see cref="GetPredictions"/>.
        /// </summary>
        /// <param name="path">
        /// The full file-system path to the XML file (e.g. "worddb.xml").
        /// The file must have the structure:
        /// <code>
        /// &lt;WordDatabase&gt;
        ///   &lt;Word value="de" frequency="123456"&gt;
        ///     &lt;Next value="beste" /&gt;
        ///   &lt;/Word&gt;
        /// &lt;/WordDatabase&gt;
        /// </code>
        /// </param>
        public static void Load(string path)
        {
            try
            {
                // Start fresh — if Load is called a second time (e.g. user
                // switches language), we throw away the previous data.
                _byExact.Clear();
                _byFrequency.Clear();

                var doc = new XmlDocument();
                doc.Load(path);

                // Walk every <Word> element at the top level of the file.
                foreach (XmlNode node in doc.SelectNodes("/WordDatabase/Word"))
                {
                    string word = node.Attributes?["value"]?.Value;
                    if (string.IsNullOrEmpty(word)) continue;   // skip malformed nodes

                    // int.TryParse returns false (and leaves freq=0) if the
                    // attribute is missing or not a valid number — safe default.
                    int.TryParse(node.Attributes?["frequency"]?.Value, out int freq);

                    var entry = new WordEntry(word, freq);

                    // Store top 10 next-words — sufficient for any slot count (max 10).
                    // Reading more than needed would waste memory.
                    int taken = 0;
                    foreach (XmlNode next in node.SelectNodes("Next"))
                    {
                        if (taken >= 10) break;
                        string nv = next.Attributes?["value"]?.Value;
                        if (!string.IsNullOrEmpty(nv)) { entry.NextWords.Add(nv); taken++; }
                    }

                    // Key the dictionary by the word itself for fast later lookup.
                    _byExact[word] = entry;
                }

                // Build the frequency-sorted list once at load time so GetPredictions
                // never has to sort at runtime (sorting 11k items per keypress would
                // be noticeably slow).
                _byFrequency = _byExact.Values
                    .OrderByDescending(e => e.Frequency)
                    .ToList();

                IsLoaded  = true;
                LoadError = null;
            }
            catch (Exception ex)
            {
                // Something went wrong (file missing, malformed XML, etc.).
                // Record the error message so the UI can display it.
                IsLoaded  = false;
                LoadError = ex.Message;
            }
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Returns up to <paramref name="count"/> word predictions for the
        /// current typing context.
        ///
        /// <para><b>Priority order:</b></para>
        /// <list type="number">
        ///   <item>
        ///     <b>Second-word suggestions</b> — words that frequently follow
        ///     <paramref name="lastCompletedWord"/> in the training corpus.
        ///     Filtered by <paramref name="currentPrefix"/> when the user has
        ///     started typing. These are the most contextually relevant results.
        ///   </item>
        ///   <item>
        ///     <b>Frequency-sorted first words</b> — the most common words in
        ///     the entire database, filtered by prefix when relevant. These fill
        ///     any prediction slots that Step 1 did not fill, so the suggestion
        ///     bar is never empty.
        ///   </item>
        /// </list>
        ///
        /// <para>
        /// When <paramref name="upperCase"/> is <c>true</c> (sentence start or
        /// Caps Lock active), words are searched case-insensitively and the
        /// returned strings are capitalised by this method before being returned.
        /// </para>
        /// </summary>
        /// <param name="lastCompletedWord">
        /// The most recently completed word (the one before the current space),
        /// used to look up second-word suggestions. Pass <c>null</c> or empty
        /// string if there is no previous word.
        /// </param>
        /// <param name="currentPrefix">
        /// Whatever the user has typed so far for the current word (may be empty
        /// if no characters have been typed yet after the space).
        /// </param>
        /// <param name="upperCase">
        /// <c>true</c> when the next word should start with a capital letter
        /// — i.e. after a sentence-ending punctuation mark or when Caps Lock
        /// is on. Causes results to be capitalised and the search to be
        /// case-insensitive so common lowercase words are not excluded.
        /// </param>
        /// <param name="count">
        /// The maximum number of suggestions to return (usually matches the
        /// number of prediction buttons visible on the keyboard).
        /// </param>
        /// <param name="preferUpperCase">
        /// <c>true</c> when the user deliberately pressed Shift mid-sentence,
        /// indicating they want a proper noun. Causes the method to return only
        /// words that begin with an uppercase letter.
        /// </param>
        /// <returns>
        /// A list of prediction strings, capitalised if appropriate, with at
        /// most <paramref name="count"/> entries and no duplicates (case-insensitive).
        /// Returns an empty list if the database is not loaded.
        /// </returns>
        public static List<string> GetPredictions(
            string lastCompletedWord,
            string currentPrefix,
            bool   upperCase,
            int    count,
            bool   preferUpperCase = false)
        {
            if (!IsLoaded || count <= 0) return new List<string>();

            bool hasPrefix = !string.IsNullOrEmpty(currentPrefix);

            // sentenceStart = true means we are at the beginning of a sentence
            // (or after Caps Lock). We will capitalise every result at the end
            // and we search case-insensitively so we don't miss common words.
            bool sentenceStart = upperCase;

            // prefixUpper = true when the user has typed an uppercase first letter
            // mid-sentence (e.g. "D" when shift was held). This signals a proper
            // noun — we should only return words that start with a capital.
            //
            // Note: if sentenceStart is already true, we skip this check because
            // sentence-start capitalisation is handled separately.
            bool prefixUpper   = hasPrefix && !sentenceStart && char.IsUpper(currentPrefix[0]);

            // filterUpper = true when we are in the middle of a sentence AND the
            // user typed a capital first letter. In that case only uppercase-starting
            // words pass the filter (Step 2 logic below).
            bool filterUpper   = !sentenceStart && hasPrefix && prefixUpper;

            var result = new List<string>(count);

            // seen tracks which words are already in the result, so we never
            // return the same word from both Step 1 and Step 2.
            // OrdinalIgnoreCase means "De" and "de" are treated as the same word.
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Step 1: Second words ──────────────────────────────────
            //
            // Look up which words commonly follow lastCompletedWord in the
            // training data. These are pre-stored in WordEntry.NextWords.
            if (!string.IsNullOrEmpty(lastCompletedWord))
            {
                // Try exact match first; also try lowercased key.
                // The database stores words in lowercase, but lastCompletedWord
                // might be "De" (capitalised at sentence start), so we also try
                // the lowercase version to find the entry.
                WordEntry lastEntry = null;
                _byExact.TryGetValue(lastCompletedWord, out lastEntry);
                if (lastEntry == null)
                    _byExact.TryGetValue(lastCompletedWord.ToLower(), out lastEntry);

                if (lastEntry != null)
                {
                    foreach (string w in lastEntry.NextWords)
                    {
                        if (result.Count >= count) break;

                        // If the user has started typing, skip any next-word that
                        // doesn't begin with the typed prefix.
                        if (hasPrefix && !MatchesPrefix(w, currentPrefix, !sentenceStart && prefixUpper)) continue;

                        // Mid-sentence case consistency:
                        // If the user typed a lowercase prefix, we only want lowercase words.
                        // If the user typed an uppercase prefix, we only want proper nouns.
                        // At sentence start we skip this filter — all cases are included.
                        if (!sentenceStart && w.Length > 0)
                        {
                            bool wUpper = char.IsUpper(w[0]);
                            if ( filterUpper && !wUpper) continue;  // uppercase prefix → proper nouns only
                            if (!filterUpper &&  wUpper) continue;  // normal mid-sentence → lowercase only
                        }

                        // seen.Add returns true only if the word was not already in the set,
                        // which prevents duplicates.
                        if (seen.Add(w)) result.Add(w);
                    }
                }
            }

            // ── Step 2: First words ───────────────────────────────────
            //
            // If Step 1 did not fill all the requested slots, pad with the
            // most-frequent words from the entire database (already sorted).
            if (result.Count < count)
            {
                if (preferUpperCase && hasPrefix)
                {
                    // The user held Shift and started typing mid-sentence.
                    // They explicitly want a proper noun, so only return words
                    // that start with an uppercase letter, matching the prefix
                    // with a case-sensitive comparison.
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
                    // Shift is active but no characters typed yet — list proper
                    // nouns first (uppercase-starting), then fill any remaining
                    // slots with common lowercase words.
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
                    // Normal case: return most-frequent words that match the
                    // prefix and case rules.
                    foreach (var entry in _byFrequency)
                    {
                        if (result.Count >= count) break;

                        // Skip words that don't start with the typed prefix.
                        if (hasPrefix && !MatchesPrefix(entry.Word, currentPrefix, !sentenceStart && prefixUpper)) continue;

                        // Mid-sentence: skip uppercase-starting words unless the
                        // user typed an uppercase prefix (filterUpper=true).
                        if (!sentenceStart && !StartsWithCase(entry.Word, filterUpper)) continue;

                        if (seen.Add(entry.Word)) result.Add(entry.Word);
                    }
                }
            }

            // Capitalise all results when at the start of a sentence.
            // We do this last — the matching above works with the stored
            // lowercase forms, and only the displayed string needs the capital.
            if (sentenceStart)
                for (int i = 0; i < result.Count; i++)
                    if (result[i].Length > 0)
                        result[i] = char.ToUpper(result[i][0]) + result[i].Substring(1);

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> if the first character of <paramref name="word"/>
        /// has the requested case.
        /// </summary>
        /// <param name="word">The word to test.</param>
        /// <param name="upper">
        /// <c>true</c> to require an uppercase first letter;
        /// <c>false</c> to require a lowercase first letter.
        /// </param>
        private static bool StartsWithCase(string word, bool upper)
        {
            if (string.IsNullOrEmpty(word)) return false;
            return upper ? char.IsUpper(word[0]) : char.IsLower(word[0]);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="word"/> starts with
        /// <paramref name="prefix"/>.
        /// </summary>
        /// <param name="word">The candidate word from the database.</param>
        /// <param name="prefix">The characters the user has typed so far.</param>
        /// <param name="upper">
        /// When <c>true</c>, the comparison is case-sensitive (Ordinal) so
        /// "D" only matches "De", "David", etc., not "de".
        /// When <c>false</c>, the comparison is case-insensitive so "d" matches
        /// both "de" and "De".
        /// </param>
        private static bool MatchesPrefix(string word, string prefix, bool upper)
        {
            return word.StartsWith(prefix,
                upper ? StringComparison.Ordinal          // case-sensitive: "D" ≠ "d"
                      : StringComparison.OrdinalIgnoreCase); // case-insensitive: "d" == "D"
        }
    }
}

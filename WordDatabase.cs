// WordDatabase.cs — loads worddb.wfq and provides word predictions
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
//
// Threading model:
//   Load() is safe to call from a background thread (via Task.Run).
//   It parses the file with a forward-only XmlReader into thread-local variables,
//   then atomically publishes the result by replacing the volatile _snapshot
//   reference.  GetPredictions() captures _snapshot once at entry so it always
//   sees a consistent view even if a concurrent reload starts mid-call.
//   No locking is required on the read path.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A static (shared, no instance needed) in-memory word database that
    /// powers the keyboard's word-prediction feature.
    ///
    /// <para>
    /// Call <see cref="Load"/> once at startup (typically on a background thread
    /// via <c>Task.Run</c>) to read the XML word list into memory.  Subscribe to
    /// <see cref="Loaded"/> to be notified when the load completes.  After loading,
    /// call <see cref="GetPredictions"/> whenever the keyboard needs to update its
    /// suggestion buttons.
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

        // ── Immutable snapshot ───────────────────────────────────────
        //
        // The entire database is published as a single immutable object.
        // Replacing _snapshot (a volatile reference) is a single atomic write,
        // so GetPredictions() can capture it once and work with a consistent view
        // even if a reload starts on another thread at the same moment.
        private sealed class DbSnapshot
        {
            /// <summary>A safe empty snapshot used before any database is loaded.</summary>
            public static readonly DbSnapshot Empty = new DbSnapshot(
                new Dictionary<string, WordEntry>(StringComparer.Ordinal),
                new List<WordEntry>(),
                string.Empty, false);

            public readonly Dictionary<string, WordEntry> ByExact;
            public readonly List<WordEntry>               ByFrequency;
            public readonly string                        Language;
            public readonly bool                          IsPersonal;

            public DbSnapshot(Dictionary<string, WordEntry> byExact,
                              List<WordEntry>               byFrequency,
                              string                        language,
                              bool                          isPersonal)
            {
                ByExact     = byExact;
                ByFrequency = byFrequency;
                Language    = language;
                IsPersonal  = isPersonal;
            }
        }

        // ── State ────────────────────────────────────────────────────

        // The currently active database snapshot.  Written once per successful
        // Load() call; read on every GetPredictions() call.  The volatile keyword
        // ensures the writing thread's stores are visible to any thread that
        // subsequently reads _snapshot (acquire / release semantics).
        private static volatile DbSnapshot _snapshot = DbSnapshot.Empty;

        // Volatile flags used for UI signalling — no explicit locking needed.
        private static volatile bool _isLoaded  = false;
        private static volatile bool _isLoading = false;

        // Generation counter — incremented at the start of every Load() call.
        // Each load stamps its own generation; it only publishes if no newer
        // call has started since (fixes the concurrent-load race).
        private static int _loadGen = 0;

        /// <summary>
        /// True after a successful call to <see cref="Load"/>; false while
        /// loading, before the first load, or if loading failed.
        /// </summary>
        public static bool IsLoaded  => _isLoaded;

        /// <summary>
        /// True while a background load is in progress.  Word-prediction cells
        /// show blank while this is true; they fill in when <see cref="Loaded"/>
        /// fires and the caller invokes <c>ApplyWPTags()</c>.
        /// </summary>
        public static bool IsLoading => _isLoading;

        /// <summary>
        /// Number of words in the currently loaded database, or 0 if not loaded.
        /// </summary>
        public static int WordCount => _snapshot.ByExact.Count;

        /// <summary>
        /// Contains the error message from the last failed <see cref="Load"/>
        /// call, or <c>null</c> if the most recent load succeeded.
        /// </summary>
        public static string LoadError { get; private set; } = null;

        /// <summary>
        /// The language code read from the loaded .wfq file (e.g. "nl", "en"),
        /// or an empty string if the file predates the language attribute.
        /// </summary>
        public static string Language => _snapshot.Language;

        /// <summary>
        /// <c>true</c> when the loaded file is a personal copy (contains learned
        /// words and adjusted frequencies); <c>false</c> for base (read-only)
        /// databases.
        /// </summary>
        public static bool IsPersonal => _snapshot.IsPersonal;

        /// <summary>
        /// Fired on the calling thread immediately after a successful load.
        /// When <see cref="Load"/> is called via <c>Task.Run</c>, this fires on
        /// the background thread — subscribers must marshal back to the UI thread
        /// (e.g. <c>BeginInvoke</c>) before touching any UI controls.
        /// </summary>
        public static event Action Loaded;

        // ── Load ─────────────────────────────────────────────────────

        /// <summary>
        /// Reads the XML word-frequency database from disk into memory.
        /// Safe to call from any thread — typically called via
        /// <c>Task.Run(() =&gt; WordDatabase.Load(path))</c> so the UI remains
        /// responsive during the parse.
        /// </summary>
        /// <param name="path">
        /// The full file-system path to the .wfq file (e.g. "worddb_EN.wfq").
        /// The file must have the structure:
        /// <code>
        /// &lt;WordDatabase version="1" language="nl" isPersonal="false"&gt;
        ///   &lt;Candidates /&gt;
        ///   &lt;Word value="de" frequency="123456"&gt;
        ///     &lt;Next value="beste" frequency="5" /&gt;
        ///   &lt;/Word&gt;
        /// &lt;/WordDatabase&gt;
        /// </code>
        /// The <c>version</c>, <c>language</c> and <c>isPersonal</c> attributes
        /// and the <c>&lt;Candidates&gt;</c> section are optional for backward
        /// compatibility with older files.
        /// </param>
        /// <remarks>
        /// <see cref="IsLoaded"/> is set to <c>false</c> and
        /// <see cref="IsLoading"/> to <c>true</c> at the start of the call.
        /// On success, the parsed data is atomically published via the internal
        /// snapshot, <see cref="IsLoaded"/> becomes <c>true</c>, and
        /// <see cref="Loaded"/> is fired.
        /// On failure, <see cref="LoadError"/> is set and <see cref="IsLoaded"/>
        /// stays <c>false</c>.
        /// </remarks>
        public static void Load(string path)
        {
            // Stamp this load with a unique generation number.  Any newer Load()
            // call will increment _loadGen further, causing this call to abort
            // its publish step (fixes concurrent-load race — finding #2).
            int gen = Interlocked.Increment(ref _loadGen);

            // Signal "loading in progress" so the UI can show blank WP cells.
            _isLoaded  = false;
            _isLoading = true;

            try
            {
                var (snap, err) = ParseFile(path);

                // A newer Load() has been started while we were parsing —
                // discard this result rather than overwriting newer data.
                if (gen != Volatile.Read(ref _loadGen)) return;

                if (err != null)
                {
                    LoadError = err;
                    return;
                }

                // Atomically publish the new snapshot.  The volatile write to
                // _snapshot acts as a release fence — any thread that subsequently
                // reads _snapshot with an acquire fence (all volatile reads do)
                // is guaranteed to see the fully-constructed snapshot contents.
                _snapshot = snap;   // volatile write (release fence)
                LoadError = null;
                // _isLoaded is set AFTER the finally block so the IsLoading flag
                // is already false when IsLoaded becomes true — no window where
                // both flags are true simultaneously (fixes finding #8).
            }
            finally
            {
                // Only lower the loading flag for the current generation; a newer
                // load that is still running will lower it when it finishes.
                if (gen == Volatile.Read(ref _loadGen))
                    _isLoading = false;
            }

            // If we were superseded after the finally, do not mark as loaded.
            if (gen != Volatile.Read(ref _loadGen)) return;

            _isLoaded = true;   // volatile — IsLoading is already false here

            // Wrap the event invocation: a subscriber that throws (e.g. BeginInvoke
            // on a closing form) must not corrupt IsLoaded or LoadError (finding #5).
            try   { Loaded?.Invoke(); }
            catch { /* swallow — database is loaded correctly regardless */ }
        }

        /// <summary>
        /// Parses the .wfq file with a forward-only <see cref="XmlReader"/> into
        /// a new immutable <see cref="DbSnapshot"/>.
        /// All work is done in thread-local variables — no static state is touched
        /// until the caller atomically publishes the result.
        /// </summary>
        private static (DbSnapshot snapshot, string error) ParseFile(string path)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing                = DtdProcessing.Prohibit,
                    XmlResolver                  = null,
                    IgnoreWhitespace             = true,
                    IgnoreComments               = true,
                    IgnoreProcessingInstructions = true,
                };

                var    byExact    = new Dictionary<string, WordEntry>(StringComparer.Ordinal);
                string lang       = string.Empty;
                bool   isPersonal = false;

                WordEntry current   = null;  // the <Word> element currently being read
                int       nextTaken = 0;     // how many <Next> children we have stored

                using var reader = XmlReader.Create(path, settings);
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;

                    switch (reader.LocalName)
                    {
                        case "WordDatabase":
                            lang       = reader.GetAttribute("language")   ?? string.Empty;
                            isPersonal = reader.GetAttribute("isPersonal") == "true";
                            break;

                        case "Word":
                        {
                            string word = reader.GetAttribute("value");
                            if (string.IsNullOrEmpty(word)) { current = null; break; }
                            int.TryParse(reader.GetAttribute("frequency"), out int freq);
                            current   = new WordEntry(word, freq);
                            nextTaken = 0;
                            byExact[word] = current;
                            break;
                        }

                        case "Next":
                            if (current != null && nextTaken < 10)
                            {
                                string nv = reader.GetAttribute("value");
                                if (!string.IsNullOrEmpty(nv))
                                {
                                    current.NextWords.Add(nv);
                                    nextTaken++;
                                }
                            }
                            break;

                        // <Candidates> and any other elements are silently skipped.
                    }
                }

                // Build the frequency-sorted list once at load time so GetPredictions
                // never has to sort at runtime (sorting 900k items per keypress would
                // be noticeably slow).
                var byFreq = byExact.Values
                    .OrderByDescending(e => e.Frequency)
                    .ToList();

                return (new DbSnapshot(byExact, byFreq, lang, isPersonal), null);
            }
            catch (Exception ex)
            {
                return (DbSnapshot.Empty, ex.Message);
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
        /// Returns an empty list if the database is not loaded or is currently loading.
        /// </returns>
        public static List<string> GetPredictions(
            string lastCompletedWord,
            string currentPrefix,
            bool   upperCase,
            int    count,
            bool   preferUpperCase = false)
        {
            if (!_isLoaded || count <= 0) return new List<string>();

            // Capture the snapshot once.  Even if Load() replaces _snapshot on
            // another thread during this call, we work with a consistent view.
            var snap = _snapshot;

            try
            {
                return GetPredictionsCore(snap, lastCompletedWord, currentPrefix,
                                          upperCase, count, preferUpperCase);
            }
            catch
            {
                // An unexpected error in the prediction engine must never crash
                // the keyboard — return empty so prediction cells go blank.
                return new List<string>();
            }
        }

        // The actual prediction logic, separated so the public method can wrap it safely.
        private static List<string> GetPredictionsCore(
            DbSnapshot snap,
            string lastCompletedWord,
            string currentPrefix,
            bool   upperCase,
            int    count,
            bool   preferUpperCase)
        {
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
                snap.ByExact.TryGetValue(lastCompletedWord, out lastEntry);
                if (lastEntry == null)
                    snap.ByExact.TryGetValue(lastCompletedWord.ToLower(), out lastEntry);

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
                    foreach (var entry in snap.ByFrequency)
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
                    foreach (var entry in snap.ByFrequency)
                    {
                        if (result.Count >= count) break;
                        if (!StartsWithCase(entry.Word, true)) continue;
                        if (seen.Add(entry.Word)) result.Add(entry.Word);
                    }
                    foreach (var entry in snap.ByFrequency)
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
                    foreach (var entry in snap.ByFrequency)
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

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
using System.IO;
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
            public string          Word      { get; }
            // Mutable: the learning engine (RecordWord) increments this in place as
            // the user types. Safe without locking because all mutation happens on
            // the UI thread (see RecordWord remarks); Save() copies values onto a
            // plain-data structure on the caller's thread before handing off to a
            // background thread for the actual file write.
            public int              Frequency { get; set; }
            public List<NextEntry>  NextWords { get; } = new List<NextEntry>();

            // How many times the user has personally used this word. Tracked
            // separately from Frequency (which stays the base corpus value for
            // words that came from the base file) so personal usage can be given
            // its own ranking tier instead of being drowned out by — or, if
            // weighted too heavily, unpredictably overtaking — the base frequency.
            // See GetPredictionsCore's "Step 1.5" and RecordWord.
            public int              PersonalUseCount { get; set; }

            // True only for entries parsed from the base file. False for entries
            // created by PromoteInternal (promoted candidates) — those have no
            // base counterpart, so at save time they are always written in full
            // as <NewWord>, never as a <PersonalUse> delta.
            public bool             IsFromBase { get; set; }

            public WordEntry(string word, int frequency)
            { Word = word; Frequency = frequency; }
        }

        // One "commonly follows" entry for a WordEntry.NextWords list.
        // Frequency/PersonalUseCount/IsFromBase mirror WordEntry's fields above,
        // for the same reasons.
        private class NextEntry
        {
            public string Word;
            public int    Frequency;
            public int    PersonalUseCount;
            public bool   IsFromBase;
            public NextEntry(string word, int frequency)
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
                string.Empty);

            public readonly Dictionary<string, WordEntry> ByExact;
            public readonly List<WordEntry>               ByFrequency;
            public readonly string                        Language;

            // Words with PersonalUseCount > 0, sorted descending by PersonalUseCount.
            // Populated by the overlay merge at load time and kept up to date by
            // RecordWord/PromoteCandidate. Small in practice (a user's personal
            // vocabulary), so a full re-sort on each change is cheap. Same
            // UI-thread-only mutation invariant as ByFrequency/NextWords — see
            // RecordWord's thread-safety remarks.
            public readonly List<WordEntry> ByPersonalUse = new List<WordEntry>();

            public DbSnapshot(Dictionary<string, WordEntry> byExact,
                              List<WordEntry>               byFrequency,
                              string                        language)
            {
                ByExact     = byExact;
                ByFrequency = byFrequency;
                Language    = language;
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

        // ── Learning engine state ───────────────────────────────────────
        //
        // Unknown words the user has typed, not yet promoted to a full WordEntry.
        // Key = the word as recorded (see RecordWord's case-normalisation rule),
        // value = how many times it has been seen. Reset on every Load() (each
        // file has its own <Candidates> section). Only ever touched from the UI
        // thread (RecordWord is called synchronously from key-press handling),
        // so no locking is needed — see RecordWord's remarks.
        private static Dictionary<string, int> _candidates =
            new Dictionary<string, int>(StringComparer.Ordinal);

        // True when RecordWord/PromoteCandidate/RemoveCandidate has changed
        // in-memory state since the last successful save.
        private static volatile bool _dirty = false;

        // Guards against overlapping background saves if SaveIfDirty is called
        // again (e.g. by a periodic timer) while a previous save is still writing.
        private static volatile bool _saving = false;

        // Companion overlay file path for whatever base file is currently loaded
        // (see DeriveOverlayPath). Null before the first successful Load(). Read
        // by SaveNow/SaveIfDirty, which no longer take a path parameter — the
        // overlay is always the one paired with the currently loaded base file.
        private static volatile string _overlayPath = null;

        // Master switch for the learning engine. RecordWord/PromoteCandidate are
        // no-ops while this is false. Defaults to true (learning starts working
        // immediately without any setup); KeyboardForm keeps this in sync with
        // LayoutMeta.WordLearningEnabled after every load and whenever the user
        // toggles the "Remember typed words" checkbox.
        public static bool LearningEnabled { get; set; } = true;

        // Unknown-word occurrences strictly greater than this are promoted from
        // the candidate buffer into the real word list (spec: "count > 2").
        private const int CandidatePromotionThreshold = 2;

        // Matches the load-time cap on <Next> children per word (see ParseFile).
        private const int MaxNextWords = 10;

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
        /// Fired on the calling thread immediately after a successful load.
        /// When <see cref="Load"/> is called via <c>Task.Run</c>, this fires on
        /// the background thread — subscribers must marshal back to the UI thread
        /// (e.g. <c>BeginInvoke</c>) before touching any UI controls.
        /// </summary>
        public static event Action Loaded;

        // ── Load ─────────────────────────────────────────────────────

        /// <summary>
        /// Reads the base XML word-frequency database from disk into memory,
        /// then merges in its companion overlay file if one exists (see
        /// <see cref="DeriveOverlayPath"/> / <see cref="ApplyOverlay"/>) — no
        /// separate call needed to pick up previously learned words. Safe to
        /// call from any thread — typically called via
        /// <c>Task.Run(() =&gt; WordDatabase.Load(path))</c> so the UI remains
        /// responsive during the parse.
        /// </summary>
        /// <param name="path">
        /// The full file-system path to the base .wfq file (e.g. "worddb_EN.wfq").
        /// The file must have the structure:
        /// <code>
        /// &lt;WordDatabase version="1" language="nl"&gt;
        ///   &lt;Word value="de" frequency="123456"&gt;
        ///     &lt;Next value="beste" frequency="5" /&gt;
        ///   &lt;/Word&gt;
        /// &lt;/WordDatabase&gt;
        /// </code>
        /// The <c>version</c> and <c>language</c> attributes are optional for
        /// backward compatibility with older files.
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

                // Merge in the companion overlay file (learned words/word-pairs and
                // the candidate buffer) before publishing, if one exists. snap is
                // not reachable from anywhere else yet, so mutating it here needs
                // no thread-safety beyond what Load already provides.
                string overlayPath = DeriveOverlayPath(path);
                var    candidates  = ApplyOverlay(snap, overlayPath);

                // Atomically publish the new snapshot.  The volatile write to
                // _snapshot acts as a release fence — any thread that subsequently
                // reads _snapshot with an acquire fence (all volatile reads do)
                // is guaranteed to see the fully-constructed snapshot contents.
                _snapshot = snap;   // volatile write (release fence)
                LoadError = null;
                // _isLoaded is set AFTER the finally block so the IsLoading flag
                // is already false when IsLoaded becomes true — no window where
                // both flags are true simultaneously (fixes finding #8).

                // Reset the learning engine's state for the newly loaded file.
                // Candidates and the overlay path are per-file; any unsaved state
                // from a previous file must not leak across.
                _candidates  = candidates;
                _overlayPath = overlayPath;
                _dirty       = false;
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
        /// Parses the base .wfq corpus file with a forward-only <see cref="XmlReader"/>
        /// into a new immutable <see cref="DbSnapshot"/>. All work is done in
        /// thread-local variables — no static state is touched until the caller
        /// atomically publishes the result. Does not read learning-engine data
        /// (candidates, personal use) — that lives in the companion overlay file
        /// and is merged in separately by <see cref="ApplyOverlay"/>.
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

                var    byExact = new Dictionary<string, WordEntry>(StringComparer.Ordinal);
                string lang    = string.Empty;

                WordEntry current   = null;  // the <Word> element currently being read
                int       nextTaken = 0;     // how many <Next> children we have stored

                using var reader = XmlReader.Create(path, settings);
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;

                    switch (reader.LocalName)
                    {
                        case "WordDatabase":
                            lang = reader.GetAttribute("language") ?? string.Empty;
                            break;

                        case "Word":
                        {
                            string word = reader.GetAttribute("value");
                            if (string.IsNullOrEmpty(word)) { current = null; break; }
                            int.TryParse(reader.GetAttribute("frequency"), out int freq);
                            current = new WordEntry(word, freq) { IsFromBase = true };
                            nextTaken = 0;
                            byExact[word] = current;
                            break;
                        }

                        case "Next":
                            if (current != null && nextTaken < MaxNextWords)
                            {
                                string nv = reader.GetAttribute("value");
                                if (!string.IsNullOrEmpty(nv))
                                {
                                    int.TryParse(reader.GetAttribute("frequency"), out int nfreq);
                                    current.NextWords.Add(new NextEntry(nv, nfreq) { IsFromBase = true });
                                    nextTaken++;
                                }
                            }
                            break;

                        // Base (corpus) files never define <Candidate>/<PersonalUse>/etc. —
                        // that learning-engine data lives only in the companion overlay
                        // file (see DeriveOverlayPath / ApplyOverlay), applied after this
                        // base parse completes.
                    }
                }

                // Build the frequency-sorted list once at load time so GetPredictions
                // never has to sort at runtime (sorting 900k items per keypress would
                // be noticeably slow).
                var byFreq = byExact.Values
                    .OrderByDescending(e => e.Frequency)
                    .ToList();

                return (new DbSnapshot(byExact, byFreq, lang), null);
            }
            catch (Exception ex)
            {
                return (DbSnapshot.Empty, ex.Message);
            }
        }

        // ── Overlay (learning engine persistence) ───────────────────────
        //
        // A small companion file that sits next to a base .wfq file and holds
        // only what the learning engine has changed — personal-use counts,
        // brand-new words/word-pairs the user typed, and the not-yet-promoted
        // candidate buffer. This keeps "remembering typed words" cheap (no need
        // to duplicate a 200MB base corpus) and lets it apply on top of ANY base
        // file without the user ever choosing or managing a separate "personal
        // database" file.

        /// <summary>
        /// The companion overlay path for a base <c>.wfq</c> file, e.g.
        /// <c>worddb_NL.wfq</c> → <c>worddb_NL.learned.wfq</c>, same directory.
        /// Public so the Word Prediction UI can locate the (small) overlay file
        /// for exporting, without duplicating the naming convention.
        /// </summary>
        public static string GetOverlayPath(string basePath) => DeriveOverlayPath(basePath);

        private static string DeriveOverlayPath(string basePath) =>
            Path.Combine(
                Path.GetDirectoryName(basePath) ?? "",
                Path.GetFileNameWithoutExtension(basePath) + ".learned.wfq");

        /// <summary>
        /// If <paramref name="overlayPath"/> exists, parses it and applies every
        /// record onto <paramref name="snap"/> (which is not yet published/
        /// reachable from anywhere else, so this needs no locking beyond what
        /// <see cref="Load"/> already provides). Reuses the same mutation
        /// primitives <see cref="RecordWord"/> uses internally, so a freshly
        /// merged snapshot looks exactly as if every learned word had just been
        /// typed again in a new session.
        /// </summary>
        /// <returns>
        /// The candidate buffer read from the overlay's <c>&lt;Candidates&gt;</c>
        /// section, or an empty dictionary if there is no overlay file (or it
        /// fails to parse — corrupt/missing overlay data must never block the
        /// base database from loading).
        /// </returns>
        private static Dictionary<string, int> ApplyOverlay(DbSnapshot snap, string overlayPath)
        {
            var candidates = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!File.Exists(overlayPath)) return candidates;

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

                // Set only while inside a <NewWord> block, so its <Next>
                // children are routed onto the right entry. Mirrors ParseFile's
                // "current" pattern — safe because the writer always emits a
                // NewWord's own <Next> children immediately after it and before
                // any other top-level record (see WriteSaveData).
                WordEntry current = null;

                using var reader = XmlReader.Create(overlayPath, settings);
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;

                    switch (reader.LocalName)
                    {
                        case "Candidate":
                        {
                            current = null;
                            string cv = reader.GetAttribute("value");
                            if (!string.IsNullOrEmpty(cv))
                            {
                                int.TryParse(reader.GetAttribute("count"), out int cnt);
                                candidates[cv] = cnt;
                            }
                            break;
                        }

                        case "PersonalUse":
                        {
                            current = null;
                            string value = reader.GetAttribute("value");
                            int.TryParse(reader.GetAttribute("count"), out int count);
                            if (!string.IsNullOrEmpty(value) && count > 0 &&
                                snap.ByExact.TryGetValue(value, out var entry))
                                BumpPersonalUse(snap, entry, count);
                            break;
                        }

                        case "NewWord":
                        {
                            string value = reader.GetAttribute("value");
                            if (string.IsNullOrEmpty(value)) { current = null; break; }
                            int.TryParse(reader.GetAttribute("frequency"),   out int freq);
                            int.TryParse(reader.GetAttribute("personalUse"), out int use);
                            var entry = new WordEntry(value, freq) { IsFromBase = false };
                            snap.ByExact[value] = entry;
                            snap.ByFrequency.Add(entry);
                            if (use > 0) BumpPersonalUse(snap, entry, use);
                            current = entry;
                            break;
                        }

                        case "Next":
                            if (current != null && current.NextWords.Count < MaxNextWords)
                            {
                                string nv = reader.GetAttribute("value");
                                if (!string.IsNullOrEmpty(nv))
                                {
                                    int.TryParse(reader.GetAttribute("frequency"),   out int nfreq);
                                    int.TryParse(reader.GetAttribute("personalUse"), out int nuse);
                                    current.NextWords.Add(new NextEntry(nv, nfreq)
                                        { IsFromBase = false, PersonalUseCount = nuse });
                                }
                            }
                            break;

                        case "PairUse":
                        {
                            current = null;
                            string word = reader.GetAttribute("word");
                            string next = reader.GetAttribute("next");
                            int.TryParse(reader.GetAttribute("count"), out int count);
                            if (!string.IsNullOrEmpty(word) && !string.IsNullOrEmpty(next) && count > 0 &&
                                snap.ByExact.TryGetValue(word, out var prevEntry))
                            {
                                var pair = prevEntry.NextWords.Find(
                                    n => string.Equals(n.Word, next, StringComparison.Ordinal));
                                if (pair != null) pair.PersonalUseCount += count;
                            }
                            break;
                        }

                        case "NewPair":
                        {
                            current = null;
                            string word = reader.GetAttribute("word");
                            string next = reader.GetAttribute("next");
                            if (!string.IsNullOrEmpty(word) && !string.IsNullOrEmpty(next) &&
                                snap.ByExact.TryGetValue(word, out var prevEntry) &&
                                prevEntry.NextWords.Count < MaxNextWords)
                            {
                                int.TryParse(reader.GetAttribute("frequency"),   out int nfreq);
                                int.TryParse(reader.GetAttribute("personalUse"), out int nuse);
                                prevEntry.NextWords.Add(new NextEntry(next, nfreq)
                                    { IsFromBase = false, PersonalUseCount = nuse });
                            }
                            break;
                        }
                    }
                }

                // Re-sort every word's NextWords once, after all PersonalUse/
                // PairUse/NewPair records have been applied, using the same key
                // RecordWord uses at runtime — simpler and just as cheap as
                // re-sorting incrementally record-by-record while parsing.
                foreach (var entry in snap.ByExact.Values)
                    if (entry.NextWords.Count > 1)
                        entry.NextWords.Sort(RankPairs);
            }
            catch
            {
                // A corrupt or unreadable overlay must never block the base
                // database from loading — worst case, learned data is lost.
            }

            return candidates;
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
        ///     <paramref name="lastCompletedWord"/> in the training corpus,
        ///     personally-reinforced pairs ranked first within this step.
        ///     Filtered by <paramref name="currentPrefix"/> when the user has
        ///     started typing. These are the most contextually relevant results.
        ///   </item>
        ///   <item>
        ///     <b>Personally-used words</b> — words the user has typed before
        ///     (tracked separately from corpus frequency so they reliably surface
        ///     regardless of how large a competing word's base frequency is),
        ///     up to a cap that always leaves room for at least one normal
        ///     suggestion. Fills any slots Step 1 did not use.
        ///   </item>
        ///   <item>
        ///     <b>Frequency-sorted first words</b> — the most common words in
        ///     the entire database, filtered by prefix when relevant. These fill
        ///     any prediction slots Steps 1–2 did not fill, so the suggestion
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
                    // NextWords is kept sorted descending by frequency (both at load
                    // time and whenever RecordWord bumps an entry), so iterating in
                    // list order already yields the strongest pairings first.
                    foreach (NextEntry ne in lastEntry.NextWords)
                    {
                        string w = ne.Word;
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

            // Adds up to maxAdd matching words from source (in whatever order
            // source is already sorted in) to result, applying the same
            // prefix/case rules used throughout this method. Shared by Step 1.5
            // (personal-use tier, capped) and Step 2 (frequency fallback,
            // uncapped) below — identical matching logic, different source list
            // and cap.
            void AddMatching(List<WordEntry> source, int maxAdd)
            {
                int added = 0;
                if (preferUpperCase && hasPrefix)
                {
                    // The user held Shift and started typing mid-sentence.
                    // They explicitly want a proper noun, so only return words
                    // that start with an uppercase letter, matching the prefix
                    // with a case-sensitive comparison.
                    foreach (var entry in source)
                    {
                        if (result.Count >= count || added >= maxAdd) break;
                        if (!MatchesPrefix(entry.Word, currentPrefix, true)) continue;
                        if (!StartsWithCase(entry.Word, true)) continue;
                        if (seen.Add(entry.Word)) { result.Add(entry.Word); added++; }
                    }
                }
                else if (preferUpperCase)
                {
                    // Shift is active but no characters typed yet — list proper
                    // nouns first (uppercase-starting), then fill any remaining
                    // slots with common lowercase words.
                    foreach (var entry in source)
                    {
                        if (result.Count >= count || added >= maxAdd) break;
                        if (!StartsWithCase(entry.Word, true)) continue;
                        if (seen.Add(entry.Word)) { result.Add(entry.Word); added++; }
                    }
                    foreach (var entry in source)
                    {
                        if (result.Count >= count || added >= maxAdd) break;
                        if (!StartsWithCase(entry.Word, false)) continue;
                        if (seen.Add(entry.Word)) { result.Add(entry.Word); added++; }
                    }
                }
                else
                {
                    // Normal case: return matching words in source order.
                    foreach (var entry in source)
                    {
                        if (result.Count >= count || added >= maxAdd) break;

                        // Skip words that don't start with the typed prefix.
                        if (hasPrefix && !MatchesPrefix(entry.Word, currentPrefix, !sentenceStart && prefixUpper)) continue;

                        // Mid-sentence: skip uppercase-starting words unless the
                        // user typed an uppercase prefix (filterUpper=true).
                        if (!sentenceStart && !StartsWithCase(entry.Word, filterUpper)) continue;

                        if (seen.Add(entry.Word)) { result.Add(entry.Word); added++; }
                    }
                }
            }

            // ── Step 1.5: Personally-used words ───────────────────────
            //
            // Words the user has typed before (snap.ByPersonalUse, already
            // sorted by PersonalUseCount descending), ranked ahead of raw
            // corpus frequency so they reliably surface regardless of how much
            // larger a competing base word's frequency is. Capped so normal
            // frequency-based suggestions are never fully crowded out.
            if (result.Count < count)
                AddMatching(snap.ByPersonalUse, PersonalCap(count));

            // ── Step 2: First words ───────────────────────────────────
            //
            // If Steps 1–1.5 did not fill all the requested slots, pad with the
            // most-frequent words from the entire database (already sorted).
            if (result.Count < count)
                AddMatching(snap.ByFrequency, int.MaxValue);

            // Capitalise all results when at the start of a sentence.
            // We do this last — the matching above works with the stored
            // lowercase forms, and only the displayed string needs the capital.
            if (sentenceStart)
                for (int i = 0; i < result.Count; i++)
                    if (result[i].Length > 0)
                        result[i] = char.ToUpper(result[i][0]) + result[i].Substring(1);

            return result;
        }

        // ── Learning engine ──────────────────────────────────────────

        /// <summary>
        /// Records that <paramref name="word"/> was just typed or chosen, and
        /// (when <paramref name="previousWord"/> is given) that it followed
        /// <paramref name="previousWord"/> — i.e. a word-pair (bigram) occurrence.
        ///
        /// <para>
        /// Known words have their <see cref="WordEntry.PersonalUseCount"/>
        /// incremented immediately — kept separate from the base corpus
        /// <see cref="WordEntry.Frequency"/>, so personal usage gets its own
        /// ranking tier (see <c>GetPredictionsCore</c>'s "Step 1.5") instead of
        /// being invisible against — or unpredictably outranking — a base
        /// frequency that can be orders of magnitude larger. The word-pair link's
        /// personal-use count is bumped the same way (or the pair is created, up
        /// to <see cref="MaxNextWords"/> pairs per word). Unknown words go into a
        /// small candidate buffer and are only promoted to a real, predictable
        /// word once they have been seen more than
        /// <see cref="CandidatePromotionThreshold"/> times — this filters out
        /// one-off typos.
        /// </para>
        ///
        /// <para>
        /// No-op when no database is loaded or <see cref="LearningEnabled"/> is
        /// <c>false</c>.
        /// </para>
        ///
        /// <para>
        /// <b>Thread-safety:</b> must only be called from the UI thread. It
        /// mutates <see cref="WordEntry"/>/<c>NextEntry</c> objects reachable from
        /// the published snapshot in place, and appends to (never removes from,
        /// except a bounded replace in the word-pair list) the snapshot's small
        /// <c>ByPersonalUse</c>/<c>NextWords</c> lists. This is safe as long as
        /// nothing else enumerates those same objects concurrently;
        /// <see cref="SaveIfDirty"/> guarantees this by copying all data into
        /// plain values on the caller's thread before handing off to a background
        /// thread for the actual file write.
        /// </para>
        /// </summary>
        /// <param name="previousWord">
        /// The word completed immediately before <paramref name="word"/>, or
        /// <c>null</c>/empty if there is none (e.g. the first word of a
        /// session). Matched the same way <see cref="GetPredictions"/> matches
        /// it: exact, then lower-cased fallback.
        /// </param>
        /// <param name="word">The word that was just completed.</param>
        public static void RecordWord(string previousWord, string word)
        {
            if (!_isLoaded || !LearningEnabled || string.IsNullOrEmpty(word)) return;

            var snap = _snapshot;

            if (snap.ByExact.TryGetValue(word, out var entry))
            {
                BumpPersonalUse(snap, entry);
                _dirty = true;
            }
            else
            {
                _candidates.TryGetValue(word, out int count);
                count++;
                if (count > CandidatePromotionThreshold)
                {
                    _candidates.Remove(word);
                    entry = PromoteInternal(snap, word, count);
                }
                else
                {
                    _candidates[word] = count;
                }
                _dirty = true;
            }

            // ── Word-pair (bigram) link ───────────────────────────────
            if (!string.IsNullOrEmpty(previousWord))
            {
                WordEntry prevEntry;
                if (!snap.ByExact.TryGetValue(previousWord, out prevEntry))
                    snap.ByExact.TryGetValue(previousWord.ToLower(), out prevEntry);

                if (prevEntry != null)
                {
                    var next = prevEntry.NextWords.Find(
                        n => string.Equals(n.Word, word, StringComparison.Ordinal));
                    if (next != null)
                    {
                        next.PersonalUseCount++;
                        _dirty = true;
                    }
                    else if (prevEntry.NextWords.Count < MaxNextWords)
                    {
                        prevEntry.NextWords.Add(new NextEntry(word, 1) { PersonalUseCount = 1 });
                        _dirty = true;
                    }
                    else
                    {
                        // List is full — replace the weakest existing pair only if
                        // it is no stronger than a brand-new (frequency 1) entry,
                        // so well-established pairs are never displaced by noise.
                        // "Weakest" uses the same (PersonalUseCount, Frequency)
                        // ordering as the ranking sort below.
                        NextEntry weakest = prevEntry.NextWords[0];
                        foreach (var n in prevEntry.NextWords)
                            if (IsWeakerPair(n, weakest)) weakest = n;
                        if (weakest.PersonalUseCount == 0 && weakest.Frequency <= 1)
                        {
                            prevEntry.NextWords.Remove(weakest);
                            prevEntry.NextWords.Add(new NextEntry(word, 1) { PersonalUseCount = 1 });
                            _dirty = true;
                        }
                    }
                    // Personally-reinforced pairs first, then strongest base pairs,
                    // so GetPredictionsCore's Step 1 (which takes them in list
                    // order) surfaces the most relevant pairing first.
                    prevEntry.NextWords.Sort(RankPairs);
                }
            }
        }

        // Shared ranking order for a WordEntry.NextWords list: personal use
        // first, base frequency as tie-break, both descending. Used by
        // RecordWord (incremental re-sort after a bump) and ApplyOverlay
        // (one re-sort pass after merging).
        private static int RankPairs(NextEntry a, NextEntry b)
        {
            int byPersonal = b.PersonalUseCount.CompareTo(a.PersonalUseCount);
            return byPersonal != 0 ? byPersonal : b.Frequency.CompareTo(a.Frequency);
        }

        // True if a is strictly weaker than b: personal use compared first,
        // base frequency as tie-break. Mirrors the NextWords ranking sort.
        private static bool IsWeakerPair(NextEntry a, NextEntry b) =>
            a.PersonalUseCount != b.PersonalUseCount
                ? a.PersonalUseCount < b.PersonalUseCount
                : a.Frequency < b.Frequency;

        /// <summary>
        /// Increments <paramref name="entry"/>'s <see cref="WordEntry.PersonalUseCount"/>
        /// by <paramref name="amount"/> and keeps <paramref name="snap"/>'s
        /// <c>ByPersonalUse</c> list correct: adds the entry the first time its
        /// count leaves zero, and re-sorts (cheap — this list only ever contains
        /// a user's personally-used words, never the full corpus).
        /// </summary>
        private static void BumpPersonalUse(DbSnapshot snap, WordEntry entry, int amount = 1)
        {
            bool wasZero = entry.PersonalUseCount == 0;
            entry.PersonalUseCount += amount;
            if (wasZero) snap.ByPersonalUse.Add(entry);
            snap.ByPersonalUse.Sort((a, b) => b.PersonalUseCount.CompareTo(a.PersonalUseCount));
        }

        /// <summary>
        /// Creates a new <see cref="WordEntry"/> for <paramref name="word"/>,
        /// not from the base file (<see cref="WordEntry.IsFromBase"/> = false, so
        /// it is written to the overlay in full as a <c>&lt;NewWord&gt;</c> rather
        /// than a <c>&lt;PersonalUse&gt;</c> delta), and adds it to
        /// <paramref name="snap"/>'s collections including <c>ByPersonalUse</c> —
        /// a promoted word is, by definition, personally used, so it should
        /// surface via the personal-use ranking tier immediately rather than
        /// wait to accumulate more occurrences. Shared by automatic promotion
        /// (candidate count exceeds the threshold) and manual promotion via
        /// <see cref="PromoteCandidate"/>.
        /// </summary>
        private static WordEntry PromoteInternal(DbSnapshot snap, string word, int useCount)
        {
            var entry = new WordEntry(word, Math.Max(1, useCount)) { IsFromBase = false };
            snap.ByExact[word] = entry;
            snap.ByFrequency.Add(entry);
            BumpPersonalUse(snap, entry, Math.Max(1, useCount));
            return entry;
        }

        /// <summary>
        /// Current candidate (not-yet-promoted) words and how many times each has
        /// been typed, most-seen first. Empty if no database is loaded or nothing
        /// has been typed yet.
        /// </summary>
        public static IReadOnlyList<(string Word, int Count)> GetCandidates() =>
            _candidates
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

        /// <summary>
        /// Immediately promotes a candidate word to the real word list,
        /// bypassing the usual occurrence-count threshold. Used by the
        /// candidate-management UI (Edit Keyboard → Word Prediction).
        /// </summary>
        /// <returns><c>true</c> if the word was a known candidate and was promoted.</returns>
        public static bool PromoteCandidate(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            if (!_isLoaded) return false;
            if (!_candidates.TryGetValue(word, out int count)) return false;

            _candidates.Remove(word);
            PromoteInternal(_snapshot, word, count);
            _dirty = true;
            return true;
        }

        /// <summary>
        /// Discards a candidate word (e.g. a typo the user does not want
        /// remembered) without promoting it. It will start accumulating from
        /// zero again if typed in the future.
        /// </summary>
        /// <returns><c>true</c> if the word was a known candidate and was removed.</returns>
        public static bool RemoveCandidate(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            bool removed = _candidates.Remove(word);
            if (removed) _dirty = true;
            return removed;
        }

        /// <summary>
        /// <c>true</c> when learned frequencies, word-pair links, or the
        /// candidate buffer have changed since the last successful save.
        /// </summary>
        public static bool IsDirty => _dirty;

        // Plain-data copy of everything a save needs, built on the calling
        // thread so the background write never touches live WordEntry/NextEntry
        // objects that the UI thread might be mutating concurrently via RecordWord.
        // Mirrors the overlay's own record types (see ApplyOverlay) exactly, so
        // BuildSaveData / WriteSaveData / ApplyOverlay stay in lock-step.
        private readonly struct SaveData
        {
            public readonly List<(string word, int count)> personalUse;
            public readonly List<(string word, int frequency, int personalUse,
                                   List<(string word, int frequency, int personalUse)> next)> newWords;
            public readonly List<(string word, string next, int count)> pairUse;
            public readonly List<(string word, string next, int frequency, int personalUse)> newPair;
            public readonly List<(string word, int count)> candidates;

            public SaveData(
                List<(string, int)> personalUse,
                List<(string, int, int, List<(string, int, int)>)> newWords,
                List<(string, string, int)> pairUse,
                List<(string, string, int, int)> newPair,
                List<(string, int)> candidates)
            {
                this.personalUse = personalUse;
                this.newWords     = newWords;
                this.pairUse      = pairUse;
                this.newPair      = newPair;
                this.candidates   = candidates;
            }
        }

        private static SaveData BuildSaveData()
        {
            var snap        = _snapshot;
            var personalUse = new List<(string, int)>();
            var newWords    = new List<(string, int, int, List<(string, int, int)>)>();
            var pairUse     = new List<(string, string, int)>();
            var newPair     = new List<(string, string, int, int)>();

            foreach (var e in snap.ByExact.Values)
            {
                if (e.IsFromBase)
                {
                    if (e.PersonalUseCount > 0) personalUse.Add((e.Word, e.PersonalUseCount));

                    foreach (var n in e.NextWords)
                    {
                        if (n.IsFromBase)
                        {
                            if (n.PersonalUseCount > 0) pairUse.Add((e.Word, n.Word, n.PersonalUseCount));
                        }
                        else
                        {
                            newPair.Add((e.Word, n.Word, n.Frequency, n.PersonalUseCount));
                        }
                    }
                }
                else
                {
                    // Brand-new word (promoted candidate): written in full,
                    // including all of its own word-pairs as children — none of
                    // them have a base counterpart either, so no PairUse/NewPair
                    // split is needed for this word's own NextWords.
                    var next = new List<(string, int, int)>(e.NextWords.Count);
                    foreach (var n in e.NextWords) next.Add((n.Word, n.Frequency, n.PersonalUseCount));
                    newWords.Add((e.Word, e.Frequency, e.PersonalUseCount, next));
                }
            }

            var candidates = new List<(string, int)>(_candidates.Count);
            foreach (var kv in _candidates) candidates.Add((kv.Key, kv.Value));

            return new SaveData(personalUse, newWords, pairUse, newPair, candidates);
        }

        /// <summary>
        /// Writes <paramref name="data"/> to <paramref name="path"/> as a
        /// <c>WordDatabaseOverlay</c> file, using the same crash-safe
        /// temp-file-then-<see cref="File.Replace(string,string,string)"/>
        /// pattern as <c>SettingsManager</c>.
        /// </summary>
        private static void WriteSaveData(SaveData data, string path)
        {
            string tmp = path + ".tmp";
            try
            {
                var xs = new XmlWriterSettings { Indent = true };
                using (var writer = XmlWriter.Create(tmp, xs))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("WordDatabaseOverlay");
                    writer.WriteAttributeString("version", "1");

                    writer.WriteStartElement("Candidates");
                    foreach (var (word, count) in data.candidates)
                    {
                        writer.WriteStartElement("Candidate");
                        writer.WriteAttributeString("value", word);
                        writer.WriteAttributeString("count", count.ToString());
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); // Candidates

                    foreach (var (word, count) in data.personalUse)
                    {
                        writer.WriteStartElement("PersonalUse");
                        writer.WriteAttributeString("value", word);
                        writer.WriteAttributeString("count", count.ToString());
                        writer.WriteEndElement();
                    }

                    // Each NewWord is fully self-contained (its own <Next>
                    // children written immediately inside it) — ApplyOverlay
                    // relies on that ordering to route them correctly.
                    foreach (var (word, frequency, personalUse, next) in data.newWords)
                    {
                        writer.WriteStartElement("NewWord");
                        writer.WriteAttributeString("value", word);
                        writer.WriteAttributeString("frequency", frequency.ToString());
                        writer.WriteAttributeString("personalUse", personalUse.ToString());
                        foreach (var (nWord, nFreq, nUse) in next)
                        {
                            writer.WriteStartElement("Next");
                            writer.WriteAttributeString("value", nWord);
                            writer.WriteAttributeString("frequency", nFreq.ToString());
                            writer.WriteAttributeString("personalUse", nUse.ToString());
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement(); // NewWord
                    }

                    foreach (var (word, next, count) in data.pairUse)
                    {
                        writer.WriteStartElement("PairUse");
                        writer.WriteAttributeString("word", word);
                        writer.WriteAttributeString("next", next);
                        writer.WriteAttributeString("count", count.ToString());
                        writer.WriteEndElement();
                    }

                    foreach (var (word, next, frequency, personalUse) in data.newPair)
                    {
                        writer.WriteStartElement("NewPair");
                        writer.WriteAttributeString("word", word);
                        writer.WriteAttributeString("next", next);
                        writer.WriteAttributeString("frequency", frequency.ToString());
                        writer.WriteAttributeString("personalUse", personalUse.ToString());
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement(); // WordDatabaseOverlay
                    writer.WriteEndDocument();
                }

                if (File.Exists(path))
                    File.Replace(tmp, path, path + ".bak");
                else
                    File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
                throw;
            }
        }

        /// <summary>
        /// Synchronously writes the current learned data (personal-use counts,
        /// new words/word-pairs, and the candidate buffer) to the overlay file
        /// paired with whatever base database is currently loaded (see
        /// <see cref="DeriveOverlayPath"/>). No-op when no database is loaded.
        /// Used directly by tests and by the app-close handler, where blocking
        /// briefly is acceptable.
        /// </summary>
        public static void SaveNow()
        {
            if (!_isLoaded || _overlayPath == null) return;
            WriteSaveData(BuildSaveData(), _overlayPath);
            _dirty = false;
        }

        /// <summary>
        /// Saves to the overlay file on a background thread, but only if
        /// something has actually changed (<see cref="IsDirty"/>) and no save is
        /// already in flight. Intended to be called periodically (e.g. every
        /// 30 s) from a UI timer.
        /// </summary>
        public static void SaveIfDirty()
        {
            if (_saving || !_dirty) return;
            if (!_isLoaded || _overlayPath == null) return;

            _saving = true;
            string path = _overlayPath;
            // Build the plain-data copy here, on the caller's (UI) thread —
            // see BuildSaveData's remarks on why the background thread must not
            // touch the live WordEntry/NextEntry objects directly.
            SaveData data = BuildSaveData();

            System.Threading.Tasks.Task.Run(() =>
            {
                try   { WriteSaveData(data, path); _dirty = false; }
                catch { /* best-effort — stays dirty, retried on the next cycle */ }
                finally { _saving = false; }
            });
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of prediction slots the personal-use tier (Step 1.5)
        /// may fill: up to 3/4 of <paramref name="count"/>, rounded up, but
        /// always leaving at least 1 slot for a normal frequency-ranked
        /// suggestion whenever <paramref name="count"/> &gt; 1.
        /// </summary>
        private static int PersonalCap(int count) =>
            count <= 1 ? 0 : Math.Min((int)Math.Ceiling(count * 0.75), count - 1);

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

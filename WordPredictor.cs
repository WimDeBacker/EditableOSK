// WordPredictor.cs — testable word-prediction state machine
//
// Extracted from KeyboardForm so the prediction logic can be unit-tested
// without a WinForms form. KeyboardForm delegates all tracking to this class.
//
// Public surface:
//   OnKeySent(send, shifted)  — call after every key is sent
//   OnWPClick(slot)           — call when a WP prediction key is clicked;
//                               returns the word to send (or null)
//   OnSentenceStart()         — call on startup / after layout load
//   Predictions[slot]         — current predicted words (read-only)
//   NextWordUpper             — true when next word should be capitalised
//   ShiftShouldBeLatchad      — true when the UI should show Shift as active
//   LastActionWasPrediction   — true when a trailing space needs to be removed

using System;
using System.Collections.Generic;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Holds the result of a word-prediction key click.
    /// It tells the caller exactly what to send to the target application:
    /// first erase the partly-typed prefix with backspaces, then send the
    /// completed word, then append a trailing space.
    /// </summary>
    /// <remarks>
    /// The caller (KeyboardForm) uses <see cref="ToString"/> to build the
    /// full SendKeys string, or accesses the three parts individually to
    /// send them in separate steps.
    /// </remarks>
    public class WPClickResult
    {
        /// <summary>
        /// Number of backspace characters to send before the word.
        /// This erases the prefix the user already typed manually
        /// so it can be replaced by the fully-completed predicted word.
        /// </summary>
        public int    Backspaces { get; }

        /// <summary>
        /// The completed (and possibly capitalised) word to send.
        /// </summary>
        public string Word       { get; }

        /// <summary>
        /// A single space " " that is always appended after the word.
        /// Having an explicit property makes it easy to skip the suffix
        /// if the caller needs to handle spacing differently.
        /// </summary>
        public string Suffix     { get; }

        /// <summary>
        /// Creates a new click result with all three parts.
        /// </summary>
        /// <param name="backspaces">How many backspaces to emit first.</param>
        /// <param name="word">The word to insert.</param>
        /// <param name="suffix">The text to append after the word (usually a space).</param>
        public WPClickResult(int backspaces, string word, string suffix)
        { Backspaces = backspaces; Word = word; Suffix = suffix; }

        /// <summary>
        /// Combines all three parts into one string ready for SendKeys.
        /// The backspace characters (\b) erase the prefix, then the word
        /// and trailing space are written.
        /// </summary>
        public override string ToString() =>
            new string('\b', Backspaces) + Word + Suffix;
    }

    /// <summary>
    /// Tracks typing state and generates word predictions after every keystroke.
    /// <para>
    /// The predictor acts like a small state machine. As keys are sent it
    /// maintains:
    /// <list type="bullet">
    ///   <item>The letters typed so far for the current word (<c>_wordBuffer</c>).</item>
    ///   <item>The last fully completed word (<c>_lastCompletedWord</c>), used to
    ///         look up likely next words.</item>
    ///   <item>Whether the next word should start with a capital letter
    ///         (<c>_nextWordUpper</c>), which happens at sentence boundaries.</item>
    /// </list>
    /// </para>
    /// <para>
    /// After each state change the predictor asks <see cref="WordDatabase"/> for
    /// fresh suggestions and fires <see cref="PredictionsChanged"/> so the UI can
    /// update its prediction buttons.
    /// </para>
    /// </summary>
    public class WordPredictor
    {
        // ── State ────────────────────────────────────────────────────

        // Letters typed so far for the word currently being built.
        // Reset to "" whenever a word is completed or deleted.
        private string   _wordBuffer            = "";

        // The most recently completed word.  Used by WordDatabase to find
        // words that typically follow it (bigram-style predictions).
        private string   _lastCompletedWord     = "";

        // When true the next word's first letter should be upper-case.
        // Set at sentence boundaries (after . ! ? or ENTER) and cleared
        // as soon as the first character of the new word is typed.
        private bool     _nextWordUpper           = false;

        // When true the Shift key in the UI should appear pressed/active.
        // Mirrors _nextWordUpper for real sentence starts, but is also
        // set/cleared independently when the user manually toggles Shift.
        private bool     _shiftLatched            = false;

        // Tracks whether the first word of the current sentence has been
        // finished.  Needed to distinguish "first word" from "second word"
        // so that capitalisation is only applied to the opening word.
        private bool     _sentenceFirstWordDone   = false;

        // True only when sentence-start state was set automatically
        // (by punctuation / ENTER / OnSentenceStart).  A manual Shift
        // press does NOT set this flag, which is how we tell the difference
        // between "start of sentence" and "user wants a capital in the middle".
        private bool     _atSentenceStart         = false;

        // True immediately after a word-prediction click.
        // KeyboardForm uses this to know it must first remove the trailing
        // space that was auto-inserted after the prediction before it can
        // send the next punctuation character.
        private bool     _lastActionWasPrediction = false;

        // Fixed-size array for prediction slots (maximum 10).
        // The actual number of slots in use is _slotCount.
        // Unused tail slots are kept as empty strings so array bounds
        // never need to be checked on the UI side.
        private readonly string[] _predictions  = new string[10];

        // How many prediction slots the current layout actually shows.
        // Clamped to [1, 10] in the constructor and SetSlotCount.
        private int               _slotCount;

        // ── Events ────────────────────────────────────────────────────

        /// <summary>
        /// Fired every time the prediction list changes.
        /// The UI should subscribe here and redraw its word-prediction keys.
        /// </summary>
        public event Action PredictionsChanged;

        /// <summary>
        /// Fired when the Shift latch state changes.
        /// The bool argument is <c>true</c> to show Shift as active
        /// and <c>false</c> to release it.
        /// </summary>
        public event Action<bool> ShiftLatchChanged;

        /// <summary>
        /// Fired when the predictor needs to inject a character into the
        /// target application on its own (currently used to add a space
        /// after sentence-ending punctuation like '.' '!' '?').
        /// </summary>
        public event Action<string> InjectSend;

        // ── Read-only state ───────────────────────────────────────────

        /// <summary>Current prediction words, indexed by slot number.</summary>
        public IReadOnlyList<string> Predictions     => _predictions;

        /// <summary>
        /// True when the next word that is started should begin with a
        /// capital letter (i.e. we are at a sentence boundary).
        /// </summary>
        public bool   NextWordUpper                  => _nextWordUpper;

        /// <summary>
        /// True when the UI should show the Shift key in its active state.
        /// This matches <see cref="NextWordUpper"/> for automatic sentence
        /// starts but can differ when the user toggles Shift manually.
        /// </summary>
        public bool   ShiftShouldBeLatched           => _shiftLatched;

        /// <summary>
        /// True immediately after a prediction word was chosen by clicking
        /// a WP key.  KeyboardForm reads this to decide whether to remove
        /// the trailing space before sending punctuation.
        /// </summary>
        public bool   LastActionWasPrediction        => _lastActionWasPrediction;

        /// <summary>Letters typed so far for the word currently in progress.</summary>
        public string WordBuffer                     => _wordBuffer;

        /// <summary>The most recently completed word (used for next-word lookup).</summary>
        public string LastCompletedWord              => _lastCompletedWord;

        // ── Constructor ───────────────────────────────────────────────

        /// <summary>
        /// Creates a new predictor.
        /// </summary>
        /// <param name="slotCount">
        /// Number of prediction slots the UI will display.
        /// Clamped to the range [1, 10].  Defaults to 7.
        /// </param>
        public WordPredictor(int slotCount = 7)
        {
            // Clamp to valid range so the rest of the code never has to worry
            // about out-of-range slot counts.
            _slotCount = Math.Max(1, Math.Min(10, slotCount));
        }

        /// <summary>
        /// Changes how many prediction slots are populated and immediately
        /// refreshes the predictions to match.
        /// Call this after loading a new keyboard layout that has a different
        /// number of word-prediction keys than the previous layout.
        /// </summary>
        /// <param name="slotCount">New slot count, clamped to [1, 10].</param>
        public void SetSlotCount(int slotCount)
        {
            int clamped = Math.Max(1, Math.Min(10, slotCount));
            if (clamped == _slotCount) return; // nothing changed — skip the refresh
            _slotCount = clamped;
            RefreshPredictions();
        }

        // ── API ───────────────────────────────────────────────────────

        /// <summary>
        /// Call this when the keyboard first becomes visible or when a new
        /// layout is loaded.  It puts the predictor in "sentence start" mode:
        /// Shift is latched and the first word will be capitalised automatically.
        /// </summary>
        public void OnSentenceStart()
        {
            SetSentenceStart();
            RefreshPredictions();
        }

        /// <summary>
        /// Notifies the predictor that a key has just been sent to the target
        /// application.  This is the main entry point — call it after every
        /// key press so the predictor can keep its state in sync with what
        /// is actually on screen.
        /// </summary>
        /// <param name="send">
        /// The raw Send string from <c>KeyProps</c>.  This can be a single
        /// printable character (e.g. <c>"a"</c>, <c>"A"</c>, <c>" "</c>),
        /// a punctuation mark (<c>"."</c>, <c>","</c>), or a special
        /// SendKeys token (<c>"{ENTER}"</c>, <c>"{BACKSPACE}"</c>).
        /// </param>
        /// <param name="shifted">
        /// True if the Shift key was active when this key was sent.
        /// Used to detect mid-sentence capitalisation (proper nouns).
        /// </param>
        public void OnKeySent(string send, bool shifted)
        {
            if (string.IsNullOrEmpty(send)) return;

            // ── Single plain character ────────────────────────────────
            // IsPlainText returns true for normal printable characters as
            // opposed to special SendKeys tokens like {ENTER}.
            if (SendKeysHelper.IsPlainText(send) && send.Length == 1)
            {
                char c = send[0];

                // ── Punctuation / symbol ──────────────────────────────
                if (char.IsPunctuation(c) || char.IsSymbol(c))
                {
                    // KeyboardForm already emits a backspace to delete the
                    // trailing space that was inserted after the last prediction
                    // BEFORE it calls OnKeySent for punctuation.  We must NOT
                    // inject another backspace here — just clear the flag.
                    _lastActionWasPrediction = false;

                    // If the user was in the middle of typing a word and then
                    // pressed punctuation, finish that word first.
                    if (_wordBuffer.Length > 0) CompleteWord(_wordBuffer);
                    _wordBuffer = "";

                    // Auto-space: add a space after these punctuation marks so
                    // the next word can start immediately without a manual space.
                    bool addSpace = c == '.' || c == '!' || c == '?' ||
                                    c == ',' || c == ';' || c == ':';
                    if (addSpace)
                        InjectSend?.Invoke(" ");

                    // Sentence-ending marks reset capitalisation; other marks do not.
                    if (c == '.' || c == '!' || c == '?')
                        SetSentenceStart();
                    else
                        _nextWordUpper = false; // mid-sentence punctuation — stay lower-case

                    RefreshPredictions();
                    return;
                }

                // ── Letter, apostrophe, or hyphen ─────────────────────
                // These are all word-building characters; add them to the buffer.
                if (char.IsLetter(c) || c == '\'' || c == '-')
                {
                    _lastActionWasPrediction = false;

                    // Check BEFORE any state changes whether the user pressed Shift
                    // in the middle of a sentence (not at a sentence start).
                    // That signals intent to type a proper noun, so we should bias
                    // predictions toward capitalised words without forcing ALL
                    // future words to be capitalised.
                    bool preferProperNoun = shifted && !_atSentenceStart && _wordBuffer.Length == 0;

                    if (_wordBuffer.Length == 0)
                    {
                        // This is the very first character of a new word.
                        if (_nextWordUpper && _sentenceFirstWordDone)
                        {
                            // We finished the first word of the sentence already
                            // (e.g. user typed "Hello" and pressed space).
                            // The second word starts mid-sentence → stop capitalising.
                            _nextWordUpper         = false;
                            _atSentenceStart       = false;
                            _sentenceFirstWordDone = false;
                        }
                        else if (_nextWordUpper)
                        {
                            // First character of the first word after a sentence
                            // boundary — release the Shift latch now (one-shot: only
                            // the first letter of the first word is forced upper-case).
                            _shiftLatched = false;
                            ShiftLatchChanged?.Invoke(false);
                        }
                        else
                        {
                            // Mid-sentence word, no forced capitalisation.
                            // Note: we do NOT set _nextWordUpper here.
                            // If the user held Shift here it is treated as a proper
                            // noun via preferProperNoun, not as a sentence start.
                            // _nextWordUpper is reserved for genuine sentence-start
                            // triggers (punctuation, ENTER, or OnSentenceStart).
                        }
                    }

                    // Store the character.  If the user held Shift and the character
                    // is lower-case (e.g. SendKeys sends "a" with shifted=true),
                    // convert it to upper-case to match what was actually inserted.
                    _wordBuffer += (shifted && char.IsLower(c)) ? char.ToUpper(c) : c;
                    RefreshPredictions(preferProperNoun);
                    return;
                }
            }

            // ── Space / Tab ───────────────────────────────────────────
            // Both finish the current word and move to the next one.
            if (send == " " || send == "{TAB}")
            {
                _lastActionWasPrediction = false;
                if (_wordBuffer.Length > 0) CompleteWord(_wordBuffer);
                _wordBuffer = "";

                // Mark that at least one word of the sentence is done so the
                // predictor knows the second word should NOT be capitalised.
                if (_nextWordUpper) _sentenceFirstWordDone = true;

                RefreshPredictions();
                return;
            }

            // ── Enter → new sentence ──────────────────────────────────
            // Pressing Enter finishes the current word and starts a fresh
            // sentence, so the next word will be capitalised.
            if (send == "{ENTER}")
            {
                _lastActionWasPrediction = false;
                if (_wordBuffer.Length > 0) CompleteWord(_wordBuffer);
                _wordBuffer = "";
                SetSentenceStart(); // treat each new line as a new sentence
                RefreshPredictions();
                return;
            }

            // ── Backspace ─────────────────────────────────────────────
            // Remove the last character from the word buffer.
            // If the buffer becomes empty we also forget the last completed
            // word, because the user has deleted back to the previous word
            // boundary and may now continue editing it.
            if (send == "{BACKSPACE}")
            {
                _lastActionWasPrediction = false;
                if (_wordBuffer.Length > 0)
                {
                    // [..^1] is C# range syntax meaning "all but the last character".
                    _wordBuffer = _wordBuffer[..^1];
                    if (_wordBuffer.Length == 0)
                        _lastCompletedWord = ""; // no prefix left — reset next-word context
                }
                RefreshPredictions();
            }
        }

        /// <summary>
        /// Call when the user clicks a word-prediction (WP) key at
        /// the given slot index.
        /// </summary>
        /// <param name="slot">
        /// Zero-based index into <see cref="Predictions"/>.
        /// </param>
        /// <returns>
        /// A <see cref="WPClickResult"/> describing how many backspaces to
        /// emit, which word to insert, and the trailing suffix (space).
        /// Returns <c>null</c> if the slot is out of range or empty.
        /// The caller is responsible for actually sending the keys;
        /// this method only updates internal state.
        /// </returns>
        public WPClickResult OnWPClick(int slot)
        {
            if (slot < 0 || slot >= _predictions.Length) return null;
            string predicted = _predictions[slot];
            if (string.IsNullOrEmpty(predicted)) return null;

            // The user typed a prefix (e.g. "hel") before clicking the prediction
            // "hello".  We need to erase that prefix first with backspaces.
            int backspaces = _wordBuffer.Length;

            string toSend = predicted;

            // Record the chosen word as the last completed word for next-word
            // prediction, then reset all sentence-start and shift flags because
            // the prediction click itself is a neutral, mid-sentence action.
            CompleteWord(toSend);
            _nextWordUpper           = false;
            _atSentenceStart         = false;
            _shiftLatched            = false;
            _sentenceFirstWordDone   = false;
            _lastActionWasPrediction = true; // caller may need to remove trailing space before punctuation
            ShiftLatchChanged?.Invoke(false); // tell UI to release the Shift key

            RefreshPredictions();

            return new WPClickResult(backspaces, toSend, " ");
        }

        /// <summary>
        /// Allows external code (e.g. the Shift key handler in KeyboardForm)
        /// to manually control whether the next word should be capitalised.
        /// This is different from <see cref="OnSentenceStart"/> because a manual
        /// Shift press is NOT treated as a true sentence boundary — it only
        /// signals the intent to type a capitalised word (e.g. a proper noun).
        /// </summary>
        /// <param name="value">
        /// <c>true</c> to request capitalisation of the next word;
        /// <c>false</c> to cancel it.
        /// </param>
        public void SetNextWordUpper(bool value)
        {
            _nextWordUpper   = value;
            _atSentenceStart = false;  // manual Shift is never a true sentence start
            RefreshPredictions();
        }

        // ── Internal helpers ──────────────────────────────────────────

        /// <summary>
        /// Puts the predictor into "sentence start" state:
        /// the next word will be capitalised and the Shift latch is enabled.
        /// Called automatically after sentence-ending punctuation, ENTER,
        /// and on initial startup.
        /// </summary>
        private void SetSentenceStart()
        {
            _nextWordUpper           = true;
            _atSentenceStart         = true;  // this is a real sentence boundary
            _shiftLatched            = true;  // show Shift as active in the UI
            _sentenceFirstWordDone   = false; // no words typed in the new sentence yet
            ShiftLatchChanged?.Invoke(true);  // tell UI to light up the Shift key
        }

        /// <summary>
        /// Records a word as completed and clears the live typing buffer.
        /// The completed word is stored so it can be used for next-word
        /// (bigram) predictions on subsequent keystrokes.
        /// </summary>
        /// <param name="word">The word that was just finished.</param>
        private void CompleteWord(string word)
        {
            _lastCompletedWord = word;
            _wordBuffer        = "";
        }

        /// <summary>
        /// Asks <see cref="WordDatabase"/> for a fresh list of predictions
        /// based on the current state and stores the results in
        /// <see cref="_predictions"/>.  Then fires <see cref="PredictionsChanged"/>
        /// so the UI can redraw the prediction buttons.
        /// </summary>
        /// <param name="shiftActive">
        /// Pass <c>true</c> when the user held Shift while starting a word
        /// mid-sentence.  This biases predictions toward capitalised/proper-noun
        /// words without forcing sentence-start capitalisation on everything.
        /// </param>
        private void RefreshPredictions(bool shiftActive = false)
        {
            // If no word list has been loaded yet there is nothing to predict.
            if (!WordDatabase.IsLoaded) return;

            try
            {
                // Fetch up to _slotCount predictions from the database.
                // The database takes into account:
                //   - _lastCompletedWord: for bigram (word-pair) frequency
                //   - _wordBuffer:        to filter words that start with the typed prefix
                //   - _atSentenceStart:   to auto-capitalise the first word of a sentence
                //   - preferUpperCase:    to show proper nouns when Shift was pressed mid-sentence
                var preds = WordDatabase.GetPredictions(
                    _lastCompletedWord,
                    _wordBuffer,
                    _atSentenceStart,
                    _slotCount,
                    preferUpperCase: shiftActive && !_atSentenceStart);

                // Copy results into the fixed-length array.
                // Slots beyond the returned list are cleared to "" so stale
                // predictions from a previous state do not linger in the UI.
                for (int i = 0; i < _predictions.Length; i++)
                    _predictions[i] = i < preds.Count ? (preds[i] ?? "") : "";
            }
            catch
            {
                // An unexpected failure in the predictor must never crash the keyboard.
                // Clear all slots so the prediction buttons go blank.
                for (int i = 0; i < _predictions.Length; i++)
                    _predictions[i] = "";
            }

            PredictionsChanged?.Invoke();
        }
    }
}

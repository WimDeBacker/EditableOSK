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
    /// Result of <see cref="WordPredictor.OnWPClick"/>: the word to send
    /// and how many backspaces to emit before it.
    /// </summary>
    public class WPClickResult
    {
        public int    Backspaces { get; }   // erase typed prefix
        public string Word       { get; }   // capitalised word to send
        public string Suffix     { get; }   // always " " (space after prediction)

        public WPClickResult(int backspaces, string word, string suffix)
        { Backspaces = backspaces; Word = word; Suffix = suffix; }

        public override string ToString() =>
            new string('\b', Backspaces) + Word + Suffix;
    }

    public class WordPredictor
    {
        // ── State ────────────────────────────────────────────────────
        private string   _wordBuffer            = "";
        private string   _lastCompletedWord     = "";
        private bool     _nextWordUpper           = false;
        private bool     _shiftLatched            = false;
        private bool     _sentenceFirstWordDone   = false;  // true after first word of sentence completed
        private bool     _lastActionWasPrediction = false;

        private readonly string[] _predictions  = new string[10];
        private readonly int      _slotCount;

        // ── Events ────────────────────────────────────────────────────
        /// <summary>Fired when predictions change. UI should refresh WP keys.</summary>
        public event Action PredictionsChanged;

        /// <summary>
        /// Fired when Shift latch state changes.
        /// bool = true means latch Shift; false means unlatch.
        /// </summary>
        public event Action<bool> ShiftLatchChanged;

        /// <summary>Fired when a space or backspace should be injected.</summary>
        public event Action<string> InjectSend;

        // ── Read-only state ───────────────────────────────────────────
        public IReadOnlyList<string> Predictions     => _predictions;
        public bool   NextWordUpper                  => _nextWordUpper;
        public bool   ShiftShouldBeLatched           => _shiftLatched;
        public bool   LastActionWasPrediction        => _lastActionWasPrediction;
        public string WordBuffer                     => _wordBuffer;
        public string LastCompletedWord              => _lastCompletedWord;

        // ── Constructor ───────────────────────────────────────────────
        public WordPredictor(int slotCount = 7)
        {
            _slotCount = Math.Max(1, Math.Min(10, slotCount));
        }

        // ── API ───────────────────────────────────────────────────────

        /// <summary>
        /// Call this when the keyboard first becomes active or a new layout loads.
        /// Treats this as a sentence start.
        /// </summary>
        public void OnSentenceStart()
        {
            SetSentenceStart();
            RefreshPredictions();
        }

        /// <summary>
        /// Call AFTER each key has been sent to the target application.
        /// <paramref name="send"/> is the raw Send value from KeyProps
        /// (e.g. "a", "A", " ", "{ENTER}", ".", ",").
        /// <paramref name="shifted"/> is true if Shift was active.
        /// </summary>
        public void OnKeySent(string send, bool shifted)
        {
            if (string.IsNullOrEmpty(send)) return;

            // ── Single plain character ────────────────────────────────
            if (SendKeysHelper.IsPlainText(send) && send.Length == 1)
            {
                char c = send[0];

                // Punctuation
                if (char.IsPunctuation(c) || char.IsSymbol(c))
                {
                    // The backspace to remove the trailing prediction space is
                    // injected by KeyboardForm BEFORE sending the punctuation,
                    // so we must NOT send it again here. Just clear the flag.
                    _lastActionWasPrediction = false;

                    if (_wordBuffer.Length > 0) CompleteWord(_wordBuffer);
                    _wordBuffer = "";

                    // Add space after word/sentence-ending punctuation
                    bool addSpace = c == '.' || c == '!' || c == '?' ||
                                    c == ',' || c == ';' || c == ':';
                    if (addSpace)
                        InjectSend?.Invoke(" ");

                    if (c == '.' || c == '!' || c == '?')
                        SetSentenceStart();
                    else
                        _nextWordUpper = false;

                    RefreshPredictions();
                    return;
                }

                // Letter or word-continuing character
                if (char.IsLetter(c) || c == '\'' || c == '-')
                {
                    _lastActionWasPrediction = false;
                    // Capture BEFORE any state changes — shifted mid-sentence = proper noun intent
                    bool preferProperNoun = shifted && !_nextWordUpper && _wordBuffer.Length == 0;

                    if (_wordBuffer.Length == 0)
                    {
                        if (_nextWordUpper && _sentenceFirstWordDone)
                        {
                            // Starting the SECOND word after sentence start → mid-sentence
                            _nextWordUpper         = false;
                            _sentenceFirstWordDone = false;
                        }
                        else if (_nextWordUpper)
                        {
                            // First char of sentence-start word: unlatch shift (one-shot)
                            _shiftLatched = false;
                            ShiftLatchChanged?.Invoke(false);
                        }
                        else
                        {
                            // Do NOT set _nextWordUpper here — mid-sentence uppercase
                            // is handled via preferProperNoun in RefreshPredictions,
                            // which shows only proper nouns without capitalising all results.
                            // _nextWordUpper is reserved for genuine sentence-start triggers.
                        }
                    }

                    _wordBuffer += (shifted && char.IsLower(c)) ? char.ToUpper(c) : c;
                    RefreshPredictions(preferProperNoun);
                    return;
                }
            }

            // ── Space / Tab ───────────────────────────────────────────
            if (send == " " || send == "{TAB}")
            {
                _lastActionWasPrediction = false;
                if (_wordBuffer.Length > 0) CompleteWord(_wordBuffer);
                _wordBuffer = "";
                if (_nextWordUpper) _sentenceFirstWordDone = true;
                RefreshPredictions();
                return;
            }

            // ── Enter → new sentence ──────────────────────────────────
            if (send == "{ENTER}")
            {
                _lastActionWasPrediction = false;
                if (_wordBuffer.Length > 0) CompleteWord(_wordBuffer);
                _wordBuffer = "";
                SetSentenceStart();
                RefreshPredictions();
                return;
            }

            // ── Backspace ─────────────────────────────────────────────
            if (send == "{BACKSPACE}")
            {
                _lastActionWasPrediction = false;
                if (_wordBuffer.Length > 0)
                {
                    _wordBuffer = _wordBuffer[..^1];
                    if (_wordBuffer.Length == 0)
                        _lastCompletedWord = "";
                }
                RefreshPredictions();
            }
        }

        /// <summary>
        /// Call when the user clicks WP key at <paramref name="slot"/>.
        /// Returns the result describing what to send, or null if slot is empty.
        /// The caller should send: result.Backspaces × {BACKSPACE}, then
        /// result.Word, then result.Suffix.
        /// </summary>
        public WPClickResult OnWPClick(int slot)
        {
            if (slot < 0 || slot >= _predictions.Length) return null;
            string predicted = _predictions[slot];
            if (string.IsNullOrEmpty(predicted)) return null;

            int backspaces = _wordBuffer.Length;

            // Capitalise if sentence start
            string toSend = _nextWordUpper && predicted.Length > 0
                ? char.ToUpper(predicted[0]) + predicted.Substring(1)
                : predicted;

            // Update state
            CompleteWord(toSend);
            _nextWordUpper           = false;
            _shiftLatched            = false;
            _sentenceFirstWordDone   = false;
            _lastActionWasPrediction = true;
            ShiftLatchChanged?.Invoke(false);
            RefreshPredictions();

            return new WPClickResult(backspaces, toSend, " ");
        }

        /// <summary>
        /// Manually set sentence-start state (e.g. when user presses Shift).
        /// </summary>
        public void SetNextWordUpper(bool value)
        {
            _nextWordUpper = value;
            RefreshPredictions();
        }

        // ── Internal helpers ──────────────────────────────────────────
        private void SetSentenceStart()
        {
            _nextWordUpper           = true;
            _shiftLatched            = true;
            _sentenceFirstWordDone   = false;
            ShiftLatchChanged?.Invoke(true);
        }

        private void CompleteWord(string word)
        {
            _lastCompletedWord = word;
            _wordBuffer        = "";
        }

        private void RefreshPredictions(bool shiftActive = false)
        {
            if (!WordDatabase.IsLoaded) return;

            var preds = WordDatabase.GetPredictions(
                _lastCompletedWord,
                _wordBuffer,
                _nextWordUpper,
                _slotCount,
                preferUpperCase: shiftActive && !_nextWordUpper);

            // GetPredictions already capitalises when upperCase=true
            for (int i = 0; i < _predictions.Length; i++)
                _predictions[i] = i < preds.Count ? (preds[i] ?? "") : "";

            PredictionsChanged?.Invoke();
        }
    }
}

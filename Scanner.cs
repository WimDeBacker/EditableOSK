// Scanner.cs — the state machine of row-column scanning, without any window: it is told about the time (Tick) and the switches (Press,
// Step), and says what is highlighted (Level, Row, Index) and what was selected. KeyboardForm draws the highlight and sends the key.
//
//   One switch (automatic): Press starts; the highlight moves over the rows by itself; Press picks the row, then the highlight moves over the
//   keys of that row; Press selects the key. After a selection the scan starts again at the first row. Without a press the scan stops after
//   Rounds rounds (Rounds also stops a pass over the keys of a row: it goes back to the rows).
//   Two switches: Step moves the highlight, Press picks the row / selects the key. It never moves or stops by itself.
//   Going back (Back): from the keys to the rows, at the rows to the first row.
//   Stepping back (Reverse, the long press that "steps back"): the highlight goes the other way round, right to left (and bottom to top at
//   the rows); from the first item it jumps to the last, through the back step when that is on. Another long press turns it round again.
//   A selection or a new level starts forwards again. With two switches a long press is one step back.

using System;
using System.Collections.Generic;
using System.Linq;

namespace OnScreenKeyboard
{
    internal enum ScanLevel { Idle, Rows, Keys }

    internal sealed class Scanner
    {
        private readonly ScanSettings _s;
        private List<List<GridCell>> _rows = new();
        private long _clock;                       // ms since the scanner exists; advanced by Tick, so tests are deterministic
        private long _lastPress = -1_000_000, _lastStep = -1_000_000;
        private int  _waited;                      // ms on the current row or key
        private bool _firstOfPass;                 // the current row or key is the first of a pass: it gets the extra time

        internal ScanLevel Level  { get; private set; }
        internal int       Row    { get; private set; }
        internal int       Index  { get; private set; }
        /// <summary>Complete passes over the current level without a press.</summary>
        internal int       Passes { get; private set; }

        /// <summary>The highlight changed (also when scanning stopped).</summary>
        internal event Action Changed;
        internal event Action<GridCell> Selected;
        /// <summary>Scanning stopped by itself (after the rounds) or through Stop().</summary>
        internal event Action Stopped;

        internal Scanner(ScanSettings settings) { _s = settings; }

        internal IReadOnlyList<IReadOnlyList<GridCell>> Rows => _rows;

        /// <summary>The cells of the highlighted row (Rows level: all of them; Keys level: all of them, with <see cref="CurrentKey"/> inside).</summary>
        internal IReadOnlyList<GridCell> CurrentRow => Level == ScanLevel.Idle || Row >= _rows.Count ? Array.Empty<GridCell>() : _rows[Row];
        internal GridCell CurrentKey => Level == ScanLevel.Keys && Row < _rows.Count && Index < _rows[Row].Count ? _rows[Row][Index] : null;

        /// <summary>
        /// The select switch is held for the long press and has been held long enough: letting go now goes back. Shown like the back step
        /// (the whole row dashed), so the person sees that the press was long enough.
        /// </summary>
        internal bool BackPreview { get; private set; }

        private bool _holding;                     // the select switch is down: the highlight stays where it is
        private int  _heldFor;                     // ms the select switch has been down
        private bool _backwards;                   // the long press turned the direction round: right to left

        /// <summary>The highlight goes from right to left (and from the last row up).</summary>
        internal bool Backwards => _backwards;

        /// <summary>
        /// The select switch is let go. After a long press that was long enough (<see cref="BackPreview"/>) it restarts the scan or steps
        /// back, as chosen; otherwise it is a normal press.
        /// </summary>
        internal void ReleaseSelect()
        {
            if (BackPreview) { if (_s.LongPressStepsBack) Reverse(); else Back(); }
            else Press();
        }

        /// <summary>
        /// Steps back: the direction turns round and the highlight takes one step the new way (with two switches: one step back, and the
        /// direction stays). Nothing while scanning is paused.
        /// </summary>
        internal void Reverse()
        {
            if (Level == ScanLevel.Idle) return;
            BackPreview = false;
            _freeStep = true;                       // the step the person asked for does not count as a pass without a press
            try
            {
                if (_s.TwoSwitch) { Retreat(); return; }
                _backwards = !_backwards;
                if (_backwards) Retreat(); else Advance();
            }
            finally { _freeStep = false; }
        }

        private bool _freeStep;

        /// <summary>
        /// The select switch goes down (true) or up (false). While it is down the highlight does not move (what was seen at the press is
        /// what is chosen at the release); with the long press on, holding it long enough shows <see cref="BackPreview"/>.
        /// </summary>
        internal void SetHolding(bool down)
        {
            if (_holding == down) return;
            _holding = down; _heldFor = 0;
            if (!down && BackPreview) { BackPreview = false; Changed?.Invoke(); }
        }

        /// <summary>The highlight is on the "back" step after the last key of the row (a press goes back to the rows).</summary>
        internal bool OnBack => Level == ScanLevel.Keys && _s.BackStep && Row < _rows.Count && Index == _rows[Row].Count;

        /// <summary>Items in the row being scanned: its keys, and the back step when that is on.</summary>
        private int ItemsInRow => _rows[Row].Count + (_s.BackStep ? 1 : 0);

        /// <summary>
        /// A way back to the rows exists (the back step, the back switch or a long press): a row of one key is then entered like any other, so
        /// a wrongly chosen row can be left, instead of typing its key at once.
        /// </summary>
        private bool CanGoBack => _s.BackStep || _s.BackSwitch || _s.BackLongPress;

        /// <summary>The rows to scan, from the cells that may be selected: top to bottom, left to right; a row without such a cell is left out.</summary>
        internal static List<List<GridCell>> BuildRows(IEnumerable<GridCell> scannable) =>
            scannable.GroupBy(c => c.Row).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.Col).ToList()).Where(r => r.Count > 0).ToList();

        /// <summary>Replaces the keys (the layout or the word suggestions changed). A running scan stays where it is when it still can.</summary>
        internal void SetRows(List<List<GridCell>> rows)
        {
            _rows = rows ?? new();
            if (Level == ScanLevel.Idle) return;
            if (_rows.Count == 0) { Stop(); return; }
            if (Row >= _rows.Count) Row = 0;
            if (Level == ScanLevel.Keys && Index >= ItemsInRow) Index = 0;
            Changed?.Invoke();
        }

        /// <summary>
        /// Back: from the keys of a row to the rows (the row stays highlighted); at the rows to the first row (quicker than waiting for the
        /// round to come round, on a keyboard with many rows). Nothing while scanning is paused.
        /// </summary>
        internal void Back()
        {
            if (Level == ScanLevel.Idle) return;
            BackPreview = false; _backwards = false;
            if (Level == ScanLevel.Keys) Level = ScanLevel.Rows; else Row = 0;
            Index = 0; Passes = 0;
            BeginItem(true);
            Changed?.Invoke();
        }

        internal void Start()
        {
            if (_rows.Count == 0) return;
            _backwards = false;
            Level = ScanLevel.Rows; Row = 0; Index = 0; Passes = 0;
            BeginItem(true);
            Changed?.Invoke();
        }

        internal void Stop()
        {
            if (Level == ScanLevel.Idle) return;
            BackPreview = false; _backwards = false;
            Level = ScanLevel.Idle; Row = 0; Index = 0; Passes = 0;
            Changed?.Invoke();
            Stopped?.Invoke();
        }

        /// <summary>The select switch.</summary>
        internal void Press()
        {
            if (!Accept(ref _lastPress)) return;
            switch (Level)
            {
                case ScanLevel.Idle: Start(); break;
                case ScanLevel.Rows:
                    if (_rows[Row].Count == 1 && !CanGoBack) { Select(_rows[Row][0]); break; }
                    Level = ScanLevel.Keys; Index = 0; Passes = 0; _backwards = false;
                    BeginItem(true);
                    Changed?.Invoke();
                    break;
                case ScanLevel.Keys:
                    if (OnBack) Back(); else Select(_rows[Row][Index]);
                    break;
            }
        }

        /// <summary>The step switch (two-switch mode only).</summary>
        internal void Step()
        {
            if (!_s.TwoSwitch || !Accept(ref _lastStep)) return;
            switch (Level)
            {
                case ScanLevel.Idle: Start(); break;
                case ScanLevel.Rows: Row   = (Row + 1)   % _rows.Count;      Changed?.Invoke(); break;
                case ScanLevel.Keys: Index = (Index + 1) % ItemsInRow; Changed?.Invoke(); break;
            }
        }

        /// <summary>Time passes (automatic mode moves the highlight).</summary>
        internal void Tick(int ms)
        {
            _clock += ms;
            if (Level == ScanLevel.Idle) return;
            if (_holding)
            {
                if (_s.BackLongPress && !BackPreview)
                {
                    _heldFor += ms;
                    if (_heldFor >= _s.BackHoldMs) { BackPreview = true; Changed?.Invoke(); }
                }
                return;
            }
            if (_s.TwoSwitch) return;
            _waited += ms;
            // More than one step may be due when the time between two calls was long: each step takes its own time.
            while (Level != ScanLevel.Idle && !_s.TwoSwitch && _waited >= _s.ScanMs + (_firstOfPass ? _s.FirstExtraMs : 0))
            {
                int carry = _waited - (_s.ScanMs + (_firstOfPass ? _s.FirstExtraMs : 0));
                if (_backwards) Retreat(); else Advance();
                _waited = carry;
            }
        }

        /// <summary>One step the other way (right to left; at the rows upwards). From the first item it jumps to the last, the back step included.</summary>
        private void Retreat()
        {
            if (Level == ScanLevel.Rows)
            {
                Row--;
                if (Row < 0)
                {
                    Row = _rows.Count - 1; if (!_freeStep) Passes++;
                    if (!_s.TwoSwitch && Passes >= _s.Rounds) { Stop(); return; }
                }
                BeginItem(Row == _rows.Count - 1);            // the first of a pass, going this way, gets the extra time
            }
            else
            {
                Index--;
                if (Index < 0)
                {
                    Index = ItemsInRow - 1; if (!_freeStep) Passes++;
                    if (!_s.TwoSwitch && Passes >= _s.Rounds)  // nothing chosen in this row: back to the rows
                    {
                        _backwards = false;
                        Level = ScanLevel.Rows; Row = 0; Passes = 0;
                        BeginItem(true);
                        Changed?.Invoke();
                        return;
                    }
                }
                BeginItem(Index == ItemsInRow - 1);
            }
            Changed?.Invoke();
        }

        private void Advance()
        {
            if (Level == ScanLevel.Rows)
            {
                Row++;
                if (Row >= _rows.Count)
                {
                    Row = 0; if (!_freeStep) Passes++;
                    if (Passes >= _s.Rounds) { Stop(); return; }
                }
                BeginItem(Row == 0);
            }
            else
            {
                Index++;
                if (Index >= ItemsInRow)
                {
                    Index = 0; if (!_freeStep) Passes++;
                    if (Passes >= _s.Rounds)               // nothing chosen in this row: back to the rows
                    {
                        Level = ScanLevel.Rows; Row = 0; Passes = 0;
                        BeginItem(true);
                        Changed?.Invoke();
                        return;
                    }
                }
                BeginItem(Index == 0);
            }
            Changed?.Invoke();
        }

        private void Select(GridCell cell)
        {
            BackPreview = false; _backwards = false;
            Level = ScanLevel.Rows; Row = 0; Index = 0; Passes = 0;
            BeginItem(true);
            Changed?.Invoke();
            Selected?.Invoke(cell);
        }

        private void BeginItem(bool first) { _waited = 0; _firstOfPass = first; }

        private bool Accept(ref long last)
        {
            if (_clock - last < _s.IgnoreRepeatMs) return false;
            last = _clock;
            return true;
        }
    }
}

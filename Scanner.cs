// Scanner.cs — the state machine of row-column scanning, without any window: it is told about the time (Tick) and the switches (Press,
// Step), and says what is highlighted (Level, Row, Index) and what was selected. KeyboardForm draws the highlight and sends the key.
//
//   One switch (automatic): Press starts; the highlight moves over the rows by itself; Press picks the row, then the highlight moves over the
//   keys of that row; Press selects the key. After a selection the scan starts again at the first row. Without a press the scan stops after
//   Rounds rounds (Rounds also stops a pass over the keys of a row: it goes back to the rows).
//   Two switches: Step moves the highlight, Press picks the row / selects the key. It never moves or stops by itself.

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
            if (Level == ScanLevel.Keys && Index >= _rows[Row].Count) Index = 0;
            Changed?.Invoke();
        }

        internal void Start()
        {
            if (_rows.Count == 0) return;
            Level = ScanLevel.Rows; Row = 0; Index = 0; Passes = 0;
            BeginItem(true);
            Changed?.Invoke();
        }

        internal void Stop()
        {
            if (Level == ScanLevel.Idle) return;
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
                    if (_rows[Row].Count == 1) { Select(_rows[Row][0]); break; }
                    Level = ScanLevel.Keys; Index = 0; Passes = 0;
                    BeginItem(true);
                    Changed?.Invoke();
                    break;
                case ScanLevel.Keys: Select(_rows[Row][Index]); break;
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
                case ScanLevel.Keys: Index = (Index + 1) % _rows[Row].Count; Changed?.Invoke(); break;
            }
        }

        /// <summary>Time passes (automatic mode moves the highlight).</summary>
        internal void Tick(int ms)
        {
            _clock += ms;
            if (Level == ScanLevel.Idle || _s.TwoSwitch) return;
            _waited += ms;
            // More than one step may be due when the time between two calls was long: each step takes its own time.
            while (Level != ScanLevel.Idle && !_s.TwoSwitch && _waited >= _s.ScanMs + (_firstOfPass ? _s.FirstExtraMs : 0))
            {
                int carry = _waited - (_s.ScanMs + (_firstOfPass ? _s.FirstExtraMs : 0));
                Advance();
                _waited = carry;
            }
        }

        private void Advance()
        {
            if (Level == ScanLevel.Rows)
            {
                Row++;
                if (Row >= _rows.Count)
                {
                    Row = 0; Passes++;
                    if (Passes >= _s.Rounds) { Stop(); return; }
                }
                BeginItem(Row == 0);
            }
            else
            {
                Index++;
                if (Index >= _rows[Row].Count)
                {
                    Index = 0; Passes++;
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

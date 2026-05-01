using System;
using System.Collections.Generic;
using System.Drawing;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A key occupying a rectangular region of the grid.
    /// Row/Col are 0-based top-left corner; RowSpan/ColSpan ≥ 1.
    /// </summary>
    public class GridCell
    {
        public int      Row     { get; set; }
        public int      Col     { get; set; }
        public int      RowSpan { get; set; } = 1;
        public int      ColSpan { get; set; } = 1;
        public KeyProps Props   { get; set; }

        public GridCell(int row, int col, KeyProps props,
                        int rowSpan = 1, int colSpan = 1)
        {
            Row = row; Col = col; Props = props;
            RowSpan = Math.Max(1, rowSpan);
            ColSpan = Math.Max(1, colSpan);
        }

        public GridCell Clone() => new GridCell(Row, Col, Props.Clone(), RowSpan, ColSpan);

        /// True if this cell covers grid position (r, c).
        public bool Covers(int r, int c) =>
            r >= Row && r < Row + RowSpan &&
            c >= Col && c < Col + ColSpan;
    }

    /// <summary>
    /// Fixed grid of Rows × Cols cells, each occupied by exactly one GridCell.
    /// Cells may span multiple rows and/or columns (merged cells).
    /// </summary>
    public class GridLayout
    {
        public int            Rows  { get; set; }
        public int            Cols  { get; set; }
        public List<GridCell> Cells { get; set; } = new List<GridCell>();

        public GridLayout(int rows, int cols)
        {
            Rows = Math.Max(1, rows);
            Cols = Math.Max(1, cols);
        }

        // ── Lookup ────────────────────────────────────────────────────
        /// Returns the cell that covers grid position (r, c), or null.
        public GridCell CellAt(int r, int c)
        {
            foreach (var cell in Cells)
                if (cell.Covers(r, c)) return cell;
            return null;
        }

        /// True when every grid position is covered by exactly one cell.
        public bool IsValid()
        {
            if (Rows < 1 || Cols < 1 || Cells.Count == 0) return false;
            // Build occupancy map
            var map = new GridCell[Rows, Cols];
            foreach (var cell in Cells)
            {
                if (cell.Props == null) return false;
                if (cell.Row < 0 || cell.Col < 0) return false;
                if (cell.Row + cell.RowSpan > Rows) return false;
                if (cell.Col + cell.ColSpan > Cols) return false;
                for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
                    for (int c = cell.Col; c < cell.Col + cell.ColSpan; c++)
                    {
                        if (map[r, c] != null) return false; // overlap
                        map[r, c] = cell;
                    }
            }
            // Every position must be occupied
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (map[r, c] == null) return false;
            return true;
        }

        // ── Structural edits ──────────────────────────────────────────
        /// Insert a new row of 1-wide keys above (insertBefore=true) or
        /// below (insertBefore=false) the row containing grid row r.
        public void InsertRow(int atRow, bool before, GlobalSettings g)
        {
            int insertAt = before ? atRow : atRow + 1;
            insertAt = Math.Clamp(insertAt, 0, Rows);

            // Shift all cells at or below insertAt down by 1
            foreach (var cell in Cells)
            {
                if (cell.Row >= insertAt)
                    cell.Row++;
                else if (cell.Row + cell.RowSpan > insertAt)
                    cell.RowSpan++; // cell spans across insertion point — extend
            }
            Rows++;

            // Fill new row with individual cells
            for (int c = 0; c < Cols; c++)
                Cells.Add(new GridCell(insertAt, c, DefaultKey(g)));
        }

        /// Remove the row containing grid row r.
        /// Cells that span across this row shrink by 1.
        /// Cells confined to this row are removed.
        /// Returns false if removing would leave any column empty.
        public bool RemoveRow(int atRow)
        {
            if (Rows <= 1) return false;
            atRow = Math.Clamp(atRow, 0, Rows - 1);

            var toRemove = new List<GridCell>();
            foreach (var cell in Cells)
            {
                if (cell.Row == atRow && cell.RowSpan == 1)
                    toRemove.Add(cell);                 // confined to this row
                else if (cell.Row <= atRow && cell.Row + cell.RowSpan > atRow)
                    cell.RowSpan--;                      // spans across — shrink
                else if (cell.Row > atRow)
                    cell.Row--;                          // below — shift up
            }
            foreach (var c in toRemove) Cells.Remove(c);
            Rows--;
            return true;
        }

        /// Insert a column of 1-tall keys to the left (before=true) or
        /// right (before=false) of column atCol.
        public void InsertCol(int atCol, bool before, GlobalSettings g)
        {
            int insertAt = before ? atCol : atCol + 1;
            insertAt = Math.Clamp(insertAt, 0, Cols);

            foreach (var cell in Cells)
            {
                if (cell.Col >= insertAt)
                    cell.Col++;
                else if (cell.Col + cell.ColSpan > insertAt)
                    cell.ColSpan++;
            }
            Cols++;

            for (int r = 0; r < Rows; r++)
                Cells.Add(new GridCell(r, insertAt, DefaultKey(g)));
        }

        /// Remove the column containing grid col atCol.
        public bool RemoveCol(int atCol)
        {
            if (Cols <= 1) return false;
            atCol = Math.Clamp(atCol, 0, Cols - 1);

            var toRemove = new List<GridCell>();
            foreach (var cell in Cells)
            {
                if (cell.Col == atCol && cell.ColSpan == 1)
                    toRemove.Add(cell);
                else if (cell.Col <= atCol && cell.Col + cell.ColSpan > atCol)
                    cell.ColSpan--;
                else if (cell.Col > atCol)
                    cell.Col--;
            }
            foreach (var c in toRemove) Cells.Remove(c);
            Cols--;
            return true;
        }

        /// Merge cell at (r,c) with its right neighbour if both are 1-row
        /// and vertically aligned. Returns true on success.
        public bool MergeRight(int r, int c)
        {
            var left  = CellAt(r, c);
            if (left == null) return false;
            int nextCol = left.Col + left.ColSpan;
            if (nextCol >= Cols) return false;
            var right = CellAt(r, nextCol);
            if (right == null) return false;
            // Both must have same row and rowspan
            if (left.Row != right.Row || left.RowSpan != right.RowSpan) return false;
            left.ColSpan += right.ColSpan;
            Cells.Remove(right);
            return true;
        }

        /// Merge cell at (r,c) with its bottom neighbour.
        public bool MergeDown(int r, int c)
        {
            var top  = CellAt(r, c);
            if (top == null) return false;
            int nextRow = top.Row + top.RowSpan;
            if (nextRow >= Rows) return false;
            var bot = CellAt(nextRow, c);
            if (bot == null) return false;
            if (top.Col != bot.Col || top.ColSpan != bot.ColSpan) return false;
            top.RowSpan += bot.RowSpan;
            Cells.Remove(bot);
            return true;
        }

        /// Split a merged cell back into individual 1×1 cells.
        public void SplitCell(int r, int c, GlobalSettings g)
        {
            var cell = CellAt(r, c);
            if (cell == null) return;
            if (cell.RowSpan == 1 && cell.ColSpan == 1) return;
            Cells.Remove(cell);
            for (int dr = 0; dr < cell.RowSpan; dr++)
                for (int dc = 0; dc < cell.ColSpan; dc++)
                {
                    var k = (dr == 0 && dc == 0) ? cell.Props.Clone() : DefaultKey(g);
                    Cells.Add(new GridCell(cell.Row + dr, cell.Col + dc, k));
                }
        }

        // ── Helpers ───────────────────────────────────────────────────
        private static KeyProps DefaultKey(GlobalSettings g) => new KeyProps("", "")
        {
            // All style properties use sentinel values (inherit from global).
            // FontSize from global if set, otherwise 0 (auto-size from button dimensions).
            FontSize = g?.FontSize ?? 0,
        };

        public GridLayout Clone()
        {
            var copy = new GridLayout(Rows, Cols);
            foreach (var c in Cells) copy.Cells.Add(c.Clone());
            return copy;
        }
    }
}

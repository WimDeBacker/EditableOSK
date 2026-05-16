using System;
using System.Collections.Generic;
using System.Drawing;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A named style preset that can be shared by a group of keys.
    ///
    /// <para>The keyboard uses a three-level style inheritance chain:
    ///   Global (VisualTheme) → Group (KeyGroup) → Individual key (KeyProps).
    /// A key first looks at its own properties; if a property is left at its
    /// "not set" sentinel value it falls back to the group, then to the global theme.</para>
    ///
    /// <para>Think of it like CSS class inheritance: the group acts as a CSS class
    /// that multiple keys can share, so you can recolour an entire category of keys
    /// (e.g. "function keys") by editing one KeyGroup instead of every key.</para>
    /// </summary>
    public class KeyGroup
    {
        /// <summary>Unique identifier used to link keys to this group in the XML layout file.</summary>
        public string Name            { get; set; } = "";

        /// <summary>Background fill colour for keys in this group.
        /// <c>Color.Empty</c> means "not set — inherit from global theme".</summary>
        public Color  KeyColor        { get; set; } = Color.Empty;

        /// <summary>Text/label colour. <c>Color.Empty</c> = inherit.</summary>
        public Color  FontColor       { get; set; } = Color.Empty;

        /// <summary>Border/outline colour. <c>Color.Empty</c> = inherit.</summary>
        public Color  BorderColor     { get; set; } = Color.Empty;

        /// <summary>Width of the key border in pixels.
        /// <c>-1</c> is the sentinel meaning "not set — inherit from global theme".</summary>
        public int    BorderThickness { get; set; } = -1;   // -1 = inherit global

        /// <summary>Font family name (e.g. "Segoe UI"). Empty string = inherit.</summary>
        public string FontName        { get; set; } = "";

        /// <summary>Font size in points. <c>0</c> = inherit.</summary>
        public int    FontSize        { get; set; } = 0;

        /// <summary>
        /// Creates an independent copy of this group.
        ///
        /// <para>Why clone? When the editor duplicates a layout or lets the user
        /// undo a change, it needs a completely separate object so that editing
        /// the copy does not accidentally change the original.</para>
        /// </summary>
        /// <returns>A new <see cref="KeyGroup"/> with identical property values.</returns>
        public KeyGroup Clone() => new KeyGroup
        {
            Name            = Name,
            KeyColor        = KeyColor,
            FontColor       = FontColor,
            BorderColor     = BorderColor,
            BorderThickness = BorderThickness,
            FontName        = FontName,
            FontSize        = FontSize,
        };
    }


    /// <summary>
    /// Represents one key (or merged multi-key area) placed on the grid.
    ///
    /// <para>The keyboard layout is modelled as a regular grid of rows and columns,
    /// exactly like an HTML table.  A <see cref="GridCell"/> is one table cell.
    /// <see cref="Row"/> and <see cref="Col"/> give its top-left corner (0-based),
    /// and <see cref="RowSpan"/>/<see cref="ColSpan"/> say how many rows/columns it
    /// stretches across — just like the HTML <c>rowspan</c> and <c>colspan</c>
    /// attributes.  Most keys are 1×1, but wide keys like Backspace or Space
    /// use a larger ColSpan.</para>
    ///
    /// <para>The actual key data (label, action, colours …) is stored in
    /// <see cref="Props"/>; GridCell only cares about position.</para>
    /// </summary>
    public class GridCell
    {
        /// <summary>0-based row index of the top-left corner of this cell.</summary>
        public int      Row     { get; set; }

        /// <summary>0-based column index of the top-left corner of this cell.</summary>
        public int      Col     { get; set; }

        /// <summary>Number of rows this cell spans (minimum 1).</summary>
        public int      RowSpan { get; set; } = 1;

        /// <summary>Number of columns this cell spans (minimum 1).</summary>
        public int      ColSpan { get; set; } = 1;

        /// <summary>The key data associated with this grid position (label, action, style …).</summary>
        public KeyProps Props   { get; set; }

        /// <summary>
        /// Creates a new cell at the specified grid position.
        /// </summary>
        /// <param name="row">0-based row index of the top-left corner.</param>
        /// <param name="col">0-based column index of the top-left corner.</param>
        /// <param name="props">Key data for the key sitting at this position.</param>
        /// <param name="rowSpan">How many rows tall the cell is. Clamped to at least 1.</param>
        /// <param name="colSpan">How many columns wide the cell is. Clamped to at least 1.</param>
        public GridCell(int row, int col, KeyProps props,
                        int rowSpan = 1, int colSpan = 1)
        {
            Row = row; Col = col; Props = props;
            // Math.Max ensures nobody can accidentally create a zero-size cell,
            // which would be invisible and impossible to interact with.
            RowSpan = Math.Max(1, rowSpan);
            ColSpan = Math.Max(1, colSpan);
        }

        /// <summary>
        /// Creates an independent deep copy of this cell (including its key properties).
        /// Useful for undo/redo and layout duplication — see <see cref="KeyGroup.Clone"/>.
        /// </summary>
        public GridCell Clone() => new GridCell(Row, Col, Props.Clone(), RowSpan, ColSpan);

        /// <summary>
        /// Returns <c>true</c> if this cell physically covers grid square (r, c).
        ///
        /// <para>Because a cell can span multiple rows and columns, we must check
        /// whether (r, c) falls anywhere inside the rectangle
        /// [Row .. Row+RowSpan) × [Col .. Col+ColSpan),
        /// not just whether it equals the cell's top-left corner.</para>
        /// </summary>
        /// <param name="r">Row to test (0-based).</param>
        /// <param name="c">Column to test (0-based).</param>
        public bool Covers(int r, int c) =>
            r >= Row && r < Row + RowSpan &&
            c >= Col && c < Col + ColSpan;
    }

    /// <summary>
    /// The complete layout of a keyboard: a fixed-size grid of cells that together
    /// describe where every key sits and how it looks.
    ///
    /// <para>Imagine a spreadsheet: <see cref="Rows"/> × <see cref="Cols"/> defines
    /// the grid size, and each <see cref="GridCell"/> in <see cref="Cells"/> occupies
    /// one or more cells of that grid (merged cells for wide keys like Space or Enter).
    /// Together they must cover every grid square exactly once — no gaps, no overlaps.
    /// The <see cref="IsValid"/> method checks this invariant.</para>
    ///
    /// <para>This class also provides the editor operations that let users reshape the
    /// layout: inserting/removing rows and columns, merging adjacent cells, and splitting
    /// merged cells back into individual ones.</para>
    /// </summary>
    public class GridLayout
    {
        /// <summary>Total number of rows in this keyboard layout.</summary>
        public int            Rows   { get; set; }

        /// <summary>Total number of columns in this keyboard layout.</summary>
        public int            Cols   { get; set; }

        /// <summary>
        /// All key cells that make up this layout.
        /// Every grid square must be covered by exactly one cell in this list.
        /// </summary>
        public List<GridCell> Cells  { get; set; } = new List<GridCell>();

        /// <summary>
        /// Named style groups defined for this layout.
        /// Keys reference a group by name via their <see cref="KeyProps"/> to inherit
        /// the group's colours and font without having to repeat those values on every key.
        /// </summary>
        public List<KeyGroup> Groups { get; set; } = new List<KeyGroup>();

        /// <summary>
        /// Creates an empty grid of the given size.
        /// </summary>
        /// <param name="rows">Number of rows. Clamped to at least 1.</param>
        /// <param name="cols">Number of columns. Clamped to at least 1.</param>
        public GridLayout(int rows, int cols)
        {
            // Math.Max guards against callers passing 0 or negative values,
            // which would produce an unusable grid.
            Rows = Math.Max(1, rows);
            Cols = Math.Max(1, cols);
        }

        // ── Lookup ────────────────────────────────────────────────────

        /// <summary>
        /// Finds the cell that occupies grid position (r, c).
        ///
        /// <para>Because cells can span multiple squares, we cannot simply index
        /// into an array — we have to ask each cell whether it <em>covers</em>
        /// that position.  Returns <c>null</c> if no cell covers (r, c),
        /// which indicates a layout integrity problem.</para>
        /// </summary>
        /// <param name="r">Row to look up (0-based).</param>
        /// <param name="c">Column to look up (0-based).</param>
        /// <returns>The <see cref="GridCell"/> that covers (r, c), or <c>null</c>.</returns>
        public GridCell CellAt(int r, int c)
        {
            foreach (var cell in Cells)
                if (cell.Covers(r, c)) return cell;
            return null;
        }

        /// <summary>
        /// Validates that the layout is internally consistent:
        /// every grid square is covered by exactly one cell, with no overlaps and no gaps.
        ///
        /// <para>This is called before saving a layout to catch mistakes made during
        /// editing.  The algorithm builds a 2-D "occupancy map" array and stamps each
        /// cell's footprint into it — if a square is already stamped when we try to
        /// stamp it again, we have an overlap; if any square is still empty after
        /// processing all cells, we have a gap.</para>
        /// </summary>
        /// <returns><c>true</c> if the layout is valid and safe to use.</returns>
        public bool IsValid()
        {
            if (Rows < 1 || Cols < 1 || Cells.Count == 0) return false;

            // Allocate a 2-D array the same size as the grid.
            // Each slot will hold the cell that covers it, or null if uncovered.
            var map = new GridCell[Rows, Cols];

            foreach (var cell in Cells)
            {
                if (cell.Props == null) return false;
                if (cell.Row < 0 || cell.Col < 0) return false;

                // Check the cell doesn't extend beyond the grid boundaries.
                if (cell.Row + cell.RowSpan > Rows) return false;
                if (cell.Col + cell.ColSpan > Cols) return false;

                // Stamp every grid square the cell covers.
                for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
                    for (int c = cell.Col; c < cell.Col + cell.ColSpan; c++)
                    {
                        if (map[r, c] != null) return false; // overlap — already stamped
                        map[r, c] = cell;
                    }
            }

            // Second pass: every square must have been stamped.
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (map[r, c] == null) return false;   // gap — no cell covers this square

            return true;
        }

        // ── Structural edits ──────────────────────────────────────────

        /// <summary>
        /// Inserts a new row of empty keys into the grid.
        ///
        /// <para>The new row is filled with plain 1×1 default keys.
        /// Existing cells are adjusted in one of three ways:
        /// <list type="bullet">
        ///   <item>Cells entirely above the insertion point — unchanged.</item>
        ///   <item>Cells entirely at or below the insertion point — their
        ///         <see cref="GridCell.Row"/> is incremented by 1 (shifted down).</item>
        ///   <item>Cells that <em>span across</em> the insertion point
        ///         (top edge above, bottom edge below) — their
        ///         <see cref="GridCell.RowSpan"/> grows by 1 so they continue
        ///         to cover the same visual area plus the new row.</item>
        /// </list></para>
        /// </summary>
        /// <param name="atRow">The row index used as reference for the insertion.</param>
        /// <param name="before">
        ///   <c>true</c> = insert above <paramref name="atRow"/>;
        ///   <c>false</c> = insert below it.
        /// </param>
        /// <param name="g">Theme used to initialise the style of the new default keys.</param>
        public void InsertRow(int atRow, bool before, VisualTheme g)
        {
            // Calculate the actual grid row index where the new row will be inserted.
            int insertAt = before ? atRow : atRow + 1;
            insertAt = Math.Clamp(insertAt, 0, Rows);

            // Walk every existing cell and decide how to update it.
            foreach (var cell in Cells)
            {
                if (cell.Row >= insertAt)
                    cell.Row++;                              // shift cells below the new row down
                else if (cell.Row + cell.RowSpan > insertAt)
                    cell.RowSpan++;                          // cell spans across insertion point — extend it
                // Cells entirely above insertAt need no change.
            }
            Rows++;

            // Fill the new row with individual 1×1 default keys, one per column.
            for (int c = 0; c < Cols; c++)
                Cells.Add(new GridCell(insertAt, c, DefaultKey(g)));
        }

        /// <summary>
        /// Removes a row from the grid.
        ///
        /// <para>Cells that exist entirely within the removed row are deleted.
        /// Cells that span across it (starting in an earlier row and ending in a
        /// later row) simply shrink by 1 row — their visual footprint stays intact
        /// except for the deleted row.  Cells below the removed row are shifted up.</para>
        /// </summary>
        /// <param name="atRow">0-based index of the row to remove.</param>
        /// <returns>
        ///   <c>true</c> if the row was removed successfully;
        ///   <c>false</c> if the grid has only one row (cannot remove the last row).
        /// </returns>
        public bool RemoveRow(int atRow)
        {
            if (Rows <= 1) return false;  // refuse to create a zero-row grid
            atRow = Math.Clamp(atRow, 0, Rows - 1);

            var toRemove = new List<GridCell>();
            foreach (var cell in Cells)
            {
                if (cell.Row == atRow && cell.RowSpan == 1)
                    toRemove.Add(cell);                 // confined to this row — schedule for deletion
                else if (cell.Row <= atRow && cell.Row + cell.RowSpan > atRow)
                    cell.RowSpan--;                      // spans across this row — shrink by 1
                else if (cell.Row > atRow)
                    cell.Row--;                          // entirely below — shift up by 1
            }

            // Remove the collected cells outside the loop to avoid modifying
            // the list while iterating over it (which would throw an exception).
            foreach (var c in toRemove) Cells.Remove(c);
            Rows--;
            return true;
        }

        /// <summary>
        /// Inserts a new column of empty keys into the grid.
        ///
        /// <para>The logic mirrors <see cref="InsertRow"/>: cells to the right of the
        /// insertion point are shifted right; cells that span across it grow wider;
        /// cells entirely to the left are untouched.</para>
        /// </summary>
        /// <param name="atCol">The column index used as reference.</param>
        /// <param name="before">
        ///   <c>true</c> = insert to the left of <paramref name="atCol"/>;
        ///   <c>false</c> = insert to the right.
        /// </param>
        /// <param name="g">Theme for the default keys that fill the new column.</param>
        public void InsertCol(int atCol, bool before, VisualTheme g)
        {
            int insertAt = before ? atCol : atCol + 1;
            insertAt = Math.Clamp(insertAt, 0, Cols);

            foreach (var cell in Cells)
            {
                if (cell.Col >= insertAt)
                    cell.Col++;                   // shift cells to the right of the new column
                else if (cell.Col + cell.ColSpan > insertAt)
                    cell.ColSpan++;               // spans across insertion point — widen it
            }
            Cols++;

            // Fill the new column with individual 1×1 default keys, one per row.
            for (int r = 0; r < Rows; r++)
                Cells.Add(new GridCell(r, insertAt, DefaultKey(g)));
        }

        /// <summary>
        /// Removes a column from the grid.
        ///
        /// <para>Mirrors <see cref="RemoveRow"/> for the column axis.</para>
        /// </summary>
        /// <param name="atCol">0-based index of the column to remove.</param>
        /// <returns>
        ///   <c>false</c> if the grid only has one column and cannot be shrunk further.
        /// </returns>
        public bool RemoveCol(int atCol)
        {
            if (Cols <= 1) return false;
            atCol = Math.Clamp(atCol, 0, Cols - 1);

            var toRemove = new List<GridCell>();
            foreach (var cell in Cells)
            {
                if (cell.Col == atCol && cell.ColSpan == 1)
                    toRemove.Add(cell);           // confined to this column — delete it
                else if (cell.Col <= atCol && cell.Col + cell.ColSpan > atCol)
                    cell.ColSpan--;               // spans across — shrink
                else if (cell.Col > atCol)
                    cell.Col--;                   // to the right — shift left
            }
            foreach (var c in toRemove) Cells.Remove(c);
            Cols--;
            return true;
        }

        /// <summary>
        /// Merges the cell at (r, c) with the cell immediately to its right,
        /// creating a wider key (like a Backspace key spanning two columns).
        ///
        /// <para>The merge is only allowed when both cells share the same starting row
        /// and the same RowSpan — in other words they are vertically "aligned" so that
        /// combining them produces a clean rectangle with no ragged edges.</para>
        ///
        /// <para>After the merge, the left cell's <see cref="GridCell.ColSpan"/> grows
        /// to absorb the right cell, and the right cell is removed from the list.</para>
        /// </summary>
        /// <param name="r">Row of the cell to start from.</param>
        /// <param name="c">Column of the cell to start from.</param>
        /// <returns><c>true</c> if the merge succeeded; <c>false</c> if conditions were not met.</returns>
        public bool MergeRight(int r, int c)
        {
            var left  = CellAt(r, c);
            if (left == null) return false;

            // The right neighbour starts immediately after the left cell ends.
            int nextCol = left.Col + left.ColSpan;
            if (nextCol >= Cols) return false;   // left cell is already at the rightmost column

            var right = CellAt(r, nextCol);
            if (right == null) return false;

            // Both cells must start on the same row and have the same height.
            // Otherwise the combined shape would not be a rectangle.
            if (left.Row != right.Row || left.RowSpan != right.RowSpan) return false;

            left.ColSpan += right.ColSpan;  // absorb the right cell's width
            Cells.Remove(right);            // the right cell no longer exists as a separate object
            return true;
        }

        /// <summary>
        /// Merges the cell at (r, c) with the cell immediately below it,
        /// creating a taller key (like a tall Enter key spanning two rows).
        ///
        /// <para>The same alignment constraint applies: both cells must share the same
        /// starting column and ColSpan so the result is a clean rectangle.</para>
        /// </summary>
        /// <param name="r">Row of the cell to start from.</param>
        /// <param name="c">Column of the cell to start from.</param>
        /// <returns><c>true</c> if the merge succeeded.</returns>
        public bool MergeDown(int r, int c)
        {
            var top  = CellAt(r, c);
            if (top == null) return false;

            int nextRow = top.Row + top.RowSpan;
            if (nextRow >= Rows) return false;   // top cell is already in the last row

            var bot = CellAt(nextRow, c);
            if (bot == null) return false;

            // Both cells must start in the same column and have the same width.
            if (top.Col != bot.Col || top.ColSpan != bot.ColSpan) return false;

            top.RowSpan += bot.RowSpan;  // absorb the bottom cell's height
            Cells.Remove(bot);
            return true;
        }

        /// <summary>
        /// Splits a previously merged cell back into individual 1×1 cells.
        ///
        /// <para>The top-left sub-cell keeps the original key's
        /// <see cref="KeyProps"/> (so the label is preserved); all other
        /// sub-cells get fresh default key data.</para>
        /// </summary>
        /// <param name="r">Row coordinate anywhere inside the merged cell.</param>
        /// <param name="c">Column coordinate anywhere inside the merged cell.</param>
        /// <param name="g">Theme used to create default key data for the new sub-cells.</param>
        public void SplitCell(int r, int c, VisualTheme g)
        {
            var cell = CellAt(r, c);
            if (cell == null) return;
            if (cell.RowSpan == 1 && cell.ColSpan == 1) return;  // already a 1×1 cell — nothing to do

            Cells.Remove(cell);  // remove the merged cell before adding the split pieces

            // Re-fill the vacated area with 1×1 cells.
            for (int dr = 0; dr < cell.RowSpan; dr++)
                for (int dc = 0; dc < cell.ColSpan; dc++)
                {
                    // Preserve the original key's properties for the top-left cell so
                    // the user doesn't lose the label they had typed; default elsewhere.
                    var k = (dr == 0 && dc == 0) ? cell.Props.Clone() : DefaultKey(g);
                    Cells.Add(new GridCell(cell.Row + dr, cell.Col + dc, k));
                }
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Builds an empty <see cref="KeyProps"/> suitable for a newly created key.
        ///
        /// <para>All style fields are left at their "inherit" sentinel values so the
        /// new key picks up its appearance from the global theme.  The only field
        /// populated is <see cref="KeyProps.FontSize"/>: we copy the global font size
        /// if one is set, otherwise leave it at 0 which signals "auto-size based on
        /// the button's pixel dimensions at runtime".</para>
        /// </summary>
        /// <param name="g">
        ///   The current <see cref="VisualTheme"/>.  May be <c>null</c> (e.g. during
        ///   unit tests), in which case font size defaults to 0.
        /// </param>
        private static KeyProps DefaultKey(VisualTheme g) => new KeyProps("", "")
        {
            // All style properties use sentinel values (inherit from global).
            // FontSize from global if set, otherwise 0 (auto-size from button dimensions).
            FontSize = g?.FontSize ?? 0,
        };

        /// <summary>
        /// Creates a deep copy of this entire layout, including all cells and groups.
        ///
        /// <para>Used by the editor's undo stack and by "save as new layout": cloning
        /// guarantees that editing the copy cannot affect the original object.</para>
        /// </summary>
        public GridLayout Clone()
        {
            var copy = new GridLayout(Rows, Cols);
            foreach (var c in Cells)  copy.Cells.Add(c.Clone());
            foreach (var g in Groups) copy.Groups.Add(g.Clone());
            return copy;
        }
    }
}

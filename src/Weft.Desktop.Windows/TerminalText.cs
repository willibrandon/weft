using System.Text;
using Weft.Client;

namespace Weft.Desktop.Windows;

/// <summary>
/// Maps the visible cell grid to UTF-16 text ranges for accessibility, including wide and combining graphemes.
/// </summary>
internal sealed class TerminalText
{
    /// <summary>
    /// Builds the text of a pane: trailing blanks are trimmed and rows are separated by newlines.
    /// </summary>
    /// <param name="block">The pane.</param>
    internal TerminalText(DesktopBlockFrame block)
    {
        ArgumentNullException.ThrowIfNull(block);
        Block = block;
        var value = new StringBuilder(block.Cells.Count);
        var cells = new (int Start, int Length)[block.Cells.Count];
        var rows = new (int Start, int Length)[block.Height];
        for (int row = 0; row < block.Height; row++)
        {
            int start = row * block.Width;
            int used = 0;
            for (int column = block.Width - 1; column >= 0; column--)
            {
                string text = block.Cells[start + column].Text;
                if (text.Length != 0 && text != " ")
                {
                    used = column + 1;
                    break;
                }
            }

            if (row == block.CursorY)
            {
                used = Math.Max(used, block.CursorX);
            }

            int beginning = value.Length;
            for (int column = 0; column < block.Width; column++)
            {
                string text = column < used ? block.Cells[start + column].Text : string.Empty;
                if (column > 0 && block.Cells[start + column].Text.Length == 0)
                {
                    // A wide character's continuation shares the range of the cell that draws it.
                    cells[start + column] = cells[start + column - 1];
                    continue;
                }

                cells[start + column] = (value.Length, text.Length);
                _ = value.Append(text);
            }

            rows[row] = (beginning, value.Length - beginning);
            if (row + 1 < block.Height)
            {
                _ = value.Append('\n');
            }
        }

        Value = value.ToString();
        Cells = cells;
        Rows = rows;
        int cursor = Math.Clamp((block.CursorY * block.Width) + block.CursorX, 0, Math.Max(0, cells.Length - 1));
        Cursor = cells.Length == 0 ? 0 : cells[cursor].Start;
    }

    /// <summary>
    /// Gets the pane the text describes.
    /// </summary>
    internal DesktopBlockFrame Block { get; }

    /// <summary>
    /// Gets the exposed text.
    /// </summary>
    internal string Value { get; }

    /// <summary>
    /// Gets each cell's UTF-16 range.
    /// </summary>
    internal IReadOnlyList<(int Start, int Length)> Cells { get; }

    /// <summary>
    /// Gets each row's UTF-16 range, excluding its newline.
    /// </summary>
    internal IReadOnlyList<(int Start, int Length)> Rows { get; }

    /// <summary>
    /// Gets the caret's UTF-16 offset.
    /// </summary>
    internal int Cursor { get; }

    /// <summary>
    /// Gets the row containing a text offset.
    /// </summary>
    /// <param name="offset">The UTF-16 offset.</param>
    /// <returns>The row index.</returns>
    internal int Row(int offset)
    {
        for (int row = Rows.Count - 1; row >= 0; row--)
        {
            if (Rows[row].Start <= offset)
            {
                return row;
            }
        }

        return 0;
    }

    /// <summary>
    /// Gets the first cell whose range reaches an offset.
    /// </summary>
    /// <param name="offset">The UTF-16 offset.</param>
    /// <returns>The cell index.</returns>
    internal int CellAt(int offset)
    {
        for (int index = 0; index < Cells.Count; index++)
        {
            if (Cells[index].Start + Math.Max(1, Cells[index].Length) > offset)
            {
                return index;
            }
        }

        return Math.Max(0, Cells.Count - 1);
    }

    /// <summary>
    /// Gets the last cell that draws the character at an offset, which is a wide character's continuation.
    /// </summary>
    /// <param name="offset">The UTF-16 offset.</param>
    /// <returns>The cell index.</returns>
    internal int LastCellAt(int offset)
    {
        int index = CellAt(offset);
        while (index + 1 < Cells.Count && (index + 1) % Block.Width != 0 && Block.Cells[index + 1].Text.Length == 0)
        {
            index++;
        }

        return index;
    }
}

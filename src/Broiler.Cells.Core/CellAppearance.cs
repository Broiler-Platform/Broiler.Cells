using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Cells.Core;

public enum CellBorderPreset
{
    None,
    Outer,
    Inner,
    All
}

public static class CellAppearance
{
    public static void ApplyBorders(SheetModel sheet, IReadOnlySet<CellPosition> selection,
        CellBorderPreset preset, BColor color, double width = 1, TableBorderStyle style = TableBorderStyle.Solid)
    {
        var border = new TableBorder(color, width, style);
        foreach (var cell in selection)
        {
            if (sheet.IsCoveredCell(cell.Row, cell.Column))
                continue;

            var content = sheet.GetContent(cell.Row, cell.Column);
            int endRow = cell.Row + content.RowSpan;
            int endColumn = cell.Column + content.ColumnSpan;

            bool HasUnselectedNeighbor(int row, int column, int rows, int columns)
            {
                for (int r = row; r < row + rows; r++)
                    for (int c = column; c < column + columns; c++)
                        if (!selection.Contains(new CellPosition(r, c)))
                            return true;
                return false;
            }

            TableBorder Edge(bool outer, bool ownsInnerEdge) => preset switch
            {
                CellBorderPreset.Outer when outer => border,
                CellBorderPreset.Inner when !outer && ownsInnerEdge => border,
                CellBorderPreset.All when outer || ownsInnerEdge => border,
                _ => default
            };

            // The upper/left cell owns interior edges, so the grid draws each line once.
            var borders = new CellBorders(
                Edge(HasUnselectedNeighbor(cell.Row, cell.Column - 1, content.RowSpan, 1), false),
                Edge(HasUnselectedNeighbor(cell.Row - 1, cell.Column, 1, content.ColumnSpan), false),
                Edge(HasUnselectedNeighbor(cell.Row, endColumn, content.RowSpan, 1), true),
                Edge(HasUnselectedNeighbor(endRow, cell.Column, 1, content.ColumnSpan), true));
            if (preset != CellBorderPreset.None)
                borders = borders with { DiagonalDown = content.Borders.DiagonalDown, DiagonalUp = content.Borders.DiagonalUp };
            sheet.SetCellBorders(cell.Row, cell.Column, borders);
        }
    }
}

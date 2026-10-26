using Broiler.Cells.Core;

namespace Broiler.Cells.Windows;

/// <summary>
/// Spreadsheet view for the Windows head, inheriting the shared platform-neutral
/// <see cref="Broiler.Cells.UI.CellsView"/> comprising Menu, Toolbar, separate Cell-Edit-Line,
/// Cell grid, and Status bar.
/// </summary>
public sealed class CellsView : Broiler.Cells.UI.CellsView
{
    public CellsView(SheetModel sheet, bool compactMode = false) : base(sheet, compactMode)
    {
    }
}

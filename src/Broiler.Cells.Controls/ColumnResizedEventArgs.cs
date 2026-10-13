namespace Broiler.Cells.Controls;

/// <summary>Provides data for column resize events.</summary>
public sealed class ColumnResizedEventArgs : EventArgs
{
    public ColumnResizedEventArgs(int column, double oldWidth, double newWidth)
    {
        Column = column;
        OldWidth = oldWidth;
        NewWidth = newWidth;
    }

    public int Column { get; }
    public double OldWidth { get; }
    public double NewWidth { get; }
}

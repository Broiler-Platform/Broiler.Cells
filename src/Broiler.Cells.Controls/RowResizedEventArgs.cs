namespace Broiler.Cells.Controls;

/// <summary>Provides data for row resize events.</summary>
public sealed class RowResizedEventArgs : EventArgs
{
    public RowResizedEventArgs(int row, double oldHeight, double newHeight)
    {
        Row = row;
        OldHeight = oldHeight;
        NewHeight = newHeight;
    }

    public int Row { get; }
    public double OldHeight { get; }
    public double NewHeight { get; }
}

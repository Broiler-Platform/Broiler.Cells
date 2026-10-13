using Broiler.Cells.Core;

namespace Broiler.Cells.Controls;

/// <summary>Provides data for the cell committed event.</summary>
public sealed class CellCommittedEventArgs(CellPosition position, string oldValue, string newValue) : EventArgs
{
    public CellPosition Position { get; } = position;
    public int Row => Position.Row;
    public int Column => Position.Column;
    public string OldValue { get; } = oldValue;
    public string NewValue { get; } = newValue;
}

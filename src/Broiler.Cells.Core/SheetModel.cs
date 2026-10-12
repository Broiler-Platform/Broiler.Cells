namespace Broiler.Cells.Core;

/// <summary>An immutable, in-memory sheet for the Version 1 display control.</summary>
public sealed class SheetModel
{
    public const int MaximumRows = 10;
    public const int MaximumColumns = 10;

    private readonly string[,] _values;

    public SheetModel(int rows = MaximumRows, int columns = MaximumColumns,
        Func<int, int, string?>? initialValue = null)
    {
        // Validate both dimensions before allocating or invoking caller code.
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, MaximumRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, MaximumColumns);

        RowCount = rows;
        ColumnCount = columns;
        _values = new string[rows, columns];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                _values[row, column] = initialValue?.Invoke(row, column) ?? string.Empty;
    }

    public int RowCount { get; }
    public int ColumnCount { get; }

    /// <summary>Gets a value using zero-based row and column indices.</summary>
    public string this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ColumnCount);
            return _values[row, column];
        }
    }
}

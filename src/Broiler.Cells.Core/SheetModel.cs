namespace Broiler.Cells.Core;

/// <summary>An in-memory sheet supporting display and editing.</summary>
public sealed class SheetModel
{
    public const int DefaultRows = 100;
    public const int DefaultColumns = 100;
    public const int MaximumRows = 100;
    public const int MaximumColumns = 100;
    public const int Version1MaximumRows = 10;
    public const int Version1MaximumColumns = 10;

    private readonly string[,] _values;

    public SheetModel(int rows = DefaultRows, int columns = DefaultColumns, Func<int, int, string?>? initialValue = null, int maxRows = MaximumRows, int maxColumns = MaximumColumns)
    {
        // Validate limits before allocating or invoking caller code.
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRows, MaximumRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColumns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColumns, MaximumColumns);

        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, maxRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, maxColumns);

        MaxRows = maxRows;
        MaxColumns = maxColumns;
        RowCount = rows;
        ColumnCount = columns;
        _values = new string[rows, columns];

        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                _values[row, column] = initialValue?.Invoke(row, column) ?? string.Empty;
    }

    public event EventHandler? Changed;

    public int MaxRows { get; }
    public int MaxColumns { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }

    /// <summary>Gets or sets a value using zero-based row and column indices.</summary>
    public string this[int row, int column]
    {
        get
        {
            ValidateIndices(row, column);
            return _values[row, column];
        }
        set
        {
            ValidateIndices(row, column);
            string next = value ?? string.Empty;
            if (_values[row, column] == next) return;
            _values[row, column] = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets or sets a value using a cell position.</summary>
    public string this[CellPosition position]
    {
        get => this[position.Row, position.Column];
        set => this[position.Row, position.Column] = value;
    }

    private void ValidateIndices(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ColumnCount);
    }
}

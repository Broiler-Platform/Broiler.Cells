using Broiler.Documents.Model;

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
    private readonly Dictionary<CellPosition, CellContent> _content = [];
    public WorkbookModel? Workbook { get; internal set; }
    private Broiler.Documents.Model.Spreadsheets.SpreadsheetWorksheet? _importedWorksheet;
    internal Broiler.Documents.Model.Spreadsheets.SpreadsheetWorksheet? ImportedWorksheet
    {
        get => _importedWorksheet;
        set { _importedWorksheet = value; _content.Clear(); _calculationDirty = true; }
    }
    private bool _calculationDirty = true;
    private IReadOnlyList<FormulaIssue> _formulaIssues = [];
    private IReadOnlyDictionary<CellAddress, IReadOnlySet<CellAddress>> _dependencies = new Dictionary<CellAddress, IReadOnlySet<CellAddress>>();
    public IReadOnlyList<FormulaIssue> FormulaIssues { get { EnsureCalculated(); return _formulaIssues; } }
    public IReadOnlyDictionary<CellAddress, IReadOnlySet<CellAddress>> FormulaDependencies { get { EnsureCalculated(); return _dependencies; } }

    public string ImportNotice { get; internal set; } = string.Empty;

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
            EnsureCalculated();
            return _values[row, column];
        }
        set
        {
            ValidateIndices(row, column);
            string next = value ?? string.Empty;
            if (_values[row, column] == next) return;
            _values[row, column] = next;
            _content[new CellPosition(row, column)] = new CellContent(CellValue.FromText(next), next);
            _calculationDirty = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets or sets a value using a cell position.</summary>
    public string this[CellPosition position]
    {
        get => this[position.Row, position.Column];
        set => this[position.Row, position.Column] = value;
    }

    public CellContent GetContent(int row, int column)
    {
        EnsureCalculated();
        return GetStoredContent(row, column);
    }

    private CellContent GetStoredContent(int row, int column)
    {
        ValidateIndices(row, column);
        var position = new CellPosition(row, column);
        if (_content.TryGetValue(position, out var content)) return content;
        var original = ImportedWorksheet?[row, column];
        if (original is not null)
            return new CellContent(original.Value, original.Formula ?? original.Value.Format(), original.Formula, original.NumberFormat, original.Formula is null ? null : original.CachedText);
        string text = _values[row, column];
        return new CellContent(CellValue.FromText(text), text);
    }

    public string GetEditText(int row, int column) => GetContent(row, column).Input;
    public bool HasEditedContent(int row, int column) => _content.ContainsKey(new CellPosition(row, column));

    public void SetInput(int row, int column, string input, CellEntryMode mode = CellEntryMode.Auto, bool force = false)
    {
        var before = GetContent(row, column);
        if (!force && mode == CellEntryMode.Auto && input == before.Input) return;
        var next = CellContent.Parse(input, mode, before.NumberFormat);
        string display = next.Display;
        _content[new CellPosition(row, column)] = next;
        _values[row, column] = display;
        _calculationDirty = true;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetNumberFormat(int row, int column, string? format)
    {
        var before = GetContent(row, column);
        var next = before with { NumberFormat = string.IsNullOrWhiteSpace(format) ? null : format.Trim(), CachedText = before.Formula is not null && !before.Value.IsEmpty ? CellNumberFormatter.Format(before.Value, format, System.Globalization.CultureInfo.InvariantCulture).Text : before.CachedText };
        string display = next.Display; // Validate/format before mutating the model.
        _content[new CellPosition(row, column)] = next;
        _values[row, column] = display;
        _calculationDirty = true;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Recalculate()
    {
        _calculationDirty = true;
        EnsureCalculated();
    }

    private void EnsureCalculated()
    {
        if (!_calculationDirty) return;
        _calculationDirty = false;
        bool hasFormula = _content.Values.Any(c => c.Formula is not null)
            || (ImportedWorksheet?.Rows.Any(r => r.Cells.Any(c => c.Formula is not null)) ?? false);
        if (!hasFormula) { _formulaIssues = []; _dependencies = new Dictionary<CellAddress, IReadOnlySet<CellAddress>>(); return; }
        try
        {
            var result = SheetCalculation.Evaluate(this, GetStoredContent);
            _formulaIssues = result.Issues;
            _dependencies = result.Evaluation.Dependencies;
            for (int row = 0; row < RowCount; row++)
            for (int column = 0; column < ColumnCount; column++)
            {
                var input = GetStoredContent(row, column);
                if (input.Formula is null) continue;
                var value = result.Evaluation.GetValue(row, column);
                var cached = CellNumberFormatter.Format(value, input.NumberFormat, System.Globalization.CultureInfo.InvariantCulture).Text;
                _content[new CellPosition(row, column)] = input with { Value = value, CachedText = cached };
                _values[row, column] = cached;
            }
        }
        catch { _calculationDirty = true; throw; }
    }

    private void ValidateIndices(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ColumnCount);
    }
}

using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics.Color;
namespace Broiler.Cells.Core;

/// <summary>An in-memory sheet supporting display and editing.</summary>
public sealed class SheetModel
{
    public const int DefaultRows = 100;
    public const int DefaultColumns = 100;
    public const int MaximumRows = 1_048_576;
    public const int MaximumColumns = 16_384;
    public const int Version1MaximumRows = 10;
    public const int Version1MaximumColumns = 10;
    private readonly Dictionary<CellPosition, string> _values = [];
    private readonly Dictionary<CellPosition, CellContent> _content = [];
    private readonly Dictionary<int, double> _customColumnWidths = [];
    private readonly Dictionary<int, double> _customRowHeights = [];
    private readonly List<CellMergeRange> _merges = [];
    public IReadOnlyList<CellMergeRange> Merges => _merges;
    private readonly List<ConditionalFormat> _conditionalFormats = [];
    public IReadOnlyList<ConditionalFormat> ConditionalFormats => _conditionalFormats;
    public event Action? ConditionalFormatsChanged;
    public void AddConditionalFormat(ConditionalFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        _conditionalFormats.Add(format);
        ConditionalFormatsChanged?.Invoke();
    }

    public bool RemoveConditionalFormat(ConditionalFormat format)
    {
        bool removed = _conditionalFormats.Remove(format);
        if (removed)
            ConditionalFormatsChanged?.Invoke();
        return removed;
    }

    public void ClearConditionalFormats()
    {
        if (_conditionalFormats.Count > 0)
        {
            _conditionalFormats.Clear();
            ConditionalFormatsChanged?.Invoke();
        }
    }

    private readonly List<DataValidation> _dataValidations = [];
    public IReadOnlyList<DataValidation> DataValidations => _dataValidations;
    public event Action? DataValidationsChanged;
    public void AddDataValidation(DataValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        _dataValidations.Add(validation);
        DataValidationsChanged?.Invoke();
    }

    public bool RemoveDataValidation(DataValidation validation)
    {
        bool removed = _dataValidations.Remove(validation);
        if (removed)
            DataValidationsChanged?.Invoke();
        return removed;
    }

    public void ClearDataValidations()
    {
        if (_dataValidations.Count > 0)
        {
            _dataValidations.Clear();
            DataValidationsChanged?.Invoke();
        }
    }

    public void ClearDataValidations(CellRangeAddress range)
    {
        var toRemove = new List<DataValidation>();
        foreach (var dv in _dataValidations)
        {
            foreach (var r in dv.Ranges)
            {
                if (r.StartRow <= range.EndRow && r.EndRow >= range.StartRow &&
                    r.StartCol <= range.EndCol && r.EndCol >= range.StartCol)
                {
                    toRemove.Add(dv);
                    break;
                }
            }
        }
        if (toRemove.Count > 0)
        {
            foreach (var dv in toRemove)
                _dataValidations.Remove(dv);
            DataValidationsChanged?.Invoke();
        }
    }

    public DataValidation? GetDataValidation(int row, int column)
    {
        for (int i = _dataValidations.Count - 1; i >= 0; i--)
        {
            var dv = _dataValidations[i];
            foreach (var r in dv.Ranges)
            {
                if (r.Contains(row, column))
                    return dv;
            }
        }
        return null;
    }

    private readonly HashSet<int> _hiddenRows = [];
    public IReadOnlySet<int> HiddenRows => _hiddenRows;
    public event Action? RowVisibilityChanged;
    public bool IsRowHidden(int row) => _hiddenRows.Contains(row);
    public bool SetRowHidden(int row, bool hidden)
    {
        bool changed = hidden ? _hiddenRows.Add(row) : _hiddenRows.Remove(row);
        if (changed)
            RowVisibilityChanged?.Invoke();
        return changed;
    }

    public bool SetRowsHidden(int startRow, int count, bool hidden)
    {
        bool anyChanged = false;
        for (int i = 0; i < count; i++)
        {
            int r = startRow + i;
            if (hidden ? _hiddenRows.Add(r) : _hiddenRows.Remove(r))
                anyChanged = true;
        }
        if (anyChanged)
            RowVisibilityChanged?.Invoke();
        return anyChanged;
    }

    private readonly HashSet<int> _hiddenColumns = [];
    public IReadOnlySet<int> HiddenColumns => _hiddenColumns;
    public event Action? ColumnVisibilityChanged;
    public bool IsColumnHidden(int column) => _hiddenColumns.Contains(column);
    public bool SetColumnHidden(int column, bool hidden)
    {
        bool changed = hidden ? _hiddenColumns.Add(column) : _hiddenColumns.Remove(column);
        if (changed)
            ColumnVisibilityChanged?.Invoke();
        return changed;
    }

    public bool SetColumnsHidden(int startColumn, int count, bool hidden)
    {
        bool anyChanged = false;
        for (int i = 0; i < count; i++)
        {
            int c = startColumn + i;
            if (hidden ? _hiddenColumns.Add(c) : _hiddenColumns.Remove(c))
                anyChanged = true;
        }
        if (anyChanged)
            ColumnVisibilityChanged?.Invoke();
        return anyChanged;
    }

    private readonly Dictionary<int, int> _rowOutlineLevels = [];
    private readonly Dictionary<int, int> _columnOutlineLevels = [];
    private readonly HashSet<(int Start, int End, int Level)> _collapsedRowGroups = [];
    private readonly HashSet<(int Start, int End, int Level)> _collapsedColumnGroups = [];
    private bool _summaryRowsBelow = true;
    private bool _summaryColumnsRight = true;

    public bool SummaryRowsBelow
    {
        get => _summaryRowsBelow;
        set
        {
            if (_summaryRowsBelow != value)
            {
                _summaryRowsBelow = value;
                OutlinesChanged?.Invoke();
            }
        }
    }

    public bool SummaryColumnsRight
    {
        get => _summaryColumnsRight;
        set
        {
            if (_summaryColumnsRight != value)
            {
                _summaryColumnsRight = value;
                OutlinesChanged?.Invoke();
            }
        }
    }

    public event Action? OutlinesChanged;

    public int MaxRowOutlineLevel => _rowOutlineLevels.Count > 0 ? _rowOutlineLevels.Values.Max() : 0;
    public int MaxColumnOutlineLevel => _columnOutlineLevels.Count > 0 ? _columnOutlineLevels.Values.Max() : 0;

    public int GetRowOutlineLevel(int row) => _rowOutlineLevels.TryGetValue(row, out int lvl) ? lvl : 0;

    public void SetRowOutlineLevel(int row, int level)
    {
        level = Math.Clamp(level, 0, 8);
        if (level == 0)
            _rowOutlineLevels.Remove(row);
        else
            _rowOutlineLevels[row] = level;
    }

    public int GetColumnOutlineLevel(int column) => _columnOutlineLevels.TryGetValue(column, out int lvl) ? lvl : 0;

    public void SetColumnOutlineLevel(int column, int level)
    {
        level = Math.Clamp(level, 0, 8);
        if (level == 0)
            _columnOutlineLevels.Remove(column);
        else
            _columnOutlineLevels[column] = level;
    }

    public void GroupRows(int startRow, int endRow)
    {
        int min = Math.Min(startRow, endRow);
        int max = Math.Max(startRow, endRow);
        for (int r = min; r <= max; r++)
        {
            SetRowOutlineLevel(r, Math.Min(8, GetRowOutlineLevel(r) + 1));
        }
        OutlinesChanged?.Invoke();
    }

    public void UngroupRows(int startRow, int endRow)
    {
        int min = Math.Min(startRow, endRow);
        int max = Math.Max(startRow, endRow);
        for (int r = min; r <= max; r++)
        {
            SetRowOutlineLevel(r, Math.Max(0, GetRowOutlineLevel(r) - 1));
        }
        var validGroups = GetRowGroups().ToHashSet();
        _collapsedRowGroups.IntersectWith(validGroups);
        UpdateRowVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    public void GroupColumns(int startCol, int endCol)
    {
        int min = Math.Min(startCol, endCol);
        int max = Math.Max(startCol, endCol);
        for (int c = min; c <= max; c++)
        {
            SetColumnOutlineLevel(c, Math.Min(8, GetColumnOutlineLevel(c) + 1));
        }
        OutlinesChanged?.Invoke();
    }

    public void UngroupColumns(int startCol, int endCol)
    {
        int min = Math.Min(startCol, endCol);
        int max = Math.Max(startCol, endCol);
        for (int c = min; c <= max; c++)
        {
            SetColumnOutlineLevel(c, Math.Max(0, GetColumnOutlineLevel(c) - 1));
        }
        var validGroups = GetColumnGroups().ToHashSet();
        _collapsedColumnGroups.IntersectWith(validGroups);
        UpdateColumnVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    public IReadOnlyList<(int Start, int End, int Level)> GetRowGroups()
    {
        var groups = new List<(int Start, int End, int Level)>();
        int max = MaxRowOutlineLevel;
        if (max == 0) return groups;

        int maxRow = _rowOutlineLevels.Count > 0 ? _rowOutlineLevels.Keys.Max() : 0;
        maxRow = Math.Max(maxRow, RowCount - 1);

        for (int level = 1; level <= max; level++)
        {
            int? start = null;
            for (int r = 0; r <= maxRow + 1; r++)
            {
                int lvl = GetRowOutlineLevel(r);
                if (lvl >= level)
                {
                    start ??= r;
                }
                else
                {
                    if (start.HasValue)
                    {
                        groups.Add((start.Value, r - 1, level));
                        start = null;
                    }
                }
            }
        }
        return groups;
    }

    public IReadOnlyList<(int Start, int End, int Level)> GetColumnGroups()
    {
        var groups = new List<(int Start, int End, int Level)>();
        int max = MaxColumnOutlineLevel;
        if (max == 0) return groups;

        int maxCol = _columnOutlineLevels.Count > 0 ? _columnOutlineLevels.Keys.Max() : 0;
        maxCol = Math.Max(maxCol, ColumnCount - 1);

        for (int level = 1; level <= max; level++)
        {
            int? start = null;
            for (int c = 0; c <= maxCol + 1; c++)
            {
                int lvl = GetColumnOutlineLevel(c);
                if (lvl >= level)
                {
                    start ??= c;
                }
                else
                {
                    if (start.HasValue)
                    {
                        groups.Add((start.Value, c - 1, level));
                        start = null;
                    }
                }
            }
        }
        return groups;
    }

    public bool IsRowGroupCollapsed(int startRow, int endRow, int level) =>
        _collapsedRowGroups.Contains((startRow, endRow, level));

    public bool IsRowGroupCollapsed(int startRow, int endRow) =>
        _collapsedRowGroups.Any(g => g.Start == startRow && g.End == endRow);

    public void SetRowGroupCollapsed(int startRow, int endRow, int level, bool collapsed)
    {
        var key = (startRow, endRow, level);
        if (collapsed)
            _collapsedRowGroups.Add(key);
        else
            _collapsedRowGroups.Remove(key);

        UpdateRowVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    public void SetRowGroupCollapsed(int startRow, int endRow, bool collapsed)
    {
        var groups = GetRowGroups().Where(g => g.Start == startRow && g.End == endRow).ToList();
        foreach (var g in groups)
        {
            if (collapsed)
                _collapsedRowGroups.Add(g);
            else
                _collapsedRowGroups.Remove(g);
        }
        UpdateRowVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    public bool IsColumnGroupCollapsed(int startCol, int endCol, int level) =>
        _collapsedColumnGroups.Contains((startCol, endCol, level));

    public bool IsColumnGroupCollapsed(int startCol, int endCol) =>
        _collapsedColumnGroups.Any(g => g.Start == startCol && g.End == endCol);

    public void SetColumnGroupCollapsed(int startCol, int endCol, int level, bool collapsed)
    {
        var key = (startCol, endCol, level);
        if (collapsed)
            _collapsedColumnGroups.Add(key);
        else
            _collapsedColumnGroups.Remove(key);

        UpdateColumnVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    public void SetColumnGroupCollapsed(int startCol, int endCol, bool collapsed)
    {
        var groups = GetColumnGroups().Where(g => g.Start == startCol && g.End == endCol).ToList();
        foreach (var g in groups)
        {
            if (collapsed)
                _collapsedColumnGroups.Add(g);
            else
                _collapsedColumnGroups.Remove(g);
        }
        UpdateColumnVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    public bool IsRowInCollapsedGroup(int row)
    {
        foreach (var g in _collapsedRowGroups)
        {
            if (row >= g.Start && row <= g.End)
                return true;
        }
        return false;
    }

    public bool IsColumnInCollapsedGroup(int col)
    {
        foreach (var g in _collapsedColumnGroups)
        {
            if (col >= g.Start && col <= g.End)
                return true;
        }
        return false;
    }

    public void ToggleRowGroup(int row, int? level = null)
    {
        var groups = GetRowGroups();
        (int Start, int End, int Level)? target = null;
        foreach (var g in groups)
        {
            if (level.HasValue && g.Level != level.Value) continue;
            int summary = _summaryRowsBelow ? g.End + 1 : Math.Max(0, g.Start - 1);
            if (summary == row)
            {
                if (target == null || g.Level > target.Value.Level)
                    target = g;
            }
        }
        if (target == null)
        {
            foreach (var g in groups)
            {
                if (level.HasValue && g.Level != level.Value) continue;
                if (row >= g.Start && row <= g.End)
                {
                    if (target == null || g.Level > target.Value.Level)
                        target = g;
                }
            }
        }

        if (target != null)
        {
            bool isCollapsed = IsRowGroupCollapsed(target.Value.Start, target.Value.End, target.Value.Level);
            SetRowGroupCollapsed(target.Value.Start, target.Value.End, target.Value.Level, !isCollapsed);
        }
    }

    public void ToggleColumnGroup(int column, int? level = null)
    {
        var groups = GetColumnGroups();
        (int Start, int End, int Level)? target = null;
        foreach (var g in groups)
        {
            if (level.HasValue && g.Level != level.Value) continue;
            int summary = _summaryColumnsRight ? g.End + 1 : Math.Max(0, g.Start - 1);
            if (summary == column)
            {
                if (target == null || g.Level > target.Value.Level)
                    target = g;
            }
        }
        if (target == null)
        {
            foreach (var g in groups)
            {
                if (level.HasValue && g.Level != level.Value) continue;
                if (column >= g.Start && column <= g.End)
                {
                    if (target == null || g.Level > target.Value.Level)
                        target = g;
                }
            }
        }

        if (target != null)
        {
            bool isCollapsed = IsColumnGroupCollapsed(target.Value.Start, target.Value.End, target.Value.Level);
            SetColumnGroupCollapsed(target.Value.Start, target.Value.End, target.Value.Level, !isCollapsed);
        }
    }

    public void CollapseRowOutlineLevel(int maxVisibleLevel)
    {
        var groups = GetRowGroups();
        _collapsedRowGroups.Clear();
        foreach (var g in groups)
        {
            if (g.Level >= maxVisibleLevel)
                _collapsedRowGroups.Add(g);
        }
        UpdateRowVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    public void CollapseColumnOutlineLevel(int maxVisibleLevel)
    {
        var groups = GetColumnGroups();
        _collapsedColumnGroups.Clear();
        foreach (var g in groups)
        {
            if (g.Level >= maxVisibleLevel)
                _collapsedColumnGroups.Add(g);
        }
        UpdateColumnVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    private void UpdateRowVisibilitiesAfterGrouping()
    {
        var hiddenByGroups = new HashSet<int>();
        foreach (var g in _collapsedRowGroups)
        {
            for (int r = g.Start; r <= g.End; r++)
                hiddenByGroups.Add(r);
        }

        int maxRow = Math.Max(RowCount, _rowOutlineLevels.Count > 0 ? _rowOutlineLevels.Keys.Max() + 1 : 0);
        bool anyChanged = false;

        for (int r = 0; r < maxRow; r++)
        {
            bool shouldBeHidden = hiddenByGroups.Contains(r) || IsRowFilteredOut(r);
            if (shouldBeHidden)
            {
                if (_hiddenRows.Add(r)) anyChanged = true;
            }
            else
            {
                if (_hiddenRows.Remove(r)) anyChanged = true;
            }
        }

        if (anyChanged)
            RowVisibilityChanged?.Invoke();
    }

    private bool IsRowFilteredOut(int row)
    {
        if (_autoFilter is null) return false;
        int startRow = _autoFilter.Range.Start.Row + 1;
        int endRow = _autoFilter.Range.End.Row;
        if (row < startRow || row > endRow) return false;

        foreach (var col in _autoFilter.Columns)
        {
            string text = this[row, col.ColumnIndex];
            bool isBlank = string.IsNullOrWhiteSpace(text);
            if (isBlank)
            {
                if (!col.IncludeBlanks) return true;
            }
            else
            {
                if (col.Values.Count > 0 && !col.Values.Contains(text, StringComparer.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private void UpdateColumnVisibilitiesAfterGrouping()
    {
        var hiddenByGroups = new HashSet<int>();
        foreach (var g in _collapsedColumnGroups)
        {
            for (int c = g.Start; c <= g.End; c++)
                hiddenByGroups.Add(c);
        }

        int maxCol = Math.Max(ColumnCount, _columnOutlineLevels.Count > 0 ? _columnOutlineLevels.Keys.Max() + 1 : 0);
        bool anyChanged = false;

        for (int c = 0; c < maxCol; c++)
        {
            bool shouldBeHidden = hiddenByGroups.Contains(c);
            if (shouldBeHidden)
            {
                if (_hiddenColumns.Add(c)) anyChanged = true;
            }
            else
            {
                if (_hiddenColumns.Remove(c)) anyChanged = true;
            }
        }

        if (anyChanged)
            ColumnVisibilityChanged?.Invoke();
    }

    public void ClearOutlines()
    {
        _rowOutlineLevels.Clear();
        _columnOutlineLevels.Clear();
        _collapsedRowGroups.Clear();
        _collapsedColumnGroups.Clear();
        UpdateRowVisibilitiesAfterGrouping();
        UpdateColumnVisibilitiesAfterGrouping();
        OutlinesChanged?.Invoke();
    }

    private SpreadsheetAutoFilter? _autoFilter;
    public SpreadsheetAutoFilter? AutoFilter => _autoFilter;
    public event Action? AutoFilterChanged;
    public void SetAutoFilter(CellRangeAddress? range, IReadOnlyList<SpreadsheetFilterColumn>? columns = null)
    {
        if (range is null)
        {
            RemoveAutoFilter();
            return;
        }

        _autoFilter = new SpreadsheetAutoFilter(range.Value, columns);
        ReapplyFilters();
        AutoFilterChanged?.Invoke();
    }

    public void ToggleAutoFilter(CellRangeAddress? range = null)
    {
        if (_autoFilter is not null)
        {
            RemoveAutoFilter();
        }
        else
        {
            CellRangeAddress target = range ?? GetDataRegion(new CellPosition(0, 0));
            SetAutoFilter(target);
        }
    }

    public void RemoveAutoFilter()
    {
        if (_autoFilter is null)
            return;

        int startRow = _autoFilter.Range.Start.Row + 1;
        int endRow = _autoFilter.Range.End.Row;
        bool anyUnhidden = false;
        for (int r = startRow; r <= endRow; r++)
        {
            if (!IsRowInCollapsedGroup(r) && _hiddenRows.Remove(r))
                anyUnhidden = true;
        }

        _autoFilter = null;
        if (anyUnhidden)
            RowVisibilityChanged?.Invoke();
        AutoFilterChanged?.Invoke();
    }

    public void ApplyFilter(int columnIndex, IEnumerable<string> selectedValues, bool includeBlanks)
    {
        if (_autoFilter is null)
            return;

        var col = new SpreadsheetFilterColumn(columnIndex, selectedValues.ToList(), includeBlanks);
        _autoFilter = _autoFilter.WithColumn(col);
        ReapplyFilters();
        AutoFilterChanged?.Invoke();
    }

    public void ClearFilter(int columnIndex)
    {
        if (_autoFilter is null)
            return;

        _autoFilter = _autoFilter.WithoutColumn(columnIndex);
        ReapplyFilters();
        AutoFilterChanged?.Invoke();
    }

    public void ClearAllFilters()
    {
        if (_autoFilter is null)
            return;

        _autoFilter = new SpreadsheetAutoFilter(_autoFilter.Range);
        int startRow = _autoFilter.Range.Start.Row + 1;
        int endRow = _autoFilter.Range.End.Row;
        bool anyUnhidden = false;
        for (int r = startRow; r <= endRow; r++)
        {
            if (!IsRowInCollapsedGroup(r) && _hiddenRows.Remove(r))
                anyUnhidden = true;
        }

        if (anyUnhidden)
            RowVisibilityChanged?.Invoke();
        AutoFilterChanged?.Invoke();
    }

    public void ReapplyFilters()
    {
        if (_autoFilter is null)
            return;

        int startRow = _autoFilter.Range.Start.Row + 1;
        int endRow = _autoFilter.Range.End.Row;
        if (startRow > endRow)
            return;

        bool anyChanged = false;
        for (int r = startRow; r <= endRow; r++)
        {
            bool matches = true;
            foreach (var col in _autoFilter.Columns)
            {
                string text = this[r, col.ColumnIndex];
                bool isBlank = string.IsNullOrWhiteSpace(text);

                if (isBlank)
                {
                    if (!col.IncludeBlanks)
                    {
                        matches = false;
                        break;
                    }
                }
                else
                {
                    if (col.Values.Count > 0 && !col.Values.Contains(text, StringComparer.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }
            }

            bool shouldHide = !matches || IsRowInCollapsedGroup(r);
            bool changed = shouldHide ? _hiddenRows.Add(r) : _hiddenRows.Remove(r);
            if (changed)
                anyChanged = true;
        }

        if (anyChanged)
            RowVisibilityChanged?.Invoke();
    }

    public IReadOnlyList<string> GetDistinctColumnValues(int columnIndex, out bool hasBlanks)
    {
        hasBlanks = false;
        if (_autoFilter is null)
            return [];

        int startRow = _autoFilter.Range.Start.Row + 1;
        int endRow = _autoFilter.Range.End.Row;
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int r = startRow; r <= endRow; r++)
        {
            string text = this[r, columnIndex];
            if (string.IsNullOrWhiteSpace(text))
            {
                hasBlanks = true;
            }
            else
            {
                set.Add(text);
            }
        }

        return set.ToList();
    }

    public CellRangeAddress GetDataRegion(CellPosition position)
    {
        int minRow = position.Row;
        int maxRow = position.Row;
        int minCol = position.Column;
        int maxCol = position.Column;

        bool CellHasData(int r, int c)
        {
            if (r < 0 || r >= RowCount || c < 0 || c >= ColumnCount)
                return false;
            return !string.IsNullOrWhiteSpace(this[r, c]) || HasEditedContent(r, c) || HasCellNote(r, c) || HasCellHyperlink(r, c);
        }

        if (!CellHasData(position.Row, position.Column))
        {
            bool foundNeighbor = false;
            for (int dr = -1; dr <= 1 && !foundNeighbor; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = position.Row + dr;
                    int nc = position.Column + dc;
                    if (CellHasData(nr, nc))
                    {
                        minRow = Math.Min(minRow, nr);
                        maxRow = Math.Max(maxRow, nr);
                        minCol = Math.Min(minCol, nc);
                        maxCol = Math.Max(maxCol, nc);
                        foundNeighbor = true;
                    }
                }
            }
            if (!foundNeighbor)
                return new CellRangeAddress(new CellAddress(position.Row, position.Column), new CellAddress(position.Row, position.Column));
        }

        bool expanded;
        do
        {
            expanded = false;

            if (minRow > 0)
            {
                for (int c = Math.Max(0, minCol - 1); c <= Math.Min(ColumnCount - 1, maxCol + 1); c++)
                {
                    if (CellHasData(minRow - 1, c))
                    {
                        minRow--;
                        minCol = Math.Min(minCol, c);
                        maxCol = Math.Max(maxCol, c);
                        expanded = true;
                        break;
                    }
                }
            }

            if (maxRow < RowCount - 1)
            {
                for (int c = Math.Max(0, minCol - 1); c <= Math.Min(ColumnCount - 1, maxCol + 1); c++)
                {
                    if (CellHasData(maxRow + 1, c))
                    {
                        maxRow++;
                        minCol = Math.Min(minCol, c);
                        maxCol = Math.Max(maxCol, c);
                        expanded = true;
                        break;
                    }
                }
            }

            if (minCol > 0)
            {
                for (int r = Math.Max(0, minRow - 1); r <= Math.Min(RowCount - 1, maxRow + 1); r++)
                {
                    if (CellHasData(r, minCol - 1))
                    {
                        minCol--;
                        minRow = Math.Min(minRow, r);
                        maxRow = Math.Max(maxRow, r);
                        expanded = true;
                        break;
                    }
                }
            }

            if (maxCol < ColumnCount - 1)
            {
                for (int r = Math.Max(0, minRow - 1); r <= Math.Min(RowCount - 1, maxRow + 1); r++)
                {
                    if (CellHasData(r, maxCol + 1))
                    {
                        maxCol++;
                        minRow = Math.Min(minRow, r);
                        maxRow = Math.Max(maxRow, r);
                        expanded = true;
                        break;
                    }
                }
            }
        } while (expanded);

        return new CellRangeAddress(new CellAddress(minRow, minCol), new CellAddress(maxRow, maxCol));
    }

    public IEnumerable<CellPosition> PopulatedPositions
    {
        get
        {
            var set = new HashSet<CellPosition>(_content.Keys);
            foreach (var key in _values.Keys)
                set.Add(key);
            if (ImportedWorksheet is not null)
            {
                foreach (var row in ImportedWorksheet.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        set.Add(new CellPosition(cell.Address.Row, cell.Address.Column));
                    }
                }
            }
            return set;
        }
    }

    public WorkbookModel? Workbook { get; internal set; }
    private Broiler.Documents.Model.Spreadsheets.SpreadsheetWorksheet? _importedWorksheet;
    public Broiler.Documents.Model.Spreadsheets.SpreadsheetWorksheet? ImportedWorksheet
    {
        get => _importedWorksheet;
        set
        {
            _importedWorksheet = value;
            _content.Clear();
            _merges.Clear();
            _conditionalFormats.Clear();
            _dataValidations.Clear();
            _hiddenRows.Clear();
            _autoFilter = value?.AutoFilter;
            if (value is not null)
            {
                _showGridlines = value.ShowGridlines;
                _frozenRows = Math.Clamp(value.FrozenRows, 0, RowCount);
                _frozenColumns = Math.Clamp(value.FrozenColumns, 0, ColumnCount);
                _splitRows = Math.Clamp(value.SplitRows, 0, RowCount);
                _splitColumns = Math.Clamp(value.SplitColumns, 0, ColumnCount);
                if (value.ConditionalFormats is { Count: > 0 } cfs)
                {
                    _conditionalFormats.AddRange(cfs);
                }

                if (value.DataValidations is { Count: > 0 } dvs)
                {
                    _dataValidations.AddRange(dvs);
                }

                foreach (var row in value.Rows)
                {
                    if (row.Hidden)
                        _hiddenRows.Add(row.RowIndex);
                    foreach (var cell in row.Cells)
                    {
                        if (cell.ColumnSpan > 1 || cell.RowSpan > 1)
                        {
                            _merges.Add(new CellMergeRange(cell.Address.Row, cell.Address.Column, cell.RowSpan, cell.ColumnSpan));
                        }
                    }
                }
            }

            _calculationDirty = true;
        }
    }

    private bool _calculationDirty = true;
    private IReadOnlyList<FormulaIssue> _formulaIssues = [];
    private IReadOnlyDictionary<CellAddress, IReadOnlySet<CellAddress>> _dependencies = new Dictionary<CellAddress, IReadOnlySet<CellAddress>>();
    public IReadOnlyList<FormulaIssue> FormulaIssues { get { EnsureCalculated(); return _formulaIssues; } }
    public IReadOnlyDictionary<CellAddress, IReadOnlySet<CellAddress>> FormulaDependencies { get { EnsureCalculated(); return _dependencies; } }
    public string ImportNotice { get; internal set; } = string.Empty;
    public WorksheetPrintSetup PrintSetup { get; set; } = new();
    private bool _showGridlines = true;
    public bool ShowGridlines
    {
        get => _showGridlines;
        set
        {
            if (_showGridlines != value)
            {
                _showGridlines = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private int _frozenRows;
    public int FrozenRows
    {
        get => _frozenRows;
        set
        {
            var clamped = Math.Clamp(value, 0, RowCount);
            if (_frozenRows != clamped)
            {
                _frozenRows = clamped;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private int _frozenColumns;
    public int FrozenColumns
    {
        get => _frozenColumns;
        set
        {
            var clamped = Math.Clamp(value, 0, ColumnCount);
            if (_frozenColumns != clamped)
            {
                _frozenColumns = clamped;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void FreezePanes(int rows, int columns)
    {
        if (rows > 0 || columns > 0)
        {
            _splitRows = 0;
            _splitColumns = 0;
            SplitChanged?.Invoke(this, EventArgs.Empty);
        }
        FrozenRows = rows;
        FrozenColumns = columns;
    }

    public void UnfreezePanes()
    {
        FrozenRows = 0;
        FrozenColumns = 0;
    }

    private int _splitRows;

    /// <summary>The number of rows at which the sheet viewport is split (0 for none).</summary>
    public int SplitRows
    {
        get => _splitRows;
        set => SetSplit(value, _splitColumns);
    }

    private int _splitColumns;

    /// <summary>The number of columns at which the sheet viewport is split (0 for none).</summary>
    public int SplitColumns
    {
        get => _splitColumns;
        set => SetSplit(_splitRows, value);
    }

    /// <summary>Whether this sheet has an active viewport split.</summary>
    public bool HasSplit => _splitRows > 0 || _splitColumns > 0;

    /// <summary>Fired when split rows or split columns change.</summary>
    public event EventHandler? SplitChanged;

    /// <summary>Sets the split pane boundaries in rows and columns.</summary>
    public void SetSplit(int splitRows, int splitColumns)
    {
        var clampedRows = Math.Clamp(splitRows, 0, RowCount);
        var clampedCols = Math.Clamp(splitColumns, 0, ColumnCount);
        if (_splitRows != clampedRows || _splitColumns != clampedCols)
        {
            if (clampedRows > 0 || clampedCols > 0)
            {
                _frozenRows = 0;
                _frozenColumns = 0;
            }
            _splitRows = clampedRows;
            _splitColumns = clampedCols;
            SplitChanged?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removes any active viewport split.</summary>
    public void RemoveSplit() => SetSplit(0, 0);

    /// <summary>Toggles viewport splitting on or off.</summary>
    public void ToggleSplit(int? splitRow = null, int? splitCol = null)
    {
        if (HasSplit)
        {
            RemoveSplit();
        }
        else
        {
            int r = splitRow ?? (RowCount > 1 ? 1 : 0);
            int c = splitCol ?? (ColumnCount > 1 ? 1 : 0);
            SetSplit(r, c);
        }
    }

    public void ToggleGridlines() => ShowGridlines = !ShowGridlines;
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
        if (initialValue is not null)
        {
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    string? val = initialValue(row, column);
                    if (!string.IsNullOrEmpty(val))
                    {
                        _values[new CellPosition(row, column)] = val;
                    }
                }
            }
        }
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
            var pos = new CellPosition(row, column);
            if (_values.TryGetValue(pos, out string? val))
                return val;
            var original = ImportedWorksheet?[row, column];
            if (original is not null)
                return original.Formula ?? original.CachedText ?? original.Value.Format();
            return string.Empty;
        }
        set
        {
            ValidateIndices(row, column);
            string next = value ?? string.Empty;
            var pos = new CellPosition(row, column);
            string current = this[row, column];
            if (current == next) return;
            if (next.Length == 0)
                _values.Remove(pos);
            else
                _values[pos] = next;

            var before = GetStoredContent(row, column);
            _content[pos] = before with
            {
                Value = CellValue.FromText(next),
                Input = next,
                Formula = null,
                CachedText = null
            };
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
        if (_content.TryGetValue(position, out var content))
        {
            if (TryGetMerge(row, column, out int r0, out int c0, out int cs, out int rs))
            {
                if (row == r0 && column == c0 && (content.ColumnSpan != cs || content.RowSpan != rs))
                    return content with { ColumnSpan = cs, RowSpan = rs };
            }

            return content;
        }

        var original = ImportedWorksheet?[row, column];
        if (original is not null)
            return new CellContent(
                original.Value,
                original.Formula ?? original.Value.Format(),
                original.Formula,
                original.NumberFormat,
                original.Formula is null ? null : original.CachedText,
                Shading: original.Shading,
                Font: original.Font,
                Borders: original.Borders,
                Alignment: original.Alignment,
                VerticalAlignment: original.VerticalAlignment,
                ColumnSpan: original.ColumnSpan,
                RowSpan: original.RowSpan,
                WrapText: original.WrapText,
                Note: original.Note,
                Hyperlink: original.Hyperlink);
        int defCs = 1, defRs = 1;
        if (TryGetMerge(row, column, out int mr0, out int mc0, out int mcs, out int mrs) && row == mr0 && column == mc0)
        {
            defCs = mcs;
            defRs = mrs;
        }

        _values.TryGetValue(position, out string? text);
        text ??= string.Empty;
        return new CellContent(CellValue.FromText(text), text, ColumnSpan: defCs, RowSpan: defRs);
    }

    public string GetEditText(int row, int column) => GetContent(row, column).Input;
    public bool HasEditedContent(int row, int column) => _content.ContainsKey(new CellPosition(row, column));
    public void SetInput(int row, int column, string input, CellEntryMode mode = CellEntryMode.Auto, bool force = false)
    {
        var before = GetContent(row, column);
        if (!force && mode == CellEntryMode.Auto && input == before.Input) return;
        var next = CellContent.Parse(input, mode, before.NumberFormat) with
        {
            Shading = before.Shading,
            Font = before.Font,
            Borders = before.Borders,
            Alignment = before.Alignment,
            VerticalAlignment = before.VerticalAlignment,
            ColumnSpan = before.ColumnSpan,
            RowSpan = before.RowSpan,
            Padding = before.Padding,
            WrapText = before.WrapText || input.Contains('\n'),
            Note = before.Note
        };
        string display = next.Display;
        var setInputPos = new CellPosition(row, column);
        _content[setInputPos] = next;
        if (display.Length == 0)
            _values.Remove(setInputPos);
        else
            _values[setInputPos] = display;
        _calculationDirty = true;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? GetCellNumberFormat(int row, int column) => GetContent(row, column).NumberFormat;
    public void SetNumberFormat(int row, int column, string? format)
    {
        var before = GetContent(row, column);
        string? normalizedFormat = string.IsNullOrWhiteSpace(format) || string.Equals(format.Trim(), "General", StringComparison.OrdinalIgnoreCase)
            ? null
            : format.Trim();
        var next = before with
        {
            NumberFormat = normalizedFormat,
            CachedText = before.Formula is not null && !before.Value.IsEmpty
                ? CellNumberFormatter.Format(before.Value, normalizedFormat, System.Globalization.CultureInfo.InvariantCulture).Text
                : before.CachedText
        };
        string display = next.Display; // Validate/format before mutating the model.
        var setFmtPos = new CellPosition(row, column);
        _content[setFmtPos] = next;
        if (display.Length == 0)
            _values.Remove(setFmtPos);
        else
            _values[setFmtPos] = display;
        _calculationDirty = true;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCellShading(int row, int column, BColor shading)
    {
        var before = GetContent(row, column);
        var next = before with { Shading = shading };
        _content[new CellPosition(row, column)] = next;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCellFont(int row, int column, InlineStyle font)
    {
        var before = GetContent(row, column);
        var next = before with { Font = font };
        _content[new CellPosition(row, column)] = next;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCellBorders(int row, int column, CellBorders borders)
    {
        var before = GetContent(row, column);
        var next = before with { Borders = borders };
        _content[new CellPosition(row, column)] = next;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCellAlignment(int row, int column, TextAlignment alignment)
    {
        var before = GetContent(row, column);
        var next = before with { Alignment = alignment };
        _content[new CellPosition(row, column)] = next;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCellVerticalAlignment(int row, int column, CellVerticalAlignment verticalAlignment)
    {
        var before = GetContent(row, column);
        var next = before with { VerticalAlignment = verticalAlignment };
        _content[new CellPosition(row, column)] = next;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool GetCellWrapText(int row, int column) => GetContent(row, column).WrapText;
    public void SetCellWrapText(int row, int column, bool wrapText)
    {
        var before = GetContent(row, column);
        var next = before with { WrapText = wrapText };
        _content[new CellPosition(row, column)] = next;
        if (before != next) Changed?.Invoke(this, EventArgs.Empty);
    }

    public CellNote? GetCellNote(int row, int column) => GetContent(row, column).Note;

    public void SetCellNote(int row, int column, CellNote? note)
    {
        if (row < 0 || row >= RowCount || column < 0 || column >= ColumnCount)
            return;

        var before = GetContent(row, column);
        if (before.Note == note)
            return;

        var next = before with { Note = note };
        _content[new CellPosition(row, column)] = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCellNote(int row, int column, string? noteText, string? author = null, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(noteText))
            SetCellNote(row, column, (CellNote?)null);
        else
            SetCellNote(row, column, new CellNote(noteText.Trim(), string.IsNullOrWhiteSpace(author) ? null : author.Trim(), timestamp ?? DateTimeOffset.UtcNow));
    }

    public void ClearCellNote(int row, int column) => SetCellNote(row, column, (CellNote?)null);
    public bool HasCellNote(int row, int column) => GetCellNote(row, column) is not null;

    /// <summary>Gets the hyperlink attached to the specified cell, or null if none.</summary>
    public CellHyperlink? GetCellHyperlink(int row, int column) => GetContent(row, column).Hyperlink;

    /// <summary>Attaches or removes a hyperlink on the specified cell.</summary>
    public void SetCellHyperlink(int row, int column, CellHyperlink? hyperlink)
    {
        var content = GetContent(row, column);
        if (content.Hyperlink == hyperlink) return;

        // If setting a hyperlink and cell has default font, style it as standard link (blue + underline)
        var font = content.Font;
        if (hyperlink is not null && font == default)
        {
            font = new InlineStyle { Foreground = new BColor(17, 85, 204), Underline = true, LinkHref = hyperlink.Target };
        }
        else if (hyperlink is not null && !font.IsLink)
        {
            font = font with { LinkHref = hyperlink.Target };
        }
        else if (hyperlink is null && font.IsLink)
        {
            font = font with { LinkHref = null };
        }

        _content[new CellPosition(row, column)] = content with { Hyperlink = hyperlink, Font = font };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Attaches a hyperlink with the specified target and optional tooltip on the cell.</summary>
    public void SetCellHyperlink(int row, int column, string? target, string? tooltip = null)
    {
        if (string.IsNullOrWhiteSpace(target))
            SetCellHyperlink(row, column, (CellHyperlink?)null);
        else
            SetCellHyperlink(row, column, new CellHyperlink(target.Trim(), string.IsNullOrWhiteSpace(tooltip) ? null : tooltip.Trim()));
    }

    /// <summary>Removes any hyperlink from the specified cell.</summary>
    public void ClearCellHyperlink(int row, int column) => SetCellHyperlink(row, column, (CellHyperlink?)null);

    /// <summary>Gets whether the specified cell has a hyperlink attached.</summary>
    public bool HasCellHyperlink(int row, int column) => GetCellHyperlink(row, column) is not null;
    public void SetCellSpan(int row, int column, int columnSpan, int rowSpan)
    {
        if (columnSpan > 1 || rowSpan > 1)
            MergeCells(row, column, row + rowSpan - 1, column + columnSpan - 1, center: false);
        else
            Unmerge(row, column);
    }

    public bool TryGetMerge(int row, int column, out int rootRow, out int rootColumn, out int colSpan, out int rowSpan)
    {
        for (int i = 0; i < _merges.Count; i++)
        {
            var m = _merges[i];
            if (m.Contains(row, column))
            {
                rootRow = m.StartRow;
                rootColumn = m.StartColumn;
                colSpan = m.ColumnSpan;
                rowSpan = m.RowSpan;
                return true;
            }
        }

        rootRow = row;
        rootColumn = column;
        colSpan = 1;
        rowSpan = 1;
        return false;
    }

    public bool IsCoveredCell(int row, int column)
    {
        for (int i = 0; i < _merges.Count; i++)
        {
            var m = _merges[i];
            if (m.Contains(row, column) && !m.IsRoot(row, column))
                return true;
        }

        return false;
    }

    public bool IsMergedRoot(int row, int column)
    {
        for (int i = 0; i < _merges.Count; i++)
        {
            if (_merges[i].IsRoot(row, column))
                return true;
        }

        return false;
    }

    public void MergeCells(int startRow, int startCol, int endRow, int endCol, bool center = true)
    {
        int r0 = Math.Clamp(Math.Min(startRow, endRow), 0, RowCount - 1);
        int r1 = Math.Clamp(Math.Max(startRow, endRow), 0, RowCount - 1);
        int c0 = Math.Clamp(Math.Min(startCol, endCol), 0, ColumnCount - 1);
        int c1 = Math.Clamp(Math.Max(startCol, endCol), 0, ColumnCount - 1);
        int rowSpan = r1 - r0 + 1;
        int colSpan = c1 - c0 + 1;
        if (rowSpan <= 1 && colSpan <= 1)
            return;
        _merges.RemoveAll(m => m.StartRow <= r1 && m.EndRow >= r0 && m.StartColumn <= c1 && m.EndColumn >= c0);
        _merges.Add(new CellMergeRange(r0, c0, rowSpan, colSpan));
        var root = GetContent(r0, c0) with
        {
            ColumnSpan = colSpan,
            RowSpan = rowSpan,
            Alignment = center ? TextAlignment.Center : GetContent(r0, c0).Alignment
        };
        _content[new CellPosition(r0, c0)] = root;
        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                if (r == r0 && c == c0) continue;
                _values.Remove(new CellPosition(r, c));
                var before = GetStoredContent(r, c);
                _content[new CellPosition(r, c)] = before with
                {
                    Value = CellValue.Empty,
                    Input = string.Empty,
                    Formula = null,
                    CachedText = null,
                    ColumnSpan = 1,
                    RowSpan = 1
                };
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Unmerge(int row, int column)
    {
        for (int i = _merges.Count - 1; i >= 0; i--)
        {
            if (_merges[i].Contains(row, column))
            {
                var m = _merges[i];
                _merges.RemoveAt(i);
                var root = GetContent(m.StartRow, m.StartColumn) with
                {
                    ColumnSpan = 1,
                    RowSpan = 1
                };
                _content[new CellPosition(m.StartRow, m.StartColumn)] = root;
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
    }

    public double? GetColumnWidth(int column) =>
        _customColumnWidths.TryGetValue(column, out var w) ? w : null;
    public void SetColumnWidth(int column, double width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ColumnCount);
        _customColumnWidths[column] = width;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetColumnWidths(IReadOnlyList<double> widths)
    {
        ArgumentNullException.ThrowIfNull(widths);
        for (int c = 0; c < Math.Min(ColumnCount, widths.Count); c++)
            if (widths[c] > 0) _customColumnWidths[c] = widths[c];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public double? GetRowHeight(int row) =>
        _customRowHeights.TryGetValue(row, out var h) ? h : null;
    public void SetRowHeight(int row, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        _customRowHeights[row] = height;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<double>? GetExplicitColumnWidths()
    {
        if (_customColumnWidths.Count == 0 && (ImportedWorksheet?.ColumnWidths is null || ImportedWorksheet.ColumnWidths.Count == 0))
            return null;
        var widths = new double[ColumnCount];
        for (int c = 0; c < ColumnCount; c++)
        {
            if (_customColumnWidths.TryGetValue(c, out var w))
                widths[c] = w;
            else if (ImportedWorksheet?.ColumnWidths is { Count: > 0 } iw && c < iw.Count)
                widths[c] = iw[c];
            else
                widths[c] = 72;
        }

        return widths;
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
            var formulaCells = new HashSet<CellPosition>();
            foreach (var (p, c) in _content)
                if (c.Formula is not null) formulaCells.Add(p);
            if (ImportedWorksheet is not null)
            {
                foreach (var r in ImportedWorksheet.Rows)
                    foreach (var c in r.Cells)
                        if (c.Formula is not null) formulaCells.Add(new CellPosition(c.Address.Row, c.Address.Column));
            }

            foreach (var p in formulaCells)
            {
                var input = GetStoredContent(p.Row, p.Column);
                if (input.Formula is null) continue;
                var value = result.Evaluation.GetValue(p.Row, p.Column);
                var cached = CellNumberFormatter.Format(value, input.NumberFormat, System.Globalization.CultureInfo.InvariantCulture).Text;
                _content[p] = input with { Value = value, CachedText = cached };
                if (cached.Length == 0)
                    _values.Remove(p);
                else
                    _values[p] = cached;
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

/// <summary>Represents a merged range of cells in a worksheet.</summary>
public readonly record struct CellMergeRange(int StartRow, int StartColumn, int RowSpan, int ColumnSpan)
{
    public int EndRow => StartRow + RowSpan - 1;
    public int EndColumn => StartColumn + ColumnSpan - 1;
    public bool Contains(int row, int col) =>
        row >= StartRow && row <= EndRow && col >= StartColumn && col <= EndColumn;
    public bool IsRoot(int row, int col) =>
        row == StartRow && col == StartColumn;
}


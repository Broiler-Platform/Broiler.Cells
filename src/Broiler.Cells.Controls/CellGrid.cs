using Broiler.Cells.Core;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Edit;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Cells.Controls;

/// <summary>A cell grid control supporting display, selection, and in-place editing.</summary>
public sealed class CellGrid : UiElement, IStandardThemedControl
{
    public const double DefaultCellWidth = 96;
    public const double CellWidth = DefaultCellWidth;
    public const double DefaultCellHeight = 28;
    public const double CellHeight = DefaultCellHeight;
    public const double MinCellWidth = 20;
    public const double MaxCellWidth = 1000;
    public const double MinCellHeight = 16;
    public const double MaxCellHeight = 500;

    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(400);
    private const double DoubleClickSlop = 4;

    private StandardThemeTokens _theme = StandardThemeTokens.Light;
    private BFontStyle _font = StandardThemeTokens.Light.FontBody;
    private readonly Dictionary<Broiler.Documents.Model.InlineStyle, BFontStyle> _fontCache = [];
    private readonly Dictionary<CellPosition, CellSemanticElement> _semanticCells = [];
    private readonly StandardEdit _editor;

    private double[] _columnWidths;
    private double[] _rowHeights;
    private double[] _columnOffsets;
    private double[] _rowOffsets;

    private bool _isEditingEnabled;
    private bool _isEditing;
    private int _editingRow;
    private int _editingCol;

    private UiTimestamp _lastClickTime;
    private BPoint _lastClickPosition;
    private CellPosition? _lastClickCell;
    private bool _hasClicked;
    private readonly SortedSet<int> _selectedRows = [];
    private readonly SortedSet<int> _selectedColumns = [];
    private int _selectionAnchor;
    private bool _anchorIsColumn;
    private CellPosition? _cellSelectionAnchor;
    public CellPosition? CellSelectionAnchor => _cellSelectionAnchor;
    private bool _isSelectingCells;
    private BPoint _lastSelectionPointer;
    private CellPosition? _hoveredCell;

    public CellPosition? HoveredCell => _hoveredCell;
    public event EventHandler<CellPosition?>? HoveredCellChanged;
    public event Action<int, int, ValidationResult>? ValidationFailed;
    public event Action<int, int, DataValidation, BRect>? OpenDropdownRequested;
    public event Action<int, BRect>? FilterDropdownRequested;
    public event Action<CellRangeAddress, CellRangeAddress>? AutoFillCompleted;
    public event Action<int, int, CellHyperlink>? HyperlinkClicked;

    private CellPosition? _pointerDownCell;
    private BPoint _pointerDownPos;

    private bool _isFillDragging;
    private CellRangeAddress? _fillSourceRange;
    private CellRangeAddress? _fillTargetRange;
    private UiTimestamp _lastFillHandleClickTime;
    private BPoint _lastFillHandleClickPosition;

    public bool IsFillDragging => _isFillDragging;
    public CellRangeAddress? FillTargetRange => _fillTargetRange;

    public enum SplitterKind
    {
        None,
        Horizontal,
        Vertical,
        Both
    }

    private SplitterKind _hoveredSplitter = SplitterKind.None;
    private bool _isDraggingSplitter;
    private SplitterKind _draggingSplitterKind = SplitterKind.None;
    private UiTimestamp _lastSplitterClickTime;
    private BPoint _lastSplitterClickPosition;

    public SplitterKind HoveredSplitter => _hoveredSplitter;
    public bool IsDraggingSplitter => _isDraggingSplitter;

    public CellGrid(SheetModel sheet, bool isEditingEnabled = true)
    {
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        _sheet.ConditionalFormatsChanged += InvalidateRender;
        _sheet.DataValidationsChanged += InvalidateRender;
        _sheet.RowVisibilityChanged += OnRowVisibilityChanged;
        _sheet.ColumnVisibilityChanged += OnColumnVisibilityChanged;
        _sheet.AutoFilterChanged += InvalidateRender;
        _sheet.SplitChanged += OnSplitChanged;
        _isEditingEnabled = isEditingEnabled;
        Focusable = isEditingEnabled;
        AccessibleName = isEditingEnabled ? "Sheet" : "Read-only sheet";

        _columnWidths = new double[sheet.ColumnCount];
        Array.Fill(_columnWidths, DefaultCellWidth);
        _rowHeights = new double[sheet.RowCount];
        Array.Fill(_rowHeights, DefaultCellHeight);
        _columnOffsets = new double[sheet.ColumnCount + 1];
        _rowOffsets = new double[sheet.RowCount + 1];
        RecalculateColumnOffsets();
        RecalculateRowOffsets();

        _editor = new StandardEdit
        {
            Visibility = UiVisibility.Collapsed,
            CornerRadius = 0,
            PaddingX = 6,
            PaddingY = 0,
            Background = _theme.Surface,
            Foreground = _theme.Text,
            Font = _font,
        };
        _editor.Submitted += OnEditorSubmitted;
        AddChild(_editor);

        UpdateSemanticCells();
    }

    private SheetModel _sheet;
    public SheetModel Sheet => _sheet;
    public double TotalWidth => _columnOffsets[Sheet.ColumnCount];
    public double TotalHeight => _rowOffsets[Sheet.RowCount];
    public BSize SheetSize => new(TotalWidth + 1, TotalHeight + 1);

    /// <summary>Optional provider returning (HorizontalOffset, VerticalOffset) from the hosting ScrollView.</summary>
    public Func<(double X, double Y)>? ScrollOffsetProvider { get; set; }
    public CellPosition? SelectedCell { get; private set; }
    public string? SelectedAddress => SelectedCell?.ToAddress();
    public IReadOnlyList<int> SelectedRows => _selectedRows.ToArray();
    public IReadOnlyList<int> SelectedColumns => _selectedColumns.ToArray();

    public bool IsCellSelected(int row, int column)
    {
        if (_selectedRows.Contains(row) || _selectedColumns.Contains(column))
            return true;

        if (Sheet.TryGetMerge(row, column, out int r0, out int c0, out int cs, out int rs))
        {
            if (SelectedCell == new CellPosition(r0, c0))
                return true;
            if (_cellSelectionAnchor is { } anchor && SelectedCell is { } end)
            {
                int minR = Math.Min(anchor.Row, end.Row);
                int maxR = Math.Max(anchor.Row, end.Row);
                int minC = Math.Min(anchor.Column, end.Column);
                int maxC = Math.Max(anchor.Column, end.Column);
                if (r0 <= maxR && (r0 + rs - 1) >= minR && c0 <= maxC && (c0 + cs - 1) >= minC)
                    return true;
            }
        }

        if (SelectedCell == new CellPosition(row, column))
            return true;

        if (_cellSelectionAnchor is { } a && SelectedCell is { } e)
        {
            return row >= Math.Min(a.Row, e.Row) && row <= Math.Max(a.Row, e.Row) &&
                   column >= Math.Min(a.Column, e.Column) && column <= Math.Max(a.Column, e.Column);
        }

        return false;
    }

    public (int StartRow, int StartCol, int EndRow, int EndCol)? GetSelectedRange()
    {
        if (_selectedRows.Count > 0)
        {
            int r0 = _selectedRows.Min();
            int r1 = _selectedRows.Max();
            return (r0, 0, r1, Sheet.ColumnCount - 1);
        }
        if (_selectedColumns.Count > 0)
        {
            int c0 = _selectedColumns.Min();
            int c1 = _selectedColumns.Max();
            return (0, c0, Sheet.RowCount - 1, c1);
        }
        if (_cellSelectionAnchor is { } anchor && SelectedCell is { } end)
        {
            int r0 = Math.Min(anchor.Row, end.Row);
            int r1 = Math.Max(anchor.Row, end.Row);
            int c0 = Math.Min(anchor.Column, end.Column);
            int c1 = Math.Max(anchor.Column, end.Column);

            bool expanded;
            do
            {
                expanded = false;
                foreach (var m in Sheet.Merges)
                {
                    if (m.StartRow <= r1 && m.EndRow >= r0 && m.StartColumn <= c1 && m.EndColumn >= c0)
                    {
                        if (m.StartRow < r0) { r0 = m.StartRow; expanded = true; }
                        if (m.EndRow > r1) { r1 = m.EndRow; expanded = true; }
                        if (m.StartColumn < c0) { c0 = m.StartColumn; expanded = true; }
                        if (m.EndColumn > c1) { c1 = m.EndColumn; expanded = true; }
                    }
                }
            } while (expanded);

            return (r0, c0, r1, c1);
        }
        if (SelectedCell is { } pos)
        {
            if (Sheet.TryGetMerge(pos.Row, pos.Column, out int r0, out int c0, out int cs, out int rs))
                return (r0, c0, r0 + rs - 1, c0 + cs - 1);
            return (pos.Row, pos.Column, pos.Row, pos.Column);
        }
        return null;
    }

    public bool IsRowSelected(int row) => _selectedRows.Contains(row);
    public bool IsColumnSelected(int column) => _selectedColumns.Contains(column);
    public bool IsEditing => _isEditing;
    public bool AreHeadersVisible { get; set; } = true;

    public double GetColumnWidth(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Sheet.ColumnCount);
        return Sheet.IsColumnHidden(column) ? 0 : _columnWidths[column];
    }

    public double GetRawColumnWidth(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Sheet.ColumnCount);
        return _columnWidths[column];
    }

    public void SetColumnWidth(int column, double width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Sheet.ColumnCount);

        width = Math.Clamp(width, MinCellWidth, MaxCellWidth);
        double oldWidth = _columnWidths[column];
        if (Math.Abs(oldWidth - width) < 0.001)
            return;

        _columnWidths[column] = width;
        RecalculateColumnOffsets();

        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        ColumnResized?.Invoke(this, new ColumnResizedEventArgs(column, oldWidth, width));
    }

    public double GetRowHeight(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Sheet.RowCount);
        return Sheet.IsRowHidden(row) ? 0 : _rowHeights[row];
    }

    public void SetRowHeight(int row, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Sheet.RowCount);

        height = Math.Clamp(height, MinCellHeight, MaxCellHeight);
        double oldHeight = _rowHeights[row];
        if (Math.Abs(oldHeight - height) < 0.001)
            return;

        _rowHeights[row] = height;
        RecalculateRowOffsets();

        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        RowResized?.Invoke(this, new RowResizedEventArgs(row, oldHeight, height));
    }

    public double GetColumnOffset(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, Sheet.ColumnCount);
        return _columnOffsets[column];
    }

    public double GetRowOffset(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, Sheet.RowCount);
        return _rowOffsets[row];
    }

    public void AutoFitColumn(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Sheet.ColumnCount);

        string headerLabel = CellPosition.GetColumnLabel(column);
        double maxWidth = BTextMeasurer.MeasureAdvance(headerLabel, _font) + 24;

        int scanned = 0;
        foreach (var pos in Sheet.PopulatedPositions)
        {
            if (pos.Column != column)
                continue;
            int row = pos.Row;
            if (Sheet.TryGetMerge(row, column, out _, out _, out int cs, out _) && cs > 1)
                continue;

            string textVal = Sheet[row, column];
            if (!string.IsNullOrEmpty(textVal))
            {
                var font = ResolveFont(Sheet.GetContent(row, column).Font);
                string[] lines = textVal.Split('\n');
                foreach (var line in lines)
                {
                    double advance = BTextMeasurer.MeasureAdvance(line.TrimEnd('\r'), font) + 16;
                    if (advance > maxWidth)
                        maxWidth = advance;
                }
            }
            if (++scanned >= 5000)
                break;
        }

        SetColumnWidth(column, maxWidth);
    }

    public void AutoFitRow(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Sheet.RowCount);

        double maxHeight = DefaultCellHeight;
        for (int col = 0; col < Sheet.ColumnCount; col++)
        {
            if (Sheet.TryGetMerge(row, col, out _, out _, out _, out int rs) && rs > 1)
                continue;

            string text = Sheet[row, col];
            if (!string.IsNullOrEmpty(text))
            {
                var content = Sheet.GetContent(row, col);
                var font = ResolveFont(content.Font);
                double lineHeight = BTextMeasurer.GetLineHeight(font);
                double colWidth = GetColumnWidth(col);
                var lines = BreakLines(text, font, content.WrapText, Math.Max(12, colWidth - 12));
                double cellNeededHeight = Math.Max(DefaultCellHeight, lines.Count * lineHeight + 8);
                if (cellNeededHeight > maxHeight)
                    maxHeight = cellNeededHeight;
            }
        }

        SetRowHeight(row, maxHeight);
    }

    public void ResetColumnWidth(int column) => SetColumnWidth(column, DefaultCellWidth);
    public void ResetRowHeight(int row) => SetRowHeight(row, DefaultCellHeight);

    private void RecalculateColumnOffsets()
    {
        _columnOffsets[0] = 0;
        for (int i = 0; i < _columnWidths.Length; i++)
        {
            double effW = Sheet.IsColumnHidden(i) ? 0 : _columnWidths[i];
            _columnOffsets[i + 1] = _columnOffsets[i] + effW;
        }
    }

    private void OnColumnVisibilityChanged()
    {
        RecalculateColumnOffsets();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    private void RecalculateRowOffsets()
    {
        _rowOffsets[0] = 0;
        for (int i = 0; i < _rowHeights.Length; i++)
        {
            double effH = Sheet.IsRowHidden(i) ? 0 : _rowHeights[i];
            _rowOffsets[i + 1] = _rowOffsets[i] + effH;
        }
    }

    private void OnRowVisibilityChanged()
    {
        RecalculateRowOffsets();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public int GetColumnAtOffset(double localX)
    {
        if (localX < 0 || localX >= TotalWidth)
            return -1;
        int index = Array.BinarySearch(_columnOffsets, localX);
        int col;
        if (index >= 0)
        {
            col = Math.Min(index, Sheet.ColumnCount - 1);
        }
        else
        {
            col = Math.Clamp(~index - 1, 0, Sheet.ColumnCount - 1);
        }

        while (col < Sheet.ColumnCount && Sheet.IsColumnHidden(col))
            col++;
        if (col >= Sheet.ColumnCount)
        {
            col = Sheet.ColumnCount - 1;
            while (col >= 0 && Sheet.IsColumnHidden(col))
                col--;
        }
        return col;
    }

    public int GetRowAtOffset(double localY)
    {
        if (localY < 0 || localY >= TotalHeight)
            return -1;
        int index = Array.BinarySearch(_rowOffsets, localY);
        int row;
        if (index >= 0)
        {
            row = Math.Min(index, Sheet.RowCount - 1);
        }
        else
        {
            row = Math.Clamp(~index - 1, 0, Sheet.RowCount - 1);
        }

        while (row < Sheet.RowCount && Sheet.IsRowHidden(row))
            row++;
        if (row >= Sheet.RowCount)
        {
            row = Sheet.RowCount - 1;
            while (row >= 0 && Sheet.IsRowHidden(row))
                row--;
        }
        return row;
    }

    public bool IsEditingEnabled
    {
        get => _isEditingEnabled;
        set
        {
            if (_isEditingEnabled == value)
                return;

            if (!value && _isEditing)
                CancelEdit();

            _isEditingEnabled = value;
            Focusable = value;
            AccessibleName = value ? "Sheet" : "Read-only sheet";
            if (!value)
            {
                EndCellSelectionDrag();
                _cellSelectionAnchor = null;
                SelectedCell = null;
                _selectedRows.Clear();
                _selectedColumns.Clear();
                SelectedCellChanged?.Invoke(this, null);
                NotifySelectionChanged();
            }

            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render | UiInvalidationKind.Semantic);
        }
    }

    public event EventHandler<CellPosition?>? SelectedCellChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<CellCommittedEventArgs>? CellCommitted;
    public event EventHandler<ColumnResizedEventArgs>? ColumnResized;
    public event EventHandler<RowResizedEventArgs>? RowResized;

    public void ApplyTheme(StandardThemeTokens theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _font = theme.FontBody;
        _fontCache.Clear();
        _editor.ApplyTheme(theme);
        _editor.CornerRadius = 0;
        _editor.PaddingX = 6;
        _editor.PaddingY = 0;
        InvalidateRender();
    }

    private BFontStyle ResolveFont(Broiler.Documents.Model.InlineStyle style)
    {
        if (style == default || (string.IsNullOrWhiteSpace(style.FontFamily) && style.FontSize is not (> 0) && !style.Bold && !style.Italic))
            return _font;

        if (_fontCache.TryGetValue(style, out var cached))
            return cached;

        string family = !string.IsNullOrWhiteSpace(style.FontFamily) ? style.FontFamily : _font.FamilyName;
        double size = style.FontSize is > 0 ? style.FontSize.Value : _font.Size;
        var weight = style.Bold ? BFontWeight.Bold : BFontWeight.Normal;
        var slant = style.Italic ? BFontSlant.Italic : BFontSlant.Normal;
        var resolved = new BFontStyle(family, Math.Max(1.0, size), weight, slant);
        _fontCache[style] = resolved;
        return resolved;
    }

    public void SetSheet(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        EndCellSelectionDrag();
        _isFillDragging = false;
        _fillSourceRange = null;
        _fillTargetRange = null;
        _isDraggingSplitter = false;
        _draggingSplitterKind = SplitterKind.None;
        _cellSelectionAnchor = null;
        if (_isEditing)
            CancelEdit();

        bool sameDimensions = _sheet.RowCount == sheet.RowCount && _sheet.ColumnCount == sheet.ColumnCount;
        bool colCountChanged = _sheet.ColumnCount != sheet.ColumnCount;
        bool rowCountChanged = _sheet.RowCount != sheet.RowCount;

        if (!ReferenceEquals(_sheet, sheet))
        {
            _sheet.ConditionalFormatsChanged -= InvalidateRender;
            _sheet.DataValidationsChanged -= InvalidateRender;
            _sheet.RowVisibilityChanged -= OnRowVisibilityChanged;
            _sheet.ColumnVisibilityChanged -= OnColumnVisibilityChanged;
            _sheet.AutoFilterChanged -= InvalidateRender;
            _sheet.SplitChanged -= OnSplitChanged;

            _sheet = sheet;
            _sheet.ConditionalFormatsChanged += InvalidateRender;
            _sheet.DataValidationsChanged += InvalidateRender;
            _sheet.RowVisibilityChanged += OnRowVisibilityChanged;
            _sheet.ColumnVisibilityChanged += OnColumnVisibilityChanged;
            _sheet.AutoFilterChanged += InvalidateRender;
            _sheet.SplitChanged += OnSplitChanged;
        }

        if (colCountChanged)
        {
            _columnWidths = new double[sheet.ColumnCount];
            Array.Fill(_columnWidths, DefaultCellWidth);
            _columnOffsets = new double[sheet.ColumnCount + 1];
        }

        if (rowCountChanged)
        {
            _rowHeights = new double[sheet.RowCount];
            Array.Fill(_rowHeights, DefaultCellHeight);
            _rowOffsets = new double[sheet.RowCount + 1];
        }

        RecalculateColumnOffsets();
        RecalculateRowOffsets();

        bool hadHover = _hoveredCell is not null;
        SelectedCell = null;
        _selectedRows.Clear();
        _selectedColumns.Clear();
        _hasClicked = false;
        _lastClickCell = null;
        _pointerDownCell = null;
        _hoveredCell = null;

        if (sameDimensions)
        {
            foreach (var kvp in _semanticCells)
            {
                kvp.Value.UpdateValue(sheet[kvp.Key.Row, kvp.Key.Column]);
            }
        }
        else
        {
            foreach (var cell in _semanticCells.Values)
                RemoveChild(cell);
            _semanticCells.Clear();
            UpdateSemanticCells();
        }

        if (hadHover)
            HoveredCellChanged?.Invoke(this, null);
        SelectedCellChanged?.Invoke(this, null);
        NotifySelectionChanged();

        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render | UiInvalidationKind.Semantic);
    }

    private void OnSplitChanged(object? sender, EventArgs e) => InvalidateRender();

    public void SelectCell(CellPosition position) => SelectCell(position, extend: false);

    /// <summary>Select a cell, optionally extending a rectangle from the existing cell anchor.</summary>
    public void SelectCell(CellPosition position, bool extend)
    {
        int row = Math.Clamp(position.Row, 0, Sheet.RowCount - 1);
        int col = Math.Clamp(position.Column, 0, Sheet.ColumnCount - 1);
        if (Sheet.TryGetMerge(row, col, out int rootR, out int rootC, out _, out _))
        {
            row = rootR;
            col = rootC;
        }
        var newPos = new CellPosition(row, col);
        var anchor = extend ? _cellSelectionAnchor ?? SelectedCell ?? newPos : newPos;
        if (SelectedCell == newPos && _cellSelectionAnchor == anchor &&
            _selectedRows.Count == 0 && _selectedColumns.Count == 0)
            return;

        if (_isEditing)
            CommitEdit();
        var oldPos = SelectedCell;
        _selectedRows.Clear();
        _selectedColumns.Clear();
        _cellSelectionAnchor = anchor;
        SelectedCell = newPos;
        if (oldPos != newPos)
            SelectedCellChanged?.Invoke(this, newPos);
        NotifySelectionChanged();

        BringIntoView(CellBounds(row, col));
        InvalidateRender();
    }

    /// <summary>Select a whole row. Extend selects from the anchor; toggle adds or removes a row.</summary>
    public void SelectRow(int row, bool extend = false, bool toggle = false) =>
        SelectHeader(row, false, extend, toggle);

    /// <summary>Select a whole column. Extend selects from the anchor; toggle adds or removes a column.</summary>
    public void SelectColumn(int column, bool extend = false, bool toggle = false) =>
        SelectHeader(column, true, extend, toggle);

    internal void ExtendHeaderSelection(int index, bool column, IReadOnlyList<int> baseline) =>
        SelectHeader(index, column, true, false, baseline);

    private void SelectHeader(int index, bool column, bool extend, bool toggle, IReadOnlyList<int>? baseline = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, column ? Sheet.ColumnCount : Sheet.RowCount);
        if (!IsEditingEnabled)
            return;
        EndCellSelectionDrag();
        _cellSelectionAnchor = null;
        if (_isEditing)
            CommitEdit();

        var selected = column ? _selectedColumns : _selectedRows;
        var other = column ? _selectedRows : _selectedColumns;
        if (!extend || (baseline is null && selected.Count == 0) || _anchorIsColumn != column)
            _selectionAnchor = index;
        _anchorIsColumn = column;
        other.Clear();
        if (!toggle)
            selected.Clear();
        if (baseline is not null)
            selected.UnionWith(baseline);
        if (extend)
        {
            for (int i = Math.Min(_selectionAnchor, index); i <= Math.Max(_selectionAnchor, index); i++)
                selected.Add(i);
        }
        else if (!toggle || !selected.Remove(index))
        {
            selected.Add(index);
        }

        var old = SelectedCell;
        int active = selected.Contains(index) ? index : selected.Count > 0 ? selected.Min : -1;
        SelectedCell = active < 0 ? null : column
            ? new CellPosition(Math.Clamp(old?.Row ?? 0, 0, Sheet.RowCount - 1), active)
            : new CellPosition(active, Math.Clamp(old?.Column ?? 0, 0, Sheet.ColumnCount - 1));
        _hasClicked = false;
        if (old != SelectedCell)
            SelectedCellChanged?.Invoke(this, SelectedCell);
        NotifySelectionChanged();
        if (SelectedCell is { } pos)
            BringIntoView(CellBounds(pos.Row, pos.Column));
    }

    public void SelectAll()
    {
        if (!IsEditingEnabled)
            return;
        EndCellSelectionDrag();
        if (_isEditing)
            CommitEdit();
        _selectedColumns.Clear();
        _selectedRows.Clear();
        _cellSelectionAnchor = new CellPosition(0, 0);
        SelectedCell = new CellPosition(Sheet.RowCount - 1, Sheet.ColumnCount - 1);
        SelectedCellChanged?.Invoke(this, SelectedCell);
        NotifySelectionChanged();
        InvalidateRender();
    }

    private void NotifySelectionChanged()
    {
        foreach (var cell in _semanticCells.Values)
            cell.Invalidate(UiInvalidationKind.Semantic);
        InvalidateRender();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartEditing(string initialText)
    {
        if (!IsEditingEnabled || SelectedCell is null)
            return;

        EndCellSelectionDrag();
        _editingRow = SelectedCell.Value.Row;
        _editingCol = SelectedCell.Value.Column;
        _isEditing = true;

        MoveChildToFront(_editor);

        _editor.Text = initialText;
        _editor.SetSelection(initialText.Length, 0);
        _editor.Visibility = UiVisibility.Visible;
        BRect editBounds = CellBounds(_editingRow, _editingCol);
        if (Sheet.GetDataValidation(_editingRow, _editingCol) is { Type: DataValidationType.List, ShowDropdown: true })
        {
            double btnW = Math.Min(18, editBounds.Width);
            editBounds = new BRect(editBounds.X, editBounds.Y, Math.Max(0, editBounds.Width - btnW), editBounds.Height);
        }
        _editor.Arrange(editBounds);

        Session?.SetFocus(_editor);
        InvalidateRender();
    }

    public CellEntryMode EntryMode { get; set; } = CellEntryMode.Auto;
    public string? LastInputError { get; private set; }
    public event EventHandler<string>? InputRejected;

    private bool ApplyInput(int row, int column, string text, bool force = false)
    {
        try
        {
            Sheet.SetInput(row, column, text, EntryMode, force);
            LastInputError = null;
            return true;
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            LastInputError = error.Message;
            InputRejected?.Invoke(this, error.Message);
            return false;
        }
    }

    public void CommitEdit()
    {
        if (!_isEditing)
            return;

        int row = _editingRow;
        int col = _editingCol;
        string oldValue = Sheet[row, col];
        string newValue = _editor.Text;

        var validationResult = DataValidationEngine.Validate(Sheet, row, col, newValue);
        if (!validationResult.IsValid)
        {
            ValidationFailed?.Invoke(row, col, validationResult);
            if (validationResult.ErrorStyle == DataValidationErrorStyle.Stop)
            {
                LastInputError = validationResult.ErrorMessage;
                return;
            }
        }

        if (newValue.Contains('\n'))
            Sheet.SetCellWrapText(row, col, true);

        if (newValue != Sheet.GetEditText(row, col) && !ApplyInput(row, col, newValue)) return;
        LastInputError = null;
        _isEditing = false;
        _editor.Visibility = UiVisibility.Collapsed;
        _editor.Arrange(BRect.Empty);
        MoveChildToBack(_editor);

        newValue = Sheet[row, col];
        if (_semanticCells.TryGetValue(new CellPosition(row, col), out var semCellCommit))
            semCellCommit.UpdateValue(newValue);

        CellCommitted?.Invoke(this, new CellCommittedEventArgs(new CellPosition(row, col), oldValue, newValue));

        Session?.SetFocus(this);
        InvalidateRender();
    }

    public void SetCellValue(CellPosition position, string value, bool force = false)
    {
        int row = position.Row;
        int col = position.Column;
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Sheet.RowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(col);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(col, Sheet.ColumnCount);

        value ??= string.Empty;
        string oldValue = Sheet[row, col];
        if (!force && Sheet.GetEditText(row, col) == value)
            return;

        if (!force)
        {
            var validationResult = DataValidationEngine.Validate(Sheet, row, col, value);
            if (!validationResult.IsValid)
            {
                ValidationFailed?.Invoke(row, col, validationResult);
                if (validationResult.ErrorStyle == DataValidationErrorStyle.Stop)
                {
                    LastInputError = validationResult.ErrorMessage;
                    return;
                }
            }
        }

        if (_isEditing && _editingRow == row && _editingCol == col)
        {
            _editor.Text = value;
        }

        if (!ApplyInput(row, col, value, force)) return;
        value = Sheet[row, col];
        if (_semanticCells.TryGetValue(position, out var semCellSet))
            semCellSet.UpdateValue(value);
        CellCommitted?.Invoke(this, new CellCommittedEventArgs(position, oldValue, value));
        InvalidateRender();
    }

    public void RefreshCellDisplay(CellPosition position)
    {
        if (_semanticCells.TryGetValue(position, out var semCellRef))
            semCellRef.UpdateValue(Sheet[position.Row, position.Column]);
        Invalidate(UiInvalidationKind.Render | UiInvalidationKind.Semantic);
    }

    public void CancelEdit()
    {
        LastInputError = null;
        if (!_isEditing)
            return;

        _isEditing = false;
        _editor.Visibility = UiVisibility.Collapsed;
        _editor.Arrange(BRect.Empty);
        MoveChildToBack(_editor);
        _editor.Text = Sheet.GetEditText(_editingRow, _editingCol);
        LastInputError = null;

        Session?.SetFocus(this);
        InvalidateRender();
    }

    private void ClearCell(int row, int column)
    {
        string oldValue = Sheet[row, column];
        if (oldValue.Length == 0)
            return;

        Sheet[row, column] = string.Empty;
        if (_semanticCells.TryGetValue(new CellPosition(row, column), out var semCellClr))
            semCellClr.UpdateValue(string.Empty);
        CellCommitted?.Invoke(this, new CellCommittedEventArgs(new CellPosition(row, column), oldValue, string.Empty));
        InvalidateRender();
    }

    private void OnEditorSubmitted(object? sender, UiEditSubmittedEventArgs e) => CommitEdit();

    protected override BSize MeasureCore(BSize availableSize)
    {
        double editW = _isEditing && SelectedCell is { } pos ? GetColumnWidth(pos.Column) - 1 : DefaultCellWidth - 1;
        double editH = _isEditing && SelectedCell is { } pos2 ? GetRowHeight(pos2.Row) - 1 : DefaultCellHeight - 1;
        _editor.Measure(new BSize(Math.Max(0, editW), Math.Max(0, editH)));
        return SheetSize;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        UpdateSemanticCells();

        if (_isEditing && SelectedCell is { } pos)
        {
            BRect editBounds = CellBounds(pos.Row, pos.Column);
            if (Sheet.GetDataValidation(pos.Row, pos.Column) is { Type: DataValidationType.List, ShowDropdown: true })
            {
                double btnW = Math.Min(18, editBounds.Width);
                editBounds = new BRect(editBounds.X, editBounds.Y, Math.Max(0, editBounds.Width - btnW), editBounds.Height);
            }
            _editor.Arrange(editBounds);
        }
        else
            _editor.Arrange(BRect.Empty);
    }

    public BRect CellBounds(int row, int column)
    {
        if (Sheet.TryGetMerge(row, column, out int r0, out int c0, out int cs, out int rs))
            return CellBounds(r0, c0, cs, rs);
        return CellBounds(row, column, 1, 1);
    }

    public BRect CellBounds(int row, int column, int colSpan, int rowSpan)
    {
        colSpan = Math.Clamp(colSpan, 1, Sheet.ColumnCount - column);
        rowSpan = Math.Clamp(rowSpan, 1, Sheet.RowCount - row);
        double w = 0;
        for (int c = 0; c < colSpan; c++) w += GetColumnWidth(column + c);
        double h = 0;
        for (int r = 0; r < rowSpan; r++) h += GetRowHeight(row + r);
        return new BRect(
            Bounds.X + GetColumnOffset(column) + 1,
            Bounds.Y + GetRowOffset(row) + 1,
            Math.Max(0, w - 1),
            Math.Max(0, h - 1));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        var list = context.RenderList;
        var sheetBounds = new BRect(Bounds.X, Bounds.Y, SheetSize.Width, SheetSize.Height);
        list.PushClip(sheetBounds);
        list.FillRect(sheetBounds, _theme.Surface);

        BRect visible = GetVisibleBounds();
        double localLeft = Math.Max(0, visible.Left - Bounds.X);
        double localRight = Math.Min(TotalWidth, visible.Right - Bounds.X);
        double localTop = Math.Max(0, visible.Top - Bounds.Y);
        double localBottom = Math.Min(TotalHeight, visible.Bottom - Bounds.Y);

        int startRow, endRow, startCol, endCol;
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            startRow = 0;
            endRow = Math.Min(Sheet.RowCount - 1, 50);
            startCol = 0;
            endCol = Math.Min(Sheet.ColumnCount - 1, 20);
        }
        else
        {
            startRow = Math.Max(0, GetRowAtOffset(localTop) - 1);
            endRow = Math.Min(Sheet.RowCount - 1, GetRowAtOffset(Math.Min(TotalHeight - 0.001, localBottom)) + 1);
            startCol = Math.Max(0, GetColumnAtOffset(localLeft) - 1);
            endCol = Math.Min(Sheet.ColumnCount - 1, GetColumnAtOffset(Math.Min(TotalWidth - 0.001, localRight)) + 1);
        }

        if (Sheet.ShowGridlines)
        {
            for (int row = startRow; row <= endRow + 1 && row <= Sheet.RowCount; row++)
                list.FillRect(new BRect(Bounds.X, Bounds.Y + GetRowOffset(row), SheetSize.Width, 1), _theme.Border);

            for (int column = startCol; column <= endCol + 1 && column <= Sheet.ColumnCount; column++)
                list.FillRect(new BRect(Bounds.X + GetColumnOffset(column), Bounds.Y, 1, SheetSize.Height), _theme.Border);
        }

        for (int row = startRow; row <= endRow; row++)
        {
            if (Sheet.IsRowHidden(row))
                continue;

            for (int column = startCol; column <= endCol; column++)
            {
                RenderCellAt(list, row, column, 0, 0);
            }
        }

        foreach (var m in Sheet.Merges)
        {
            if (m.StartRow < startRow && m.EndRow >= startRow && m.StartColumn <= endCol && m.EndColumn >= startCol)
            {
                RenderCellAt(list, m.StartRow, m.StartColumn, 0, 0);
            }
        }

        // Render AutoFilter dropdown buttons on header cells
        if (Sheet.AutoFilter is { } autoFilter && !Sheet.IsRowHidden(autoFilter.Range.Start.Row))
        {
            int hRow = autoFilter.Range.Start.Row;
            int c0 = Math.Max(startCol, autoFilter.Range.Start.Column);
            int c1 = Math.Min(endCol, autoFilter.Range.End.Column);

            for (int c = c0; c <= c1; c++)
            {
                if (TryGetFilterButtonBounds(hRow, c, out var btnRect))
                {
                    RenderFilterButton(list, c, btnRect);
                }
            }
        }

        // Active cell or selection outline
        if (GetSelectedRange() is { } selRange && _selectedRows.Count == 0 && _selectedColumns.Count == 0)
        {
            BRect selBounds = CellBounds(selRange.StartRow, selRange.StartCol, selRange.EndCol - selRange.StartCol + 1, selRange.EndRow - selRange.StartRow + 1);
            list.FillRect(new BRect(selBounds.X - 1, selBounds.Y - 1, selBounds.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(selBounds.X - 1, selBounds.Bottom - 1, selBounds.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(selBounds.X - 1, selBounds.Y - 1, 2, selBounds.Height + 2), _theme.FocusRing);
            list.FillRect(new BRect(selBounds.Right - 1, selBounds.Y - 1, 2, selBounds.Height + 2), _theme.FocusRing);

            if (!_isEditing && IsEditingEnabled && TryGetFillHandleBounds(out var handleRect))
            {
                list.FillRect(handleRect, _theme.FocusRing);
            }
        }
        else if (SelectedCell is { } selected)
        {
            var selContent = Sheet.GetContent(selected.Row, selected.Column);
            BRect activeCell = CellBounds(selected.Row, selected.Column, selContent.ColumnSpan, selContent.RowSpan);
            list.FillRect(new BRect(activeCell.X - 1, activeCell.Y - 1, activeCell.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(activeCell.X - 1, activeCell.Bottom - 1, activeCell.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(activeCell.X - 1, activeCell.Y - 1, 2, activeCell.Height + 2), _theme.FocusRing);
            list.FillRect(new BRect(activeCell.Right - 1, activeCell.Y - 1, 2, activeCell.Height + 2), _theme.FocusRing);

            if (!_isEditing && IsEditingEnabled && TryGetFillHandleBounds(out var handleRect))
            {
                list.FillRect(handleRect, _theme.FocusRing);
            }
        }

        if (SelectedCell is { } activePos && Sheet.GetDataValidation(activePos.Row, activePos.Column) is { Type: DataValidationType.List, ShowDropdown: true })
        {
            var activeContent = Sheet.GetContent(activePos.Row, activePos.Column);
            BRect activeCell = CellBounds(activePos.Row, activePos.Column, activeContent.ColumnSpan, activeContent.RowSpan);
            RenderDropdownButton(list, activeCell);
        }

        // Fill drag preview outline
        if (_isFillDragging && _fillTargetRange is { } tr)
        {
            BRect targetBounds = CellBounds(tr.StartRow, tr.StartCol, tr.EndCol - tr.StartCol + 1, tr.EndRow - tr.StartRow + 1);
            list.FillRect(targetBounds, _theme.AccentSoft);
            list.FillRect(new BRect(targetBounds.X - 1, targetBounds.Y - 1, targetBounds.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(targetBounds.X - 1, targetBounds.Bottom - 1, targetBounds.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(targetBounds.X - 1, targetBounds.Y - 1, 2, targetBounds.Height + 2), _theme.FocusRing);
            list.FillRect(new BRect(targetBounds.Right - 1, targetBounds.Y - 1, 2, targetBounds.Height + 2), _theme.FocusRing);
        }

        // Pinned freeze panes rendering when scrolled
        var (scrollX, scrollY) = ScrollOffsetProvider?.Invoke() ?? (0, 0);
        int frozenRows = Sheet.FrozenRows;
        int frozenCols = Sheet.FrozenColumns;
        double frozenWidth = GetColumnOffset(frozenCols);
        double frozenHeight = GetRowOffset(frozenRows);

        if ((scrollX > 0 && frozenCols > 0) || (scrollY > 0 && frozenRows > 0))
        {
            // Pinned columns on the left
            if (scrollX > 0 && frozenCols > 0)
            {
                var pinnedColBounds = new BRect(Bounds.X + scrollX, Bounds.Y, frozenWidth, SheetSize.Height);
                list.PushClip(pinnedColBounds);
                list.FillRect(pinnedColBounds, _theme.Surface);

                if (Sheet.ShowGridlines)
                {
                    for (int r = startRow; r <= endRow + 1 && r <= Sheet.RowCount; r++)
                        list.FillRect(new BRect(Bounds.X + scrollX, Bounds.Y + GetRowOffset(r), frozenWidth, 1), _theme.Border);
                    for (int c = 0; c <= frozenCols; c++)
                        list.FillRect(new BRect(Bounds.X + scrollX + GetColumnOffset(c), Bounds.Y, 1, SheetSize.Height), _theme.Border);
                }

                for (int r = startRow; r <= endRow; r++)
                {
                    for (int c = 0; c < frozenCols; c++)
                    {
                        RenderCellAt(list, r, c, scrollX, 0);
                    }
                }
                list.PopClip();
            }

            // Pinned rows on the top
            if (scrollY > 0 && frozenRows > 0)
            {
                var pinnedRowBounds = new BRect(Bounds.X, Bounds.Y + scrollY, SheetSize.Width, frozenHeight);
                list.PushClip(pinnedRowBounds);
                list.FillRect(pinnedRowBounds, _theme.Surface);

                if (Sheet.ShowGridlines)
                {
                    for (int r = 0; r <= frozenRows; r++)
                        list.FillRect(new BRect(Bounds.X, Bounds.Y + scrollY + GetRowOffset(r), SheetSize.Width, 1), _theme.Border);
                    for (int c = startCol; c <= endCol + 1 && c <= Sheet.ColumnCount; c++)
                        list.FillRect(new BRect(Bounds.X + GetColumnOffset(c), Bounds.Y + scrollY, 1, frozenHeight), _theme.Border);
                }

                for (int r = 0; r < frozenRows; r++)
                {
                    for (int c = startCol; c <= endCol; c++)
                    {
                        RenderCellAt(list, r, c, 0, scrollY);
                    }
                }
                list.PopClip();
            }

            // Pinned top-left intersection
            if (scrollX > 0 && scrollY > 0 && frozenCols > 0 && frozenRows > 0)
            {
                var pinnedCorner = new BRect(Bounds.X + scrollX, Bounds.Y + scrollY, frozenWidth, frozenHeight);
                list.PushClip(pinnedCorner);
                list.FillRect(pinnedCorner, _theme.Surface);

                if (Sheet.ShowGridlines)
                {
                    for (int r = 0; r <= frozenRows; r++)
                        list.FillRect(new BRect(Bounds.X + scrollX, Bounds.Y + scrollY + GetRowOffset(r), frozenWidth, 1), _theme.Border);
                    for (int c = 0; c <= frozenCols; c++)
                        list.FillRect(new BRect(Bounds.X + scrollX + GetColumnOffset(c), Bounds.Y + scrollY, 1, frozenHeight), _theme.Border);
                }

                for (int r = 0; r < frozenRows; r++)
                {
                    for (int c = 0; c < frozenCols; c++)
                    {
                        RenderCellAt(list, r, c, scrollX, scrollY);
                    }
                }
                list.PopClip();
            }
        }

        // Split panes pinned rendering when scrolled
        int splitRows = Sheet.SplitRows;
        int splitCols = Sheet.SplitColumns;
        double splitWidth = GetColumnOffset(splitCols);
        double splitHeight = GetRowOffset(splitRows);

        if ((scrollX > 0 && splitCols > 0) || (scrollY > 0 && splitRows > 0))
        {
            if (scrollX > 0 && splitCols > 0)
            {
                var pinnedColBounds = new BRect(Bounds.X + scrollX, Bounds.Y, splitWidth, SheetSize.Height);
                list.PushClip(pinnedColBounds);
                list.FillRect(pinnedColBounds, _theme.Surface);

                if (Sheet.ShowGridlines)
                {
                    for (int r = startRow; r <= endRow + 1 && r <= Sheet.RowCount; r++)
                        list.FillRect(new BRect(Bounds.X + scrollX, Bounds.Y + GetRowOffset(r), splitWidth, 1), _theme.Border);
                    for (int c = 0; c <= splitCols; c++)
                        list.FillRect(new BRect(Bounds.X + scrollX + GetColumnOffset(c), Bounds.Y, 1, SheetSize.Height), _theme.Border);
                }

                for (int r = startRow; r <= endRow; r++)
                {
                    for (int c = 0; c < splitCols; c++)
                    {
                        RenderCellAt(list, r, c, scrollX, 0);
                    }
                }
                list.PopClip();
            }

            if (scrollY > 0 && splitRows > 0)
            {
                var pinnedRowBounds = new BRect(Bounds.X, Bounds.Y + scrollY, SheetSize.Width, splitHeight);
                list.PushClip(pinnedRowBounds);
                list.FillRect(pinnedRowBounds, _theme.Surface);

                if (Sheet.ShowGridlines)
                {
                    for (int r = 0; r <= splitRows; r++)
                        list.FillRect(new BRect(Bounds.X, Bounds.Y + scrollY + GetRowOffset(r), SheetSize.Width, 1), _theme.Border);
                    for (int c = startCol; c <= endCol + 1 && c <= Sheet.ColumnCount; c++)
                        list.FillRect(new BRect(Bounds.X + GetColumnOffset(c), Bounds.Y + scrollY, 1, splitHeight), _theme.Border);
                }

                for (int r = 0; r < splitRows; r++)
                {
                    for (int c = startCol; c <= endCol; c++)
                    {
                        RenderCellAt(list, r, c, 0, scrollY);
                    }
                }
                list.PopClip();
            }

            if (scrollX > 0 && scrollY > 0 && splitCols > 0 && splitRows > 0)
            {
                var pinnedCorner = new BRect(Bounds.X + scrollX, Bounds.Y + scrollY, splitWidth, splitHeight);
                list.PushClip(pinnedCorner);
                list.FillRect(pinnedCorner, _theme.Surface);

                if (Sheet.ShowGridlines)
                {
                    for (int r = 0; r <= splitRows; r++)
                        list.FillRect(new BRect(Bounds.X + scrollX, Bounds.Y + scrollY + GetRowOffset(r), splitWidth, 1), _theme.Border);
                    for (int c = 0; c <= splitCols; c++)
                        list.FillRect(new BRect(Bounds.X + scrollX + GetColumnOffset(c), Bounds.Y + scrollY, 1, splitHeight), _theme.Border);
                }

                for (int r = 0; r < splitRows; r++)
                {
                    for (int c = 0; c < splitCols; c++)
                    {
                        RenderCellAt(list, r, c, scrollX, scrollY);
                    }
                }
                list.PopClip();
            }
        }

        // Draw freeze divider lines
        if (frozenRows > 0 && frozenRows <= Sheet.RowCount)
        {
            double splitY = Bounds.Y + scrollY + frozenHeight;
            list.FillRect(new BRect(Bounds.X, splitY - 1, SheetSize.Width, 2), _theme.FocusRing);
        }
        if (frozenCols > 0 && frozenCols <= Sheet.ColumnCount)
        {
            double splitX = Bounds.X + scrollX + frozenWidth;
            list.FillRect(new BRect(splitX - 1, Bounds.Y, 2, SheetSize.Height), _theme.FocusRing);
        }

        // Draw split divider lines
        if (splitRows > 0 && splitRows <= Sheet.RowCount)
        {
            double splitBarY = Bounds.Y + scrollY + splitHeight;
            list.FillRect(new BRect(Bounds.X, splitBarY - 2, SheetSize.Width, 1), _theme.Border);
            list.FillRect(new BRect(Bounds.X, splitBarY - 1, SheetSize.Width, 2), _theme.ScrollbarTrack);
            list.FillRect(new BRect(Bounds.X, splitBarY + 1, SheetSize.Width, 1), _theme.Border);
        }
        if (splitCols > 0 && splitCols <= Sheet.ColumnCount)
        {
            double splitBarX = Bounds.X + scrollX + splitWidth;
            list.FillRect(new BRect(splitBarX - 2, Bounds.Y, 1, SheetSize.Height), _theme.Border);
            list.FillRect(new BRect(splitBarX - 1, Bounds.Y, 2, SheetSize.Height), _theme.ScrollbarTrack);
            list.FillRect(new BRect(splitBarX + 1, Bounds.Y, 1, SheetSize.Height), _theme.Border);
        }
        if (splitRows > 0 && splitRows <= Sheet.RowCount && splitCols > 0 && splitCols <= Sheet.ColumnCount)
        {
            double splitBarX = Bounds.X + scrollX + splitWidth;
            double splitBarY = Bounds.Y + scrollY + splitHeight;
            list.FillRect(new BRect(splitBarX - 3, splitBarY - 3, 6, 6), _theme.FocusRing);
        }

        base.RenderCore(context);

        if (_hoveredCell is { Row: var hr, Column: var hc } && Sheet.GetCellNote(hr, hc) is { } note)
        {
            RenderNoteTooltip(list, hr, hc, note);
        }
        else if (_hoveredCell is { Row: var hlr, Column: var hlc } && Sheet.GetCellHyperlink(hlr, hlc) is { } hyperlink)
        {
            RenderHyperlinkTooltip(list, hlr, hlc, hyperlink);
        }

        if (SelectedCell is { Row: var sr, Column: var sc } &&
            Sheet.GetDataValidation(sr, sc) is { ShowInputMessage: true } dvMsg &&
            (!string.IsNullOrWhiteSpace(dvMsg.InputTitle) || !string.IsNullOrWhiteSpace(dvMsg.InputMessage)))
        {
            RenderInputMessageTooltip(list, sr, sc, dvMsg);
        }

        list.PopClip();
    }

    public static List<string> BreakLines(string text, BFontStyle font, bool wrapText, double availableWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            result.Add(string.Empty);
            return result;
        }

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] paragraphs = normalized.Split('\n');

        foreach (string para in paragraphs)
        {
            if (!wrapText || string.IsNullOrEmpty(para))
            {
                result.Add(para);
                continue;
            }

            string[] words = para.Split(' ');
            var curLine = new System.Text.StringBuilder();

            foreach (string word in words)
            {
                if (curLine.Length == 0)
                {
                    curLine.Append(word);
                }
                else
                {
                    string candidate = curLine + " " + word;
                    if (BTextMeasurer.MeasureAdvance(candidate, font) <= availableWidth)
                    {
                        curLine.Append(' ').Append(word);
                    }
                    else
                    {
                        result.Add(curLine.ToString());
                        curLine.Clear();
                        curLine.Append(word);
                    }
                }
            }

            if (curLine.Length > 0)
                result.Add(curLine.ToString());
        }

        return result;
    }

    private static void RenderCellText(BRenderList list, BRect cell, string text, CellContent content, InlineStyle fontStyle, BFontStyle font, BColor textColor)
    {
        list.PushClip(cell);

        bool isMultilineOrWrap = content.WrapText;

        if (!isMultilineOrWrap)
        {
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            double lineHeight = BTextMeasurer.GetLineHeight(font);
            double textWidth = BTextMeasurer.MeasureAdvance(text, font);
            double textX = content.Alignment switch
            {
                Broiler.Documents.Model.TextAlignment.Right => Math.Max(cell.X + 6, cell.Right - 6 - textWidth),
                Broiler.Documents.Model.TextAlignment.Center => cell.X + Math.Max(6, (cell.Width - textWidth) / 2),
                _ => cell.X + 6,
            };
            double textY = content.VerticalAlignment switch
            {
                Broiler.Documents.Model.CellVerticalAlignment.Top => cell.Y + 2,
                Broiler.Documents.Model.CellVerticalAlignment.Bottom => Math.Max(cell.Y + 2, cell.Bottom - lineHeight - 2),
                _ => cell.Y + Math.Max(0, (cell.Height - lineHeight) / 2),
            };

            list.DrawText(new BTextRun(text, font, textColor), new BPoint(textX, textY));
            if (fontStyle.Underline)
                list.FillRect(new BRect(textX, textY + lineHeight - 1, textWidth, 1), textColor);
            if (fontStyle.Strikethrough)
                list.FillRect(new BRect(textX, textY + lineHeight / 2, textWidth, 1), textColor);
        }
        else
        {
            double availableWidth = Math.Max(12, cell.Width - 12);
            var lines = BreakLines(text, font, content.WrapText, availableWidth);
            double lineHeight = BTextMeasurer.GetLineHeight(font);
            double totalBlockHeight = lines.Count * lineHeight;

            double startY = content.VerticalAlignment switch
            {
                Broiler.Documents.Model.CellVerticalAlignment.Top => cell.Y + 2,
                Broiler.Documents.Model.CellVerticalAlignment.Bottom => Math.Max(cell.Y + 2, cell.Bottom - totalBlockHeight - 2),
                _ => cell.Y + Math.Max(2, (cell.Height - totalBlockHeight) / 2),
            };

            for (int i = 0; i < lines.Count; i++)
            {
                string lineText = lines[i].Replace('\t', ' ');
                double lineY = startY + i * lineHeight;
                if (lineY > cell.Bottom + lineHeight)
                    break;

                double lineWidth = BTextMeasurer.MeasureAdvance(lineText, font);
                double lineX = content.Alignment switch
                {
                    Broiler.Documents.Model.TextAlignment.Right => Math.Max(cell.X + 6, cell.Right - 6 - lineWidth),
                    Broiler.Documents.Model.TextAlignment.Center => cell.X + Math.Max(6, (cell.Width - lineWidth) / 2),
                    _ => cell.X + 6,
                };

                list.DrawText(new BTextRun(lineText, font, textColor), new BPoint(lineX, lineY));
                if (fontStyle.Underline)
                    list.FillRect(new BRect(lineX, lineY + lineHeight - 1, lineWidth, 1), textColor);
                if (fontStyle.Strikethrough)
                    list.FillRect(new BRect(lineX, lineY + lineHeight / 2, lineWidth, 1), textColor);
            }
        }

        list.PopClip();
    }

    private void RenderCellAt(BRenderList list, int row, int column, double xOffset, double yOffset)
    {
        if (Sheet.IsCoveredCell(row, column) || Sheet.IsRowHidden(row) || Sheet.IsColumnHidden(column))
            return;

        var content = Sheet.GetContent(row, column);
        BRect baseCell = CellBounds(row, column, content.ColumnSpan, content.RowSpan);
        BRect cell = new BRect(baseCell.X + xOffset, baseCell.Y + yOffset, baseCell.Width, baseCell.Height);

        var condStyle = ConditionalFormatEngine.Evaluate(Sheet, row, column);

        var effectiveShading = (condStyle?.Shading is { } cs && cs.A > 0) ? cs : (content.Shading.A > 0 ? content.Shading : default);
        if (effectiveShading.A > 0)
            list.FillRect(cell, effectiveShading);
        else if (content.ColumnSpan > 1 || content.RowSpan > 1)
            list.FillRect(cell, _theme.Surface);

        if (IsCellSelected(row, column))
            list.FillRect(cell, _theme.AccentSoft);

        var borders = condStyle?.Borders ?? content.Borders;
        if (borders.Top.IsVisible)
        {
            double w = Math.Max(1, Math.Round(borders.Top.Width));
            list.FillRect(new BRect(cell.X, cell.Y, cell.Width, w), borders.Top.Color);
        }
        if (borders.Bottom.IsVisible)
        {
            double w = Math.Max(1, Math.Round(borders.Bottom.Width));
            list.FillRect(new BRect(cell.X, cell.Bottom - w, cell.Width, w), borders.Bottom.Color);
        }
        if (borders.Left.IsVisible)
        {
            double w = Math.Max(1, Math.Round(borders.Left.Width));
            list.FillRect(new BRect(cell.X, cell.Y, w, cell.Height), borders.Left.Color);
        }
        if (borders.Right.IsVisible)
        {
            double w = Math.Max(1, Math.Round(borders.Right.Width));
            list.FillRect(new BRect(cell.Right - w, cell.Y, w, cell.Height), borders.Right.Color);
        }

        if (content.Note is not null)
        {
            double indicatorSize = 6;
            double trX = cell.Right;
            double trY = cell.Y;
            list.FillTriangle(
                new BPoint(trX, trY),
                new BPoint(trX - indicatorSize, trY),
                new BPoint(trX, trY + indicatorSize),
                new BColor(218, 41, 28));
        }

        if (_isEditing && _editingRow == row && _editingCol == column)
            return;

        string text = Sheet[row, column];
        if (text.Length == 0)
            return;

        var effectiveFont = content.Font;
        if (condStyle is not null)
        {
            effectiveFont = effectiveFont with
            {
                Bold = condStyle.Bold ?? effectiveFont.Bold,
                Italic = condStyle.Italic ?? effectiveFont.Italic,
                Underline = condStyle.Underline ?? effectiveFont.Underline,
                Strikethrough = condStyle.Strikethrough ?? effectiveFont.Strikethrough,
                Foreground = (condStyle.Foreground.HasValue && condStyle.Foreground.Value.A > 0) ? condStyle.Foreground.Value : effectiveFont.Foreground
            };
        }
        if (content.Hyperlink is not null)
        {
            effectiveFont = effectiveFont with
            {
                Underline = true,
                Foreground = effectiveFont.Foreground.IsEmpty || effectiveFont.Foreground.A == 0
                    ? new BColor(17, 85, 204)
                    : effectiveFont.Foreground
            };
        }

        var font = ResolveFont(effectiveFont);
        var textColor = IsCellSelected(row, column)
            ? _theme.SelectionText
            : ((condStyle?.Foreground is { } cfg && cfg.A > 0) ? cfg : (content.FormattedColor ?? (!effectiveFont.Foreground.IsEmpty && effectiveFont.Foreground.A > 0 ? effectiveFont.Foreground : _theme.Text)));

        RenderCellText(list, cell, text, content, effectiveFont, font, textColor);
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (input.Kind == UiInputEventKind.PointerMove)
        {
            if (_isDraggingSplitter)
            {
                if (_draggingSplitterKind is SplitterKind.Horizontal or SplitterKind.Both)
                {
                    double localY = Math.Max(0, input.Position.Y - Bounds.Y);
                    int newRow = GetRowAtOffset(localY);
                    Sheet.SplitRows = newRow;
                }
                if (_draggingSplitterKind is SplitterKind.Vertical or SplitterKind.Both)
                {
                    double localX = Math.Max(0, input.Position.X - Bounds.X);
                    int newCol = GetColumnAtOffset(localX);
                    Sheet.SplitColumns = newCol;
                }
                var dragCursor = _draggingSplitterKind switch
                {
                    SplitterKind.Both => UiCursorShape.Move,
                    SplitterKind.Horizontal => UiCursorShape.ResizeVertical,
                    SplitterKind.Vertical => UiCursorShape.ResizeHorizontal,
                    _ => UiCursorShape.Arrow
                };
                (Session?.Host as IUiCursorHost)?.SetCursor(dragCursor);
                InvalidateRender();
                return true;
            }

            var hitSplitter = HitTestSplitter(input.Position);
            if (hitSplitter != SplitterKind.None && !_isFillDragging && !_isSelectingCells)
            {
                _hoveredSplitter = hitSplitter;
                var hoverCursor = hitSplitter switch
                {
                    SplitterKind.Both => UiCursorShape.Move,
                    SplitterKind.Horizontal => UiCursorShape.ResizeVertical,
                    SplitterKind.Vertical => UiCursorShape.ResizeHorizontal,
                    _ => UiCursorShape.Arrow
                };
                (Session?.Host as IUiCursorHost)?.SetCursor(hoverCursor);
                return true;
            }
            _hoveredSplitter = SplitterKind.None;

            if (_isFillDragging)
            {
                (Session?.Host as IUiCursorHost)?.SetCursor(UiCursorShape.Move);
                UpdateFillDrag(input.Position);
                return true;
            }

            if (!_isEditing && IsEditingEnabled && TryGetFillHandleBounds(out var handleRect))
            {
                BRect hitArea = new BRect(handleRect.X - 4, handleRect.Y - 4, handleRect.Width + 8, handleRect.Height + 8);
                if (hitArea.Contains(input.Position))
                {
                    (Session?.Host as IUiCursorHost)?.SetCursor(UiCursorShape.Move);
                    return true;
                }
            }

            if (Sheet.AutoFilter is { } af && !_isEditing)
            {
                int filterRow = af.Range.Start.Row;
                for (int c = af.Range.Start.Column; c <= af.Range.End.Column; c++)
                {
                    if (TryGetFilterButtonBounds(filterRow, c, out var btnRect) && btnRect.Contains(input.Position))
                    {
                        (Session?.Host as IUiCursorHost)?.SetCursor(UiCursorShape.Hand);
                        return true;
                    }
                }
            }

            (Session?.Host as IUiCursorHost)?.SetCursor(UiCursorShape.Arrow);
            if (_isSelectingCells && IsEditingEnabled)
            {
                if (Session?.CapturedElement != this)
                {
                    EndCellSelectionDrag();
                    return false;
                }
                UpdateCellSelectionDrag(input.Position);
                return true;
            }

            if (TryGetCellAt(input.Position, out int hRow, out int hCol))
            {
                var newHover = new CellPosition(hRow, hCol);
                if (_hoveredCell != newHover)
                {
                    _hoveredCell = newHover;
                    InvalidateRender();
                    HoveredCellChanged?.Invoke(this, _hoveredCell);
                }
                if (Sheet.HasCellHyperlink(hRow, hCol))
                {
                    (Session?.Host as IUiCursorHost)?.SetCursor(UiCursorShape.Hand);
                }
            }
            else if (_hoveredCell is not null)
            {
                _hoveredCell = null;
                InvalidateRender();
                HoveredCellChanged?.Invoke(this, null);
            }

            return false;
        }

        if (!IsEditingEnabled)
            return false;

        return input.Kind switch
        {
            UiInputEventKind.PointerButton => HandlePointerButton(input),
            UiInputEventKind.KeyboardKey => HandleKeyboardKey(input),
            UiInputEventKind.TextInput => HandleTextInput(input),
            _ => false,
        };
    }

    private bool HandlePointerButton(UiInputEvent input)
    {
        if (input.MouseButton != MouseButton.Left)
            return false;

        if (input.MouseButtonTransition == MouseButtonTransition.Down)
        {
            var hitSplitter = HitTestSplitter(input.Position);
            if (hitSplitter != SplitterKind.None && !_isEditing)
            {
                if (IsSplitterDoubleClick(input.Position))
                {
                    if (hitSplitter == SplitterKind.Both)
                        Sheet.RemoveSplit();
                    else if (hitSplitter == SplitterKind.Horizontal)
                        Sheet.SetSplit(0, Sheet.SplitColumns);
                    else if (hitSplitter == SplitterKind.Vertical)
                        Sheet.SetSplit(Sheet.SplitRows, 0);
                    InvalidateRender();
                    return true;
                }

                _isDraggingSplitter = true;
                _draggingSplitterKind = hitSplitter;
                Session?.CaptureInput(this);
                InvalidateRender();
                return true;
            }

            if (Sheet.AutoFilter is { } af && !_isEditing)
            {
                int hRow = af.Range.Start.Row;
                for (int c = af.Range.Start.Column; c <= af.Range.End.Column; c++)
                {
                    if (TryGetFilterButtonBounds(hRow, c, out var btnRect))
                    {
                        if (btnRect.Contains(input.Position))
                        {
                            FilterDropdownRequested?.Invoke(c, btnRect);
                            return true;
                        }
                    }
                }
            }

            if (!_isEditing && IsEditingEnabled && TryGetFillHandleBounds(out var handleRect))
            {
                BRect hitArea = new BRect(handleRect.X - 4, handleRect.Y - 4, handleRect.Width + 8, handleRect.Height + 8);
                if (hitArea.Contains(input.Position))
                {
                    if (GetSelectedRange() is { } sel)
                    {
                        var srcRange = new CellRangeAddress(new CellAddress(sel.StartRow, sel.StartCol), new CellAddress(sel.EndRow, sel.EndCol));
                        if (IsFillHandleDoubleClick(input.Position))
                        {
                            if (AutoFillEngine.QuickFillDown(Sheet, srcRange) is { } targetRange)
                            {
                                _selectedRows.Clear();
                                _selectedColumns.Clear();
                                _cellSelectionAnchor = new CellPosition(targetRange.StartRow, targetRange.StartCol);
                                SelectCell(new CellPosition(targetRange.EndRow, targetRange.EndCol), extend: true);
                                AutoFillCompleted?.Invoke(srcRange, targetRange);
                                InvalidateRender();
                            }
                            return true;
                        }

                        _isFillDragging = true;
                        _fillSourceRange = srcRange;
                        _fillTargetRange = srcRange;
                        Session?.CaptureInput(this);
                        InvalidateRender();
                        return true;
                    }
                }
            }

            if (!TryGetCellAt(input.Position, out int row, out int col))
                return false;

            var clicked = new CellPosition(row, col);
            bool extend = input.KeyModifiers.HasFlag(KeyboardModifierState.Shift);
            _pointerDownCell = clicked;
            _pointerDownPos = input.Position;

            var content = Sheet.GetContent(row, col);
            BRect cellBounds = CellBounds(row, col, content.ColumnSpan, content.RowSpan);
            if (Sheet.GetDataValidation(row, col) is { Type: DataValidationType.List, ShowDropdown: true } dv)
            {
                BRect btnBounds = GetDropdownButtonBounds(cellBounds);
                if (btnBounds.Contains(input.Position))
                {
                    if (_isEditing && SelectedCell != clicked)
                        CommitEdit();
                    SelectCell(clicked);
                    Session?.SetFocus(this);
                    OpenDropdownRequested?.Invoke(row, col, dv, btnBounds);
                    return true;
                }
            }

            if (_isEditing)
            {
                if (clicked == SelectedCell)
                    return true;
                CommitEdit();
                _hasClicked = false;
            }

            bool isDouble = !extend && IsDoubleClick(input.Position) && SelectedCell == clicked;
            if (extend)
                _hasClicked = false;
            else
                UpdateClickState(input.Position, clicked);

            if (isDouble)
            {
                SelectCell(clicked);
                StartEditing(Sheet.GetEditText(clicked.Row, clicked.Column));
                return true;
            }

            SelectCell(clicked, extend);
            Session?.SetFocus(this);
            _isSelectingCells = true;
            _lastSelectionPointer = input.Position;
            Session?.CaptureInput(this);
            return true;
        }

        if (input.MouseButtonTransition == MouseButtonTransition.Up)
        {
            if (_isDraggingSplitter)
            {
                Session?.ReleaseInputCapture(this);
                _isDraggingSplitter = false;
                _draggingSplitterKind = SplitterKind.None;
                InvalidateRender();
                return true;
            }

            if (_isFillDragging)
            {
                Session?.ReleaseInputCapture(this);
                _isFillDragging = false;
                if (_fillSourceRange is { } src && _fillTargetRange is { } dst &&
                    (dst.EndRow > src.EndRow || dst.EndCol > src.EndCol || dst.StartRow < src.StartRow || dst.StartCol < src.StartCol))
                {
                    bool filled = AutoFillEngine.Fill(Sheet, src, dst, AutoFillMode.Default);
                    if (filled)
                    {
                        _selectedRows.Clear();
                        _selectedColumns.Clear();
                        _cellSelectionAnchor = new CellPosition(dst.StartRow, dst.StartCol);
                        SelectCell(new CellPosition(dst.EndRow, dst.EndCol), extend: true);
                        AutoFillCompleted?.Invoke(src, dst);
                    }
                }
                _fillSourceRange = null;
                _fillTargetRange = null;
                InvalidateRender();
                return true;
            }

            if (_isSelectingCells)
            {
                UpdateCellSelectionDrag(input.Position);
                EndCellSelectionDrag();

                if (!input.KeyModifiers.HasFlag(KeyboardModifierState.Shift) &&
                    _pointerDownCell is { Row: var downRow, Column: var downCol } &&
                    Math.Abs(input.Position.X - _pointerDownPos.X) <= 4 &&
                    Math.Abs(input.Position.Y - _pointerDownPos.Y) <= 4 &&
                    TryGetCellAt(input.Position, out int upRow, out int upCol) &&
                    downRow == upRow && downCol == upCol &&
                    Sheet.GetCellHyperlink(upRow, upCol) is { } hl)
                {
                    HyperlinkClicked?.Invoke(upRow, upCol, hl);
                }

                _pointerDownCell = null;
                return true;
            }
            double localX = input.Position.X - Bounds.X;
            double localY = input.Position.Y - Bounds.Y;

            if (localX >= 0 && localX < TotalWidth && localY >= 0 && localY < TotalHeight)
                return true;
        }

        return false;
    }

    private void UpdateCellSelectionDrag(BPoint pointer)
    {
        // Scrolling can move the grid during selection; a stationary release
        // must not reinterpret the same screen position as another cell.
        if (pointer == _lastSelectionPointer)
            return;
        _lastSelectionPointer = pointer;
        var (scrollX, scrollY) = ScrollOffsetProvider?.Invoke() ?? (0, 0);
        int frozenCols = Math.Max(Sheet.FrozenColumns, Sheet.SplitColumns);
        int frozenRows = Math.Max(Sheet.FrozenRows, Sheet.SplitRows);
        double frozenWidth = GetColumnOffset(frozenCols);
        double frozenHeight = GetRowOffset(frozenRows);

        double viewX = pointer.X - Bounds.X - scrollX;
        double viewY = pointer.Y - Bounds.Y - scrollY;

        double localX = (frozenCols > 0 && viewX >= 0 && viewX < frozenWidth)
            ? viewX
            : Math.Clamp(pointer.X - Bounds.X, 0, TotalWidth - 0.001);

        double localY = (frozenRows > 0 && viewY >= 0 && viewY < frozenHeight)
            ? viewY
            : Math.Clamp(pointer.Y - Bounds.Y, 0, TotalHeight - 0.001);

        int row = GetRowAtOffset(localY);
        int column = GetColumnAtOffset(localX);
        if (Sheet.TryGetMerge(row, column, out int r0, out int c0, out _, out _))
        {
            row = r0;
            column = c0;
        }
        var position = new CellPosition(row, column);
        if (SelectedCell != position)
        {
            _hasClicked = false;
            SelectCell(position, extend: true);
        }
    }

    private void EndCellSelectionDrag()
    {
        _isSelectingCells = false;
        Session?.ReleaseInputCapture(this);
    }

    private bool HandleKeyboardKey(UiInputEvent input)
    {
        if (input.KeyTransition != KeyboardKeyTransition.Down)
            return false;

        if (!_isEditing && input.KeyModifiers.HasFlag(KeyboardModifierState.Control) && !input.KeyModifiers.HasFlag(KeyboardModifierState.Alt))
        {
            if (IsKey(input, 0x44, "D"))
            {
                if (FillDown()) return true;
            }
            else if (IsKey(input, 0x52, "R"))
            {
                if (FillRight()) return true;
            }
        }

        if ((input.KeyModifiers.HasFlag(KeyboardModifierState.Alt) && IsKey(input, 0x28, "Down")) ||
            IsKey(input, 0x73, "F4"))
        {
            if (SelectedCell is { } sel && Sheet.GetDataValidation(sel.Row, sel.Column) is { Type: DataValidationType.List, ShowDropdown: true } dv)
            {
                var content = Sheet.GetContent(sel.Row, sel.Column);
                BRect cellBounds = CellBounds(sel.Row, sel.Column, content.ColumnSpan, content.RowSpan);
                BRect btnBounds = GetDropdownButtonBounds(cellBounds);
                OpenDropdownRequested?.Invoke(sel.Row, sel.Column, dv, btnBounds);
                return true;
            }

            if (SelectedCell is { } selCell && Sheet.AutoFilter is { } af &&
                selCell.Row == af.Range.Start.Row && selCell.Column >= af.Range.Start.Column && selCell.Column <= af.Range.End.Column)
            {
                if (TryGetFilterButtonBounds(selCell.Row, selCell.Column, out var btnBounds))
                {
                    FilterDropdownRequested?.Invoke(selCell.Column, btnBounds);
                    return true;
                }
            }
        }

        if (_isEditing)
        {
            if (IsKey(input, 0x1B, "Escape"))
            {
                CancelEdit();
                return true;
            }

            if (IsKey(input, 0x0D, "Enter"))
            {
                if (input.KeyModifiers.HasFlag(KeyboardModifierState.Alt))
                {
                    int selStart = _editor.SelectionStart;
                    int selLength = _editor.SelectionLength;
                    string curText = _editor.Text;
                    string updated = (selStart >= 0 && selStart <= curText.Length)
                        ? curText.Remove(selStart, selLength).Insert(selStart, "\n")
                        : curText + "\n";
                    _editor.Text = updated;
                    _editor.SetSelection(selStart + 1, 0);
                    return true;
                }

                CommitEdit();
                return true;
            }

            return false;
        }

        if (IsKey(input, 0x71, "F2"))
        {
            if (SelectedCell is { } selected)
            {
                StartEditing(Sheet.GetEditText(selected.Row, selected.Column));
                return true;
            }
            return false;
        }

        if (IsKey(input, 0x2E, "Delete"))
        {
            if (SelectedCell is not null)
            {
                for (int row = 0; row < Sheet.RowCount; row++)
                    for (int column = 0; column < Sheet.ColumnCount; column++)
                        if (IsCellSelected(row, column))
                            ClearCell(row, column);
                return true;
            }
            return false;
        }

        if (IsKey(input, 0x25, "Left"))
        {
            Navigate(0, -1);
            return true;
        }
        if (IsKey(input, 0x27, "Right"))
        {
            Navigate(0, 1);
            return true;
        }
        if (IsKey(input, 0x26, "Up"))
        {
            Navigate(-1, 0);
            return true;
        }
        if (IsKey(input, 0x28, "Down"))
        {
            Navigate(1, 0);
            return true;
        }

        return false;
    }

    private bool HandleTextInput(UiInputEvent input)
    {
        if (_isEditing)
            return false;

        if (string.IsNullOrEmpty(input.Text) || char.IsControl(input.Text[0]))
            return false;

        if (SelectedCell is not null)
        {
            StartEditing(input.Text);
            return true;
        }

        return false;
    }

    public void Navigate(int deltaRow, int deltaColumn)
    {
        CellPosition current = SelectedCell ?? new CellPosition(0, 0);
        int row = current.Row;
        int col = current.Column;

        if (Sheet.TryGetMerge(row, col, out int r0, out int c0, out int cs, out int rs))
        {
            row = r0;
            col = c0;
            if (deltaColumn > 0) col += cs;
            else if (deltaColumn < 0) col -= 1;
            if (deltaRow > 0) row += rs;
            else if (deltaRow < 0) row -= 1;
        }
        else
        {
            row += deltaRow;
            col += deltaColumn;
        }

        if (deltaRow > 0)
        {
            while (row < Sheet.RowCount && Sheet.IsRowHidden(row))
                row++;
        }
        else if (deltaRow < 0)
        {
            while (row >= 0 && Sheet.IsRowHidden(row))
                row--;
        }

        if (deltaColumn > 0)
        {
            while (col < Sheet.ColumnCount && Sheet.IsColumnHidden(col))
                col++;
        }
        else if (deltaColumn < 0)
        {
            while (col >= 0 && Sheet.IsColumnHidden(col))
                col--;
        }

        row = Math.Clamp(row, 0, Sheet.RowCount - 1);
        col = Math.Clamp(col, 0, Sheet.ColumnCount - 1);
        if (!Sheet.IsRowHidden(row) && !Sheet.IsColumnHidden(col))
            SelectCell(new CellPosition(row, col));
    }

    private bool IsDoubleClick(BPoint point)
    {
        if (!_hasClicked || Session is null)
            return false;

        TimeSpan delta = Session.Clock.Now.Elapsed - _lastClickTime.Elapsed;
        bool quick = delta >= TimeSpan.Zero && delta <= DoubleClickWindow;
        bool near = Math.Abs(point.X - _lastClickPosition.X) <= DoubleClickSlop &&
                    Math.Abs(point.Y - _lastClickPosition.Y) <= DoubleClickSlop;
        return quick && near;
    }

    private void UpdateClickState(BPoint point, CellPosition cell)
    {
        _lastClickTime = Session?.Clock.Now ?? default;
        _lastClickPosition = point;
        _lastClickCell = cell;
        _hasClicked = true;
    }

    public bool TryGetFillHandleBounds(out BRect bounds)
    {
        bounds = default;
        if (_isEditing || !IsEditingEnabled)
            return false;

        if (GetSelectedRange() is not { } sel)
            return false;

        if (_selectedRows.Count > 0 || _selectedColumns.Count > 0)
            return false;

        BRect selBounds = CellBounds(sel.StartRow, sel.StartCol, sel.EndCol - sel.StartCol + 1, sel.EndRow - sel.StartRow + 1);
        bounds = new BRect(selBounds.Right - 3, selBounds.Bottom - 3, 6, 6);
        return true;
    }

    private void UpdateFillDrag(BPoint pointer)
    {
        if (_fillSourceRange is not { } src) return;

        double localX = Math.Max(0, pointer.X - Bounds.X);
        double localY = Math.Max(0, pointer.Y - Bounds.Y);
        int pr = Math.Clamp(GetRowAtOffset(localY), 0, Sheet.RowCount - 1);
        int pc = Math.Clamp(GetColumnAtOffset(localX), 0, Sheet.ColumnCount - 1);

        int deltaDown = pr - src.EndRow;
        int deltaRight = pc - src.EndCol;
        int deltaUp = src.StartRow - pr;
        int deltaLeft = src.StartCol - pc;

        CellRangeAddress newTarget;
        if (deltaDown > 0 && deltaDown >= deltaRight && deltaDown >= deltaLeft)
        {
            newTarget = new CellRangeAddress(src.Start, new CellAddress(pr, src.EndCol));
        }
        else if (deltaRight > 0 && deltaRight >= deltaDown && deltaRight >= deltaUp)
        {
            newTarget = new CellRangeAddress(src.Start, new CellAddress(src.EndRow, pc));
        }
        else if (deltaUp > 0 && deltaUp >= deltaLeft && deltaUp >= deltaRight)
        {
            newTarget = new CellRangeAddress(new CellAddress(pr, src.StartCol), src.End);
        }
        else if (deltaLeft > 0 && deltaLeft >= deltaUp && deltaLeft >= deltaDown)
        {
            newTarget = new CellRangeAddress(new CellAddress(src.StartRow, pc), src.End);
        }
        else
        {
            newTarget = src;
        }

        if (_fillTargetRange != newTarget)
        {
            _fillTargetRange = newTarget;
            InvalidateRender();
        }
    }

    private bool IsFillHandleDoubleClick(BPoint point)
    {
        if (Session is null) return false;
        TimeSpan delta = Session.Clock.Now.Elapsed - _lastFillHandleClickTime.Elapsed;
        bool quick = delta >= TimeSpan.Zero && delta <= DoubleClickWindow;
        bool near = Math.Abs(point.X - _lastFillHandleClickPosition.X) <= DoubleClickSlop &&
                    Math.Abs(point.Y - _lastFillHandleClickPosition.Y) <= DoubleClickSlop;
        if (quick && near)
        {
            _lastFillHandleClickTime = default;
            return true;
        }
        _lastFillHandleClickTime = Session.Clock.Now;
        _lastFillHandleClickPosition = point;
        return false;
    }

    public SplitterKind HitTestSplitter(BPoint pointer)
    {
        if (!Sheet.HasSplit) return SplitterKind.None;

        var (scrollX, scrollY) = ScrollOffsetProvider?.Invoke() ?? (0, 0);
        bool hasH = Sheet.SplitRows > 0;
        bool hasV = Sheet.SplitColumns > 0;

        double splitY = hasH ? Bounds.Y + scrollY + GetRowOffset(Sheet.SplitRows) : -1000;
        double splitX = hasV ? Bounds.X + scrollX + GetColumnOffset(Sheet.SplitColumns) : -1000;

        const double hitTolerance = 4.0;
        bool hitH = hasH && Math.Abs(pointer.Y - splitY) <= hitTolerance;
        bool hitV = hasV && Math.Abs(pointer.X - splitX) <= hitTolerance;

        if (hitH && hitV) return SplitterKind.Both;
        if (hitH) return SplitterKind.Horizontal;
        if (hitV) return SplitterKind.Vertical;
        return SplitterKind.None;
    }

    private bool IsSplitterDoubleClick(BPoint point)
    {
        if (Session is null) return false;
        TimeSpan delta = Session.Clock.Now.Elapsed - _lastSplitterClickTime.Elapsed;
        bool quick = delta >= TimeSpan.Zero && delta <= DoubleClickWindow;
        bool near = Math.Abs(point.X - _lastSplitterClickPosition.X) <= DoubleClickSlop &&
                    Math.Abs(point.Y - _lastSplitterClickPosition.Y) <= DoubleClickSlop;
        if (quick && near)
        {
            _lastSplitterClickTime = default;
            return true;
        }
        _lastSplitterClickTime = Session.Clock.Now;
        _lastSplitterClickPosition = point;
        return false;
    }

    public bool FillDown()
    {
        if (GetSelectedRange() is not { } sel) return false;
        var range = new CellRangeAddress(new CellAddress(sel.StartRow, sel.StartCol), new CellAddress(sel.EndRow, sel.EndCol));
        if (range.StartRow == range.EndRow)
        {
            if (range.StartRow > 0)
            {
                var src = new CellRangeAddress(new CellAddress(range.StartRow - 1, range.StartCol), new CellAddress(range.StartRow - 1, range.EndCol));
                if (AutoFillEngine.Fill(Sheet, src, range, AutoFillMode.Default))
                {
                    AutoFillCompleted?.Invoke(src, range);
                    InvalidateRender();
                    return true;
                }
            }
        }
        else
        {
            var src = new CellRangeAddress(new CellAddress(range.StartRow, range.StartCol), new CellAddress(range.StartRow, range.EndCol));
            if (AutoFillEngine.Fill(Sheet, src, range, AutoFillMode.Default))
            {
                AutoFillCompleted?.Invoke(src, range);
                InvalidateRender();
                return true;
            }
        }
        return false;
    }

    public bool FillRight()
    {
        if (GetSelectedRange() is not { } sel) return false;
        var range = new CellRangeAddress(new CellAddress(sel.StartRow, sel.StartCol), new CellAddress(sel.EndRow, sel.EndCol));
        if (range.StartCol == range.EndCol)
        {
            if (range.StartCol > 0)
            {
                var src = new CellRangeAddress(new CellAddress(range.StartRow, range.StartCol - 1), new CellAddress(range.EndRow, range.StartCol - 1));
                if (AutoFillEngine.Fill(Sheet, src, range, AutoFillMode.Default))
                {
                    AutoFillCompleted?.Invoke(src, range);
                    InvalidateRender();
                    return true;
                }
            }
        }
        else
        {
            var src = new CellRangeAddress(new CellAddress(range.StartRow, range.StartCol), new CellAddress(range.EndRow, range.StartCol));
            if (AutoFillEngine.Fill(Sheet, src, range, AutoFillMode.Default))
            {
                AutoFillCompleted?.Invoke(src, range);
                InvalidateRender();
                return true;
            }
        }
        return false;
    }

    public bool QuickFillDown()
    {
        if (GetSelectedRange() is not { } sel) return false;
        var srcRange = new CellRangeAddress(new CellAddress(sel.StartRow, sel.StartCol), new CellAddress(sel.EndRow, sel.EndCol));
        if (AutoFillEngine.QuickFillDown(Sheet, srcRange) is { } targetRange)
        {
            _selectedRows.Clear();
            _selectedColumns.Clear();
            _cellSelectionAnchor = new CellPosition(targetRange.StartRow, targetRange.StartCol);
            SelectCell(new CellPosition(targetRange.EndRow, targetRange.EndCol), extend: true);
            AutoFillCompleted?.Invoke(srcRange, targetRange);
            InvalidateRender();
            return true;
        }
        return false;
    }

    public bool AutoFill(CellRangeAddress source, CellRangeAddress target, AutoFillMode mode = AutoFillMode.Default)
    {
        if (AutoFillEngine.Fill(Sheet, source, target, mode))
        {
            AutoFillCompleted?.Invoke(source, target);
            InvalidateRender();
            return true;
        }
        return false;
    }

    public bool TryGetFilterButtonBounds(int row, int column, out BRect buttonRect, double xOffset = 0, double yOffset = 0)
    {
        buttonRect = default;
        if (Sheet.AutoFilter is not { } af)
            return false;

        int headerRow = af.Range.Start.Row;
        if (row != headerRow)
            return false;

        if (column < af.Range.Start.Column || column > af.Range.End.Column)
            return false;

        if (Sheet.IsRowHidden(row))
            return false;

        BRect cellBounds = CellBounds(row, column);
        if (cellBounds.Width < 22 || cellBounds.Height < 18)
            return false;

        double btnW = 16;
        double btnH = 16;
        double btnX = cellBounds.Right + xOffset - btnW - 2;
        double btnY = cellBounds.Y + yOffset + (cellBounds.Height - btnH) / 2;
        buttonRect = new BRect(btnX, btnY, btnW, btnH);
        return true;
    }

    private void RenderFilterButton(BRenderList list, int column, BRect btnRect)
    {
        list.FillRect(btnRect, _theme.Surface);
        list.StrokeRect(btnRect, _theme.Border, 1.0);

        bool hasActiveFilter = Sheet.AutoFilter?.GetColumn(column) is { Values.Count: > 0 } ||
                              (Sheet.AutoFilter?.GetColumn(column) is { IncludeBlanks: false });

        double cx = btnRect.X + btnRect.Width / 2.0;
        double cy = btnRect.Y + btnRect.Height / 2.0;

        if (hasActiveFilter)
        {
            var accent = _theme.Accent;
            list.FillRect(new BRect(cx - 4, cy - 3, 8, 2), accent);
            list.FillTriangle(new BPoint(cx - 4, cy - 3), new BPoint(cx + 4, cy - 3), new BPoint(cx, cy + 1), accent);
            list.FillRect(new BRect(cx - 1, cy, 2, 4), accent);
        }
        else
        {
            var muted = _theme.TextMuted;
            list.FillTriangle(new BPoint(cx - 3, cy - 1), new BPoint(cx + 3, cy - 1), new BPoint(cx, cy + 2), muted);
        }
    }

    public void ToggleAutoFilter()
    {
        if (Sheet.AutoFilter is not null)
        {
            Sheet.RemoveAutoFilter();
        }
        else
        {
            var sel = GetSelectedRange();
            CellRangeAddress range;
            if (sel is { } s && (s.EndRow > s.StartRow || s.EndCol > s.StartCol))
            {
                range = new CellRangeAddress(new CellAddress(s.StartRow, s.StartCol), new CellAddress(s.EndRow, s.EndCol));
            }
            else
            {
                var pos = SelectedCell ?? new CellPosition(0, 0);
                range = Sheet.GetDataRegion(pos);
            }
            Sheet.SetAutoFilter(range);
        }
    }

    public void QuickSort(int columnIndex, bool ascending)
    {
        var sel = GetSelectedRange();
        CellRangeAddress? range = null;
        if (sel is { } s && (s.EndRow > s.StartRow || s.EndCol > s.StartCol))
        {
            range = new CellRangeAddress(new CellAddress(s.StartRow, s.StartCol), new CellAddress(s.EndRow, s.EndCol));
        }
        TableSortEngine.QuickSort(Sheet, range, columnIndex, ascending);
    }

    private static bool IsKey(UiInputEvent input, int nativeKeyCode, string name) =>
        input.NativeKeyCode == nativeKeyCode ||
        string.Equals(input.KeyName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(input.KeyName, "VirtualKey:" + nativeKeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
    {
        Role = UiSemanticRole.Group,
        State = UiSemanticState.Visible | UiSemanticState.Enabled | (_isEditingEnabled ? UiSemanticState.None : UiSemanticState.ReadOnly)
    };


    private void UpdateSemanticCells()
    {
        BRect visible = GetVisibleBounds();
        double localLeft = Math.Max(0, visible.Left - Bounds.X);
        double localRight = Math.Min(TotalWidth, visible.Right - Bounds.X);
        double localTop = Math.Max(0, visible.Top - Bounds.Y);
        double localBottom = Math.Min(TotalHeight, visible.Bottom - Bounds.Y);

        int startRow, endRow, startCol, endCol;
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            startRow = 0;
            endRow = Math.Min(Sheet.RowCount - 1, 50);
            startCol = 0;
            endCol = Math.Min(Sheet.ColumnCount - 1, 20);
        }
        else
        {
            startRow = Math.Max(0, GetRowAtOffset(localTop) - 1);
            endRow = Math.Min(Sheet.RowCount - 1, GetRowAtOffset(Math.Min(TotalHeight - 0.001, localBottom)) + 1);
            startCol = Math.Max(0, GetColumnAtOffset(localLeft) - 1);
            endCol = Math.Min(Sheet.ColumnCount - 1, GetColumnAtOffset(Math.Min(TotalWidth - 0.001, localRight)) + 1);
        }

        var needed = new HashSet<CellPosition>();
        for (int r = startRow; r <= endRow; r++)
        {
            for (int c = startCol; c <= endCol; c++)
            {
                needed.Add(new CellPosition(r, c));
            }
        }

        if (SelectedCell is { } sel && sel.Row >= 0 && sel.Row < Sheet.RowCount && sel.Column >= 0 && sel.Column < Sheet.ColumnCount)
        {
            needed.Add(sel);
        }

        List<CellPosition>? toRemove = null;
        foreach (var kvp in _semanticCells)
        {
            if (!needed.Contains(kvp.Key))
            {
                toRemove ??= [];
                toRemove.Add(kvp.Key);
                RemoveChild(kvp.Value);
            }
        }
        if (toRemove != null)
        {
            foreach (var pos in toRemove)
                _semanticCells.Remove(pos);
        }

        for (int r = startRow; r <= endRow; r++)
        {
            for (int c = startCol; c <= endCol; c++)
            {
                var pos = new CellPosition(r, c);
                if (!_semanticCells.TryGetValue(pos, out var elem))
                {
                    elem = new CellSemanticElement(this, r, c, Sheet[r, c]);
                    _semanticCells[pos] = elem;
                    AddChild(elem);
                }
                elem.Arrange(CellBounds(r, c));
            }
        }

        if (SelectedCell is { } selPos &&
            selPos.Row >= 0 && selPos.Row < Sheet.RowCount &&
            selPos.Column >= 0 && selPos.Column < Sheet.ColumnCount &&
            !_semanticCells.TryGetValue(selPos, out var selElem))
        {
            selElem = new CellSemanticElement(this, selPos.Row, selPos.Column, Sheet[selPos.Row, selPos.Column]);
            _semanticCells[selPos] = selElem;
            AddChild(selElem);
            selElem.Arrange(CellBounds(selPos.Row, selPos.Column));
        }
    }

    private sealed class CellSemanticElement : UiElement
    {
        private readonly CellGrid _grid;
        private readonly int _row;
        private readonly int _column;
        private string _value = string.Empty;

        public CellSemanticElement(CellGrid grid, int row, int column, string value)
        {
            _grid = grid;
            _row = row;
            _column = column;
            UpdateValue(value);
        }

        public void UpdateValue(string value)
        {
            _value = value ?? string.Empty;
            AccessibleName = $"Row {_row + 1}, column {_column + 1}: {(_value.Length == 0 ? "Empty" : _value)}";
            Invalidate(UiInvalidationKind.Semantic);
        }

        protected override UiSemanticNode GetSemanticNodeCore()
        {
            var state = UiSemanticState.Visible | UiSemanticState.Enabled;
            if (!_grid.IsEditingEnabled)
                state |= UiSemanticState.ReadOnly;

            // Detached semantic elements may still be queried briefly by host accessibility bridges
            // after switching to a smaller sheet; avoid indexing the new sheet out of bounds.
            bool isValidForSheet = Parent is not null &&
                                   _row >= 0 && _row < _grid.Sheet.RowCount &&
                                   _column >= 0 && _column < _grid.Sheet.ColumnCount;
            if (isValidForSheet && _grid.IsCellSelected(_row, _column))
                state |= UiSemanticState.Selected;

            string text = isValidForSheet ? _grid.Sheet[_row, _column] : _value;
            return base.GetSemanticNodeCore() with
            {
                Role = UiSemanticRole.Label,
                Name = $"Row {_row + 1}, column {_column + 1}: {(text.Length == 0 ? "Empty" : text)}",
                State = state,
            };
        }
    }

    public bool TryGetCellAt(BPoint position, out int row, out int column)
    {
        row = -1;
        column = -1;
        var (scrollX, scrollY) = ScrollOffsetProvider?.Invoke() ?? (0, 0);
        int frozenCols = Math.Max(Sheet.FrozenColumns, Sheet.SplitColumns);
        int frozenRows = Math.Max(Sheet.FrozenRows, Sheet.SplitRows);
        double frozenWidth = GetColumnOffset(frozenCols);
        double frozenHeight = GetRowOffset(frozenRows);

        double viewX = position.X - Bounds.X - scrollX;
        double viewY = position.Y - Bounds.Y - scrollY;

        double localX = (frozenCols > 0 && viewX >= 0 && viewX < frozenWidth)
            ? viewX
            : position.X - Bounds.X;

        double localY = (frozenRows > 0 && viewY >= 0 && viewY < frozenHeight)
            ? viewY
            : position.Y - Bounds.Y;

        if (localX < 0 || localX >= TotalWidth || localY < 0 || localY >= TotalHeight)
            return false;

        int c = GetColumnAtOffset(localX);
        int r = GetRowAtOffset(localY);
        if (c < 0 || r < 0)
            return false;

        if (Sheet.TryGetMerge(r, c, out int rootR, out int rootC, out _, out _))
        {
            r = rootR;
            c = rootC;
        }

        row = r;
        column = c;
        return true;
    }

    public void ClearHoveredCell()
    {
        if (_hoveredCell is not null)
        {
            _hoveredCell = null;
            InvalidateRender();
            HoveredCellChanged?.Invoke(this, null);
        }
    }

    private void RenderNoteTooltip(BRenderList list, int row, int column, CellNote note)
    {
        var (scrollX, scrollY) = ScrollOffsetProvider?.Invoke() ?? (0, 0);
        int frozenCols = Math.Max(Sheet.FrozenColumns, Sheet.SplitColumns);
        int frozenRows = Math.Max(Sheet.FrozenRows, Sheet.SplitRows);
        double xOffset = column < frozenCols ? 0 : scrollX;
        double yOffset = row < frozenRows ? 0 : scrollY;

        var content = Sheet.GetContent(row, column);
        BRect baseCell = CellBounds(row, column, content.ColumnSpan, content.RowSpan);
        BRect cell = new BRect(baseCell.X + xOffset, baseCell.Y + yOffset, baseCell.Width, baseCell.Height);

        double tipX = cell.Right + 4;
        double tipY = cell.Y - 4;
        double maxWidth = 220;
        double padding = 8;

        var authorFont = new BFontStyle("Segoe UI", 11, BFontWeight.Bold);
        var textFont = new BFontStyle("Segoe UI", 11);
        var authorColor = new BColor(30, 30, 30);
        var textColor = new BColor(50, 50, 50);

        string header = !string.IsNullOrEmpty(note.Author) ? $"{note.Author}:" : string.Empty;
        var lines = BreakLines(note.Text, textFont, wrapText: true, maxWidth - padding * 2);

        double lineHeight = 16;
        double totalTextHeight = (string.IsNullOrEmpty(header) ? 0 : lineHeight + 2) + Math.Max(1, lines.Count) * lineHeight;
        double tipHeight = totalTextHeight + padding * 2;
        double tipWidth = maxWidth;

        if (tipX + tipWidth > Bounds.Right && cell.X - tipWidth - 4 >= Bounds.X)
            tipX = cell.X - tipWidth - 4;
        if (tipY + tipHeight > Bounds.Bottom)
            tipY = Math.Max(Bounds.Y, Bounds.Bottom - tipHeight - 4);

        var tipRect = new BRect(tipX, tipY, tipWidth, tipHeight);

        list.FillRect(new BRect(tipRect.X + 2, tipRect.Y + 2, tipRect.Width, tipRect.Height), new BColor(0, 0, 0, 40));
        list.FillRect(tipRect, new BColor(254, 249, 217));
        list.StrokeRect(tipRect, new BColor(218, 204, 140), 1);

        double curY = tipY + padding;
        if (!string.IsNullOrEmpty(header))
        {
            list.DrawText(new BTextRun(header, authorFont, authorColor), new BPoint(tipX + padding, curY));
            curY += lineHeight + 2;
        }

        foreach (var line in lines)
        {
            list.DrawText(new BTextRun(line, textFont, textColor), new BPoint(tipX + padding, curY));
            curY += lineHeight;
        }
    }

    public static BRect GetDropdownButtonBounds(BRect activeCell)
    {
        double btnW = Math.Min(18, activeCell.Width);
        return new BRect(activeCell.Right - btnW, activeCell.Y, btnW, activeCell.Height);
    }

    private void RenderDropdownButton(BRenderList list, BRect activeCell)
    {
        BRect btnRect = GetDropdownButtonBounds(activeCell);
        list.FillRect(btnRect, _theme.Surface);
        list.FillRect(new BRect(btnRect.X, btnRect.Y, 1, btnRect.Height), _theme.Border);

        double arrowW = 8;
        double arrowH = 4;
        double midX = btnRect.X + btnRect.Width / 2.0;
        double midY = btnRect.Y + btnRect.Height / 2.0;
        list.FillTriangle(
            new BPoint(midX - arrowW / 2.0, midY - arrowH / 2.0),
            new BPoint(midX + arrowW / 2.0, midY - arrowH / 2.0),
            new BPoint(midX, midY + arrowH / 2.0),
            _theme.TextMuted);
    }

    private void RenderInputMessageTooltip(BRenderList list, int row, int column, DataValidation dv)
    {
        var (scrollX, scrollY) = ScrollOffsetProvider?.Invoke() ?? (0, 0);
        int frozenCols = Math.Max(Sheet.FrozenColumns, Sheet.SplitColumns);
        int frozenRows = Math.Max(Sheet.FrozenRows, Sheet.SplitRows);
        double xOffset = column < frozenCols ? 0 : scrollX;
        double yOffset = row < frozenRows ? 0 : scrollY;

        var content = Sheet.GetContent(row, column);
        BRect baseCell = CellBounds(row, column, content.ColumnSpan, content.RowSpan);
        BRect cell = new BRect(baseCell.X + xOffset, baseCell.Y + yOffset, baseCell.Width, baseCell.Height);

        double tipX = cell.X;
        double tipY = cell.Bottom + 4;
        double maxWidth = 220;
        double padding = 6;

        var titleFont = new BFontStyle("Segoe UI", 11, BFontWeight.Bold);
        var textFont = new BFontStyle("Segoe UI", 11);
        var titleColor = _theme.Text;
        var textColor = _theme.TextMuted;

        string title = dv.InputTitle ?? string.Empty;
        string prompt = dv.InputMessage ?? string.Empty;

        var lines = BreakLines(prompt, textFont, wrapText: true, maxWidth - padding * 2);

        double lineHeight = 16;
        double totalTextHeight = (string.IsNullOrEmpty(title) ? 0 : lineHeight + 2) + Math.Max(1, lines.Count) * lineHeight;
        double tipHeight = totalTextHeight + padding * 2;
        double tipWidth = maxWidth;

        if (tipY + tipHeight > Bounds.Bottom && cell.Y - tipHeight - 4 >= Bounds.Y)
            tipY = cell.Y - tipHeight - 4;
        if (tipX + tipWidth > Bounds.Right)
            tipX = Math.Max(Bounds.X, Bounds.Right - tipWidth - 4);

        var tipRect = new BRect(tipX, tipY, tipWidth, tipHeight);

        list.FillRect(new BRect(tipRect.X + 2, tipRect.Y + 2, tipRect.Width, tipRect.Height), new BColor(0, 0, 0, 30));
        list.FillRect(tipRect, new BColor(255, 255, 235));
        list.StrokeRect(tipRect, new BColor(190, 190, 150), 1);

        double curY = tipY + padding;
        if (!string.IsNullOrEmpty(title))
        {
            list.DrawText(new BTextRun(title, titleFont, titleColor), new BPoint(tipX + padding, curY));
            curY += lineHeight + 2;
        }

        foreach (var line in lines)
        {
            list.DrawText(new BTextRun(line, textFont, textColor), new BPoint(tipX + padding, curY));
            curY += lineHeight;
        }
    }

    private void RenderHyperlinkTooltip(BRenderList list, int row, int column, CellHyperlink hyperlink)
    {
        var (scrollX, scrollY) = ScrollOffsetProvider?.Invoke() ?? (0, 0);
        int frozenCols = Math.Max(Sheet.FrozenColumns, Sheet.SplitColumns);
        int frozenRows = Math.Max(Sheet.FrozenRows, Sheet.SplitRows);
        double xOffset = column < frozenCols ? 0 : scrollX;
        double yOffset = row < frozenRows ? 0 : scrollY;

        var content = Sheet.GetContent(row, column);
        BRect baseCell = CellBounds(row, column, content.ColumnSpan, content.RowSpan);
        BRect cell = new BRect(baseCell.X + xOffset, baseCell.Y + yOffset, baseCell.Width, baseCell.Height);

        double tipX = cell.X;
        double tipY = cell.Bottom + 4;
        double maxWidth = 300;
        double padding = 6;

        var headerFont = new BFontStyle("Segoe UI", 11, BFontWeight.Bold);
        var targetFont = new BFontStyle("Segoe UI", 11);
        var hintFont = new BFontStyle("Segoe UI", 10, BFontWeight.Normal, BFontSlant.Italic);

        string header = !string.IsNullOrWhiteSpace(hyperlink.Tooltip) ? hyperlink.Tooltip : (hyperlink.IsInternal ? "Internal Link" : "Link");
        string target = hyperlink.Target;
        string hint = "Click to follow link";

        double lineHeight = 16;
        double totalTextHeight = lineHeight * 3 + 4;
        double tipHeight = totalTextHeight + padding * 2;
        double tipWidth = maxWidth;

        if (tipY + tipHeight > Bounds.Bottom && cell.Y - tipHeight - 4 >= Bounds.Y)
            tipY = cell.Y - tipHeight - 4;
        if (tipX + tipWidth > Bounds.Right)
            tipX = Math.Max(Bounds.X, Bounds.Right - tipWidth - 4);

        var tipRect = new BRect(tipX, tipY, tipWidth, tipHeight);

        list.FillRect(new BRect(tipRect.X + 2, tipRect.Y + 2, tipRect.Width, tipRect.Height), new BColor(0, 0, 0, 30));
        list.FillRect(tipRect, _theme.Surface);
        list.StrokeRect(tipRect, _theme.Border, 1);

        double curY = tipY + padding;
        list.DrawText(new BTextRun(header, headerFont, _theme.Text), new BPoint(tipX + padding, curY));
        curY += lineHeight + 2;

        string displayTarget = target;
        while (displayTarget.Length > 8 && BTextMeasurer.MeasureAdvance(displayTarget + "...", targetFont) > maxWidth - padding * 2)
        {
            displayTarget = displayTarget[..^1];
        }
        if (displayTarget.Length < target.Length)
            displayTarget += "...";

        list.DrawText(new BTextRun(displayTarget, targetFont, new BColor(17, 85, 204)), new BPoint(tipX + padding, curY));
        curY += lineHeight + 2;

        list.DrawText(new BTextRun(hint, hintFont, _theme.TextMuted), new BPoint(tipX + padding, curY));
    }
}

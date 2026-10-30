using Broiler.Cells.Core;
using Broiler.Graphics.Geometry;
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
    private CellSemanticElement[,] _semanticCells;
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
    private bool _isSelectingCells;
    private BPoint _lastSelectionPointer;

    public CellGrid(SheetModel sheet, bool isEditingEnabled = true)
    {
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
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

        _semanticCells = new CellSemanticElement[sheet.RowCount, sheet.ColumnCount];
        for (int row = 0; row < sheet.RowCount; row++)
        {
            for (int column = 0; column < sheet.ColumnCount; column++)
            {
                var cell = new CellSemanticElement(this, row, column, sheet[row, column]);
                _semanticCells[row, column] = cell;
                AddChild(cell);
            }
        }
    }

    private SheetModel _sheet;
    public SheetModel Sheet => _sheet;
    public double TotalWidth => _columnOffsets[Sheet.ColumnCount];
    public double TotalHeight => _rowOffsets[Sheet.RowCount];
    public BSize SheetSize => new(TotalWidth + 1, TotalHeight + 1);
    public CellPosition? SelectedCell { get; private set; }
    public string? SelectedAddress => SelectedCell?.ToAddress();
    public IReadOnlyList<int> SelectedRows => _selectedRows.ToArray();
    public IReadOnlyList<int> SelectedColumns => _selectedColumns.ToArray();

    public bool IsCellSelected(int row, int column) =>
        _selectedRows.Contains(row) || _selectedColumns.Contains(column) ||
        SelectedCell == new CellPosition(row, column) ||
        (_cellSelectionAnchor is { } anchor && SelectedCell is { } end &&
         row >= Math.Min(anchor.Row, end.Row) && row <= Math.Max(anchor.Row, end.Row) &&
         column >= Math.Min(anchor.Column, end.Column) && column <= Math.Max(anchor.Column, end.Column));

    public bool IsRowSelected(int row) => _selectedRows.Contains(row);
    public bool IsColumnSelected(int column) => _selectedColumns.Contains(column);
    public bool IsEditing => _isEditing;
    public bool AreHeadersVisible { get; set; } = true;

    public double GetColumnWidth(int column)
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
        return _rowHeights[row];
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

        for (int row = 0; row < Sheet.RowCount; row++)
        {
            string text = Sheet[row, column];
            if (!string.IsNullOrEmpty(text))
            {
                double advance = BTextMeasurer.MeasureAdvance(text, _font) + 16;
                if (advance > maxWidth)
                    maxWidth = advance;
            }
        }

        SetColumnWidth(column, maxWidth);
    }

    public void ResetColumnWidth(int column) => SetColumnWidth(column, DefaultCellWidth);
    public void ResetRowHeight(int row) => SetRowHeight(row, DefaultCellHeight);

    private void RecalculateColumnOffsets()
    {
        _columnOffsets[0] = 0;
        for (int i = 0; i < _columnWidths.Length; i++)
            _columnOffsets[i + 1] = _columnOffsets[i] + _columnWidths[i];
    }

    private void RecalculateRowOffsets()
    {
        _rowOffsets[0] = 0;
        for (int i = 0; i < _rowHeights.Length; i++)
            _rowOffsets[i + 1] = _rowOffsets[i] + _rowHeights[i];
    }

    public int GetColumnAtOffset(double localX)
    {
        if (localX < 0 || localX >= TotalWidth)
            return -1;
        int index = Array.BinarySearch(_columnOffsets, localX);
        if (index >= 0)
            return Math.Min(index, Sheet.ColumnCount - 1);
        int col = ~index - 1;
        return Math.Clamp(col, 0, Sheet.ColumnCount - 1);
    }

    public int GetRowAtOffset(double localY)
    {
        if (localY < 0 || localY >= TotalHeight)
            return -1;
        int index = Array.BinarySearch(_rowOffsets, localY);
        if (index >= 0)
            return Math.Min(index, Sheet.RowCount - 1);
        int row = ~index - 1;
        return Math.Clamp(row, 0, Sheet.RowCount - 1);
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
        _editor.ApplyTheme(theme);
        _editor.CornerRadius = 0;
        _editor.PaddingX = 6;
        _editor.PaddingY = 0;
        InvalidateRender();
    }

    public void SetSheet(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        EndCellSelectionDrag();
        _cellSelectionAnchor = null;
        if (_isEditing)
            CancelEdit();

        if (_sheet.ColumnCount != sheet.ColumnCount)
        {
            _columnWidths = new double[sheet.ColumnCount];
            Array.Fill(_columnWidths, DefaultCellWidth);
            _columnOffsets = new double[sheet.ColumnCount + 1];
            RecalculateColumnOffsets();
        }

        if (_sheet.RowCount != sheet.RowCount)
        {
            _rowHeights = new double[sheet.RowCount];
            Array.Fill(_rowHeights, DefaultCellHeight);
            _rowOffsets = new double[sheet.RowCount + 1];
            RecalculateRowOffsets();
        }

        if (_sheet.RowCount == sheet.RowCount && _sheet.ColumnCount == sheet.ColumnCount)
        {
            _sheet = sheet;
            for (int row = 0; row < sheet.RowCount; row++)
            {
                for (int column = 0; column < sheet.ColumnCount; column++)
                {
                    _semanticCells[row, column].UpdateValue(sheet[row, column]);
                }
            }
        }
        else
        {
            for (int row = 0; row < _sheet.RowCount; row++)
            {
                for (int column = 0; column < _sheet.ColumnCount; column++)
                {
                    var cell = _semanticCells[row, column];
                    RemoveChild(cell);
                }
            }

            _sheet = sheet;
            _semanticCells = new CellSemanticElement[sheet.RowCount, sheet.ColumnCount];
            for (int row = 0; row < sheet.RowCount; row++)
            {
                for (int column = 0; column < sheet.ColumnCount; column++)
                {
                    var cell = new CellSemanticElement(this, row, column, sheet[row, column]);
                    _semanticCells[row, column] = cell;
                    AddChild(cell);
                }
            }
        }

        SelectedCell = null;
        _selectedRows.Clear();
        _selectedColumns.Clear();
        _hasClicked = false;
        SelectedCellChanged?.Invoke(this, null);
        NotifySelectionChanged();

        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render | UiInvalidationKind.Semantic);
    }

    public void SelectCell(CellPosition position) => SelectCell(position, extend: false);

    /// <summary>Select a cell, optionally extending a rectangle from the existing cell anchor.</summary>
    public void SelectCell(CellPosition position, bool extend)
    {
        int row = Math.Clamp(position.Row, 0, Sheet.RowCount - 1);
        int col = Math.Clamp(position.Column, 0, Sheet.ColumnCount - 1);
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
            ? new CellPosition(old?.Row ?? 0, active)
            : new CellPosition(active, old?.Column ?? 0);
        _hasClicked = false;
        if (old != SelectedCell)
            SelectedCellChanged?.Invoke(this, SelectedCell);
        NotifySelectionChanged();
        if (SelectedCell is { } pos)
            BringIntoView(CellBounds(pos.Row, pos.Column));
    }

    private void NotifySelectionChanged()
    {
        foreach (var cell in _semanticCells)
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
        _editor.Arrange(CellBounds(_editingRow, _editingCol));

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

        if (newValue != Sheet.GetEditText(row, col) && !ApplyInput(row, col, newValue)) return;
        LastInputError = null;
        _isEditing = false;
        _editor.Visibility = UiVisibility.Collapsed;
        _editor.Arrange(BRect.Empty);
        MoveChildToBack(_editor);

        newValue = Sheet[row, col];
        _semanticCells[row, col].UpdateValue(newValue);

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

        if (_isEditing && _editingRow == row && _editingCol == col)
        {
            _editor.Text = value;
        }

        if (!ApplyInput(row, col, value, force)) return;
        value = Sheet[row, col];
        _semanticCells[row, col].UpdateValue(value);
        CellCommitted?.Invoke(this, new CellCommittedEventArgs(position, oldValue, value));
        InvalidateRender();
    }

    public void RefreshCellDisplay(CellPosition position)
    {
        _semanticCells[position.Row, position.Column].UpdateValue(Sheet[position.Row, position.Column]);
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
        _semanticCells[row, column].UpdateValue(string.Empty);
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
        for (int row = 0; row < Sheet.RowCount; row++)
            for (int column = 0; column < Sheet.ColumnCount; column++)
                _semanticCells[row, column].Arrange(CellBounds(row, column));

        if (_isEditing && SelectedCell is { } pos)
            _editor.Arrange(CellBounds(pos.Row, pos.Column));
        else
            _editor.Arrange(BRect.Empty);
    }

    public BRect CellBounds(int row, int column) => new(
        Bounds.X + GetColumnOffset(column) + 1,
        Bounds.Y + GetRowOffset(row) + 1,
        Math.Max(0, GetColumnWidth(column) - 1),
        Math.Max(0, GetRowHeight(row) - 1));

    protected override void RenderCore(UiRenderContext context)
    {
        var list = context.RenderList;
        var sheetBounds = new BRect(Bounds.X, Bounds.Y, SheetSize.Width, SheetSize.Height);
        list.PushClip(sheetBounds);
        list.FillRect(sheetBounds, _theme.Surface);

        for (int row = 0; row <= Sheet.RowCount; row++)
            list.FillRect(new BRect(Bounds.X, Bounds.Y + GetRowOffset(row), SheetSize.Width, 1), _theme.Border);

        for (int column = 0; column <= Sheet.ColumnCount; column++)
            list.FillRect(new BRect(Bounds.X + GetColumnOffset(column), Bounds.Y, 1, SheetSize.Height), _theme.Border);

        BRect visible = GetVisibleBounds();
        for (int row = 0; row < Sheet.RowCount; row++)
        {
            double rowTop = Bounds.Y + GetRowOffset(row);
            double rowH = GetRowHeight(row);
            double rowBottom = rowTop + rowH;
            if (rowBottom <= visible.Top || rowTop >= visible.Bottom)
                continue;

            double textYOffset = Math.Max(0, (rowH - BTextMeasurer.GetLineHeight(_font)) / 2);

            for (int column = 0; column < Sheet.ColumnCount; column++)
            {
                if (_isEditing && _editingRow == row && _editingCol == column)
                    continue;

                BRect cell = CellBounds(row, column);
                if (cell.Right <= visible.Left || cell.Left >= visible.Right)
                    continue;

                if (IsCellSelected(row, column))
                    list.FillRect(cell, _theme.AccentSoft);

                string text = Sheet[row, column];
                if (text.Length == 0)
                    continue;

                // Display on one line without altering the stored value.
                text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                list.PushClip(cell);
                list.DrawText(new BTextRun(text, _font, IsCellSelected(row, column) ? _theme.SelectionText : _theme.Text),
                    new BPoint(cell.X + 6, rowTop + textYOffset));
                list.PopClip();
            }
        }

        // Active cell outline
        if (SelectedCell is { } selected)
        {
            BRect activeCell = CellBounds(selected.Row, selected.Column);
            list.FillRect(new BRect(activeCell.X - 1, activeCell.Y - 1, activeCell.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(activeCell.X - 1, activeCell.Bottom - 1, activeCell.Width + 2, 2), _theme.FocusRing);
            list.FillRect(new BRect(activeCell.X - 1, activeCell.Y - 1, 2, activeCell.Height + 2), _theme.FocusRing);
            list.FillRect(new BRect(activeCell.Right - 1, activeCell.Y - 1, 2, activeCell.Height + 2), _theme.FocusRing);
        }

        base.RenderCore(context);
        list.PopClip();
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (input.Kind == UiInputEventKind.PointerMove)
        {
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
            double localX = input.Position.X - Bounds.X;
            double localY = input.Position.Y - Bounds.Y;
            if (localX < 0 || localX >= TotalWidth || localY < 0 || localY >= TotalHeight)
                return false;

            int col = GetColumnAtOffset(localX);
            int row = GetRowAtOffset(localY);
            if (col < 0 || row < 0)
                return false;

            var clicked = new CellPosition(row, col);
            bool extend = input.KeyModifiers.HasFlag(KeyboardModifierState.Shift);

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
            if (_isSelectingCells)
            {
                UpdateCellSelectionDrag(input.Position);
                EndCellSelectionDrag();
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
        int row = GetRowAtOffset(Math.Clamp(pointer.Y - Bounds.Y, 0, TotalHeight - 0.001));
        int column = GetColumnAtOffset(Math.Clamp(pointer.X - Bounds.X, 0, TotalWidth - 0.001));
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

        if (_isEditing)
        {
            if (IsKey(input, 0x1B, "Escape"))
            {
                CancelEdit();
                return true;
            }

            if (IsKey(input, 0x0D, "Enter"))
            {
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

    private void Navigate(int deltaRow, int deltaColumn)
    {
        CellPosition current = SelectedCell ?? new CellPosition(0, 0);
        int row = Math.Clamp(current.Row + deltaRow, 0, Sheet.RowCount - 1);
        int col = Math.Clamp(current.Column + deltaColumn, 0, Sheet.ColumnCount - 1);
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

    private static bool IsKey(UiInputEvent input, int nativeKeyCode, string name) =>
        input.NativeKeyCode == nativeKeyCode ||
        string.Equals(input.KeyName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(input.KeyName, "VirtualKey:" + nativeKeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
    {
        Role = UiSemanticRole.Group,
        State = UiSemanticState.Visible | UiSemanticState.Enabled | (_isEditingEnabled ? UiSemanticState.None : UiSemanticState.ReadOnly)
    };

    private sealed class CellSemanticElement : UiElement
    {
        private readonly CellGrid _grid;
        private readonly int _row;
        private readonly int _column;

        public CellSemanticElement(CellGrid grid, int row, int column, string value)
        {
            _grid = grid;
            _row = row;
            _column = column;
            UpdateValue(value);
        }

        public void UpdateValue(string value)
        {
            AccessibleName = $"Row {_row + 1}, column {_column + 1}: {(value.Length == 0 ? "Empty" : value)}";
            Invalidate(UiInvalidationKind.Semantic);
        }

        protected override UiSemanticNode GetSemanticNodeCore()
        {
            var state = UiSemanticState.Visible | UiSemanticState.Enabled;
            if (!_grid.IsEditingEnabled)
                state |= UiSemanticState.ReadOnly;
            if (_grid.IsCellSelected(_row, _column))
                state |= UiSemanticState.Selected;

            return base.GetSemanticNodeCore() with
            {
                Role = UiSemanticRole.Label,
                Name = $"Row {_row + 1}, column {_column + 1}: {(_grid.Sheet[_row, _column].Length == 0 ? "Empty" : _grid.Sheet[_row, _column])}",
                State = state,
            };
        }
    }
}

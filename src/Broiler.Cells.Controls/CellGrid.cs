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
                SelectedCell = null;

            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render | UiInvalidationKind.Semantic);
        }
    }

    public event EventHandler<CellPosition?>? SelectedCellChanged;
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
        SelectedCellChanged?.Invoke(this, null);

        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render | UiInvalidationKind.Semantic);
    }

    public void SelectCell(CellPosition position)
    {
        int row = Math.Clamp(position.Row, 0, Sheet.RowCount - 1);
        int col = Math.Clamp(position.Column, 0, Sheet.ColumnCount - 1);
        var newPos = new CellPosition(row, col);
        if (SelectedCell == newPos)
            return;

        var oldPos = SelectedCell;
        SelectedCell = newPos;
        SelectedCellChanged?.Invoke(this, newPos);

        if (oldPos is { } old)
            _semanticCells[old.Row, old.Column].Invalidate(UiInvalidationKind.Semantic);
        _semanticCells[row, col].Invalidate(UiInvalidationKind.Semantic);

        BringIntoView(CellBounds(row, col));
        InvalidateRender();
    }

    public void StartEditing(string initialText)
    {
        if (!IsEditingEnabled || SelectedCell is null)
            return;

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

    public void CommitEdit()
    {
        if (!_isEditing)
            return;

        int row = _editingRow;
        int col = _editingCol;
        string oldValue = Sheet[row, col];
        string newValue = _editor.Text;

        _isEditing = false;
        _editor.Visibility = UiVisibility.Collapsed;
        _editor.Arrange(BRect.Empty);
        MoveChildToBack(_editor);

        Sheet[row, col] = newValue;
        _semanticCells[row, col].UpdateValue(newValue);

        CellCommitted?.Invoke(this, new CellCommittedEventArgs(new CellPosition(row, col), oldValue, newValue));

        Session?.SetFocus(this);
        InvalidateRender();
    }

    public void CancelEdit()
    {
        if (!_isEditing)
            return;

        _isEditing = false;
        _editor.Visibility = UiVisibility.Collapsed;
        _editor.Arrange(BRect.Empty);
        MoveChildToBack(_editor);
        _editor.Text = Sheet[_editingRow, _editingCol];

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

                string text = Sheet[row, column];
                if (text.Length == 0)
                    continue;

                // Display on one line without altering the stored value.
                text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                list.PushClip(cell);
                list.DrawText(new BTextRun(text, _font, _theme.Text),
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

            if (_isEditing)
            {
                if (clicked != SelectedCell)
                {
                    CommitEdit();
                    SelectCell(clicked);
                    Session?.SetFocus(this);
                }
                return true;
            }

            bool isDouble = IsDoubleClick(input.Position) && SelectedCell == clicked;
            UpdateClickState(input.Position, clicked);

            if (isDouble)
            {
                SelectCell(clicked);
                StartEditing(Sheet[clicked.Row, clicked.Column]);
                return true;
            }

            SelectCell(clicked);
            Session?.SetFocus(this);
            return true;
        }

        if (input.MouseButtonTransition == MouseButtonTransition.Up)
        {
            double localX = input.Position.X - Bounds.X;
            double localY = input.Position.Y - Bounds.Y;

            if (localX >= 0 && localX < TotalWidth && localY >= 0 && localY < TotalHeight)
                return true;
        }

        return false;
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
                StartEditing(Sheet[selected.Row, selected.Column]);
                return true;
            }
            return false;
        }

        if (IsKey(input, 0x2E, "Delete"))
        {
            if (SelectedCell is { } selected)
            {
                ClearCell(selected.Row, selected.Column);
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
            if (_grid.SelectedCell is { } pos && pos.Row == _row && pos.Column == _column)
                state |= UiSemanticState.Selected;

            return base.GetSemanticNodeCore() with
            {
                Role = UiSemanticRole.Label,
                State = state,
            };
        }
    }
}

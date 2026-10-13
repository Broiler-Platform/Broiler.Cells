using Broiler.Cells.Core;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Cells.Controls;

/// <summary>
/// A reusable spreadsheet control coordinating display, in-place editing, scrolling,
/// and optional coordinate headers (column letters A-CV and row numbers 1-100).
/// </summary>
public sealed class CellControl : UiElement, IStandardThemedControl
{
    public const double RowHeaderWidth = 48;
    public const double ColumnHeaderHeight = 28;

    private StandardThemeTokens _theme = StandardThemeTokens.Light;
    private BFontStyle _font = StandardThemeTokens.Light.FontBody;
    private BColor _headerBackground = BColor.FromArgb(255, 231, 235, 240);

    private readonly CornerHeaderElement _corner;
    private readonly ColumnHeadersElement _columnHeaders;
    private readonly RowHeadersElement _rowHeaders;
    private readonly StandardScrollView _scroll;
    private readonly CellGrid _grid;

    private bool _areHeadersVisible;

    public CellControl(SheetModel sheet, bool isEditingEnabled = true, bool areHeadersVisible = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _areHeadersVisible = areHeadersVisible;
        AccessibleName = "Sheet control";

        _grid = new CellGrid(sheet, isEditingEnabled) { AreHeadersVisible = areHeadersVisible };
        _grid.SelectedCellChanged += (_, pos) => SelectedCellChanged?.Invoke(this, pos);
        _grid.CellCommitted += (_, args) => CellCommitted?.Invoke(this, args);

        _corner = new CornerHeaderElement(this);
        _columnHeaders = new ColumnHeadersElement(this);
        _rowHeaders = new RowHeadersElement(this);

        _grid.ColumnResized += (_, args) =>
        {
            _columnHeaders.InvalidateRender();
            ColumnResized?.Invoke(this, args);
        };
        _grid.RowResized += (_, args) =>
        {
            _rowHeaders.InvalidateRender();
            RowResized?.Invoke(this, args);
        };

        _scroll = new StandardScrollView
        {
            Background = _theme.Surface,
            FocusWhenScrollable = true,
            AccessibleName = "Sheet viewport",
        };
        _scroll.AddChild(_grid);
        _scroll.OffsetChanged += (_, _) =>
        {
            _columnHeaders.InvalidateRender();
            _rowHeaders.InvalidateRender();
        };

        if (!areHeadersVisible)
        {
            _corner.Visibility = UiVisibility.Collapsed;
            _columnHeaders.Visibility = UiVisibility.Collapsed;
            _rowHeaders.Visibility = UiVisibility.Collapsed;
        }

        AddChild(_corner);
        AddChild(_columnHeaders);
        AddChild(_rowHeaders);
        AddChild(_scroll);
    }

    public SheetModel Sheet => _grid.Sheet;
    public CellGrid Grid => _grid;
    public StandardScrollView ScrollView => _scroll;

    public void SetSheet(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _grid.SetSheet(sheet);
        _columnHeaders.InvalidateRender();
        _rowHeaders.InvalidateRender();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public bool IsEditingEnabled
    {
        get => _grid.IsEditingEnabled;
        set => _grid.IsEditingEnabled = value;
    }

    public bool AreHeadersVisible
    {
        get => _areHeadersVisible;
        set
        {
            if (_areHeadersVisible == value)
                return;

            _areHeadersVisible = value;
            _corner.Visibility = value ? UiVisibility.Visible : UiVisibility.Collapsed;
            _columnHeaders.Visibility = value ? UiVisibility.Visible : UiVisibility.Collapsed;
            _rowHeaders.Visibility = value ? UiVisibility.Visible : UiVisibility.Collapsed;
            _grid.AreHeadersVisible = value;

            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        }
    }

    public CellPosition? SelectedCell => _grid.SelectedCell;
    public string? SelectedAddress => _grid.SelectedCell?.ToAddress();
    public bool IsEditing => _grid.IsEditing;

    public double GetColumnWidth(int column) => _grid.GetColumnWidth(column);
    public void SetColumnWidth(int column, double width) => _grid.SetColumnWidth(column, width);
    public double GetRowHeight(int row) => _grid.GetRowHeight(row);
    public void SetRowHeight(int row, double height) => _grid.SetRowHeight(row, height);
    public void AutoFitColumn(int column) => _grid.AutoFitColumn(column);
    public void ResetColumnWidth(int column) => _grid.ResetColumnWidth(column);
    public void ResetRowHeight(int row) => _grid.ResetRowHeight(row);

    public BColor HeaderBackground
    {
        get => _headerBackground;
        set
        {
            _headerBackground = value;
            InvalidateRender();
        }
    }

    public StandardThemeTokens Theme => _theme;
    public BFontStyle Font => _font;

    public event EventHandler<CellPosition?>? SelectedCellChanged;
    public event EventHandler<CellCommittedEventArgs>? CellCommitted;
    public event EventHandler<ColumnResizedEventArgs>? ColumnResized;
    public event EventHandler<RowResizedEventArgs>? RowResized;

    internal void SetCursor(UiCursorShape shape)
    {
        (Session?.Host as IUiCursorHost)?.SetCursor(shape);
    }

    public void SelectCell(CellPosition position) => _grid.SelectCell(position);
    public void StartEditing(string initialText) => _grid.StartEditing(initialText);
    public void CommitEdit() => _grid.CommitEdit();
    public void CancelEdit() => _grid.CancelEdit();

    public void ApplyTheme(StandardThemeTokens theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _font = theme.FontBody;
        _scroll.Background = theme.Surface;
        _grid.ApplyTheme(theme);
        InvalidateRender();
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        if (_areHeadersVisible)
        {
            double contentWidth = Math.Max(0, availableSize.Width - RowHeaderWidth);
            double contentHeight = Math.Max(0, availableSize.Height - ColumnHeaderHeight);
            _corner.Measure(new BSize(RowHeaderWidth, ColumnHeaderHeight));
            _columnHeaders.Measure(new BSize(contentWidth, ColumnHeaderHeight));
            _rowHeaders.Measure(new BSize(RowHeaderWidth, contentHeight));
            _scroll.Measure(new BSize(contentWidth, contentHeight));
        }
        else
        {
            _scroll.Measure(availableSize);
        }

        return availableSize;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (_areHeadersVisible)
        {
            double contentWidth = Math.Max(0, finalRect.Width - RowHeaderWidth);
            double contentHeight = Math.Max(0, finalRect.Height - ColumnHeaderHeight);

            _corner.Arrange(new BRect(finalRect.X, finalRect.Y, RowHeaderWidth, ColumnHeaderHeight));
            _columnHeaders.Arrange(new BRect(finalRect.X + RowHeaderWidth, finalRect.Y, contentWidth, ColumnHeaderHeight));
            _rowHeaders.Arrange(new BRect(finalRect.X, finalRect.Y + ColumnHeaderHeight, RowHeaderWidth, contentHeight));
            _scroll.Arrange(new BRect(finalRect.X + RowHeaderWidth, finalRect.Y + ColumnHeaderHeight, contentWidth, contentHeight));
        }
        else
        {
            _corner.Arrange(BRect.Empty);
            _columnHeaders.Arrange(BRect.Empty);
            _rowHeaders.Arrange(BRect.Empty);
            _scroll.Arrange(finalRect);
        }
    }

    protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
    {
        Role = UiSemanticRole.Group,
        State = UiSemanticState.Visible | UiSemanticState.Enabled
    };

    private sealed class CornerHeaderElement : UiElement
    {
        private readonly CellControl _control;

        public CornerHeaderElement(CellControl control)
        {
            _control = control;
            AccessibleName = "Header corner";
        }

        protected override bool OnInput(UiInputEvent input)
        {
            if (input.Kind == UiInputEventKind.PointerMove)
            {
                _control.SetCursor(UiCursorShape.Arrow);
            }
            return base.OnInput(input);
        }

        protected override void RenderCore(UiRenderContext context)
        {
            var list = context.RenderList;
            list.PushClip(Bounds);
            list.FillRect(Bounds, _control.HeaderBackground);
            list.FillRect(new BRect(Bounds.X, Bounds.Bottom - 1, Bounds.Width, 1), _control.Theme.Border);
            list.FillRect(new BRect(Bounds.Right - 1, Bounds.Y, 1, Bounds.Height), _control.Theme.Border);
            list.PopClip();
        }

        protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
        {
            Role = UiSemanticRole.Generic,
            State = UiSemanticState.Visible | UiSemanticState.Enabled
        };
    }

    private sealed class ColumnHeadersElement : UiElement
    {
        private const double DividerHitSlop = 4.0;
        private readonly CellControl _control;

        private bool _isDragging;
        private int _dragColumnIndex = -1;
        private double _dragStartPointer;
        private double _dragStartWidth;
        private int _hoveredDividerIndex = -1;

        private UiTimestamp _lastClickTime;
        private BPoint _lastClickPosition;
        private int _lastClickColumn = -1;
        private bool _hasClicked;

        public ColumnHeadersElement(CellControl control)
        {
            _control = control;
            AccessibleName = "Column headers";
        }

        private int FindColumnDivider(BPoint pointer)
        {
            if (pointer.Y < Bounds.Top || pointer.Y > Bounds.Bottom)
                return -1;

            double offsetX = _control.ScrollView.HorizontalOffset;

            for (int col = 0; col < _control.Sheet.ColumnCount; col++)
            {
                double dividerX = _control.Grid.GetColumnOffset(col + 1);
                double screenDividerX = Bounds.X - offsetX + dividerX;

                if (screenDividerX >= Bounds.Left - DividerHitSlop && screenDividerX <= Bounds.Right + DividerHitSlop)
                {
                    if (Math.Abs(pointer.X - screenDividerX) <= DividerHitSlop)
                        return col;
                }
            }

            return -1;
        }

        protected override bool OnInput(UiInputEvent input)
        {
            return input.Kind switch
            {
                UiInputEventKind.PointerButton => HandlePointerButton(input),
                UiInputEventKind.PointerMove => HandlePointerMove(input),
                _ => false,
            };
        }

        private bool HandlePointerButton(UiInputEvent input)
        {
            if (input.MouseButton != MouseButton.Left)
                return false;

            if (input.MouseButtonTransition == MouseButtonTransition.Down)
            {
                int divider = FindColumnDivider(input.Position);
                if (divider >= 0)
                {
                    if (IsDoubleClick(input.Position, divider))
                    {
                        _control.AutoFitColumn(divider);
                        _isDragging = false;
                        _dragColumnIndex = -1;
                        _hoveredDividerIndex = divider;
                        _control.SetCursor(UiCursorShape.ResizeHorizontal);
                        InvalidateRender();
                        return true;
                    }

                    _isDragging = true;
                    _dragColumnIndex = divider;
                    _dragStartPointer = input.Position.X;
                    _dragStartWidth = _control.Grid.GetColumnWidth(divider);
                    _hoveredDividerIndex = divider;
                    UpdateClickState(input.Position, divider);

                    Session?.CaptureInput(this);
                    _control.SetCursor(UiCursorShape.ResizeHorizontal);
                    InvalidateRender();
                    return true;
                }
            }
            else if (input.MouseButtonTransition == MouseButtonTransition.Up)
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    _dragColumnIndex = -1;
                    Session?.ReleaseInputCapture(this);

                    int divider = FindColumnDivider(input.Position);
                    _hoveredDividerIndex = divider;
                    _control.SetCursor(divider >= 0 ? UiCursorShape.ResizeHorizontal : UiCursorShape.Arrow);
                    InvalidateRender();
                    return true;
                }
            }

            return false;
        }

        private bool HandlePointerMove(UiInputEvent input)
        {
            if (_isDragging && _dragColumnIndex >= 0)
            {
                double delta = input.Position.X - _dragStartPointer;
                double newWidth = Math.Clamp(_dragStartWidth + delta, CellGrid.MinCellWidth, CellGrid.MaxCellWidth);
                _control.SetColumnWidth(_dragColumnIndex, newWidth);
                _control.SetCursor(UiCursorShape.ResizeHorizontal);
                InvalidateRender();
                return true;
            }

            int divider = FindColumnDivider(input.Position);
            if (divider != _hoveredDividerIndex)
            {
                _hoveredDividerIndex = divider;
                InvalidateRender();
            }

            if (divider >= 0)
            {
                _control.SetCursor(UiCursorShape.ResizeHorizontal);
                return true;
            }
            else
            {
                _control.SetCursor(UiCursorShape.Arrow);
                return false;
            }
        }

        private bool IsDoubleClick(BPoint point, int column)
        {
            if (!_hasClicked || Session is null || _lastClickColumn != column)
                return false;

            TimeSpan delta = Session.Clock.Now.Elapsed - _lastClickTime.Elapsed;
            bool quick = delta >= TimeSpan.Zero && delta <= TimeSpan.FromMilliseconds(400);
            bool near = Math.Abs(point.X - _lastClickPosition.X) <= 4 &&
                        Math.Abs(point.Y - _lastClickPosition.Y) <= 4;
            return quick && near;
        }

        private void UpdateClickState(BPoint point, int column)
        {
            _lastClickTime = Session?.Clock.Now ?? default;
            _lastClickPosition = point;
            _lastClickColumn = column;
            _hasClicked = true;
        }

        protected override void RenderCore(UiRenderContext context)
        {
            var list = context.RenderList;
            list.PushClip(Bounds);
            list.FillRect(Bounds, _control.HeaderBackground);
            list.FillRect(new BRect(Bounds.X, Bounds.Bottom - 1, Bounds.Width, 1), _control.Theme.Border);

            double offsetX = _control.ScrollView.HorizontalOffset;
            double textY = Math.Max(0, (Bounds.Height - BTextMeasurer.GetLineHeight(_control.Font)) / 2);

            for (int col = 0; col < _control.Sheet.ColumnCount; col++)
            {
                double colLeft = Bounds.X - offsetX + _control.Grid.GetColumnOffset(col);
                double colWidth = _control.Grid.GetColumnWidth(col);
                double colRight = colLeft + colWidth;

                if (colRight <= Bounds.Left || colLeft >= Bounds.Right)
                    continue;

                bool isHighlighted = _hoveredDividerIndex == col || (_isDragging && _dragColumnIndex == col);
                BColor dividerColor = isHighlighted ? _control.Theme.FocusRing : _control.Theme.Border;
                double dividerThickness = isHighlighted ? 2 : 1;
                list.FillRect(new BRect(colRight - (isHighlighted ? 1 : 0), Bounds.Y, dividerThickness, Bounds.Height), dividerColor);

                string label = CellPosition.GetColumnLabel(col);
                double labelWidth = BTextMeasurer.MeasureAdvance(label, _control.Font);
                double labelX = colLeft + Math.Max(0, (colWidth - labelWidth) / 2);

                list.PushClip(new BRect(colLeft + 1, Bounds.Y, Math.Max(0, colWidth - 1), Bounds.Height));
                list.DrawText(new BTextRun(label, _control.Font, _control.Theme.TextMuted), new BPoint(labelX, Bounds.Y + textY));
                list.PopClip();
            }

            list.PopClip();
        }

        protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
        {
            Role = UiSemanticRole.Label,
            State = UiSemanticState.Visible | UiSemanticState.Enabled
        };
    }

    private sealed class RowHeadersElement : UiElement
    {
        private const double DividerHitSlop = 4.0;
        private readonly CellControl _control;

        private bool _isDragging;
        private int _dragRowIndex = -1;
        private double _dragStartPointer;
        private double _dragStartHeight;
        private int _hoveredDividerIndex = -1;

        private UiTimestamp _lastClickTime;
        private BPoint _lastClickPosition;
        private int _lastClickRow = -1;
        private bool _hasClicked;

        public RowHeadersElement(CellControl control)
        {
            _control = control;
            AccessibleName = "Row headers";
        }

        private int FindRowDivider(BPoint pointer)
        {
            if (pointer.X < Bounds.Left || pointer.X > Bounds.Right)
                return -1;

            double offsetY = _control.ScrollView.VerticalOffset;

            for (int row = 0; row < _control.Sheet.RowCount; row++)
            {
                double dividerY = _control.Grid.GetRowOffset(row + 1);
                double screenDividerY = Bounds.Y - offsetY + dividerY;

                if (screenDividerY >= Bounds.Top - DividerHitSlop && screenDividerY <= Bounds.Bottom + DividerHitSlop)
                {
                    if (Math.Abs(pointer.Y - screenDividerY) <= DividerHitSlop)
                        return row;
                }
            }

            return -1;
        }

        protected override bool OnInput(UiInputEvent input)
        {
            return input.Kind switch
            {
                UiInputEventKind.PointerButton => HandlePointerButton(input),
                UiInputEventKind.PointerMove => HandlePointerMove(input),
                _ => false,
            };
        }

        private bool HandlePointerButton(UiInputEvent input)
        {
            if (input.MouseButton != MouseButton.Left)
                return false;

            if (input.MouseButtonTransition == MouseButtonTransition.Down)
            {
                int divider = FindRowDivider(input.Position);
                if (divider >= 0)
                {
                    if (IsDoubleClick(input.Position, divider))
                    {
                        _control.ResetRowHeight(divider);
                        _isDragging = false;
                        _dragRowIndex = -1;
                        _hoveredDividerIndex = divider;
                        _control.SetCursor(UiCursorShape.ResizeVertical);
                        InvalidateRender();
                        return true;
                    }

                    _isDragging = true;
                    _dragRowIndex = divider;
                    _dragStartPointer = input.Position.Y;
                    _dragStartHeight = _control.Grid.GetRowHeight(divider);
                    _hoveredDividerIndex = divider;
                    UpdateClickState(input.Position, divider);

                    Session?.CaptureInput(this);
                    _control.SetCursor(UiCursorShape.ResizeVertical);
                    InvalidateRender();
                    return true;
                }
            }
            else if (input.MouseButtonTransition == MouseButtonTransition.Up)
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    _dragRowIndex = -1;
                    Session?.ReleaseInputCapture(this);

                    int divider = FindRowDivider(input.Position);
                    _hoveredDividerIndex = divider;
                    _control.SetCursor(divider >= 0 ? UiCursorShape.ResizeVertical : UiCursorShape.Arrow);
                    InvalidateRender();
                    return true;
                }
            }

            return false;
        }

        private bool HandlePointerMove(UiInputEvent input)
        {
            if (_isDragging && _dragRowIndex >= 0)
            {
                double delta = input.Position.Y - _dragStartPointer;
                double newHeight = Math.Clamp(_dragStartHeight + delta, CellGrid.MinCellHeight, CellGrid.MaxCellHeight);
                _control.SetRowHeight(_dragRowIndex, newHeight);
                _control.SetCursor(UiCursorShape.ResizeVertical);
                InvalidateRender();
                return true;
            }

            int divider = FindRowDivider(input.Position);
            if (divider != _hoveredDividerIndex)
            {
                _hoveredDividerIndex = divider;
                InvalidateRender();
            }

            if (divider >= 0)
            {
                _control.SetCursor(UiCursorShape.ResizeVertical);
                return true;
            }
            else
            {
                _control.SetCursor(UiCursorShape.Arrow);
                return false;
            }
        }

        private bool IsDoubleClick(BPoint point, int row)
        {
            if (!_hasClicked || Session is null || _lastClickRow != row)
                return false;

            TimeSpan delta = Session.Clock.Now.Elapsed - _lastClickTime.Elapsed;
            bool quick = delta >= TimeSpan.Zero && delta <= TimeSpan.FromMilliseconds(400);
            bool near = Math.Abs(point.X - _lastClickPosition.X) <= 4 &&
                        Math.Abs(point.Y - _lastClickPosition.Y) <= 4;
            return quick && near;
        }

        private void UpdateClickState(BPoint point, int row)
        {
            _lastClickTime = Session?.Clock.Now ?? default;
            _lastClickPosition = point;
            _lastClickRow = row;
            _hasClicked = true;
        }

        protected override void RenderCore(UiRenderContext context)
        {
            var list = context.RenderList;
            list.PushClip(Bounds);
            list.FillRect(Bounds, _control.HeaderBackground);
            list.FillRect(new BRect(Bounds.Right - 1, Bounds.Y, 1, Bounds.Height), _control.Theme.Border);

            double offsetY = _control.ScrollView.VerticalOffset;

            for (int row = 0; row < _control.Sheet.RowCount; row++)
            {
                double rowTop = Bounds.Y - offsetY + _control.Grid.GetRowOffset(row);
                double rowHeight = _control.Grid.GetRowHeight(row);
                double rowBottom = rowTop + rowHeight;

                if (rowBottom <= Bounds.Top || rowTop >= Bounds.Bottom)
                    continue;

                bool isHighlighted = _hoveredDividerIndex == row || (_isDragging && _dragRowIndex == row);
                BColor dividerColor = isHighlighted ? _control.Theme.FocusRing : _control.Theme.Border;
                double dividerThickness = isHighlighted ? 2 : 1;
                list.FillRect(new BRect(Bounds.X, rowBottom - (isHighlighted ? 1 : 0), Bounds.Width, dividerThickness), dividerColor);

                string label = (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                double labelWidth = BTextMeasurer.MeasureAdvance(label, _control.Font);
                double labelX = Bounds.X + Math.Max(0, (Bounds.Width - labelWidth) / 2);
                double textYOffset = Math.Max(0, (rowHeight - BTextMeasurer.GetLineHeight(_control.Font)) / 2);
                double labelY = rowTop + textYOffset;

                list.PushClip(new BRect(Bounds.X, rowTop + 1, Bounds.Width - 1, Math.Max(0, rowHeight - 1)));
                list.DrawText(new BTextRun(label, _control.Font, _control.Theme.TextMuted),
                    new BPoint(labelX, labelY));
                list.PopClip();
            }

            list.PopClip();
        }

        protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
        {
            Role = UiSemanticRole.Label,
            State = UiSemanticState.Visible | UiSemanticState.Enabled
        };
    }
}

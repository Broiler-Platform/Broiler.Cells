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
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
namespace Broiler.Cells.Controls;

/// <summary>
/// A reusable spreadsheet control coordinating display, in-place editing, scrolling,
/// and optional coordinate headers (column letters A-CV and row numbers 1-100).
/// </summary>
public sealed class CellControl : UiElement, IStandardThemedControl
{
    public const double RowHeaderWidthBase = 48;
    public const double ColumnHeaderHeightBase = 28;
    public double RowHeaderWidth => RowHeaderWidthBase + (Sheet?.MaxRowOutlineLevel ?? 0) * 14.0;
    public double ColumnHeaderHeight => ColumnHeaderHeightBase + (Sheet?.MaxColumnOutlineLevel ?? 0) * 14.0;
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
        ToolTipText = "Spreadsheet grid";
        _grid = new CellGrid(sheet, isEditingEnabled) { AreHeadersVisible = areHeadersVisible };
        _grid.SelectedCellChanged += (_, pos) => SelectedCellChanged?.Invoke(this, pos);
        _grid.CellCommitted += (_, args) => CellCommitted?.Invoke(this, args);
        _grid.ValidationFailed += (r, c, res) => ValidationFailed?.Invoke(r, c, res);
        _grid.OpenDropdownRequested += (r, c, dv, rect) => OpenDropdownRequested?.Invoke(r, c, dv, rect);
        _grid.AutoFillCompleted += (src, dst) => AutoFillCompleted?.Invoke(src, dst);
        _grid.FilterDropdownRequested += (col, rect) => FilterDropdownRequested?.Invoke(col, rect);
        _grid.HyperlinkClicked += (r, c, hl) =>
        {
            HyperlinkClicked?.Invoke(r, c, hl);
            NavigateHyperlink(hl);
        };
        _corner = new CornerHeaderElement(this);
        _columnHeaders = new ColumnHeadersElement(this);
        _rowHeaders = new RowHeadersElement(this);
        _grid.SelectionChanged += (_, _) =>
        {
            _columnHeaders.InvalidateRender();
            _rowHeaders.InvalidateRender();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
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
        _grid.ScrollOffsetProvider = () => (_scroll.HorizontalOffset, _scroll.VerticalOffset);
        _scroll.OffsetChanged += (_, _) =>
        {
            _columnHeaders.InvalidateRender();
            _rowHeaders.InvalidateRender();
            if (_grid.Sheet.FrozenRows > 0 || _grid.Sheet.FrozenColumns > 0 || _grid.Sheet.SplitRows > 0 || _grid.Sheet.SplitColumns > 0)
                _grid.InvalidateRender();
        };
        _grid.Sheet.SplitChanged += OnSheetSplitChanged;
        _grid.Sheet.OutlinesChanged += OnSheetOutlinesChanged;
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
    private void OnSheetSplitChanged(object? sender, EventArgs e)
    {
        _columnHeaders.InvalidateRender();
        _rowHeaders.InvalidateRender();
        _grid.InvalidateRender();
    }

    private void OnSheetOutlinesChanged()
    {
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void SetSheet(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (_grid.Sheet != null)
        {
            _grid.Sheet.SplitChanged -= OnSheetSplitChanged;
            _grid.Sheet.OutlinesChanged -= OnSheetOutlinesChanged;
        }
        _grid.SetSheet(sheet);
        _grid.Sheet.SplitChanged += OnSheetSplitChanged;
        _grid.Sheet.OutlinesChanged += OnSheetOutlinesChanged;

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
    public IReadOnlyList<int> SelectedRows => _grid.SelectedRows;
    public IReadOnlyList<int> SelectedColumns => _grid.SelectedColumns;
    public bool IsCellSelected(int row, int column) => _grid.IsCellSelected(row, column);
    public bool IsEditing => _grid.IsEditing;
    public double GetColumnWidth(int column) => _grid.GetColumnWidth(column);
    public void SetColumnWidth(int column, double width) => _grid.SetColumnWidth(column, width);
    public double GetRowHeight(int row) => _grid.GetRowHeight(row);
    public void SetRowHeight(int row, double height) => _grid.SetRowHeight(row, height);
    public void AutoFitColumn(int column) => _grid.AutoFitColumn(column);
    public void AutoFitRow(int row) => _grid.AutoFitRow(row);
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
    public event EventHandler? SelectionChanged;
    public event EventHandler<CellCommittedEventArgs>? CellCommitted;
    public event EventHandler<ColumnResizedEventArgs>? ColumnResized;
    public event EventHandler<RowResizedEventArgs>? RowResized;
    public event Action<int, int, ValidationResult>? ValidationFailed;
    public event Action<int, int, DataValidation, BRect>? OpenDropdownRequested;
    public event Action<CellRangeAddress, CellRangeAddress>? AutoFillCompleted;
    public event Action<int, BRect>? FilterDropdownRequested;
    public event Action<int, int, CellHyperlink>? HyperlinkClicked;
    public event EventHandler<CellHyperlinkNavigatedEventArgs>? HyperlinkNavigated;
    internal void SetCursor(UiCursorShape shape)
    {
        (Session?.Host as IUiCursorHost)?.SetCursor(shape);
    }

    public void SelectCell(CellPosition position) => _grid.SelectCell(position);
    public void SelectCell(CellPosition position, bool extend) => _grid.SelectCell(position, extend);
    public void SelectRow(int row, bool extend = false, bool toggle = false) => _grid.SelectRow(row, extend, toggle);
    public void SelectColumn(int column, bool extend = false, bool toggle = false) => _grid.SelectColumn(column, extend, toggle);
    public void StartEditing(string initialText) => _grid.StartEditing(initialText);
    public void CommitEdit() => _grid.CommitEdit();
    public void CancelEdit() => _grid.CancelEdit();
    public void SetCellValue(CellPosition position, string value) => _grid.SetCellValue(position, value);
    public void SetCellValue(int row, int column, string value) => _grid.SetCellValue(new CellPosition(row, column), value);
    public bool FillDown() => _grid.FillDown();
    public bool FillRight() => _grid.FillRight();
    public bool QuickFillDown() => _grid.QuickFillDown();
    public bool AutoFill(CellRangeAddress source, CellRangeAddress target, AutoFillMode mode = AutoFillMode.Default) => _grid.AutoFill(source, target, mode);
    public void ToggleAutoFilter() => _grid.ToggleAutoFilter();
    public void QuickSort(int columnIndex, bool ascending) => _grid.QuickSort(columnIndex, ascending);

    public void SelectAll() => _grid.SelectAll();

    public void GroupSelectedRows()
    {
        var sel = Grid.GetSelectedRange();
        if (sel is null) return;
        Sheet.GroupRows(sel.Value.StartRow, sel.Value.EndRow);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void UngroupSelectedRows()
    {
        var sel = Grid.GetSelectedRange();
        if (sel is null) return;
        Sheet.UngroupRows(sel.Value.StartRow, sel.Value.EndRow);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void GroupSelectedColumns()
    {
        var sel = Grid.GetSelectedRange();
        if (sel is null) return;
        Sheet.GroupColumns(sel.Value.StartCol, sel.Value.EndCol);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void UngroupSelectedColumns()
    {
        var sel = Grid.GetSelectedRange();
        if (sel is null) return;
        Sheet.UngroupColumns(sel.Value.StartCol, sel.Value.EndCol);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void ShowSelectedDetail()
    {
        var sel = Grid.GetSelectedRange();
        if (sel is null) return;
        for (int r = sel.Value.StartRow; r <= sel.Value.EndRow; r++)
        {
            var groups = Sheet.GetRowGroups().Where(g => (r >= g.Start && r <= g.End) || (Sheet.SummaryRowsBelow ? g.End + 1 == r : g.Start - 1 == r));
            foreach (var g in groups)
                Sheet.SetRowGroupCollapsed(g.Start, g.End, g.Level, false);
        }
        for (int c = sel.Value.StartCol; c <= sel.Value.EndCol; c++)
        {
            var groups = Sheet.GetColumnGroups().Where(g => (c >= g.Start && c <= g.End) || (Sheet.SummaryColumnsRight ? g.End + 1 == c : g.Start - 1 == c));
            foreach (var g in groups)
                Sheet.SetColumnGroupCollapsed(g.Start, g.End, g.Level, false);
        }
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void HideSelectedDetail()
    {
        var sel = Grid.GetSelectedRange();
        if (sel is null) return;
        for (int r = sel.Value.StartRow; r <= sel.Value.EndRow; r++)
        {
            var groups = Sheet.GetRowGroups().Where(g => (r >= g.Start && r <= g.End) || (Sheet.SummaryRowsBelow ? g.End + 1 == r : g.Start - 1 == r));
            foreach (var g in groups)
                Sheet.SetRowGroupCollapsed(g.Start, g.End, g.Level, true);
        }
        for (int c = sel.Value.StartCol; c <= sel.Value.EndCol; c++)
        {
            var groups = Sheet.GetColumnGroups().Where(g => (c >= g.Start && c <= g.End) || (Sheet.SummaryColumnsRight ? g.End + 1 == c : g.Start - 1 == c));
            foreach (var g in groups)
                Sheet.SetColumnGroupCollapsed(g.Start, g.End, g.Level, true);
        }
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public CellHyperlink? GetCellHyperlink(int row, int column) => Sheet.GetCellHyperlink(row, column);
    public void SetCellHyperlink(int row, int column, CellHyperlink? hyperlink) => Sheet.SetCellHyperlink(row, column, hyperlink);
    public void SetCellHyperlink(int row, int column, string target, string? tooltip = null) => Sheet.SetCellHyperlink(row, column, target, tooltip);
    public void ClearCellHyperlink(int row, int column) => Sheet.ClearCellHyperlink(row, column);
    public bool HasCellHyperlink(int row, int column) => Sheet.HasCellHyperlink(row, column);

    public bool NavigateHyperlink(CellHyperlink hyperlink)
    {
        ArgumentNullException.ThrowIfNull(hyperlink);

        var args = new CellHyperlinkNavigatedEventArgs(hyperlink);
        HyperlinkNavigated?.Invoke(this, args);
        if (args.IsHandled)
            return true;

        if (hyperlink.IsInternal)
        {
            return TryNavigateInternalReference(hyperlink.Target);
        }

        return false;
    }

    private bool TryNavigateInternalReference(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        string raw = target.Trim().TrimStart('#').Trim();
        string? sheetName = null;
        string cellRef = raw;

        int bangIdx = raw.IndexOf('!');
        if (bangIdx >= 0)
        {
            sheetName = raw[..bangIdx].Trim().Trim('\'');
            cellRef = raw[(bangIdx + 1)..].Trim();
        }
        else if (raw.Contains('.') && !CellAddress.TryParse(raw, out _))
        {
            int dotIdx = raw.LastIndexOf('.');
            sheetName = raw[..dotIdx].Trim().Trim('\'');
            cellRef = raw[(dotIdx + 1)..].Trim();
        }

        if (CellAddress.TryParse(cellRef, out var cellAddr))
        {
            if (!string.IsNullOrEmpty(sheetName))
            {
                if (Sheet.Workbook is { } wb)
                {
                    if (wb.TryGetSheet(sheetName, out var targetSheet))
                    {
                        int sheetIdx = wb.Sheets.ToList().FindIndex(s => ReferenceEquals(s.Sheet, targetSheet));
                        SetSheet(targetSheet);
                        if (sheetIdx >= 0)
                            wb.Select(sheetIdx);
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            var targetPos = new CellPosition(cellAddr.Row, cellAddr.Column);
            SelectCell(targetPos);
            ScrollCellIntoView(targetPos);
            return true;
        }

        if (Sheet.Workbook is { } workbook && workbook.TryGetSheet(raw.Trim('\''), out var matchingSheet))
        {
            int sheetIdx = workbook.Sheets.ToList().FindIndex(s => ReferenceEquals(s.Sheet, matchingSheet));
            SetSheet(matchingSheet);
            if (sheetIdx >= 0)
                workbook.Select(sheetIdx);
            var a1 = new CellPosition(0, 0);
            SelectCell(a1);
            ScrollCellIntoView(a1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Scrolls the viewport so that the cell at the given row and column is brought into view.
    /// Takes frozen panes into account.
    /// </summary>
    public void ScrollCellIntoView(int row, int column)
    {
        if (row < 0 || row >= _grid.Sheet.RowCount || column < 0 || column >= _grid.Sheet.ColumnCount)
            return;

        double cellLeft = _grid.GetColumnOffset(column);
        double cellWidth = _grid.GetColumnWidth(column);
        double cellRight = cellLeft + cellWidth;

        double cellTop = _grid.GetRowOffset(row);
        double cellHeight = _grid.GetRowHeight(row);
        double cellBottom = cellTop + cellHeight;

        double viewX = _scroll.HorizontalOffset;
        double viewY = _scroll.VerticalOffset;
        double viewWidth = _scroll.Bounds.Width > 0 ? _scroll.Bounds.Width : 600;
        double viewHeight = _scroll.Bounds.Height > 0 ? _scroll.Bounds.Height : 400;

        int frozenCols = Math.Max(_grid.Sheet.FrozenColumns, _grid.Sheet.SplitColumns);
        double frozenWidth = 0;
        for (int c = 0; c < frozenCols; c++) frozenWidth += _grid.GetColumnWidth(c);

        int frozenRows = Math.Max(_grid.Sheet.FrozenRows, _grid.Sheet.SplitRows);
        double frozenHeight = 0;
        for (int r = 0; r < frozenRows; r++) frozenHeight += _grid.GetRowHeight(r);

        double newX = viewX;
        double newY = viewY;

        if (column >= frozenCols)
        {
            double visibleLeft = viewX + frozenWidth;
            double visibleRight = viewX + viewWidth;

            if (cellLeft < visibleLeft)
                newX = Math.Max(0, cellLeft - frozenWidth);
            else if (cellRight > visibleRight)
                newX = Math.Max(0, cellRight - viewWidth);
        }

        if (row >= frozenRows)
        {
            double visibleTop = viewY + frozenHeight;
            double visibleBottom = viewY + viewHeight;

            if (cellTop < visibleTop)
                newY = Math.Max(0, cellTop - frozenHeight);
            else if (cellBottom > visibleBottom)
                newY = Math.Max(0, cellBottom - viewHeight);
        }

        if (Math.Abs(newX - viewX) > 0.5 || Math.Abs(newY - viewY) > 0.5)
        {
            _scroll.SetOffset(new BPoint(newX, newY));
        }
    }

    /// <summary>
    /// Scrolls the viewport so that the cell at the specified position is brought into view.
    /// </summary>
    public void ScrollCellIntoView(CellPosition position) => ScrollCellIntoView(position.Row, position.Column);

    /// <summary>Toggles split panes at the specified cell position or active selection.</summary>
    public void ToggleSplit(int? splitRow = null, int? splitCol = null) =>
        _grid.Sheet.ToggleSplit(splitRow ?? SelectedCell?.Row, splitCol ?? SelectedCell?.Column);

    /// <summary>Sets the split pane boundaries in rows and columns.</summary>
    public void SetSplit(int splitRows, int splitColumns) =>
        _grid.Sheet.SetSplit(splitRows, splitColumns);

    /// <summary>Removes any active split panes.</summary>
    public void RemoveSplit() =>
        _grid.Sheet.RemoveSplit();

    /// <summary>Whether this control's sheet has active split panes.</summary>
    public bool HasSplit => _grid.Sheet.HasSplit;

    /// <summary>The split rows count.</summary>
    public int SplitRows => _grid.Sheet.SplitRows;

    /// <summary>The split columns count.</summary>
    public int SplitColumns => _grid.Sheet.SplitColumns;
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
                return true;
            }

            if (input.Kind == UiInputEventKind.PointerButton && input.MouseButton == MouseButton.Left && input.MouseButtonTransition == MouseButtonTransition.Down)
            {
                int maxRowLvl = _control.Sheet.MaxRowOutlineLevel;
                int maxColLvl = _control.Sheet.MaxColumnOutlineLevel;

                if (maxRowLvl > 0)
                {
                    for (int lvl = 1; lvl <= maxRowLvl + 1; lvl++)
                    {
                        var btnRect = new BRect(Bounds.X + (lvl - 1) * 14.0 + 1, Bounds.Bottom - 14, 12, 12);
                        if (btnRect.Contains(input.Position))
                        {
                            _control.Sheet.CollapseRowOutlineLevel(lvl);
                            _control.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
                            return true;
                        }
                    }
                }

                if (maxColLvl > 0)
                {
                    for (int lvl = 1; lvl <= maxColLvl + 1; lvl++)
                    {
                        var btnRect = new BRect(Bounds.Right - 14, Bounds.Y + (lvl - 1) * 14.0 + 1, 12, 12);
                        if (btnRect.Contains(input.Position))
                        {
                            _control.Sheet.CollapseColumnOutlineLevel(lvl);
                            _control.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
                            return true;
                        }
                    }
                }
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
            int maxRowLvl = _control.Sheet.MaxRowOutlineLevel;
            int maxColLvl = _control.Sheet.MaxColumnOutlineLevel;

            if (maxRowLvl > 0)
            {
                double rowOutlineWidth = maxRowLvl * 14.0;
                list.FillRect(new BRect(Bounds.X + rowOutlineWidth - 1, Bounds.Y, 1, Bounds.Height), _control.Theme.Border);
                for (int lvl = 1; lvl <= maxRowLvl + 1; lvl++)
                {
                    var btnRect = new BRect(Bounds.X + (lvl - 1) * 14.0 + 1, Bounds.Bottom - 14, 12, 12);
                    list.FillRect(btnRect, BColor.White);
                    list.StrokeRect(btnRect, _control.Theme.Border, 1.0);
                    string text = lvl.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    double tw = BTextMeasurer.MeasureAdvance(text, _control.Font);
                    list.DrawText(new BTextRun(text, _control.Font, _control.Theme.Text), new BPoint(btnRect.X + (btnRect.Width - tw) / 2, btnRect.Y + 1));
                }
            }

            if (maxColLvl > 0)
            {
                double colOutlineHeight = maxColLvl * 14.0;
                list.FillRect(new BRect(Bounds.X, Bounds.Y + colOutlineHeight - 1, Bounds.Width, 1), _control.Theme.Border);
                for (int lvl = 1; lvl <= maxColLvl + 1; lvl++)
                {
                    var btnRect = new BRect(Bounds.Right - 14, Bounds.Y + (lvl - 1) * 14.0 + 1, 12, 12);
                    list.FillRect(btnRect, BColor.White);
                    list.StrokeRect(btnRect, _control.Theme.Border, 1.0);
                    string text = lvl.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    double tw = BTextMeasurer.MeasureAdvance(text, _control.Font);
                    list.DrawText(new BTextRun(text, _control.Font, _control.Theme.Text), new BPoint(btnRect.X + (btnRect.Width - tw) / 2, btnRect.Y + 1));
                }
            }

            list.PopClip();
        }

        protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
        {
            Role = UiSemanticRole.Generic,
            State = UiSemanticState.Visible | UiSemanticState.Enabled
        };
    }

    private sealed class HeaderSelectionGesture(CellControl control, UiElement owner, bool column)
    {
        private bool _active;
        private int _lastIndex;
        private BPoint _lastPosition;
        private SheetModel? _sheet;
        private IReadOnlyList<int> _baseline = [];
        private int HitTest(BPoint point, bool clamp)
        {
            double offset;
            if (column)
            {
                double pointerOffset = point.X - owner.Bounds.X;
                int frozenCols = Math.Max(control.Sheet.FrozenColumns, control.Sheet.SplitColumns);
                double frozenWidth = control.Grid.GetColumnOffset(frozenCols);
                if (frozenCols > 0 && pointerOffset >= 0 && pointerOffset < frozenWidth)
                    offset = pointerOffset;
                else
                    offset = pointerOffset + control.ScrollView.HorizontalOffset;
                double length = control.Grid.TotalWidth;
                if (clamp) offset = Math.Clamp(offset, 0, length - 0.001);
                return control.Grid.GetColumnAtOffset(offset);
            }
            else
            {
                double pointerOffset = point.Y - owner.Bounds.Y;
                int frozenRows = Math.Max(control.Sheet.FrozenRows, control.Sheet.SplitRows);
                double frozenHeight = control.Grid.GetRowOffset(frozenRows);
                if (frozenRows > 0 && pointerOffset >= 0 && pointerOffset < frozenHeight)
                    offset = pointerOffset;
                else
                    offset = pointerOffset + control.ScrollView.VerticalOffset;
                double length = control.Grid.TotalHeight;
                if (clamp) offset = Math.Clamp(offset, 0, length - 0.001);
                return control.Grid.GetRowAtOffset(offset);
            }
        }

        public bool Begin(UiInputEvent input)
        {
            if (!control.IsEditingEnabled)
                return false;
            int index = HitTest(input.Position, false);
            if (index < 0)
                return false;
            bool extend = input.KeyModifiers.HasFlag(KeyboardModifierState.Shift);
            bool toggle = input.KeyModifiers.HasFlag(KeyboardModifierState.Control);
            _baseline = toggle ? (column ? control.SelectedColumns : control.SelectedRows) : [];
            if (column)
                control.SelectColumn(index, extend, toggle);
            else
                control.SelectRow(index, extend, toggle);
            _lastIndex = index;
            _lastPosition = input.Position;
            _sheet = control.Sheet;
            _active = true;
            owner.Session?.SetFocus(control.Grid);
            owner.Session?.CaptureInput(owner);
            control.SetCursor(UiCursorShape.Arrow);
            return true;
        }

        public bool Handle(UiInputEvent input)
        {
            if (!_active)
                return false;
            if (!control.IsEditingEnabled || _sheet != control.Sheet || !control.AreHeadersVisible)
            {
                _active = false;
                owner.Session?.ReleaseInputCapture(owner);
                return false;
            }

            bool released = input.Kind == UiInputEventKind.PointerButton &&
                input.MouseButton == MouseButton.Left && input.MouseButtonTransition == MouseButtonTransition.Up;
            if (input.Kind != UiInputEventKind.PointerMove && !released)
                return false;
            int index = HitTest(input.Position, true);
            if (input.Position != _lastPosition && index != _lastIndex)
            {
                control.Grid.ExtendHeaderSelection(index, column, _baseline);
                _lastIndex = index;
            }

            _lastPosition = input.Position;
            if (released)
            {
                _active = false;
                owner.Session?.ReleaseInputCapture(owner);
            }

            return true;
        }
    }

    private sealed class ColumnHeadersElement : UiElement
    {
        private const double DividerHitSlop = 4.0;
        private readonly CellControl _control;
        private readonly HeaderSelectionGesture _selection;
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
            _selection = new HeaderSelectionGesture(control, this, true);
            AccessibleName = "Column headers";
        }

        private int FindColumnDivider(BPoint pointer)
        {
            if (pointer.Y < Bounds.Top || pointer.Y > Bounds.Bottom)
                return -1;
            double offsetX = _control.ScrollView.HorizontalOffset;
            int frozenCols = Math.Max(_control.Sheet.FrozenColumns, _control.Sheet.SplitColumns);
            double frozenWidth = _control.Grid.GetColumnOffset(frozenCols);
            for (int col = 0; col < frozenCols; col++)
            {
                double screenDividerX = Bounds.X + _control.Grid.GetColumnOffset(col + 1);
                if (Math.Abs(pointer.X - screenDividerX) <= DividerHitSlop)
                    return col;
            }

            double localX = pointer.X - Bounds.X + offsetX;
            int cand = _control.Grid.GetColumnAtOffset(localX);
            for (int col = Math.Max(frozenCols, cand - 1); col <= Math.Min(_control.Sheet.ColumnCount - 1, cand + 1); col++)
            {
                double screenDividerX = Bounds.X - offsetX + _control.Grid.GetColumnOffset(col + 1);
                if (screenDividerX < Bounds.X + frozenWidth)
                    continue;

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
            if (_control.Sheet.MaxColumnOutlineLevel > 0 && input.Position.Y < Bounds.Y + _control.Sheet.MaxColumnOutlineLevel * 14.0)
            {
                if (input.Kind == UiInputEventKind.PointerMove)
                {
                    _control.SetCursor(UiCursorShape.Arrow);
                    return true;
                }
                if (input.Kind == UiInputEventKind.PointerButton && input.MouseButton == MouseButton.Left && input.MouseButtonTransition == MouseButtonTransition.Down)
                {
                    int level = 1 + (int)((input.Position.Y - Bounds.Y) / 14.0);
                    double offsetX = _control.ScrollView.HorizontalOffset;
                    int col = _control.Grid.GetColumnAtOffset(input.Position.X - Bounds.X + offsetX);
                    if (col >= 0)
                    {
                        _control.Sheet.ToggleColumnGroup(col, level);
                        _control.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
                        return true;
                    }
                }
                return false;
            }

            if (_selection.Handle(input))
                return true;
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

                _hasClicked = false;
                return _selection.Begin(input);
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
            int frozenCols = Math.Max(_control.Sheet.FrozenColumns, _control.Sheet.SplitColumns);
            double frozenWidth = _control.Grid.GetColumnOffset(frozenCols);

            if (_control.Sheet.MaxColumnOutlineLevel > 0)
            {
                double outlineH = _control.Sheet.MaxColumnOutlineLevel * 14.0;
                list.FillRect(new BRect(Bounds.X, Bounds.Y + outlineH - 1, Bounds.Width, 1), _control.Theme.Border);
                RenderColumnOutlines(list, offsetX, frozenWidth);
            }

            double outlineHeight = _control.Sheet.MaxColumnOutlineLevel * 14.0;
            double letterAreaHeight = Bounds.Height - outlineHeight;
            double textY = outlineHeight + Math.Max(0, (letterAreaHeight - BTextMeasurer.GetLineHeight(_control.Font)) / 2);

            int startCol = frozenCols;
            if (_control.Grid.TotalWidth > 0)
            {
                double leftOffset = Math.Clamp(offsetX + frozenWidth, 0, _control.Grid.TotalWidth - 0.001);
                int foundStart = _control.Grid.GetColumnAtOffset(leftOffset);
                if (foundStart >= 0)
                    startCol = Math.Max(frozenCols, foundStart);
            }

            int endCol = _control.Sheet.ColumnCount - 1;
            if (_control.Grid.TotalWidth > 0)
            {
                double rightOffset = Math.Clamp(offsetX + Bounds.Width, 0, _control.Grid.TotalWidth - 0.001);
                int foundEnd = _control.Grid.GetColumnAtOffset(rightOffset);
                if (foundEnd >= 0)
                    endCol = Math.Min(_control.Sheet.ColumnCount - 1, foundEnd + 1);
            }

            for (int col = startCol; col <= endCol; col++)
            {
                double colLeft = Bounds.X - offsetX + _control.Grid.GetColumnOffset(col);
                double colWidth = _control.Grid.GetColumnWidth(col);
                double colRight = colLeft + colWidth;

                if (colRight <= Bounds.X + frozenWidth || colLeft >= Bounds.Right)
                    continue;

                RenderHeaderCell(list, col, colLeft, colWidth, textY);
            }

            if (frozenCols > 0)
            {
                list.FillRect(new BRect(Bounds.X, Bounds.Y, frozenWidth, Bounds.Height - 1), _control.HeaderBackground);
                for (int col = 0; col < frozenCols; col++)
                {
                    double colLeft = Bounds.X + _control.Grid.GetColumnOffset(col);
                    double colWidth = _control.Grid.GetColumnWidth(col);
                    RenderHeaderCell(list, col, colLeft, colWidth, textY);
                }

                list.FillRect(new BRect(Bounds.X + frozenWidth - 1, Bounds.Y, 2, Bounds.Height), _control.Theme.FocusRing);
            }

            list.PopClip();
        }

        private void RenderHeaderCell(BRenderList list, int col, double colLeft, double colWidth, double textY)
        {
            double outlineHeight = _control.Sheet.MaxColumnOutlineLevel * 14.0;
            double letterY = Bounds.Y + outlineHeight;
            double letterHeight = Bounds.Height - outlineHeight;
            double colRight = colLeft + colWidth;

            if (_control.Grid.IsColumnSelected(col))
                list.FillRect(new BRect(colLeft + 1, letterY, colWidth - 1, letterHeight - 1), _control.Theme.AccentSoft);

            bool isHighlighted = _hoveredDividerIndex == col || (_isDragging && _dragColumnIndex == col);
            BColor dividerColor = isHighlighted ? _control.Theme.FocusRing : _control.Theme.Border;
            double dividerThickness = isHighlighted ? 2 : 1;

            list.FillRect(new BRect(colRight - (isHighlighted ? 1 : 0), letterY, dividerThickness, letterHeight), dividerColor);

            string label = CellPosition.GetColumnLabel(col);
            double labelWidth = BTextMeasurer.MeasureAdvance(label, _control.Font);
            double labelX = colLeft + Math.Max(0, (colWidth - labelWidth) / 2);

            list.PushClip(new BRect(colLeft + 1, letterY, Math.Max(0, colWidth - 1), letterHeight));
            list.DrawText(new BTextRun(label, _control.Font, _control.Grid.IsColumnSelected(col) ? _control.Theme.SelectionText : _control.Theme.TextMuted), new BPoint(labelX, Bounds.Y + textY));
            list.PopClip();
        }

        private void RenderColumnOutlines(BRenderList list, double offsetX, double frozenWidth)
        {
            int maxLevel = _control.Sheet.MaxColumnOutlineLevel;
            if (maxLevel == 0) return;

            var groups = _control.Sheet.GetColumnGroups();

            for (int lvl = 1; lvl <= maxLevel; lvl++)
            {
                double trackY = Bounds.Y + (lvl - 1) * 14.0 + 7.0;

                foreach (var g in groups.Where(grp => grp.Level == lvl))
                {
                    int summaryCol = _control.Sheet.SummaryColumnsRight ? g.End + 1 : Math.Max(0, g.Start - 1);
                    bool isCollapsed = _control.Sheet.IsColumnGroupCollapsed(g.Start, g.End, g.Level);

                    double startLeft = Bounds.X - offsetX + _control.Grid.GetColumnOffset(g.Start);
                    double endRight = Bounds.X - offsetX + _control.Grid.GetColumnOffset(g.End) + _control.Grid.GetColumnWidth(g.End);

                    double sumLeft = Bounds.X - offsetX + _control.Grid.GetColumnOffset(summaryCol);
                    double sumWidth = _control.Grid.GetColumnWidth(summaryCol);

                    if (sumWidth > 0 && sumLeft + sumWidth > Bounds.X && sumLeft < Bounds.Right)
                    {
                        double btnX = sumLeft + (sumWidth - 10) / 2;
                        var btnRect = new BRect(btnX, trackY - 5, 10, 10);
                        list.FillRect(btnRect, BColor.White);
                        list.StrokeRect(btnRect, _control.Theme.TextMuted, 1.0);

                        list.FillRect(new BRect(btnX + 2, trackY - 0.75, 6, 1.5), _control.Theme.Text);
                        if (isCollapsed)
                        {
                            list.FillRect(new BRect(btnX + 4.25, trackY - 3, 1.5, 6), _control.Theme.Text);
                        }
                    }

                    if (!isCollapsed)
                    {
                        if (endRight > Bounds.X && startLeft < Bounds.Right)
                        {
                            double drawLeft = Math.Max(Bounds.X, startLeft + 2);
                            double drawRight = Math.Min(Bounds.Right, endRight - 2);
                            if (drawRight > drawLeft)
                            {
                                list.FillRect(new BRect(drawLeft, trackY - 0.75, drawRight - drawLeft, 1.5), _control.Theme.TextMuted);
                            }
                            if (startLeft >= Bounds.X)
                            {
                                list.FillRect(new BRect(startLeft + 2, trackY, 1.5, 4), _control.Theme.TextMuted);
                            }
                            if (sumWidth > 0 && sumLeft >= Bounds.X)
                            {
                                double btnX = sumLeft + (sumWidth - 10) / 2;
                                list.FillRect(new BRect(endRight - 2, trackY - 0.75, Math.Max(0, btnX - endRight + 2), 1.5), _control.Theme.TextMuted);
                                list.FillRect(new BRect(btnX - 0.75, trackY, 1.5, 4), _control.Theme.TextMuted);
                            }
                        }
                    }
                }
            }
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
        private readonly HeaderSelectionGesture _selection;
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
            _selection = new HeaderSelectionGesture(control, this, false);
            AccessibleName = "Row headers";
        }

        private int FindRowDivider(BPoint pointer)
        {
            if (pointer.X < Bounds.Left || pointer.X > Bounds.Right)
                return -1;
            double offsetY = _control.ScrollView.VerticalOffset;
            int frozenRows = Math.Max(_control.Sheet.FrozenRows, _control.Sheet.SplitRows);
            double frozenHeight = _control.Grid.GetRowOffset(frozenRows);
            for (int row = 0; row < frozenRows; row++)
            {
                double screenDividerY = Bounds.Y + _control.Grid.GetRowOffset(row + 1);
                if (Math.Abs(pointer.Y - screenDividerY) <= DividerHitSlop)
                    return row;
            }

            double localY = pointer.Y - Bounds.Y + offsetY;
            int cand = _control.Grid.GetRowAtOffset(localY);
            for (int row = Math.Max(frozenRows, cand - 1); row <= Math.Min(_control.Sheet.RowCount - 1, cand + 1); row++)
            {
                double screenDividerY = Bounds.Y - offsetY + _control.Grid.GetRowOffset(row + 1);
                if (screenDividerY < Bounds.Y + frozenHeight)
                    continue;

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
            if (_control.Sheet.MaxRowOutlineLevel > 0 && input.Position.X < Bounds.X + _control.Sheet.MaxRowOutlineLevel * 14.0)
            {
                if (input.Kind == UiInputEventKind.PointerMove)
                {
                    _control.SetCursor(UiCursorShape.Arrow);
                    return true;
                }
                if (input.Kind == UiInputEventKind.PointerButton && input.MouseButton == MouseButton.Left && input.MouseButtonTransition == MouseButtonTransition.Down)
                {
                    int level = 1 + (int)((input.Position.X - Bounds.X) / 14.0);
                    double offsetY = _control.ScrollView.VerticalOffset;
                    int row = _control.Grid.GetRowAtOffset(input.Position.Y - Bounds.Y + offsetY);
                    if (row >= 0)
                    {
                        _control.Sheet.ToggleRowGroup(row, level);
                        _control.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
                        return true;
                    }
                }
                return false;
            }

            if (_selection.Handle(input))
                return true;
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

                _hasClicked = false;
                return _selection.Begin(input);
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
            int frozenRows = Math.Max(_control.Sheet.FrozenRows, _control.Sheet.SplitRows);
            double frozenHeight = _control.Grid.GetRowOffset(frozenRows);

            if (_control.Sheet.MaxRowOutlineLevel > 0)
            {
                double outlineW = _control.Sheet.MaxRowOutlineLevel * 14.0;
                list.FillRect(new BRect(Bounds.X + outlineW - 1, Bounds.Y, 1, Bounds.Height), _control.Theme.Border);
                RenderRowOutlines(list, offsetY, frozenHeight);
            }

            int startRow = frozenRows;
            if (_control.Grid.TotalHeight > 0)
            {
                double topOffset = Math.Clamp(offsetY + frozenHeight, 0, _control.Grid.TotalHeight - 0.001);
                int foundStart = _control.Grid.GetRowAtOffset(topOffset);
                if (foundStart >= 0)
                    startRow = Math.Max(frozenRows, foundStart);
            }

            int endRow = _control.Sheet.RowCount - 1;
            if (_control.Grid.TotalHeight > 0)
            {
                double bottomOffset = Math.Clamp(offsetY + Bounds.Height, 0, _control.Grid.TotalHeight - 0.001);
                int foundEnd = _control.Grid.GetRowAtOffset(bottomOffset);
                if (foundEnd >= 0)
                    endRow = Math.Min(_control.Sheet.RowCount - 1, foundEnd + 1);
            }

            for (int row = startRow; row <= endRow; row++)
            {
                double rowTop = Bounds.Y - offsetY + _control.Grid.GetRowOffset(row);
                double rowHeight = _control.Grid.GetRowHeight(row);
                double rowBottom = rowTop + rowHeight;

                if (rowBottom <= Bounds.Y + frozenHeight || rowTop >= Bounds.Bottom)
                    continue;

                RenderRowCell(list, row, rowTop, rowHeight);
            }

            if (frozenRows > 0)
            {
                list.FillRect(new BRect(Bounds.X, Bounds.Y, Bounds.Width - 1, frozenHeight), _control.HeaderBackground);
                for (int row = 0; row < frozenRows; row++)
                {
                    double rowTop = Bounds.Y + _control.Grid.GetRowOffset(row);
                    double rowHeight = _control.Grid.GetRowHeight(row);
                    RenderRowCell(list, row, rowTop, rowHeight);
                }

                list.FillRect(new BRect(Bounds.X, Bounds.Y + frozenHeight - 1, Bounds.Width, 2), _control.Theme.FocusRing);
            }

            list.PopClip();
        }

        private void RenderRowCell(BRenderList list, int row, double rowTop, double rowHeight)
        {
            double outlineWidth = _control.Sheet.MaxRowOutlineLevel * 14.0;
            double numX = Bounds.X + outlineWidth;
            double numWidth = Bounds.Width - outlineWidth;

            if (_control.Grid.IsRowSelected(row))
                list.FillRect(new BRect(numX, rowTop + 1, numWidth - 1, rowHeight - 1), _control.Theme.AccentSoft);

            bool isHighlighted = _hoveredDividerIndex == row || (_isDragging && _dragRowIndex == row);
            BColor dividerColor = isHighlighted ? _control.Theme.FocusRing : _control.Theme.Border;
            double dividerThickness = isHighlighted ? 2 : 1;

            list.FillRect(new BRect(numX, rowTop + rowHeight - (isHighlighted ? 1 : 0), numWidth, dividerThickness), dividerColor);

            string label = (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            double labelWidth = BTextMeasurer.MeasureAdvance(label, _control.Font);
            double labelX = numX + Math.Max(0, (numWidth - labelWidth) / 2);
            double textYOffset = Math.Max(0, (rowHeight - BTextMeasurer.GetLineHeight(_control.Font)) / 2);
            double labelY = rowTop + textYOffset;

            list.PushClip(new BRect(numX, rowTop + 1, Math.Max(0, numWidth - 1), Math.Max(0, rowHeight - 1)));
            list.DrawText(new BTextRun(label, _control.Font, _control.Grid.IsRowSelected(row) ? _control.Theme.SelectionText : _control.Theme.TextMuted),
                new BPoint(labelX, labelY));
            list.PopClip();
        }

        private void RenderRowOutlines(BRenderList list, double offsetY, double frozenHeight)
        {
            int maxLevel = _control.Sheet.MaxRowOutlineLevel;
            if (maxLevel == 0) return;

            var groups = _control.Sheet.GetRowGroups();

            for (int lvl = 1; lvl <= maxLevel; lvl++)
            {
                double trackX = Bounds.X + (lvl - 1) * 14.0 + 7.0;

                foreach (var g in groups.Where(grp => grp.Level == lvl))
                {
                    int summaryRow = _control.Sheet.SummaryRowsBelow ? g.End + 1 : Math.Max(0, g.Start - 1);
                    bool isCollapsed = _control.Sheet.IsRowGroupCollapsed(g.Start, g.End, g.Level);

                    double startTop = Bounds.Y - offsetY + _control.Grid.GetRowOffset(g.Start);
                    double endBottom = Bounds.Y - offsetY + _control.Grid.GetRowOffset(g.End) + _control.Grid.GetRowHeight(g.End);

                    double sumTop = Bounds.Y - offsetY + _control.Grid.GetRowOffset(summaryRow);
                    double sumHeight = _control.Grid.GetRowHeight(summaryRow);

                    if (sumHeight > 0 && sumTop + sumHeight > Bounds.Y && sumTop < Bounds.Bottom)
                    {
                        double btnY = sumTop + (sumHeight - 10) / 2;
                        var btnRect = new BRect(trackX - 5, btnY, 10, 10);
                        list.FillRect(btnRect, BColor.White);
                        list.StrokeRect(btnRect, _control.Theme.TextMuted, 1.0);

                        list.FillRect(new BRect(trackX - 3, btnY + 4.25, 6, 1.5), _control.Theme.Text);
                        if (isCollapsed)
                        {
                            list.FillRect(new BRect(trackX - 0.75, btnY + 2, 1.5, 6), _control.Theme.Text);
                        }
                    }

                    if (!isCollapsed)
                    {
                        if (endBottom > Bounds.Y && startTop < Bounds.Bottom)
                        {
                            double drawTop = Math.Max(Bounds.Y, startTop + 2);
                            double drawBottom = Math.Min(Bounds.Bottom, endBottom - 2);
                            if (drawBottom > drawTop)
                            {
                                list.FillRect(new BRect(trackX - 0.75, drawTop, 1.5, drawBottom - drawTop), _control.Theme.TextMuted);
                            }
                            if (startTop >= Bounds.Y)
                            {
                                list.FillRect(new BRect(trackX, startTop + 2, 4, 1.5), _control.Theme.TextMuted);
                            }
                            if (sumHeight > 0 && sumTop >= Bounds.Y)
                            {
                                double btnY = sumTop + (sumHeight - 10) / 2;
                                list.FillRect(new BRect(trackX - 0.75, endBottom - 2, 1.5, Math.Max(0, btnY - endBottom + 2)), _control.Theme.TextMuted);
                                list.FillRect(new BRect(trackX, btnY - 0.75, 4, 1.5), _control.Theme.TextMuted);
                            }
                        }
                    }
                }
            }
        }

        protected override UiSemanticNode GetSemanticNodeCore() => base.GetSemanticNodeCore() with
        {
            Role = UiSemanticRole.Label,
            State = UiSemanticState.Visible | UiSemanticState.Enabled
        };
    }
}


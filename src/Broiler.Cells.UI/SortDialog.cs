using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Cells.Core;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Cells.UI;

/// <summary>
/// A non-modal floating dialog for multi-column sorting with headers support,
/// multi-level keys, ascending/descending direction, and custom ranges.
/// </summary>
public sealed class SortDialog : UiElement
{
    public const double DialogWidth = 440;
    public const double DialogHeight = 290;
    private const double LevelRowHeight = 28;
    private const int MaxLevels = 4;

    private readonly BFontStyle _headerFont = new("Segoe UI", 11, BFontWeight.SemiBold);
    private readonly BFontStyle _labelFont = new("Segoe UI", 9.5);
    private readonly BFontStyle _boldFont = new("Segoe UI", 9.5, BFontWeight.SemiBold);
    private readonly BFontStyle _colFont = new("Segoe UI", 9.5);

    private readonly StandardLabel _titleLabel;
    private readonly StandardButton _closeButton;
    private readonly StandardButton _addLevelButton;
    private readonly StandardButton _deleteLevelButton;
    private readonly StandardButton _hasHeadersButton;
    private readonly StandardButton _okButton;
    private readonly StandardButton _cancelButton;

    private bool _isVisible;
    private SheetModel? _sheet;
    private CellRangeAddress _range;
    private bool _hasHeaders = true;

    private readonly List<SortLevelItem> _levels = [];
    private int _hoveredColLevel = -1;
    private int _hoveredOrderLevel = -1;

    public bool IsPanelVisible => _isVisible;
    public CellRangeAddress CurrentRange => _range;
    public bool HasHeaders => _hasHeaders;
    public IReadOnlyList<SortLevelItem> Levels => _levels;

    public event Action? SortApplied;
    public event Action? CloseRequested;

    public SortDialog()
    {
        AccessibleName = "Sort Dialog";
        Focusable = true;

        _titleLabel = new StandardLabel
        {
            Text = "Sort",
            Font = new BFontStyle("Segoe UI", 12, BFontWeight.Bold),
            Foreground = CellsPalette.Title,
        };

        _closeButton = new StandardButton
        {
            Text = "\u2715",
            Font = new BFontStyle("Segoe UI", 10),
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = BColor.Transparent,
            Foreground = CellsPalette.Muted,
            PaddingX = 4,
            PaddingY = 1,
        };
        _closeButton.Clicked += (_, _) => Hide();

        _addLevelButton = new StandardButton
        {
            Text = "+ Add Level",
            Font = new BFontStyle("Segoe UI", 9.5),
            CornerRadius = 3,
            Background = CellsPalette.ToolbarButton,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 8,
            PaddingY = 2,
        };
        _addLevelButton.Clicked += (_, _) => AddLevel();

        _deleteLevelButton = new StandardButton
        {
            Text = "\u2212 Delete Level",
            Font = new BFontStyle("Segoe UI", 9.5),
            CornerRadius = 3,
            Background = CellsPalette.ToolbarButton,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 8,
            PaddingY = 2,
        };
        _deleteLevelButton.Clicked += (_, _) => DeleteLevel();

        _hasHeadersButton = new StandardButton
        {
            Text = "\u2611 My data has headers",
            Font = new BFontStyle("Segoe UI", 9.5),
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 8,
            PaddingY = 2,
        };
        _hasHeadersButton.Clicked += (_, _) => ToggleHeaders();

        _okButton = new StandardButton
        {
            Text = "Sort",
            Font = new BFontStyle("Segoe UI", 10, BFontWeight.SemiBold),
            CornerRadius = 3,
            Background = CellsPalette.ToolbarButton,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 16,
            PaddingY = 3,
        };
        _okButton.Clicked += (_, _) => ApplySort();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            Font = new BFontStyle("Segoe UI", 10),
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 12,
            PaddingY = 3,
        };
        _cancelButton.Clicked += (_, _) => Hide();

        AddChild(_titleLabel);
        AddChild(_closeButton);
        AddChild(_addLevelButton);
        AddChild(_deleteLevelButton);
        AddChild(_hasHeadersButton);
        AddChild(_okButton);
        AddChild(_cancelButton);
    }

    public void Show(SheetModel sheet, CellRangeAddress range)
    {
        _sheet = sheet;
        _range = range;
        _hasHeaders = true;
        _hasHeadersButton.Text = "\u2611 My data has headers";

        _levels.Clear();
        _levels.Add(new SortLevelItem
        {
            ColumnIndex = range.Start.Column,
            Ascending = true,
        });

        _isVisible = true;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        _isVisible = false;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        CloseRequested?.Invoke();
    }

    private void AddLevel()
    {
        if (_levels.Count >= MaxLevels || _range.End.Column < _range.Start.Column)
            return;

        int nextCol = _range.Start.Column;
        for (int c = _range.Start.Column; c <= _range.End.Column; c++)
        {
            if (!_levels.Any(lvl => lvl.ColumnIndex == c))
            {
                nextCol = c;
                break;
            }
        }

        _levels.Add(new SortLevelItem
        {
            ColumnIndex = nextCol,
            Ascending = true,
        });

        Invalidate(UiInvalidationKind.Render);
    }

    private void DeleteLevel()
    {
        if (_levels.Count > 1)
        {
            _levels.RemoveAt(_levels.Count - 1);
            Invalidate(UiInvalidationKind.Render);
        }
    }

    private void ToggleHeaders()
    {
        _hasHeaders = !_hasHeaders;
        _hasHeadersButton.Text = _hasHeaders ? "\u2611 My data has headers" : "\u2610 My data has headers";
        Invalidate(UiInvalidationKind.Render);
    }

    private void ApplySort()
    {
        if (_sheet is not null && _levels.Count > 0)
        {
            var keys = _levels.Select(lvl => new SortKey(lvl.ColumnIndex, lvl.Ascending)).ToList();
            TableSortEngine.SortRange(_sheet, _range, keys, _hasHeaders);
            SortApplied?.Invoke();
        }

        Hide();
    }

    public string GetColumnTitle(int columnIndex)
    {
        if (_hasHeaders && _sheet is not null && _range.Start.Row <= _range.End.Row)
        {
            string headerVal = _sheet[_range.Start.Row, columnIndex];
            if (!string.IsNullOrWhiteSpace(headerVal))
            {
                return headerVal.Length > 16 ? headerVal.Substring(0, 14) + "..." : headerVal;
            }
        }

        return $"Column {CellAddress.ColumnToLetters(columnIndex)}";
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        if (!_isVisible)
            return BSize.Empty;

        _titleLabel.Measure(new BSize(120, 24));
        _closeButton.Measure(new BSize(20, 20));
        _addLevelButton.Measure(new BSize(90, 24));
        _deleteLevelButton.Measure(new BSize(100, 24));
        _hasHeadersButton.Measure(new BSize(170, 24));
        _okButton.Measure(new BSize(75, 26));
        _cancelButton.Measure(new BSize(75, 26));

        return new BSize(DialogWidth, DialogHeight);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (!_isVisible)
            return;

        // Title and close
        _titleLabel.Arrange(new BRect(finalRect.X + 16, finalRect.Y + 12, 120, 24));
        _closeButton.Arrange(new BRect(finalRect.Right - 28, finalRect.Y + 12, 20, 20));

        // Toolbar
        double toolY = finalRect.Y + 44;
        _addLevelButton.Arrange(new BRect(finalRect.X + 16, toolY, 90, 24));
        _deleteLevelButton.Arrange(new BRect(finalRect.X + 112, toolY, 100, 24));
        _hasHeadersButton.Arrange(new BRect(finalRect.Right - 186, toolY, 170, 24));

        // Footer
        double footerY = finalRect.Bottom - 36;
        _cancelButton.Arrange(new BRect(finalRect.Right - 88, footerY, 72, 26));
        _okButton.Arrange(new BRect(finalRect.Right - 168, footerY, 72, 26));
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (!_isVisible)
            return false;

        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down)
        {
            if (input.NativeKeyCode == 27 || string.Equals(input.KeyName, "Escape", StringComparison.OrdinalIgnoreCase))
            {
                Hide();
                return true;
            }
            if (input.NativeKeyCode == 13 || string.Equals(input.KeyName, "Enter", StringComparison.OrdinalIgnoreCase))
            {
                ApplySort();
                return true;
            }
        }

        if (input.Kind == UiInputEventKind.PointerMove)
        {
            UpdateHover(input.Position);
            return true;
        }

        if (input.Kind == UiInputEventKind.PointerButton &&
            input.MouseButton == MouseButton.Left &&
            input.MouseButtonTransition == MouseButtonTransition.Down)
        {
            if (!Bounds.Contains(input.Position))
                return false;

            // Check if clicking child buttons
            if (_closeButton.Bounds.Contains(input.Position) ||
                _addLevelButton.Bounds.Contains(input.Position) ||
                _deleteLevelButton.Bounds.Contains(input.Position) ||
                _hasHeadersButton.Bounds.Contains(input.Position) ||
                _okButton.Bounds.Contains(input.Position) ||
                _cancelButton.Bounds.Contains(input.Position))
            {
                return base.OnInput(input);
            }

            // Check sort level items
            double tableY = Bounds.Y + 80;
            double rowY = tableY + 22;

            for (int i = 0; i < _levels.Count; i++)
            {
                var colBtnRect = new BRect(Bounds.X + 80, rowY, 130, 22);
                if (colBtnRect.Contains(input.Position))
                {
                    // Cycle to next column in range
                    int cur = _levels[i].ColumnIndex;
                    int next = cur + 1;
                    if (next > _range.End.Column)
                        next = _range.Start.Column;
                    _levels[i].ColumnIndex = next;
                    Invalidate(UiInvalidationKind.Render);
                    return true;
                }

                var orderBtnRect = new BRect(Bounds.X + 285, rowY, 120, 22);
                if (orderBtnRect.Contains(input.Position))
                {
                    // Toggle Ascending / Descending
                    _levels[i].Ascending = !_levels[i].Ascending;
                    Invalidate(UiInvalidationKind.Render);
                    return true;
                }

                rowY += LevelRowHeight;
            }

            return true;
        }

        return base.OnInput(input);
    }

    private void UpdateHover(BPoint pos)
    {
        int oldCol = _hoveredColLevel;
        int oldOrder = _hoveredOrderLevel;

        _hoveredColLevel = -1;
        _hoveredOrderLevel = -1;

        if (Bounds.Contains(pos))
        {
            double tableY = Bounds.Y + 80;
            double rowY = tableY + 22;

            for (int i = 0; i < _levels.Count; i++)
            {
                var colBtnRect = new BRect(Bounds.X + 80, rowY, 130, 22);
                if (colBtnRect.Contains(pos))
                {
                    _hoveredColLevel = i;
                    break;
                }

                var orderBtnRect = new BRect(Bounds.X + 285, rowY, 120, 22);
                if (orderBtnRect.Contains(pos))
                {
                    _hoveredOrderLevel = i;
                    break;
                }

                rowY += LevelRowHeight;
            }
        }

        if (oldCol != _hoveredColLevel || oldOrder != _hoveredOrderLevel)
        {
            Invalidate(UiInvalidationKind.Render);
        }
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible)
            return;

        var list = context.RenderList;

        // Shadow & dialog surface
        list.FillRect(new BRect(Bounds.X + 2, Bounds.Y + 2, Bounds.Width, Bounds.Height), new BColor(0, 0, 0, 45));
        list.FillRect(Bounds, CellsPalette.Surface);
        list.StrokeRect(Bounds, CellsPalette.WindowBorder, 1);

        // Header divider
        list.FillRect(new BRect(Bounds.X + 16, Bounds.Y + 38, Bounds.Width - 32, 1), CellsPalette.WindowBorder);

        // Levels Table Header
        double tableY = Bounds.Y + 76;
        var tableRect = new BRect(Bounds.X + 16, tableY, Bounds.Width - 32, 140);
        list.FillRect(tableRect, new BColor(255, 255, 255));
        list.StrokeRect(tableRect, CellsPalette.WindowBorder, 1);

        // Table column headers
        var headRowRect = new BRect(tableRect.X, tableRect.Y, tableRect.Width, 22);
        list.FillRect(headRowRect, new BColor(245, 245, 245));
        list.StrokeRect(headRowRect, CellsPalette.WindowBorder, 1);

        list.DrawText(new BTextRun("Level", _boldFont, CellsPalette.Title), new BPoint(tableRect.X + 10, tableRect.Y + 4));
        list.DrawText(new BTextRun("Column", _boldFont, CellsPalette.Title), new BPoint(Bounds.X + 80, tableRect.Y + 4));
        list.DrawText(new BTextRun("Sort On", _boldFont, CellsPalette.Title), new BPoint(Bounds.X + 220, tableRect.Y + 4));
        list.DrawText(new BTextRun("Order", _boldFont, CellsPalette.Title), new BPoint(Bounds.X + 285, tableRect.Y + 4));

        // Render each sort level row
        double rowY = tableY + 24;
        for (int i = 0; i < _levels.Count; i++)
        {
            var level = _levels[i];
            string levelLabel = i == 0 ? "Sort by" : "Then by";
            list.DrawText(new BTextRun(levelLabel, _labelFont, CellsPalette.Title), new BPoint(tableRect.X + 10, rowY + 3));

            // Column selector pill
            var colBtnRect = new BRect(Bounds.X + 80, rowY, 130, 22);
            var colBg = (_hoveredColLevel == i) ? new BColor(220, 235, 252) : new BColor(248, 248, 248);
            list.FillRect(colBtnRect, colBg);
            list.StrokeRect(colBtnRect, CellsPalette.WindowBorder, 1);
            string colText = GetColumnTitle(level.ColumnIndex);
            list.DrawText(new BTextRun(colText + " \u25be", _colFont, CellsPalette.Title), new BPoint(colBtnRect.X + 6, rowY + 3));

            // Sort on: Cell Values
            list.DrawText(new BTextRun("Cell Values", _labelFont, CellsPalette.Muted), new BPoint(Bounds.X + 220, rowY + 3));

            // Order selector pill
            var orderBtnRect = new BRect(Bounds.X + 285, rowY, 120, 22);
            var orderBg = (_hoveredOrderLevel == i) ? new BColor(220, 235, 252) : new BColor(248, 248, 248);
            list.FillRect(orderBtnRect, orderBg);
            list.StrokeRect(orderBtnRect, CellsPalette.WindowBorder, 1);
            string orderText = level.Ascending ? "A to Z \u25be" : "Z to A \u25be";
            list.DrawText(new BTextRun(orderText, _labelFont, CellsPalette.Title), new BPoint(orderBtnRect.X + 6, rowY + 3));

            rowY += LevelRowHeight;
        }

        // Footer divider
        double footerDivY = Bounds.Bottom - 48;
        list.FillRect(new BRect(Bounds.X + 16, footerDivY, Bounds.Width - 32, 1), CellsPalette.WindowBorder);
    }

    public sealed class SortLevelItem
    {
        public int ColumnIndex { get; set; }
        public bool Ascending { get; set; } = true;
    }
}

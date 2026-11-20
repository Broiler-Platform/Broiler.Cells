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
using Broiler.UI.Edit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Cells.UI;

/// <summary>
/// Interactive popup for table AutoFilter header dropdowns, supporting A-Z / Z-A sorting,
/// clearing filters, search filtering, and distinct value multi-selection checklists.
/// </summary>
public sealed class FilterDropdownPopup : UiElement
{
    public const double PopupWidth = 240;
    public const double PopupHeight = 310;
    private const double ActionItemHeight = 24;
    private const double CheckItemHeight = 20;
    private const int MaxVisibleCheckItems = 6;

    private readonly BFontStyle _itemFont = new("Segoe UI", 10);
    private readonly BFontStyle _boldFont = new("Segoe UI", 10, BFontWeight.SemiBold);
    private readonly BFontStyle _headerFont = new("Segoe UI", 9, BFontWeight.SemiBold);

    private readonly StandardEdit _searchEdit;
    private readonly StandardButton _okButton;
    private readonly StandardButton _cancelButton;

    private bool _isVisible;
    private int _column = -1;
    private SheetModel? _sheet;
    private BRect _anchorRect;

    private readonly List<FilterCheckItem> _allItems = [];
    private readonly List<FilterCheckItem> _filteredItems = [];
    private bool _selectAllChecked = true;
    private int _scrollIndex;
    private int _hoveredAction = -1; // 0 = Sort AZ, 1 = Sort ZA, 2 = Clear Filter
    private int _hoveredCheckItem = -2; // -1 = Select All, >= 0 = filtered item index

    public bool IsPopupVisible => _isVisible;
    public int Column => _column;
    public SheetModel? Sheet => _sheet;

    public event Action<int, bool>? SortRequested;
    public event Action<int>? ClearFilterRequested;
    public event Action<int, IReadOnlyList<string>, bool>? FilterApplied;
    public event Action? Closed;

    public FilterDropdownPopup()
    {
        AccessibleName = "Filter Dropdown";
        Focusable = true;

        _searchEdit = new StandardEdit
        {
            AccessibleName = "Search values",
            ToolTipText = "Search values",
            Font = new BFontStyle("Segoe UI", 9.5),
            BorderColor = CellsPalette.WindowBorder,
            Background = CellsPalette.Surface,
            Foreground = CellsPalette.Title,
        };
        _searchEdit.TextChanged += (_, _) => OnSearchChanged();

        _okButton = new StandardButton
        {
            Text = "OK",
            Font = new BFontStyle("Segoe UI", 9.5, BFontWeight.SemiBold),
            CornerRadius = 3,
            Background = CellsPalette.ToolbarButton,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 14,
            PaddingY = 2,
        };
        _okButton.Clicked += (_, _) => ApplyFilterAndClose();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            Font = new BFontStyle("Segoe UI", 9.5),
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 10,
            PaddingY = 2,
        };
        _cancelButton.Clicked += (_, _) => Hide();

        AddChild(_searchEdit);
        AddChild(_okButton);
        AddChild(_cancelButton);
    }

    public void Show(SheetModel sheet, int column, BRect anchorRect)
    {
        _sheet = sheet;
        _column = column;
        _anchorRect = anchorRect;
        _searchEdit.Text = "";
        _scrollIndex = 0;
        _hoveredAction = -1;
        _hoveredCheckItem = -2;

        PopulateItems();

        _isVisible = true;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        _isVisible = false;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        Closed?.Invoke();
    }

    public BRect ComputeDesiredBounds(double viewWidth, double viewHeight)
    {
        if (!_isVisible)
            return BRect.Empty;

        double width = PopupWidth;
        double height = PopupHeight;

        double x = _anchorRect.Left;
        if (x + width > viewWidth)
            x = Math.Max(0, viewWidth - width - 4);

        double y = _anchorRect.Bottom + 2;
        if (y + height > viewHeight && _anchorRect.Top - height >= 0)
            y = _anchorRect.Top - height - 2;

        return new BRect(x, y, width, height);
    }

    private void PopulateItems()
    {
        _allItems.Clear();
        if (_sheet is null || _column < 0)
            return;

        var distinctValues = _sheet.GetDistinctColumnValues(_column, out bool hasBlanks);

        var filterCol = _sheet.AutoFilter?.GetColumn(_column);
        bool isFiltered = filterCol is not null;
        HashSet<string>? selectedSet = isFiltered && filterCol is not null
            ? new HashSet<string>(filterCol.Values, StringComparer.OrdinalIgnoreCase)
            : null;
        bool blanksSelected = (!isFiltered || filterCol is null) || filterCol.IncludeBlanks;

        foreach (var val in distinctValues)
        {
            bool isSelected = selectedSet is null || selectedSet.Contains(val);
            _allItems.Add(new FilterCheckItem
            {
                DisplayText = val,
                Value = val,
                IsBlank = false,
                IsSelected = isSelected,
            });
        }

        if (hasBlanks)
        {
            _allItems.Add(new FilterCheckItem
            {
                DisplayText = "(Blanks)",
                Value = "",
                IsBlank = true,
                IsSelected = blanksSelected,
            });
        }

        ApplySearchFilter();
    }

    private void OnSearchChanged()
    {
        ApplySearchFilter();
        Invalidate(UiInvalidationKind.Render);
    }

    private void ApplySearchFilter()
    {
        _filteredItems.Clear();
        string filter = _searchEdit.Text?.Trim() ?? "";

        foreach (var item in _allItems)
        {
            if (string.IsNullOrEmpty(filter) ||
                item.DisplayText.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _filteredItems.Add(item);
            }
        }

        _selectAllChecked = _filteredItems.Count > 0 && _filteredItems.All(i => i.IsSelected);
        _scrollIndex = 0;
    }

    private void ApplyFilterAndClose()
    {
        if (_sheet is not null && _column >= 0)
        {
            var selectedValues = _allItems
                .Where(it => !it.IsBlank && it.IsSelected)
                .Select(it => it.Value)
                .ToList();
            bool includeBlanks = _allItems.Any(it => it.IsBlank && it.IsSelected);

            _sheet.ApplyFilter(_column, selectedValues, includeBlanks);
            FilterApplied?.Invoke(_column, selectedValues, includeBlanks);
        }

        Hide();
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        if (!_isVisible)
            return BSize.Empty;

        _searchEdit.Measure(new BSize(PopupWidth - 16, 22));
        _okButton.Measure(new BSize(65, 24));
        _cancelButton.Measure(new BSize(65, 24));

        return new BSize(PopupWidth, PopupHeight);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (!_isVisible)
            return;

        double searchY = finalRect.Y + (ActionItemHeight * 3) + 12;
        _searchEdit.Arrange(new BRect(finalRect.X + 8, searchY, finalRect.Width - 16, 22));

        double footerY = finalRect.Bottom - 30;
        _cancelButton.Arrange(new BRect(finalRect.Right - 73, footerY, 65, 24));
        _okButton.Arrange(new BRect(finalRect.Right - 144, footerY, 65, 24));
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
                ApplyFilterAndClose();
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
            {
                Hide();
                return false;
            }

            // Check if clicking SearchEdit or OK/Cancel buttons
            if (_searchEdit.Bounds.Contains(input.Position) ||
                _okButton.Bounds.Contains(input.Position) ||
                _cancelButton.Bounds.Contains(input.Position))
            {
                return base.OnInput(input);
            }

            // Check Action items
            double actionY = Bounds.Y + 4;
            var sortAzRect = new BRect(Bounds.X + 4, actionY, Bounds.Width - 8, ActionItemHeight);
            if (sortAzRect.Contains(input.Position))
            {
                SortRequested?.Invoke(_column, true);
                if (_sheet is not null)
                {
                    TableSortEngine.QuickSort(_sheet, _sheet.AutoFilter?.Range, _column, ascending: true);
                }
                Hide();
                return true;
            }

            actionY += ActionItemHeight;
            var sortZaRect = new BRect(Bounds.X + 4, actionY, Bounds.Width - 8, ActionItemHeight);
            if (sortZaRect.Contains(input.Position))
            {
                SortRequested?.Invoke(_column, false);
                if (_sheet is not null)
                {
                    TableSortEngine.QuickSort(_sheet, _sheet.AutoFilter?.Range, _column, ascending: false);
                }
                Hide();
                return true;
            }

            actionY += ActionItemHeight;
            var clearRect = new BRect(Bounds.X + 4, actionY, Bounds.Width - 8, ActionItemHeight);
            if (clearRect.Contains(input.Position))
            {
                if (_sheet is not null)
                {
                    _sheet.ClearFilter(_column);
                }
                ClearFilterRequested?.Invoke(_column);
                Hide();
                return true;
            }

            // Check checklist area
            double listStartY = Bounds.Y + (ActionItemHeight * 3) + 40;
            var selectAllRect = new BRect(Bounds.X + 8, listStartY, Bounds.Width - 16, CheckItemHeight);
            if (selectAllRect.Contains(input.Position))
            {
                _selectAllChecked = !_selectAllChecked;
                foreach (var item in _filteredItems)
                {
                    item.IsSelected = _selectAllChecked;
                }
                Invalidate(UiInvalidationKind.Render);
                return true;
            }

            double itemY = listStartY + CheckItemHeight;
            for (int i = 0; i < MaxVisibleCheckItems && (_scrollIndex + i) < _filteredItems.Count; i++)
            {
                var itemRect = new BRect(Bounds.X + 8, itemY, Bounds.Width - 16, CheckItemHeight);
                if (itemRect.Contains(input.Position))
                {
                    var item = _filteredItems[_scrollIndex + i];
                    item.IsSelected = !item.IsSelected;
                    _selectAllChecked = _filteredItems.All(it => it.IsSelected);
                    Invalidate(UiInvalidationKind.Render);
                    return true;
                }
                itemY += CheckItemHeight;
            }

            // Scroll arrows
            if (_filteredItems.Count > MaxVisibleCheckItems)
            {
                var upArrowRect = new BRect(Bounds.Right - 22, listStartY, 16, 14);
                if (upArrowRect.Contains(input.Position) && _scrollIndex > 0)
                {
                    _scrollIndex--;
                    Invalidate(UiInvalidationKind.Render);
                    return true;
                }
                var downArrowRect = new BRect(Bounds.Right - 22, itemY, 16, 14);
                if (downArrowRect.Contains(input.Position) && _scrollIndex + MaxVisibleCheckItems < _filteredItems.Count)
                {
                    _scrollIndex++;
                    Invalidate(UiInvalidationKind.Render);
                    return true;
                }
            }

            return true;
        }

        return base.OnInput(input);
    }

    private void UpdateHover(BPoint pos)
    {
        int oldAction = _hoveredAction;
        int oldCheck = _hoveredCheckItem;

        _hoveredAction = -1;
        _hoveredCheckItem = -2;

        if (Bounds.Contains(pos))
        {
            double actionY = Bounds.Y + 4;
            if (new BRect(Bounds.X + 4, actionY, Bounds.Width - 8, ActionItemHeight).Contains(pos))
                _hoveredAction = 0;
            else if (new BRect(Bounds.X + 4, actionY + ActionItemHeight, Bounds.Width - 8, ActionItemHeight).Contains(pos))
                _hoveredAction = 1;
            else if (new BRect(Bounds.X + 4, actionY + (ActionItemHeight * 2), Bounds.Width - 8, ActionItemHeight).Contains(pos))
                _hoveredAction = 2;

            double listStartY = Bounds.Y + (ActionItemHeight * 3) + 40;
            if (new BRect(Bounds.X + 8, listStartY, Bounds.Width - 16, CheckItemHeight).Contains(pos))
            {
                _hoveredCheckItem = -1;
            }
            else
            {
                double itemY = listStartY + CheckItemHeight;
                for (int i = 0; i < MaxVisibleCheckItems && (_scrollIndex + i) < _filteredItems.Count; i++)
                {
                    if (new BRect(Bounds.X + 8, itemY, Bounds.Width - 16, CheckItemHeight).Contains(pos))
                    {
                        _hoveredCheckItem = _scrollIndex + i;
                        break;
                    }
                    itemY += CheckItemHeight;
                }
            }
        }

        if (oldAction != _hoveredAction || oldCheck != _hoveredCheckItem)
        {
            Invalidate(UiInvalidationKind.Render);
        }
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible)
            return;

        var list = context.RenderList;

        // Shadow & popup card
        list.FillRect(new BRect(Bounds.X + 2, Bounds.Y + 2, Bounds.Width, Bounds.Height), new BColor(0, 0, 0, 45));
        list.FillRect(Bounds, CellsPalette.Surface);
        list.StrokeRect(Bounds, CellsPalette.WindowBorder, 1);

        // Action 1: Sort A to Z
        double actionY = Bounds.Y + 4;
        RenderActionItem(list, new BRect(Bounds.X + 4, actionY, Bounds.Width - 8, ActionItemHeight),
            "Sort A to Z", _hoveredAction == 0, isSortAz: true);

        // Action 2: Sort Z to A
        actionY += ActionItemHeight;
        RenderActionItem(list, new BRect(Bounds.X + 4, actionY, Bounds.Width - 8, ActionItemHeight),
            "Sort Z to A", _hoveredAction == 1, isSortZa: true);

        // Action 3: Clear Filter
        actionY += ActionItemHeight;
        bool isFiltered = _sheet is not null && _sheet.AutoFilter is not null &&
                          _sheet.AutoFilter.Columns.Any(c => c.ColumnIndex == _column);
        string colName = _column >= 0 ? CellAddress.ColumnToLetters(_column) : "";
        string clearText = isFiltered ? $"Clear Filter From \"{colName}\"" : "Clear Filter";
        RenderActionItem(list, new BRect(Bounds.X + 4, actionY, Bounds.Width - 8, ActionItemHeight),
            clearText, _hoveredAction == 2, isClear: true, enabled: isFiltered);

        // Divider
        actionY += ActionItemHeight + 2;
        list.FillRect(new BRect(Bounds.X + 6, actionY, Bounds.Width - 12, 1), CellsPalette.WindowBorder);

        // Search Edit is arranged by child layout

        // Checklist Box
        double listStartY = Bounds.Y + (ActionItemHeight * 3) + 40;
        double listHeight = (MaxVisibleCheckItems + 1) * CheckItemHeight + 6;
        var boxRect = new BRect(Bounds.X + 8, listStartY - 2, Bounds.Width - 16, listHeight);
        list.FillRect(boxRect, new BColor(255, 255, 255));
        list.StrokeRect(boxRect, CellsPalette.WindowBorder, 1);

        // (Select All)
        var selectAllRect = new BRect(Bounds.X + 10, listStartY, Bounds.Width - 20, CheckItemHeight);
        if (_hoveredCheckItem == -1)
            list.FillRect(selectAllRect, new BColor(235, 243, 253));
        RenderCheckbox(list, selectAllRect.X + 2, selectAllRect.Y + 3, _selectAllChecked);
        list.DrawText(new BTextRun("(Select All)", _boldFont, CellsPalette.Title),
            new BPoint(selectAllRect.X + 20, selectAllRect.Y + 3));

        // Filtered items
        double itemY = listStartY + CheckItemHeight;
        for (int i = 0; i < MaxVisibleCheckItems && (_scrollIndex + i) < _filteredItems.Count; i++)
        {
            int idx = _scrollIndex + i;
            var item = _filteredItems[idx];
            var itemRect = new BRect(Bounds.X + 10, itemY, Bounds.Width - 20, CheckItemHeight);

            if (_hoveredCheckItem == idx)
                list.FillRect(itemRect, new BColor(235, 243, 253));

            RenderCheckbox(list, itemRect.X + 2, itemRect.Y + 3, item.IsSelected);
            list.DrawText(new BTextRun(item.DisplayText, _itemFont, CellsPalette.Title),
                new BPoint(itemRect.X + 20, itemRect.Y + 3));

            itemY += CheckItemHeight;
        }

        // Scroll indicators if needed
        if (_filteredItems.Count > MaxVisibleCheckItems)
        {
            var scrollBarRect = new BRect(boxRect.Right - 10, boxRect.Y + 2, 6, boxRect.Height - 4);
            list.FillRect(scrollBarRect, new BColor(230, 230, 230));

            double thumbRatio = (double)MaxVisibleCheckItems / _filteredItems.Count;
            double thumbHeight = Math.Max(12, scrollBarRect.Height * thumbRatio);
            double maxScroll = _filteredItems.Count - MaxVisibleCheckItems;
            double thumbY = scrollBarRect.Y + ((scrollBarRect.Height - thumbHeight) * (_scrollIndex / maxScroll));
            list.FillRect(new BRect(scrollBarRect.X, thumbY, scrollBarRect.Width, thumbHeight), CellsPalette.Muted);
        }

        // Bottom divider
        double footerDivY = Bounds.Bottom - 38;
        list.FillRect(new BRect(Bounds.X + 6, footerDivY, Bounds.Width - 12, 1), CellsPalette.WindowBorder);
    }

    private void RenderActionItem(BRenderList list, BRect rect, string text, bool isHovered,
        bool isSortAz = false, bool isSortZa = false, bool isClear = false, bool enabled = true)
    {
        if (isHovered && enabled)
        {
            list.FillRect(rect, new BColor(220, 235, 252));
        }

        var iconRect = new BRect(rect.X + 4, rect.Y + 4, 16, 16);
        var iconColor = enabled ? CellsPalette.Title : CellsPalette.Muted;
        var textColor = enabled ? CellsPalette.Title : CellsPalette.Muted;

        if (isSortAz)
        {
            CellsIcons.SortAscending(list, iconRect, iconColor);
        }
        else if (isSortZa)
        {
            CellsIcons.SortDescending(list, iconRect, iconColor);
        }
        else if (isClear)
        {
            CellsIcons.Filter(list, iconRect, iconColor);
        }

        list.DrawText(new BTextRun(text, _itemFont, textColor), new BPoint(rect.X + 26, rect.Y + 4));
    }

    private static void RenderCheckbox(BRenderList list, double x, double y, bool isChecked)
    {
        var box = new BRect(x, y, 13, 13);
        list.FillRect(box, new BColor(255, 255, 255));
        list.StrokeRect(box, isChecked ? CellsPalette.Accent : CellsPalette.WindowBorder, 1);

        if (isChecked)
        {
            list.FillRect(new BRect(x + 2, y + 2, 9, 9), CellsPalette.Accent);
            // White checkmark lines
            list.FillRect(new BRect(x + 4, y + 6, 2, 3), new BColor(255, 255, 255));
            list.FillRect(new BRect(x + 6, y + 4, 2, 5), new BColor(255, 255, 255));
        }
    }

    private sealed class FilterCheckItem
    {
        public string DisplayText { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsBlank { get; set; }
        public bool IsSelected { get; set; } = true;
    }
}

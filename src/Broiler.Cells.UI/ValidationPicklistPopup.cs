using System;
using System.Collections.Generic;
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
using Broiler.UI.Standard;

namespace Broiler.Cells.UI;

/// <summary>
/// A popup picklist displaying the allowed list items for a cell with List Data Validation.
/// </summary>
public sealed class ValidationPicklistPopup : UiElement
{
    private const double ItemHeight = 22;
    private const double MaxPopupHeight = 180;
    private const double MinPopupWidth = 80;
    private const double MaxPopupWidth = 280;

    private readonly BFontStyle _font = new("Segoe UI", 11);
    private IReadOnlyList<string> _items = [];
    private int _hoveredIndex = -1;
    private int _selectedIndex = -1;
    private int _targetRow;
    private int _targetCol;
    private bool _isVisible;
    private BRect _anchorRect;

    public bool IsPopupVisible => _isVisible;
    public IReadOnlyList<string> Items => _items;
    public int TargetRow => _targetRow;
    public int TargetCol => _targetCol;
    public int SelectedIndex => _selectedIndex;

    public event Action<int, int, string>? ItemSelected;
    public event Action? Closed;

    public ValidationPicklistPopup()
    {
        AccessibleName = "Validation Picklist";
        Focusable = true;
    }

    public void Show(int row, int col, IReadOnlyList<string> items, BRect anchorRect, string? currentValue = null)
    {
        _targetRow = row;
        _targetCol = col;
        _items = items ?? [];
        _anchorRect = anchorRect;
        _selectedIndex = -1;
        _hoveredIndex = -1;

        if (currentValue is not null)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i], currentValue, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedIndex = i;
                    _hoveredIndex = i;
                    break;
                }
            }
        }

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

    public void SelectItem(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            string chosen = _items[index];
            Hide();
            ItemSelected?.Invoke(_targetRow, _targetCol, chosen);
        }
    }

    public BRect ComputeDesiredBounds(double viewWidth, double viewHeight)
    {
        if (!_isVisible || _items.Count == 0)
            return BRect.Empty;

        double maxTextW = 60;
        foreach (var item in _items)
        {
            double w = BTextMeasurer.MeasureAdvance(item, _font);
            if (w > maxTextW) maxTextW = w;
        }

        double width = Math.Clamp(Math.Max(_anchorRect.Width, maxTextW + 24), MinPopupWidth, MaxPopupWidth);
        double height = Math.Min(MaxPopupHeight, _items.Count * ItemHeight + 6);

        double x = _anchorRect.Left;
        if (x + width > viewWidth)
            x = Math.Max(0, viewWidth - width - 4);

        double y = _anchorRect.Bottom + 1;
        if (y + height > viewHeight && _anchorRect.Top - height >= 0)
            y = _anchorRect.Top - height - 1;

        return new BRect(x, y, width, height);
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (!_isVisible)
            return false;

        if (input.Kind == UiInputEventKind.PointerMove)
        {
            int idx = GetIndexAt(input.Position.Y);
            if (idx != _hoveredIndex)
            {
                _hoveredIndex = idx;
                InvalidateRender();
            }
            return true;
        }

        if (input.Kind == UiInputEventKind.PointerButton)
        {
            if (input.MouseButton == MouseButton.Left && input.MouseButtonTransition == MouseButtonTransition.Down)
            {
                if (Bounds.Contains(input.Position))
                {
                    int idx = GetIndexAt(input.Position.Y);
                    if (idx >= 0 && idx < _items.Count)
                    {
                        SelectItem(idx);
                        return true;
                    }
                }
                else
                {
                    Hide();
                    return false;
                }
            }
        }

        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down)
        {
            if (input.NativeKeyCode == 27 || string.Equals(input.KeyName, "Escape", StringComparison.OrdinalIgnoreCase))
            {
                Hide();
                return true;
            }
            if (input.NativeKeyCode == 38 || string.Equals(input.KeyName, "Up", StringComparison.OrdinalIgnoreCase))
            {
                _hoveredIndex = Math.Max(0, (_hoveredIndex <= 0 ? _items.Count - 1 : _hoveredIndex - 1));
                InvalidateRender();
                return true;
            }
            if (input.NativeKeyCode == 40 || string.Equals(input.KeyName, "Down", StringComparison.OrdinalIgnoreCase))
            {
                _hoveredIndex = Math.Min(_items.Count - 1, (_hoveredIndex >= _items.Count - 1 ? 0 : _hoveredIndex + 1));
                InvalidateRender();
                return true;
            }
            if (input.NativeKeyCode == 13 || string.Equals(input.KeyName, "Enter", StringComparison.OrdinalIgnoreCase))
            {
                if (_hoveredIndex >= 0 && _hoveredIndex < _items.Count)
                {
                    SelectItem(_hoveredIndex);
                    return true;
                }
            }
        }

        return base.OnInput(input);
    }

    private int GetIndexAt(double y)
    {
        double localY = y - Bounds.Y - 3;
        if (localY < 0) return -1;
        int idx = (int)(localY / ItemHeight);
        return (idx >= 0 && idx < _items.Count) ? idx : -1;
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible || _items.Count == 0)
            return;

        var list = context.RenderList;

        // Shadow
        list.FillRect(new BRect(Bounds.X + 2, Bounds.Y + 2, Bounds.Width, Bounds.Height), new BColor(0, 0, 0, 45));

        // Background
        list.FillRect(Bounds, CellsPalette.Surface);
        list.StrokeRect(Bounds, CellsPalette.WindowBorder, 1);

        double curY = Bounds.Y + 3;
        for (int i = 0; i < _items.Count; i++)
        {
            if (curY + ItemHeight > Bounds.Bottom)
                break;

            var itemRect = new BRect(Bounds.X + 1, curY, Bounds.Width - 2, ItemHeight);
            bool isHovered = (i == _hoveredIndex);
            bool isSelected = (i == _selectedIndex);

            if (isHovered)
                list.FillRect(itemRect, new BColor(220, 235, 252));
            else if (isSelected)
                list.FillRect(itemRect, new BColor(235, 243, 253));

            var textColor = CellsPalette.Title;
            list.DrawText(new BTextRun(_items[i], _font, textColor), new BPoint(itemRect.X + 8, curY + 3));

            curY += ItemHeight;
        }
    }
}

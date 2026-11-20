using System;
using System.Collections.Generic;
using Broiler.Cells.Core;
using Broiler.Documents.Model;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Cells.UI;

/// <summary>
/// A non-modal floating dialog panel for inserting and editing cell hyperlinks (Ctrl+K).
/// Supports external URLs, mailto links, internal sheet/cell targets, and custom tooltips.
/// </summary>
public sealed class HyperlinkPanel : UiElement
{
    public const double PanelWidth = 380;
    public const double PanelHeight = 220;

    private readonly StandardLabel _titleLabel;
    private readonly StandardButton _closeButton;

    private readonly StandardLabel _displayLabel;
    private readonly StandardEdit _displayEdit;

    private readonly StandardLabel _targetLabel;
    private readonly StandardEdit _targetEdit;

    private readonly StandardLabel _tooltipLabel;
    private readonly StandardEdit _tooltipEdit;

    private readonly StandardLabel _quickSheetsLabel;
    private readonly List<StandardButton> _sheetButtons = [];

    private readonly StandardButton _applyButton;
    private readonly StandardButton _removeButton;
    private readonly StandardButton _cancelButton;

    private bool _isVisible;
    private int _targetRow;
    private int _targetColumn;
    private string _cellAddress = string.Empty;
    private WorkbookModel? _workbook;

    public event EventHandler<(int Row, int Column, string Target, string? Tooltip, string? DisplayText)>? HyperlinkApplied;
    public event EventHandler<(int Row, int Column)>? HyperlinkRemoved;
    public event EventHandler? CloseRequested;

    public bool IsPanelVisible => _isVisible;
    public int TargetRow => _targetRow;
    public int TargetColumn => _targetColumn;
    public string CellAddressText => _cellAddress;

    public string DisplayText
    {
        get => _displayEdit.Text;
        set => _displayEdit.Text = value;
    }

    public string TargetText
    {
        get => _targetEdit.Text;
        set => _targetEdit.Text = value;
    }

    public string TooltipTextValue
    {
        get => _tooltipEdit.Text;
        set => _tooltipEdit.Text = value;
    }

    public StandardEdit DisplayEdit => _displayEdit;
    public StandardEdit TargetEdit => _targetEdit;
    public StandardEdit TooltipEdit => _tooltipEdit;
    public StandardButton ApplyButton => _applyButton;
    public StandardButton RemoveButton => _removeButton;
    public StandardButton CancelButton => _cancelButton;
    public StandardButton CloseButton => _closeButton;

    public HyperlinkPanel()
    {
        AccessibleName = "Insert / Edit Hyperlink dialog";
        ToolTipText = "Hyperlink dialog";

        _titleLabel = new StandardLabel("Insert Hyperlink", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 12, BFontWeight.Bold),
            Foreground = CellsPalette.Title,
            AccessibleName = "Dialog title",
        };

        _closeButton = CreateSmallButton("✕", "Close (Esc)", Hide);

        _displayLabel = new StandardLabel("Text to display:", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _displayEdit = new StandardEdit
        {
            AccessibleName = "Text to display",
            ToolTipText = "Text displayed in cell",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };
        _displayEdit.Submitted += (_, _) => Apply();

        _targetLabel = new StandardLabel("Link target:", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _targetEdit = new StandardEdit
        {
            AccessibleName = "Link target address",
            ToolTipText = "URL, mailto:, or sheet reference (e.g. #Sheet2!A1)",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };
        _targetEdit.Submitted += (_, _) => Apply();

        _tooltipLabel = new StandardLabel("ScreenTip:", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _tooltipEdit = new StandardEdit
        {
            AccessibleName = "ScreenTip tooltip",
            ToolTipText = "Optional hover tooltip message",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };
        _tooltipEdit.Submitted += (_, _) => Apply();

        _quickSheetsLabel = new StandardLabel("Sheets:", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _applyButton = new StandardButton
        {
            Text = "Apply",
            ToolTipText = "Apply hyperlink (Enter)",
            AccessibleName = "Apply hyperlink",
            CornerRadius = 3,
            Background = CellsPalette.Accent,
            BorderColor = CellsPalette.Accent,
            Foreground = BColor.FromArgb(255, 255, 255, 255),
            Font = new BFontStyle("Segoe UI", 11, BFontWeight.SemiBold),
            PaddingX = 8,
            PaddingY = 3,
        };
        _applyButton.Clicked += (_, _) => Apply();

        _removeButton = CreateSmallButton("Remove Link", "Remove hyperlink from cell", Remove);
        _cancelButton = CreateSmallButton("Cancel", "Cancel (Esc)", Hide);

        AddChild(_titleLabel);
        AddChild(_closeButton);
        AddChild(_displayLabel);
        AddChild(_displayEdit);
        AddChild(_targetLabel);
        AddChild(_targetEdit);
        AddChild(_tooltipLabel);
        AddChild(_tooltipEdit);
        AddChild(_quickSheetsLabel);
        AddChild(_applyButton);
        AddChild(_removeButton);
        AddChild(_cancelButton);
    }

    private static StandardButton CreateSmallButton(string text, string tip, Action action)
    {
        var btn = new StandardButton
        {
            Text = text,
            ToolTipText = tip,
            AccessibleName = tip,
            CornerRadius = 3,
            Background = CellsPalette.ToolbarButton,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            Font = new BFontStyle("Segoe UI", 11),
            PaddingX = 6,
            PaddingY = 3,
        };
        btn.Clicked += (_, _) => action();
        return btn;
    }

    public void Show(int row, int column, string a1Address, string currentText, CellHyperlink? existingLink = null, WorkbookModel? workbook = null)
    {
        _targetRow = row;
        _targetColumn = column;
        _cellAddress = a1Address;
        _workbook = workbook;

        bool hasExisting = existingLink is not null;
        _titleLabel.Text = hasExisting ? $"Edit Hyperlink - {a1Address}" : $"Insert Hyperlink - {a1Address}";
        _displayEdit.Text = !string.IsNullOrEmpty(currentText) ? currentText : string.Empty;
        _targetEdit.Text = existingLink?.Target ?? string.Empty;
        _tooltipEdit.Text = existingLink?.Tooltip ?? string.Empty;
        _removeButton.IsEnabled = hasExisting;

        RefreshSheetButtons();

        _isVisible = true;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);

        if (string.IsNullOrEmpty(_targetEdit.Text))
            _targetEdit.Focus();
        else
            _displayEdit.Focus();
    }

    private void RefreshSheetButtons()
    {
        foreach (var btn in _sheetButtons)
        {
            RemoveChild(btn);
        }
        _sheetButtons.Clear();

        if (_workbook is { Sheets: { Count: > 0 } sheets })
        {
            int maxButtons = Math.Min(4, sheets.Count);
            for (int i = 0; i < maxButtons; i++)
            {
                string sheetName = sheets[i].Name;
                var btn = CreateSmallButton(sheetName, $"Insert target #{sheetName}!A1", () =>
                {
                    _targetEdit.Text = $"#{sheetName}!A1";
                    _targetEdit.Focus();
                });
                _sheetButtons.Add(btn);
                AddChild(btn);
            }
        }
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        _isVisible = false;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Apply()
    {
        if (!_isVisible)
            return;

        string target = _targetEdit.Text.Trim();
        if (target.Length == 0)
        {
            Remove();
            return;
        }

        string? tooltip = string.IsNullOrWhiteSpace(_tooltipEdit.Text) ? null : _tooltipEdit.Text.Trim();
        string? displayText = string.IsNullOrWhiteSpace(_displayEdit.Text) ? null : _displayEdit.Text;

        HyperlinkApplied?.Invoke(this, (_targetRow, _targetColumn, target, tooltip, displayText));
        Hide();
    }

    public void Remove()
    {
        if (!_isVisible)
            return;

        HyperlinkRemoved?.Invoke(this, (_targetRow, _targetColumn));
        Hide();
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (!_isVisible)
            return false;

        if (input.Kind == UiInputEventKind.KeyboardKey)
        {
            if (IsKey(input, 27, "Escape"))
            {
                Hide();
                return true;
            }

            if (IsKey(input, 13, "Enter"))
            {
                Apply();
                return true;
            }
        }

        if (input.Kind == UiInputEventKind.PointerButton && Bounds.Contains(input.Position))
        {
            base.OnInput(input);
            return true; // Absorb click inside panel so it doesn't punch through
        }

        return base.OnInput(input);
    }

    private static bool IsKey(UiInputEvent input, int nativeKeyCode, string name) =>
        input.NativeKeyCode == nativeKeyCode ||
        string.Equals(input.KeyName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(input.KeyName, "VirtualKey:" + nativeKeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    protected override BSize MeasureCore(BSize availableSize)
    {
        if (!_isVisible)
            return new BSize(0, 0);

        _titleLabel.Measure(new BSize(PanelWidth - 40, 22));
        _closeButton.Measure(new BSize(24, 24));

        _displayLabel.Measure(new BSize(95, 20));
        _displayEdit.Measure(new BSize(PanelWidth - 125, 24));

        _targetLabel.Measure(new BSize(95, 20));
        _targetEdit.Measure(new BSize(PanelWidth - 125, 24));

        _tooltipLabel.Measure(new BSize(95, 20));
        _tooltipEdit.Measure(new BSize(PanelWidth - 125, 24));

        _quickSheetsLabel.Measure(new BSize(95, 20));
        foreach (var btn in _sheetButtons)
        {
            btn.Measure(new BSize(55, 22));
        }

        _applyButton.Measure(new BSize(85, 26));
        _removeButton.Measure(new BSize(95, 26));
        _cancelButton.Measure(new BSize(75, 26));

        return new BSize(PanelWidth, PanelHeight);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (!_isVisible)
        {
            var zero = new BRect(0, 0, 0, 0);
            _titleLabel.Arrange(zero);
            _closeButton.Arrange(zero);
            _displayLabel.Arrange(zero);
            _displayEdit.Arrange(zero);
            _targetLabel.Arrange(zero);
            _targetEdit.Arrange(zero);
            _tooltipLabel.Arrange(zero);
            _tooltipEdit.Arrange(zero);
            _quickSheetsLabel.Arrange(zero);
            foreach (var btn in _sheetButtons) btn.Arrange(zero);
            _applyButton.Arrange(zero);
            _removeButton.Arrange(zero);
            _cancelButton.Arrange(zero);
            return;
        }

        double left = finalRect.Left;
        double top = finalRect.Top;

        _titleLabel.Arrange(new BRect(left + 12, top + 8, PanelWidth - 48, 22));
        _closeButton.Arrange(new BRect(left + PanelWidth - 32, top + 6, 24, 24));

        double editWidth = PanelWidth - 130;

        _displayLabel.Arrange(new BRect(left + 12, top + 38, 95, 22));
        _displayEdit.Arrange(new BRect(left + 115, top + 36, editWidth, 24));

        _targetLabel.Arrange(new BRect(left + 12, top + 68, 95, 22));
        _targetEdit.Arrange(new BRect(left + 115, top + 66, editWidth, 24));

        _tooltipLabel.Arrange(new BRect(left + 12, top + 98, 95, 22));
        _tooltipEdit.Arrange(new BRect(left + 115, top + 96, editWidth, 24));

        _quickSheetsLabel.Arrange(new BRect(left + 12, top + 128, 95, 22));
        double btnX = left + 115;
        foreach (var btn in _sheetButtons)
        {
            btn.Arrange(new BRect(btnX, top + 126, 55, 22));
            btnX += 60;
        }

        // Action buttons
        _applyButton.Arrange(new BRect(left + 12, top + 172, 85, 28));
        _removeButton.Arrange(new BRect(left + 105, top + 172, 100, 28));
        _cancelButton.Arrange(new BRect(left + PanelWidth - 88, top + 172, 76, 28));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible)
            return;

        var list = context.RenderList;

        // Shadow
        list.FillRect(new BRect(Bounds.X + 3, Bounds.Y + 3, Bounds.Width, Bounds.Height), new BColor(0, 0, 0, 40));

        // Background
        list.FillRect(Bounds, CellsPalette.Surface);
        list.StrokeRect(Bounds, CellsPalette.WindowBorder, 1);

        // Header separator line
        list.FillRect(new BRect(Bounds.X + 8, Bounds.Y + 32, Bounds.Width - 16, 1), CellsPalette.MenuRule);

        // Footer separator line
        list.FillRect(new BRect(Bounds.X + 8, Bounds.Y + 160, Bounds.Width - 16, 1), CellsPalette.MenuRule);

        base.RenderCore(context);
    }
}

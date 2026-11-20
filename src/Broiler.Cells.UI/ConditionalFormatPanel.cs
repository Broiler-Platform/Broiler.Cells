using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Cells.Core;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
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
/// A non-modal floating dialog for creating and managing conditional formatting rules on cell ranges.
/// </summary>
public sealed class ConditionalFormatPanel : UiElement
{
    public const double PanelWidth = 360;
    public const double PanelHeight = 270;

    private readonly StandardLabel _titleLabel;
    private readonly StandardButton _closeButton;

    private readonly StandardLabel _rangeLabel;
    private readonly StandardEdit _rangeEdit;

    private readonly StandardLabel _ruleLabel;
    private readonly StandardButton _btnGreaterThan;
    private readonly StandardButton _btnLessThan;
    private readonly StandardButton _btnBetween;
    private readonly StandardButton _btnContains;
    private readonly StandardButton _btnDuplicates;
    private readonly StandardButton _btnColorScale;

    private readonly StandardLabel _val1Label;
    private readonly StandardEdit _val1Edit;
    private readonly StandardLabel _val2Label;
    private readonly StandardEdit _val2Edit;

    private readonly StandardLabel _styleLabel;
    private readonly StandardButton _btnStyleRed;
    private readonly StandardButton _btnStyleGreen;
    private readonly StandardButton _btnStyleYellow;

    private readonly StandardButton _applyButton;
    private readonly StandardButton _clearButton;
    private readonly StandardButton _cancelButton;

    private bool _isVisible;
    private SheetModel? _sheet;
    private CellRangeAddress _currentRange;

    private ConditionalFormatRuleType _selectedType = ConditionalFormatRuleType.CellIs;
    private ConditionalFormatOperator _selectedOp = ConditionalFormatOperator.GreaterThan;
    private int _selectedStylePreset = 0; // 0 = Red, 1 = Green, 2 = Yellow, 3 = ColorScale

    public event EventHandler? RuleApplied;
    public event EventHandler? RulesCleared;
    public event EventHandler? CloseRequested;

    public bool IsPanelVisible => _isVisible;
    public CellRangeAddress CurrentRange => _currentRange;
    public ConditionalFormatRuleType SelectedType => _selectedType;
    public ConditionalFormatOperator SelectedOperator => _selectedOp;

    public StandardEdit RangeEdit => _rangeEdit;
    public StandardEdit Val1Edit => _val1Edit;
    public StandardEdit Val2Edit => _val2Edit;
    public StandardButton ApplyButton => _applyButton;
    public StandardButton ClearButton => _clearButton;

    public ConditionalFormatPanel()
    {
        AccessibleName = "Conditional Formatting Panel";
        ToolTipText = "Conditional Formatting";

        _titleLabel = new StandardLabel
        {
            Text = "Conditional Formatting",
            AccessibleName = "Conditional Formatting Title",
            Font = new BFontStyle("Segoe UI", 12, BFontWeight.SemiBold),
            Foreground = CellsPalette.Title,
        };

        _closeButton = new StandardButton
        {
            Text = "\u2715",
            AccessibleName = "Close Conditional Formatting",
            ToolTipText = "Close",
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = BColor.Transparent,
            Foreground = CellsPalette.Muted,
            Font = new BFontStyle("Segoe UI", 11),
            PaddingX = 2,
            PaddingY = 1,
        };
        _closeButton.Clicked += (_, _) => Hide();

        _rangeLabel = new StandardLabel
        {
            Text = "Range:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _rangeEdit = new StandardEdit
        {
            AccessibleName = "Format Range",
            ToolTipText = "Cell range (e.g. A1:B10)",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 2,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };

        _ruleLabel = new StandardLabel
        {
            Text = "Rule:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _btnGreaterThan = CreateOptionButton(">", "Greater Than", () => SelectRule(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.GreaterThan));
        _btnLessThan = CreateOptionButton("<", "Less Than", () => SelectRule(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.LessThan));
        _btnBetween = CreateOptionButton("Between", "Between", () => SelectRule(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.Between));
        _btnContains = CreateOptionButton("Text", "Contains Text", () => SelectRule(ConditionalFormatRuleType.ContainsText, ConditionalFormatOperator.None));
        _btnDuplicates = CreateOptionButton("Dupes", "Duplicate Values", () => SelectRule(ConditionalFormatRuleType.DuplicateValues, ConditionalFormatOperator.None));
        _btnColorScale = CreateOptionButton("Scale", "Color Scale", () => SelectRule(ConditionalFormatRuleType.ColorScale, ConditionalFormatOperator.None));

        _val1Label = new StandardLabel
        {
            Text = "Value:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _val1Edit = new StandardEdit
        {
            AccessibleName = "First value",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 2,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Text = "100",
        };

        _val2Label = new StandardLabel
        {
            Text = "and:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _val2Edit = new StandardEdit
        {
            AccessibleName = "Second value",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 2,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Text = "200",
        };

        _styleLabel = new StandardLabel
        {
            Text = "Format Style:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _btnStyleRed = CreateOptionButton("Light Red", "Light Red Fill with Dark Red Text", () => SelectStylePreset(0));
        _btnStyleGreen = CreateOptionButton("Light Green", "Light Green Fill with Dark Green Text", () => SelectStylePreset(1));
        _btnStyleYellow = CreateOptionButton("Light Yellow", "Light Yellow Fill with Dark Yellow Text", () => SelectStylePreset(2));

        _applyButton = CreateActionButton("Apply", "Apply rule", Apply);
        _clearButton = CreateActionButton("Clear", "Clear rules for range", Clear);
        _cancelButton = CreateActionButton("Close", "Close dialog", Hide);

        AddChild(_titleLabel);
        AddChild(_closeButton);
        AddChild(_rangeLabel);
        AddChild(_rangeEdit);
        AddChild(_ruleLabel);
        AddChild(_btnGreaterThan);
        AddChild(_btnLessThan);
        AddChild(_btnBetween);
        AddChild(_btnContains);
        AddChild(_btnDuplicates);
        AddChild(_btnColorScale);
        AddChild(_val1Label);
        AddChild(_val1Edit);
        AddChild(_val2Label);
        AddChild(_val2Edit);
        AddChild(_styleLabel);
        AddChild(_btnStyleRed);
        AddChild(_btnStyleGreen);
        AddChild(_btnStyleYellow);
        AddChild(_applyButton);
        AddChild(_clearButton);
        AddChild(_cancelButton);

        UpdateRuleButtons();
        UpdateStyleButtons();
    }

    private static StandardButton CreateOptionButton(string text, string tip, Action action)
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
            Font = new BFontStyle("Segoe UI", 10),
            PaddingX = 4,
            PaddingY = 1,
        };
        btn.Clicked += (_, _) => action();
        return btn;
    }

    private static StandardButton CreateActionButton(string text, string tip, Action action)
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
            PaddingX = 8,
            PaddingY = 2,
        };
        btn.Clicked += (_, _) => action();
        return btn;
    }

    public void SetSheet(SheetModel sheet)
    {
        _sheet = sheet;
    }

    public void ShowForRange(
        CellRangeAddress range,
        SheetModel? sheet = null,
        ConditionalFormatRuleType ruleType = ConditionalFormatRuleType.CellIs,
        ConditionalFormatOperator op = ConditionalFormatOperator.GreaterThan)
    {
        if (sheet is not null)
            _sheet = sheet;

        _currentRange = range;
        _rangeEdit.Text = range.Format();
        _selectedType = ruleType;
        _selectedOp = op;

        if (ruleType == ConditionalFormatRuleType.ColorScale)
            _selectedStylePreset = 3;

        UpdateRuleButtons();
        UpdateStyleButtons();

        _isVisible = true;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        _val1Edit.Focus();
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        _isVisible = false;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SelectRule(ConditionalFormatRuleType ruleType, ConditionalFormatOperator op)
    {
        _selectedType = ruleType;
        _selectedOp = op;

        if (ruleType == ConditionalFormatRuleType.ColorScale)
            _selectedStylePreset = 3;
        else if (_selectedStylePreset == 3)
            _selectedStylePreset = 0;

        UpdateRuleButtons();
        UpdateStyleButtons();
        Invalidate(UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void SelectStylePreset(int preset)
    {
        _selectedStylePreset = preset;
        UpdateStyleButtons();
        InvalidateRender();
    }

    private void UpdateRuleButtons()
    {
        HighlightButton(_btnGreaterThan, _selectedType == ConditionalFormatRuleType.CellIs && _selectedOp == ConditionalFormatOperator.GreaterThan);
        HighlightButton(_btnLessThan, _selectedType == ConditionalFormatRuleType.CellIs && _selectedOp == ConditionalFormatOperator.LessThan);
        HighlightButton(_btnBetween, _selectedType == ConditionalFormatRuleType.CellIs && _selectedOp == ConditionalFormatOperator.Between);
        HighlightButton(_btnContains, _selectedType == ConditionalFormatRuleType.ContainsText);
        HighlightButton(_btnDuplicates, _selectedType == ConditionalFormatRuleType.DuplicateValues);
        HighlightButton(_btnColorScale, _selectedType == ConditionalFormatRuleType.ColorScale);
    }

    private void UpdateStyleButtons()
    {
        HighlightButton(_btnStyleRed, _selectedStylePreset == 0);
        HighlightButton(_btnStyleGreen, _selectedStylePreset == 1);
        HighlightButton(_btnStyleYellow, _selectedStylePreset == 2);
    }

    private static void HighlightButton(StandardButton btn, bool active)
    {
        btn.Background = active ? new BColor(200, 225, 255) : CellsPalette.ToolbarButton;
        btn.BorderColor = active ? new BColor(0, 100, 200) : CellsPalette.WindowBorder;
    }

    public void Apply()
    {
        if (_sheet is null)
            return;

        if (!CellRangeAddress.TryParse(_rangeEdit.Text.Trim(), out var range))
            range = _currentRange;

        ConditionalFormatStyle? style = null;
        ColorScaleConfiguration? colorScale = null;

        if (_selectedType == ConditionalFormatRuleType.ColorScale)
        {
            colorScale = new ColorScaleConfiguration(
                MinColor: new BColor(248, 105, 107),  // Soft red
                MaxColor: new BColor(99, 190, 123),   // Soft green
                MidColor: new BColor(255, 235, 132)); // Soft yellow
        }
        else
        {
            style = _selectedStylePreset switch
            {
                1 => new ConditionalFormatStyle(
                    Shading: new BColor(198, 239, 206),
                    Foreground: new BColor(0, 97, 0)),
                2 => new ConditionalFormatStyle(
                    Shading: new BColor(255, 235, 156),
                    Foreground: new BColor(156, 101, 0)),
                _ => new ConditionalFormatStyle(
                    Shading: new BColor(255, 199, 206),
                    Foreground: new BColor(156, 0, 6))
            };
        }

        string val1 = _val1Edit.Text.Trim();
        string val2 = _val2Edit.Text.Trim();

        var rule = new ConditionalFormatRule(
            Type: _selectedType,
            Operator: _selectedOp,
            Formula1: val1.Length > 0 ? val1 : null,
            Formula2: _selectedOp == ConditionalFormatOperator.Between && val2.Length > 0 ? val2 : null,
            Style: style,
            ColorScale: colorScale);

        var format = new ConditionalFormat([range], [rule]);
        _sheet.AddConditionalFormat(format);

        RuleApplied?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    public void Clear()
    {
        if (_sheet is null)
            return;

        if (!CellRangeAddress.TryParse(_rangeEdit.Text.Trim(), out var range))
            range = _currentRange;

        var toRemove = new List<ConditionalFormat>();
        foreach (var cf in _sheet.ConditionalFormats)
        {
            foreach (var r in cf.Ranges)
            {
                if (r.StartRow <= range.EndRow && r.EndRow >= range.StartRow &&
                    r.StartCol <= range.EndCol && r.EndCol >= range.StartCol)
                {
                    toRemove.Add(cf);
                    break;
                }
            }
        }

        foreach (var cf in toRemove)
            _sheet.RemoveConditionalFormat(cf);

        RulesCleared?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (!_isVisible)
            return false;

        if (input.Kind == UiInputEventKind.KeyboardKey)
        {
            if (input.NativeKeyCode == 27 || string.Equals(input.KeyName, "Escape", StringComparison.OrdinalIgnoreCase))
            {
                Hide();
                return true;
            }
            if (input.NativeKeyCode == 13 || string.Equals(input.KeyName, "Enter", StringComparison.OrdinalIgnoreCase))
            {
                Apply();
                return true;
            }
        }

        if (input.Kind == UiInputEventKind.PointerButton && Bounds.Contains(input.Position))
        {
            base.OnInput(input);
            return true;
        }

        return base.OnInput(input);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        if (!_isVisible)
            return new BSize(0, 0);

        _titleLabel.Measure(new BSize(280, 22));
        _closeButton.Measure(new BSize(24, 24));
        _rangeLabel.Measure(new BSize(50, 20));
        _rangeEdit.Measure(new BSize(280, 22));

        _ruleLabel.Measure(new BSize(50, 20));
        _btnGreaterThan.Measure(new BSize(36, 22));
        _btnLessThan.Measure(new BSize(36, 22));
        _btnBetween.Measure(new BSize(56, 22));
        _btnContains.Measure(new BSize(46, 22));
        _btnDuplicates.Measure(new BSize(52, 22));
        _btnColorScale.Measure(new BSize(50, 22));

        _val1Label.Measure(new BSize(50, 20));
        _val1Edit.Measure(new BSize(100, 22));
        _val2Label.Measure(new BSize(35, 20));
        _val2Edit.Measure(new BSize(100, 22));

        _styleLabel.Measure(new BSize(80, 20));
        _btnStyleRed.Measure(new BSize(80, 22));
        _btnStyleGreen.Measure(new BSize(85, 22));
        _btnStyleYellow.Measure(new BSize(85, 22));

        _applyButton.Measure(new BSize(80, 26));
        _clearButton.Measure(new BSize(80, 26));
        _cancelButton.Measure(new BSize(80, 26));

        return new BSize(PanelWidth, PanelHeight);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (!_isVisible)
        {
            var zero = new BRect(0, 0, 0, 0);
            _titleLabel.Arrange(zero);
            _closeButton.Arrange(zero);
            _rangeLabel.Arrange(zero);
            _rangeEdit.Arrange(zero);
            _ruleLabel.Arrange(zero);
            _btnGreaterThan.Arrange(zero);
            _btnLessThan.Arrange(zero);
            _btnBetween.Arrange(zero);
            _btnContains.Arrange(zero);
            _btnDuplicates.Arrange(zero);
            _btnColorScale.Arrange(zero);
            _val1Label.Arrange(zero);
            _val1Edit.Arrange(zero);
            _val2Label.Arrange(zero);
            _val2Edit.Arrange(zero);
            _styleLabel.Arrange(zero);
            _btnStyleRed.Arrange(zero);
            _btnStyleGreen.Arrange(zero);
            _btnStyleYellow.Arrange(zero);
            _applyButton.Arrange(zero);
            _clearButton.Arrange(zero);
            _cancelButton.Arrange(zero);
            return;
        }

        double left = finalRect.Left;
        double top = finalRect.Top;

        _titleLabel.Arrange(new BRect(left + 12, top + 8, 300, 22));
        _closeButton.Arrange(new BRect(left + 326, top + 6, 24, 24));

        _rangeLabel.Arrange(new BRect(left + 12, top + 36, 50, 22));
        _rangeEdit.Arrange(new BRect(left + 64, top + 34, 284, 24));

        _ruleLabel.Arrange(new BRect(left + 12, top + 68, 45, 22));
        _btnGreaterThan.Arrange(new BRect(left + 64, top + 66, 36, 24));
        _btnLessThan.Arrange(new BRect(left + 104, top + 66, 36, 24));
        _btnBetween.Arrange(new BRect(left + 144, top + 66, 56, 24));
        _btnContains.Arrange(new BRect(left + 204, top + 66, 44, 24));
        _btnDuplicates.Arrange(new BRect(left + 252, top + 66, 46, 24));
        _btnColorScale.Arrange(new BRect(left + 302, top + 66, 46, 24));

        bool showVal1 = _selectedType != ConditionalFormatRuleType.DuplicateValues && _selectedType != ConditionalFormatRuleType.ColorScale;
        bool showVal2 = _selectedType == ConditionalFormatRuleType.CellIs && _selectedOp == ConditionalFormatOperator.Between;

        if (showVal1)
        {
            _val1Label.Arrange(new BRect(left + 12, top + 100, 48, 22));
            _val1Edit.Arrange(new BRect(left + 64, top + 98, showVal2 ? 110 : 284, 24));
        }
        else
        {
            _val1Label.Arrange(new BRect(0, 0, 0, 0));
            _val1Edit.Arrange(new BRect(0, 0, 0, 0));
        }

        if (showVal2)
        {
            _val2Label.Arrange(new BRect(left + 180, top + 100, 32, 22));
            _val2Edit.Arrange(new BRect(left + 216, top + 98, 132, 24));
        }
        else
        {
            _val2Label.Arrange(new BRect(0, 0, 0, 0));
            _val2Edit.Arrange(new BRect(0, 0, 0, 0));
        }

        _styleLabel.Arrange(new BRect(left + 12, top + 134, 300, 20));
        _btnStyleRed.Arrange(new BRect(left + 12, top + 158, 104, 26));
        _btnStyleGreen.Arrange(new BRect(left + 122, top + 158, 108, 26));
        _btnStyleYellow.Arrange(new BRect(left + 236, top + 158, 112, 26));

        _applyButton.Arrange(new BRect(left + 12, top + 224, 100, 28));
        _clearButton.Arrange(new BRect(left + 118, top + 224, 100, 28));
        _cancelButton.Arrange(new BRect(left + 248, top + 224, 100, 28));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible)
            return;

        var list = context.RenderList;

        // Shadow
        list.FillRect(new BRect(Bounds.X + 3, Bounds.Y + 3, Bounds.Width, Bounds.Height), new BColor(0, 0, 0, 45));

        // Panel Surface
        list.FillRect(Bounds, CellsPalette.Surface);
        list.StrokeRect(Bounds, CellsPalette.WindowBorder, 1);

        // Header separator
        list.FillRect(new BRect(Bounds.X + 8, Bounds.Y + 30, Bounds.Width - 16, 1), CellsPalette.WindowBorder);

        base.RenderCore(context);
    }
}

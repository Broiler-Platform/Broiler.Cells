using System;
using System.Collections.Generic;
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
/// A non-modal floating dialog for creating, configuring, and clearing data validation rules on cell ranges.
/// </summary>
public sealed class DataValidationPanel : UiElement
{
    public const double PanelWidth = 380;
    public const double PanelHeight = 340;

    private readonly StandardLabel _titleLabel;
    private readonly StandardButton _closeButton;

    private readonly StandardLabel _rangeLabel;
    private readonly StandardEdit _rangeEdit;

    private readonly StandardLabel _typeLabel;
    private readonly StandardButton _btnTypeNone;
    private readonly StandardButton _btnTypeWhole;
    private readonly StandardButton _btnTypeDecimal;
    private readonly StandardButton _btnTypeList;
    private readonly StandardButton _btnTypeDate;
    private readonly StandardButton _btnTypeTextLen;
    private readonly StandardButton _btnTypeCustom;

    private readonly StandardLabel _opLabel;
    private readonly StandardButton _btnOpBetween;
    private readonly StandardButton _btnOpNotBetween;
    private readonly StandardButton _btnOpEqual;
    private readonly StandardButton _btnOpNotEqual;
    private readonly StandardButton _btnOpGreaterThan;
    private readonly StandardButton _btnOpLessThan;
    private readonly StandardButton _btnOpGte;
    private readonly StandardButton _btnOpLte;

    private readonly StandardLabel _val1Label;
    private readonly StandardEdit _val1Edit;
    private readonly StandardLabel _val2Label;
    private readonly StandardEdit _val2Edit;

    private readonly StandardButton _btnAllowBlank;
    private readonly StandardButton _btnShowDropdown;

    private readonly StandardLabel _alertLabel;
    private readonly StandardButton _btnAlertStop;
    private readonly StandardButton _btnAlertWarning;
    private readonly StandardButton _btnAlertInfo;

    private readonly StandardLabel _msgLabel;
    private readonly StandardEdit _errorMsgEdit;

    private readonly StandardButton _applyButton;
    private readonly StandardButton _clearButton;
    private readonly StandardButton _cancelButton;

    private bool _isVisible;
    private SheetModel? _sheet;
    private CellRangeAddress _currentRange;

    private DataValidationType _selectedType = DataValidationType.List;
    private DataValidationOperator _selectedOp = DataValidationOperator.Between;
    private DataValidationErrorStyle _selectedErrorStyle = DataValidationErrorStyle.Stop;
    private bool _allowBlank = true;
    private bool _showDropdown = true;

    public event EventHandler? ValidationApplied;
    public event EventHandler? ValidationCleared;
    public event EventHandler? CloseRequested;

    public bool IsPanelVisible => _isVisible;
    public CellRangeAddress CurrentRange => _currentRange;
    public DataValidationType SelectedType => _selectedType;
    public DataValidationOperator SelectedOperator => _selectedOp;
    public DataValidationErrorStyle SelectedErrorStyle => _selectedErrorStyle;
    public bool AllowBlank => _allowBlank;
    public bool ShowDropdown => _showDropdown;

    public StandardEdit RangeEdit => _rangeEdit;
    public StandardEdit Val1Edit => _val1Edit;
    public StandardEdit Val2Edit => _val2Edit;
    public StandardEdit ErrorMsgEdit => _errorMsgEdit;
    public StandardButton ApplyButton => _applyButton;
    public StandardButton ClearButton => _clearButton;

    public DataValidationPanel()
    {
        AccessibleName = "Data Validation Panel";
        ToolTipText = "Data Validation";

        _titleLabel = new StandardLabel
        {
            Text = "Data Validation",
            AccessibleName = "Data Validation Title",
            Font = new BFontStyle("Segoe UI", 12, BFontWeight.SemiBold),
            Foreground = CellsPalette.Title,
        };

        _closeButton = new StandardButton
        {
            Text = "\u2715",
            AccessibleName = "Close Data Validation",
            ToolTipText = "Close",
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = BColor.Transparent,
            Foreground = CellsPalette.Muted,
            Font = new BFontStyle("Segoe UI", 11),
            PaddingX = 4,
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
            AccessibleName = "Validation Range",
            ToolTipText = "Cell range for validation (e.g. A1:A10)",
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            Font = new BFontStyle("Segoe UI", 11),
            PaddingX = 6,
            PaddingY = 2,
        };

        _typeLabel = new StandardLabel
        {
            Text = "Allow:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _btnTypeNone = CreateOptionButton("Any", "Allow any value (remove validation)", () => SelectType(DataValidationType.None));
        _btnTypeWhole = CreateOptionButton("Whole", "Allow whole number", () => SelectType(DataValidationType.WholeNumber));
        _btnTypeDecimal = CreateOptionButton("Dec", "Allow decimal number", () => SelectType(DataValidationType.Decimal));
        _btnTypeList = CreateOptionButton("List", "Allow list of items", () => SelectType(DataValidationType.List));
        _btnTypeDate = CreateOptionButton("Date", "Allow date", () => SelectType(DataValidationType.Date));
        _btnTypeTextLen = CreateOptionButton("Length", "Allow text of specific length", () => SelectType(DataValidationType.TextLength));
        _btnTypeCustom = CreateOptionButton("Custom", "Allow custom formula", () => SelectType(DataValidationType.Custom));

        _opLabel = new StandardLabel
        {
            Text = "Data:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _btnOpBetween = CreateOptionButton("between", "Between", () => SelectOperator(DataValidationOperator.Between));
        _btnOpNotBetween = CreateOptionButton("not betw", "Not Between", () => SelectOperator(DataValidationOperator.NotBetween));
        _btnOpEqual = CreateOptionButton("=", "Equal to", () => SelectOperator(DataValidationOperator.Equal));
        _btnOpNotEqual = CreateOptionButton("\u2260", "Not equal to", () => SelectOperator(DataValidationOperator.NotEqual));
        _btnOpGreaterThan = CreateOptionButton(">", "Greater than", () => SelectOperator(DataValidationOperator.GreaterThan));
        _btnOpLessThan = CreateOptionButton("<", "Less than", () => SelectOperator(DataValidationOperator.LessThan));
        _btnOpGte = CreateOptionButton("\u2265", "Greater than or equal to", () => SelectOperator(DataValidationOperator.GreaterThanOrEqual));
        _btnOpLte = CreateOptionButton("\u2264", "Less than or equal to", () => SelectOperator(DataValidationOperator.LessThanOrEqual));

        _val1Label = new StandardLabel
        {
            Text = "Source:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _val1Edit = new StandardEdit
        {
            AccessibleName = "Validation Formula 1",
            ToolTipText = "List items (comma separated) or minimum value or formula",
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            Font = new BFontStyle("Segoe UI", 11),
            PaddingX = 6,
            PaddingY = 2,
        };

        _val2Label = new StandardLabel
        {
            Text = "Max:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _val2Edit = new StandardEdit
        {
            AccessibleName = "Validation Formula 2",
            ToolTipText = "Maximum value for between operators",
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            Font = new BFontStyle("Segoe UI", 11),
            PaddingX = 6,
            PaddingY = 2,
        };

        _btnAllowBlank = CreateOptionButton("Ignore Blank", "Allow empty or blank cells", ToggleAllowBlank);
        _btnShowDropdown = CreateOptionButton("In-cell dropdown", "Show dropdown button on active cell", ToggleShowDropdown);

        _alertLabel = new StandardLabel
        {
            Text = "Alert:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _btnAlertStop = CreateOptionButton("Stop", "Stop invalid input", () => SelectAlertStyle(DataValidationErrorStyle.Stop));
        _btnAlertWarning = CreateOptionButton("Warning", "Warn before proceeding", () => SelectAlertStyle(DataValidationErrorStyle.Warning));
        _btnAlertInfo = CreateOptionButton("Info", "Display info and proceed", () => SelectAlertStyle(DataValidationErrorStyle.Information));

        _msgLabel = new StandardLabel
        {
            Text = "Message:",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Muted,
        };

        _errorMsgEdit = new StandardEdit
        {
            AccessibleName = "Error Message",
            ToolTipText = "Optional error alert message text",
            CornerRadius = 3,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            Font = new BFontStyle("Segoe UI", 11),
            PaddingX = 6,
            PaddingY = 2,
        };

        _applyButton = CreateActionButton("Apply", "Apply data validation", Apply);
        _clearButton = CreateActionButton("Clear", "Clear data validation for range", Clear);
        _cancelButton = CreateActionButton("Close", "Close data validation panel", Hide);

        AddChild(_titleLabel);
        AddChild(_closeButton);
        AddChild(_rangeLabel);
        AddChild(_rangeEdit);
        AddChild(_typeLabel);
        AddChild(_btnTypeNone);
        AddChild(_btnTypeWhole);
        AddChild(_btnTypeDecimal);
        AddChild(_btnTypeList);
        AddChild(_btnTypeDate);
        AddChild(_btnTypeTextLen);
        AddChild(_btnTypeCustom);
        AddChild(_opLabel);
        AddChild(_btnOpBetween);
        AddChild(_btnOpNotBetween);
        AddChild(_btnOpEqual);
        AddChild(_btnOpNotEqual);
        AddChild(_btnOpGreaterThan);
        AddChild(_btnOpLessThan);
        AddChild(_btnOpGte);
        AddChild(_btnOpLte);
        AddChild(_val1Label);
        AddChild(_val1Edit);
        AddChild(_val2Label);
        AddChild(_val2Edit);
        AddChild(_btnAllowBlank);
        AddChild(_btnShowDropdown);
        AddChild(_alertLabel);
        AddChild(_btnAlertStop);
        AddChild(_btnAlertWarning);
        AddChild(_btnAlertInfo);
        AddChild(_msgLabel);
        AddChild(_errorMsgEdit);
        AddChild(_applyButton);
        AddChild(_clearButton);
        AddChild(_cancelButton);

        UpdateButtons();
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
        DataValidation? existing = null)
    {
        if (sheet is not null)
            _sheet = sheet;

        _currentRange = range;
        _rangeEdit.Text = range.Format();

        if (existing is not null)
        {
            _selectedType = existing.Type;
            _selectedOp = existing.Operator;
            _val1Edit.Text = existing.Formula1 ?? string.Empty;
            _val2Edit.Text = existing.Formula2 ?? string.Empty;
            _allowBlank = existing.AllowBlank;
            _showDropdown = existing.ShowDropdown;
            _selectedErrorStyle = existing.ErrorStyle;
            _errorMsgEdit.Text = existing.ErrorMessage ?? string.Empty;
        }
        else
        {
            _selectedType = DataValidationType.List;
            _selectedOp = DataValidationOperator.Between;
            _val1Edit.Text = string.Empty;
            _val2Edit.Text = string.Empty;
            _allowBlank = true;
            _showDropdown = true;
            _selectedErrorStyle = DataValidationErrorStyle.Stop;
            _errorMsgEdit.Text = string.Empty;
        }

        UpdateLabels();
        UpdateButtons();

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

    public void SelectType(DataValidationType type)
    {
        _selectedType = type;
        UpdateLabels();
        UpdateButtons();
        Invalidate(UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void SelectOperator(DataValidationOperator op)
    {
        _selectedOp = op;
        UpdateButtons();
        Invalidate(UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void SelectAlertStyle(DataValidationErrorStyle style)
    {
        _selectedErrorStyle = style;
        UpdateButtons();
        InvalidateRender();
    }

    public void ToggleAllowBlank()
    {
        _allowBlank = !_allowBlank;
        UpdateButtons();
        InvalidateRender();
    }

    public void ToggleShowDropdown()
    {
        _showDropdown = !_showDropdown;
        UpdateButtons();
        InvalidateRender();
    }

    private void UpdateLabels()
    {
        switch (_selectedType)
        {
            case DataValidationType.List:
                _val1Label.Text = "Source:";
                break;
            case DataValidationType.Custom:
                _val1Label.Text = "Formula:";
                break;
            case DataValidationType.WholeNumber:
            case DataValidationType.Decimal:
            case DataValidationType.Date:
            case DataValidationType.TextLength:
                _val1Label.Text = (_selectedOp is DataValidationOperator.Between or DataValidationOperator.NotBetween) ? "Min:" : "Value:";
                break;
            default:
                _val1Label.Text = "Value:";
                break;
        }
    }

    private void UpdateButtons()
    {
        HighlightButton(_btnTypeNone, _selectedType == DataValidationType.None);
        HighlightButton(_btnTypeWhole, _selectedType == DataValidationType.WholeNumber);
        HighlightButton(_btnTypeDecimal, _selectedType == DataValidationType.Decimal);
        HighlightButton(_btnTypeList, _selectedType == DataValidationType.List);
        HighlightButton(_btnTypeDate, _selectedType == DataValidationType.Date);
        HighlightButton(_btnTypeTextLen, _selectedType == DataValidationType.TextLength);
        HighlightButton(_btnTypeCustom, _selectedType == DataValidationType.Custom);

        HighlightButton(_btnOpBetween, _selectedOp == DataValidationOperator.Between);
        HighlightButton(_btnOpNotBetween, _selectedOp == DataValidationOperator.NotBetween);
        HighlightButton(_btnOpEqual, _selectedOp == DataValidationOperator.Equal);
        HighlightButton(_btnOpNotEqual, _selectedOp == DataValidationOperator.NotEqual);
        HighlightButton(_btnOpGreaterThan, _selectedOp == DataValidationOperator.GreaterThan);
        HighlightButton(_btnOpLessThan, _selectedOp == DataValidationOperator.LessThan);
        HighlightButton(_btnOpGte, _selectedOp == DataValidationOperator.GreaterThanOrEqual);
        HighlightButton(_btnOpLte, _selectedOp == DataValidationOperator.LessThanOrEqual);

        HighlightButton(_btnAllowBlank, _allowBlank);
        HighlightButton(_btnShowDropdown, _showDropdown);

        HighlightButton(_btnAlertStop, _selectedErrorStyle == DataValidationErrorStyle.Stop);
        HighlightButton(_btnAlertWarning, _selectedErrorStyle == DataValidationErrorStyle.Warning);
        HighlightButton(_btnAlertInfo, _selectedErrorStyle == DataValidationErrorStyle.Information);
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

        if (_selectedType == DataValidationType.None)
        {
            _sheet.ClearDataValidations(range);
            ValidationCleared?.Invoke(this, EventArgs.Empty);
            Hide();
            return;
        }

        string val1 = _val1Edit.Text.Trim();
        string val2 = _val2Edit.Text.Trim();
        string errMsg = _errorMsgEdit.Text.Trim();

        bool hasVal2 = _selectedOp is DataValidationOperator.Between or DataValidationOperator.NotBetween;

        var dv = new DataValidation(
            Ranges: [range],
            Type: _selectedType,
            Operator: _selectedOp,
            Formula1: val1.Length > 0 ? val1 : null,
            Formula2: hasVal2 && val2.Length > 0 ? val2 : null,
            AllowBlank: _allowBlank,
            ShowDropdown: _showDropdown,
            ShowInputMessage: false,
            InputTitle: null,
            InputMessage: null,
            ShowErrorMessage: true,
            ErrorTitle: "Invalid Value",
            ErrorMessage: errMsg.Length > 0 ? errMsg : null,
            ErrorStyle: _selectedErrorStyle);

        // Remove any existing validation intersecting this range first to avoid duplicates
        _sheet.ClearDataValidations(range);
        _sheet.AddDataValidation(dv);

        ValidationApplied?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    public void Clear()
    {
        if (_sheet is null)
            return;

        if (!CellRangeAddress.TryParse(_rangeEdit.Text.Trim(), out var range))
            range = _currentRange;

        _sheet.ClearDataValidations(range);
        ValidationCleared?.Invoke(this, EventArgs.Empty);
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

        _titleLabel.Measure(new BSize(300, 22));
        _closeButton.Measure(new BSize(24, 24));
        _rangeLabel.Measure(new BSize(50, 20));
        _rangeEdit.Measure(new BSize(300, 22));

        _typeLabel.Measure(new BSize(50, 20));
        _btnTypeNone.Measure(new BSize(36, 22));
        _btnTypeWhole.Measure(new BSize(46, 22));
        _btnTypeDecimal.Measure(new BSize(38, 22));
        _btnTypeList.Measure(new BSize(38, 22));
        _btnTypeDate.Measure(new BSize(40, 22));
        _btnTypeTextLen.Measure(new BSize(54, 22));
        _btnTypeCustom.Measure(new BSize(52, 22));

        _opLabel.Measure(new BSize(50, 20));
        _btnOpBetween.Measure(new BSize(56, 22));
        _btnOpNotBetween.Measure(new BSize(56, 22));
        _btnOpEqual.Measure(new BSize(28, 22));
        _btnOpNotEqual.Measure(new BSize(28, 22));
        _btnOpGreaterThan.Measure(new BSize(28, 22));
        _btnOpLessThan.Measure(new BSize(28, 22));
        _btnOpGte.Measure(new BSize(28, 22));
        _btnOpLte.Measure(new BSize(28, 22));

        _val1Label.Measure(new BSize(50, 20));
        _val1Edit.Measure(new BSize(120, 22));
        _val2Label.Measure(new BSize(35, 20));
        _val2Edit.Measure(new BSize(120, 22));

        _btnAllowBlank.Measure(new BSize(90, 22));
        _btnShowDropdown.Measure(new BSize(110, 22));

        _alertLabel.Measure(new BSize(50, 20));
        _btnAlertStop.Measure(new BSize(46, 22));
        _btnAlertWarning.Measure(new BSize(60, 22));
        _btnAlertInfo.Measure(new BSize(46, 22));

        _msgLabel.Measure(new BSize(60, 20));
        _errorMsgEdit.Measure(new BSize(280, 22));

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
            _typeLabel.Arrange(zero);
            _btnTypeNone.Arrange(zero);
            _btnTypeWhole.Arrange(zero);
            _btnTypeDecimal.Arrange(zero);
            _btnTypeList.Arrange(zero);
            _btnTypeDate.Arrange(zero);
            _btnTypeTextLen.Arrange(zero);
            _btnTypeCustom.Arrange(zero);
            _opLabel.Arrange(zero);
            _btnOpBetween.Arrange(zero);
            _btnOpNotBetween.Arrange(zero);
            _btnOpEqual.Arrange(zero);
            _btnOpNotEqual.Arrange(zero);
            _btnOpGreaterThan.Arrange(zero);
            _btnOpLessThan.Arrange(zero);
            _btnOpGte.Arrange(zero);
            _btnOpLte.Arrange(zero);
            _val1Label.Arrange(zero);
            _val1Edit.Arrange(zero);
            _val2Label.Arrange(zero);
            _val2Edit.Arrange(zero);
            _btnAllowBlank.Arrange(zero);
            _btnShowDropdown.Arrange(zero);
            _alertLabel.Arrange(zero);
            _btnAlertStop.Arrange(zero);
            _btnAlertWarning.Arrange(zero);
            _btnAlertInfo.Arrange(zero);
            _msgLabel.Arrange(zero);
            _errorMsgEdit.Arrange(zero);
            _applyButton.Arrange(zero);
            _clearButton.Arrange(zero);
            _cancelButton.Arrange(zero);
            return;
        }

        double left = finalRect.Left;
        double top = finalRect.Top;

        _titleLabel.Arrange(new BRect(left + 12, top + 8, 300, 22));
        _closeButton.Arrange(new BRect(left + 346, top + 6, 24, 24));

        _rangeLabel.Arrange(new BRect(left + 12, top + 36, 45, 22));
        _rangeEdit.Arrange(new BRect(left + 60, top + 34, 308, 24));

        _typeLabel.Arrange(new BRect(left + 12, top + 68, 45, 22));
        _btnTypeNone.Arrange(new BRect(left + 60, top + 66, 36, 24));
        _btnTypeWhole.Arrange(new BRect(left + 99, top + 66, 46, 24));
        _btnTypeDecimal.Arrange(new BRect(left + 148, top + 66, 36, 24));
        _btnTypeList.Arrange(new BRect(left + 187, top + 66, 38, 24));
        _btnTypeDate.Arrange(new BRect(left + 228, top + 66, 40, 24));
        _btnTypeTextLen.Arrange(new BRect(left + 271, top + 66, 48, 24));
        _btnTypeCustom.Arrange(new BRect(left + 322, top + 66, 46, 24));

        bool showOp = _selectedType is DataValidationType.WholeNumber or DataValidationType.Decimal or DataValidationType.Date or DataValidationType.TextLength;
        if (showOp)
        {
            _opLabel.Arrange(new BRect(left + 12, top + 98, 45, 22));
            _btnOpBetween.Arrange(new BRect(left + 60, top + 96, 56, 24));
            _btnOpNotBetween.Arrange(new BRect(left + 119, top + 96, 56, 24));
            _btnOpEqual.Arrange(new BRect(left + 178, top + 96, 26, 24));
            _btnOpNotEqual.Arrange(new BRect(left + 207, top + 96, 26, 24));
            _btnOpGreaterThan.Arrange(new BRect(left + 236, top + 96, 26, 24));
            _btnOpLessThan.Arrange(new BRect(left + 265, top + 96, 26, 24));
            _btnOpGte.Arrange(new BRect(left + 294, top + 96, 26, 24));
            _btnOpLte.Arrange(new BRect(left + 323, top + 96, 26, 24));
        }
        else
        {
            var zero = new BRect(0, 0, 0, 0);
            _opLabel.Arrange(zero);
            _btnOpBetween.Arrange(zero);
            _btnOpNotBetween.Arrange(zero);
            _btnOpEqual.Arrange(zero);
            _btnOpNotEqual.Arrange(zero);
            _btnOpGreaterThan.Arrange(zero);
            _btnOpLessThan.Arrange(zero);
            _btnOpGte.Arrange(zero);
            _btnOpLte.Arrange(zero);
        }

        bool showVal1 = _selectedType != DataValidationType.None;
        bool showVal2 = showOp && (_selectedOp is DataValidationOperator.Between or DataValidationOperator.NotBetween);

        if (showVal1)
        {
            _val1Label.Arrange(new BRect(left + 12, top + 128, 50, 22));
            _val1Edit.Arrange(new BRect(left + 64, top + 126, showVal2 ? 120 : 304, 24));
        }
        else
        {
            _val1Label.Arrange(new BRect(0, 0, 0, 0));
            _val1Edit.Arrange(new BRect(0, 0, 0, 0));
        }

        if (showVal2)
        {
            _val2Label.Arrange(new BRect(left + 192, top + 128, 35, 22));
            _val2Edit.Arrange(new BRect(left + 230, top + 126, 138, 24));
        }
        else
        {
            _val2Label.Arrange(new BRect(0, 0, 0, 0));
            _val2Edit.Arrange(new BRect(0, 0, 0, 0));
        }

        // Toggles
        _btnAllowBlank.Arrange(new BRect(left + 64, top + 158, 95, 24));
        if (_selectedType == DataValidationType.List)
        {
            _btnShowDropdown.Arrange(new BRect(left + 166, top + 158, 115, 24));
        }
        else
        {
            _btnShowDropdown.Arrange(new BRect(0, 0, 0, 0));
        }

        // Alert style
        _alertLabel.Arrange(new BRect(left + 12, top + 192, 45, 22));
        _btnAlertStop.Arrange(new BRect(left + 64, top + 190, 48, 24));
        _btnAlertWarning.Arrange(new BRect(left + 116, top + 190, 64, 24));
        _btnAlertInfo.Arrange(new BRect(left + 184, top + 190, 48, 24));

        // Error message
        _msgLabel.Arrange(new BRect(left + 12, top + 224, 60, 22));
        _errorMsgEdit.Arrange(new BRect(left + 74, top + 222, 294, 24));

        // Action buttons
        _applyButton.Arrange(new BRect(left + 12, top + 296, 100, 28));
        _clearButton.Arrange(new BRect(left + 122, top + 296, 100, 28));
        _cancelButton.Arrange(new BRect(left + 268, top + 296, 100, 28));
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

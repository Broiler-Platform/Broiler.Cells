using System;
using Broiler.Cells.Core;
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
/// A lightweight, non-modal floating search and replace bar.
/// Supports find next/prev, match case, whole cell, searching formulas vs values,
/// worksheet vs workbook scope, single replacement, and replace all.
/// </summary>
public sealed class FindReplacePanel : UiElement
{
    public const double PanelWidth = 380;
    public const double FindModeHeight = 72;
    public const double ReplaceModeHeight = 104;

    private readonly StandardEdit _findEdit;
    private readonly StandardLabel _matchCountLabel;
    private readonly StandardButton _prevButton;
    private readonly StandardButton _nextButton;
    private readonly StandardButton _modeToggleButton;
    private readonly StandardButton _closeButton;

    private readonly StandardEdit _replaceEdit;
    private readonly StandardButton _replaceButton;
    private readonly StandardButton _replaceAllButton;

    private readonly StandardButton _caseToggleButton;
    private readonly StandardButton _exactToggleButton;
    private readonly StandardButton _formulaToggleButton;
    private readonly StandardButton _scopeToggleButton;
    private readonly StandardLabel _statusLabel;

    private bool _isVisible;
    private bool _isReplaceMode;
    private bool _matchCase;
    private bool _matchEntireCell;
    private bool _searchFormulas;
    private CellSearchScope _scope = CellSearchScope.Sheet;

    public event EventHandler? FindNextRequested;
    public event EventHandler? FindPreviousRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? ReplaceAllRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? SearchTextChanged;
    public event EventHandler? OptionsChanged;

    public FindReplacePanel()
    {
        AccessibleName = "Find and Replace panel";
        ToolTipText = "Find and Replace";

        _findEdit = new StandardEdit
        {
            AccessibleName = "Find text",
            ToolTipText = "Search text (Press Enter for next, Shift+Enter for previous)",
            Font = new BFontStyle("Segoe UI", 12),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 2,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };

        _matchCountLabel = new StandardLabel(string.Empty, StandardLabelRole.Muted)
        {
            Font = new BFontStyle("Segoe UI", 11),
            Foreground = CellsPalette.Muted,
            AccessibleName = "Match count",
            ToolTipText = "Current match position and total matches",
        };

        _prevButton = CreateSmallButton("▲", "Find previous (Shift+Enter or Shift+F3)", () => FindPreviousRequested?.Invoke(this, EventArgs.Empty));
        _nextButton = CreateSmallButton("▼", "Find next (Enter or F3)", () => FindNextRequested?.Invoke(this, EventArgs.Empty));
        _modeToggleButton = CreateSmallButton("Replace ›", "Toggle Replace mode (Ctrl+H)", ToggleMode);
        _closeButton = CreateSmallButton("✕", "Close (Esc)", Hide);

        _replaceEdit = new StandardEdit
        {
            AccessibleName = "Replace text",
            ToolTipText = "Replacement text (Press Enter to replace)",
            Font = new BFontStyle("Segoe UI", 12),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 2,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };

        _replaceButton = CreateSmallButton("Replace", "Replace current match", () => ReplaceRequested?.Invoke(this, EventArgs.Empty));
        _replaceAllButton = CreateSmallButton("Replace All", "Replace all matches in scope", () => ReplaceAllRequested?.Invoke(this, EventArgs.Empty));

        _caseToggleButton = CreateSmallButton("Aa", "Toggle Match Case", () =>
        {
            _matchCase = !_matchCase;
            UpdateButtonStyles();
            OptionsChanged?.Invoke(this, EventArgs.Empty);
        });

        _exactToggleButton = CreateSmallButton("Whole", "Toggle Match Entire Cell", () =>
        {
            _matchEntireCell = !_matchEntireCell;
            UpdateButtonStyles();
            OptionsChanged?.Invoke(this, EventArgs.Empty);
        });

        _formulaToggleButton = CreateSmallButton("Values", "Toggle search in Values or Formulas", () =>
        {
            _searchFormulas = !_searchFormulas;
            UpdateButtonStyles();
            OptionsChanged?.Invoke(this, EventArgs.Empty);
        });

        _scopeToggleButton = CreateSmallButton("Sheet", "Toggle search scope (Sheet or Entire Workbook)", () =>
        {
            _scope = _scope == CellSearchScope.Sheet ? CellSearchScope.Workbook : CellSearchScope.Sheet;
            UpdateButtonStyles();
            OptionsChanged?.Invoke(this, EventArgs.Empty);
        });

        _statusLabel = new StandardLabel(string.Empty, StandardLabelRole.Muted)
        {
            Font = new BFontStyle("Segoe UI", 11),
            Foreground = CellsPalette.Muted,
            AccessibleName = "Search status",
        };

        _findEdit.Submitted += (_, _) => FindNextRequested?.Invoke(this, EventArgs.Empty);
        _findEdit.TextChanged += (_, _) => SearchTextChanged?.Invoke(this, EventArgs.Empty);
        _replaceEdit.Submitted += (_, _) => ReplaceRequested?.Invoke(this, EventArgs.Empty);

        AddChild(_findEdit);
        AddChild(_matchCountLabel);
        AddChild(_prevButton);
        AddChild(_nextButton);
        AddChild(_modeToggleButton);
        AddChild(_closeButton);

        AddChild(_replaceEdit);
        AddChild(_replaceButton);
        AddChild(_replaceAllButton);

        AddChild(_caseToggleButton);
        AddChild(_exactToggleButton);
        AddChild(_formulaToggleButton);
        AddChild(_scopeToggleButton);
        AddChild(_statusLabel);

        UpdateButtonStyles();
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            }
        }
    }

    public bool IsReplaceMode
    {
        get => _isReplaceMode;
        set
        {
            if (_isReplaceMode != value)
            {
                _isReplaceMode = value;
                UpdateButtonStyles();
                Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            }
        }
    }

    public string SearchText
    {
        get => _findEdit.Text;
        set => _findEdit.Text = value ?? string.Empty;
    }

    public string ReplaceText
    {
        get => _replaceEdit.Text;
        set => _replaceEdit.Text = value ?? string.Empty;
    }

    public bool MatchCase
    {
        get => _matchCase;
        set
        {
            _matchCase = value;
            UpdateButtonStyles();
        }
    }

    public bool MatchEntireCell
    {
        get => _matchEntireCell;
        set
        {
            _matchEntireCell = value;
            UpdateButtonStyles();
        }
    }

    public bool SearchFormulas
    {
        get => _searchFormulas;
        set
        {
            _searchFormulas = value;
            UpdateButtonStyles();
        }
    }

    public CellSearchScope Scope
    {
        get => _scope;
        set
        {
            _scope = value;
            UpdateButtonStyles();
        }
    }

    public double PreferredHeight => _isReplaceMode ? ReplaceModeHeight : FindModeHeight;

    public CellSearchOptions GetOptions() => new()
    {
        SearchText = SearchText,
        ReplaceText = ReplaceText,
        MatchCase = _matchCase,
        MatchEntireCell = _matchEntireCell,
        SearchFormulas = _searchFormulas,
        Scope = _scope,
        WrapAround = true,
    };

    public void ShowFind()
    {
        IsVisible = true;
        IsReplaceMode = false;
        _findEdit.Focus();
    }

    public void ShowReplace()
    {
        IsVisible = true;
        IsReplaceMode = true;
        if (!string.IsNullOrEmpty(SearchText))
            _replaceEdit.Focus();
        else
            _findEdit.Focus();
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        IsVisible = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleMode()
    {
        IsReplaceMode = !IsReplaceMode;
        if (IsReplaceMode && !string.IsNullOrEmpty(SearchText))
            _replaceEdit.Focus();
        else
            _findEdit.Focus();
    }

    public void UpdateMatchCount(int currentMatchIndex, int totalMatches)
    {
        if (totalMatches <= 0)
        {
            _matchCountLabel.Text = string.IsNullOrEmpty(SearchText) ? string.Empty : "0 of 0";
        }
        else
        {
            _matchCountLabel.Text = $"{currentMatchIndex + 1} of {totalMatches}";
        }
        Invalidate(UiInvalidationKind.Render);
    }

    public void SetStatus(string message)
    {
        _statusLabel.Text = message ?? string.Empty;
        Invalidate(UiInvalidationKind.Render);
    }

    private void UpdateButtonStyles()
    {
        _modeToggleButton.Text = _isReplaceMode ? "‹ Find" : "Replace ›";
        _modeToggleButton.ToolTipText = _isReplaceMode ? "Switch to Find only" : "Switch to Find & Replace";

        _caseToggleButton.Background = _matchCase ? CellsPalette.ToolbarButtonActive : CellsPalette.ToolbarButton;
        _caseToggleButton.ToolTipText = $"Match Case: {(_matchCase ? "On" : "Off")}";

        _exactToggleButton.Background = _matchEntireCell ? CellsPalette.ToolbarButtonActive : CellsPalette.ToolbarButton;
        _exactToggleButton.ToolTipText = $"Match Entire Cell: {(_matchEntireCell ? "On" : "Off")}";

        _formulaToggleButton.Text = _searchFormulas ? "Formulas" : "Values";
        _formulaToggleButton.Background = _searchFormulas ? CellsPalette.ToolbarButtonActive : CellsPalette.ToolbarButton;
        _formulaToggleButton.ToolTipText = $"Search In: {(_searchFormulas ? "Formulas" : "Values")}";

        _scopeToggleButton.Text = _scope == CellSearchScope.Sheet ? "Sheet" : "Workbook";
        _scopeToggleButton.Background = _scope == CellSearchScope.Workbook ? CellsPalette.ToolbarButtonActive : CellsPalette.ToolbarButton;
        _scopeToggleButton.ToolTipText = $"Search Scope: {(_scope == CellSearchScope.Sheet ? "Active Sheet" : "Entire Workbook")}";
    }

    private static StandardButton CreateSmallButton(string text, string tip, Action action)
    {
        var btn = new StandardButton
        {
            Text = text,
            ToolTipText = tip,
            PaddingX = 4,
            PaddingY = 2,
            Font = new BFontStyle("Segoe UI", 11),
            Background = CellsPalette.ToolbarButton,
            BorderColor = CellsPalette.ToolbarButtonBorder,
            CornerRadius = 3,
            Foreground = CellsPalette.Title,
        };
        btn.Clicked += (_, _) => action();
        return btn;
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (!_isVisible)
            return false;

        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down)
        {
            if (IsKey(input, 0x1B, "Escape"))
            {
                Hide();
                return true;
            }

            if (IsKey(input, 0x0D, "Enter"))
            {
                if (input.KeyModifiers.HasFlag(KeyboardModifierState.Shift))
                    FindPreviousRequested?.Invoke(this, EventArgs.Empty);
                else
                    FindNextRequested?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (IsKey(input, 0x72, "F3"))
            {
                if (input.KeyModifiers.HasFlag(KeyboardModifierState.Shift))
                    FindPreviousRequested?.Invoke(this, EventArgs.Empty);
                else
                    FindNextRequested?.Invoke(this, EventArgs.Empty);
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

        _findEdit.Measure(new BSize(150, 26));
        _matchCountLabel.Measure(new BSize(62, 24));
        _prevButton.Measure(new BSize(26, 26));
        _nextButton.Measure(new BSize(26, 26));
        _modeToggleButton.Measure(new BSize(62, 26));
        _closeButton.Measure(new BSize(24, 26));

        if (_isReplaceMode)
        {
            _replaceEdit.Measure(new BSize(186, 26));
            _replaceButton.Measure(new BSize(80, 26));
            _replaceAllButton.Measure(new BSize(90, 26));
        }

        _caseToggleButton.Measure(new BSize(36, 24));
        _exactToggleButton.Measure(new BSize(46, 24));
        _formulaToggleButton.Measure(new BSize(62, 24));
        _scopeToggleButton.Measure(new BSize(70, 24));
        _statusLabel.Measure(new BSize(130, 24));

        return new BSize(PanelWidth, PreferredHeight);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (!_isVisible)
        {
            var zero = new BRect(0, 0, 0, 0);
            _findEdit.Arrange(zero);
            _matchCountLabel.Arrange(zero);
            _prevButton.Arrange(zero);
            _nextButton.Arrange(zero);
            _modeToggleButton.Arrange(zero);
            _closeButton.Arrange(zero);
            _replaceEdit.Arrange(zero);
            _replaceButton.Arrange(zero);
            _replaceAllButton.Arrange(zero);
            _caseToggleButton.Arrange(zero);
            _exactToggleButton.Arrange(zero);
            _formulaToggleButton.Arrange(zero);
            _scopeToggleButton.Arrange(zero);
            _statusLabel.Arrange(zero);
            return;
        }

        double left = finalRect.Left;
        double top = finalRect.Top;

        // Row 1: Find row
        _findEdit.Arrange(new BRect(left + 8, top + 6, 150, 26));
        _matchCountLabel.Arrange(new BRect(left + 162, top + 7, 62, 24));
        _prevButton.Arrange(new BRect(left + 228, top + 6, 26, 26));
        _nextButton.Arrange(new BRect(left + 256, top + 6, 26, 26));
        _modeToggleButton.Arrange(new BRect(left + 284, top + 6, 62, 26));
        _closeButton.Arrange(new BRect(left + 348, top + 6, 24, 26));

        // Row 2: Replace row (if active)
        if (_isReplaceMode)
        {
            _replaceEdit.Arrange(new BRect(left + 8, top + 36, 186, 26));
            _replaceButton.Arrange(new BRect(left + 198, top + 36, 80, 26));
            _replaceAllButton.Arrange(new BRect(left + 282, top + 36, 90, 26));
        }
        else
        {
            var zero = new BRect(0, 0, 0, 0);
            _replaceEdit.Arrange(zero);
            _replaceButton.Arrange(zero);
            _replaceAllButton.Arrange(zero);
        }

        // Row 3: Options row
        double optY = top + (_isReplaceMode ? 68 : 38);
        _caseToggleButton.Arrange(new BRect(left + 8, optY, 36, 24));
        _exactToggleButton.Arrange(new BRect(left + 48, optY, 46, 24));
        _formulaToggleButton.Arrange(new BRect(left + 98, optY, 62, 24));
        _scopeToggleButton.Arrange(new BRect(left + 164, optY, 70, 24));

        double statusX = left + 238;
        double statusW = Math.Max(0, finalRect.Right - statusX - 8);
        _statusLabel.Arrange(new BRect(statusX, optY, statusW, 24));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible)
            return;

        // Card background and drop shadow outline
        context.RenderList.FillRoundedRect(Bounds, CellsPalette.Surface, 4, 4);
        context.RenderList.StrokeRoundedRect(Bounds, CellsPalette.WindowBorder, 4, 4, 1.2);

        base.RenderCore(context);
    }
}

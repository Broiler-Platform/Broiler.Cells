using System;
using Broiler.Cells.Core;
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
/// A non-modal error alert dialog/card shown when a cell input violates a Data Validation rule.
/// </summary>
public sealed class ValidationAlertCard : UiElement
{
    public const double CardWidth = 320;
    public const double CardHeight = 120;

    private readonly StandardLabel _titleLabel;
    private readonly StandardLabel _messageLabel;
    private readonly StandardButton _okButton;
    private readonly StandardButton _closeButton;

    private bool _isVisible;
    private ValidationResult? _lastResult;

    public bool IsCardVisible => _isVisible;
    public ValidationResult? LastResult => _lastResult;

    public event Action? Dismissed;

    public ValidationAlertCard()
    {
        AccessibleName = "Validation Alert";

        _titleLabel = new StandardLabel
        {
            Text = "Validation Error",
            Font = new BFontStyle("Segoe UI", 12, BFontWeight.Bold),
            Foreground = CellsPalette.Title,
        };

        _messageLabel = new StandardLabel
        {
            Text = "The value entered is not valid.",
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = CellsPalette.Title,
        };

        _okButton = new StandardButton
        {
            Text = "OK",
            Font = new BFontStyle("Segoe UI", 10, BFontWeight.SemiBold),
            CornerRadius = 3,
            Background = CellsPalette.ToolbarButton,
            BorderColor = CellsPalette.WindowBorder,
            Foreground = CellsPalette.Title,
            PaddingX = 14,
            PaddingY = 2,
        };
        _okButton.Clicked += (_, _) => Dismiss();

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
        _closeButton.Clicked += (_, _) => Dismiss();

        AddChild(_titleLabel);
        AddChild(_messageLabel);
        AddChild(_okButton);
        AddChild(_closeButton);
    }

    public void Show(ValidationResult result)
    {
        _lastResult = result;
        _titleLabel.Text = !string.IsNullOrWhiteSpace(result.ErrorTitle) ? result.ErrorTitle : "Validation Error";
        _messageLabel.Text = !string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.ErrorMessage : "The value entered is not valid.";

        _isVisible = true;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void Dismiss()
    {
        if (!_isVisible)
            return;

        _isVisible = false;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        Dismissed?.Invoke();
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (!_isVisible)
            return false;

        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down)
        {
            if (input.NativeKeyCode == 27 || input.NativeKeyCode == 13 ||
                string.Equals(input.KeyName, "Escape", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input.KeyName, "Enter", StringComparison.OrdinalIgnoreCase))
            {
                Dismiss();
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
            return BSize.Empty;

        _titleLabel.Measure(new BSize(240, 20));
        _messageLabel.Measure(new BSize(280, 40));
        _okButton.Measure(new BSize(60, 24));
        _closeButton.Measure(new BSize(20, 20));

        return new BSize(CardWidth, CardHeight);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (!_isVisible)
        {
            var zero = BRect.Empty;
            _titleLabel.Arrange(zero);
            _messageLabel.Arrange(zero);
            _okButton.Arrange(zero);
            _closeButton.Arrange(zero);
            return;
        }

        double left = finalRect.Left;
        double top = finalRect.Top;

        _titleLabel.Arrange(new BRect(left + 16, top + 12, 250, 20));
        _closeButton.Arrange(new BRect(left + finalRect.Width - 28, top + 10, 20, 20));
        _messageLabel.Arrange(new BRect(left + 16, top + 38, finalRect.Width - 32, 40));
        _okButton.Arrange(new BRect(left + finalRect.Width - 76, top + finalRect.Height - 34, 60, 24));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible)
            return;

        var list = context.RenderList;

        // Shadow
        list.FillRect(new BRect(Bounds.X + 3, Bounds.Y + 3, Bounds.Width, Bounds.Height), new BColor(0, 0, 0, 50));

        // Background
        list.FillRect(Bounds, CellsPalette.Surface);

        // Accent indicator bar based on ErrorStyle
        var accentColor = _lastResult?.ErrorStyle switch
        {
            DataValidationErrorStyle.Warning => new BColor(220, 140, 0),
            DataValidationErrorStyle.Information => new BColor(0, 120, 215),
            _ => new BColor(215, 40, 40)
        };
        list.FillRect(new BRect(Bounds.X, Bounds.Y, 5, Bounds.Height), accentColor);
        list.StrokeRect(Bounds, CellsPalette.WindowBorder, 1);

        base.RenderCore(context);
    }
}

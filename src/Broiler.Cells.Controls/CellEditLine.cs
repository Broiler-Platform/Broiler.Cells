using Broiler.Cells.Core;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Cells.Controls;

/// <summary>
/// A standalone formula and cell-editing bar positioned between the application toolbar
/// and the spreadsheet cell grid. It displays the active cell coordinates and allows
/// inspection and direct editing of the active cell value.
/// </summary>
public sealed class CellEditLine : UiElement
{
    public const double DefaultHeight = 30;
    private const double AddressWidth = 64;
    private const double FxWidth = 26;
    private const double PaddingX = 6;
    private const double Spacing = 6;

    private readonly StandardLabel _addressLabel;
    private readonly StandardLabel _fxLabel;
    private readonly StandardEdit _editBox;

    private CellControl? _cellControl;
    private StandardThemeTokens _theme = StandardThemeTokens.Light;
    private BColor _background = BColor.FromArgb(255, 248, 249, 250);
    private BColor _borderColor = BColor.FromArgb(255, 218, 222, 228);
    private BColor _separatorColor = BColor.FromArgb(255, 225, 229, 235);
    private BColor _boxBackground = BColor.FromArgb(255, 255, 255, 255);

    public CellEditLine()
    {
        AccessibleName = "Cell edit line";

        _addressLabel = new StandardLabel(string.Empty, StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 12, BFontWeight.SemiBold),
            Foreground = _theme.Text,
            AccessibleName = "Cell address",
        };

        _fxLabel = new StandardLabel("fx", StandardLabelRole.Muted)
        {
            Font = new BFontStyle("Segoe UI", 13, BFontWeight.SemiBold, BFontSlant.Italic),
            Foreground = BColor.FromArgb(255, 120, 130, 142),
            AccessibleName = "Formula marker",
        };

        _editBox = new StandardEdit
        {
            Font = new BFontStyle("Segoe UI", 13),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 1,
            Background = _boxBackground,
            BorderColor = _borderColor,
            FocusRing = _theme.FocusRing,
            AccessibleName = "Cell content editor",
        };

        _editBox.Submitted += (_, _) => CommitEdit();

        AddChild(_addressLabel);
        AddChild(_fxLabel);
        AddChild(_editBox);
    }

    public StandardLabel AddressLabel => _addressLabel;
    public StandardLabel FxLabel => _fxLabel;
    public StandardEdit EditBox => _editBox;

    public string AddressText
    {
        get => _addressLabel.Text;
        set => _addressLabel.Text = value ?? string.Empty;
    }

    public string Text
    {
        get => _editBox.Text;
        set => _editBox.Text = value ?? string.Empty;
    }

    public CellControl? AttachedControl => _cellControl;

    public double PreferredHeight => DefaultHeight;

    public event EventHandler<string>? ValueCommitted;
    public event EventHandler? ValueCancelled;

    /// <summary>Attaches this edit line to a CellControl to synchronize selection and edits.</summary>
    public void Attach(CellControl cellControl)
    {
        ArgumentNullException.ThrowIfNull(cellControl);
        if (_cellControl == cellControl)
            return;

        Detach();
        _cellControl = cellControl;
        _cellControl.SelectedCellChanged += OnSelectedCellChanged;
        _cellControl.CellCommitted += OnCellCommitted;

        UpdateFromSelection(_cellControl.SelectedCell);
    }

    /// <summary>Detaches from the currently attached CellControl.</summary>
    public void Detach()
    {
        if (_cellControl is null)
            return;

        _cellControl.SelectedCellChanged -= OnSelectedCellChanged;
        _cellControl.CellCommitted -= OnCellCommitted;
        _cellControl = null;
    }

    public void CommitEdit()
    {
        string text = _editBox.Text;
        if (_cellControl is { SelectedCell: { } pos })
        {
            _cellControl.SetCellValue(pos, text);
        }

        ValueCommitted?.Invoke(this, text);
        _cellControl?.Session?.SetFocus(_cellControl);
        InvalidateRender();
    }

    public void CancelEdit()
    {
        _cellControl?.CancelEdit();
        if (_cellControl is { SelectedCell: { } pos })
        {
            _editBox.Text = _cellControl.Sheet.GetEditText(pos.Row, pos.Column);
        }
        else
        {
            _editBox.Text = string.Empty;
        }

        ValueCancelled?.Invoke(this, EventArgs.Empty);
        _cellControl?.Session?.SetFocus(_cellControl);
        InvalidateRender();
    }

    public void ApplyTheme(StandardThemeTokens theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _background = theme.Surface;
        _boxBackground = theme.Surface;
        _borderColor = theme.Border;
        _separatorColor = theme.Border;
        _addressLabel.Foreground = theme.Text;
        _addressLabel.Font = theme.FontBody;
        _editBox.ApplyTheme(theme);
        _editBox.CornerRadius = 3;
        _editBox.PaddingX = 6;
        _editBox.PaddingY = 1;
        InvalidateRender();
    }

    private void OnSelectedCellChanged(object? sender, CellPosition? position) =>
        UpdateFromSelection(position);

    private void OnCellCommitted(object? sender, CellCommittedEventArgs e)
    {
        if (_cellControl?.SelectedCell == e.Position)
        {
            _editBox.Text = _cellControl.Sheet.GetEditText(e.Position.Row, e.Position.Column);
        }
    }

    private void UpdateFromSelection(CellPosition? position)
    {
        if (position is { } pos && _cellControl is not null)
        {
            _addressLabel.Text = pos.ToAddress();
            _editBox.Text = _cellControl.Sheet.GetEditText(pos.Row, pos.Column);
        }
        else
        {
            _addressLabel.Text = string.Empty;
            _editBox.Text = string.Empty;
        }
        InvalidateRender();
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down)
        {
            if (string.Equals(input.KeyName, "Escape", StringComparison.OrdinalIgnoreCase))
            {
                CancelEdit();
                return true;
            }
            if (string.Equals(input.KeyName, "Enter", StringComparison.OrdinalIgnoreCase))
            {
                CommitEdit();
                return true;
            }
        }

        return base.OnInput(input);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 600 : Math.Max(0, availableSize.Width);
        double height = DefaultHeight;

        double innerHeight = Math.Max(18, height - 6);
        _addressLabel.Measure(new BSize(AddressWidth, innerHeight));
        _fxLabel.Measure(new BSize(FxWidth, innerHeight));

        double editWidth = Math.Max(0, width - (PaddingX * 2) - AddressWidth - FxWidth - (Spacing * 2));
        _editBox.Measure(new BSize(editWidth, innerHeight));

        return new BSize(width, height);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double height = finalRect.Height;
        double innerHeight = Math.Max(18, height - 6);
        double y = finalRect.Top + Math.Max(0, (height - innerHeight) / 2);
        double x = finalRect.Left + PaddingX;

        _addressLabel.Arrange(new BRect(x, y, AddressWidth, innerHeight));
        x += AddressWidth + Spacing;

        _fxLabel.Arrange(new BRect(x, y, FxWidth, innerHeight));
        x += FxWidth + Spacing;

        double editWidth = Math.Max(0, finalRect.Right - PaddingX - x);
        _editBox.Arrange(new BRect(x, y, editWidth, innerHeight));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        var list = context.RenderList;
        list.PushClip(Bounds);

        // Background
        list.FillRect(Bounds, _background);

        // Address box background and border
        BRect addressBox = new(Bounds.Left + PaddingX - 2, Bounds.Top + 2, AddressWidth + 4, Bounds.Height - 5);
        list.FillRoundedRect(addressBox, _boxBackground, 3, 3);
        list.StrokeRoundedRect(addressBox, _borderColor, 3, 3, 1);

        // Bottom separator divider line separating the edit line from the cell grid
        list.FillRect(new BRect(Bounds.Left, Bounds.Bottom - 1, Bounds.Width, 1), _separatorColor);

        base.RenderCore(context);
        list.PopClip();
    }
}

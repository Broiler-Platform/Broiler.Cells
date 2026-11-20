using System;
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
/// A lightweight, non-modal floating note / comment editor panel.
/// Allows viewing, editing, saving, and deleting cell annotations.
/// </summary>
public sealed class NoteEditPanel : UiElement
{
    public const double PanelWidth = 300;
    public const double PanelHeight = 200;

    private readonly StandardLabel _titleLabel;
    private readonly StandardButton _closeButton;
    private readonly StandardLabel _authorLabel;
    private readonly StandardEdit _authorEdit;
    private readonly StandardLabel _textLabel;
    private readonly StandardEdit _textEdit;
    private readonly StandardButton _saveButton;
    private readonly StandardButton _deleteButton;
    private readonly StandardButton _cancelButton;

    private bool _isVisible;
    private int _targetRow;
    private int _targetColumn;
    private string _cellAddress = string.Empty;

    public event EventHandler<(int Row, int Column, string Text, string? Author)>? NoteSaved;
    public event EventHandler<(int Row, int Column)>? NoteDeleted;
    public event EventHandler? CloseRequested;

    public bool IsPanelVisible => _isVisible;
    public int TargetRow => _targetRow;
    public int TargetColumn => _targetColumn;
    public string CellAddressText => _cellAddress;
    public string NoteText
    {
        get => _textEdit.Text;
        set => _textEdit.Text = value;
    }
    public string NoteAuthor
    {
        get => _authorEdit.Text;
        set => _authorEdit.Text = value;
    }
    public StandardEdit TextEdit => _textEdit;
    public StandardEdit AuthorEdit => _authorEdit;
    public StandardButton SaveButton => _saveButton;
    public StandardButton DeleteButton => _deleteButton;
    public StandardButton CancelButton => _cancelButton;
    public StandardButton CloseButton => _closeButton;

    public NoteEditPanel()
    {
        AccessibleName = "Cell Note editor";
        ToolTipText = "Cell Note";

        _titleLabel = new StandardLabel("Cell Note", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 12, BFontWeight.Bold),
            Foreground = new BColor(40, 40, 40),
            AccessibleName = "Note title",
        };

        _closeButton = CreateSmallButton("✕", "Close (Esc)", Hide);

        _authorLabel = new StandardLabel("Author:", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = new BColor(90, 85, 60),
        };

        _authorEdit = new StandardEdit
        {
            AccessibleName = "Author",
            ToolTipText = "Author name (optional)",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 2,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };

        _textLabel = new StandardLabel("Note text:", StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 10),
            Foreground = new BColor(90, 85, 60),
        };

        _textEdit = new StandardEdit
        {
            AccessibleName = "Note text",
            ToolTipText = "Note text (Press Ctrl+Enter to save)",
            Font = new BFontStyle("Segoe UI", 11),
            CornerRadius = 3,
            PaddingX = 6,
            PaddingY = 4,
            Background = CellsPalette.Surface,
            BorderColor = CellsPalette.WindowBorder,
        };
        _textEdit.Submitted += (_, _) => Save();

        _saveButton = CreateSmallButton("Save", "Save note (Ctrl+Enter)", Save);
        _deleteButton = CreateSmallButton("Delete", "Delete note", Delete);
        _cancelButton = CreateSmallButton("Cancel", "Cancel (Esc)", Hide);

        AddChild(_titleLabel);
        AddChild(_closeButton);
        AddChild(_authorLabel);
        AddChild(_authorEdit);
        AddChild(_textLabel);
        AddChild(_textEdit);
        AddChild(_saveButton);
        AddChild(_deleteButton);
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
            PaddingY = 2,
        };
        btn.Clicked += (_, _) => action();
        return btn;
    }

    public void Show(int row, int column, string a1Address, CellNote? existingNote = null)
    {
        _targetRow = row;
        _targetColumn = column;
        _cellAddress = a1Address;
        _titleLabel.Text = $"Note - {a1Address}";
        _authorEdit.Text = existingNote?.Author ?? string.Empty;
        _textEdit.Text = existingNote?.Text ?? string.Empty;
        _deleteButton.IsEnabled = existingNote is not null;

        _isVisible = true;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        _textEdit.Focus();
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        _isVisible = false;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        if (!_isVisible)
            return;

        string text = _textEdit.Text.Trim();
        string author = _authorEdit.Text.Trim();
        if (text.Length == 0)
        {
            Delete();
            return;
        }

        NoteSaved?.Invoke(this, (_targetRow, _targetColumn, text, author.Length > 0 ? author : null));
        Hide();
    }

    public void Delete()
    {
        if (!_isVisible)
            return;

        NoteDeleted?.Invoke(this, (_targetRow, _targetColumn));
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

            if (IsKey(input, 13, "Enter") && input.KeyModifiers.HasFlag(KeyboardModifierState.Control))
            {
                Save();
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

        _titleLabel.Measure(new BSize(240, 22));
        _closeButton.Measure(new BSize(24, 24));
        _authorLabel.Measure(new BSize(50, 22));
        _authorEdit.Measure(new BSize(226, 24));
        _textLabel.Measure(new BSize(100, 20));
        _textEdit.Measure(new BSize(280, 68));
        _saveButton.Measure(new BSize(76, 26));
        _deleteButton.Measure(new BSize(76, 26));
        _cancelButton.Measure(new BSize(76, 26));

        return new BSize(PanelWidth, PanelHeight);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (!_isVisible)
        {
            var zero = new BRect(0, 0, 0, 0);
            _titleLabel.Arrange(zero);
            _closeButton.Arrange(zero);
            _authorLabel.Arrange(zero);
            _authorEdit.Arrange(zero);
            _textLabel.Arrange(zero);
            _textEdit.Arrange(zero);
            _saveButton.Arrange(zero);
            _deleteButton.Arrange(zero);
            _cancelButton.Arrange(zero);
            return;
        }

        double left = finalRect.Left;
        double top = finalRect.Top;

        _titleLabel.Arrange(new BRect(left + 10, top + 8, 250, 22));
        _closeButton.Arrange(new BRect(left + 266, top + 6, 24, 24));

        _authorLabel.Arrange(new BRect(left + 10, top + 34, 50, 22));
        _authorEdit.Arrange(new BRect(left + 64, top + 32, 226, 24));

        _textLabel.Arrange(new BRect(left + 10, top + 60, 100, 18));
        _textEdit.Arrange(new BRect(left + 10, top + 80, 280, 72));

        _saveButton.Arrange(new BRect(left + 10, top + 160, 80, 28));
        _deleteButton.Arrange(new BRect(left + 96, top + 160, 80, 28));
        _cancelButton.Arrange(new BRect(left + 182, top + 160, 80, 28));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        if (!_isVisible)
            return;

        var list = context.RenderList;

        // Shadow
        list.FillRect(new BRect(Bounds.X + 3, Bounds.Y + 3, Bounds.Width, Bounds.Height), new BColor(0, 0, 0, 45));

        // Pale yellow Post-It note card background
        list.FillRect(Bounds, new BColor(254, 249, 217)); // #FEF9D9
        list.StrokeRect(Bounds, new BColor(218, 204, 140), 1);

        // Header separator line
        list.FillRect(new BRect(Bounds.X + 8, Bounds.Y + 30, Bounds.Width - 16, 1), new BColor(230, 216, 150));

        base.RenderCore(context);
    }
}

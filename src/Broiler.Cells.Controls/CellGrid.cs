using Broiler.Cells.Core;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Cells.Controls;

/// <summary>A read-only grid with no app shell, editing, selection, or coordinate headers.</summary>
public sealed class CellGrid : UiElement, IStandardThemedControl
{
    public const double CellWidth = 96;
    public const double CellHeight = 28;
    private StandardThemeTokens _theme = StandardThemeTokens.Light;
    private BFontStyle _font = StandardThemeTokens.Light.FontBody;

    public CellGrid(SheetModel sheet)
    {
        Sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        AccessibleName = "Read-only sheet";
        // These lightweight children expose individual values to accessibility.
        // All visual drawing remains in this control; none is a text editor.
        for (int row = 0; row < sheet.RowCount; row++)
            for (int column = 0; column < sheet.ColumnCount; column++)
                AddChild(new CellSemanticElement(row, column, sheet[row, column]));
    }

    public SheetModel Sheet { get; }
    public BSize SheetSize => new(Sheet.ColumnCount * CellWidth + 1, Sheet.RowCount * CellHeight + 1);

    public void ApplyTheme(StandardThemeTokens theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _font = theme.FontBody;
        InvalidateRender();
    }

    protected override BSize MeasureCore(BSize availableSize) => SheetSize;

    protected override void ArrangeCore(BRect finalRect)
    {
        for (int row = 0; row < Sheet.RowCount; row++)
            for (int column = 0; column < Sheet.ColumnCount; column++)
                Children[row * Sheet.ColumnCount + column].Arrange(CellBounds(row, column));
    }

    private BRect CellBounds(int row, int column) => new(
        Bounds.X + column * CellWidth + 1, Bounds.Y + row * CellHeight + 1,
        CellWidth - 1, CellHeight - 1);

    protected override void RenderCore(UiRenderContext context)
    {
        var list = context.RenderList;
        var sheetBounds = new BRect(Bounds.X, Bounds.Y, SheetSize.Width, SheetSize.Height);
        list.PushClip(sheetBounds);
        list.FillRect(sheetBounds, _theme.Surface);
        for (int row = 0; row <= Sheet.RowCount; row++)
            list.FillRect(new BRect(Bounds.X, Bounds.Y + row * CellHeight, SheetSize.Width, 1), _theme.Border);
        for (int column = 0; column <= Sheet.ColumnCount; column++)
            list.FillRect(new BRect(Bounds.X + column * CellWidth, Bounds.Y, 1, SheetSize.Height), _theme.Border);

        BRect visible = GetVisibleBounds();
        double textY = Math.Max(0, (CellHeight - BTextMeasurer.GetLineHeight(_font)) / 2);
        for (int row = 0; row < Sheet.RowCount; row++)
        {
            for (int column = 0; column < Sheet.ColumnCount; column++)
            {
                BRect cell = CellBounds(row, column);
                if (cell.Right <= visible.Left || cell.Left >= visible.Right ||
                    cell.Bottom <= visible.Top || cell.Top >= visible.Bottom)
                    continue;
                string text = Sheet[row, column];
                if (text.Length == 0)
                    continue;

                // Display on one line without altering the stored value.
                text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                list.PushClip(cell);
                list.DrawText(new BTextRun(text, _font, _theme.Text),
                    new BPoint(cell.X + 6, Bounds.Y + row * CellHeight + textY));
                list.PopClip();
            }
        }
        list.PopClip();
    }

    protected override UiSemanticNode GetSemanticNodeCore() =>
        base.GetSemanticNodeCore() with { Role = UiSemanticRole.Group, State = UiSemanticState.Visible | UiSemanticState.Enabled | UiSemanticState.ReadOnly };

    private sealed class CellSemanticElement : UiElement
    {
        public CellSemanticElement(int row, int column, string value)
        {
            AccessibleName = $"Row {row + 1}, column {column + 1}: {(value.Length == 0 ? "Empty" : value)}";
        }

        protected override UiSemanticNode GetSemanticNodeCore() =>
            base.GetSemanticNodeCore() with { Role = UiSemanticRole.Label, State = UiSemanticState.Visible | UiSemanticState.Enabled | UiSemanticState.ReadOnly };
    }
}

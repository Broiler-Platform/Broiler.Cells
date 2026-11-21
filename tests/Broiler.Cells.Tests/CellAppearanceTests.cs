using Broiler.Cells.Core;
using Broiler.Graphics.Color;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class CellAppearanceTests
{
    [Fact]
    public void InnerAndOuterBordersUseOneEdgeWithoutFillingSelectionGaps()
    {
        var sheet = new SheetModel(2, 4);
        var color = new BColor(20, 80, 120);
        HashSet<CellPosition> selection = [new(0, 0), new(0, 1), new(0, 3)];
        CellAppearance.ApplyBorders(sheet, selection, CellBorderPreset.Inner, color);
        Assert.Equal(color, sheet.GetContent(0, 0).Borders.Right.Color);
        Assert.False(sheet.GetContent(0, 1).Borders.IsVisible);
        Assert.False(sheet.GetContent(0, 3).Borders.IsVisible);

        CellAppearance.ApplyBorders(sheet, selection, CellBorderPreset.Outer, color);
        Assert.False(sheet.GetContent(0, 0).Borders.Right.IsVisible);
        Assert.Equal(color, sheet.GetContent(0, 1).Borders.Right.Color);
        Assert.Equal(color, sheet.GetContent(0, 3).Borders.Left.Color);
        Assert.False(sheet.GetContent(0, 2).Borders.IsVisible);
    }

    [Fact]
    public void MergedCellsUseTheAnchorForTheirPerimeter()
    {
        var sheet = new SheetModel(2, 3);
        sheet[0, 0] = "Heading";
        sheet.MergeCells(0, 0, 1, 1);
        HashSet<CellPosition> selection = [new(0, 0), new(0, 1), new(1, 0), new(1, 1)];
        var color = new BColor(20, 80, 120);

        CellAppearance.ApplyBorders(sheet, selection, CellBorderPreset.All, color);
        Assert.Equal(color, sheet.GetContent(0, 0).Borders.Bottom.Color);
        Assert.Equal(color, sheet.GetContent(0, 0).Borders.Right.Color);
        Assert.False(sheet.GetContent(1, 1).Borders.IsVisible);
        Assert.Equal("Heading", sheet[0, 0]);

        CellAppearance.ApplyBorders(sheet, selection, CellBorderPreset.None, default);
        Assert.False(sheet.GetContent(0, 0).Borders.IsVisible);
    }
}

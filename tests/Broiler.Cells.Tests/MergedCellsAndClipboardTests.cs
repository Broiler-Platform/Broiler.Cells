using System;
using System.IO;
using System.Linq;
using System.Text;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class MergedCellsAndClipboardTests
{
    [Fact]
    public void SheetModel_MergeCells_TracksRootAndClearsCovered()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Header";
        sheet[0, 1] = "Sub1";
        sheet[1, 0] = "Sub2";
        sheet[1, 1] = "Sub3";

        sheet.MergeCells(0, 0, 1, 1, center: true);

        Assert.True(sheet.TryGetMerge(0, 0, out int rootR, out int rootC, out int colSpan, out int rowSpan));
        Assert.Equal(0, rootR);
        Assert.Equal(0, rootC);
        Assert.Equal(2, colSpan);
        Assert.Equal(2, rowSpan);

        Assert.True(sheet.IsMergedRoot(0, 0));
        Assert.False(sheet.IsCoveredCell(0, 0));

        Assert.True(sheet.IsCoveredCell(0, 1));
        Assert.True(sheet.IsCoveredCell(1, 0));
        Assert.True(sheet.IsCoveredCell(1, 1));

        Assert.Equal("Header", sheet[0, 0]);
        Assert.Equal(string.Empty, sheet[0, 1]);
        Assert.Equal(string.Empty, sheet[1, 0]);
        Assert.Equal(string.Empty, sheet[1, 1]);

        var rootContent = sheet.GetContent(0, 0);
        Assert.Equal(2, rootContent.ColumnSpan);
        Assert.Equal(2, rootContent.RowSpan);
        Assert.Equal(Broiler.Documents.Model.TextAlignment.Center, rootContent.Alignment);
    }

    [Fact]
    public void SheetModel_Unmerge_RestoresSingleCells()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Header";
        sheet.MergeCells(0, 0, 1, 1);

        Assert.True(sheet.IsCoveredCell(0, 1));

        sheet.Unmerge(0, 1); // Unmerging any cell in merge unmerges the group

        Assert.False(sheet.TryGetMerge(0, 0, out _, out _, out _, out _));
        Assert.False(sheet.IsCoveredCell(0, 1));

        var rootContent = sheet.GetContent(0, 0);
        Assert.Equal(1, rootContent.ColumnSpan);
        Assert.Equal(1, rootContent.RowSpan);
    }

    [Fact]
    public void CellGrid_CellBounds_ReturnsMergedBounds()
    {
        var sheet = new SheetModel(5, 5);
        sheet.SetColumnWidth(0, 100);
        sheet.SetColumnWidth(1, 80);
        sheet.SetRowHeight(0, 30);
        sheet.SetRowHeight(1, 25);

        sheet.MergeCells(0, 0, 1, 1);
        var grid = new CellGrid(sheet);

        BRect bounds00 = grid.CellBounds(0, 0);
        BRect bounds01 = grid.CellBounds(0, 1);
        BRect bounds10 = grid.CellBounds(1, 0);

        Assert.Equal(bounds00, bounds01);
        Assert.Equal(bounds00, bounds10);
        Assert.True(bounds00.Width >= 179); // 100 + 80 - 1
        Assert.True(bounds00.Height >= 54); // 30 + 25 - 1
    }

    [Fact]
    public void CellGrid_AutoFitColumn_IgnoresMergedColumns()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Short";
        sheet[1, 0] = "Tiny";
        sheet[2, 0] = "A Very Long Merged Title Across Multiple Columns";
        sheet.MergeCells(2, 0, 2, 3); // Merged across 4 columns

        var grid = new CellGrid(sheet);
        grid.AutoFitColumn(0);

        // Column 0 should be sized for "Short", not the 4-column merged title
        Assert.True(grid.GetColumnWidth(0) < 150);
    }

    [Fact]
    public void CellControl_AutoFitRow_CalculatesHeight()
    {
        var sheet = new SheetModel(5, 5);
        sheet[1, 1] = "Standard text";
        var control = new CellControl(sheet);

        control.AutoFitRow(1);
        Assert.True(control.GetRowHeight(1) >= 20);
    }

    [Fact]
    public void CellsView_ToggleMergeSelected_MergesAndUnmerges()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Title";
        var view = new CellsView(sheet);

        // Select range A1:B2 (rows 0..1, cols 0..1)
        view.CellControl.SelectCell(new CellPosition(0, 0));
        view.CellControl.SelectCell(new CellPosition(1, 1), extend: true);

        view.ToggleMergeSelected();

        Assert.True(sheet.TryGetMerge(0, 0, out int r0, out int c0, out int cs, out int rs));
        Assert.Equal(2, cs);
        Assert.Equal(2, rs);

        // Toggle again on the merged cell -> unmerges
        view.ToggleMergeSelected();
        Assert.False(sheet.TryGetMerge(0, 0, out _, out _, out _, out _));
    }

    [Fact]
    public void CellsView_CopySelection_OutputsTsv()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "A";
        sheet[0, 1] = "B";
        sheet[1, 0] = "1";
        sheet[1, 1] = "2";

        string clipboardText = "";
        var host = new CellsUiHost(() => new BSize(800, 600), () => 1.0, () => { }, _ => { },
            getClipboardText: () => clipboardText,
            setClipboardText: text => clipboardText = text);

        using var app = new CellsApp(host, () => { }, initialSheet: sheet);
        var view = app.View;

        // Select A1:B2
        view.CellControl.SelectCell(new CellPosition(0, 0));
        view.CellControl.SelectCell(new CellPosition(1, 1), extend: true);

        view.CopySelection();

        string expected = "A\tB" + Environment.NewLine + "1\t2";
        Assert.Equal(expected, clipboardText);
    }

    [Fact]
    public void CellsView_PasteSelection_ParsesTsvGrid()
    {
        var sheet = new SheetModel(5, 5);
        string clipboardText = "Alpha\tBeta\r\n100\t200";
        var host = new CellsUiHost(() => new BSize(800, 600), () => 1.0, () => { }, _ => { },
            getClipboardText: () => clipboardText,
            setClipboardText: text => clipboardText = text);

        using var app = new CellsApp(host, () => { }, initialSheet: sheet);
        var view = app.View;

        view.CellControl.SelectCell(new CellPosition(1, 1));
        view.PasteSelection();

        Assert.Equal("Alpha", sheet[1, 1]);
        Assert.Equal("Beta", sheet[1, 2]);
        Assert.Equal("100", sheet[2, 1]);
        Assert.Equal("200", sheet[2, 2]);
    }

    [Fact]
    public void CellsView_PasteSelection_HandlesQuotedMultilineTsv()
    {
        var sheet = new SheetModel(5, 5);
        string clipboardText = "\"Line1\nLine2\"\tSingle\r\nRow2Cell1\tRow2Cell2";
        var host = new CellsUiHost(() => new BSize(800, 600), () => 1.0, () => { }, _ => { },
            getClipboardText: () => clipboardText,
            setClipboardText: text => clipboardText = text);

        using var app = new CellsApp(host, () => { }, initialSheet: sheet);
        var view = app.View;

        view.CellControl.SelectCell(new CellPosition(0, 0));
        view.PasteSelection();

        Assert.Equal("Line1\nLine2", sheet[0, 0]);
        Assert.Equal("Single", sheet[0, 1]);
        Assert.Equal("Row2Cell1", sheet[1, 0]);
        Assert.Equal("Row2Cell2", sheet[1, 1]);
    }

    [Fact]
    public void CellsView_ClearSelection_ClearsAllSelectedCells()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "A";
        sheet[0, 1] = "B";
        sheet[1, 0] = "C";
        sheet[1, 1] = "D";

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));
        view.CellControl.SelectCell(new CellPosition(1, 1), extend: true);

        view.ClearSelection();

        Assert.Equal(string.Empty, sheet[0, 0]);
        Assert.Equal(string.Empty, sheet[0, 1]);
        Assert.Equal(string.Empty, sheet[1, 0]);
        Assert.Equal(string.Empty, sheet[1, 1]);
    }

    [Fact]
    public void SheetMarkdownSerializer_RoundTripsGfmTable()
    {
        var sheet = new SheetModel(3, 3);
        sheet[0, 0] = "Name";
        sheet[0, 1] = "Qty";
        sheet[0, 2] = "Price";
        sheet[1, 0] = "Apples";
        sheet[1, 1] = "5";
        sheet[1, 2] = "1.50";
        sheet[2, 0] = "Oranges";
        sheet[2, 1] = "10";
        sheet[2, 2] = "2.00";

        string md = SheetMarkdownSerializer.ToMarkdownString(sheet);
        Assert.Contains("| Name | Qty | Price |", md);
        Assert.Contains("| Apples | 5 | 1.50 |", md);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(md));
        var loaded = SheetMarkdownSerializer.Load(stream);

        Assert.Equal("Name", loaded[0, 0]);
        Assert.Equal("Apples", loaded[1, 0]);
        Assert.Equal("10", loaded[2, 1]);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void SheetDocumentSerializer_RoundTripsMergedCellsAcrossXlsxAndOds(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        try
        {
            var sheet = new SheetModel(5, 5);
            sheet[0, 0] = "Merged Header";
            sheet.MergeCells(0, 0, 1, 2); // 2 rows, 3 cols

            SheetDocumentSerializer.Save(sheet, path);
            var loaded = SheetDocumentSerializer.Load(path);

            Assert.Equal("Merged Header", loaded[0, 0]);
            Assert.True(loaded.TryGetMerge(0, 0, out int r0, out int c0, out int cs, out int rs));
            Assert.Equal(0, r0);
            Assert.Equal(0, c0);
            Assert.Equal(3, cs);
            Assert.Equal(2, rs);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".tsv")]
    [InlineData(".md")]
    public void SheetDocumentSerializer_SupportsTsvAndMarkdownExtensions(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        try
        {
            var sheet = new SheetModel(3, 3);
            sheet[0, 0] = "Item";
            sheet[0, 1] = "Count";
            sheet[1, 0] = "Widget";
            sheet[1, 1] = "42";

            SheetDocumentSerializer.Save(sheet, path);
            Assert.True(File.Exists(path));

            var loaded = SheetDocumentSerializer.Load(path);
            Assert.Equal("Item", loaded[0, 0]);
            Assert.Equal("Widget", loaded[1, 0]);
            Assert.Equal("42", loaded[1, 1]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

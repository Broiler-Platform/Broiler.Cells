using System;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class MultilineAndWordWrapTests
{
    [Fact]
    public void SheetModel_WrapText_GetAndSet_UpdatesCellAndFiresChanged()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Sample text";

        Assert.False(sheet.GetCellWrapText(0, 0));
        Assert.False(sheet.GetContent(0, 0).WrapText);

        int changedCount = 0;
        sheet.Changed += (_, _) => changedCount++;

        sheet.SetCellWrapText(0, 0, true);
        Assert.True(sheet.GetCellWrapText(0, 0));
        Assert.True(sheet.GetContent(0, 0).WrapText);
        Assert.Equal(1, changedCount);

        sheet.SetCellWrapText(0, 0, false);
        Assert.False(sheet.GetCellWrapText(0, 0));
        Assert.False(sheet.GetContent(0, 0).WrapText);
        Assert.Equal(2, changedCount);
    }

    [Fact]
    public void SheetDocumentSerializer_XlsxRoundtrip_PreservesWrapTextAndMultilineText()
    {
        var sheet = new SheetModel(4, 4);
        sheet[0, 0] = "Line 1\nLine 2\nLine 3";
        sheet.SetCellWrapText(0, 0, true);

        sheet[0, 1] = "Single line text";
        sheet.SetCellWrapText(0, 1, false);

        sheet[1, 0] = "Wrapped paragraph that should wrap across several lines when rendered";
        sheet.SetCellWrapText(1, 0, true);

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "wrap_test.xlsx");

        ms.Position = 0;
        var loaded = SheetDocumentSerializer.Load(ms, "wrap_test.xlsx");

        Assert.NotNull(loaded);
        Assert.Equal("Line 1\nLine 2\nLine 3", loaded[0, 0]);
        Assert.True(loaded.GetCellWrapText(0, 0));

        Assert.Equal("Single line text", loaded[0, 1]);
        Assert.False(loaded.GetCellWrapText(0, 1));

        Assert.Equal("Wrapped paragraph that should wrap across several lines when rendered", loaded[1, 0]);
        Assert.True(loaded.GetCellWrapText(1, 0));
    }

    [Fact]
    public void SheetDocumentSerializer_OdsRoundtrip_PreservesWrapTextAndMultilineText()
    {
        var sheet = new SheetModel(4, 4);
        sheet[0, 0] = "Alpha\nBeta\nGamma";
        sheet.SetCellWrapText(0, 0, true);

        sheet[0, 1] = "Plain text";
        sheet.SetCellWrapText(0, 1, false);

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "wrap_test.ods");

        ms.Position = 0;
        var loaded = SheetDocumentSerializer.Load(ms, "wrap_test.ods");

        Assert.NotNull(loaded);
        Assert.Equal("Alpha\nBeta\nGamma", loaded[0, 0]);
        Assert.True(loaded.GetCellWrapText(0, 0));

        Assert.Equal("Plain text", loaded[0, 1]);
        Assert.False(loaded.GetCellWrapText(0, 1));
    }

    [Fact]
    public void CellGrid_BreakLines_ExplicitNewlines()
    {
        var font = new BFontStyle("Segoe UI", 12);
        string text = "Header\r\nSecond Row\nThird Row";

        var lines = CellGrid.BreakLines(text, font, wrapText: true, availableWidth: 500);

        Assert.Equal(3, lines.Count);
        Assert.Equal("Header", lines[0]);
        Assert.Equal("Second Row", lines[1]);
        Assert.Equal("Third Row", lines[2]);
    }

    [Fact]
    public void CellGrid_BreakLines_WordWrapping_WhenWrapTextTrue()
    {
        var font = new BFontStyle("Segoe UI", 12);
        string text = "The quick brown fox jumps over the lazy dog";

        // Without wrapping, returns 1 line
        var singleLine = CellGrid.BreakLines(text, font, wrapText: false, availableWidth: 80);
        Assert.Single(singleLine);
        Assert.Equal(text, singleLine[0]);

        // With wrapping and narrow width, wraps into multiple lines
        var wrappedLines = CellGrid.BreakLines(text, font, wrapText: true, availableWidth: 80);
        Assert.True(wrappedLines.Count > 1);
        string joined = string.Join(" ", wrappedLines);
        Assert.Equal(text, joined);
    }

    [Fact]
    public void CellGrid_AutoFitRow_ExpandsHeightForMultilineAndWrap()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Line 1\nLine 2\nLine 3\nLine 4";
        sheet.SetCellWrapText(0, 0, true);

        var grid = new CellGrid(sheet);
        double initialHeight = grid.GetRowHeight(0);
        Assert.Equal(CellGrid.DefaultCellHeight, initialHeight);

        grid.AutoFitRow(0);

        double fittedHeight = grid.GetRowHeight(0);
        Assert.True(fittedHeight > initialHeight + 20, $"Expected fitted row height to expand significantly, got {fittedHeight}");
    }

    [Fact]
    public void CellGrid_AutoFitColumn_MeasuresWidestLineInMultilineCell()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Short\nA considerably wider second line in the cell\nShort 3";

        var grid = new CellGrid(sheet);
        grid.AutoFitColumn(0);

        double width = grid.GetColumnWidth(0);
        var font = new BFontStyle("Segoe UI", 12);
        double wideLineAdvance = BTextMeasurer.MeasureAdvance("A considerably wider second line in the cell", font);

        Assert.True(width >= wideLineAdvance, $"Expected column width >= {wideLineAdvance}, got {width}");
    }

    [Fact]
    public void CellsView_Command_FormatWrapText_TogglesSelectedCells()
    {
        var sheet = new SheetModel(4, 4);
        sheet[0, 0] = "Hello";
        sheet[0, 1] = "World";

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));

        Assert.False(view.CellControl.Sheet.GetCellWrapText(0, 0));

        // Execute format.wrap-text command
        Assert.True(view.CommandDispatcher.TryExecute("format.wrap-text"));
        Assert.True(view.CellControl.Sheet.GetCellWrapText(0, 0));

        // Execute again to toggle off
        Assert.True(view.CommandDispatcher.TryExecute("format.wrap-text"));
        Assert.False(view.CellControl.Sheet.GetCellWrapText(0, 0));
    }

    [Fact]
    public void CellGrid_AltEnter_InsertsNewline_AndEnablesWrapText()
    {
        var sheet = new SheetModel(3, 3);
        sheet[0, 0] = "Initial";

        using var fixture = new TestFixture(sheet, 600, 300);
        fixture.Grid.SelectCell(new CellPosition(0, 0));
        fixture.Grid.StartEditing("Hello");

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618
        var altEnter = adapter.FromKey(new BKeyEventArgs(0x0D, false, false, true), KeyboardKeyTransition.Down);

        fixture.Grid.DispatchInput(altEnter);

        // Edit commit
        fixture.Grid.CommitEdit();

        Assert.Equal("Hello\n", sheet[0, 0]);
        Assert.True(sheet.GetCellWrapText(0, 0));
    }

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(SheetModel sheet, double width = 1000, double height = 400, bool isEditingEnabled = true)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Grid = new CellGrid(sheet, isEditingEnabled);
            Session.AddRoot(Grid);
        }

        public TestHost Host { get; }
        public CellGrid Grid { get; }
        public UiSession Session { get; }
        public void Dispose() => Session.Dispose();
    }

    private sealed class TestHost : IUiHost
    {
        public BSize ViewportSize { get; set; }
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}

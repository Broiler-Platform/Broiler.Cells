using System;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class ViewStateAndFreezePanesTests
{
    [Fact]
    public void SheetModel_ViewStateProperties_DefaultAndClamp()
    {
        var sheet = new SheetModel(10, 8);
        Assert.True(sheet.ShowGridlines);
        Assert.Equal(0, sheet.FrozenRows);
        Assert.Equal(0, sheet.FrozenColumns);

        int changedCount = 0;
        sheet.Changed += (_, _) => changedCount++;

        sheet.ShowGridlines = false;
        Assert.False(sheet.ShowGridlines);
        Assert.Equal(1, changedCount);

        sheet.ToggleGridlines();
        Assert.True(sheet.ShowGridlines);
        Assert.Equal(2, changedCount);

        // Clamping on FrozenRows
        sheet.FrozenRows = 5;
        Assert.Equal(5, sheet.FrozenRows);
        Assert.Equal(3, changedCount);

        sheet.FrozenRows = 20; // > RowCount (10)
        Assert.Equal(10, sheet.FrozenRows);
        Assert.Equal(4, changedCount);

        sheet.FrozenRows = -5;
        Assert.Equal(0, sheet.FrozenRows);
        Assert.Equal(5, changedCount);

        // Clamping on FrozenColumns
        sheet.FrozenColumns = 4;
        Assert.Equal(4, sheet.FrozenColumns);
        Assert.Equal(6, changedCount);

        sheet.FrozenColumns = 15; // > ColumnCount (8)
        Assert.Equal(8, sheet.FrozenColumns);
        Assert.Equal(7, changedCount);

        // Helper methods
        sheet.FreezePanes(3, 2);
        Assert.Equal(3, sheet.FrozenRows);
        Assert.Equal(2, sheet.FrozenColumns);

        sheet.UnfreezePanes();
        Assert.Equal(0, sheet.FrozenRows);
        Assert.Equal(0, sheet.FrozenColumns);
    }

    [Fact]
    public void XlsxExportAndImport_PreservesViewState_GridlinesAndFreezePanes()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Header 1";
        sheet[0, 1] = "Header 2";
        sheet[1, 0] = "Item A";
        sheet[1, 1] = "100";
        sheet.ShowGridlines = false;
        sheet.FreezePanes(1, 1);

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "viewstate.xlsx");

        ms.Position = 0;
        var loaded = SheetDocumentSerializer.Load(ms, "viewstate.xlsx");

        Assert.NotNull(loaded);
        Assert.False(loaded.ShowGridlines);
        Assert.Equal(1, loaded.FrozenRows);
        Assert.Equal(1, loaded.FrozenColumns);
        Assert.Equal("Header 1", loaded[0, 0]);
        Assert.Equal("100", loaded[1, 1]);
    }

    [Fact]
    public void OdsExportAndImport_PreservesViewState_GridlinesAndFreezePanes()
    {
        var sheet = new SheetModel(6, 6);
        sheet[0, 0] = "Col A";
        sheet[0, 1] = "Col B";
        sheet[1, 0] = "Val 1";
        sheet[1, 1] = "Val 2";
        sheet.ShowGridlines = false;
        sheet.FreezePanes(2, 1);

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "viewstate.ods");

        ms.Position = 0;
        var loaded = SheetDocumentSerializer.Load(ms, "viewstate.ods");

        Assert.NotNull(loaded);
        Assert.False(loaded.ShowGridlines);
        Assert.Equal(2, loaded.FrozenRows);
        Assert.Equal(1, loaded.FrozenColumns);
        Assert.Equal("Col A", loaded[0, 0]);
        Assert.Equal("Val 2", loaded[1, 1]);
    }

    [Fact]
    public void CellGrid_RendersWithoutGridlinesWhenShowGridlinesFalse()
    {
        var sheet = new SheetModel(3, 3, (r, c) => $"{r}:{c}");
        sheet.ShowGridlines = false;

        using var fixture = new TestFixture(sheet, 600, 300);
        var frame = fixture.Session.RenderFrame();

        // Border gridlines (at right and bottom edge) should NOT be emitted
        var borders = frame.Commands.OfType<BRenderCommand.FillRect>()
            .Where(x => x.Rect == new BRect(3 * CellGrid.CellWidth, 0, 1, 3 * CellGrid.CellHeight + 1)
                     || x.Rect == new BRect(0, 3 * CellGrid.CellHeight, 3 * CellGrid.CellWidth + 1, 1))
            .ToList();
        Assert.Empty(borders);
    }

    [Fact]
    public void CellGrid_RendersGridlinesWhenShowGridlinesTrue()
    {
        var sheet = new SheetModel(3, 3, (r, c) => $"{r}:{c}");
        sheet.ShowGridlines = true;

        using var fixture = new TestFixture(sheet, 600, 300);
        var frame = fixture.Session.RenderFrame();

        var borders = frame.Commands.OfType<BRenderCommand.FillRect>()
            .Where(x => x.Rect == new BRect(3 * CellGrid.CellWidth, 0, 1, 3 * CellGrid.CellHeight + 1)
                     || x.Rect == new BRect(0, 3 * CellGrid.CellHeight, 3 * CellGrid.CellWidth + 1, 1))
            .ToList();
        Assert.NotEmpty(borders);
    }

    [Fact]
    public void CellGrid_PointerHitTest_MapsToFrozenCoordinatesWhenScrolled()
    {
        var sheet = new SheetModel(10, 10);
        sheet.FreezePanes(1, 1); // Row 0 and Col 0 frozen

        using var fixture = new TestFixture(sheet, 600, 400);
        fixture.Grid.ScrollOffsetProvider = () => (200, 100);
        fixture.Session.RenderFrame();

#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618
        // Clicking at (20, 15) in the top-left area lands in frozen row 0, col 0
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(20, 15), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(20, 15), BMouseButtons.None, BMouseButtons.Left)));

        Assert.Equal(new CellPosition(0, 0), fixture.Grid.SelectedCell);
    }

    [Fact]
    public void CellsView_Commands_ToggleGridlinesAndFreezePanes()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        Assert.True(view.CellControl.Sheet.ShowGridlines);

        // Execute view.toggle-gridlines
        Assert.True(view.CommandDispatcher.TryExecute("view.toggle-gridlines"));
        Assert.False(view.CellControl.Sheet.ShowGridlines);

        Assert.True(view.CommandDispatcher.TryExecute("view.toggle-gridlines"));
        Assert.True(view.CellControl.Sheet.ShowGridlines);

        // Select cell (2, 1) and freeze panes
        view.CellControl.SelectCell(new CellPosition(2, 1));
        Assert.True(view.CommandDispatcher.TryExecute("view.freeze-panes"));
        Assert.Equal(2, view.CellControl.Sheet.FrozenRows);
        Assert.Equal(1, view.CellControl.Sheet.FrozenColumns);

        // Freezing again toggles / unfreezes
        Assert.True(view.CommandDispatcher.TryExecute("view.freeze-panes"));
        Assert.Equal(0, view.CellControl.Sheet.FrozenRows);
        Assert.Equal(0, view.CellControl.Sheet.FrozenColumns);

        // Freeze and explicit unfreeze command
        view.CellControl.SelectCell(new CellPosition(1, 2));
        Assert.True(view.CommandDispatcher.TryExecute("view.freeze-panes"));
        Assert.Equal(1, view.CellControl.Sheet.FrozenRows);
        Assert.Equal(2, view.CellControl.Sheet.FrozenColumns);

        Assert.True(view.CommandDispatcher.TryExecute("view.unfreeze-panes"));
        Assert.Equal(0, view.CellControl.Sheet.FrozenRows);
        Assert.Equal(0, view.CellControl.Sheet.FrozenColumns);
    }

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(SheetModel sheet, double width = 1000, double height = 400, bool isEditingEnabled = true)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Grid = new CellGrid(sheet, isEditingEnabled);
            Scroll = new StandardScrollView { FocusWhenScrollable = true };
            Scroll.AddChild(Grid);
            Session.AddRoot(Scroll);
        }

        public TestHost Host { get; }
        public CellGrid Grid { get; }
        public StandardScrollView Scroll { get; }
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

using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class ResizeTests
{
    [Fact]
    public void DefaultSizesAndOffsetsMatchConstants()
    {
        var sheet = new SheetModel(3, 3);
        var grid = new CellGrid(sheet);

        Assert.Equal(CellGrid.DefaultCellWidth, grid.GetColumnWidth(0));
        Assert.Equal(CellGrid.DefaultCellWidth, grid.GetColumnWidth(1));
        Assert.Equal(CellGrid.DefaultCellHeight, grid.GetRowHeight(0));
        Assert.Equal(CellGrid.DefaultCellHeight, grid.GetRowHeight(1));

        Assert.Equal(0, grid.GetColumnOffset(0));
        Assert.Equal(96, grid.GetColumnOffset(1));
        Assert.Equal(192, grid.GetColumnOffset(2));
        Assert.Equal(288, grid.GetColumnOffset(3));

        Assert.Equal(0, grid.GetRowOffset(0));
        Assert.Equal(28, grid.GetRowOffset(1));
        Assert.Equal(56, grid.GetRowOffset(2));
        Assert.Equal(84, grid.GetRowOffset(3));

        Assert.Equal(288, grid.TotalWidth);
        Assert.Equal(84, grid.TotalHeight);
        Assert.Equal(new BSize(289, 85), grid.SheetSize);
    }

    [Fact]
    public void SetColumnWidthUpdatesOffsetsBoundsAndRaisesEvent()
    {
        var sheet = new SheetModel(3, 3);
        var grid = new CellGrid(sheet);

        ColumnResizedEventArgs? capturedArgs = null;
        grid.ColumnResized += (_, args) => capturedArgs = args;

        grid.SetColumnWidth(0, 150);

        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.Column);
        Assert.Equal(96, capturedArgs.OldWidth);
        Assert.Equal(150, capturedArgs.NewWidth);

        Assert.Equal(150, grid.GetColumnWidth(0));
        Assert.Equal(0, grid.GetColumnOffset(0));
        Assert.Equal(150, grid.GetColumnOffset(1));
        Assert.Equal(246, grid.GetColumnOffset(2));
        Assert.Equal(342, grid.GetColumnOffset(3));
        Assert.Equal(342, grid.TotalWidth);

        BRect cell00 = grid.CellBounds(0, 0);
        Assert.Equal(1, cell00.X);
        Assert.Equal(149, cell00.Width);

        BRect cell01 = grid.CellBounds(0, 1);
        Assert.Equal(151, cell01.X);
        Assert.Equal(95, cell01.Width);
    }

    [Fact]
    public void SetRowHeightUpdatesOffsetsBoundsAndRaisesEvent()
    {
        var sheet = new SheetModel(3, 3);
        var grid = new CellGrid(sheet);

        RowResizedEventArgs? capturedArgs = null;
        grid.RowResized += (_, args) => capturedArgs = args;

        grid.SetRowHeight(1, 50);

        Assert.NotNull(capturedArgs);
        Assert.Equal(1, capturedArgs.Row);
        Assert.Equal(28, capturedArgs.OldHeight);
        Assert.Equal(50, capturedArgs.NewHeight);

        Assert.Equal(50, grid.GetRowHeight(1));
        Assert.Equal(0, grid.GetRowOffset(0));
        Assert.Equal(28, grid.GetRowOffset(1));
        Assert.Equal(78, grid.GetRowOffset(2));
        Assert.Equal(106, grid.GetRowOffset(3));
        Assert.Equal(106, grid.TotalHeight);

        BRect cell10 = grid.CellBounds(1, 0);
        Assert.Equal(29, cell10.Y);
        Assert.Equal(49, cell10.Height);
    }

    [Fact]
    public void ColumnAndRowSizesClampedToValidLimits()
    {
        var sheet = new SheetModel(2, 2);
        var grid = new CellGrid(sheet);

        grid.SetColumnWidth(0, 5);
        Assert.Equal(CellGrid.MinCellWidth, grid.GetColumnWidth(0));

        grid.SetColumnWidth(0, 5000);
        Assert.Equal(CellGrid.MaxCellWidth, grid.GetColumnWidth(0));

        grid.SetRowHeight(0, 2);
        Assert.Equal(CellGrid.MinCellHeight, grid.GetRowHeight(0));

        grid.SetRowHeight(0, 9999);
        Assert.Equal(CellGrid.MaxCellHeight, grid.GetRowHeight(0));
    }

    [Fact]
    public void AutoFitColumnAndResetSizesWorkCorrectly()
    {
        var sheet = new SheetModel(2, 2);
        sheet[0, 0] = "Very long column header and cell text that needs wider space";
        var grid = new CellGrid(sheet);

        grid.AutoFitColumn(0);
        double fitted = grid.GetColumnWidth(0);
        Assert.True(fitted > 96);

        grid.ResetColumnWidth(0);
        Assert.Equal(CellGrid.DefaultCellWidth, grid.GetColumnWidth(0));

        grid.SetRowHeight(0, 60);
        Assert.Equal(60, grid.GetRowHeight(0));
        grid.ResetRowHeight(0);
        Assert.Equal(CellGrid.DefaultCellHeight, grid.GetRowHeight(0));
    }

    [Fact]
    public void HitTestingWithCustomSizesSelectsCorrectCells()
    {
        var sheet = new SheetModel(3, 3);
        var grid = new CellGrid(sheet);
        grid.SetColumnWidth(0, 200);
        grid.SetColumnWidth(1, 100);
        grid.SetRowHeight(0, 60);
        grid.SetRowHeight(1, 40);

        Assert.Equal(0, grid.GetColumnAtOffset(50));
        Assert.Equal(0, grid.GetColumnAtOffset(199));
        Assert.Equal(1, grid.GetColumnAtOffset(200));
        Assert.Equal(1, grid.GetColumnAtOffset(299));
        Assert.Equal(2, grid.GetColumnAtOffset(300));

        Assert.Equal(0, grid.GetRowAtOffset(30));
        Assert.Equal(0, grid.GetRowAtOffset(59));
        Assert.Equal(1, grid.GetRowAtOffset(60));
        Assert.Equal(1, grid.GetRowAtOffset(99));
        Assert.Equal(2, grid.GetRowAtOffset(100));
    }

    [Fact]
    public void HoveringOverHeaderDividersUpdatesCursor()
    {
        var sheet = new SheetModel(3, 3);
        using var fixture = new TestFixture(sheet);
        fixture.Session.RenderFrame();

        // Column 0 right divider is at X = RowHeaderWidth + Col0Width = 48 + 96 = 144
        fixture.DispatchPointerMove(new BPoint(144, 14));
        Assert.Equal(UiCursorShape.ResizeHorizontal, fixture.Host.CurrentCursor);

        // Move away to middle of column 0
        fixture.DispatchPointerMove(new BPoint(90, 14));
        Assert.Equal(UiCursorShape.Arrow, fixture.Host.CurrentCursor);

        // Row 0 bottom divider is at Y = ColumnHeaderHeight + Row0Height = 28 + 28 = 56
        fixture.DispatchPointerMove(new BPoint(24, 56));
        Assert.Equal(UiCursorShape.ResizeVertical, fixture.Host.CurrentCursor);

        // Move away to middle of row 0
        fixture.DispatchPointerMove(new BPoint(24, 40));
        Assert.Equal(UiCursorShape.Arrow, fixture.Host.CurrentCursor);
    }

    [Fact]
    public void DraggingColumnHeaderDividerResizesColumn()
    {
        var sheet = new SheetModel(3, 3);
        using var fixture = new TestFixture(sheet);
        fixture.Session.RenderFrame();

        ColumnResizedEventArgs? captured = null;
        fixture.Control.ColumnResized += (_, args) => captured = args;

        // Divider for column 0 is at X = 48 + 96 = 144
        fixture.DispatchPointerDown(new BPoint(144, 14));
        fixture.DispatchPointerMove(new BPoint(184, 14)); // +40px
        fixture.DispatchPointerUp(new BPoint(184, 14));

        Assert.Equal(136, fixture.Control.GetColumnWidth(0));
        Assert.NotNull(captured);
        Assert.Equal(0, captured.Column);
        Assert.Equal(136, captured.NewWidth);
    }

    [Fact]
    public void DraggingRowHeaderDividerResizesRow()
    {
        var sheet = new SheetModel(3, 3);
        using var fixture = new TestFixture(sheet);
        fixture.Session.RenderFrame();

        RowResizedEventArgs? captured = null;
        fixture.Control.RowResized += (_, args) => captured = args;

        // Divider for row 0 is at Y = 28 + 28 = 56
        fixture.DispatchPointerDown(new BPoint(24, 56));
        fixture.DispatchPointerMove(new BPoint(24, 86)); // +30px
        fixture.DispatchPointerUp(new BPoint(24, 86));

        Assert.Equal(58, fixture.Control.GetRowHeight(0));
        Assert.NotNull(captured);
        Assert.Equal(0, captured.Row);
        Assert.Equal(58, captured.NewHeight);
    }

    [Fact]
    public void DoubleClickColumnDividerAutoFitsColumn()
    {
        var sheet = new SheetModel(3, 3);
        sheet[0, 0] = "A rather long text string that needs plenty of space";
        using var fixture = new TestFixture(sheet);
        fixture.Session.RenderFrame();

        // Double click divider at (144, 14)
        fixture.DispatchPointerDown(new BPoint(144, 14));
        fixture.DispatchPointerUp(new BPoint(144, 14));
        fixture.DispatchPointerDown(new BPoint(144, 14));
        fixture.DispatchPointerUp(new BPoint(144, 14));

        Assert.True(fixture.Control.GetColumnWidth(0) > 96);
    }

    [Fact]
    public void DoubleClickRowDividerResetsRowHeight()
    {
        var sheet = new SheetModel(3, 3);
        using var fixture = new TestFixture(sheet);
        fixture.Control.SetRowHeight(0, 75);
        fixture.Session.RenderFrame();

        // Divider for row 0 at Y = 28 + 75 = 103
        fixture.DispatchPointerDown(new BPoint(24, 103));
        fixture.DispatchPointerUp(new BPoint(24, 103));
        fixture.DispatchPointerDown(new BPoint(24, 103));
        fixture.DispatchPointerUp(new BPoint(24, 103));

        Assert.Equal(CellGrid.DefaultCellHeight, fixture.Control.GetRowHeight(0));
    }

    [Fact]
    public void RenderFrameDrawsGridLinesAtCustomOffsets()
    {
        var sheet = new SheetModel(2, 2, (r, c) => $"V{r}{c}");
        using var fixture = new TestFixture(sheet);
        fixture.Control.SetColumnWidth(0, 150);
        fixture.Control.SetRowHeight(0, 45);

        BRenderList frame = fixture.Session.RenderFrame();
        var fillRects = frame.Commands.OfType<BRenderCommand.FillRect>().Select(r => r.Rect).ToArray();
        double expectedX = CellControl.RowHeaderWidth + 150;
        double expectedY = CellControl.ColumnHeaderHeight + 45;

        // Grid lines rendered at expected column and row positions
        Assert.Contains(fillRects, r => r.X == expectedX && r.Width >= 1);
        Assert.Contains(fillRects, r => r.Y == expectedY && r.Height >= 1);
    }

    private sealed class TestFixture : IDisposable
    {
#pragma warning disable CS0618
        private readonly StandardLegacyGraphicsInputAdapter _input = new("resize-test");
#pragma warning restore CS0618

        public TestFixture(SheetModel sheet, double width = 1000, double height = 400)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Control = new CellControl(sheet, isEditingEnabled: true, areHeadersVisible: true);
            Session.AddRoot(Control);
        }

        public TestHost Host { get; }
        public CellControl Control { get; }
        public UiSession Session { get; }

        public void DispatchPointerMove(BPoint position)
        {
            Session.DispatchInput(_input.FromPointerMove(new BPointerEventArgs(position, BMouseButtons.None, BMouseButtons.None)));
        }

        public void DispatchPointerDown(BPoint position)
        {
            Session.DispatchInput(_input.FromPointerButton(new BPointerEventArgs(position, BMouseButtons.Left, BMouseButtons.Left)));
        }

        public void DispatchPointerUp(BPoint position)
        {
            Session.DispatchInput(_input.FromPointerButton(new BPointerEventArgs(position, BMouseButtons.None, BMouseButtons.Left)));
        }

        public void Dispose() => Session.Dispose();
    }

    private sealed class TestHost : IUiHost, IUiCursorHost
    {
        public BSize ViewportSize { get; set; }
        public double Scale => 1;
        public UiCursorShape CurrentCursor { get; private set; } = UiCursorShape.Arrow;
        public void SetCursor(UiCursorShape shape) => CurrentCursor = shape;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}

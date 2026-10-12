using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class CellGridTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void EverySuppliedValueAndCellBoundaryIsRendered(int rows, int columns)
    {
        using var fixture = new Fixture(new SheetModel(rows, columns, (r, c) => $"{r}:{c}"));
        BRenderList frame = fixture.Session.RenderFrame();
        var text = frame.Commands.OfType<BRenderCommand.DrawText>().Select(command => command.Text.Text).ToArray();
        Assert.Equal(rows * columns, text.Length);
        Assert.Contains("0:0", text);
        Assert.Contains($"{rows - 1}:{columns - 1}", text);
        // Lines include the final bottom and right edges, even for a single cell.
        Assert.Contains(frame.Commands.OfType<BRenderCommand.FillRect>(),
            x => x.Rect == new BRect(columns * CellGrid.CellWidth, 0, 1, rows * CellGrid.CellHeight + 1));
        Assert.Contains(frame.Commands.OfType<BRenderCommand.FillRect>(),
            x => x.Rect == new BRect(0, rows * CellGrid.CellHeight, columns * CellGrid.CellWidth + 1, 1));
    }

    [Fact]
    public void LongTextIsClippedAndDoesNotPaintIntoTheEmptyNeighbour()
    {
        string original = new('W', 100);
        using var fixture = new Fixture(new SheetModel(1, 2, (_, col) => col == 0 ? original : null));
        BRenderList frame = fixture.Session.RenderFrame();
        int index = frame.Commands.ToList().FindIndex(c => c is BRenderCommand.DrawText);
        Assert.Equal(new BRect(1, 1, 95, 27), Assert.IsType<BRenderCommand.PushClip>(frame.Commands[index - 1]).Rect);
        Assert.IsType<BRenderCommand.PopClip>(frame.Commands[index + 1]);
        Assert.Equal(original, fixture.Grid.Sheet[0, 0]);

        using var renderer = new BImageRenderer();
        using var bitmap = renderer.RenderToImage(frame, BSurfaceDescriptor.Default(fixture.Host.ViewportSize),
            new BFrameContext(BColor.White));
        // Check actual raster output, not just the presence of clip commands.
        for (int y = 2; y < 27; y++)
            for (int x = 98; x < 191; x++)
                Assert.Equal(BColor.White, bitmap.GetPixel(x, y));
    }

    [Fact]
    public void DisplayingControlCharactersDoesNotAlterTheModel()
    {
        const string original = "one\r\ntwo\tthree";
        using var fixture = new Fixture(new SheetModel(1, 1, (_, _) => original));
        var text = Assert.Single(fixture.Session.RenderFrame().Commands.OfType<BRenderCommand.DrawText>());
        Assert.Equal("one  two three", text.Text.Text);
        Assert.Equal(original, fixture.Grid.Sheet[0, 0]);
    }

    [Fact]
    public void LastCellRemainsReachableAndOffsetsClampAfterResize()
    {
        using var fixture = new Fixture(new SheetModel(initialValue: (r, c) => r == 9 && c == 9 ? "Last cell" : null), 200, 110);
        fixture.Session.RenderFrame();
        Assert.True(fixture.Scroll.HasHorizontalScrollbar);
        Assert.True(fixture.Scroll.HasVerticalScrollbar);
        fixture.Scroll.SetOffset(new BPoint(10000, 10000));
        BRenderList frame = fixture.Session.RenderFrame();
        Assert.Contains(frame.Commands.OfType<BRenderCommand.DrawText>(), t => t.Text.Text == "Last cell");
        UiElement lastCell = fixture.Grid.Children[^1];
        Assert.Equal(lastCell.Bounds, lastCell.GetVisibleBounds());

        fixture.Host.ViewportSize = new BSize(1000, 400);
        fixture.Session.RenderFrame();
        Assert.Equal(new BPoint(0, 0), fixture.Scroll.Offset);
        Assert.False(fixture.Scroll.HasHorizontalScrollbar);
        Assert.False(fixture.Scroll.HasVerticalScrollbar);
    }

    [Fact]
    public void PointerAndKeyboardInputCannotEditOrSelectCells()
    {
        using var fixture = new Fixture(new SheetModel(initialValue: (_, _) => "Original"), 200, 110);
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(20, 15), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(20, 15), BMouseButtons.None, BMouseButtons.Left)));
        fixture.Session.SetFocus(fixture.Scroll);
        foreach (int key in new[] { 0x71, 0x2E, 0x0D }) // F2, Delete, Enter
            fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(key, false, false, false), KeyboardKeyTransition.Down));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('X')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Right, false, false, false), KeyboardKeyTransition.Down));
        Assert.True(fixture.Scroll.HorizontalOffset > 0); // Keyboard input still scrolls.
        fixture.Session.RenderFrame();
        for (int row = 0; row < 10; row++)
            for (int column = 0; column < 10; column++)
                Assert.Equal("Original", fixture.Grid.Sheet[row, column]);
        Assert.All(fixture.Grid.GetSemanticNode().Children, node =>
        {
            Assert.True(node.State.HasFlag(UiSemanticState.ReadOnly));
            Assert.False(node.State.HasFlag(UiSemanticState.Selected));
        });
        Assert.All(fixture.Grid.Children, child => Assert.False(child.CanFocus));
    }

    [Fact]
    public void AccessibilityExposesEmptyAndPopulatedCellsWithoutVisibleHeaders()
    {
        using var fixture = new Fixture(new SheetModel(1, 2, (_, c) => c == 0 ? "Paper" : null));
        BRenderList frame = fixture.Session.RenderFrame();
        Assert.Equal("Paper", Assert.Single(frame.Commands.OfType<BRenderCommand.DrawText>()).Text.Text);
        var cells = fixture.Grid.GetSemanticNode().Children;
        Assert.Equal("Row 1, column 1: Paper", cells[0].Name);
        Assert.Equal("Row 1, column 2: Empty", cells[1].Name);
        Assert.NotEqual(cells[0].Id, cells[1].Id);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(SheetModel sheet, double width = 1000, double height = 400)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Grid = new CellGrid(sheet);
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

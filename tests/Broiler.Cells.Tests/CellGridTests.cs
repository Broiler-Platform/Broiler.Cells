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
        using var fixture = new Fixture(new SheetModel(10, 10, (r, c) => r == 9 && c == 9 ? "Last cell" : null), 200, 110);
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
        using var fixture = new Fixture(new SheetModel(initialValue: (_, _) => "Original"), 200, 110, isEditingEnabled: false);
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

    [Fact]
    public void SelectEditCommitCancelReplaceAndClearWorkOn100x100Sheet()
    {
        using var fixture = new Fixture(new SheetModel(100, 100, (r, c) => $"R{r}C{c}"));
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Select cell at row 10, col 5 by clicking it
        double clickX = 5 * CellGrid.CellWidth + 10;
        double clickY = 10 * CellGrid.CellHeight + 10;
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.None, BMouseButtons.Left)));
        Assert.Equal(new CellPosition(10, 5), fixture.Grid.SelectedCell);
        Assert.False(fixture.Grid.IsEditing);

        // Press F2 to start editing
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x71, false, false, false), KeyboardKeyTransition.Down));
        Assert.True(fixture.Grid.IsEditing);

        // Type additional characters and commit with Enter
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('!')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x0D, false, false, false), KeyboardKeyTransition.Down));
        Assert.False(fixture.Grid.IsEditing);
        Assert.Equal("R10C5!", fixture.Grid.Sheet[10, 5]);
        Assert.Equal(new CellPosition(10, 5), fixture.Grid.SelectedCell);

        // Move to cell (10, 6) and test Cancel with Escape
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Right, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(10, 6), fixture.Grid.SelectedCell);
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x71, false, false, false), KeyboardKeyTransition.Down));
        Assert.True(fixture.Grid.IsEditing);
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('?')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x1B, false, false, false), KeyboardKeyTransition.Down));
        Assert.False(fixture.Grid.IsEditing);
        Assert.Equal("R10C6", fixture.Grid.Sheet[10, 6]);

        // Move to cell (10, 7) and test typing replaces old value
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Right, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(10, 7), fixture.Grid.SelectedCell);
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('N')));
        Assert.True(fixture.Grid.IsEditing);
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('e')));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('w')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x0D, false, false, false), KeyboardKeyTransition.Down));
        Assert.False(fixture.Grid.IsEditing);
        Assert.Equal("New", fixture.Grid.Sheet[10, 7]);

        // Move to cell (10, 8) and test Delete clears cell while not editing
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Right, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(10, 8), fixture.Grid.SelectedCell);
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x2E, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(string.Empty, fixture.Grid.Sheet[10, 8]);
    }

    [Fact]
    public void CancelPreservesOldValueAndCommitChangesOnlyEditedCell()
    {
        using var fixture = new Fixture(new SheetModel(100, 100, (r, c) => $"Val_{r}_{c}"));
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        fixture.Grid.SelectCell(new CellPosition(5, 5));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x71, false, false, false), KeyboardKeyTransition.Down));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('X')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x1B, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal("Val_5_5", fixture.Grid.Sheet[5, 5]);

        // Now commit a new value
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x71, false, false, false), KeyboardKeyTransition.Down));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('Y')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x0D, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal("Val_5_5Y", fixture.Grid.Sheet[5, 5]);

        // Surrounding cells unchanged
        Assert.Equal("Val_4_5", fixture.Grid.Sheet[4, 5]);
        Assert.Equal("Val_6_5", fixture.Grid.Sheet[6, 5]);
        Assert.Equal("Val_5_4", fixture.Grid.Sheet[5, 4]);
        Assert.Equal("Val_5_6", fixture.Grid.Sheet[5, 6]);
        Assert.Equal("Val_0_0", fixture.Grid.Sheet[0, 0]);
        Assert.Equal("Val_99_99", fixture.Grid.Sheet[99, 99]);
    }

    [Fact]
    public void CommittedValuesRemainCorrectAfterScrollingAwayAndBack()
    {
        using var fixture = new Fixture(new SheetModel(100, 100, (r, c) => $"R{r}C{c}"), 300, 200);
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        fixture.Grid.SelectCell(new CellPosition(0, 0));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('H')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x0D, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal("H", fixture.Grid.Sheet[0, 0]);

        // Scroll far away so (0,0) is clipped and not rendered
        fixture.Scroll.SetOffset(new BPoint(5000, 5000));
        BRenderList scrolledAwayFrame = fixture.Session.RenderFrame();
        Assert.DoesNotContain(scrolledAwayFrame.Commands.OfType<BRenderCommand.DrawText>(), t => t.Text.Text == "H");

        // Scroll back to (0,0)
        fixture.Scroll.SetOffset(new BPoint(0, 0));
        BRenderList scrolledBackFrame = fixture.Session.RenderFrame();
        Assert.Contains(scrolledBackFrame.Commands.OfType<BRenderCommand.DrawText>(), t => t.Text.Text == "H");
        Assert.Equal("H", fixture.Grid.Sheet[0, 0]);
    }

    [Fact]
    public void KeyboardNavigationReachesAllFourCornersWithoutLeavingSheet()
    {
        using var fixture = new Fixture(new SheetModel(100, 100));
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        fixture.Grid.SelectCell(new CellPosition(0, 0));

        // Attempting to move past top/left stays at (0, 0)
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Up, false, false, false), KeyboardKeyTransition.Down));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Left, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(0, 0), fixture.Grid.SelectedCell);

        // Move right to top-right corner (0, 99)
        for (int i = 0; i < 150; i++)
            fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Right, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(0, 99), fixture.Grid.SelectedCell);

        // Move down to bottom-right corner (99, 99)
        for (int i = 0; i < 150; i++)
            fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Down, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(99, 99), fixture.Grid.SelectedCell);

        // Move left to bottom-left corner (99, 0)
        for (int i = 0; i < 150; i++)
            fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Left, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(99, 0), fixture.Grid.SelectedCell);

        // Move up back to top-left corner (0, 0)
        for (int i = 0; i < 150; i++)
            fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Up, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(0, 0), fixture.Grid.SelectedCell);
    }

    [Fact]
    public void ClickingAnotherCellCommitsBeforeSelectingIt()
    {
        using var fixture = new Fixture(new SheetModel(10, 10, (r, c) => $"Orig_{r}_{c}"));
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        fixture.Grid.SelectCell(new CellPosition(1, 1));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x71, false, false, false), KeyboardKeyTransition.Down));
        Assert.True(fixture.Grid.IsEditing);
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('+')));

        // Click on cell (2, 2)
        double clickX = 2 * CellGrid.CellWidth + 10;
        double clickY = 2 * CellGrid.CellHeight + 10;
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.None, BMouseButtons.Left)));

        Assert.False(fixture.Grid.IsEditing);
        Assert.Equal("Orig_1_1+", fixture.Grid.Sheet[1, 1]);
        Assert.Equal(new CellPosition(2, 2), fixture.Grid.SelectedCell);
    }

    [Fact]
    public void EditorAndSelectionRemainAlignedWithCellsAfterScrollingAndResizing()
    {
        using var fixture = new Fixture(new SheetModel(100, 100), 500, 300);
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        fixture.Grid.SelectCell(new CellPosition(3, 3));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x71, false, false, false), KeyboardKeyTransition.Down));
        Assert.True(fixture.Grid.IsEditing);

        // Scroll
        fixture.Scroll.SetOffset(new BPoint(96, 28));
        fixture.Session.RenderFrame();

        BRect expectedBounds = fixture.Grid.CellBounds(3, 3);
        // The bounds of CellBounds match Bounds.X + 3*CellWidth + 1
        Assert.Equal(fixture.Grid.Bounds.X + 3 * CellGrid.CellWidth + 1, expectedBounds.X);
        Assert.Equal(fixture.Grid.Bounds.Y + 3 * CellGrid.CellHeight + 1, expectedBounds.Y);

        // Resize
        fixture.Host.ViewportSize = new BSize(800, 600);
        fixture.Session.RenderFrame();
        expectedBounds = fixture.Grid.CellBounds(3, 3);
        Assert.Equal(fixture.Grid.Bounds.X + 3 * CellGrid.CellWidth + 1, expectedBounds.X);
        Assert.Equal(fixture.Grid.Bounds.Y + 3 * CellGrid.CellHeight + 1, expectedBounds.Y);
    }

    [Fact]
    public void EventsAreRaisedOnCommittedAndSelectedCellChanged()
    {
        using var fixture = new Fixture(new SheetModel(10, 10, (r, c) => "Test"));
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        CellPosition? selectedEvent = null;
        CellCommittedEventArgs? committedEvent = null;

        fixture.Grid.SelectedCellChanged += (_, pos) => selectedEvent = pos;
        fixture.Grid.CellCommitted += (_, args) => committedEvent = args;

        fixture.Grid.SelectCell(new CellPosition(2, 4));
        Assert.Equal(new CellPosition(2, 4), selectedEvent);

        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('A')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x0D, false, false, false), KeyboardKeyTransition.Down));

        Assert.NotNull(committedEvent);
        Assert.Equal(new CellPosition(2, 4), committedEvent!.Position);
        Assert.Equal("Test", committedEvent.OldValue);
        Assert.Equal("A", committedEvent.NewValue);
    }

    [Fact]
    public void DoubleClickStartsEditingInPlace()
    {
        using var fixture = new Fixture(new SheetModel(10, 10, (r, c) => $"Cell_{r}_{c}"));
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        double clickX = 1 * CellGrid.CellWidth + 10;
        double clickY = 1 * CellGrid.CellHeight + 10;

        // First click
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.None, BMouseButtons.Left)));
        Assert.Equal(new CellPosition(1, 1), fixture.Grid.SelectedCell);
        Assert.False(fixture.Grid.IsEditing);

        // Second click (quick succession)
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(clickX, clickY), BMouseButtons.None, BMouseButtons.Left)));
        Assert.True(fixture.Grid.IsEditing);
    }

    [Fact]
    public void ContentBeginningWithEqualsIsTreatedAsPlainText()
    {
        using var fixture = new Fixture(new SheetModel(10, 10));
        fixture.Session.RenderFrame();
#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        fixture.Grid.SelectCell(new CellPosition(0, 0));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('=')));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('1')));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('+')));
        fixture.Session.DispatchInput(input.FromText(new BTextInputEventArgs('1')));
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x0D, false, false, false), KeyboardKeyTransition.Down));

        Assert.Equal("=1+1", fixture.Grid.Sheet[0, 0]);
    }

    [Fact]
    public void ActiveCellOutlineAndAccessibilitySelectionStateRendered()
    {
        using var fixture = new Fixture(new SheetModel(2, 2, (r, c) => $"Val{r}{c}"));
        fixture.Session.RenderFrame();

        fixture.Grid.SelectCell(new CellPosition(1, 1));
        BRenderList frame = fixture.Session.RenderFrame();

        // Check focus ring rectangle is drawn around (1, 1)
        BRect cell = fixture.Grid.CellBounds(1, 1);
        Assert.Contains(frame.Commands.OfType<BRenderCommand.FillRect>(), r => r.Rect == new BRect(cell.X - 1, cell.Y - 1, cell.Width + 2, 2));

        // Check accessibility
        var nodes = fixture.Grid.GetSemanticNode().Children;
        Assert.True(nodes[3].State.HasFlag(UiSemanticState.Selected));
        Assert.False(nodes[0].State.HasFlag(UiSemanticState.Selected));
        Assert.False(nodes[3].State.HasFlag(UiSemanticState.ReadOnly));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(SheetModel sheet, double width = 1000, double height = 400, bool isEditingEnabled = true)
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

using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class SelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragAcrossDataCellsSelectsRectangleAndShiftPreservesAnchor(bool shift)
    {
        using var f = new Fixture();
        f.Control.SelectCell(new CellPosition(0, 0));
        f.Down(f.Cell(1, 1), shift ? InputModifiers.Shift : InputModifiers.None);
        f.Session.RenderFrame();
        f.Move(f.Cell(3, 3));
        f.Session.RenderFrame();
        f.Up(f.Cell(3, 3));

        int first = shift ? 0 : 1;
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 5; col++)
                Assert.Equal(row >= first && row <= 3 && col >= first && col <= 3,
                    f.Control.IsCellSelected(row, col));
        Assert.Null(f.Session.CapturedElement);
        f.Move(f.Cell(4, 4));
        Assert.False(f.Control.IsCellSelected(4, 4));
    }

    [Fact]
    public void CellDragCanReverseAndShrinkAndDeleteOnlyClearsSelectedRectangle()
    {
        using var f = new Fixture();
        f.Down(f.Cell(3, 3));
        f.Move(f.Cell(0, 0));
        f.Session.RenderFrame();
        f.Move(f.Cell(2, 1));
        f.Up(f.Cell(2, 1));
        var nodes = f.Control.Grid.GetSemanticNode().Children;
        Assert.Equal(6, nodes.Count(n => n.State.HasFlag(UiSemanticState.Selected)));
        var frame = f.Session.RenderFrame();
        Assert.Contains(frame.Commands.OfType<BRenderCommand.FillRect>(),
            r => r.Rect == f.Control.Grid.CellBounds(3, 3) && r.Color == f.Control.Theme.AccentSoft);
        f.Key(0x2E);
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 5; col++)
                Assert.Equal(row is >= 2 and <= 3 && col is >= 1 and <= 3 ? "" : "value",
                    f.Control.Sheet[row, col]);
    }

    [Fact]
    public void ShiftClickExtendsOriginalAnchorAndPlainClickOnEndpointCollapsesRange()
    {
        using var f = new Fixture();
        f.Down(f.Cell(1, 1));
        f.Move(f.Cell(2, 2));
        f.Up(f.Cell(2, 2));
        f.Down(f.Cell(4, 3), InputModifiers.Shift);
        f.Up(f.Cell(4, 3));
        Assert.True(f.Control.IsCellSelected(1, 1));
        Assert.False(f.Control.IsCellSelected(0, 0));
        Assert.Equal(12, f.Control.Grid.GetSemanticNode().Children.Count(n => n.State.HasFlag(UiSemanticState.Selected)));
        f.Down(f.Cell(4, 3));
        f.Up(f.Cell(4, 3));
        Assert.False(f.Control.IsEditing);
        Assert.Single(f.Control.Grid.GetSemanticNode().Children.Where(n => n.State.HasFlag(UiSemanticState.Selected)));
    }

    [Fact]
    public void CellDragCapturesOutsideGridAndClampsToSheetBounds()
    {
        using var f = new Fixture();
        f.Down(f.Cell(2, 2));
        Assert.Same(f.Control.Grid, f.Session.CapturedElement);
        f.Move(new BPoint(-20, -20));
        f.Up(new BPoint(-20, -20));
        Assert.Equal(new CellPosition(0, 0), f.Control.SelectedCell);
        Assert.Equal(9, f.Control.Grid.GetSemanticNode().Children.Count(n => n.State.HasFlag(UiSemanticState.Selected)));
        Assert.Null(f.Session.CapturedElement);
    }

    [Fact]
    public void CellDragUsesResizedAndScrolledBoundsAndWorksWithoutHeaders()
    {
        using var f = new Fixture(300, 140);
        f.Control.AreHeadersVisible = false;
        f.Control.SetColumnWidth(0, 150);
        f.Control.SetRowHeight(0, 50);
        f.Session.RenderFrame();
        f.Control.ScrollView.SetOffset(new BPoint(120, 30));
        f.Session.RenderFrame();
        f.Down(f.Cell(1, 1));
        f.Session.RenderFrame();
        f.Move(f.Cell(3, 2));
        f.Session.RenderFrame();
        f.Up(f.Cell(3, 2));
        Assert.Equal(6, f.Control.Grid.GetSemanticNode().Children.Count(n => n.State.HasFlag(UiSemanticState.Selected)));
        Assert.True(f.Control.IsCellSelected(1, 1));
        Assert.True(f.Control.IsCellSelected(3, 2));
        Assert.False(f.Control.IsCellSelected(0, 0));
    }

    [Fact]
    public void ChangingSheetOrDisablingEditingCancelsCellDrag()
    {
        using var f = new Fixture();
        f.Down(f.Cell(1, 1));
        f.Control.SetSheet(new SheetModel(5, 5));
        Assert.Null(f.Session.CapturedElement);
        f.Move(f.Cell(2, 2));
        Assert.Null(f.Control.SelectedCell);
        f.Down(f.Cell(1, 1));
        f.Control.IsEditingEnabled = false;
        Assert.Null(f.Session.CapturedElement);
        f.Move(f.Cell(2, 2));
        f.Up(f.Cell(2, 2));
        Assert.Null(f.Control.SelectedCell);
    }

    [Fact]
    public void CellDragCommitsEditorAndHeaderSelectionReplacesRectangle()
    {
        using var f = new Fixture();
        f.Control.SelectCell(new CellPosition(0, 0));
        f.Control.StartEditing("edited");
        f.Down(f.Cell(1, 1));
        f.Move(f.Cell(2, 2));
        f.Up(f.Cell(2, 2));
        Assert.Equal("edited", f.Control.Sheet[0, 0]);
        Assert.False(f.Control.IsEditing);
        Assert.True(f.Control.IsCellSelected(1, 1));
        Assert.True(f.Control.IsCellSelected(2, 2));
        f.Click(true, 4);
        Assert.False(f.Control.IsCellSelected(1, 1));
        Assert.False(f.Control.IsCellSelected(2, 2));
        Assert.Equal(5, f.Control.Grid.GetSemanticNode().Children.Count(n => n.State.HasFlag(UiSemanticState.Selected)));
        f.Down(f.Cell(0, 0));
        f.Move(f.Cell(1, 1));
        f.Up(f.Cell(1, 1));
        Assert.Empty(f.Control.SelectedColumns);
        Assert.Equal(4, f.Control.Grid.GetSemanticNode().Children.Count(n => n.State.HasFlag(UiSemanticState.Selected)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderClickShiftRangeAndControlToggle(bool columns)
    {
        using var f = new Fixture();
        f.Click(columns, 1);
        Assert.Equal(new[] { 1 }, f.Selected(columns));
        f.Click(columns, 3, InputModifiers.Shift);
        Assert.Equal(new[] { 1, 2, 3 }, f.Selected(columns));
        f.Click(columns, 0, InputModifiers.Shift);
        Assert.Equal(new[] { 0, 1 }, f.Selected(columns));
        f.Click(columns, 3, InputModifiers.Control);
        Assert.Equal(new[] { 0, 1, 3 }, f.Selected(columns));
        f.Click(columns, 1, InputModifiers.Control);
        Assert.Equal(new[] { 0, 3 }, f.Selected(columns));
        f.Click(columns, 2);
        Assert.Equal(new[] { 2 }, f.Selected(columns));
        f.Click(columns, 2, InputModifiers.Control);
        Assert.Empty(f.Selected(columns));
        Assert.Null(f.Control.SelectedCell);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragCanExpandAndContractAndPreserveControlSelection(bool columns)
    {
        using var f = new Fixture();
        f.Click(columns, 0);
        f.Down(f.Header(columns, 2), InputModifiers.Control);
        f.Move(f.Header(columns, 4));
        Assert.Equal(new[] { 0, 2, 3, 4 }, f.Selected(columns));
        f.Move(f.Header(columns, 3));
        Assert.Equal(new[] { 0, 2, 3 }, f.Selected(columns));
        f.Up(f.Header(columns, 3));
        f.Move(f.Header(columns, 4));
        Assert.Equal(new[] { 0, 2, 3 }, f.Selected(columns));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlShiftAddsRangeAndReverseDragUsesItsStartingHeader(bool columns)
    {
        using var f = new Fixture();
        f.Click(columns, 0);
        f.Click(columns, 2, InputModifiers.Control);
        f.Click(columns, 4, InputModifiers.Control | InputModifiers.Shift);
        Assert.Equal(new[] { 0, 2, 3, 4 }, f.Selected(columns));
        f.Down(f.Header(columns, 3));
        f.Move(f.Header(columns, 1));
        f.Up(f.Header(columns, 1));
        Assert.Equal(new[] { 1, 2, 3 }, f.Selected(columns));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteClearsOnlySelectedRowsOrColumnsAndRaisesCommitEvents(bool columns)
    {
        using var f = new Fixture();
        var changed = new List<CellPosition>();
        f.Control.CellCommitted += (_, e) => changed.Add(e.Position);
        f.Click(columns, 1);
        f.Click(columns, 3, InputModifiers.Control);
        f.Key(0x2E);
        Assert.Equal(10, changed.Count);
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 5; col++)
                Assert.Equal((columns ? col : row) is 1 or 3 ? "" : "value", f.Control.Sheet[row, col]);
    }

    [Fact]
    public void SelectionHighlightsEmptyCellsAndHeadersAndUpdatesSemantics()
    {
        using var f = new Fixture();
        f.Control.Sheet[2, 0] = "";
        f.Click(false, 2);
        var frame = f.Session.RenderFrame();
        Assert.Contains(frame.Commands.OfType<BRenderCommand.FillRect>(),
            r => r.Rect == f.Control.Grid.CellBounds(2, 0) && r.Color == f.Control.Theme.AccentSoft);
        Assert.Contains(frame.Commands.OfType<BRenderCommand.FillRect>(),
            r => r.Rect == new BRect(0, 85, 47, 27) && r.Color == f.Control.Theme.AccentSoft);
        var nodes = f.Control.Grid.GetSemanticNode().Children;
        Assert.Equal(5, nodes.Count(n => n.State.HasFlag(UiSemanticState.Selected)));
        f.Control.SelectCell(f.Control.SelectedCell!.Value);
        Assert.Empty(f.Control.SelectedRows);
        Assert.Single(f.Control.Grid.GetSemanticNode().Children.Where(n => n.State.HasFlag(UiSemanticState.Selected)));
    }

    [Fact]
    public void SelectingHeaderCommitsEditAndSwitchingAxisReplacesSelection()
    {
        using var f = new Fixture();
        f.Control.SelectCell(new CellPosition(0, 0));
        f.Control.StartEditing("edited");
        f.Click(false, 1);
        Assert.False(f.Control.IsEditing);
        Assert.Equal("edited", f.Control.Sheet[0, 0]);
        f.Click(true, 3, InputModifiers.Control);
        Assert.Empty(f.Control.SelectedRows);
        Assert.Equal(new[] { 3 }, f.Control.SelectedColumns);
        f.Key(0x25);
        Assert.Empty(f.Control.SelectedColumns);
        Assert.Equal(new CellPosition(1, 2), f.Control.SelectedCell);
    }

    [Fact]
    public void ResetAndReadOnlyClearSelectionAndNotifyEvenWhenActiveCellIsUnchanged()
    {
        using var f = new Fixture();
        int changes = 0;
        f.Control.SelectionChanged += (_, _) => changes++;
        f.Control.SelectCell(new CellPosition(0, 0));
        f.Control.SelectRow(0);
        Assert.Equal(2, changes);
        f.Control.SetSheet(new SheetModel(2, 2));
        Assert.Empty(f.Control.SelectedRows);
        Assert.Null(f.Control.SelectedCell);
        f.Control.SelectColumn(1);
        f.Control.IsEditingEnabled = false;
        Assert.Empty(f.Control.SelectedColumns);
        Assert.Null(f.Control.SelectedCell);
        f.Click(false, 0);
        Assert.Null(f.Control.SelectedCell);
        f.Control.SelectRow(0);
        Assert.Empty(f.Control.SelectedRows);
    }

    [Fact]
    public void HeadersUseResizedAndScrolledOffsetsAndDividersStillResize()
    {
        using var f = new Fixture(300, 140);
        f.Control.SetColumnWidth(0, 150);
        f.Control.SetRowHeight(0, 50);
        f.Session.RenderFrame();
        f.Control.ScrollView.SetOffset(new BPoint(120, 30));
        f.Session.RenderFrame();
        f.Click(true, 2);
        Assert.Equal(new[] { 2 }, f.Control.SelectedColumns);
        f.Click(false, 2);
        Assert.Equal(new[] { 2 }, f.Control.SelectedRows);
        var divider = new BPoint(48 + f.Control.Grid.GetColumnOffset(2) - f.Control.ScrollView.HorizontalOffset, 14);
        f.Down(divider);
        f.Move(new BPoint(divider.X + 20, divider.Y));
        f.Up(new BPoint(divider.X + 20, divider.Y));
        Assert.Equal(116, f.Control.GetColumnWidth(1));
        Assert.Equal(new[] { 2 }, f.Control.SelectedRows);
    }

    [Fact]
    public void SelectionIndicesAreValidated()
    {
        var grid = new CellGrid(new SheetModel(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SelectRow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SelectRow(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SelectColumn(3));
    }

    private sealed class Fixture : IDisposable
    {
        private long _sequence;
#pragma warning disable CS0618
        private readonly StandardLegacyGraphicsInputAdapter _keys = new("selection-keys");
#pragma warning restore CS0618
        public Fixture(double width = 650, double height = 250)
        {
            Session = new StandardUiSessionBuilder().Build(new Host(new BSize(width, height)));
            Control = new CellControl(new SheetModel(5, 5, (_, _) => "value"));
            Session.AddRoot(Control);
            Session.RenderFrame();
        }

        public CellControl Control { get; }
        public UiSession Session { get; }
        public IReadOnlyList<int> Selected(bool columns) => columns ? Control.SelectedColumns : Control.SelectedRows;
        public BPoint Cell(int row, int column)
        {
            var bounds = Control.Grid.CellBounds(row, column);
            return new BPoint(bounds.X + 10, bounds.Y + 10);
        }
        public BPoint Header(bool columns, int index) => columns
            ? new BPoint(48 + Control.Grid.GetColumnOffset(index) + 10 - Control.ScrollView.HorizontalOffset, 14)
            : new BPoint(24, 28 + Control.Grid.GetRowOffset(index) + 10 - Control.ScrollView.VerticalOffset);
        private InputEventHeader NextHeader() => new(InputDeviceId.FromOpaqueValue("selection"),
            new InputTimestamp(++_sequence, TimeSpan.TicksPerSecond, "selection"), _sequence);
        public void Down(BPoint p, InputModifiers modifiers = InputModifiers.None) =>
            Session.DispatchInput(UiInputEvent.FromMouseButton(new MouseButtonEvent(NextHeader(),
                InputPoint.ClientDeviceIndependentPixels(p.X, p.Y), MouseButtons.Left, MouseButton.Left,
                MouseButtonTransition.Down, InputEventSource.Synthetic, modifiers)));
        public void Up(BPoint p) =>
            Session.DispatchInput(UiInputEvent.FromMouseButton(new MouseButtonEvent(NextHeader(),
                InputPoint.ClientDeviceIndependentPixels(p.X, p.Y), MouseButtons.None, MouseButton.Left,
                MouseButtonTransition.Up, InputEventSource.Synthetic)));
        public void Move(BPoint p) => Session.DispatchInput(UiInputEvent.FromMouseMove(new MouseMoveEvent(NextHeader(),
            InputPoint.ClientDeviceIndependentPixels(p.X, p.Y), MouseButtons.Left, InputEventSource.Synthetic)));
        public void Click(bool columns, int index, InputModifiers modifiers = InputModifiers.None)
        {
            var point = Header(columns, index);
            Down(point, modifiers);
            Up(point);
            Session.RenderFrame();
        }
        public void Key(int key) => Session.DispatchInput(_keys.FromKey(new BKeyEventArgs(key, false, false, false), KeyboardKeyTransition.Down));
        public void Dispose() => Session.Dispose();
    }

    private sealed class Host(BSize size) : IUiHost
    {
        public BSize ViewportSize => size;
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}

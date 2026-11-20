using System;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class SplitPanesTests
{
    [Fact]
    public void SheetModel_SplitState_SetsAndClampsProperly()
    {
        var sheet = new SheetModel(10, 8);
        Assert.False(sheet.HasSplit);
        Assert.Equal(0, sheet.SplitRows);
        Assert.Equal(0, sheet.SplitColumns);

        sheet.SetSplit(3, 2);
        Assert.True(sheet.HasSplit);
        Assert.Equal(3, sheet.SplitRows);
        Assert.Equal(2, sheet.SplitColumns);

        // Clamping to bounds
        sheet.SetSplit(20, 15);
        Assert.Equal(10, sheet.SplitRows);
        Assert.Equal(8, sheet.SplitColumns);

        sheet.SetSplit(-5, -3);
        Assert.Equal(0, sheet.SplitRows);
        Assert.Equal(0, sheet.SplitColumns);
        Assert.False(sheet.HasSplit);
    }

    [Fact]
    public void SheetModel_SplitAndFreezePanes_AreMutuallyExclusive()
    {
        var sheet = new SheetModel(10, 8);

        // Setting freeze panes clears split panes
        sheet.SetSplit(3, 2);
        Assert.True(sheet.HasSplit);

        sheet.FreezePanes(2, 1);
        Assert.False(sheet.HasSplit);
        Assert.Equal(0, sheet.SplitRows);
        Assert.Equal(0, sheet.SplitColumns);
        Assert.Equal(2, sheet.FrozenRows);
        Assert.Equal(1, sheet.FrozenColumns);

        // Setting split panes clears freeze panes
        sheet.SetSplit(4, 3);
        Assert.True(sheet.HasSplit);
        Assert.Equal(4, sheet.SplitRows);
        Assert.Equal(3, sheet.SplitColumns);
        Assert.Equal(0, sheet.FrozenRows);
        Assert.Equal(0, sheet.FrozenColumns);
    }

    [Fact]
    public void SheetModel_ToggleSplit_And_RemoveSplit()
    {
        var sheet = new SheetModel(10, 8);

        // Toggle on with defaults
        sheet.ToggleSplit();
        Assert.True(sheet.HasSplit);
        Assert.Equal(1, sheet.SplitRows);
        Assert.Equal(1, sheet.SplitColumns);

        // Toggle off
        sheet.ToggleSplit();
        Assert.False(sheet.HasSplit);
        Assert.Equal(0, sheet.SplitRows);
        Assert.Equal(0, sheet.SplitColumns);

        // Toggle on with specified coordinates
        sheet.ToggleSplit(4, 3);
        Assert.True(sheet.HasSplit);
        Assert.Equal(4, sheet.SplitRows);
        Assert.Equal(3, sheet.SplitColumns);

        // Remove split explicitly
        sheet.RemoveSplit();
        Assert.False(sheet.HasSplit);
        Assert.Equal(0, sheet.SplitRows);
        Assert.Equal(0, sheet.SplitColumns);
    }

    [Fact]
    public void SheetModel_SplitChanged_FiresOnModifications()
    {
        var sheet = new SheetModel(10, 8);
        int splitEvents = 0;
        sheet.SplitChanged += (_, _) => splitEvents++;

        sheet.SetSplit(2, 3);
        Assert.Equal(1, splitEvents);

        // Setting same values doesn't fire
        sheet.SetSplit(2, 3);
        Assert.Equal(1, splitEvents);

        sheet.RemoveSplit();
        Assert.Equal(2, splitEvents);

        sheet.ToggleSplit(1, 1);
        Assert.Equal(3, splitEvents);

        // Freezing panes clears split and fires SplitChanged
        sheet.FreezePanes(1, 1);
        Assert.Equal(4, splitEvents);
    }

    [Fact]
    public void CellControl_ExposesSplitPropertiesAndMethods()
    {
        var sheet = new SheetModel(8, 6);
        var control = new CellControl(sheet);

        Assert.False(control.HasSplit);
        Assert.Equal(0, control.SplitRows);
        Assert.Equal(0, control.SplitColumns);

        control.SelectCell(new CellPosition(3, 2));
        control.ToggleSplit();

        Assert.True(control.HasSplit);
        Assert.Equal(3, control.SplitRows);
        Assert.Equal(2, control.SplitColumns);

        control.RemoveSplit();
        Assert.False(control.HasSplit);
        Assert.Equal(0, control.SplitRows);
        Assert.Equal(0, control.SplitColumns);

        control.SetSplit(2, 4);
        Assert.True(control.HasSplit);
        Assert.Equal(2, control.SplitRows);
        Assert.Equal(4, control.SplitColumns);
    }

    [Fact]
    public void CellGrid_HitTestSplitter_DetectsHorizontalVerticalAndIntersection()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetSplit(2, 2);

        using var fixture = new TestFixture(sheet, 800, 600);
        fixture.Session.RenderFrame();

        double splitX = fixture.Grid.Bounds.X + fixture.Grid.GetColumnOffset(2);
        double splitY = fixture.Grid.Bounds.Y + fixture.Grid.GetRowOffset(2);

        // Away from splitters
        Assert.Equal(CellGrid.SplitterKind.None, fixture.Grid.HitTestSplitter(new BPoint(splitX + 50, splitY + 50)));

        // On horizontal splitter (away from vertical)
        Assert.Equal(CellGrid.SplitterKind.Horizontal, fixture.Grid.HitTestSplitter(new BPoint(splitX + 50, splitY)));

        // On vertical splitter (away from horizontal)
        Assert.Equal(CellGrid.SplitterKind.Vertical, fixture.Grid.HitTestSplitter(new BPoint(splitX, splitY + 50)));

        // At intersection
        Assert.Equal(CellGrid.SplitterKind.Both, fixture.Grid.HitTestSplitter(new BPoint(splitX, splitY)));
    }

    [Fact]
    public void CellGrid_PointerDrag_MovesSplitterBoundaries()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetSplit(2, 2);

        using var fixture = new TestFixture(sheet, 800, 600);
        fixture.Session.RenderFrame();

        double splitX = fixture.Grid.Bounds.X + fixture.Grid.GetColumnOffset(2);
        double splitY = fixture.Grid.Bounds.Y + fixture.Grid.GetRowOffset(2);

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Press down on horizontal splitter
        fixture.Session.DispatchInput(adapter.FromPointerButton(new BPointerEventArgs(new BPoint(splitX + 50, splitY), BMouseButtons.Left, BMouseButtons.Left)));
        Assert.True(fixture.Grid.IsDraggingSplitter);

        // Move to row 5 position
        double newY = fixture.Grid.Bounds.Y + fixture.Grid.GetRowOffset(5) + 10;
        fixture.Session.DispatchInput(adapter.FromPointerMove(new BPointerEventArgs(new BPoint(splitX + 50, newY), BMouseButtons.None, BMouseButtons.Left)));

        // SplitRows should have updated to 5
        Assert.Equal(5, sheet.SplitRows);

        // Release mouse
        fixture.Session.DispatchInput(adapter.FromPointerButton(new BPointerEventArgs(new BPoint(splitX + 50, newY), BMouseButtons.None, BMouseButtons.Left)));
        Assert.False(fixture.Grid.IsDraggingSplitter);
    }

    [Fact]
    public void CellGrid_DoubleClickOnSplitter_RemovesSplit()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetSplit(3, 3);

        using var fixture = new TestFixture(sheet, 800, 600);
        fixture.Session.RenderFrame();

        double splitX = fixture.Grid.Bounds.X + fixture.Grid.GetColumnOffset(3);
        double splitY = fixture.Grid.Bounds.Y + fixture.Grid.GetRowOffset(3);

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Double-click on vertical splitter line (away from horizontal line)
        var p = new BPoint(splitX, splitY + 60);
        fixture.Session.DispatchInput(adapter.FromPointerButton(new BPointerEventArgs(p, BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(adapter.FromPointerButton(new BPointerEventArgs(p, BMouseButtons.None, BMouseButtons.Left)));
        // Second click in double click window
        fixture.Session.DispatchInput(adapter.FromPointerButton(new BPointerEventArgs(p, BMouseButtons.Left, BMouseButtons.Left)));

        // Vertical split removed; horizontal split retained
        Assert.Equal(0, sheet.SplitColumns);
        Assert.Equal(3, sheet.SplitRows);
    }

    [Fact]
    public void MultiWindow_TwoViewsSharingSameWorkbook_SynchronizeEditsInRealTime()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Original";

        var view1 = new CellsView(sheet);
        var view2 = new CellsView(sheet);

        Assert.Equal("Original", view1.CellControl.Sheet[0, 0]);
        Assert.Equal("Original", view2.CellControl.Sheet[0, 0]);

        // Edit cell in view1
        view1.CellControl.SelectCell(new CellPosition(0, 0));
        view1.CellEditLine.Text = "Updated via View 1";
        view1.CellControl.SetCellValue(0, 0, "Updated via View 1");

        // Immediately reflected in view2
        Assert.Equal("Updated via View 1", view2.CellControl.Sheet[0, 0]);

        // Set note via view1
        view1.CellControl.Sheet.SetCellNote(1, 1, "Shared Note", "Reviewer");
        Assert.NotNull(view2.CellControl.Sheet.GetCellNote(1, 1));
        Assert.Equal("Shared Note", view2.CellControl.Sheet.GetCellNote(1, 1)!.Text);

        // Set validation via view1
        var range = new CellRangeAddress(new CellAddress(2, 2), new CellAddress(2, 2));
        view1.CellControl.Sheet.AddDataValidation(new DataValidation([range], DataValidationType.List, Formula1: "A,B,C"));
        Assert.NotNull(view2.CellControl.Sheet.GetDataValidation(2, 2));

        // Set split via view1
        view1.CellControl.SetSplit(3, 2);
        Assert.True(view2.CellControl.HasSplit);
        Assert.Equal(3, view2.CellControl.SplitRows);
        Assert.Equal(2, view2.CellControl.SplitColumns);
    }

    [Fact]
    public void CellsView_Commands_ExecuteSplitAndNewWindowCommands()
    {
        var sheet = new SheetModel(8, 8);
        var view = new CellsView(sheet);

        view.CellControl.SelectCell(new CellPosition(3, 2));

        // view.split toggles on at selection
        Assert.True(view.CommandDispatcher.TryExecute("view.split"));
        Assert.True(view.CellControl.HasSplit);
        Assert.Equal(3, view.CellControl.SplitRows);
        Assert.Equal(2, view.CellControl.SplitColumns);

        // view.split toggles off
        Assert.True(view.CommandDispatcher.TryExecute("view.split"));
        Assert.False(view.CellControl.HasSplit);

        // view.split-horizontal
        view.CellControl.SelectCell(new CellPosition(4, 1));
        Assert.True(view.CommandDispatcher.TryExecute("view.split-horizontal"));
        Assert.Equal(4, view.CellControl.SplitRows);
        Assert.Equal(0, view.CellControl.SplitColumns);

        // view.split-vertical
        view.CellControl.SelectCell(new CellPosition(1, 5));
        Assert.True(view.CommandDispatcher.TryExecute("view.split-vertical"));
        Assert.Equal(0, view.CellControl.SplitRows);
        Assert.Equal(5, view.CellControl.SplitColumns);

        // view.split-remove
        Assert.True(view.CommandDispatcher.TryExecute("view.split-remove"));
        Assert.False(view.CellControl.HasSplit);

        // window.new-window
        CellsView? spawnedView = null;
        view.NewWindowOpened += (_, v) => spawnedView = v;
        Assert.True(view.CommandDispatcher.TryExecute("window.new-window"));
        Assert.NotNull(spawnedView);
        Assert.Same(view.CellControl.Sheet, spawnedView.CellControl.Sheet);
    }

    [Fact]
    public void Xlsx_StreamRoundtrip_PreservesSplitPanes()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Header";
        sheet[2, 2] = "Content";
        sheet.SetSplit(2, 3);

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "test.xlsx");

        ms.Position = 0;
        var loaded = SheetDocumentSerializer.Load(ms, "test.xlsx");

        Assert.NotNull(loaded);
        Assert.True(loaded.HasSplit);
        Assert.Equal(2, loaded.SplitRows);
        Assert.Equal(3, loaded.SplitColumns);
        Assert.Equal(0, loaded.FrozenRows);
        Assert.Equal(0, loaded.FrozenColumns);
        Assert.Equal("Header", loaded[0, 0]);
        Assert.Equal("Content", loaded[2, 2]);
    }

    [Fact]
    public void Ods_StreamRoundtrip_PreservesSplitPanes()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Item";
        sheet[1, 1] = "Value";
        sheet.SetSplit(3, 1);

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "test.ods");

        ms.Position = 0;
        var loaded = SheetDocumentSerializer.Load(ms, "test.ods");

        Assert.NotNull(loaded);
        Assert.True(loaded.HasSplit);
        Assert.Equal(3, loaded.SplitRows);
        Assert.Equal(1, loaded.SplitColumns);
        Assert.Equal(0, loaded.FrozenRows);
        Assert.Equal(0, loaded.FrozenColumns);
        Assert.Equal("Item", loaded[0, 0]);
        Assert.Equal("Value", loaded[1, 1]);
    }

    [Fact]
    public void CellControl_ScrollCellIntoView_AccountsForSplitPanes()
    {
        var sheet = new SheetModel(30, 20);
        var control = new CellControl(sheet);
        control.SetSplit(2, 2);

        control.Measure(new BSize(600, 400));
        control.Arrange(new BRect(0, 0, 600, 400));

        // Cell in split area (row 0, col 0) should not cause scroll
        control.ScrollCellIntoView(0, 0);
        Assert.Equal(0, control.ScrollView.HorizontalOffset);
        Assert.Equal(0, control.ScrollView.VerticalOffset);

        // Cell far away (row 25, col 15) should cause scroll beyond split boundary
        control.ScrollCellIntoView(25, 15);
        Assert.True(control.ScrollView.VerticalOffset > 0);
        Assert.True(control.ScrollView.HorizontalOffset > 0);
    }

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(SheetModel sheet, double width = 800, double height = 600)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Grid = new CellGrid(sheet);
            Scroll = new StandardScrollView { FocusWhenScrollable = true };
            Scroll.AddChild(Grid);
            Session.AddRoot(Scroll);
            Grid.ScrollOffsetProvider = () => (Scroll.HorizontalOffset, Scroll.VerticalOffset);
        }

        public TestHost Host { get; }
        public UiSession Session { get; }
        public CellGrid Grid { get; }
        public StandardScrollView Scroll { get; }

        public void Dispose()
        {
            Session.Dispose();
        }
    }

    private sealed class TestHost : IUiHost
    {
        public BSize ViewportSize { get; set; } = new(800, 600);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}

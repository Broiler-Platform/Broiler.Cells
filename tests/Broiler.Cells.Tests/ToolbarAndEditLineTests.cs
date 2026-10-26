using System;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.AboutDialog.Standard;
using Broiler.UI.Button.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class ToolbarAndEditLineTests
{
    [Fact]
    public void CellsViewExposesToolbarAndCellEditLine()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        Assert.NotNull(view.Toolbar);
        Assert.NotNull(view.CellEditLine);
        Assert.NotNull(view.Menu);
        Assert.NotNull(view.CellControl);
        Assert.Same(sheet, view.CellControl.Sheet);
        Assert.Equal(UiToolbarOrientation.Horizontal, view.Toolbar.Orientation);
    }

    [Fact]
    public void ToolbarContainsActionButtons()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        Assert.True(view.Toolbar.Children.Count >= 5);
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "New");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "Open");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "Save");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "Cut");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "Copy");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "Paste");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "Clear");
    }

    [Fact]
    public void CellEditLineSynchronizesWithSelectedCell()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Alpha";
        sheet[1, 2] = "Beta";

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));

        Assert.Equal("A1", view.CellEditLine.AddressText);
        Assert.Equal("Alpha", view.CellEditLine.Text);

        view.CellControl.SelectCell(new CellPosition(1, 2));
        Assert.Equal("C2", view.CellEditLine.AddressText);
        Assert.Equal("Beta", view.CellEditLine.Text);
    }

    [Fact]
    public void CellEditLineCommitUpdatesSheetAndFiresCommittedEvent()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Initial";

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));

        CellCommittedEventArgs? committedArgs = null;
        view.CellControl.CellCommitted += (_, args) => committedArgs = args;

        view.CellEditLine.Text = "ModifiedFromEditLine";
        view.CellEditLine.CommitEdit();

        Assert.Equal("ModifiedFromEditLine", sheet[0, 0]);
        Assert.NotNull(committedArgs);
        Assert.Equal(new CellPosition(0, 0), committedArgs.Position);
        Assert.Equal("Initial", committedArgs.OldValue);
        Assert.Equal("ModifiedFromEditLine", committedArgs.NewValue);
    }

    [Fact]
    public void CellEditLineCancelRevertsText()
    {
        var sheet = new SheetModel(5, 5);
        sheet[1, 1] = "Original";

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(1, 1));

        view.CellEditLine.Text = "UncommittedTyping";
        view.CellEditLine.CancelEdit();

        Assert.Equal("Original", view.CellEditLine.Text);
        Assert.Equal("Original", sheet[1, 1]);
    }

    [Fact]
    public void LayoutPlacesCellEditLineBetweenToolbarAndCellGrid()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        view.Measure(new BSize(1000, 600));
        view.Arrange(new BRect(0, 0, 1000, 600));

        double menuBottom = view.Menu.Bounds.Bottom;
        double toolbarTop = view.Toolbar.Bounds.Top;
        double toolbarBottom = view.Toolbar.Bounds.Bottom;
        double editLineTop = view.CellEditLine.Bounds.Top;
        double editLineBottom = view.CellEditLine.Bounds.Bottom;
        double gridTop = view.CellControl.Bounds.Top;

        Assert.True(toolbarTop >= menuBottom, "Toolbar should be below Menu");
        Assert.True(editLineTop >= toolbarBottom, "Cell-Edit-Line should be below Toolbar");
        Assert.True(gridTop >= editLineBottom, "Cell Grid should be below Cell-Edit-Line");
    }

    [Fact]
    public void SetCellValueUpdatesGridAndSheet()
    {
        var sheet = new SheetModel(5, 5);
        var control = new CellControl(sheet);

        control.SetCellValue(new CellPosition(2, 3), "DirectValue");

        Assert.Equal("DirectValue", sheet[2, 3]);
    }

    [Fact]
    public void StandaloneCellEditLineAttachAndDetach()
    {
        var sheet = new SheetModel(3, 3);
        var control = new CellControl(sheet);
        var editLine = new CellEditLine();

        editLine.Attach(control);
        control.SelectCell(new CellPosition(0, 0));
        control.SetCellValue(new CellPosition(0, 0), "Hello");
        Assert.Equal("Hello", editLine.Text);

        editLine.Detach();
        control.SetCellValue(new CellPosition(0, 0), "World");
        Assert.Equal("Hello", editLine.Text);
    }
}

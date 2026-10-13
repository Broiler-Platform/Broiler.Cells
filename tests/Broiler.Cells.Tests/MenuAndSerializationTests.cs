using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.Windows;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.UI;
using Broiler.UI.Dialog;
using Broiler.UI.FileDialog;
using Broiler.UI.FileDialog.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Standard;
using Broiler.UI.Window.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class MenuAndSerializationTests
{
    [Fact]
    public void DisposedFileDialogThrowsOnSelectedPathAccess()
    {
        var dialog = new StandardFileDialog { FileName = "Spreadsheet.csv" };
        var acceptedResult = new UiDialogResult(UiDialogResultKind.Accepted, "C:\\test\\Spreadsheet.csv");
        dialog.Dispose();

        Assert.True(dialog.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = dialog.SelectedPath);
        Assert.Equal("C:\\test\\Spreadsheet.csv", acceptedResult.Value);
    }

    [Fact]
    public void CellControlSetSheetUpdatesSheetAndDimensions()
    {
        var initialSheet = new SheetModel(3, 3);
        initialSheet[0, 0] = "Old";
        var control = new CellControl(initialSheet);
        control.SelectCell(new CellPosition(1, 1));
        Assert.Equal(new CellPosition(1, 1), control.SelectedCell);

        var newSheet = new SheetModel(5, 4);
        newSheet[0, 0] = "New";
        control.SetSheet(newSheet);

        Assert.Same(newSheet, control.Sheet);
        Assert.Null(control.SelectedCell);
        Assert.Equal(5, control.Sheet.RowCount);
        Assert.Equal(4, control.Sheet.ColumnCount);
        Assert.Equal("New", control.Sheet[0, 0]);

        control.Measure(new BSize(600, 400));
        control.Arrange(new BRect(0, 0, 600, 400));
    }

    [Fact]
    public void ReproDisposedSemanticCellOnOpen()
    {
        var sheet = new SheetModel(initialValue: (r, c) => "cell");
        var view = new CellsView(sheet);
        var rootWindow = new StandardWindow();
        rootWindow.AddChild(view);
        view.OwnerWindow = rootWindow;

        var host = new TestHost { ViewportSize = new BSize(1000, 400) };
        var session = new StandardUiSessionBuilder().Build(host);
        session.AddRoot(rootWindow);

        session.RenderFrame();

        var newSheet = new SheetModel(5, 5, (r, c) => "new");
        view.CellControl.SetSheet(newSheet);

        session.RenderFrame();
    }

    private sealed class TestHost : IUiHost
    {
        public BSize ViewportSize { get; set; } = new(1000, 400);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    [Fact]
    public void CellControlSetSheetMatchingDimensionsUpdatesInPlace()
    {
        var initialSheet = new SheetModel(2, 2);
        initialSheet[0, 0] = "Alpha";
        var control = new CellControl(initialSheet);
        var initialChildren = control.Grid.Children.ToList();

        var updatedSheet = new SheetModel(2, 2);
        updatedSheet[0, 0] = "Omega";
        control.SetSheet(updatedSheet);

        var currentChildren = control.Grid.Children.ToList();
        Assert.Equal(initialChildren.Count, currentChildren.Count);
        // Children instances are preserved when dimensions match
        for (int i = 0; i < initialChildren.Count; i++)
        {
            Assert.Same(initialChildren[i], currentChildren[i]);
            Assert.False(initialChildren[i].IsDisposed);
        }
        Assert.Equal("Omega", control.Sheet[0, 0]);
    }

    [Fact]
    public void CellControlSetSheetDifferentDimensionsDetachesOldElements()
    {
        var initialSheet = new SheetModel(2, 2);
        var control = new CellControl(initialSheet);
        var initialCellElements = control.Grid.Children.Skip(1).ToList();

        var updatedSheet = new SheetModel(3, 3);
        control.SetSheet(updatedSheet);

        // Previous cell elements must be cleanly detached from the tree without being marked disposed
        foreach (var oldChild in initialCellElements)
        {
            Assert.Null(oldChild.Parent);
            Assert.DoesNotContain(oldChild, control.Grid.Children);
            Assert.False(oldChild.IsDisposed);
        }
        // Editor is kept + 9 new cell elements
        Assert.Equal(10, control.Grid.Children.Count);
        Assert.False(control.Grid.Children.First().IsDisposed);
    }

    [Fact]
    public void MenuBarHasExpectedStructureAndItems()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        var menu = view.Menu;
        Assert.NotNull(menu);
        Assert.Equal(2, menu.Items.Count);

        // File Menu
        UiMenuItem fileMenu = menu.Items[0];
        Assert.Equal("menu.file", fileMenu.Id);
        Assert.Equal("File", fileMenu.Text);
        Assert.Equal('F', fileMenu.AccessKey);

        var nonSeparatorFileItems = fileMenu.Children.Where(i => !i.IsSeparator).ToList();
        Assert.Equal(6, nonSeparatorFileItems.Count);

        // New
        UiMenuItem newItem = nonSeparatorFileItems[0];
        Assert.Equal("file.new", newItem.Id);
        Assert.Equal("New", newItem.Text);
        Assert.Equal("file.new", newItem.CommandName);
        Assert.Equal('N', newItem.AccessKey);
        Assert.Equal("Ctrl+N", newItem.Accelerator);

        // Open
        UiMenuItem openItem = nonSeparatorFileItems[1];
        Assert.Equal("file.open", openItem.Id);
        Assert.Equal("Open", openItem.Text);
        Assert.Equal("file.open", openItem.CommandName);
        Assert.Equal('O', openItem.AccessKey);
        Assert.Equal("Ctrl+O", openItem.Accelerator);

        // Save
        UiMenuItem saveItem = nonSeparatorFileItems[2];
        Assert.Equal("file.save", saveItem.Id);
        Assert.Equal("Save", saveItem.Text);
        Assert.Equal("file.save", saveItem.CommandName);
        Assert.Equal('S', saveItem.AccessKey);
        Assert.Equal("Ctrl+S", saveItem.Accelerator);

        // Save as
        UiMenuItem saveAsItem = nonSeparatorFileItems[3];
        Assert.Equal("file.save-as", saveAsItem.Id);
        Assert.Equal("Save as", saveAsItem.Text);
        Assert.Equal("file.save-as", saveAsItem.CommandName);
        Assert.Equal('A', saveAsItem.AccessKey);
        Assert.Equal("Ctrl+Shift+S", saveAsItem.Accelerator);

        // Close
        UiMenuItem closeItem = nonSeparatorFileItems[4];
        Assert.Equal("file.close", closeItem.Id);
        Assert.Equal("Close", closeItem.Text);
        Assert.Equal("file.close", closeItem.CommandName);
        Assert.Equal('C', closeItem.AccessKey);
        Assert.Equal("Ctrl+W", closeItem.Accelerator);

        // Exit
        UiMenuItem exitItem = nonSeparatorFileItems[5];
        Assert.Equal("file.exit", exitItem.Id);
        Assert.Equal("Exit", exitItem.Text);
        Assert.Equal("file.exit", exitItem.CommandName);
        Assert.Equal('X', exitItem.AccessKey);
        Assert.Equal("Alt+F4", exitItem.Accelerator);

        // Help Menu
        UiMenuItem helpMenu = menu.Items[1];
        Assert.Equal("menu.help", helpMenu.Id);
        Assert.Equal("Help", helpMenu.Text);
        Assert.Equal('H', helpMenu.AccessKey);

        Assert.Single(helpMenu.Children);
        UiMenuItem aboutItem = helpMenu.Children[0];
        Assert.Equal("help.about", aboutItem.Id);
        Assert.Equal("About", aboutItem.Text);
        Assert.Equal("help.about", aboutItem.CommandName);
        Assert.Equal('A', aboutItem.AccessKey);
    }

    [Fact]
    public void FileOperationsSaveAndLoadSpreadsheet()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"broiler_cells_view_{Guid.NewGuid():N}.csv");
        try
        {
            var sheet = new SheetModel(3, 3);
            sheet[0, 0] = "Product";
            sheet[0, 1] = "Price";
            sheet[1, 0] = "Broiler";
            sheet[1, 1] = "99";

            var view = new CellsView(sheet);
            string? changedTitle = null;
            view.TitleChanged += (_, t) => changedTitle = t;

            view.Save(tempFile);

            Assert.True(File.Exists(tempFile));
            Assert.Equal(tempFile, view.CurrentFilePath);
            Assert.Contains(Path.GetFileName(tempFile), view.StatusText);
            Assert.NotNull(changedTitle);
            Assert.Contains(Path.GetFileName(tempFile), changedTitle);

            // Mutate and test SaveDocument uses current file path
            view.CellControl.Sheet[2, 0] = "Widgets";
            view.CellControl.Sheet[2, 1] = "15";
            view.SaveDocument();

            // Create fresh view and Load
            var freshSheet = new SheetModel(10, 10);
            var freshView = new CellsView(freshSheet);
            freshView.Load(tempFile);

            Assert.Equal(tempFile, freshView.CurrentFilePath);
            Assert.Equal(3, freshView.CellControl.Sheet.RowCount);
            Assert.Equal(3, freshView.CellControl.Sheet.ColumnCount);
            Assert.Equal("Product", freshView.CellControl.Sheet[0, 0]);
            Assert.Equal("Price", freshView.CellControl.Sheet[0, 1]);
            Assert.Equal("Broiler", freshView.CellControl.Sheet[1, 0]);
            Assert.Equal("99", freshView.CellControl.Sheet[1, 1]);
            Assert.Equal("Widgets", freshView.CellControl.Sheet[2, 0]);
            Assert.Equal("15", freshView.CellControl.Sheet[2, 1]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void NewDocumentResetsSheetAndStatus()
    {
        var sheet = new SheetModel(4, 4);
        sheet[0, 0] = "Custom";
        var view = new CellsView(sheet);
        string? changedTitle = null;
        view.TitleChanged += (_, t) => changedTitle = t;

        view.Save(Path.Combine(Path.GetTempPath(), "dummy.csv"));
        Assert.NotNull(view.CurrentFilePath);

        view.NewDocument();

        Assert.Null(view.CurrentFilePath);
        Assert.Equal("Session only", view.StatusText);
        Assert.Equal("Broiler Cells", changedTitle);
        Assert.Equal(10, view.CellControl.Sheet.RowCount);
        Assert.Equal(10, view.CellControl.Sheet.ColumnCount);
        Assert.Equal(string.Empty, view.CellControl.Sheet[0, 0]);
    }

    [Fact]
    public void CloseDocumentResetsSheetAndStatus()
    {
        var sheet = new SheetModel(2, 2);
        sheet[0, 0] = "Custom";
        var view = new CellsView(sheet);
        string? changedTitle = null;
        view.TitleChanged += (_, t) => changedTitle = t;

        view.CloseDocument();

        Assert.Null(view.CurrentFilePath);
        Assert.Equal("Session only", view.StatusText);
        Assert.Equal("Broiler Cells", changedTitle);
        Assert.Equal(10, view.CellControl.Sheet.RowCount);
        Assert.Equal(10, view.CellControl.Sheet.ColumnCount);
    }

    [Fact]
    public void ExitApplicationRaisesExitRequested()
    {
        var sheet = new SheetModel(2, 2);
        var view = new CellsView(sheet);
        bool exitRequested = false;
        view.ExitRequested += (_, _) => exitRequested = true;

        bool executed = view.CommandDispatcher.TryExecute("file.exit");
        Assert.True(executed);
        Assert.True(exitRequested);
    }

    [Fact]
    public void CommandDispatcherExecutesFileCommands()
    {
        var sheet = new SheetModel(2, 2);
        sheet[0, 0] = "Test";
        var view = new CellsView(sheet);

        Assert.True(view.CommandDispatcher.TryExecute("file.new"));
        Assert.Equal(10, view.CellControl.Sheet.RowCount);

        Assert.True(view.CommandDispatcher.TryExecute("file.open"));
        Assert.True(view.CommandDispatcher.TryExecute("file.save"));
        Assert.True(view.CommandDispatcher.TryExecute("file.save-as"));

        Assert.True(view.CommandDispatcher.TryExecute("file.close"));
        Assert.Equal(10, view.CellControl.Sheet.RowCount);

        // Help commands are registered
        Assert.True(view.CommandDispatcher.TryExecute("help.about"));
        Assert.True(view.CommandDispatcher.TryExecute("help.info"));
    }

    [Fact]
    public void LayoutAllocatesSpaceForMenuAndCellControl()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        view.Measure(new BSize(800, 600));
        view.Arrange(new BRect(0, 0, 800, 600));

        Assert.Equal(0, view.Menu.Bounds.X);
        Assert.Equal(0, view.Menu.Bounds.Y);
        Assert.Equal(800, view.Menu.Bounds.Width);
        Assert.Equal(view.Menu.MenuBarHeight, view.Menu.Bounds.Height);

        Assert.True(view.CellControl.Bounds.Y >= view.Menu.MenuBarHeight);
        Assert.True(view.CellControl.Bounds.Height > 0);
        Assert.True(view.AddressLabel.Bounds.Y > view.CellControl.Bounds.Y);
        Assert.True(view.StatusLabel.Bounds.Y > view.CellControl.Bounds.Y);
    }
}

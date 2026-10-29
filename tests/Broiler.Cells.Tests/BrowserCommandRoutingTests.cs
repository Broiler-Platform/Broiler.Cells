using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Xunit;

namespace Broiler.Cells.Tests;

public class BrowserCommandRoutingTests
{
    [Fact]
    public async Task HostReceivesAllFileCommandsWithoutDesktopDialogsOrPaths()
    {
        var sheet = new SheetModel(1,1); sheet[0,0] = "keep";
        var actions = new List<string>();
        var view = new CellsView(sheet, fileAction: actions.Add);
        view.NewDocument(); await view.OpenDocumentAsync(); view.SaveDocument();
        await view.SaveDocumentAsAsync(); view.CloseDocument(); view.ExitApplication();
        Assert.Equal(new[] { "new", "open", "export", "export-as", "close", "exit" }, actions);
        Assert.Same(sheet, view.CellControl.Sheet);
        Assert.Equal("keep", sheet[0,0]);
    }

    [Fact]
    public void ClipboardHostMayRefuseCutWithoutClearingCell()
    {
        var sheet = new SheetModel(1,1); sheet[0,0] = "keep";
        var view = new CellsView(sheet) { ClipboardCommand = _ => true };
        view.CellControl.SelectCell(new CellPosition(0,0)); view.CutSelection();
        Assert.Equal("keep", sheet[0,0]);
    }
}

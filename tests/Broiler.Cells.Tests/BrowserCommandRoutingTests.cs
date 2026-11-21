using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.UI.Button.Standard;
using Broiler.UI.ColorDialog.Standard;
using Broiler.UI.Dialog.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public class BrowserCommandRoutingTests
{
    [Fact]
    public void FormatCommandOpensAppearanceControlsAndAppliesFill()
    {
        using var host = new CellsUiHost(() => new BSize(800, 600), () => 1, () => { }, _ => { });
        using var app = new CellsApp(host, () => { }, fileAction: _ => { });
        var view = app.View;
        var sheet = view.CellControl.Sheet;
        view.CellControl.SelectCell(new CellPosition(1, 1));
        Assert.True(view.CommandDispatcher.TryExecute("format.appearance"));

        var dialog = Assert.IsType<StandardDialog>(app.Session.ModalElement);
        Assert.Equal("Cell Appearance", dialog.Title);
        var fill = Assert.Single(Assert.Single(dialog.Children).Children.OfType<StandardButton>(),
            button => button.Text == "Fill color…");

        fill.Click();
        var chooser = Assert.IsType<StandardColorDialog>(app.Session.ModalElement);
        chooser.SelectedColor = new BColor(40, 120, 184);
        chooser.Cancel();
        Assert.Same(dialog, app.Session.ModalElement);
        Assert.Equal(default, sheet.GetContent(1, 1).Shading);

        fill.Click();
        chooser = Assert.IsType<StandardColorDialog>(app.Session.ModalElement);
        chooser.SelectedColor = new BColor(40, 120, 184);
        chooser.AcceptSelection();
        Assert.False(dialog.IsPresented);
        Assert.Equal(new BColor(40, 120, 184), sheet.GetContent(1, 1).Shading);
        Assert.Equal(default, sheet.GetContent(0, 0).Shading);
    }

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

using System;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.AboutDialog.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Window.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class CellsAboutTests
{

    [Theory]
    [InlineData(1200, 800)]
    [InlineData(400, 300)]
    public void CellsAbout_Show_Creates_Centered_Modal_Dialog(double width, double height)
    {
        using var host = new CellsUiHost(
            () => new BSize(width, height),
            () => 1,
            () => { },
            _ => { });

        var session = new StandardUiSessionBuilder().Build(host);
        var rootWindow = new StandardWindow();
        session.AddRoot(rootWindow);
        rootWindow.Arrange(new BRect(0, 0, width, height));

        Uri? openedUrl = null;
        var dialog = CellsAbout.Show(rootWindow, new BSize(width, height), typeof(CellsView).Assembly, url => openedUrl = url);

        Assert.NotNull(dialog);
        Assert.True(dialog.IsPresented);
        Assert.True(dialog.IsModal);
        Assert.Equal("Broiler Cells", dialog.ProductName);
        Assert.Equal("About Broiler Cells", dialog.Title);
        Assert.Equal("https://broiler-platform.github.io/", dialog.ProductUrl?.AbsoluteUri);

        Assert.InRange(dialog.Bounds.Left, 0, width);
        Assert.InRange(dialog.Bounds.Right, 0, width);
        Assert.InRange(dialog.Bounds.Top, 0, height);
        Assert.InRange(dialog.Bounds.Bottom, 0, height);

        dialog.WebsiteButton.Click();
        Assert.Equal(dialog.ProductUrl, openedUrl);

        dialog.OkButton.Click();
        Assert.False(dialog.IsPresented);
    }

    [Fact]
    public void CellsApp_HelpAbout_Command_Shows_Dialog()
    {
        using var host = new CellsUiHost(
            () => new BSize(1000, 600),
            () => 1,
            () => { },
            _ => { });

        Uri? openedUrl = null;
        using var app = new CellsApp(
            host,
            requestClose: () => { },
            openUrl: url => openedUrl = url);

        app.RenderFrame();

        Assert.True(app.View.CommandDispatcher.TryExecute("help.about"));
        var dialog = Assert.IsType<StandardAboutDialog>(app.Session.FocusedElement);
        Assert.True(dialog.IsPresented);
        Assert.True(dialog.IsModal);
        app.RenderFrame();

        dialog.WebsiteButton.Click();
        Assert.Equal(new Uri("https://broiler-platform.github.io/"), openedUrl);

        dialog.OkButton.Click();
        Assert.False(dialog.IsPresented);
    }
}

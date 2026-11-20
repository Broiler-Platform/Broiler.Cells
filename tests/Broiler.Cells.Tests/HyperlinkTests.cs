using System;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents.Model;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class HyperlinkTests
{
    [Fact]
    public void SheetModel_SetGetClear_Hyperlink_WorksAndAppliesDefaultStyle()
    {
        var sheet = new SheetModel(5, 5);
        Assert.False(sheet.HasCellHyperlink(1, 1));
        Assert.Null(sheet.GetCellHyperlink(1, 1));

        // Setting a link on an unstyled cell applies blue underlined styling
        sheet.SetCellHyperlink(1, 1, "https://example.com", "Example Link");
        Assert.True(sheet.HasCellHyperlink(1, 1));
        var link = sheet.GetCellHyperlink(1, 1);
        Assert.NotNull(link);
        Assert.Equal("https://example.com", link.Target);
        Assert.Equal("Example Link", link.Tooltip);
        Assert.False(link.IsInternal);

        var content = sheet.GetContent(1, 1);
        Assert.True(content.Font.Underline);
        Assert.Equal(new BColor(17, 85, 204), content.Font.Foreground);

        // Clearing removes the link
        sheet.ClearCellHyperlink(1, 1);
        Assert.False(sheet.HasCellHyperlink(1, 1));
        Assert.Null(sheet.GetCellHyperlink(1, 1));
    }

    [Fact]
    public void SheetModel_InternalReference_DetectedCorrectly()
    {
        var sheet = new SheetModel(5, 5);
        sheet.SetCellHyperlink(0, 0, "#Sheet2!A1", "Go to Sheet 2");
        var link = sheet.GetCellHyperlink(0, 0);
        Assert.NotNull(link);
        Assert.True(link.IsInternal);

        sheet.SetCellHyperlink(0, 1, "Sheet2!B5");
        Assert.True(sheet.GetCellHyperlink(0, 1)!.IsInternal);

        sheet.SetCellHyperlink(0, 2, "#A10");
        Assert.True(sheet.GetCellHyperlink(0, 2)!.IsInternal);

        sheet.SetCellHyperlink(0, 3, "mailto:user@example.com");
        Assert.False(sheet.GetCellHyperlink(0, 3)!.IsInternal);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void SheetDocumentSerializer_Hyperlink_Roundtrip(string extension)
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "External";
        sheet.SetCellHyperlink(0, 0, "https://github.com", "GitHub Homepage");

        sheet[1, 1] = "Internal";
        sheet.SetCellHyperlink(1, 1, "#Sheet2!C10", "Jump to C10");

        using var stream = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, stream, "test" + extension);

        stream.Position = 0;
        var loadedSheet = SheetDocumentSerializer.Load(stream, "test" + extension);

        var link0 = loadedSheet.GetCellHyperlink(0, 0);
        Assert.NotNull(link0);
        Assert.Equal("https://github.com", link0.Target);

        var link1 = loadedSheet.GetCellHyperlink(1, 1);
        Assert.NotNull(link1);
        Assert.True(link1.IsInternal);
        Assert.Contains("Sheet2!C10", link1.Target);
    }

    private sealed class TestHost(BSize size) : IUiHost, IUiCursorHost
    {
        public BSize ViewportSize => size;
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
        public UiCursorShape CurrentCursor { get; private set; } = UiCursorShape.Arrow;
        public void SetCursor(UiCursorShape shape) => CurrentCursor = shape;
    }

    private static InputEventHeader NextHeader(ref long seq) => new(
        InputDeviceId.FromOpaqueValue("hyperlink-test"),
        new InputTimestamp(++seq, TimeSpan.TicksPerSecond, "hyperlink-test"),
        seq);

    [Fact]
    public void CellGrid_HoverAndClick_DispatchesHyperlink()
    {
        var sheet = new SheetModel(10, 10);
        sheet[1, 1] = "Click Me";
        sheet.SetCellHyperlink(1, 1, "https://example.org", "Go to Example");

        var host = new TestHost(new BSize(800, 600));
        using var session = new StandardUiSessionBuilder().Build(host);
        var control = new CellControl(sheet);
        session.AddRoot(control);
        session.RenderFrame();

        var bounds = control.Grid.CellBounds(1, 1);
        var center = new BPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

        long seq = 0;
        // Hover
        session.DispatchInput(UiInputEvent.FromMouseMove(new MouseMoveEvent(NextHeader(ref seq),
            InputPoint.ClientDeviceIndependentPixels(center.X, center.Y), MouseButtons.None, InputEventSource.Synthetic)));

        Assert.Equal(UiCursorShape.Hand, host.CurrentCursor);

        // Click
        CellHyperlink? clickedLink = null;
        control.Grid.HyperlinkClicked += (r, c, hl) => clickedLink = hl;

        session.DispatchInput(UiInputEvent.FromMouseButton(new MouseButtonEvent(NextHeader(ref seq),
            InputPoint.ClientDeviceIndependentPixels(center.X, center.Y), MouseButtons.Left, MouseButton.Left,
            MouseButtonTransition.Down, InputEventSource.Synthetic)));

        session.DispatchInput(UiInputEvent.FromMouseButton(new MouseButtonEvent(NextHeader(ref seq),
            InputPoint.ClientDeviceIndependentPixels(center.X, center.Y), MouseButtons.None, MouseButton.Left,
            MouseButtonTransition.Up, InputEventSource.Synthetic)));

        Assert.NotNull(clickedLink);
        Assert.Equal("https://example.org", clickedLink.Target);
        Assert.Equal("Go to Example", clickedLink.Tooltip);
    }

    [Fact]
    public void CellControl_NavigateHyperlink_InternalTarget_SwitchesSheetAndSelectsCell()
    {
        var sheet1 = new SheetModel(10, 10);
        var sheet2 = new SheetModel(10, 10);
        var wb = new WorkbookModel(sheet1, "Sheet1");
        wb.Add("Sheet2", sheet2);

        var control = new CellControl(sheet1);
        Assert.Same(sheet1, control.Sheet);

        // Navigate to #Sheet2!B4
        bool navigated = control.NavigateHyperlink(new CellHyperlink("#Sheet2!B4", "Jump to B4"));
        Assert.True(navigated);

        // Active sheet should now be Sheet2
        Assert.Same(sheet2, control.Sheet);
        Assert.Equal(wb.Sheets[1], wb.SelectedSheet);

        // Cell B4 is row 3, column 1
        Assert.Equal(new CellPosition(3, 1), control.SelectedCell);
    }

    [Fact]
    public void CellControl_NavigateHyperlink_FiresEvent()
    {
        var sheet = new SheetModel(5, 5);
        var control = new CellControl(sheet);

        bool eventFired = false;
        control.HyperlinkNavigated += (_, e) =>
        {
            eventFired = true;
            Assert.Equal("https://custom.site", e.Hyperlink.Target);
            e.IsHandled = true; // suppress default handling
        };

        bool handled = control.NavigateHyperlink(new CellHyperlink("https://custom.site"));
        Assert.True(eventFired);
        Assert.True(handled);
    }

    [Fact]
    public void HyperlinkPanel_Lifecycle_ApplyAndRemove()
    {
        var panel = new HyperlinkPanel();
        panel.Arrange(new BRect(0, 0, HyperlinkPanel.PanelWidth, HyperlinkPanel.PanelHeight));

        Assert.False(panel.IsPanelVisible);

        var sheet1 = new SheetModel(5, 5);
        var sheet2 = new SheetModel(5, 5);
        var wb = new WorkbookModel(sheet1, "Sheet1");
        wb.Add("Summary", sheet2);

        panel.Show(2, 3, "D3", "Initial Text", null, wb);
        Assert.True(panel.IsPanelVisible);
        Assert.Equal(2, panel.TargetRow);
        Assert.Equal(3, panel.TargetColumn);
        Assert.Equal("Initial Text", panel.DisplayText);
        Assert.False(panel.RemoveButton.IsEnabled);

        // Enter target and tooltip
        panel.TargetText = "https://example.com/data";
        panel.TooltipTextValue = "Data source";

        string? appliedTarget = null;
        string? appliedTip = null;
        string? appliedText = null;

        panel.HyperlinkApplied += (_, e) =>
        {
            appliedTarget = e.Target;
            appliedTip = e.Tooltip;
            appliedText = e.DisplayText;
        };

        panel.Apply();
        Assert.False(panel.IsPanelVisible);
        Assert.Equal("https://example.com/data", appliedTarget);
        Assert.Equal("Data source", appliedTip);
        Assert.Equal("Initial Text", appliedText);

        // Test Remove on existing link
        var existing = new CellHyperlink("https://example.com/data", "Data source");
        panel.Show(2, 3, "D3", "Initial Text", existing, wb);
        Assert.True(panel.IsPanelVisible);
        Assert.True(panel.RemoveButton.IsEnabled);

        bool removed = false;
        panel.HyperlinkRemoved += (_, e) =>
        {
            removed = true;
            Assert.Equal(2, e.Row);
            Assert.Equal(3, e.Column);
        };

        panel.Remove();
        Assert.False(panel.IsPanelVisible);
        Assert.True(removed);
    }

    [Fact]
    public void CellsView_OpenHyperlinkDialog_And_ApplyHyperlink()
    {
        var sheet = new SheetModel(5, 5);
        sheet[1, 1] = "Old Text";

        var view = new CellsView(sheet);
        view.Arrange(new BRect(0, 0, 1024, 768));

        view.CellControl.SelectCell(new CellPosition(1, 1));
        Assert.False(view.IsHyperlinkDialogVisible);

        // Open dialog via command
        view.OpenHyperlinkDialog();
        Assert.True(view.IsHyperlinkDialogVisible);
        Assert.Equal("Old Text", view.HyperlinkDialog.DisplayText);

        // Apply new link and text
        view.HyperlinkDialog.DisplayText = "New Link Text";
        view.HyperlinkDialog.TargetText = "https://example.org";
        view.HyperlinkDialog.TooltipTextValue = "Hover me";
        view.HyperlinkDialog.Apply();

        Assert.False(view.IsHyperlinkDialogVisible);
        Assert.Equal("New Link Text", sheet[1, 1]);
        Assert.True(sheet.HasCellHyperlink(1, 1));
        var link = sheet.GetCellHyperlink(1, 1);
        Assert.NotNull(link);
        Assert.Equal("https://example.org", link.Target);
        Assert.Equal("Hover me", link.Tooltip);

        // Remove link via command
        view.RemoveSelectedHyperlink();
        Assert.False(sheet.HasCellHyperlink(1, 1));
        Assert.Null(sheet.GetCellHyperlink(1, 1));
        Assert.Equal("New Link Text", sheet[1, 1]);
    }
}

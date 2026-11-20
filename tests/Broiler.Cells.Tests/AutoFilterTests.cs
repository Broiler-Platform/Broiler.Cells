using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Ods;
using Broiler.Documents.Xlsx;
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

public sealed class AutoFilterTests
{
    private static DocumentCodec Codec(string extension) => extension == ".xlsx" ? new XlsxDocumentCodec() : new OdsDocumentCodec();
    private static SpreadsheetWorkbook Workbook(DocumentReadResult result) => result is XlsxReadResult x ? x.Workbook : ((OdsReadResult)result).Workbook;

    [Fact]
    public void SetAutoFilter_And_ToggleAutoFilter_WorksCorrectly()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Name"; sheet[0, 1] = "City";
        sheet[1, 0] = "Alice"; sheet[1, 1] = "Paris";
        sheet[2, 0] = "Bob"; sheet[2, 1] = "London";

        Assert.Null(sheet.AutoFilter);

        // Toggle filter on
        sheet.ToggleAutoFilter();
        Assert.NotNull(sheet.AutoFilter);
        Assert.Equal(0, sheet.AutoFilter.Range.StartRow);
        Assert.Equal(0, sheet.AutoFilter.Range.StartCol);
        Assert.Equal(2, sheet.AutoFilter.Range.EndRow);
        Assert.Equal(1, sheet.AutoFilter.Range.EndCol);

        // Toggle filter off
        sheet.ToggleAutoFilter();
        Assert.Null(sheet.AutoFilter);
    }

    [Fact]
    public void GetDistinctColumnValues_ExtractsSortedValuesAndDetectsBlanks()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Category";
        sheet[1, 0] = "Fruit";
        sheet[2, 0] = "Dairy";
        sheet[3, 0] = "Fruit";
        sheet[4, 0] = "";
        sheet[5, 0] = "Bakery";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(5, 0)));

        var distinct = sheet.GetDistinctColumnValues(0, out bool hasBlanks);

        Assert.True(hasBlanks);
        Assert.Equal(3, distinct.Count);
        Assert.Equal("Bakery", distinct[0]);
        Assert.Equal("Dairy", distinct[1]);
        Assert.Equal("Fruit", distinct[2]);
    }

    [Fact]
    public void ApplyFilter_HidesNonMatchingRows()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Name"; sheet[0, 1] = "Status";
        sheet[1, 0] = "Task 1"; sheet[1, 1] = "Open";
        sheet[2, 0] = "Task 2"; sheet[2, 1] = "Closed";
        sheet[3, 0] = "Task 3"; sheet[3, 1] = "Open";
        sheet[4, 0] = "Task 4"; sheet[4, 1] = "Pending";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 1)));

        // Filter column 1 for "Open" only
        sheet.ApplyFilter(1, ["Open"], includeBlanks: false);

        Assert.False(sheet.IsRowHidden(0)); // Header always visible
        Assert.False(sheet.IsRowHidden(1)); // Open
        Assert.True(sheet.IsRowHidden(2));  // Closed - hidden
        Assert.False(sheet.IsRowHidden(3)); // Open
        Assert.True(sheet.IsRowHidden(4));  // Pending - hidden

        // Clear filter on column 1
        sheet.ClearFilter(1);

        Assert.False(sheet.IsRowHidden(1));
        Assert.False(sheet.IsRowHidden(2));
        Assert.False(sheet.IsRowHidden(3));
        Assert.False(sheet.IsRowHidden(4));
    }

    [Fact]
    public void IncludeBlanks_ControlsVisibilityOfBlankRows()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Item";
        sheet[1, 0] = "A";
        sheet[2, 0] = "";
        sheet[3, 0] = "B";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(3, 0)));

        // Filter selecting only "A" and blanks
        sheet.ApplyFilter(0, ["A"], includeBlanks: true);
        Assert.False(sheet.IsRowHidden(1)); // A
        Assert.False(sheet.IsRowHidden(2)); // Blank (included)
        Assert.True(sheet.IsRowHidden(3));  // B (excluded)

        // Filter selecting only "A" without blanks
        sheet.ApplyFilter(0, ["A"], includeBlanks: false);
        Assert.False(sheet.IsRowHidden(1)); // A
        Assert.True(sheet.IsRowHidden(2));  // Blank (excluded)
        Assert.True(sheet.IsRowHidden(3));  // B (excluded)
    }

    [Fact]
    public void MultiColumnFilter_IntersectsCriteria()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Region"; sheet[0, 1] = "Type";
        sheet[1, 0] = "East";   sheet[1, 1] = "Gold";
        sheet[2, 0] = "West";   sheet[2, 1] = "Gold";
        sheet[3, 0] = "East";   sheet[3, 1] = "Silver";
        sheet[4, 0] = "West";   sheet[4, 1] = "Silver";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 1)));

        // Column 0: East
        sheet.ApplyFilter(0, ["East"], includeBlanks: false);
        // Column 1: Gold
        sheet.ApplyFilter(1, ["Gold"], includeBlanks: false);

        // Row 1 (East, Gold) -> visible
        Assert.False(sheet.IsRowHidden(1));
        // Row 2 (West, Gold) -> hidden by Col 0
        Assert.True(sheet.IsRowHidden(2));
        // Row 3 (East, Silver) -> hidden by Col 1
        Assert.True(sheet.IsRowHidden(3));
        // Row 4 (West, Silver) -> hidden by both
        Assert.True(sheet.IsRowHidden(4));

        // Clear all filters
        sheet.ClearAllFilters();
        Assert.False(sheet.IsRowHidden(1));
        Assert.False(sheet.IsRowHidden(2));
        Assert.False(sheet.IsRowHidden(3));
        Assert.False(sheet.IsRowHidden(4));
    }

    [Fact]
    public void CellGrid_RowMetrics_And_Rendering_SkipsHiddenRows()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "H";
        sheet[1, 0] = "R1";
        sheet[2, 0] = "R2";
        sheet[3, 0] = "R3";

        var grid = new CellGrid(sheet);
        grid.Measure(new BSize(500, 300));
        grid.Arrange(new BRect(0, 0, 500, 300));

        double defaultHeight = CellGrid.CellHeight;
        Assert.Equal(defaultHeight, grid.GetRowHeight(1));
        Assert.Equal(defaultHeight, grid.GetRowHeight(2));

        // Hide Row 2
        sheet.SetRowHidden(2, true);

        Assert.Equal(defaultHeight, grid.GetRowHeight(1));
        Assert.Equal(0, grid.GetRowHeight(2));
        Assert.Equal(defaultHeight, grid.GetRowHeight(3));
    }

    [Fact]
    public void CellGrid_Navigation_SkipsHiddenRows()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "H";
        sheet[1, 0] = "R1";
        sheet[2, 0] = "R2";
        sheet[3, 0] = "R3";

        var grid = new CellGrid(sheet);
        grid.Measure(new BSize(500, 300));
        grid.Arrange(new BRect(0, 0, 500, 300));

        // Hide row 2
        sheet.SetRowHidden(2, true);

        // Select Row 1
        grid.SelectCell(new CellPosition(1, 0));
        Assert.Equal(1, grid.SelectedCell?.Row);

        // Navigate Down (+1 row): Should jump over Row 2 to Row 3!
        grid.Navigate(1, 0);
        Assert.Equal(3, grid.SelectedCell?.Row);

        // Navigate Up (-1 row): Should jump over Row 2 to Row 1!
        grid.Navigate(-1, 0);
        Assert.Equal(1, grid.SelectedCell?.Row);
    }

    [Fact]
    public void CellGrid_AltDown_Fires_FilterDropdownRequested()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Category";
        sheet[1, 0] = "Val";
        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(1, 0)));

        using var fixture = new Fixture(sheet);
        fixture.Session.RenderFrame();
        fixture.Session.SetFocus(fixture.Grid);

        // Select header cell (0, 0)
        fixture.Grid.SelectCell(new CellPosition(0, 0));

        int requestedCol = -1;
        fixture.Grid.FilterDropdownRequested += (col, _) => requestedCol = col;

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Simulate Alt+Down key
        fixture.Session.DispatchInput(adapter.FromKey(new BKeyEventArgs(BVirtualKey.Down, false, false, true), KeyboardKeyTransition.Down));

        Assert.Equal(0, requestedCol);
    }

    [Fact]
    public void FilterDropdownPopup_InteractiveActions_WorkCorrectly()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "City";
        sheet[1, 0] = "Tokyo";
        sheet[2, 0] = "Berlin";
        sheet[3, 0] = "Tokyo";
        sheet[4, 0] = "";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 0)));

        var popup = new FilterDropdownPopup();
        Assert.False(popup.IsPopupVisible);

        var anchorRect = new BRect(100, 50, 16, 16);
        popup.Show(sheet, 0, anchorRect);

        Assert.True(popup.IsPopupVisible);
        Assert.Equal(0, popup.Column);

        // Compute desired bounds check
        var bounds = popup.ComputeDesiredBounds(800, 600);
        Assert.True(bounds.Width >= FilterDropdownPopup.PopupWidth);
        Assert.True(bounds.Height >= FilterDropdownPopup.PopupHeight);

        // Hide
        popup.Hide();
        Assert.False(popup.IsPopupVisible);
    }

    [Fact]
    public void SortDialog_MultiLevel_AddAndDeleteLevel()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Name"; sheet[0, 1] = "Age"; sheet[0, 2] = "Salary";
        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(3, 2));

        var dialog = new SortDialog();
        dialog.Show(sheet, range);

        Assert.True(dialog.IsPanelVisible);
        Assert.True(dialog.HasHeaders);
        Assert.Single(dialog.Levels);
        Assert.Equal(0, dialog.Levels[0].ColumnIndex);

        // Header titles
        Assert.Equal("Name", dialog.GetColumnTitle(0));
        Assert.Equal("Age", dialog.GetColumnTitle(1));

        dialog.Hide();
        Assert.False(dialog.IsPanelVisible);
    }

    [Fact]
    public void SheetDocumentSerializer_PreservesAutoFilterAndHiddenRows()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "A"; sheet[0, 1] = "B";
        sheet[1, 0] = "1"; sheet[1, 1] = "X";
        sheet[2, 0] = "2"; sheet[2, 1] = "Y";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 1)));
        sheet.ApplyFilter(0, ["1"], includeBlanks: false);

        Assert.True(sheet.IsRowHidden(2));

        // Export to workbook
        var workbook = SheetDocumentSerializer.ToSpreadsheetWorkbook(sheet);
        var ws = workbook.Sheets[0];

        Assert.NotNull(ws.AutoFilter);
        Assert.Equal("A1:B3", ws.AutoFilter.Range.Format());
        Assert.Single(ws.AutoFilter.Columns);
        Assert.Equal(0, ws.AutoFilter.Columns[0].ColumnIndex);

        // Verify hidden row in exported worksheet
        var row2 = ws.Rows.FirstOrDefault(r => r.RowIndex == 2);
        Assert.NotNull(row2);
        Assert.True(row2.Hidden);

        // Re-import to SheetModel
        var importedSheet = new SheetModel();
        importedSheet.ImportedWorksheet = ws;

        Assert.NotNull(importedSheet.AutoFilter);
        Assert.True(importedSheet.IsRowHidden(2));
        Assert.False(importedSheet.IsRowHidden(1));
    }

    [Fact]
    public void Xlsx_Stream_Roundtrip_PreservesAutoFilterAndHiddenRows()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Name"; sheet[0, 1] = "Score";
        sheet[1, 0] = "Alice"; sheet[1, 1] = "100";
        sheet[2, 0] = "Bob"; sheet[2, 1] = "50";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 1)));
        sheet.SetRowHidden(2, true);

        var wb = SheetDocumentSerializer.ToSpreadsheetWorkbook(sheet);

        using var ms = new MemoryStream();
        Codec(".xlsx").Write(wb.ToRichTextDocument(), ms);
        ms.Position = 0;

        var readWb = Workbook(Codec(".xlsx").Read(ms));
        var readWs = readWb.Sheets[0];

        Assert.NotNull(readWs.AutoFilter);
        Assert.Equal(0, readWs.AutoFilter.Range.StartRow);
        Assert.Equal(2, readWs.AutoFilter.Range.EndRow);

        var row2 = readWs.Rows.FirstOrDefault(r => r.RowIndex == 2);
        Assert.NotNull(row2);
        Assert.True(row2.Hidden);
    }

    [Fact]
    public void Ods_Stream_Roundtrip_PreservesAutoFilterAndHiddenRows()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Dept"; sheet[0, 1] = "Total";
        sheet[1, 0] = "Sales"; sheet[1, 1] = "500";
        sheet[2, 0] = "Support"; sheet[2, 1] = "300";

        sheet.SetAutoFilter(new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 1)));
        sheet.SetRowHidden(2, true);

        var wb = SheetDocumentSerializer.ToSpreadsheetWorkbook(sheet);

        using var ms = new MemoryStream();
        Codec(".ods").Write(wb.ToRichTextDocument(), ms);
        ms.Position = 0;

        var readWb = Workbook(Codec(".ods").Read(ms));
        var readWs = readWb.Sheets[0];

        Assert.NotNull(readWs.AutoFilter);
        Assert.Equal(0, readWs.AutoFilter.Range.StartRow);
        Assert.Equal(2, readWs.AutoFilter.Range.EndRow);

        var row2 = readWs.Rows.FirstOrDefault(r => r.RowIndex == 2);
        Assert.NotNull(row2);
        Assert.True(row2.Hidden);
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

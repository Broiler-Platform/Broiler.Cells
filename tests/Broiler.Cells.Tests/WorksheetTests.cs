using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Geometry;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class WorksheetTests
{
    [Fact]
    public void SwitchingCommitsPendingEditsAndRestoresSelection()
    {
        var first = new SheetModel(3, 3);
        var view = new CellsView(first);
        view.CellControl.SelectCell(new CellPosition(1, 1));
        view.CellEditLine.Text = "pending first edit";
        view.AddWorksheet();
        Assert.Equal("pending first edit", first[1, 1]);
        view.CellControl.SelectCell(new CellPosition(2, 2));
        view.CellEditLine.Text = "second edit";
        view.SelectWorksheet(0);
        Assert.Same(first, view.CellControl.Sheet);
        Assert.Equal(new CellPosition(1, 1), view.CellControl.SelectedCell);
        view.SelectWorksheet(1);
        Assert.Equal("second edit", view.CellControl.Sheet[2, 2]);
        Assert.Equal(new CellPosition(2, 2), view.CellControl.SelectedCell);
    }

    [Fact]
    public void TabSelectionAndConfirmedRemovalUseTheActiveWorksheet()
    {
        var view = new CellsView(new SheetModel(2, 2));
        var first = view.Workbook.SelectedSheet;
        view.AddWorksheet();
        view.WorksheetTabs.SelectTab(first.Id);
        Assert.Same(first.Sheet, view.CellControl.Sheet);
        var remove = view.Children.OfType<Broiler.UI.Button.Standard.StandardButton>().Single(b => b.Text == "Remove");
        remove.Click();
        Assert.Equal(2, view.Workbook.Sheets.Count);
        Assert.Equal("Confirm remove", remove.Text);
        view.SelectWorksheet(1); // Switching cancels the pending removal.
        Assert.Equal("Remove", remove.Text);
        remove.Click();
        remove.Click();
        Assert.Single(view.Workbook.Sheets);
        Assert.Same(first, view.Workbook.SelectedSheet);
        remove.Click();
        Assert.Single(view.Workbook.Sheets);
        var retained = view.Workbook;
        view.Dispose();
        retained.Add(); // A retained workbook must not call a disposed view.
    }

    [Fact]
    public void NamesAreUniqueAndRemovalKeepsActiveSheetIdentity()
    {
        var book = new WorkbookModel(new SheetModel(2, 2));
        var second = book.Add();
        book.Rename(1, "Summary");
        Assert.Throws<ArgumentException>(() => book.Rename(0, "summary"));
        Assert.Throws<ArgumentException>(() => book.Add("Summary"));
        Assert.Throws<ArgumentException>(() => book.Rename(0, "bad/name"));
        Assert.Throws<ArgumentException>(() => book.Rename(0, new string('a', 32)));
        book.Remove(0);
        Assert.Same(second, book.SelectedSheet);
        Assert.Equal(0, book.SelectedIndex);
        Assert.Throws<InvalidOperationException>(() => book.Remove(0));
        Assert.Single(book.Sheets);
    }

    [Fact]
    public void OnlyChangesMarkTheWorkbookDirtyAndRemovedSheetsAreDetached()
    {
        var book = new WorkbookModel(new SheetModel(2, 2));
        var second = book.Add(sheet: new SheetModel(2, 2));
        int changes = 0;
        book.Changed += (_, _) => changes++;
        book.Select(0);
        book.Select(1);
        Assert.Equal(0, changes);
        book.Sheets[0].Sheet[0, 0] = "inactive edit";
        Assert.Equal(1, changes);
        book.Rename(1, "Renamed");
        Assert.Equal(2, changes);
        book.Remove(1);
        Assert.Equal(3, changes);
        second.Sheet[0, 0] = "detached";
        Assert.Equal(3, changes);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void AllEditedSheetsAndStructuralChangesRoundTrip(string extension)
    {
        var first = new SheetModel(3, 3);
        var book = new WorkbookModel(first, "Original");
        first[0, 0] = "first";
        var second = book.Add("Second", new SheetModel(3, 3));
        second.Sheet[2, 2] = "second";
        book.Add("RemoveMe", new SheetModel(1, 1));
        book.Remove(2);
        book.Rename(0, "Renamed");
        using var initial = new MemoryStream();
        SheetDocumentSerializer.Save(first, initial, "book" + extension);
        initial.Position = 0;
        var imported = SheetDocumentSerializer.Load(initial, "book" + extension);
        var loaded = imported.Workbook!;
        Assert.Equal(new[] { "Renamed", "Second" }, loaded.Sheets.Select(s => s.Name));
        loaded.Sheets[0].Sheet[0, 0] = "first edited";
        loaded.Sheets[1].Sheet[2, 2] = "second edited";
        loaded.Select(1);
        using var output = new MemoryStream();
        SheetDocumentSerializer.Save(loaded.SelectedSheet.Sheet, output, "copy" + extension);
        output.Position = 0;
        var restored = SheetDocumentSerializer.Load(output, "copy" + extension).Workbook!;
        Assert.Equal("first edited", restored.Sheets[0].Sheet[0, 0]);
        Assert.Equal("second edited", restored.Sheets[1].Sheet[2, 2]);
        Assert.Equal(2, restored.Sheets.Count);
    }

    [Fact]
    public void NewDocumentDropsOldWorkbookAndPagingKeepsSelectedTabVisible()
    {
        var view = new CellsView(new SheetModel(2, 2));
        for (int i = 0; i < 12; i++) view.AddWorksheet();
        view.Measure(new BSize(400, 600));
        view.Arrange(new BRect(0, 0, 400, 600));
        Assert.Equal(view.Workbook.SelectedSheet.Id, view.WorksheetTabs.SelectedTab!.Id);
        Assert.True(view.WorksheetTabs.Tabs.Count < view.Workbook.Sheets.Count);
        view.NewDocument();
        Assert.Single(view.Workbook.Sheets);
        Assert.Single(view.WorksheetTabs.Tabs);
        Assert.Equal("Sheet1", view.Workbook.SelectedSheet.Name);
    }
}

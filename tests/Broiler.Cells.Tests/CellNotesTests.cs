using System;
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
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class CellNotesTests
{
    private static DocumentCodec Codec(string extension) => extension == ".xlsx" ? new XlsxDocumentCodec() : new OdsDocumentCodec();
    private static SpreadsheetWorkbook Dense(SpreadsheetWorkbook source) => new(source.Sheets.Select(sheet =>
        new SpreadsheetWorksheet(sheet.Name, Enumerable.Range(0, sheet.RowCount).Select(row =>
            new SpreadsheetRow(row, Enumerable.Range(0, sheet.ColumnCount).Select(col =>
                sheet[row, col] ?? new SpreadsheetCell(new CellAddress(row, col), CellValue.Empty)).ToArray())).ToArray())).ToArray());

    [Fact]
    public void SheetModel_CellNotes_GetSetClearAndHas()
    {
        var sheet = new SheetModel(10, 10);

        Assert.False(sheet.HasCellNote(0, 0));
        Assert.Null(sheet.GetCellNote(0, 0));

        var now = DateTimeOffset.UtcNow;
        var note = new CellNote("Important Note", "Alice", now);
        sheet.SetCellNote(0, 0, note);

        Assert.True(sheet.HasCellNote(0, 0));
        var retrieved = sheet.GetCellNote(0, 0);
        Assert.NotNull(retrieved);
        Assert.Equal("Important Note", retrieved.Text);
        Assert.Equal("Alice", retrieved.Author);
        Assert.Equal(now, retrieved.Timestamp);

        // String overload
        sheet.SetCellNote(1, 2, "Review required", "Bob");
        Assert.True(sheet.HasCellNote(1, 2));
        var retrieved2 = sheet.GetCellNote(1, 2);
        Assert.NotNull(retrieved2);
        Assert.Equal("Review required", retrieved2.Text);
        Assert.Equal("Bob", retrieved2.Author);
        Assert.NotNull(retrieved2.Timestamp);

        // Clear note
        sheet.ClearCellNote(0, 0);
        Assert.False(sheet.HasCellNote(0, 0));
        Assert.Null(sheet.GetCellNote(0, 0));

        // Cell (1, 2) still has its note
        Assert.True(sheet.HasCellNote(1, 2));
    }

    [Fact]
    public void SheetModel_CellNotes_PreservedAcrossCellOperations()
    {
        var sheet = new SheetModel(5, 5);
        sheet.SetInput(1, 1, "123.45");
        sheet.SetCellNote(1, 1, "Financial metric", "Analyst");

        // Verify note initially present
        Assert.True(sheet.HasCellNote(1, 1));
        Assert.Equal("Financial metric", sheet.GetCellNote(1, 1)!.Text);

        // 1. Changing cell input preserves note
        sheet.SetInput(1, 1, "999.99");
        Assert.True(sheet.HasCellNote(1, 1));
        Assert.Equal("Financial metric", sheet.GetCellNote(1, 1)!.Text);
        Assert.Equal("999.99", sheet[1, 1]);

        // 2. Cell shading preserves note
        sheet.SetCellShading(1, 1, new BColor(240, 240, 200));
        Assert.True(sheet.HasCellNote(1, 1));
        Assert.Equal("Financial metric", sheet.GetCellNote(1, 1)!.Text);

        // 3. Alignment preserves note
        sheet.SetCellAlignment(1, 1, TextAlignment.Right);
        Assert.True(sheet.HasCellNote(1, 1));
        Assert.Equal("Financial metric", sheet.GetCellNote(1, 1)!.Text);

        // 4. Wrap text preserves note
        sheet.SetCellWrapText(1, 1, true);
        Assert.True(sheet.HasCellNote(1, 1));
        Assert.Equal("Financial metric", sheet.GetCellNote(1, 1)!.Text);

        // 5. Number format preserves note
        sheet.SetNumberFormat(1, 1, "$#,##0.00");
        Assert.True(sheet.HasCellNote(1, 1));
        Assert.Equal("Financial metric", sheet.GetCellNote(1, 1)!.Text);

        // 6. Clearing cell note retains cell content/value
        sheet.ClearCellNote(1, 1);
        Assert.False(sheet.HasCellNote(1, 1));
        Assert.Equal("$999.99", sheet[1, 1]);
        Assert.Equal("999.99", sheet.GetEditText(1, 1));
        Assert.Equal("$#,##0.00", sheet.GetCellNumberFormat(1, 1));
    }

    [Fact]
    public void CellSearchEngine_SearchNotes_OptionControlsNoteMatching()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Revenue Q1";
        sheet[1, 1] = "Expenses Q1";
        sheet.SetCellNote(1, 1, "Audited by KPMG");
        sheet.SetCellNote(3, 3, "KPMG audit pending for next quarter");

        // When SearchNotes is false (default):
        var defaultOpt = new CellSearchOptions { SearchText = "KPMG", SearchNotes = false };
        var matchDefault = CellSearchEngine.FindNext(sheet, null, defaultOpt);
        Assert.Null(matchDefault);

        // When SearchNotes is true:
        var noteOpt = new CellSearchOptions { SearchText = "KPMG", SearchNotes = true };
        var firstMatch = CellSearchEngine.FindNext(sheet, null, noteOpt);
        Assert.NotNull(firstMatch);
        Assert.Equal(new CellPosition(1, 1), firstMatch.Position);
        Assert.Equal("Audited by KPMG", firstMatch.Value);

        var secondMatch = CellSearchEngine.FindNext(sheet, firstMatch.Position, noteOpt);
        Assert.NotNull(secondMatch);
        Assert.Equal(new CellPosition(3, 3), secondMatch.Position);
        Assert.Equal("KPMG audit pending for next quarter", secondMatch.Value);

        // FindPrevious in reverse
        var prevMatch = CellSearchEngine.FindPrevious(sheet, null, noteOpt);
        Assert.NotNull(prevMatch);
        Assert.Equal(new CellPosition(3, 3), prevMatch.Position);

        // FindAll returns both cells
        var allMatches = CellSearchEngine.FindAll(sheet, noteOpt);
        Assert.Equal(2, allMatches.Count);
        Assert.Equal(new CellPosition(1, 1), allMatches[0].Position);
        Assert.Equal(new CellPosition(3, 3), allMatches[1].Position);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void SheetDocumentSerializer_XlsxAndOds_RoundtripsCellNotes(string extension)
    {
        var sheet = new SheetModel(3, 3);
        sheet.SetInput(0, 0, "Quarterly Earnings");
        sheet.SetCellNote(0, 0, "Verified with finance", "CFO");

        // Note on empty cell
        sheet.SetCellNote(0, 1, "Review pending", "Editor");

        // Note on formula cell
        sheet.SetInput(1, 0, "=SUM(1, 2)");
        sheet.SetCellNote(1, 0, "Formula note", "Auditor");

        var wb = SheetDocumentSerializer.ToSpreadsheetWorkbook(sheet);
        Assert.NotNull(wb.Sheets[0][0, 0]?.Note);
        var richDoc = wb.ToRichTextDocument();
        var rewb = SpreadsheetWorkbook.FromRichTextDocument(richDoc);
        Assert.NotNull(rewb.Sheets[0][0, 0]?.Note);

        using var stream = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, stream, "test" + extension);

        stream.Position = 0;
        var readResult = Codec(extension).Read(stream);
        var readWb = extension == ".xlsx" ? ((XlsxReadResult)readResult).Workbook : ((OdsReadResult)readResult).Workbook;
        Assert.NotNull(readWb.Sheets[0][0, 0]?.Note);

        stream.Position = 0;
        var loaded = SheetDocumentSerializer.Load(stream, "test" + extension);

        // Check cell (0, 0)
        Assert.Equal("Quarterly Earnings", loaded[0, 0]);
        Assert.True(loaded.HasCellNote(0, 0));
        var note00 = loaded.GetCellNote(0, 0);
        Assert.NotNull(note00);
        Assert.Equal("Verified with finance", note00.Text);
        Assert.Equal("CFO", note00.Author);

        // Check cell (0, 1) - empty value with note
        Assert.True(loaded.HasCellNote(0, 1));
        var note01 = loaded.GetCellNote(0, 1);
        Assert.NotNull(note01);
        Assert.Equal("Review pending", note01.Text);
        Assert.Equal("Editor", note01.Author);

        // Check cell (1, 0) - formula cell with note
        Assert.True(loaded.HasCellNote(1, 0));
        var note10 = loaded.GetCellNote(1, 0);
        Assert.NotNull(note10);
        Assert.Equal("Formula note", note10.Text);
        Assert.Equal("Auditor", note10.Author);
    }

    [Fact]
    public void CellGrid_RendersTriangleIndicator_And_HoverTooltip()
    {
        var sheet = new SheetModel(3, 3);
        sheet[0, 0] = "CellWithNote";
        sheet.SetCellNote(0, 0, "Hover note text here", "Reviewer");

        using var fixture = new GridFixture(sheet, 600, 400);

        // Render initial frame
        BRenderList frame = fixture.Session.RenderFrame();

        // 1. Verify red corner triangle indicator rendered
        var triangleCommand = frame.Commands.FirstOrDefault(c => c.GetType().Name.Contains("Triangle"));
        Assert.NotNull(triangleCommand);

        // 2. Test TryGetCellAt
        Assert.True(fixture.Grid.TryGetCellAt(new BPoint(10, 10), out int hitRow, out int hitCol));
        Assert.Equal(0, hitRow);
        Assert.Equal(0, hitCol);

        // 3. Pointer move triggers hover state
#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        bool eventFired = false;
        fixture.Grid.HoveredCellChanged += (_, cell) =>
        {
            if (cell == new CellPosition(0, 0))
                eventFired = true;
        };

        fixture.Session.DispatchInput(adapter.FromPointerMove(new BPointerEventArgs(new BPoint(15, 15), BMouseButtons.None, BMouseButtons.None)));

        Assert.True(eventFired);
        Assert.Equal(new CellPosition(0, 0), fixture.Grid.HoveredCell);

        // Render frame with hover active: should contain tooltip text
        BRenderList hoverFrame = fixture.Session.RenderFrame();
        var texts = hoverFrame.Commands.OfType<BRenderCommand.DrawText>().Select(t => t.Text.Text).ToArray();
        Assert.Contains(texts, t => t.Contains("Hover note text here") || t.Contains("Reviewer"));

        // Clear hover
        fixture.Grid.ClearHoveredCell();
        Assert.Null(fixture.Grid.HoveredCell);
    }

    [Fact]
    public void NoteEditPanel_Lifecycle_Events()
    {
        var panel = new NoteEditPanel();
        Assert.False(panel.IsPanelVisible);

        var existingNote = new CellNote("Existing text", "John Doe");
        panel.Show(2, 3, "D3", existingNote);

        Assert.True(panel.IsPanelVisible);
        Assert.Equal(2, panel.TargetRow);
        Assert.Equal(3, panel.TargetColumn);
        Assert.Equal("D3", panel.CellAddressText);
        Assert.Equal("Existing text", panel.NoteText);
        Assert.Equal("John Doe", panel.NoteAuthor);
        Assert.True(panel.DeleteButton.IsEnabled);

        // Save event
        (int Row, int Col, string Text, string? Author)? savedData = null;
        panel.NoteSaved += (_, data) => savedData = data;

        panel.NoteText = "Updated note text";
        panel.Save();

        Assert.False(panel.IsPanelVisible);
        Assert.NotNull(savedData);
        Assert.Equal(2, savedData.Value.Row);
        Assert.Equal(3, savedData.Value.Col);
        Assert.Equal("Updated note text", savedData.Value.Text);
        Assert.Equal("John Doe", savedData.Value.Author);

        // Delete event
        (int Row, int Col)? deletedData = null;
        panel.NoteDeleted += (_, data) => deletedData = data;

        panel.Show(1, 1, "B2", existingNote);
        panel.Delete();

        Assert.False(panel.IsPanelVisible);
        Assert.NotNull(deletedData);
        Assert.Equal(1, deletedData.Value.Row);
        Assert.Equal(1, deletedData.Value.Col);

        // Hide / cancel
        panel.Show(0, 0, "A1", null);
        Assert.True(panel.IsPanelVisible);
        panel.Hide();
        Assert.False(panel.IsPanelVisible);
    }

    [Fact]
    public void CellsView_NoteCommands_Toggle_And_Delete()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Data";
        sheet.SetCellNote(0, 0, "Original Note", "Author A");

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));

        Assert.False(view.IsNoteEditorVisible);

        // Toggle opens note editor
        view.ToggleNoteEditor();
        Assert.True(view.IsNoteEditorVisible);
        Assert.Equal(0, view.NoteEditor.TargetRow);
        Assert.Equal(0, view.NoteEditor.TargetColumn);
        Assert.Equal("Original Note", view.NoteEditor.NoteText);
        Assert.Equal("Author A", view.NoteEditor.NoteAuthor);

        // Toggle again closes note editor
        view.ToggleNoteEditor();
        Assert.False(view.IsNoteEditorVisible);

        // Open and save modification
        view.ShowNoteEditor(0, 0);
        view.NoteEditor.NoteText = "Changed note content";
        view.NoteEditor.Save();

        Assert.False(view.IsNoteEditorVisible);
        Assert.Equal("Changed note content", sheet.GetCellNote(0, 0)!.Text);

        // Delete note
        view.DeleteSelectedNote();
        Assert.False(sheet.HasCellNote(0, 0));
        Assert.Null(sheet.GetCellNote(0, 0));
    }

    [Fact]
    public void CellsView_KeyboardShortcuts_ShiftF2_And_Escape()
    {
        var sheet = new SheetModel(5, 5);
        sheet[1, 1] = "Active Cell";
        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(1, 1));

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        Assert.False(view.IsNoteEditorVisible);

        // Shift+F2 toggles note editor open
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs(0x71, false, true, false), KeyboardKeyTransition.Down));
        Assert.True(view.IsNoteEditorVisible);
        Assert.Equal(1, view.NoteEditor.TargetRow);
        Assert.Equal(1, view.NoteEditor.TargetColumn);

        // Escape closes note editor
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs(0x1B, false, false, false), KeyboardKeyTransition.Down));
        Assert.False(view.IsNoteEditorVisible);
    }

    private sealed class GridFixture : IDisposable
    {
        public GridFixture(SheetModel sheet, double width = 600, double height = 400)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Grid = new CellGrid(sheet, true);
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

using Broiler.Cells.Core;
using Broiler.Documents;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Ods;
using Broiler.Documents.Xlsx;
using Broiler.Graphics.Color;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class WorkbookSerializationTests
{
    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void AppearanceOnEmptyCellSurvivesSaveAndReset(string extension)
    {
        var sheet = new SheetModel(2, 2);
        var color = new BColor(23, 89, 145);
        sheet.SetCellShading(1, 1, color);
        sheet.SetCellFont(1, 1, new InlineStyle { Foreground = color });
        sheet.SetCellBorders(1, 1, CellBorders.All(new TableBorder(color, 2, TableBorderStyle.Dashed)));

        SheetModel RoundTrip(SheetModel input)
        {
            using var bytes = new MemoryStream();
            SheetDocumentSerializer.Save(input, bytes, "appearance" + extension);
            bytes.Position = 0;
            return SheetDocumentSerializer.Load(bytes, "appearance" + extension);
        }

        var restored = RoundTrip(sheet);
        var content = restored.GetContent(1, 1);
        Assert.Equal(color, content.Shading);
        Assert.Equal(color, content.Font.Foreground);
        Assert.Equal(color, content.Borders.Top.Color);
        Assert.Equal(TableBorderStyle.Dashed, content.Borders.Top.Style);
        Assert.Equal(extension == ".xlsx" ? 1.5 : 2, content.Borders.Top.Width);
        Assert.True(content.Value.IsEmpty);

        restored.SetCellShading(1, 1, default);
        restored.SetCellFont(1, 1, content.Font with { Foreground = default });
        restored.SetCellBorders(1, 1, default);
        restored[1, 1] = "Keep the reset cell in the file";
        var exported = SheetDocumentSerializer.ToSpreadsheetWorkbook(restored).Sheets[0][1, 1]!;
        Assert.Equal(0, exported.Shading.A);
        Assert.Equal(0, exported.Font.Foreground.A);
        Assert.False(exported.Borders.IsVisible);
        var reset = RoundTrip(restored).GetContent(1, 1);
        Assert.Equal(0, reset.Shading.A);
        Assert.False(reset.Borders.IsVisible);
        Assert.NotEqual(color, reset.Font.Foreground);
    }

    private static DocumentCodec Codec(string extension) => extension == ".xlsx" ? new XlsxDocumentCodec() : new OdsDocumentCodec();
    private static SpreadsheetWorkbook Workbook(DocumentReadResult result) => result is XlsxReadResult x ? x.Workbook : ((OdsReadResult)result).Workbook;

    // ODS currently serializes cell order; explicit blanks produce a valid sparse-position fixture.
    private static SpreadsheetWorkbook Dense(SpreadsheetWorkbook source) => new(source.Sheets.Select(sheet =>
        new SpreadsheetWorksheet(sheet.Name, Enumerable.Range(0, sheet.RowCount).Select(row =>
            new SpreadsheetRow(row, Enumerable.Range(0, sheet.ColumnCount).Select(col =>
                sheet[row, col] ?? new SpreadsheetCell(new CellAddress(row, col), CellValue.Empty)).ToArray())).ToArray())).ToArray());

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void TypedCellsFormulasSecondSheetAndSparseCoordinatesSurviveEditing(string extension)
    {
        var original = new SpreadsheetWorkbook([
            new SpreadsheetWorksheet("Budget", [
                new SpreadsheetRow(0, [new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromNumber(42))]),
                new SpreadsheetRow(1, []),
                new SpreadsheetRow(2, [
                    new SpreadsheetCell(new CellAddress(2, 1), CellValue.FromNumber(84), formula: "=A1*2", cachedText: "84"),
                    new SpreadsheetCell(new CellAddress(2, 2), CellValue.FromText("before"))])]),
            new SpreadsheetWorksheet("Retained", [new SpreadsheetRow(0, [new SpreadsheetCell(new CellAddress(0, 0), CellValue.True)])])]);
        using var input = new MemoryStream();
        Codec(extension).Write(Dense(original).ToRichTextDocument(), input);
        input.Position = 0;
        var sheet = SheetDocumentSerializer.Load(input, "budget" + extension);
        Assert.Equal("42", sheet[0, 0]);
        Assert.Equal("84", sheet[2, 1]);
        Assert.Contains("first of 2", sheet.ImportNotice);
        sheet[2, 2] = "edited <&> ü";
        using var output = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, output, "copy" + extension);
        output.Position = 0;
        var restored = Workbook(Codec(extension).Read(output));
        Assert.Equal(2, restored.Sheets.Count);
        Assert.Equal("Budget", restored.Sheets[0].Name);
        Assert.Equal(42, restored.Sheets[0][0, 0]!.Value.Number);
        Assert.Equal("=A1*2", restored.Sheets[0][2, 1]!.Formula);
        Assert.Equal("edited <&> ü", restored.Sheets[0][2, 2]!.CachedText);
        Assert.True(restored.Sheets[1][0, 0]!.Value.Boolean);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void NewSheetExportsLiteralFormulaLikeTextAndMultilineUnicode(string extension)
    {
        var sheet = new SheetModel(2, 2);
        sheet[0, 0] = "=1+1";
        sheet[1, 1] = "first\nsecond ü";
        using var stream = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, stream, "new" + extension);
        stream.Position = 0;
        var read = SheetDocumentSerializer.Load(stream, "new" + extension);
        Assert.Equal(sheet[0, 0], read[0, 0]);
        Assert.Equal(sheet[1, 1], read[1, 1]);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void OversizedWorkbookIsRefusedWithoutTruncation(string extension)
    {
        var workbook = new SpreadsheetWorkbook([new SpreadsheetWorksheet("TooWide", [new SpreadsheetRow(0,
            [new SpreadsheetCell(new CellAddress(0, SheetModel.MaximumColumns), CellValue.FromText("outside"))])])]);
        using var stream = new MemoryStream();
        Codec(extension).Write(Dense(workbook).ToRichTextDocument(), stream);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => SheetDocumentSerializer.Load(stream, "wide" + extension));
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void DesktopImportProtectsOriginalButAllowsSavingACopy(string extension)
    {
        string original = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        string copy = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        try
        {
            var sheet = new SheetModel(2, 2);
            sheet[0, 0] = "before";
            SheetDocumentSerializer.Save(sheet, original);
            byte[] bytes = File.ReadAllBytes(original);
            var view = new Broiler.Cells.UI.CellsView(new SheetModel(1, 1));
            view.Load(original);
            Assert.Throws<InvalidDataException>(() => view.Save(original));
            Assert.Equal(bytes, File.ReadAllBytes(original));
            view.Save(copy);
            Assert.Equal("before", SheetDocumentSerializer.Load(copy)[0, 0]);
        }
        finally { File.Delete(original); File.Delete(copy); }
    }

    [Fact]
    public void UnsupportedExportDoesNotOverwriteDestination()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsm");
        try
        {
            File.WriteAllText(path, "original");
            Assert.Throws<NotSupportedException>(() => SheetDocumentSerializer.Save(new SheetModel(1, 1), path));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void CellStylingAndDimensionsSurviveEditing(string extension)
    {
        var font = new InlineStyle { Bold = true, Italic = true, FontSize = 14, Foreground = Broiler.Graphics.Color.BColor.Red };
        var borders = new CellBorders(new TableBorder(Broiler.Graphics.Color.BColor.Green, 1.5, TableBorderStyle.Solid), default, default, default);
        var shading = Broiler.Graphics.Color.BColor.Blue;
        var original = new SpreadsheetWorkbook([
            new SpreadsheetWorksheet("Styles", [
                new SpreadsheetRow(0, [
                    new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromNumber(100), numberFormat: "#,##0.00", shading: shading)
                    {
                        Font = font,
                        Borders = borders,
                        Alignment = TextAlignment.Right,
                        VerticalAlignment = CellVerticalAlignment.Bottom
                    }
                ], height: 35)
            ], columnWidths: [120])
        ]);

        using var input = new MemoryStream();
        Codec(extension).Write(original.ToRichTextDocument(), input);
        input.Position = 0;

        var sheet = SheetDocumentSerializer.Load(input, "styles" + extension);
        Assert.Equal("100", sheet[0, 0]);

        var loadedContent = sheet.GetContent(0, 0);
        Assert.Equal(shading, loadedContent.Shading);
        Assert.True(loadedContent.Font.Bold);
        Assert.Equal(TextAlignment.Right, loadedContent.Alignment);

        // Edit the value and resize dimensions
        sheet[0, 0] = "200";
        sheet.SetColumnWidth(0, 180);
        sheet.SetRowHeight(0, 45);

        using var output = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, output, "edited" + extension);
        output.Position = 0;

        var restored = Workbook(Codec(extension).Read(output));
        var restoredCell = restored.Sheets[0][0, 0]!;
        Assert.Equal(shading, restoredCell.Shading);
        Assert.True(restoredCell.Font.Bold);
        Assert.True(restoredCell.Font.Italic);
        Assert.Equal(14, restoredCell.Font.FontSize);
        Assert.Equal(Broiler.Graphics.Color.BColor.Red, restoredCell.Font.Foreground);
        Assert.Equal(TextAlignment.Right, restoredCell.Alignment);
        Assert.Equal(CellVerticalAlignment.Bottom, restoredCell.VerticalAlignment);
        Assert.Equal(1.5, restoredCell.Borders.Left.Width);
        Assert.Equal(45, restored.Sheets[0].Rows[0].Height);
        Assert.Equal(180, restored.Sheets[0].ColumnWidths[0]);
    }
}

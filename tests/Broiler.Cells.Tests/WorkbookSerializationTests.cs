using Broiler.Cells.Core;
using Broiler.Documents;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Ods;
using Broiler.Documents.Xlsx;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class WorkbookSerializationTests
{
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
            [new SpreadsheetCell(new CellAddress(0, 100), CellValue.FromText("outside"))])])]);
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
}

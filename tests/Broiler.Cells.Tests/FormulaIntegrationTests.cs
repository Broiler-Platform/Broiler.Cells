using Broiler.Cells.Core;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class FormulaIntegrationTests
{
    [Fact]
    public void DependenciesRecalculateChainsAndRetainFormulaSourceAndFormat()
    {
        var sheet = new SheetModel(3, 4);
        sheet.SetInput(0, 0, "2", CellEntryMode.Number);
        sheet.SetInput(1, 0, "3", CellEntryMode.Number);
        sheet.SetInput(0, 1, "=SUM(A1:A2)", CellEntryMode.Formula);
        sheet.SetInput(0, 2, "=B1*2", CellEntryMode.Formula);
        sheet.SetNumberFormat(0, 2, "0.00");
        Assert.Equal("10.00", sheet[0, 2]);
        Assert.Equal("=B1*2", sheet.GetEditText(0, 2));
        Assert.Contains(new CellAddress(0, 0), sheet.FormulaDependencies[new CellAddress(0, 1)]);
        sheet.SetInput(1, 0, "8", CellEntryMode.Number);
        Assert.Equal("20.00", sheet[0, 2]);
        sheet.SetInput(0, 1, "=A1", CellEntryMode.Formula);
        Assert.DoesNotContain(new CellAddress(1, 0), sheet.FormulaDependencies[new CellAddress(0, 1)]);
        Assert.Equal("4.00", sheet[0, 2]);
    }

    [Fact]
    public void CyclesErrorsAndUnsupportedFormulasAreReportedAndRecoverAfterAnEdit()
    {
        var sheet = new SheetModel(2, 4);
        sheet.SetInput(0, 0, "=B1", CellEntryMode.Formula);
        sheet.SetInput(0, 1, "=A1", CellEntryMode.Formula);
        Assert.True(sheet.GetContent(0, 0).Value.IsError);
        Assert.Contains(sheet.FormulaIssues, i => i.Error == FormulaError.Cycle);
        sheet.SetInput(0, 0, "4", CellEntryMode.Number);
        Assert.Equal("4", sheet[0, 1]);
        sheet.SetInput(0, 2, "=UNKNOWN(1)", CellEntryMode.Formula);
        Assert.Equal(FormulaError.Name, sheet.GetContent(0, 2).Value.Error);
        sheet.SetInput(0, 3, "=OtherSheet!A1", CellEntryMode.Formula);
        Assert.True(sheet.GetContent(0, 3).Value.IsError);
        Assert.Contains(sheet.FormulaIssues, i => i.Cell.Column == 3 && i.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
        sheet.SetInput(1, 0, "=1/0", CellEntryMode.Formula);
        Assert.Equal(FormulaError.Div0, sheet.GetContent(1, 0).Value.Error);
    }

    [Theory]
    [InlineData("=IF(A1>0,A1*2,1/0)", "8")]
    [InlineData("=AVERAGE(A1:A2)", "3")]
    [InlineData("=CONCAT(\"hello\",\" world\")", "hello world")]
    [InlineData("=ROUND(1.234,2)", "1.23")]
    [InlineData("=LEFT(\"test\",2)", "te")]
    [InlineData("=RIGHT(\"test\",2)", "st")]
    [InlineData("=MID(\"testing\",4,3)", "tin")]
    [InlineData("=YEAR(DATE(2026,5,20))", "2026")]
    [InlineData("=MONTH(DATE(2026,5,20))", "5")]
    [InlineData("=DAY(DATE(2026,5,20))", "20")]
    public void SupportedFunctionsUseTheDocumentsEvaluator(string formula, string expected)
    {
        var sheet = new SheetModel(2, 2);
        sheet.SetInput(0, 0, "4", CellEntryMode.Number);
        sheet.SetInput(1, 0, "2", CellEntryMode.Number);
        sheet.SetInput(0, 1, formula, CellEntryMode.Formula);
        Assert.Equal(expected, sheet[0, 1]);
    }

    [Fact]
    public void LookupFunctionsEvaluateInWorksheet()
    {
        var sheet = new SheetModel(5, 5);
        sheet.SetInput(0, 0, "10", CellEntryMode.Number);
        sheet.SetInput(0, 1, "Apples", CellEntryMode.Text);
        sheet.SetInput(1, 0, "20", CellEntryMode.Number);
        sheet.SetInput(1, 1, "Oranges", CellEntryMode.Text);

        sheet.SetInput(2, 0, "=VLOOKUP(20, A1:B2, 2, FALSE)", CellEntryMode.Formula);
        sheet.SetInput(2, 1, "=INDEX(A1:B2, 1, 2)", CellEntryMode.Formula);
        sheet.SetInput(2, 2, "=MATCH(20, A1:A2, 0)", CellEntryMode.Formula);
        sheet.SetInput(2, 3, "=COUNTIF(A1:A2, \">15\")", CellEntryMode.Formula);

        Assert.Equal("Oranges", sheet[2, 0]);
        Assert.Equal("Apples", sheet[2, 1]);
        Assert.Equal("2", sheet[2, 2]);
        Assert.Equal("1", sheet[2, 3]);
    }

    [Fact]
    public void LiteralFormulaTextIsNeverEvaluatedAndComplexFormulasAreBounded()
    {
        var sheet = new SheetModel(1, 3);
        sheet.SetInput(0, 0, "=1+1", CellEntryMode.Text);
        sheet.SetInput(0, 1, "=" + new string('(', 100) + "1" + new string(')', 100), CellEntryMode.Formula);
        Assert.Equal("=1+1", sheet[0, 0]);
        Assert.Equal(FormulaError.Num, sheet.GetContent(0, 1).Value.Error);
        Assert.Contains(sheet.FormulaIssues, i => i.Message.Contains("limits"));
    }

    [Fact]
    public void LongDependencyChainsAreRefusedBeforeRecursiveEvaluation()
    {
        var sheet = new SheetModel(10, 15);
        sheet.SetInput(0, 0, "1", CellEntryMode.Number);
        for (int i = 1; i <= 129; i++)
        {
            var previous = new CellAddress((i - 1) / 15, (i - 1) % 15);
            sheet.SetInput(i / 15, i % 15, "=" + previous, CellEntryMode.Formula);
        }
        Assert.Equal(FormulaError.Num, sheet.GetContent(8, 9).Value.Error);
        Assert.Contains(sheet.FormulaIssues, issue => issue.Message.Contains("limits"));
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void StaleImportedCachedValuesAreReplacedByTheEvaluator(string extension)
    {
        var workbook = new Broiler.Documents.Model.Spreadsheets.SpreadsheetWorkbook([
            new Broiler.Documents.Model.Spreadsheets.SpreadsheetWorksheet("Stale", [
                new Broiler.Documents.Model.Spreadsheets.SpreadsheetRow(0, [
                    new Broiler.Documents.Model.Spreadsheets.SpreadsheetCell(new CellAddress(0, 0), CellValue.FromNumber(4)),
                    new Broiler.Documents.Model.Spreadsheets.SpreadsheetCell(new CellAddress(0, 1), CellValue.FromNumber(999), formula: "=A1*2", cachedText: "999")])])]);
        Broiler.Documents.DocumentCodec codec = extension == ".xlsx" ? new Broiler.Documents.Xlsx.XlsxDocumentCodec() : new Broiler.Documents.Ods.OdsDocumentCodec();
        using var stream = new MemoryStream();
        codec.Write(workbook.ToRichTextDocument(), stream);
        stream.Position = 0;
        var imported = SheetDocumentSerializer.Load(stream, "stale" + extension);
        Assert.Equal("8", imported[0, 1]);
        Assert.Equal(8, imported.GetContent(0, 1).Value.Number);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void ExportedCachesAreCurrentAndImportedCachesAreNotTrusted(string extension)
    {
        var sheet = new SheetModel(2, 2);
        sheet.SetInput(0, 0, "5", CellEntryMode.Number);
        sheet.SetInput(0, 1, "=A1*3", CellEntryMode.Formula);
        Assert.Equal("15", sheet[0, 1]);
        sheet.SetInput(0, 0, "7", CellEntryMode.Number);
        using var output = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, output, "calculated" + extension);
        output.Position = 0;
        var restored = SheetDocumentSerializer.Load(output, "calculated" + extension);
        Assert.Equal("=A1*3", restored.GetEditText(0, 1));
        Assert.Equal("21", restored[0, 1]);
        restored.SetInput(0, 0, "8", CellEntryMode.Number);
        Assert.Equal("24", restored[0, 1]);
        Assert.Empty(restored.FormulaIssues);
    }

    [Fact]
    public void CrossSheetFormulasEvaluateAcrossWorkbookAndReactToChanges()
    {
        var sheet1 = new SheetModel(2, 2);
        var workbook = new WorkbookModel(sheet1, "Summary");
        var sheet2 = new SheetModel(2, 2);
        workbook.Add("Data", sheet2);

        sheet2.SetInput(0, 0, "15", CellEntryMode.Number);
        sheet1.SetInput(0, 0, "=Data!A1 * 2", CellEntryMode.Formula);

        Assert.Equal("30", sheet1[0, 0]);
        Assert.Equal(30d, sheet1.GetContent(0, 0).Value.Number);
        Assert.Empty(sheet1.FormulaIssues);

        // Edit referenced sheet and assert referencing sheet updates
        sheet2.SetInput(0, 0, "25", CellEntryMode.Number);
        Assert.Equal("50", sheet1[0, 0]);
        Assert.Equal(50d, sheet1.GetContent(0, 0).Value.Number);
    }

    [Fact]
    public void CrossSheetRangeAndQuotedNamesEvaluate()
    {
        var sheet1 = new SheetModel(2, 2);
        var workbook = new WorkbookModel(sheet1, "Totals");
        var sheet2 = new SheetModel(3, 1);
        workbook.Add("Q1 Sales", sheet2);

        sheet2.SetInput(0, 0, "100", CellEntryMode.Number);
        sheet2.SetInput(1, 0, "200", CellEntryMode.Number);
        sheet2.SetInput(2, 0, "300", CellEntryMode.Number);

        sheet1.SetInput(0, 0, "=SUM('Q1 Sales'!A1:A3)", CellEntryMode.Formula);

        Assert.Equal("600", sheet1[0, 0]);
        Assert.Equal(600d, sheet1.GetContent(0, 0).Value.Number);
        Assert.Empty(sheet1.FormulaIssues);
    }

    [Fact]
    public void LargeFormulaCountsExceeding512EvaluateAccurately()
    {
        const int count = 1200;
        var sheet = new SheetModel(count, 2);
        for (int i = 0; i < count; i++)
        {
            sheet.SetInput(i, 0, (i + 1).ToString(), CellEntryMode.Number);
            sheet.SetInput(i, 1, $"=A{i + 1} * 2", CellEntryMode.Formula);
        }

        Assert.Empty(sheet.FormulaIssues);
        Assert.Equal("2", sheet[0, 1]);
        Assert.Equal("1000", sheet[499, 1]);
        Assert.Equal("2400", sheet[count - 1, 1]);
        Assert.Equal(2400d, sheet.GetContent(count - 1, 1).Value.Number);
    }

    [Fact]
    public void FormulasInSparseDistantCellsEvaluateFastWithoutDenseAllocations()
    {
        var sheet = new SheetModel(50_000, 200);
        sheet.SetInput(0, 0, "42", CellEntryMode.Number);
        sheet.SetInput(49_999, 199, "=A1 * 3", CellEntryMode.Formula);

        Assert.Empty(sheet.FormulaIssues);
        Assert.Equal("126", sheet[49_999, 199]);
        Assert.Equal(126d, sheet.GetContent(49_999, 199).Value.Number);
    }
}

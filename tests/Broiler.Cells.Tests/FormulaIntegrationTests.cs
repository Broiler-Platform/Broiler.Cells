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
    public void SupportedFunctionsUseTheDocumentsEvaluator(string formula, string expected)
    {
        var sheet = new SheetModel(2, 2);
        sheet.SetInput(0, 0, "4", CellEntryMode.Number);
        sheet.SetInput(1, 0, "2", CellEntryMode.Number);
        sheet.SetInput(0, 1, formula, CellEntryMode.Formula);
        Assert.Equal(expected, sheet[0, 1]);
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
}

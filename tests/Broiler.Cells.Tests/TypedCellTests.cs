using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class TypedCellTests
{
    [Theory]
    [InlineData("12.5", CellEntryMode.Auto, CellValueType.Number)]
    [InlineData("TRUE", CellEntryMode.Auto, CellValueType.Boolean)]
    [InlineData("2026-10-30", CellEntryMode.Auto, CellValueType.Number)]
    [InlineData("#DIV/0!", CellEntryMode.Auto, CellValueType.Error)]
    [InlineData("00123", CellEntryMode.Auto, CellValueType.Text)]
    [InlineData("=1+1", CellEntryMode.Auto, CellValueType.Text)]
    [InlineData("123", CellEntryMode.Text, CellValueType.Text)]
    [InlineData("'00123", CellEntryMode.Auto, CellValueType.Text)]
    public void EntryModesProduceTypedValues(string input, CellEntryMode mode, CellValueType expected)
    {
        var sheet = new SheetModel(1, 1);
        sheet.SetInput(0, 0, input, mode);
        Assert.Equal(expected, sheet.GetContent(0, 0).Value.Type);
        Assert.Equal(input, sheet.GetEditText(0, 0));
        if (input.StartsWith('\'')) Assert.Equal("00123", sheet[0, 0]);
    }

    [Fact]
    public void FormattingRetainsRawNumberAndDateEdits()
    {
        var sheet = new SheetModel(2, 2);
        sheet.SetInput(0, 0, "1234.5", CellEntryMode.Number);
        sheet.SetNumberFormat(0, 0, "#,##0.00");
        Assert.Equal("1,234.50", sheet[0, 0]);
        Assert.Equal("1234.5", sheet.GetEditText(0, 0));
        Assert.Equal(1234.5, sheet.GetContent(0, 0).Value.Number);
        sheet.SetInput(1, 0, "1900-01-01", CellEntryMode.Date);
        Assert.Equal(1, sheet.GetContent(1, 0).Value.Number);
        Assert.Equal("1900-01-01", sheet[1, 0]);
        sheet.SetInput(0, 1, "0.125", CellEntryMode.Number);
        sheet.SetNumberFormat(0, 1, "0.0%");
        Assert.Equal("12.5%", sheet[0, 1]);
    }

    [Fact]
    public void ExplicitFormulaHasAnEvaluatedAnswerAndInvalidTypedInputIsAtomic()
    {
        var sheet = new SheetModel(1, 1);
        sheet.SetInput(0, 0, "=SUM(A2:A3)", CellEntryMode.Formula);
        Assert.Equal("=SUM(A2:A3)", sheet.GetContent(0, 0).Formula);
        Assert.NotNull(sheet.GetContent(0, 0).CachedText);
        var before = sheet.GetContent(0, 0);
        Assert.Throws<FormatException>(() => sheet.SetInput(0, 0, "not a number", CellEntryMode.Number));
        Assert.Equal(before, sheet.GetContent(0, 0));
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".ods")]
    public void TypedValuesAndFormatsRoundTripAcrossAllWorksheets(string extension)
    {
        var sheet = new SheetModel(2, 3);
        var workbook = new WorkbookModel(sheet);
        sheet.SetInput(0, 0, "12.5", CellEntryMode.Number);
        sheet.SetNumberFormat(0, 0, "0.00");
        sheet.SetInput(0, 1, "true", CellEntryMode.Boolean);
        sheet.SetInput(0, 2, "2026-10-30", CellEntryMode.Date);
        sheet.SetInput(1, 0, "=1+1", CellEntryMode.Text);
        sheet.SetInput(1, 1, "#DIV/0!", CellEntryMode.Error);
        sheet.SetInput(1, 2, "=A1*2", CellEntryMode.Formula);
        var second = workbook.Add(sheet: new SheetModel(1, 1));
        second.Sheet.SetInput(0, 0, "42", CellEntryMode.Number);
        using var output = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, output, "typed" + extension);
        output.Position = 0;
        var restored = SheetDocumentSerializer.Load(output, "typed" + extension);
        Assert.Equal(12.5, restored.GetContent(0, 0).Value.Number);
        Assert.Equal("0.00", restored.GetContent(0, 0).NumberFormat);
        Assert.True(restored.GetContent(0, 1).Value.Boolean);
        Assert.Equal(CellDateSerial.ToSerial(new DateTime(2026, 10, 30)), restored.GetContent(0, 2).Value.Number);
        Assert.Equal("=1+1", restored.GetContent(1, 0).Value.Text);
        Assert.Equal(FormulaError.Div0, restored.GetContent(1, 1).Value.Error);
        Assert.Equal("=A1*2", restored.GetContent(1, 2).Formula);
        Assert.Equal(42, restored.Workbook!.Sheets[1].Sheet.GetContent(0, 0).Value.Number);
    }

    [Fact]
    public void UnsupportedOdsCustomFormatDoesNotReplaceExistingFile()
    {
        var sheet = new SheetModel(1, 1);
        sheet.SetInput(0, 0, "12.345", CellEntryMode.Number);
        sheet.SetNumberFormat(0, 0, "0.###");
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ods");
        try
        {
            File.WriteAllText(path, "original");
            Assert.Throws<NotSupportedException>(() => SheetDocumentSerializer.Save(sheet, path));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EditLineShowsRawInputAndSwitchingModesDoesNotReinterpretUntouchedCells()
    {
        var sheet = new SheetModel(2, 2);
        sheet.SetInput(0, 0, "12.5", CellEntryMode.Number);
        sheet.SetNumberFormat(0, 0, "0.00");
        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));
        Assert.Equal("12.5", view.CellEditLine.Text);
        view.SetEntryMode(CellEntryMode.Boolean);
        view.AddWorksheet(); // A mode change alone must not turn 12.5 into a boolean.
        Assert.Equal(12.5, sheet.GetContent(0, 0).Value.Number);
        view.SelectWorksheet(0);
        view.SetEntryMode(CellEntryMode.Number);
        view.ApplySelectedInput("bad");
        Assert.NotNull(view.CellControl.Grid.LastInputError);
        Assert.Equal(12.5, sheet.GetContent(0, 0).Value.Number);
        view.ApplySelectedInput("18.75");
        Assert.Equal(18.75, sheet.GetContent(0, 0).Value.Number);
        view.ApplySelectedNumberFormat("0.0");
        Assert.Equal("18.8", sheet[0, 0]);
        Assert.Equal("18.75", view.CellEditLine.Text);
    }
}

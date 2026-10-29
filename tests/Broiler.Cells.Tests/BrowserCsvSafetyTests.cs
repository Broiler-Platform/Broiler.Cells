using System.Text;
using Broiler.Cells.Core;
using Xunit;

namespace Broiler.Cells.Tests;

public class BrowserCsvSafetyTests
{
    [Fact]
    public void StrictLoadRejectsRowsInsteadOfClipping()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", Enumerable.Repeat("value", 101))));
        Assert.Throws<InvalidDataException>(() => SheetCsvSerializer.Load(stream, rejectDataLoss: true));
    }

    [Fact]
    public void StrictLoadRejectsColumnsInsteadOfClipping()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join(",", Enumerable.Repeat("value", 101))));
        Assert.Throws<InvalidDataException>(() => SheetCsvSerializer.Load(stream, rejectDataLoss: true));
    }

    [Fact]
    public void StrictLoadPreservesUnicodeQuotesMultilineAndFormulaStrings()
    {
        var sheet = new SheetModel(2, 3);
        sheet[0,0] = "ÄΩ日本語"; sheet[0,1] = "comma, quote\""; sheet[0,2] = "line\nnext";
        sheet[1,0] = "=1+2"; sheet[1,1] = "+SUM(A1)";
        using var stream = new MemoryStream(); SheetCsvSerializer.Save(sheet, stream); stream.Position = 0;
        var copy = SheetCsvSerializer.Load(stream, rejectDataLoss: true);
        for (int r=0;r<2;r++) for (int c=0;c<3;c++) Assert.Equal(sheet[r,c],copy[r,c]);
    }

    [Fact]
    public void ChangeNotificationsOnlyFireForChangedValues()
    {
        var sheet = new SheetModel(1,1); int changes=0; sheet.Changed += (_,_) => changes++;
        sheet[0,0] = "x"; sheet[0,0] = "x"; sheet[0,0] = "";
        Assert.Equal(2, changes);
    }
}

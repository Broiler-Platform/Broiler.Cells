using Broiler.Cells.Core;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class SheetCsvSerializerTests
{
    [Fact]
    public void RoundTripSimpleSpreadsheet()
    {
        var original = new SheetModel(3, 2, (r, c) => (r, c) switch
        {
            (0, 0) => "Item",
            (0, 1) => "Quantity",
            (1, 0) => "Paper",
            (1, 1) => "12",
            (2, 0) => "Pens",
            (2, 1) => "4",
            _ => string.Empty,
        });

        string csv = SheetCsvSerializer.ToCsvString(original);
        Assert.Contains("Item,Quantity", csv);
        Assert.Contains("Paper,12", csv);
        Assert.Contains("Pens,4", csv);

        SheetModel loaded = SheetCsvSerializer.FromCsvString(csv);
        Assert.Equal(3, loaded.RowCount);
        Assert.Equal(2, loaded.ColumnCount);
        Assert.Equal("Item", loaded[0, 0]);
        Assert.Equal("Quantity", loaded[0, 1]);
        Assert.Equal("Paper", loaded[1, 0]);
        Assert.Equal("12", loaded[1, 1]);
        Assert.Equal("Pens", loaded[2, 0]);
        Assert.Equal("4", loaded[2, 1]);
    }

    [Fact]
    public void RoundTripSpecialCharactersAndQuotes()
    {
        var original = new SheetModel(2, 3);
        original[0, 0] = "Hello, world";
        original[0, 1] = "He said \"quoted\"";
        original[0, 2] = "Line 1\nLine 2";
        original[1, 0] = "=SUM(A1:B1)";
        original[1, 1] = "";
        original[1, 2] = "Simple";

        string csv = SheetCsvSerializer.ToCsvString(original);
        SheetModel loaded = SheetCsvSerializer.FromCsvString(csv);

        Assert.Equal(2, loaded.RowCount);
        Assert.Equal(3, loaded.ColumnCount);
        Assert.Equal("Hello, world", loaded[0, 0]);
        Assert.Equal("He said \"quoted\"", loaded[0, 1]);
        Assert.Equal("Line 1\nLine 2", loaded[0, 2]);
        Assert.Equal("=SUM(A1:B1)", loaded[1, 0]);
        Assert.Equal("", loaded[1, 1]);
        Assert.Equal("Simple", loaded[1, 2]);
    }

    [Fact]
    public void RoundTrip100x100Sheet()
    {
        var original = new SheetModel(100, 100);
        original[0, 0] = "First";
        original[99, 99] = "Last";
        original[50, 50] = "Middle";

        string csv = SheetCsvSerializer.ToCsvString(original);
        SheetModel loaded = SheetCsvSerializer.FromCsvString(csv);

        Assert.Equal(100, loaded.RowCount);
        Assert.Equal(100, loaded.ColumnCount);
        Assert.Equal("First", loaded[0, 0]);
        Assert.Equal("Last", loaded[99, 99]);
        Assert.Equal("Middle", loaded[50, 50]);
        Assert.Equal("", loaded[0, 1]);
    }

    [Fact]
    public void FileSaveAndLoadRoundTrip()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"broiler_cells_{Guid.NewGuid():N}.csv");
        try
        {
            var original = new SheetModel(2, 2);
            original[0, 0] = "A";
            original[0, 1] = "B";
            original[1, 0] = "C";
            original[1, 1] = "D";

            SheetCsvSerializer.Save(original, tempFile);
            Assert.True(File.Exists(tempFile));

            SheetModel loaded = SheetCsvSerializer.Load(tempFile);
            Assert.Equal("A", loaded[0, 0]);
            Assert.Equal("B", loaded[0, 1]);
            Assert.Equal("C", loaded[1, 0]);
            Assert.Equal("D", loaded[1, 1]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ClampsDimensionsToConfiguredLimits()
    {
        string csv = "A,B,C,D\n1,2,3,4\n5,6,7,8\n9,10,11,12";
        SheetModel loaded = SheetCsvSerializer.FromCsvString(csv, maxRows: 2, maxColumns: 2);
        Assert.Equal(2, loaded.RowCount);
        Assert.Equal(2, loaded.ColumnCount);
        Assert.Equal("A", loaded[0, 0]);
        Assert.Equal("B", loaded[0, 1]);
        Assert.Equal("1", loaded[1, 0]);
        Assert.Equal("2", loaded[1, 1]);
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SheetCsvSerializer.Save(null!, Stream.Null));
        Assert.Throws<ArgumentNullException>(() => SheetCsvSerializer.Save(new SheetModel(), (Stream)null!));
        Assert.Throws<ArgumentNullException>(() => SheetCsvSerializer.Load((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => SheetCsvSerializer.FromCsvString(null!));
    }
}

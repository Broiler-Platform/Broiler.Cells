using Broiler.Cells.Core;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class SheetModelTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(101, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    [InlineData(1, int.MaxValue)]
    public void InvalidDimensionsFailBeforeCallingInitializer(int rows, int columns)
    {
        bool called = false;
        Assert.Throws<ArgumentOutOfRangeException>(() => new SheetModel(rows, columns, (_, _) =>
        {
            called = true;
            return "Must not initialize";
        }));
        Assert.False(called);
    }

    [Theory]
    [InlineData(11, 1)]
    [InlineData(1, 11)]
    public void Version1ConfigurationEnforces10x10Limit(int rows, int columns)
    {
        bool called = false;
        Assert.Throws<ArgumentOutOfRangeException>(() => new SheetModel(
            rows,
            columns,
            (_, _) =>
            {
                called = true;
                return "Must not initialize";
            },
            maxRows: SheetModel.Version1MaximumRows,
            maxColumns: SheetModel.Version1MaximumColumns));
        Assert.False(called);
    }

    [Fact]
    public void ValuesAreCapturedOnceAndNullBecomesEmpty()
    {
        string value = "Unchanged";
        var sheet = new SheetModel(1, 2, (_, col) => col == 0 ? value : null);
        value = "Changed outside the model";
        Assert.Equal("Unchanged", sheet[0, 0]);
        Assert.Equal(string.Empty, sheet[0, 1]);
        Assert.Equal(string.Empty, new SheetModel()[9, 9]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 3)]
    public void ReadsOutsideTheActualDimensionsFail(int row, int column)
    {
        var sheet = new SheetModel(2, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet[row, column]);
    }

    [Fact]
    public void ValuesCanBeModifiedAndIndexedByPosition()
    {
        var sheet = new SheetModel(100, 100);
        sheet[0, 0] = "First";
        sheet[new CellPosition(99, 99)] = "Last";
        Assert.Equal("First", sheet[0, 0]);
        Assert.Equal("First", sheet[new CellPosition(0, 0)]);
        Assert.Equal("Last", sheet[99, 99]);
        Assert.Equal("Last", sheet[new CellPosition(99, 99)]);
        Assert.Equal(string.Empty, sheet[50, 50]);
    }
}

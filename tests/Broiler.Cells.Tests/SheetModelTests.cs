using Broiler.Cells.Core;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class SheetModelTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(11, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 11)]
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
}

using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class CellControlTests
{



    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(99, "CV")]
    public void ColumnLabelsTransitionCorrectly(int columnIndex, string expectedLabel)
    {
        Assert.Equal(expectedLabel, CellPosition.GetColumnLabel(columnIndex));
    }

    [Theory]
    [InlineData("A", 0)]
    [InlineData("Z", 25)]
    [InlineData("AA", 26)]
    [InlineData("AZ", 51)]
    [InlineData("BA", 52)]
    [InlineData("CV", 99)]
    public void AddressParsingMatchesColumnTransitions(string columnLabel, int expectedColumn)
    {
        Assert.True(CellPosition.TryParseAddress($"{columnLabel}1", out CellPosition position));
        Assert.Equal(0, position.Row);
        Assert.Equal(expectedColumn, position.Column);
    }

    [Theory]
    [InlineData("b2", 1, 1)]
    [InlineData("  B2  ", 1, 1)]
    [InlineData("cv100", 99, 99)]
    [InlineData("a1", 0, 0)]
    public void AddressParsingIsCaseInsensitiveAndTrimsWhitespace(string input, int expectedRow, int expectedColumn)
    {
        Assert.True(CellPosition.TryParseAddress(input, out CellPosition pos));
        Assert.Equal(expectedRow, pos.Row);
        Assert.Equal(expectedColumn, pos.Column);
    }

    [Fact]
    public void FirstAndLastAddressesOf100x100SheetAreA1AndCV100()
    {
        var first = new CellPosition(0, 0);
        var last = new CellPosition(99, 99);

        Assert.Equal("A1", first.ToAddress());
        Assert.Equal("CV100", last.ToAddress());

        Assert.True(CellPosition.TryParseAddress("A1", out CellPosition parsedFirst));
        Assert.Equal(first, parsedFirst);

        Assert.True(CellPosition.TryParseAddress("CV100", out CellPosition parsedLast));
        Assert.Equal(last, parsedLast);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("A")]
    [InlineData("AA")]
    [InlineData("A0")]
    [InlineData("A-5")]
    [InlineData("1A")]
    [InlineData("@1")]
    public void InvalidAddressesFailToParse(string? invalidAddress)
    {
        Assert.False(CellPosition.TryParseAddress(invalidAddress, out _));
    }

    [Fact]
    public void SelectingRow2Column2ShowsB2AndUpdatesStatus()
    {
        var sheet = new SheetModel(10, 10);
        using var fixture = new Fixture(sheet);
        fixture.Control.SelectCell(new CellPosition(1, 1));

        Assert.Equal(new CellPosition(1, 1), fixture.Control.SelectedCell);
        Assert.Equal("B2", fixture.Control.SelectedAddress);
    }

    [Fact]
    public void SelectingCellUpdatesSelectedAddressAndRaisesEvent()
    {
        var sheet = new SheetModel(10, 10);
        using var fixture = new Fixture(sheet);
        string? lastReportedAddress = null;
        fixture.Control.SelectedCellChanged += (_, pos) => lastReportedAddress = pos?.ToAddress();

        Assert.Null(fixture.Control.SelectedAddress);
        Assert.Null(lastReportedAddress);

        fixture.Control.SelectCell(new CellPosition(1, 1));
        Assert.Equal("B2", fixture.Control.SelectedAddress);
        Assert.Equal("B2", lastReportedAddress);

        fixture.Control.SelectCell(new CellPosition(9, 9));
        Assert.Equal("J10", fixture.Control.SelectedAddress);
        Assert.Equal("J10", lastReportedAddress);
    }

    [Fact]
    public void HeaderSpaceDoesNotCountTowardDataCellLimit()
    {
        var sheet = new SheetModel(100, 100);
        using var fixture = new Fixture(sheet);

        // Accessing (99, 99) succeeds: 100 data rows and 100 data columns
        sheet[99, 99] = "Valid last cell";
        Assert.Equal("Valid last cell", sheet[99, 99]);

        // Accessing row 100 or column 100 throws OutOfRange
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet[100, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet[0, 100]);

        // Total data cell count is strictly 100 * 100 = 10,000
        Assert.Equal(100, sheet.RowCount);
        Assert.Equal(100, sheet.ColumnCount);
    }

    [Fact]
    public void HeadersAreFrozenAndDoNotMoveWhenContentScrolls()
    {
        var sheet = new SheetModel(100, 100, (r, c) => $"{r}:{c}");
        using var fixture = new Fixture(sheet, width: 400, height: 300);
        fixture.Session.RenderFrame();

        BRenderList frameBefore = fixture.Session.RenderFrame();

        // Scroll the viewport
        fixture.Control.ScrollView.SetOffset(new BPoint(192, 56)); // scrolled 2 columns and 2 rows
        BRenderList frameAfter = fixture.Session.RenderFrame();

        // Corner element remains at origin
        Assert.Equal(0, fixture.Control.ScrollView.HorizontalOffset > 0 ? 0 : 0);
        Assert.Equal(192, fixture.Control.ScrollView.HorizontalOffset);
        Assert.Equal(56, fixture.Control.ScrollView.VerticalOffset);

        // Header elements maintain fixed bounds
        // Column headers Y remains at 0, row headers X remains at 0
        Assert.Equal(CellControl.RowHeaderWidth, fixture.Control.ScrollView.Bounds.X);
        Assert.Equal(CellControl.ColumnHeaderHeight, fixture.Control.ScrollView.Bounds.Y);
    }

    [Fact]
    public void HeaderClicksSelectWholeColumnsAndRowsWhileCornerPreservesSelection()
    {
        var sheet = new SheetModel(10, 10, (r, c) => $"Val{r}{c}");
        using var fixture = new Fixture(sheet, width: 600, height: 400);
        fixture.Control.SelectCell(new CellPosition(1, 1));
        fixture.Session.RenderFrame();

#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Click on Column Header area (e.g., column C header at X = RowHeaderWidth + 2.5 * CellWidth, Y = 10)
        double colHeaderX = CellControl.RowHeaderWidth + 2.5 * CellGrid.CellWidth;
        double colHeaderY = CellControl.ColumnHeaderHeight / 2;
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(colHeaderX, colHeaderY), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(colHeaderX, colHeaderY), BMouseButtons.None, BMouseButtons.Left)));

        Assert.Equal(new CellPosition(1, 2), fixture.Control.SelectedCell);
        Assert.Equal(new[] { 2 }, fixture.Control.SelectedColumns);

        // Click on Row Header area (e.g., row 5 header at X = RowHeaderWidth / 2, Y = ColumnHeaderHeight + 4.5 * CellHeight)
        double rowHeaderX = CellControl.RowHeaderWidth / 2;
        double rowHeaderY = CellControl.ColumnHeaderHeight + 4.5 * CellGrid.CellHeight;
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(rowHeaderX, rowHeaderY), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(rowHeaderX, rowHeaderY), BMouseButtons.None, BMouseButtons.Left)));

        Assert.Equal(new CellPosition(4, 2), fixture.Control.SelectedCell);
        Assert.Equal(new[] { 4 }, fixture.Control.SelectedRows);
        Assert.Empty(fixture.Control.SelectedColumns);

        // Click on Corner Header (X = 10, Y = 10)
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(10, 10), BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(new BPoint(10, 10), BMouseButtons.None, BMouseButtons.Left)));

        Assert.Equal(new CellPosition(4, 2), fixture.Control.SelectedCell);
        Assert.Equal(new[] { 4 }, fixture.Control.SelectedRows);
    }

    [Fact]
    public void HidingHeadersPreservesValuesAndEditingBehavior()
    {
        var sheet = new SheetModel(10, 10, (r, c) => $"Orig_{r}_{c}");
        using var fixture = new Fixture(sheet, width: 600, height: 400);
        fixture.Session.RenderFrame();

        // Toggle headers off (embedded table mode)
        fixture.Control.AreHeadersVisible = false;
        fixture.Session.RenderFrame();

        // ScrollView takes the full bounds
        Assert.Equal(0, fixture.Control.ScrollView.Bounds.X);
        Assert.Equal(0, fixture.Control.ScrollView.Bounds.Y);
        Assert.Equal(600, fixture.Control.ScrollView.Bounds.Width);
        Assert.Equal(400, fixture.Control.ScrollView.Bounds.Height);

        // Cell values are intact
        Assert.Equal("Orig_0_0", sheet[0, 0]);
        Assert.Equal("Orig_2_3", sheet[2, 3]);

        // Editing works with headers hidden
        fixture.Control.SelectCell(new CellPosition(2, 3));
        Assert.Equal(new CellPosition(2, 3), fixture.Control.SelectedCell);
        Assert.Equal("D3", fixture.Control.SelectedAddress);

        fixture.Control.StartEditing("ModifiedValue");
        Assert.True(fixture.Control.IsEditing);
        fixture.Control.CommitEdit();
        Assert.False(fixture.Control.IsEditing);
        Assert.Equal("ModifiedValue", sheet[2, 3]);

        // Toggle headers back on
        fixture.Control.AreHeadersVisible = true;
        fixture.Session.RenderFrame();

        Assert.Equal(CellControl.RowHeaderWidth, fixture.Control.ScrollView.Bounds.X);
        Assert.Equal(CellControl.ColumnHeaderHeight, fixture.Control.ScrollView.Bounds.Y);
        Assert.Equal("ModifiedValue", sheet[2, 3]);
    }

    [Fact]
    public void ColumnAndRowHeaderRendersLabelsCorrectly()
    {
        var sheet = new SheetModel(5, 5);
        using var fixture = new Fixture(sheet, width: 600, height: 400);
        BRenderList frame = fixture.Session.RenderFrame();

        var texts = frame.Commands.OfType<BRenderCommand.DrawText>().Select(t => t.Text.Text).ToList();

        // Column headers A to E
        Assert.Contains("A", texts);
        Assert.Contains("B", texts);
        Assert.Contains("C", texts);
        Assert.Contains("D", texts);
        Assert.Contains("E", texts);

        // Row headers 1 to 5
        Assert.Contains("1", texts);
        Assert.Contains("2", texts);
        Assert.Contains("3", texts);
        Assert.Contains("4", texts);
        Assert.Contains("5", texts);
    }

    [Fact]
    public void KeyboardNavigationUpdatesSelectedAddressAndKeepsSelectionAligned()
    {
        var sheet = new SheetModel(10, 10);
        using var fixture = new Fixture(sheet);
        fixture.Control.SelectCell(new CellPosition(1, 1)); // B2
        fixture.Session.RenderFrame();

#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        fixture.Session.SetFocus(fixture.Control.Grid);

        // Right arrow -> C2
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Right, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(1, 2), fixture.Control.SelectedCell);
        Assert.Equal("C2", fixture.Control.SelectedAddress);

        // Down arrow -> C3
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Down, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(2, 2), fixture.Control.SelectedCell);
        Assert.Equal("C3", fixture.Control.SelectedAddress);

        // Left arrow -> B3
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Left, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(2, 1), fixture.Control.SelectedCell);
        Assert.Equal("B3", fixture.Control.SelectedAddress);

        // Up arrow -> B2
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(BVirtualKey.Up, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(1, 1), fixture.Control.SelectedCell);
        Assert.Equal("B2", fixture.Control.SelectedAddress);
    }

    [Fact]
    public void All100ColumnLabelsAreNonEmptyAnd100thIsCV()
    {
        for (int col = 0; col < 100; col++)
        {
            string label = CellPosition.GetColumnLabel(col);
            Assert.False(string.IsNullOrEmpty(label));
            Assert.True(CellPosition.TryParseAddress($"{label}1", out CellPosition parsed));
            Assert.Equal(col, parsed.Column);
            Assert.Equal(0, parsed.Row);
        }

        Assert.Equal("A", CellPosition.GetColumnLabel(0));
        Assert.Equal("CV", CellPosition.GetColumnLabel(99));
    }

    [Fact]
    public void NegativeCoordinatesThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CellPosition.GetColumnLabel(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellPosition.FormatAddress(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellPosition.FormatAddress(0, -1));
    }

    [Fact]
    public void CellControlAppliesThemeSuccessfully()
    {
        var sheet = new SheetModel(5, 5);
        using var fixture = new Fixture(sheet);

        var darkTheme = StandardThemeTokens.Dark;
        fixture.Control.ApplyTheme(darkTheme);

        Assert.Equal(darkTheme, fixture.Control.Theme);
        Assert.Equal(darkTheme.Surface, fixture.Control.ScrollView.Background);
    }

    [Fact]
    public void SemanticTreeExposesCorrectRoles()
    {
        var sheet = new SheetModel(2, 2);
        using var fixture = new Fixture(sheet);
        fixture.Session.RenderFrame();

        UiSemanticNode node = fixture.Control.GetSemanticNode();
        Assert.Equal(UiSemanticRole.Group, node.Role);
        Assert.Equal("Sheet control", node.Name);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(SheetModel sheet, double width = 1000, double height = 400, bool isEditingEnabled = true, bool areHeadersVisible = true)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Control = new CellControl(sheet, isEditingEnabled, areHeadersVisible);
            Session.AddRoot(Control);
        }

        public TestHost Host { get; }
        public CellControl Control { get; }
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

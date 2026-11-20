using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class AutoFillTests
{
    [Fact]
    public void ShiftFormulaReferences_TranslatesRelativeReferencesCorrectly()
    {
        // Simple relative reference shift down
        Assert.Equal("=A2+B2", AutoFillEngine.ShiftFormulaReferences("=A1+B1", 1, 0));
        Assert.Equal("=A3+B3", AutoFillEngine.ShiftFormulaReferences("=A1+B1", 2, 0));

        // Shift right
        Assert.Equal("=B1+C1", AutoFillEngine.ShiftFormulaReferences("=A1+B1", 0, 1));
        Assert.Equal("=C1+D1", AutoFillEngine.ShiftFormulaReferences("=A1+B1", 0, 2));

        // Absolute references ($A$1 should never shift)
        Assert.Equal("=$A$1+A3", AutoFillEngine.ShiftFormulaReferences("=$A$1+A2", 1, 0));
        Assert.Equal("=$A$1+B2", AutoFillEngine.ShiftFormulaReferences("=$A$1+A2", 0, 1));

        // Mixed references: A$1 (fixed row), $B2 (fixed col)
        Assert.Equal("=A$1+$B3", AutoFillEngine.ShiftFormulaReferences("=A$1+$B2", 1, 0));
        Assert.Equal("=B$1+$B2", AutoFillEngine.ShiftFormulaReferences("=A$1+$B2", 0, 1));

        // Ranges inside function calls
        Assert.Equal("=SUM(A2:B6)", AutoFillEngine.ShiftFormulaReferences("=SUM(A1:B5)", 1, 0));
        Assert.Equal("=SUM(B1:C5)", AutoFillEngine.ShiftFormulaReferences("=SUM(A1:B5)", 0, 1));

        // Quoted string literal preserved
        Assert.Equal("=\"Total: \" & A2", AutoFillEngine.ShiftFormulaReferences("=\"Total: \" & A1", 1, 0));

        // Sheet prefix preserved
        Assert.Equal("=Sheet1!A2", AutoFillEngine.ShiftFormulaReferences("=Sheet1!A1", 1, 0));

        // Out of bounds shift produces #REF!
        Assert.Equal("=#REF!", AutoFillEngine.ShiftFormulaReferences("=A1", -1, 0));
        Assert.Equal("=#REF!", AutoFillEngine.ShiftFormulaReferences("=A1", 0, -1));
    }

    [Fact]
    public void AutoFill_LinearNumericSeries_GeneratesCorrectValues()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "1", CellEntryMode.Number);
        sheet.SetInput(1, 0, "2", CellEntryMode.Number);

        var src = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(1, 0));
        var dst = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 0));

        bool filled = AutoFillEngine.Fill(sheet, src, dst);
        Assert.True(filled);

        Assert.Equal("1", sheet[0, 0]);
        Assert.Equal("2", sheet[1, 0]);
        Assert.Equal("3", sheet[2, 0]);
        Assert.Equal("4", sheet[3, 0]);
        Assert.Equal("5", sheet[4, 0]);
    }

    [Fact]
    public void AutoFill_StepAndDecreasingSeries_GeneratesCorrectValues()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "10", CellEntryMode.Number);
        sheet.SetInput(1, 0, "20", CellEntryMode.Number);

        var src = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(1, 0));
        var dst = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(3, 0));

        Assert.True(AutoFillEngine.Fill(sheet, src, dst));
        Assert.Equal("30", sheet[2, 0]);
        Assert.Equal("40", sheet[3, 0]);

        // Decreasing series
        sheet.SetInput(0, 1, "10", CellEntryMode.Number);
        sheet.SetInput(1, 1, "8", CellEntryMode.Number);

        var srcDec = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(1, 1));
        var dstDec = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(3, 1));

        Assert.True(AutoFillEngine.Fill(sheet, srcDec, dstDec));
        Assert.Equal("6", sheet[2, 1]);
        Assert.Equal("4", sheet[3, 1]);
    }

    [Fact]
    public void AutoFill_HorizontalNumericSeries_GeneratesCorrectValues()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "5", CellEntryMode.Number);
        sheet.SetInput(0, 1, "10", CellEntryMode.Number);

        var src = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 1));
        var dst = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 4));

        Assert.True(AutoFillEngine.Fill(sheet, src, dst));
        Assert.Equal("15", sheet[0, 2]);
        Assert.Equal("20", sheet[0, 3]);
        Assert.Equal("25", sheet[0, 4]);
    }

    [Fact]
    public void AutoFill_SingleNumber_DefaultCopies_SeriesIncrements()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "42", CellEntryMode.Number);

        var src = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0));
        var dst = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));

        // Default mode on single number duplicates in Excel
        Assert.True(AutoFillEngine.Fill(sheet, src, dst, AutoFillMode.Default));
        Assert.Equal("42", sheet[1, 0]);
        Assert.Equal("42", sheet[2, 0]);

        // FillSeries mode increments
        sheet.SetInput(0, 1, "100", CellEntryMode.Number);
        var src2 = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(0, 1));
        var dst2 = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(2, 1));

        Assert.True(AutoFillEngine.Fill(sheet, src2, dst2, AutoFillMode.FillSeries));
        Assert.Equal("101", sheet[1, 1]);
        Assert.Equal("102", sheet[2, 1]);
    }

    [Fact]
    public void AutoFill_AlphanumericPattern_TrailingDigitsIncrement()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "Item 1", CellEntryMode.Auto);
        sheet.SetInput(0, 1, "Q1", CellEntryMode.Auto);
        sheet.SetInput(0, 2, "001", CellEntryMode.Auto);

        // Fill Down Item 1
        var srcItem = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0));
        var dstItem = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));
        Assert.True(AutoFillEngine.Fill(sheet, srcItem, dstItem));
        Assert.Equal("Item 2", sheet[1, 0]);
        Assert.Equal("Item 3", sheet[2, 0]);

        // Fill Down Q1
        var srcQ = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(0, 1));
        var dstQ = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(3, 1));
        Assert.True(AutoFillEngine.Fill(sheet, srcQ, dstQ));
        Assert.Equal("Q2", sheet[1, 1]);
        Assert.Equal("Q3", sheet[2, 1]);
        Assert.Equal("Q4", sheet[3, 1]);

        // Zero-padding preserved: 001 -> 002, 003
        var srcPad = new CellRangeAddress(new CellAddress(0, 2), new CellAddress(0, 2));
        var dstPad = new CellRangeAddress(new CellAddress(0, 2), new CellAddress(2, 2));
        Assert.True(AutoFillEngine.Fill(sheet, srcPad, dstPad));
        Assert.Equal("002", sheet[1, 2]);
        Assert.Equal("003", sheet[2, 2]);
    }

    [Fact]
    public void AutoFill_DayOfWeekSeries_CyclesCorrectly()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "Monday", CellEntryMode.Text);
        sheet.SetInput(0, 1, "Mon", CellEntryMode.Text);
        sheet.SetInput(0, 2, "SUNDAY", CellEntryMode.Text);

        // Full names
        var src1 = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0));
        var dst1 = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));
        Assert.True(AutoFillEngine.Fill(sheet, src1, dst1));
        Assert.Equal("Tuesday", sheet[1, 0]);
        Assert.Equal("Wednesday", sheet[2, 0]);

        // Abbreviated
        var src2 = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(0, 1));
        var dst2 = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(2, 1));
        Assert.True(AutoFillEngine.Fill(sheet, src2, dst2));
        Assert.Equal("Tue", sheet[1, 1]);
        Assert.Equal("Wed", sheet[2, 1]);

        // Cyclic wrap from Sunday with uppercase casing
        var src3 = new CellRangeAddress(new CellAddress(0, 2), new CellAddress(0, 2));
        var dst3 = new CellRangeAddress(new CellAddress(0, 2), new CellAddress(2, 2));
        Assert.True(AutoFillEngine.Fill(sheet, src3, dst3));
        Assert.Equal("MONDAY", sheet[1, 2]);
        Assert.Equal("TUESDAY", sheet[2, 2]);
    }

    [Fact]
    public void AutoFill_MonthOfYearSeries_CyclesCorrectly()
    {
        var sheet = new SheetModel(15, 10);
        sheet.SetInput(0, 0, "January", CellEntryMode.Text);
        sheet.SetInput(0, 1, "Dec", CellEntryMode.Text);

        var src1 = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0));
        var dst1 = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));
        Assert.True(AutoFillEngine.Fill(sheet, src1, dst1));
        Assert.Equal("February", sheet[1, 0]);
        Assert.Equal("March", sheet[2, 0]);

        // Cyclic wrap from Dec -> Jan
        var src2 = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(0, 1));
        var dst2 = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(2, 1));
        Assert.True(AutoFillEngine.Fill(sheet, src2, dst2));
        Assert.Equal("Jan", sheet[1, 1]);
        Assert.Equal("Feb", sheet[2, 1]);
    }

    [Fact]
    public void AutoFill_DateSeries_IncrementsDays()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "2026-01-01", CellEntryMode.Date);

        var src = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0));
        var dst = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));

        Assert.True(AutoFillEngine.Fill(sheet, src, dst));
        Assert.Equal("2026-01-02", sheet[1, 0]);
        Assert.Equal("2026-01-03", sheet[2, 0]);
    }

    [Fact]
    public void AutoFill_FormulaRelativeShifting_InGrid()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "10", CellEntryMode.Number);
        sheet.SetInput(1, 0, "20", CellEntryMode.Number);
        sheet.SetInput(2, 0, "30", CellEntryMode.Number);

        sheet.SetInput(0, 1, "=A1*2", CellEntryMode.Formula);

        var src = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(0, 1));
        var dst = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(2, 1));

        Assert.True(AutoFillEngine.Fill(sheet, src, dst));

        Assert.Equal("=A1*2", sheet.GetEditText(0, 1));
        Assert.Equal("=A2*2", sheet.GetEditText(1, 1));
        Assert.Equal("=A3*2", sheet.GetEditText(2, 1));

        Assert.Equal("20", sheet[0, 1]);
        Assert.Equal("40", sheet[1, 1]);
        Assert.Equal("60", sheet[2, 1]);
    }

    [Fact]
    public void AutoFill_FormattingPreservation_And_FormattingOnly()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "Alpha", CellEntryMode.Text);
        sheet.SetCellShading(0, 0, new BColor(200, 220, 240));
        sheet.SetCellFont(0, 0, new InlineStyle { Bold = true });
        sheet.SetNumberFormat(0, 0, "@");

        // Target already has data
        sheet.SetInput(1, 0, "Beta", CellEntryMode.Text);
        sheet.SetInput(2, 0, "Gamma", CellEntryMode.Text);

        var src = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0));
        var dst = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));

        // FillFormattingOnly preserves text, copies formatting
        Assert.True(AutoFillEngine.Fill(sheet, src, dst, AutoFillMode.FillFormattingOnly));

        Assert.Equal("Beta", sheet[1, 0]);
        Assert.Equal("Gamma", sheet[2, 0]);

        var c1 = sheet.GetContent(1, 0);
        Assert.Equal(new BColor(200, 220, 240), c1.Shading);
        Assert.True(c1.Font.Bold);
        Assert.Equal("@", c1.NumberFormat);
    }

    [Fact]
    public void AutoFill_DataValidation_PropagatesToTarget()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "Approved", CellEntryMode.Text);

        var src = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0));
        var dv = new DataValidation(
            Ranges: [src],
            Type: DataValidationType.List,
            Formula1: "Approved,Pending,Rejected");
        sheet.AddDataValidation(dv);

        var dst = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(3, 0));
        Assert.True(AutoFillEngine.Fill(sheet, src, dst));

        // Check that rows 1, 2, 3 have data validation rule
        Assert.NotNull(sheet.GetDataValidation(1, 0));
        Assert.NotNull(sheet.GetDataValidation(2, 0));
        Assert.NotNull(sheet.GetDataValidation(3, 0));
        Assert.Equal(DataValidationType.List, sheet.GetDataValidation(3, 0)?.Type);
    }

    [Fact]
    public void QuickFillDown_DetectsAdjacentData_FillsCorrectly()
    {
        var sheet = new SheetModel(10, 10);
        // Column 0 has 5 entries
        for (int r = 0; r < 5; r++)
            sheet.SetInput(r, 0, $"Data_{r}", CellEntryMode.Text);

        // Column 1 has 1 formula in row 0
        sheet.SetInput(0, 1, "Item 1", CellEntryMode.Auto);

        var src = new CellRangeAddress(new CellAddress(0, 1), new CellAddress(0, 1));
        var targetRange = AutoFillEngine.QuickFillDown(sheet, src);

        Assert.NotNull(targetRange);
        Assert.Equal(0, targetRange!.Value.StartRow);
        Assert.Equal(4, targetRange.Value.EndRow);

        Assert.Equal("Item 2", sheet[1, 1]);
        Assert.Equal("Item 3", sheet[2, 1]);
        Assert.Equal("Item 4", sheet[3, 1]);
        Assert.Equal("Item 5", sheet[4, 1]);
    }

    [Fact]
    public void CellGrid_FillHandle_HitTesting_And_HoverCursor()
    {
        var sheet = new SheetModel(10, 10);
        using var fixture = new Fixture(sheet);
        fixture.Session.RenderFrame();
        fixture.Grid.SelectCell(new CellPosition(0, 0));
        fixture.Session.RenderFrame();

        Assert.True(fixture.Grid.TryGetFillHandleBounds(out var handleRect));
        Assert.True(handleRect.Width > 0 && handleRect.Height > 0);

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Hover over handle center
        var center = new BPoint(handleRect.X + handleRect.Width / 2, handleRect.Y + handleRect.Height / 2);
        fixture.Session.DispatchInput(adapter.FromPointerMove(new BPointerEventArgs(center, BMouseButtons.None, BMouseButtons.None)));

        Assert.Equal(UiCursorShape.Move, fixture.Host.CurrentCursor);

        // Move away from handle
        fixture.Session.DispatchInput(adapter.FromPointerMove(new BPointerEventArgs(new BPoint(10, 10), BMouseButtons.None, BMouseButtons.None)));
        Assert.Equal(UiCursorShape.Arrow, fixture.Host.CurrentCursor);
    }

    [Fact]
    public void CellGrid_FillDrag_InteractiveGesture_ExecutesFill()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "10", CellEntryMode.Number);
        sheet.SetInput(1, 0, "20", CellEntryMode.Number);

        using var fixture = new Fixture(sheet);
        fixture.Session.RenderFrame();
        fixture.Grid.SelectCell(new CellPosition(0, 0));
        fixture.Grid.SelectCell(new CellPosition(1, 0), extend: true);
        fixture.Session.RenderFrame();

        Assert.True(fixture.Grid.TryGetFillHandleBounds(out var handleRect));
        var handleCenter = new BPoint(handleRect.X + handleRect.Width / 2, handleRect.Y + handleRect.Height / 2);

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        bool eventFired = false;
        fixture.Grid.AutoFillCompleted += (src, dst) => eventFired = true;

        // PointerDown on handle initiates drag
        fixture.Session.DispatchInput(adapter.FromPointerButton(new BPointerEventArgs(handleCenter, BMouseButtons.Left, BMouseButtons.Left)));
        Assert.True(fixture.Grid.IsFillDragging);

        // Drag down to row 3
        double row3Y = fixture.Grid.GetRowOffset(3) + 10;
        fixture.Session.DispatchInput(adapter.FromPointerMove(new BPointerEventArgs(new BPoint(handleCenter.X, row3Y), BMouseButtons.Left, BMouseButtons.None)));
        Assert.NotNull(fixture.Grid.FillTargetRange);
        Assert.Equal(3, fixture.Grid.FillTargetRange!.Value.EndRow);

        // PointerUp commits fill
        fixture.Session.DispatchInput(adapter.FromPointerButton(new BPointerEventArgs(new BPoint(handleCenter.X, row3Y), BMouseButtons.None, BMouseButtons.Left)));
        Assert.False(fixture.Grid.IsFillDragging);
        Assert.True(eventFired);

        Assert.Equal("30", sheet[2, 0]);
        Assert.Equal("40", sheet[3, 0]);
    }

    [Fact]
    public void KeyboardShortcuts_FillDown_And_FillRight()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "HeaderA", CellEntryMode.Text);
        sheet.SetInput(0, 1, "HeaderB", CellEntryMode.Text);

        using var fixture = new Fixture(sheet);

        // Select A1:B3
        fixture.Grid.SelectCell(new CellPosition(0, 0));
        fixture.Grid.SelectCell(new CellPosition(2, 1), extend: true);

        // Execute FillDown
        Assert.True(fixture.Grid.FillDown());

        Assert.Equal("HeaderA", sheet[1, 0]);
        Assert.Equal("HeaderA", sheet[2, 0]);
        Assert.Equal("HeaderB", sheet[1, 1]);
        Assert.Equal("HeaderB", sheet[2, 1]);

        // Select C1:E1 and FillRight
        sheet.SetInput(4, 0, "SourceVal", CellEntryMode.Text);
        fixture.Grid.SelectCell(new CellPosition(4, 0));
        fixture.Grid.SelectCell(new CellPosition(4, 2), extend: true);

        Assert.True(fixture.Grid.FillRight());
        Assert.Equal("SourceVal", sheet[4, 1]);
        Assert.Equal("SourceVal", sheet[4, 2]);
    }

    [Fact]
    public void CellsView_FillCommands_ExecuteCorrectly()
    {
        var sheet = new SheetModel(10, 10);
        sheet.SetInput(0, 0, "Seed", CellEntryMode.Text);

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));
        view.CellControl.SelectCell(new CellPosition(2, 0), extend: true);

        // Execute fill down command
        Assert.True(view.FillDown());
        Assert.Equal("Seed", sheet[1, 0]);
        Assert.Equal("Seed", sheet[2, 0]);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(SheetModel sheet, double width = 1000, double height = 400, bool isEditingEnabled = true)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Grid = new CellGrid(sheet, isEditingEnabled);
            Scroll = new StandardScrollView { FocusWhenScrollable = true };
            Scroll.AddChild(Grid);
            Session.AddRoot(Scroll);
        }

        public TestHost Host { get; }
        public CellGrid Grid { get; }
        public StandardScrollView Scroll { get; }
        public UiSession Session { get; }
        public void Dispose() => Session.Dispose();
    }

    private sealed class TestHost : IUiHost, IUiCursorHost
    {
        public BSize ViewportSize { get; set; }
        public double Scale => 1;
        public UiCursorShape CurrentCursor { get; private set; } = UiCursorShape.Arrow;
        public void SetCursor(UiCursorShape shape) => CurrentCursor = shape;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}

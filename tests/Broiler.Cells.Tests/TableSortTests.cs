using System;
using System.Collections.Generic;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics.Color;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class TableSortTests
{
    [Fact]
    public void QuickSort_NumericAscendingAndDescending_SortsCorrectly()
    {
        var sheet = new SheetModel();
        // Header
        sheet[0, 0] = "Item";
        sheet[0, 1] = "Score";

        // Rows
        sheet[1, 0] = "Alpha"; sheet[1, 1] = "30";
        sheet[2, 0] = "Beta";  sheet[2, 1] = "10";
        sheet[3, 0] = "Gamma"; sheet[3, 1] = "50";
        sheet[4, 0] = "Delta"; sheet[4, 1] = "20";

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 1));

        // Ascending by Score (Column 1)
        TableSortEngine.QuickSort(sheet, range, 1, ascending: true, hasHeaders: true);

        Assert.Equal("Item", sheet[0, 0]);
        Assert.Equal("Score", sheet[0, 1]);

        Assert.Equal("Beta", sheet[1, 0]);
        Assert.Equal("10", sheet[1, 1]);

        Assert.Equal("Delta", sheet[2, 0]);
        Assert.Equal("20", sheet[2, 1]);

        Assert.Equal("Alpha", sheet[3, 0]);
        Assert.Equal("30", sheet[3, 1]);

        Assert.Equal("Gamma", sheet[4, 0]);
        Assert.Equal("50", sheet[4, 1]);

        // Descending by Score (Column 1)
        TableSortEngine.QuickSort(sheet, range, 1, ascending: false, hasHeaders: true);

        Assert.Equal("Gamma", sheet[1, 0]);
        Assert.Equal("50", sheet[1, 1]);

        Assert.Equal("Alpha", sheet[2, 0]);
        Assert.Equal("30", sheet[2, 1]);

        Assert.Equal("Delta", sheet[3, 0]);
        Assert.Equal("20", sheet[3, 1]);

        Assert.Equal("Beta", sheet[4, 0]);
        Assert.Equal("10", sheet[4, 1]);
    }

    [Fact]
    public void QuickSort_TextAscendingAndDescending_SortsAlphabetically()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Name";
        sheet[1, 0] = "Orange";
        sheet[2, 0] = "Apple";
        sheet[3, 0] = "Banana";

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(3, 0));

        TableSortEngine.QuickSort(sheet, range, 0, ascending: true, hasHeaders: true);

        Assert.Equal("Name", sheet[0, 0]);
        Assert.Equal("Apple", sheet[1, 0]);
        Assert.Equal("Banana", sheet[2, 0]);
        Assert.Equal("Orange", sheet[3, 0]);

        TableSortEngine.QuickSort(sheet, range, 0, ascending: false, hasHeaders: true);

        Assert.Equal("Name", sheet[0, 0]);
        Assert.Equal("Orange", sheet[1, 0]);
        Assert.Equal("Banana", sheet[2, 0]);
        Assert.Equal("Apple", sheet[3, 0]);
    }

    [Fact]
    public void QuickSort_DateAscendingAndDescending_SortsChronologically()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "EventDate";
        sheet[1, 0] = "2026-11-15";
        sheet[2, 0] = "2024-01-01";
        sheet[3, 0] = "2025-06-30";

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(3, 0));

        TableSortEngine.QuickSort(sheet, range, 0, ascending: true, hasHeaders: true);

        Assert.Equal("2024-01-01", sheet[1, 0]);
        Assert.Equal("2025-06-30", sheet[2, 0]);
        Assert.Equal("2026-11-15", sheet[3, 0]);

        TableSortEngine.QuickSort(sheet, range, 0, ascending: false, hasHeaders: true);

        Assert.Equal("2026-11-15", sheet[1, 0]);
        Assert.Equal("2025-06-30", sheet[2, 0]);
        Assert.Equal("2024-01-01", sheet[3, 0]);
    }

    [Fact]
    public void QuickSort_BlanksSortedToBottomInBothDirections()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Values";
        sheet[1, 0] = "30";
        sheet[2, 0] = "";
        sheet[3, 0] = "10";
        sheet[4, 0] = "20";

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 0));

        // In ascending order, 10, 20, 30, with blank at the bottom
        TableSortEngine.QuickSort(sheet, range, 0, ascending: true, hasHeaders: true);
        Assert.Equal("10", sheet[1, 0]);
        Assert.Equal("20", sheet[2, 0]);
        Assert.Equal("30", sheet[3, 0]);
        Assert.Equal("", sheet[4, 0]);

        // In descending order, 30, 20, 10, with blank still at the bottom
        TableSortEngine.QuickSort(sheet, range, 0, ascending: false, hasHeaders: true);
        Assert.Equal("30", sheet[1, 0]);
        Assert.Equal("20", sheet[2, 0]);
        Assert.Equal("10", sheet[3, 0]);
        Assert.Equal("", sheet[4, 0]);
    }

    [Fact]
    public void MultiKeySort_PrimaryAndSecondaryKeys_SortsHierarchically()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Dept"; sheet[0, 1] = "Salary";
        sheet[1, 0] = "Sales"; sheet[1, 1] = "50000";
        sheet[2, 0] = "Eng";   sheet[2, 1] = "90000";
        sheet[3, 0] = "Sales"; sheet[3, 1] = "70000";
        sheet[4, 0] = "Eng";   sheet[4, 1] = "60000";

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 1));

        // Primary: Dept Ascending ("Eng" before "Sales")
        // Secondary: Salary Descending (Eng: 90000 then 60000; Sales: 70000 then 50000)
        var keys = new List<SortKey>
        {
            new(0, Ascending: true),
            new(1, Ascending: false),
        };

        TableSortEngine.SortRange(sheet, range, keys, hasHeaders: true);

        Assert.Equal("Eng", sheet[1, 0]);
        Assert.Equal("90000", sheet[1, 1]);

        Assert.Equal("Eng", sheet[2, 0]);
        Assert.Equal("60000", sheet[2, 1]);

        Assert.Equal("Sales", sheet[3, 0]);
        Assert.Equal("70000", sheet[3, 1]);

        Assert.Equal("Sales", sheet[4, 0]);
        Assert.Equal("50000", sheet[4, 1]);
    }

    [Fact]
    public void SortRange_WithoutHeaders_SortsAllRowsIncludingRowZero()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Zebra";
        sheet[1, 0] = "Bear";
        sheet[2, 0] = "Cat";

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));
        TableSortEngine.SortRange(sheet, range, [new SortKey(0, Ascending: true)], hasHeaders: false);

        Assert.Equal("Bear", sheet[0, 0]);
        Assert.Equal("Cat", sheet[1, 0]);
        Assert.Equal("Zebra", sheet[2, 0]);
    }

    [Fact]
    public void SortRange_PreservesAndMovesFormattingNotesAndValidations()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Num";
        sheet[1, 0] = "20";
        sheet[2, 0] = "10";

        // Row 1 (value 20) formatting: Red background and note
        var redColor = BColor.FromArgb(255, 255, 0, 0);
        sheet.SetCellShading(1, 0, redColor);
        sheet.SetCellNote(1, 0, new CellNote("Important value", "Alice"));

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0));
        TableSortEngine.QuickSort(sheet, range, 0, ascending: true, hasHeaders: true);

        // Row 1 should now be 10, Row 2 should now be 20
        Assert.Equal("10", sheet[1, 0]);
        Assert.Equal("20", sheet[2, 0]);

        // Shading and Note for "20" must now be at Row 2!
        Assert.Equal(redColor, sheet.GetContent(2, 0).Shading);
        Assert.NotEqual(redColor, sheet.GetContent(1, 0).Shading);

        var note2 = sheet.GetCellNote(2, 0);
        Assert.NotNull(note2);
        Assert.Equal("Important value", note2.Text);
        Assert.Null(sheet.GetCellNote(1, 0));
    }

    [Fact]
    public void SortRange_AdjustsIntraRowFormulaReferences()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Val"; sheet[0, 1] = "Double";
        sheet.SetInput(1, 0, "20", CellEntryMode.Number);
        sheet.SetInput(1, 1, "=A2*2", CellEntryMode.Formula);
        sheet.SetInput(2, 0, "10", CellEntryMode.Number);
        sheet.SetInput(2, 1, "=A3*2", CellEntryMode.Formula);

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 1));
        TableSortEngine.QuickSort(sheet, range, 0, ascending: true, hasHeaders: true);

        // After sorting:
        // Row 1 is "10", formula shifted from A3 to A2: "=A2*2"
        // Row 2 is "20", formula shifted from A2 to A3: "=A3*2"
        Assert.Equal("10", sheet[1, 0]);
        Assert.Equal("=A2*2", sheet.GetContent(1, 1).Formula);

        Assert.Equal("20", sheet[2, 0]);
        Assert.Equal("=A3*2", sheet.GetContent(2, 1).Formula);
    }

    [Fact]
    public void GetDataRegion_DetectsContiguousDataBlock()
    {
        var sheet = new SheetModel();
        // A block at B2:C4
        sheet[1, 1] = "Name";  sheet[1, 2] = "Age";
        sheet[2, 1] = "Alice"; sheet[2, 2] = "30";
        sheet[3, 1] = "Bob";   sheet[3, 2] = "40";

        // Querying inside the block (e.g. at B3)
        var region = sheet.GetDataRegion(new CellPosition(2, 1));

        Assert.Equal(1, region.StartRow);
        Assert.Equal(1, region.StartCol);
        Assert.Equal(3, region.EndRow);
        Assert.Equal(2, region.EndCol);
    }

    [Fact]
    public void CellControl_And_Grid_QuickSort_WiredCorrectly()
    {
        var sheet = new SheetModel();
        sheet[0, 0] = "Col";
        sheet[1, 0] = "3";
        sheet[2, 0] = "1";
        sheet[3, 0] = "2";

        var control = new CellControl(sheet);
        control.QuickSort(0, ascending: true);

        Assert.Equal("Col", sheet[0, 0]);
        Assert.Equal("1", sheet[1, 0]);
        Assert.Equal("2", sheet[2, 0]);
        Assert.Equal("3", sheet[3, 0]);
    }
}

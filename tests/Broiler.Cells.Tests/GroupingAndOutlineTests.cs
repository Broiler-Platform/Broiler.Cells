using System;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class GroupingAndOutlineTests
{
    [Fact]
    public void SheetModel_RowGrouping_IncrementsAndDecrementsOutlineLevels()
    {
        var sheet = new SheetModel(20, 10);
        Assert.Equal(0, sheet.MaxRowOutlineLevel);

        sheet.GroupRows(2, 5);
        Assert.Equal(1, sheet.MaxRowOutlineLevel);
        for (int r = 2; r <= 5; r++)
            Assert.Equal(1, sheet.GetRowOutlineLevel(r));
        Assert.Equal(0, sheet.GetRowOutlineLevel(1));
        Assert.Equal(0, sheet.GetRowOutlineLevel(6));

        // Nested group inside rows 3..4
        sheet.GroupRows(3, 4);
        Assert.Equal(2, sheet.MaxRowOutlineLevel);
        Assert.Equal(1, sheet.GetRowOutlineLevel(2));
        Assert.Equal(2, sheet.GetRowOutlineLevel(3));
        Assert.Equal(2, sheet.GetRowOutlineLevel(4));
        Assert.Equal(1, sheet.GetRowOutlineLevel(5));

        var groups = sheet.GetRowGroups();
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Start == 2 && g.End == 5 && g.Level == 1);
        Assert.Contains(groups, g => g.Start == 3 && g.End == 4 && g.Level == 2);

        // Ungroup nested
        sheet.UngroupRows(3, 4);
        Assert.Equal(1, sheet.MaxRowOutlineLevel);
        Assert.Equal(1, sheet.GetRowOutlineLevel(3));

        // Ungroup outer
        sheet.UngroupRows(2, 5);
        Assert.Equal(0, sheet.MaxRowOutlineLevel);
        Assert.Empty(sheet.GetRowGroups());
    }

    [Fact]
    public void SheetModel_ColumnGrouping_IncrementsAndDecrementsOutlineLevels()
    {
        var sheet = new SheetModel(10, 15);
        Assert.Equal(0, sheet.MaxColumnOutlineLevel);

        sheet.GroupColumns(1, 4);
        Assert.Equal(1, sheet.MaxColumnOutlineLevel);
        for (int c = 1; c <= 4; c++)
            Assert.Equal(1, sheet.GetColumnOutlineLevel(c));
        Assert.Equal(0, sheet.GetColumnOutlineLevel(0));
        Assert.Equal(0, sheet.GetColumnOutlineLevel(5));

        // Nested column group
        sheet.GroupColumns(2, 3);
        Assert.Equal(2, sheet.MaxColumnOutlineLevel);
        Assert.Equal(1, sheet.GetColumnOutlineLevel(1));
        Assert.Equal(2, sheet.GetColumnOutlineLevel(2));
        Assert.Equal(2, sheet.GetColumnOutlineLevel(3));
        Assert.Equal(1, sheet.GetColumnOutlineLevel(4));

        var groups = sheet.GetColumnGroups();
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Start == 1 && g.End == 4 && g.Level == 1);
        Assert.Contains(groups, g => g.Start == 2 && g.End == 3 && g.Level == 2);

        // Ungroup
        sheet.UngroupColumns(2, 3);
        Assert.Equal(1, sheet.MaxColumnOutlineLevel);
        sheet.UngroupColumns(1, 4);
        Assert.Equal(0, sheet.MaxColumnOutlineLevel);
        Assert.Empty(sheet.GetColumnGroups());
    }

    [Fact]
    public void SheetModel_RowGroupCollapsing_HidesRowsAndToggles()
    {
        var sheet = new SheetModel(20, 10);
        sheet.GroupRows(2, 5);

        Assert.False(sheet.IsRowGroupCollapsed(2, 5, 1));
        for (int r = 2; r <= 5; r++)
            Assert.False(sheet.IsRowHidden(r));

        // Collapse group
        sheet.SetRowGroupCollapsed(2, 5, 1, true);
        Assert.True(sheet.IsRowGroupCollapsed(2, 5, 1));
        for (int r = 2; r <= 5; r++)
            Assert.True(sheet.IsRowHidden(r));

        // Toggle back to expanded
        sheet.ToggleRowGroup(3, 1);
        Assert.False(sheet.IsRowGroupCollapsed(2, 5, 1));
        for (int r = 2; r <= 5; r++)
            Assert.False(sheet.IsRowHidden(r));

        // Toggle to collapsed
        sheet.ToggleRowGroup(3, 1);
        Assert.True(sheet.IsRowGroupCollapsed(2, 5, 1));
        for (int r = 2; r <= 5; r++)
            Assert.True(sheet.IsRowHidden(r));
    }

    [Fact]
    public void SheetModel_ColumnGroupCollapsing_HidesColumnsAndToggles()
    {
        var sheet = new SheetModel(10, 15);
        sheet.GroupColumns(3, 6);

        Assert.False(sheet.IsColumnGroupCollapsed(3, 6, 1));
        for (int c = 3; c <= 6; c++)
            Assert.False(sheet.IsColumnHidden(c));

        // Collapse column group
        sheet.SetColumnGroupCollapsed(3, 6, 1, true);
        Assert.True(sheet.IsColumnGroupCollapsed(3, 6, 1));
        for (int c = 3; c <= 6; c++)
            Assert.True(sheet.IsColumnHidden(c));

        // Toggle column group
        sheet.ToggleColumnGroup(4, 1);
        Assert.False(sheet.IsColumnGroupCollapsed(3, 6, 1));
        for (int c = 3; c <= 6; c++)
            Assert.False(sheet.IsColumnHidden(c));
    }

    [Fact]
    public void SheetModel_CollapseOutlineLevel_CollapsesAllGroupsAtOrAboveLevel()
    {
        var sheet = new SheetModel(30, 10);
        sheet.GroupRows(1, 4);
        sheet.GroupRows(6, 9);
        sheet.GroupRows(2, 3); // Level 2

        Assert.Equal(2, sheet.MaxRowOutlineLevel);

        // Collapse to level 1: level 1 groups are collapsed
        sheet.CollapseRowOutlineLevel(1);
        Assert.True(sheet.IsRowGroupCollapsed(1, 4, 1));
        Assert.True(sheet.IsRowGroupCollapsed(6, 9, 1));
        Assert.True(sheet.IsRowHidden(1));
        Assert.True(sheet.IsRowHidden(6));

        // Expand to level 2: level 1 groups expand, level 2 groups remain collapsed
        sheet.CollapseRowOutlineLevel(2);
        Assert.False(sheet.IsRowGroupCollapsed(1, 4, 1));
        Assert.False(sheet.IsRowGroupCollapsed(6, 9, 1));
        Assert.True(sheet.IsRowGroupCollapsed(2, 3, 2));

        // Expand all: level 3 (max + 1)
        sheet.CollapseRowOutlineLevel(3);
        Assert.False(sheet.IsRowGroupCollapsed(1, 4, 1));
        Assert.False(sheet.IsRowGroupCollapsed(6, 9, 1));
        Assert.False(sheet.IsRowGroupCollapsed(2, 3, 2));
    }

    [Fact]
    public void CellGrid_HiddenColumns_AffectsOffsetsAndWidth()
    {
        var sheet = new SheetModel(10, 10);
        var grid = new CellGrid(sheet);

        double w0 = grid.GetColumnWidth(0);
        double w1 = grid.GetColumnWidth(1);
        double offset2Before = grid.GetColumnOffset(2);

        Assert.True(w0 > 0);
        Assert.True(w1 > 0);
        Assert.Equal(w0 + w1, offset2Before);

        // Hide column 1
        sheet.SetColumnHidden(1, true);
        Assert.Equal(0, grid.GetColumnWidth(1));
        Assert.Equal(w0, grid.GetColumnOffset(2)); // Col 2 offset now immediately follows Col 0

        // Navigation skips hidden column
        grid.SelectCell(new CellPosition(0, 0));
        grid.Navigate(0, 1); // Move right
        Assert.Equal(new CellPosition(0, 2), grid.SelectedCell); // Skips hidden col 1!

        grid.Navigate(0, -1); // Move left
        Assert.Equal(new CellPosition(0, 0), grid.SelectedCell); // Skips hidden col 1 back to 0!

        // Unhide column 1
        sheet.SetColumnHidden(1, false);
        Assert.Equal(w1, grid.GetColumnWidth(1));
        Assert.Equal(offset2Before, grid.GetColumnOffset(2));
    }

    [Fact]
    public void CellControl_HeaderDimensions_AdjustDynamicallyWithOutlines()
    {
        var sheet = new SheetModel(20, 20);
        var control = new CellControl(sheet);

        Assert.Equal(CellControl.RowHeaderWidthBase, control.RowHeaderWidth);
        Assert.Equal(CellControl.ColumnHeaderHeightBase, control.ColumnHeaderHeight);

        sheet.GroupRows(1, 5);
        Assert.Equal(CellControl.RowHeaderWidthBase + 14.0, control.RowHeaderWidth);

        sheet.GroupRows(2, 4); // Nested row level 2
        Assert.Equal(CellControl.RowHeaderWidthBase + 28.0, control.RowHeaderWidth);

        sheet.GroupColumns(1, 3);
        Assert.Equal(CellControl.ColumnHeaderHeightBase + 14.0, control.ColumnHeaderHeight);

        sheet.ClearOutlines();
        Assert.Equal(CellControl.RowHeaderWidthBase, control.RowHeaderWidth);
        Assert.Equal(CellControl.ColumnHeaderHeightBase, control.ColumnHeaderHeight);
    }

    [Fact]
    public void CellControl_GroupingHelpers_OperateOnSelectedRange()
    {
        var sheet = new SheetModel(20, 20);
        var control = new CellControl(sheet);

        // Select rows 3..7
        control.SelectRow(3);
        control.SelectRow(7, extend: true);

        control.GroupSelectedRows();
        Assert.Equal(1, sheet.MaxRowOutlineLevel);
        Assert.Equal(1, sheet.GetRowOutlineLevel(4));

        control.HideSelectedDetail();
        Assert.True(sheet.IsRowGroupCollapsed(3, 7, 1));
        Assert.True(sheet.IsRowHidden(4));

        control.ShowSelectedDetail();
        Assert.False(sheet.IsRowGroupCollapsed(3, 7, 1));
        Assert.False(sheet.IsRowHidden(4));

        control.UngroupSelectedRows();
        Assert.Equal(0, sheet.MaxRowOutlineLevel);

        // Select columns 2..5
        control.SelectColumn(2);
        control.SelectColumn(5, extend: true);

        control.GroupSelectedColumns();
        Assert.Equal(1, sheet.MaxColumnOutlineLevel);
        Assert.Equal(1, sheet.GetColumnOutlineLevel(3));

        control.HideSelectedDetail();
        Assert.True(sheet.IsColumnGroupCollapsed(2, 5, 1));
        Assert.True(sheet.IsColumnHidden(3));

        control.ShowSelectedDetail();
        Assert.False(sheet.IsColumnGroupCollapsed(2, 5, 1));
        Assert.False(sheet.IsColumnHidden(3));

        control.UngroupSelectedColumns();
        Assert.Equal(0, sheet.MaxColumnOutlineLevel);
    }

    [Fact]
    public void CellsView_CommandsAndShortcuts_TriggerGroupingAndDetails()
    {
        var sheet = new SheetModel(20, 20);
        var view = new CellsView(sheet);

        // Select rows 2..4
        view.CellControl.SelectRow(2);
        view.CellControl.SelectRow(4, extend: true);

        // Execute data.group command
        Assert.True(view.CommandDispatcher.TryExecute("data.group"));
        Assert.Equal(1, sheet.MaxRowOutlineLevel);
        Assert.Equal(1, sheet.GetRowOutlineLevel(3));

        // Execute data.hide-detail
        Assert.True(view.CommandDispatcher.TryExecute("data.hide-detail"));
        Assert.True(sheet.IsRowGroupCollapsed(2, 4, 1));

        // Execute data.show-detail
        Assert.True(view.CommandDispatcher.TryExecute("data.show-detail"));
        Assert.False(sheet.IsRowGroupCollapsed(2, 4, 1));

        // Execute data.ungroup
        Assert.True(view.CommandDispatcher.TryExecute("data.ungroup"));
        Assert.Equal(0, sheet.MaxRowOutlineLevel);
    }

    [Fact]
    public void SheetDocumentSerializer_XlsxAndOds_RoundtripOutlinesAndHiddenState()
    {
        var sheet = new SheetModel(15, 12);
        sheet[0, 0] = "Header1";
        sheet[1, 0] = "Item1";
        sheet[2, 0] = "Item2";
        sheet[3, 0] = "Total";

        sheet.GroupRows(1, 2);
        sheet.SetRowGroupCollapsed(1, 2, 1, true);

        sheet.GroupColumns(1, 3);
        sheet.SetColumnGroupCollapsed(1, 3, 1, true);

        sheet.SummaryRowsBelow = true;
        sheet.SummaryColumnsRight = true;

        string xlsxPath = Path.Combine(Path.GetTempPath(), $"outline_test_{Guid.NewGuid():N}.xlsx");
        string odsPath = Path.Combine(Path.GetTempPath(), $"outline_test_{Guid.NewGuid():N}.ods");

        try
        {
            // XLSX Roundtrip
            SheetDocumentSerializer.Save(sheet, xlsxPath);
            var loadedXlsxSheet = SheetDocumentSerializer.Load(xlsxPath);

            Assert.Equal(1, loadedXlsxSheet.MaxRowOutlineLevel);
            Assert.Equal(1, loadedXlsxSheet.GetRowOutlineLevel(1));
            Assert.Equal(1, loadedXlsxSheet.GetRowOutlineLevel(2));
            Assert.True(loadedXlsxSheet.IsRowHidden(1));
            Assert.True(loadedXlsxSheet.IsRowHidden(2));
            Assert.True(loadedXlsxSheet.IsRowGroupCollapsed(1, 2, 1));

            Assert.Equal(1, loadedXlsxSheet.MaxColumnOutlineLevel);
            Assert.Equal(1, loadedXlsxSheet.GetColumnOutlineLevel(1));
            Assert.True(loadedXlsxSheet.IsColumnHidden(1));
            Assert.True(loadedXlsxSheet.IsColumnGroupCollapsed(1, 3, 1));
            Assert.True(loadedXlsxSheet.SummaryRowsBelow);
            Assert.True(loadedXlsxSheet.SummaryColumnsRight);

            // ODS Roundtrip
            SheetDocumentSerializer.Save(sheet, odsPath);
            var loadedOdsSheet = SheetDocumentSerializer.Load(odsPath);

            Assert.Equal(1, loadedOdsSheet.MaxRowOutlineLevel);
            Assert.Equal(1, loadedOdsSheet.GetRowOutlineLevel(1));
            Assert.Equal(1, loadedOdsSheet.GetRowOutlineLevel(2));
            Assert.True(loadedOdsSheet.IsRowHidden(1));
            Assert.True(loadedOdsSheet.IsRowHidden(2));
            Assert.True(loadedOdsSheet.IsRowGroupCollapsed(1, 2, 1));

            Assert.Equal(1, loadedOdsSheet.MaxColumnOutlineLevel);
            Assert.Equal(1, loadedOdsSheet.GetColumnOutlineLevel(1));
            Assert.True(loadedOdsSheet.IsColumnHidden(1));
            Assert.True(loadedOdsSheet.IsColumnGroupCollapsed(1, 3, 1));
            Assert.True(loadedOdsSheet.SummaryRowsBelow);
            Assert.True(loadedOdsSheet.SummaryColumnsRight);
        }
        finally
        {
            if (File.Exists(xlsxPath)) File.Delete(xlsxPath);
            if (File.Exists(odsPath)) File.Delete(odsPath);
        }
    }
}

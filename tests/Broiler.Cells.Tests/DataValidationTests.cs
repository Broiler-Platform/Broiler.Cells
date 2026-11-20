using System;
using System.Collections.Generic;
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
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class DataValidationTests
{
    [Fact]
    public void SheetModel_DataValidations_Management_And_Events()
    {
        var sheet = new SheetModel(10, 10);
        int changeCount = 0;
        sheet.DataValidationsChanged += () => changeCount++;

        var range = new CellRangeAddress(new CellAddress(1, 1), new CellAddress(5, 5));
        var dv = new DataValidation(
            Ranges: [range],
            Type: DataValidationType.List,
            Formula1: "Apple,Banana,Orange",
            AllowBlank: true,
            ShowDropdown: true);

        // Add
        sheet.AddDataValidation(dv);
        Assert.Single(sheet.DataValidations);
        Assert.Equal(1, changeCount);

        // Query
        Assert.NotNull(sheet.GetDataValidation(1, 1));
        Assert.NotNull(sheet.GetDataValidation(3, 3));
        Assert.Null(sheet.GetDataValidation(0, 0));
        Assert.Null(sheet.GetDataValidation(6, 6));

        // Clear range
        var clearRange = new CellRangeAddress(new CellAddress(2, 2), new CellAddress(2, 2));
        sheet.ClearDataValidations(clearRange);
        Assert.Empty(sheet.DataValidations);
        Assert.Equal(2, changeCount);

        // Re-add and remove
        sheet.AddDataValidation(dv);
        Assert.Equal(3, changeCount);
        bool removed = sheet.RemoveDataValidation(dv);
        Assert.True(removed);
        Assert.Empty(sheet.DataValidations);
        Assert.Equal(4, changeCount);

        // Clear all
        sheet.AddDataValidation(dv);
        Assert.Equal(5, changeCount);
        sheet.ClearDataValidations();
        Assert.Empty(sheet.DataValidations);
        Assert.Equal(6, changeCount);
    }

    [Fact]
    public void DataValidationEngine_List_LiteralValues()
    {
        var sheet = new SheetModel(10, 10);
        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(9, 9))],
            Type: DataValidationType.List,
            Formula1: "\"Apple,Banana,Cherry\"",
            AllowBlank: false);

        sheet.AddDataValidation(dv);

        var items = DataValidationEngine.GetValidationListItems(sheet, 0, 0);
        Assert.Equal(3, items.Count);
        Assert.Equal("Apple", items[0]);
        Assert.Equal("Banana", items[1]);
        Assert.Equal("Cherry", items[2]);

        // Exact match
        var res1 = DataValidationEngine.Validate(sheet, 0, 0, "Apple");
        Assert.True(res1.IsValid);

        // Case-insensitive match
        var res2 = DataValidationEngine.Validate(sheet, 0, 0, "banana");
        Assert.True(res2.IsValid);

        // Trimmed match
        var res3 = DataValidationEngine.Validate(sheet, 0, 0, " Cherry ");
        Assert.True(res3.IsValid);

        // Invalid item
        var res4 = DataValidationEngine.Validate(sheet, 0, 0, "Grape");
        Assert.False(res4.IsValid);
        Assert.Equal(DataValidationErrorStyle.Stop, res4.ErrorStyle);
    }

    [Fact]
    public void DataValidationEngine_List_RangeReference_SingleAndCrossSheet()
    {
        var sheet1 = new SheetModel(10, 10);
        var sheet2 = new SheetModel(10, 10);
        var wb = new WorkbookModel(sheet1, "Sheet1");
        wb.Add("Sheet2", sheet2);

        // Populate items in Sheet1 A1:A3
        sheet1[0, 0] = "North";
        sheet1[1, 0] = "South";
        sheet1[2, 0] = "East";

        // Populate items in Sheet2 B1:B2
        sheet2[0, 1] = "Active";
        sheet2[1, 1] = "Inactive";

        // Rule with single-sheet range "=A1:A3"
        var dv1 = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 5), new CellAddress(5, 5))],
            Type: DataValidationType.List,
            Formula1: "=A1:A3");
        sheet1.AddDataValidation(dv1);

        var items1 = DataValidationEngine.GetValidationListItems(sheet1, 0, 5);
        Assert.Equal(3, items1.Count);
        Assert.Equal("North", items1[0]);
        Assert.Equal("South", items1[1]);
        Assert.Equal("East", items1[2]);

        Assert.True(DataValidationEngine.Validate(sheet1, 0, 5, "North").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet1, 0, 5, "West").IsValid);

        // Rule with cross-sheet range "Sheet2!$B$1:$B$2"
        var dv2 = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 6), new CellAddress(5, 6))],
            Type: DataValidationType.List,
            Formula1: "Sheet2!$B$1:$B$2");
        sheet1.AddDataValidation(dv2);

        var items2 = DataValidationEngine.GetValidationListItems(sheet1, 0, 6);
        Assert.Equal(2, items2.Count);
        Assert.Equal("Active", items2[0]);
        Assert.Equal("Inactive", items2[1]);

        Assert.True(DataValidationEngine.Validate(sheet1, 0, 6, "Active").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet1, 0, 6, "inactive").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet1, 0, 6, "Pending").IsValid);
    }

    [Fact]
    public void DataValidationEngine_WholeNumber_Rules()
    {
        var sheet = new SheetModel(10, 10);

        // Between 10 and 100
        var dvBetween = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.WholeNumber,
            Operator: DataValidationOperator.Between,
            Formula1: "10",
            Formula2: "100");
        sheet.AddDataValidation(dvBetween);

        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "10").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "50").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "100").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "9").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "101").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "abc").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "12.34").IsValid);

        // Greater than 0
        var dvGt = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(1, 0), new CellAddress(1, 0))],
            Type: DataValidationType.WholeNumber,
            Operator: DataValidationOperator.GreaterThan,
            Formula1: "0");
        sheet.AddDataValidation(dvGt);

        Assert.True(DataValidationEngine.Validate(sheet, 1, 0, "5").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 1, 0, "0").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 1, 0, "-5").IsValid);
    }

    [Fact]
    public void DataValidationEngine_Decimal_Rules()
    {
        var sheet = new SheetModel(10, 10);
        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.Decimal,
            Operator: DataValidationOperator.Between,
            Formula1: "0.5",
            Formula2: "9.99");
        sheet.AddDataValidation(dv);

        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "0.5").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "5.25").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "9.99").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "0.49").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "10.0").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "not-a-number").IsValid);
    }

    [Fact]
    public void DataValidationEngine_Date_Rules()
    {
        var sheet = new SheetModel(10, 10);
        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.Date,
            Operator: DataValidationOperator.GreaterThanOrEqual,
            Formula1: "2026-01-01");
        sheet.AddDataValidation(dv);

        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "2026-01-01").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "2026-06-15").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "2025-12-31").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "invalid-date").IsValid);
    }

    [Fact]
    public void DataValidationEngine_TextLength_Rules()
    {
        var sheet = new SheetModel(10, 10);
        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.TextLength,
            Operator: DataValidationOperator.Between,
            Formula1: "3",
            Formula2: "6");
        sheet.AddDataValidation(dv);

        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "abc").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "hello").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "123456").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "ab").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 0, 0, "toolongtext").IsValid);
    }

    [Fact]
    public void DataValidationEngine_AllowBlank_Behavior()
    {
        var sheet = new SheetModel(10, 10);

        // AllowBlank = true
        var dvAllow = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.WholeNumber,
            Operator: DataValidationOperator.GreaterThan,
            Formula1: "10",
            AllowBlank: true);
        sheet.AddDataValidation(dvAllow);

        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "").IsValid);
        Assert.True(DataValidationEngine.Validate(sheet, 0, 0, "   ").IsValid);

        // AllowBlank = false
        var dvNoBlank = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(1, 0), new CellAddress(1, 0))],
            Type: DataValidationType.WholeNumber,
            Operator: DataValidationOperator.GreaterThan,
            Formula1: "10",
            AllowBlank: false);
        sheet.AddDataValidation(dvNoBlank);

        Assert.False(DataValidationEngine.Validate(sheet, 1, 0, "").IsValid);
        Assert.False(DataValidationEngine.Validate(sheet, 1, 0, "   ").IsValid);
    }

    [Fact]
    public void DataValidationEngine_ErrorAlertStyles()
    {
        var sheet = new SheetModel(10, 10);

        var dvWarn = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.WholeNumber,
            Operator: DataValidationOperator.Equal,
            Formula1: "42",
            ErrorTitle: "Custom Warning",
            ErrorMessage: "Are you sure this is correct?",
            ErrorStyle: DataValidationErrorStyle.Warning);

        var res = DataValidationEngine.Validate(sheet, 0, 0, "99", dvWarn);
        Assert.False(res.IsValid);
        Assert.Equal(DataValidationErrorStyle.Warning, res.ErrorStyle);
        Assert.Equal("Custom Warning", res.ErrorTitle);
        Assert.Equal("Are you sure this is correct?", res.ErrorMessage);
    }

    [Fact]
    public void SheetDocumentSerializer_DataValidation_Roundtrip()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Category";
        sheet[1, 0] = "Food";

        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(1, 0), new CellAddress(4, 0))],
            Type: DataValidationType.List,
            Formula1: "Food,Drink,Other",
            AllowBlank: false,
            ShowDropdown: true,
            ErrorTitle: "Invalid Category",
            ErrorMessage: "Please pick a valid category.",
            ErrorStyle: DataValidationErrorStyle.Stop);
        sheet.AddDataValidation(dv);

        // Roundtrip via XLSX stream
        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "test.xlsx");
        ms.Position = 0;

        var importedSheet = SheetDocumentSerializer.Load(ms, "test.xlsx");
        Assert.NotNull(importedSheet.DataValidations);
        Assert.Single(importedSheet.DataValidations);

        var importedDv = importedSheet.DataValidations[0];
        Assert.Equal(DataValidationType.List, importedDv.Type);
        Assert.Equal("Food,Drink,Other", importedDv.Formula1);
        Assert.False(importedDv.AllowBlank);
        Assert.True(importedDv.ShowDropdown);
        Assert.Equal("Invalid Category", importedDv.ErrorTitle);
        Assert.Equal("Please pick a valid category.", importedDv.ErrorMessage);
        Assert.Equal(DataValidationErrorStyle.Stop, importedDv.ErrorStyle);
    }

    [Fact]
    public void CellGrid_DropdownButton_Bounds_And_Input()
    {
        var sheet = new SheetModel(5, 5);
        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.List,
            Formula1: "A,B,C",
            ShowDropdown: true);
        sheet.AddDataValidation(dv);

        using var fixture = new GridFixture(sheet);
        fixture.Grid.SelectCell(new CellPosition(0, 0));
        fixture.Session.RenderFrame();

        var cellBounds = fixture.Grid.CellBounds(0, 0);
        var btnBounds = CellGrid.GetDropdownButtonBounds(cellBounds);

        Assert.True(btnBounds.Width <= 18);
        Assert.Equal(cellBounds.Right, btnBounds.Right);
        Assert.Equal(cellBounds.Y, btnBounds.Y);

        int reqCount = 0;
        fixture.Grid.OpenDropdownRequested += (r, c, d, b) =>
        {
            reqCount++;
            Assert.Equal(0, r);
            Assert.Equal(0, c);
            Assert.Equal(DataValidationType.List, d.Type);
        };

#pragma warning disable CS0618
        var input = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Click dropdown button
        var clickPoint = new BPoint(btnBounds.X + 2, btnBounds.Y + 2);
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(clickPoint, BMouseButtons.Left, BMouseButtons.Left)));
        fixture.Session.DispatchInput(input.FromPointerButton(new BPointerEventArgs(clickPoint, BMouseButtons.None, BMouseButtons.Left)));
        Assert.Equal(1, reqCount);

        // F4 keyboard shortcut (VK_F4 = 0x73)
        fixture.Session.DispatchInput(input.FromKey(new BKeyEventArgs(0x73, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(2, reqCount);

        // Alt+Down keyboard shortcut (VK_DOWN = 0x28, isAlt = true)
        var altDown = input.FromKey(new BKeyEventArgs(0x28, false, false, true), KeyboardKeyTransition.Down);
        fixture.Grid.DispatchInput(altDown);
        Assert.Equal(3, reqCount);
    }

    [Fact]
    public void CellGrid_CommitEdit_Rejects_Stop_On_ValidationFailure()
    {
        var sheet = new SheetModel(5, 5);
        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.WholeNumber,
            Operator: DataValidationOperator.GreaterThan,
            Formula1: "100",
            ErrorTitle: "Range Violation",
            ErrorMessage: "Number must exceed 100",
            ErrorStyle: DataValidationErrorStyle.Stop);
        sheet.AddDataValidation(dv);

        var grid = new CellGrid(sheet);
        grid.SelectCell(new CellPosition(0, 0));

        int failedCount = 0;
        ValidationResult lastRes = default;
        grid.ValidationFailed += (r, c, res) =>
        {
            failedCount++;
            lastRes = res;
        };

        // Attempt to set invalid value directly
        grid.SetCellValue(new CellPosition(0, 0), "50");
        Assert.Equal(1, failedCount);
        Assert.False(lastRes.IsValid);
        Assert.Equal("Range Violation", lastRes.ErrorTitle);
        Assert.Equal(string.Empty, sheet[0, 0]); // rejected, remains empty

        // Set valid value
        grid.SetCellValue(new CellPosition(0, 0), "150");
        Assert.Equal("150", sheet[0, 0]);
    }

    [Fact]
    public void ValidationPicklistPopup_Lifecycle_And_Selection()
    {
        var popup = new ValidationPicklistPopup();
        Assert.False(popup.IsPopupVisible);

        var items = new List<string> { "Low", "Medium", "High" };
        var anchor = new BRect(50, 50, 100, 24);

        popup.Show(2, 3, items, anchor, "Medium");
        Assert.True(popup.IsPopupVisible);
        Assert.Equal(3, popup.Items.Count);
        Assert.Equal(2, popup.TargetRow);
        Assert.Equal(3, popup.TargetCol);
        Assert.Equal(1, popup.SelectedIndex);

        string? selectedItem = null;
        popup.ItemSelected += (r, c, val) => selectedItem = val;

        // Select item
        popup.SelectItem(2); // "High"
        Assert.False(popup.IsPopupVisible);
        Assert.Equal("High", selectedItem);
    }

    [Fact]
    public void ValidationAlertCard_Show_And_Dismiss()
    {
        var card = new ValidationAlertCard();
        Assert.False(card.IsCardVisible);

        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            Type: DataValidationType.WholeNumber,
            ErrorTitle: "Invalid Quantity",
            ErrorMessage: "Quantity must be greater than zero",
            ErrorStyle: DataValidationErrorStyle.Warning);

        var fail = ValidationResult.Failure(dv);
        card.Show(fail);
        Assert.True(card.IsCardVisible);
        Assert.Equal("Invalid Quantity", card.LastResult?.ErrorTitle);

        bool dismissed = false;
        card.Dismissed += () => dismissed = true;

        card.Dismiss();
        Assert.False(card.IsCardVisible);
        Assert.True(dismissed);
    }

    [Fact]
    public void DataValidationPanel_Show_Configure_Apply_Clear()
    {
        var sheet = new SheetModel(10, 10);
        var panel = new DataValidationPanel();
        panel.SetSheet(sheet);
        Assert.False(panel.IsPanelVisible);

        var range = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(3, 3));
        panel.ShowForRange(range, sheet);
        Assert.True(panel.IsPanelVisible);
        Assert.Equal(range, panel.CurrentRange);

        // Configure Decimal GreaterThan 5.5
        panel.SelectType(DataValidationType.Decimal);
        Assert.Equal(DataValidationType.Decimal, panel.SelectedType);

        panel.SelectOperator(DataValidationOperator.GreaterThan);
        Assert.Equal(DataValidationOperator.GreaterThan, panel.SelectedOperator);

        panel.Val1Edit.Text = "5.5";
        panel.SelectAlertStyle(DataValidationErrorStyle.Warning);
        Assert.Equal(DataValidationErrorStyle.Warning, panel.SelectedErrorStyle);

        bool applied = false;
        panel.ValidationApplied += (_, _) => applied = true;

        panel.Apply();
        Assert.True(applied);
        Assert.False(panel.IsPanelVisible);

        // Verify rule was added to sheet
        var dv = sheet.GetDataValidation(0, 0);
        Assert.NotNull(dv);
        Assert.Equal(DataValidationType.Decimal, dv.Type);
        Assert.Equal(DataValidationOperator.GreaterThan, dv.Operator);
        Assert.Equal("5.5", dv.Formula1);
        Assert.Equal(DataValidationErrorStyle.Warning, dv.ErrorStyle);

        // Re-open and clear
        panel.ShowForRange(range, sheet, dv);
        Assert.True(panel.IsPanelVisible);
        Assert.Equal(DataValidationType.Decimal, panel.SelectedType);
        Assert.Equal("5.5", panel.Val1Edit.Text);

        bool cleared = false;
        panel.ValidationCleared += (_, _) => cleared = true;
        panel.Clear();
        Assert.True(cleared);
        Assert.False(panel.IsPanelVisible);
        Assert.Null(sheet.GetDataValidation(0, 0));
    }

    [Fact]
    public void CellsView_DataValidation_Commands_And_Picklists()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Item A";
        sheet[1, 0] = "Item B";

        var dv = new DataValidation(
            Ranges: [new CellRangeAddress(new CellAddress(0, 1), new CellAddress(4, 1))],
            Type: DataValidationType.List,
            Formula1: "Red,Green,Blue",
            ShowDropdown: true);
        sheet.AddDataValidation(dv);

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 1));

        // Command execution: open dialog
        Assert.True(view.CommandDispatcher.TryExecute("data.validation"));
        Assert.True(view.IsDataValidationDialogVisible);

        // Close dialog
        view.CloseDataValidation();
        Assert.False(view.IsDataValidationDialogVisible);

        // Trigger picklist
        view.ShowValidationPicklist(0, 1, dv, new BRect(100, 100, 20, 20));
        Assert.True(view.IsValidationPicklistVisible);
        Assert.Equal(3, view.ValidationPicklistPopup.Items.Count);

        // Pick an item
        view.ValidationPicklistPopup.SelectItem(1); // "Green"
        Assert.False(view.IsValidationPicklistVisible);
        Assert.Equal("Green", sheet[0, 1]);

        // Validation failure alert
        var failRes = new ValidationResult(false, DataValidationErrorStyle.Stop, "Err", "Bad Value");
        view.HandleValidationFailed(0, 1, failRes);
        Assert.True(view.IsValidationAlertVisible);
        Assert.Equal("Bad Value", view.LastValidationError);

        view.ValidationAlertCard.Dismiss();
        Assert.False(view.IsValidationAlertVisible);

        // Clear validation command
        Assert.True(view.CommandDispatcher.TryExecute("data.validation.clear"));
        Assert.Null(sheet.GetDataValidation(0, 1));
    }

    private sealed class GridFixture : IDisposable
    {
        public GridFixture(SheetModel sheet, double width = 500, double height = 500)
        {
            Host = new TestHost { ViewportSize = new BSize(width, height) };
            Session = new StandardUiSessionBuilder().Build(Host);
            Grid = new CellGrid(sheet);
            Scroll = new StandardScrollView { FocusWhenScrollable = true };
            Scroll.AddChild(Grid);
            Session.AddRoot(Scroll);
        }

        public TestHost Host { get; }
        public CellGrid Grid { get; }
        public StandardScrollView Scroll { get; }
        public UiSession Session { get; }
        public void Dispose() => Session.Dispose();

        public sealed class TestHost : IUiHost
        {
            public BSize ViewportSize { get; set; }
            public double Scale => 1;
            public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
            public void Invalidate(UiInvalidation invalidation) { }
            public void Present(BRenderList renderList) { }
        }
    }
}

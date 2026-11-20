using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Broiler.UI.Button.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class NumberFormatPresetsTests
{
    [Fact]
    public void CellFormatUtility_GetPresetName_IdentifiesStandardPresets()
    {
        Assert.Equal("General", CellFormatUtility.GetPresetName(null));
        Assert.Equal("General", CellFormatUtility.GetPresetName(""));
        Assert.Equal("General", CellFormatUtility.GetPresetName("General"));
        Assert.Equal("Number", CellFormatUtility.GetPresetName(CellFormatPresets.Integer));
        Assert.Equal("Decimal", CellFormatUtility.GetPresetName(CellFormatPresets.Decimal2));
        Assert.Equal("Currency", CellFormatUtility.GetPresetName(CellFormatPresets.Currency));
        Assert.Equal("Percentage", CellFormatUtility.GetPresetName(CellFormatPresets.Percentage));
        Assert.Equal("Short Date", CellFormatUtility.GetPresetName(CellFormatPresets.ShortDate));
        Assert.Equal("Long Date", CellFormatUtility.GetPresetName(CellFormatPresets.LongDate));
        Assert.Equal("Time", CellFormatUtility.GetPresetName(CellFormatPresets.Time));
        Assert.Equal("Date & Time", CellFormatUtility.GetPresetName(CellFormatPresets.DateTime));
        Assert.Equal("Scientific", CellFormatUtility.GetPresetName(CellFormatPresets.Scientific));
        Assert.Equal("Text", CellFormatUtility.GetPresetName("@"));
        Assert.Equal("Custom", CellFormatUtility.GetPresetName("[Red]0000"));
    }

    [Fact]
    public void CellFormatUtility_GetNextPreset_CyclesThroughAllPresets()
    {
        var current = CellFormatPresets.General;
        var visited = new System.Collections.Generic.List<string>();

        for (int i = 0; i < CellFormatUtility.Presets.Count; i++)
        {
            var next = CellFormatUtility.GetNextPreset(current);
            visited.Add(next.Name);
            current = next.Format;
        }

        Assert.Equal(CellFormatUtility.Presets.Count, visited.Count);
        // After cycling through all presets, we are back to General
        Assert.Equal(CellFormatPresets.General, current);
    }

    [Fact]
    public void CellFormatUtility_AdjustDecimals_IncreasesAndDecreasesPrecision()
    {
        // General / null
        Assert.Equal("#,##0.0", CellFormatUtility.AdjustDecimals(null, 1));
        Assert.Equal("#,##0.00", CellFormatUtility.AdjustDecimals(null, 2));
        Assert.Equal("#,##0", CellFormatUtility.AdjustDecimals(null, -1));

        // Decimal2 (#,##0.00)
        Assert.Equal("#,##0.000", CellFormatUtility.AdjustDecimals(CellFormatPresets.Decimal2, 1));
        Assert.Equal("#,##0.0", CellFormatUtility.AdjustDecimals(CellFormatPresets.Decimal2, -1));

        // Single decimal to integer (drops decimal dot)
        Assert.Equal("#,##0", CellFormatUtility.AdjustDecimals("#,##0.0", -1));

        // Percentage (0.0%)
        Assert.Equal("0.00%", CellFormatUtility.AdjustDecimals("0.0%", 1));
        Assert.Equal("0%", CellFormatUtility.AdjustDecimals("0.0%", -1));

        // Currency multi-section
        string currency = CellFormatPresets.Currency;
        string incCurrency = CellFormatUtility.AdjustDecimals(currency, 1);
        Assert.Contains("$#,##0.000", incCurrency);

        string decCurrency = CellFormatUtility.AdjustDecimals(currency, -1);
        Assert.Contains("$#,##0.0", decCurrency);
    }

    [Fact]
    public void SheetModel_GetAndSetNumberFormat_NormalizesGeneral()
    {
        var sheet = new SheetModel(3, 3);
        sheet.SetInput(0, 0, "1234.56", CellEntryMode.Number);

        sheet.SetNumberFormat(0, 0, CellFormatPresets.Currency);
        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("$1,234.56", sheet[0, 0]);

        // Setting "General" normalizes to null (default)
        sheet.SetNumberFormat(0, 0, "General");
        Assert.Null(sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("1234.56", sheet[0, 0]);
    }

    [Fact]
    public void CellContent_FormattedColor_AppliesToFormulasAndConstants()
    {
        var sheet = new SheetModel(3, 3);
        string colorFormat = "$#,##0.00;[Red]($#,##0.00);\"-\"";

        // Constant negative number
        sheet.SetInput(0, 0, "-50.5", CellEntryMode.Number);
        sheet.SetNumberFormat(0, 0, colorFormat);
        var content = sheet.GetContent(0, 0);
        Assert.Equal("($50.50)", content.Display);
        Assert.NotNull(content.FormattedColor);
        Assert.Equal(255, content.FormattedColor.Value.R);
        Assert.Equal(0, content.FormattedColor.Value.G);
        Assert.Equal(0, content.FormattedColor.Value.B);

        // Formula evaluated negative result
        sheet.SetInput(1, 0, "10", CellEntryMode.Number);
        sheet.SetInput(1, 1, "30", CellEntryMode.Number);
        sheet.SetInput(1, 2, "=A2-B2", CellEntryMode.Formula); // 10 - 30 = -20
        sheet.SetNumberFormat(1, 2, colorFormat);

        var formulaContent = sheet.GetContent(1, 2);
        Assert.Equal("($20.00)", formulaContent.Display);
        Assert.NotNull(formulaContent.FormattedColor);
        Assert.Equal(255, formulaContent.FormattedColor.Value.R);
    }

    [Fact]
    public void CellsView_ApplySelectedNumberFormat_AppliesAcrossMultiCellSelection()
    {
        var sheet = new SheetModel(4, 4);
        sheet.SetInput(0, 0, "1000", CellEntryMode.Number);
        sheet.SetInput(0, 1, "2000", CellEntryMode.Number);
        sheet.SetInput(1, 0, "3000", CellEntryMode.Number);
        sheet.SetInput(1, 1, "4000", CellEntryMode.Number);

        var view = new CellsView(sheet);
        // Select range (0,0) to (1,1)
        view.CellControl.SelectCell(new CellPosition(0, 0));
        view.CellControl.SelectCell(new CellPosition(1, 1), extend: true);

        view.ApplySelectedNumberFormat(CellFormatPresets.Currency);

        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(0, 1));
        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(1, 0));
        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(1, 1));

        Assert.Equal("$1,000.00", sheet[0, 0]);
        Assert.Equal("$2,000.00", sheet[0, 1]);
        Assert.Equal("$3,000.00", sheet[1, 0]);
        Assert.Equal("$4,000.00", sheet[1, 1]);
    }

    [Fact]
    public void CellsView_AdjustSelectedDecimals_UpdatesRangePrecision()
    {
        var sheet = new SheetModel(3, 3);
        sheet.SetInput(0, 0, "123.456", CellEntryMode.Number);
        sheet.SetInput(0, 1, "789.123", CellEntryMode.Number);
        sheet.SetNumberFormat(0, 0, "#,##0.00");
        sheet.SetNumberFormat(0, 1, "#,##0.00");

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));
        view.CellControl.SelectCell(new CellPosition(0, 1), extend: true);

        // Increase decimals (+1 -> 3 decimals)
        view.AdjustSelectedDecimals(1);
        Assert.Equal("#,##0.000", sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("#,##0.000", sheet.GetCellNumberFormat(0, 1));
        Assert.Equal("123.456", sheet[0, 0]);

        // Decrease decimals (-2 -> 1 decimal)
        view.AdjustSelectedDecimals(-1);
        view.AdjustSelectedDecimals(-1);
        Assert.Equal("#,##0.0", sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("123.5", sheet[0, 0]);
    }

    [Fact]
    public void CellsView_CycleFormatPreset_CyclesThroughPresets()
    {
        var sheet = new SheetModel(3, 3);
        sheet.SetInput(0, 0, "0.75", CellEntryMode.Number);

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));

        // Initially General
        Assert.Null(sheet.GetCellNumberFormat(0, 0));

        // Cycle once -> Number (#,##0)
        view.CycleFormatPreset();
        Assert.Equal(CellFormatPresets.Integer, sheet.GetCellNumberFormat(0, 0));

        // Cycle again -> Decimal (#,##0.00)
        view.CycleFormatPreset();
        Assert.Equal(CellFormatPresets.Decimal2, sheet.GetCellNumberFormat(0, 0));

        // Cycle again -> Currency
        view.CycleFormatPreset();
        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(0, 0));
    }

    [Fact]
    public void CellsView_ToolbarContainsNumberFormatButtons()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "$");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == "%");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == ",");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == ".0+");
        Assert.Contains(view.Toolbar.Children, c => c is StandardButton btn && btn.Text == ".0-");
    }

    [Fact]
    public void CellsView_ToolbarButtons_ExecuteFormatting()
    {
        var sheet = new SheetModel(3, 3);
        sheet.SetInput(0, 0, "1500", CellEntryMode.Number);
        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));

        // Currency button
        var currencyBtn = view.Toolbar.Children.OfType<StandardButton>().First(b => b.Text == "$");
        currencyBtn.Click();
        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("$1,500.00", sheet[0, 0]);

        // Percentage button
        var percentBtn = view.Toolbar.Children.OfType<StandardButton>().First(b => b.Text == "%");
        percentBtn.Click();
        Assert.Equal(CellFormatPresets.Percentage, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("150000.0%", sheet[0, 0]);

        // Comma button
        var commaBtn = view.Toolbar.Children.OfType<StandardButton>().First(b => b.Text == ",");
        commaBtn.Click();
        Assert.Equal(CellFormatPresets.Decimal2, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("1,500.00", sheet[0, 0]);

        // Increase decimals
        var incBtn = view.Toolbar.Children.OfType<StandardButton>().First(b => b.Text == ".0+");
        incBtn.Click();
        Assert.Equal("#,##0.000", sheet.GetCellNumberFormat(0, 0));

        // Decrease decimals
        var decBtn = view.Toolbar.Children.OfType<StandardButton>().First(b => b.Text == ".0-");
        decBtn.Click();
        Assert.Equal("#,##0.00", sheet.GetCellNumberFormat(0, 0));
    }

    [Fact]
    public void CellsView_DispatcherExecutesNumberFormatCommands()
    {
        var sheet = new SheetModel(3, 3);
        sheet.SetInput(0, 0, "42.5", CellEntryMode.Number);

        var view = new CellsView(sheet);
        view.CellControl.SelectCell(new CellPosition(0, 0));

        Assert.True(view.CommandDispatcher.TryExecute("format.number.currency"));
        Assert.Equal(CellFormatPresets.Currency, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("$42.50", sheet[0, 0]);

        Assert.True(view.CommandDispatcher.TryExecute("format.number.percentage"));
        Assert.Equal(CellFormatPresets.Percentage, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("4250.0%", sheet[0, 0]);

        Assert.True(view.CommandDispatcher.TryExecute("format.number.integer"));
        Assert.Equal(CellFormatPresets.Integer, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("43", sheet[0, 0]);

        Assert.True(view.CommandDispatcher.TryExecute("format.number.decimal"));
        Assert.Equal(CellFormatPresets.Decimal2, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("42.50", sheet[0, 0]);

        Assert.True(view.CommandDispatcher.TryExecute("format.number.scientific"));
        Assert.Equal(CellFormatPresets.Scientific, sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("4.25E+01", sheet[0, 0]);

        Assert.True(view.CommandDispatcher.TryExecute("format.number.general"));
        Assert.Null(sheet.GetCellNumberFormat(0, 0));
        Assert.Equal("42.5", sheet[0, 0]);
    }
}

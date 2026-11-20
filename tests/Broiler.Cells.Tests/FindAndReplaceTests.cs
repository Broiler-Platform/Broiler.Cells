using System;
using System.IO;
using System.Linq;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public class FindAndReplaceTests
{
    [Fact]
    public void CellSearchEngine_FindNext_MatchesCaseInsensitiveSubstringByDefault()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Hello World";
        sheet[2, 3] = "the world of data";
        sheet[5, 5] = "Unrelated";

        var options = new CellSearchOptions { SearchText = "world" };

        var first = CellSearchEngine.FindNext(sheet, null, options);
        Assert.NotNull(first);
        Assert.Equal(new CellPosition(0, 0), first.Position);
        Assert.Equal("Hello World", first.Value);

        var second = CellSearchEngine.FindNext(sheet, new CellPosition(0, 0), options);
        Assert.NotNull(second);
        Assert.Equal(new CellPosition(2, 3), second.Position);

        // Wrap around to the start
        var wrap = CellSearchEngine.FindNext(sheet, new CellPosition(2, 3), options with { WrapAround = true });
        Assert.NotNull(wrap);
        Assert.Equal(new CellPosition(0, 0), wrap.Position);

        // Without wrap around returns null
        var noWrap = CellSearchEngine.FindNext(sheet, new CellPosition(2, 3), options with { WrapAround = false });
        Assert.Null(noWrap);
    }

    [Fact]
    public void CellSearchEngine_FindPrevious_TraversesInReverse()
    {
        var sheet = new SheetModel(10, 10);
        sheet[1, 1] = "alpha";
        sheet[3, 4] = "alpha beta";
        sheet[8, 2] = "gamma alpha";

        var options = new CellSearchOptions { SearchText = "alpha" };

        var last = CellSearchEngine.FindPrevious(sheet, null, options);
        Assert.NotNull(last);
        Assert.Equal(new CellPosition(8, 2), last.Position);

        var prev = CellSearchEngine.FindPrevious(sheet, new CellPosition(8, 2), options);
        Assert.NotNull(prev);
        Assert.Equal(new CellPosition(3, 4), prev.Position);

        var prev2 = CellSearchEngine.FindPrevious(sheet, new CellPosition(3, 4), options);
        Assert.NotNull(prev2);
        Assert.Equal(new CellPosition(1, 1), prev2.Position);

        // Wrap around from beginning to end
        var wrap = CellSearchEngine.FindPrevious(sheet, new CellPosition(1, 1), options with { WrapAround = true });
        Assert.NotNull(wrap);
        Assert.Equal(new CellPosition(8, 2), wrap.Position);
    }

    [Fact]
    public void CellSearchEngine_MatchCase_EnforcesCasing()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Quarter 1";
        sheet[1, 1] = "quarter 2";

        var caseSensitive = new CellSearchOptions { SearchText = "Quarter", MatchCase = true };
        var matches = CellSearchEngine.FindAll(sheet, caseSensitive);
        Assert.Single(matches);
        Assert.Equal(new CellPosition(0, 0), matches[0].Position);

        var caseInsensitive = new CellSearchOptions { SearchText = "Quarter", MatchCase = false };
        var allMatches = CellSearchEngine.FindAll(sheet, caseInsensitive);
        Assert.Equal(2, allMatches.Count);
    }

    [Fact]
    public void CellSearchEngine_MatchEntireCell_RequiresExactEquality()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "Revenue";
        sheet[0, 1] = "Total Revenue";

        var exact = new CellSearchOptions { SearchText = "Revenue", MatchEntireCell = true };
        var matches = CellSearchEngine.FindAll(sheet, exact);
        Assert.Single(matches);
        Assert.Equal(new CellPosition(0, 0), matches[0].Position);

        var substring = new CellSearchOptions { SearchText = "Revenue", MatchEntireCell = false };
        var subMatches = CellSearchEngine.FindAll(sheet, substring);
        Assert.Equal(2, subMatches.Count);
    }

    [Fact]
    public void CellSearchEngine_SearchFormulas_MatchesFormulaText()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "10";
        sheet[0, 1] = "20";
        sheet.SetInput(1, 0, "=SUM(A1:B1)", CellEntryMode.Formula);
        sheet.Recalculate();

        // When searching values (SearchFormulas = false), formula "=SUM" is not matched by "SUM" because displayed value is "30"
        var valueSearch = new CellSearchOptions { SearchText = "SUM", SearchFormulas = false };
        var valueMatch = CellSearchEngine.FindNext(sheet, null, valueSearch);
        Assert.Null(valueMatch);

        // When searching formulas (SearchFormulas = true), formula is matched
        var formulaSearch = new CellSearchOptions { SearchText = "SUM", SearchFormulas = true };
        var formulaMatch = CellSearchEngine.FindNext(sheet, null, formulaSearch);
        Assert.NotNull(formulaMatch);
        Assert.Equal(new CellPosition(1, 0), formulaMatch.Position);
        Assert.Equal("=SUM(A1:B1)", formulaMatch.Formula);
    }

    [Fact]
    public void CellSearchEngine_WorkbookScope_CrossesSheetsAndWraps()
    {
        var sheet1 = new SheetModel(5, 5);
        sheet1[0, 0] = "Item A";

        var workbook = new WorkbookModel(sheet1);
        var sheet2 = workbook.Add("Sheet 2").Sheet;
        sheet2[2, 2] = "Item B";

        var sheet3 = workbook.Add("Sheet 3").Sheet;
        sheet3[4, 1] = "Item C";

        var options = new CellSearchOptions { SearchText = "Item", Scope = CellSearchScope.Workbook };

        // Search starting from Sheet 1, cell (0, 0)
        var next = CellSearchEngine.FindNextInWorkbook(workbook, 0, new CellPosition(0, 0), options, out int nextSheet);
        Assert.NotNull(next);
        Assert.Equal(1, nextSheet); // Sheet 2
        Assert.Equal(new CellPosition(2, 2), next.Position);

        // Search starting from Sheet 2, cell (2, 2)
        var next2 = CellSearchEngine.FindNextInWorkbook(workbook, 1, new CellPosition(2, 2), options, out int nextSheet2);
        Assert.NotNull(next2);
        Assert.Equal(2, nextSheet2); // Sheet 3
        Assert.Equal(new CellPosition(4, 1), next2.Position);

        // Search starting from Sheet 3, cell (4, 1) -> wraps around to Sheet 1
        var wrap = CellSearchEngine.FindNextInWorkbook(workbook, 2, new CellPosition(4, 1), options, out int wrapSheet);
        Assert.NotNull(wrap);
        Assert.Equal(0, wrapSheet); // Sheet 1
        Assert.Equal(new CellPosition(0, 0), wrap.Position);

        // Search previous backward across sheets
        var prev = CellSearchEngine.FindPreviousInWorkbook(workbook, 0, new CellPosition(0, 0), options, out int prevSheet);
        Assert.NotNull(prev);
        Assert.Equal(2, prevSheet); // Sheet 3
        Assert.Equal(new CellPosition(4, 1), prev.Position);
    }

    [Fact]
    public void CellSearchEngine_Replace_SingleCellUpdatesAndRecalculates()
    {
        var sheet = new SheetModel(5, 5);
        sheet[1, 1] = "Old Value Here";

        var options = new CellSearchOptions
        {
            SearchText = "Old",
            ReplaceText = "New",
            MatchCase = true,
        };

        bool replaced = CellSearchEngine.Replace(sheet, new CellPosition(1, 1), options);
        Assert.True(replaced);
        Assert.Equal("New Value Here", sheet[1, 1]);
    }

    [Fact]
    public void CellSearchEngine_ReplaceAll_ReplacesAllOccurrencesInSheet()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "cat and cat";
        sheet[1, 1] = "the cat slept";
        sheet[2, 2] = "dog";

        var options = new CellSearchOptions
        {
            SearchText = "cat",
            ReplaceText = "bird",
            MatchCase = false,
        };

        int count = CellSearchEngine.ReplaceAll(sheet, options);
        Assert.Equal(2, count);
        Assert.Equal("bird and bird", sheet[0, 0]);
        Assert.Equal("the bird slept", sheet[1, 1]);
        Assert.Equal("dog", sheet[2, 2]);
    }

    [Fact]
    public void CellSearchEngine_ReplaceAllInWorkbook_ReplacesAcrossAllSheets()
    {
        var sheet1 = new SheetModel(5, 5);
        sheet1[0, 0] = "Replace Me";

        var workbook = new WorkbookModel(sheet1);
        var sheet2 = workbook.Add("Sheet 2").Sheet;
        sheet2[1, 1] = "Replace Me Too";

        var options = new CellSearchOptions
        {
            SearchText = "Replace Me",
            ReplaceText = "Done",
        };

        int count = CellSearchEngine.ReplaceAllInWorkbook(workbook, options);
        Assert.Equal(2, count);
        Assert.Equal("Done", sheet1[0, 0]);
        Assert.Equal("Done Too", sheet2[1, 1]);
    }

    [Fact]
    public void CellSearchEngine_SparseSheetScale_EvaluatesFastOnLargeDimensions()
    {
        // 1,000,000 rows × 10,000 columns sparse sheet
        var sheet = new SheetModel(1_000_000, 10_000);
        sheet[10, 5] = "Match 1";
        sheet[500_000, 5_000] = "Match 2";

        var options = new CellSearchOptions { SearchText = "Match" };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var all = CellSearchEngine.FindAll(sheet, options);
        sw.Stop();

        Assert.Equal(2, all.Count);
        Assert.Equal(new CellPosition(10, 5), all[0].Position);
        Assert.Equal(new CellPosition(500_000, 5_000), all[1].Position);
        Assert.True(sw.ElapsedMilliseconds < 250, $"FindAll took {sw.ElapsedMilliseconds} ms, expected < 250 ms");
    }

    [Fact]
    public void CellsView_ShowFind_OpensPanelAndTogglesModes()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

        Assert.False(view.IsFindReplaceVisible);

        view.ShowFind();
        Assert.True(view.IsFindReplaceVisible);
        Assert.False(view.FindReplace.IsReplaceMode);

        view.ShowReplace();
        Assert.True(view.IsFindReplaceVisible);
        Assert.True(view.FindReplace.IsReplaceMode);

        view.CloseFindReplace();
        Assert.False(view.IsFindReplaceVisible);
    }

    [Fact]
    public void CellsView_FindNext_NavigatesAndSelectsCell()
    {
        var sheet = new SheetModel(20, 20);
        sheet[5, 5] = "SearchTarget";
        var view = new CellsView(sheet);

        view.ShowFind();
        view.FindReplace.SearchText = "SearchTarget";

        var result = view.FindNext();
        Assert.NotNull(result);
        Assert.Equal(new CellPosition(5, 5), result.Position);
        Assert.Equal(new CellPosition(5, 5), view.CellControl.SelectedCell);
    }

    [Fact]
    public void CellsView_FindNext_CrossSheetWorkbookScopeSwitchesTab()
    {
        var sheet1 = new SheetModel(10, 10);
        var workbook = new WorkbookModel(sheet1);
        var sheet2 = workbook.Add("SecondSheet").Sheet;
        sheet2[3, 3] = "FoundInSecond";

        var view = new CellsView(sheet1);
        view.ShowFind();
        view.FindReplace.SearchText = "FoundInSecond";
        view.FindReplace.Scope = CellSearchScope.Workbook;

        var result = view.FindNext();
        Assert.NotNull(result);
        Assert.Equal(new CellPosition(3, 3), result.Position);
        Assert.Equal("SecondSheet", view.Workbook.SelectedSheet.Name);
        Assert.Equal(new CellPosition(3, 3), view.CellControl.SelectedCell);
    }

    [Fact]
    public void CellsView_ReplaceNextAndReplaceAll_OperatesInteractively()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Alpha Beta";
        sheet[2, 2] = "Alpha Gamma";
        var view = new CellsView(sheet);

        view.ShowReplace();
        view.FindReplace.SearchText = "Alpha";
        view.FindReplace.ReplaceText = "Delta";

        int replaced = view.ReplaceAll();
        Assert.Equal(2, replaced);
        Assert.Equal("Delta Beta", sheet[0, 0]);
        Assert.Equal("Delta Gamma", sheet[2, 2]);
    }

    [Fact]
    public void CellControl_ScrollCellIntoView_AdjustsScrollOffsets()
    {
        var sheet = new SheetModel(100, 100);
        var control = new CellControl(sheet);
        control.Measure(new BSize(600, 400));
        control.Arrange(new BRect(0, 0, 600, 400));

        // Target cell at row 50, col 20 (well outside visible 600x400)
        control.ScrollCellIntoView(50, 20);

        Assert.True(control.ScrollView.HorizontalOffset > 0, "Horizontal scroll should have shifted right.");
        Assert.True(control.ScrollView.VerticalOffset > 0, "Vertical scroll should have shifted down.");
    }

    [Fact]
    public void CellsView_KeyboardShortcuts_DispatchesCtrlF_CtrlH_Escape()
    {
        var sheet = new SheetModel(5, 5);
        var view = new CellsView(sheet);

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        // Ctrl+F opens Find mode
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs((int)'F', true, false, false), KeyboardKeyTransition.Down));

        Assert.True(view.IsFindReplaceVisible);
        Assert.False(view.FindReplace.IsReplaceMode);

        // Ctrl+H switches to Replace mode
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs((int)'H', true, false, false), KeyboardKeyTransition.Down));

        Assert.True(view.IsFindReplaceVisible);
        Assert.True(view.FindReplace.IsReplaceMode);

        // Escape closes the panel
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs(0x1B, false, false, false), KeyboardKeyTransition.Down));

        Assert.False(view.IsFindReplaceVisible);
    }

    [Fact]
    public void CellsView_KeyboardShortcuts_F3_And_ShiftF3_NavigateMatches()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Item";
        sheet[2, 2] = "Item";
        sheet[5, 5] = "Item";
        var view = new CellsView(sheet);

#pragma warning disable CS0618
        var adapter = new StandardLegacyGraphicsInputAdapter("cells-test");
#pragma warning restore CS0618

        view.ShowFind();
        view.FindReplace.SearchText = "Item";

        // Current cell is (0, 0). First F3 finds next match -> (2, 2)
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs(0x72, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(2, 2), view.CellControl.SelectedCell);

        // Next F3 finds next match -> (5, 5)
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs(0x72, false, false, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(5, 5), view.CellControl.SelectedCell);

        // Shift+F3 moves backward to previous match -> (2, 2)
        view.DispatchInput(adapter.FromKey(new BKeyEventArgs(0x72, false, true, false), KeyboardKeyTransition.Down));
        Assert.Equal(new CellPosition(2, 2), view.CellControl.SelectedCell);
    }

    [Fact]
    public void CellSearchEngine_ReplaceFormulas_UpdatesFormulaExpression()
    {
        var sheet = new SheetModel(5, 5);
        sheet.SetInput(0, 0, "=SUM(A1:B1)", CellEntryMode.Formula);

        var options = new CellSearchOptions
        {
            SearchText = "SUM",
            ReplaceText = "AVERAGE",
            SearchFormulas = true,
        };

        bool replaced = CellSearchEngine.Replace(sheet, new CellPosition(0, 0), options);
        Assert.True(replaced);
        Assert.Equal("=AVERAGE(A1:B1)", sheet.GetContent(0, 0).Formula);
    }

    [Fact]
    public void FindReplacePanel_ToggleOptions_UpdatesStateAndTriggersEvents()
    {
        var panel = new FindReplacePanel();
        int optionsChangedCount = 0;
        panel.OptionsChanged += (_, _) => optionsChangedCount++;

        Assert.False(panel.MatchCase);
        Assert.False(panel.MatchEntireCell);
        Assert.False(panel.SearchFormulas);
        Assert.Equal(CellSearchScope.Sheet, panel.Scope);

        panel.MatchCase = true;
        panel.MatchEntireCell = true;
        panel.SearchFormulas = true;
        panel.Scope = CellSearchScope.Workbook;

        var options = panel.GetOptions();
        Assert.True(options.MatchCase);
        Assert.True(options.MatchEntireCell);
        Assert.True(options.SearchFormulas);
        Assert.Equal(CellSearchScope.Workbook, options.Scope);
    }
}

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
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class ConditionalFormattingTests
{
    [Fact]
    public void SheetModel_ConditionalFormats_Management_And_Events()
    {
        var sheet = new SheetModel(10, 10);
        int changeCount = 0;
        sheet.ConditionalFormatsChanged += () => changeCount++;

        var rule = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.GreaterThan,
            Formula1: "50",
            Style: new ConditionalFormatStyle(Shading: BColor.Red));

        var format = new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 4))],
            [rule]);

        // Add
        sheet.AddConditionalFormat(format);
        Assert.Single(sheet.ConditionalFormats);
        Assert.Equal(1, changeCount);

        // Remove
        bool removed = sheet.RemoveConditionalFormat(format);
        Assert.True(removed);
        Assert.Empty(sheet.ConditionalFormats);
        Assert.Equal(2, changeCount);

        // Clear
        sheet.AddConditionalFormat(format);
        Assert.Equal(3, changeCount);
        sheet.ClearConditionalFormats();
        Assert.Empty(sheet.ConditionalFormats);
        Assert.Equal(4, changeCount);
    }

    [Fact]
    public void ConditionalFormatEngine_Evaluates_CellIs_Numeric_And_Text()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "150";
        sheet[0, 1] = "50";
        sheet[0, 2] = "75";
        sheet[0, 3] = "Hello";

        var gtRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.GreaterThan,
            Formula1: "100",
            Style: new ConditionalFormatStyle(Shading: BColor.Green));

        var btwRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.Between,
            Formula1: "60",
            Formula2: "90",
            Style: new ConditionalFormatStyle(Shading: BColor.FromName("yellow")));

        var eqRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.Equal,
            Formula1: "Hello",
            Style: new ConditionalFormatStyle(Shading: BColor.Blue));

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 9))],
            [gtRule, btwRule, eqRule]));

        // Cell (0, 0) = 150 > 100 -> Green
        var style0 = ConditionalFormatEngine.Evaluate(sheet, 0, 0);
        Assert.NotNull(style0);
        Assert.Equal(BColor.Green, style0.Shading);

        // Cell (0, 1) = 50 -> No rule matches
        var style1 = ConditionalFormatEngine.Evaluate(sheet, 0, 1);
        Assert.Null(style1);

        // Cell (0, 2) = 75 between 60 and 90 -> Yellow
        var style2 = ConditionalFormatEngine.Evaluate(sheet, 0, 2);
        Assert.NotNull(style2);
        Assert.Equal(BColor.FromName("yellow"), style2.Shading);

        // Cell (0, 3) = "Hello" equals "Hello" -> Blue
        var style3 = ConditionalFormatEngine.Evaluate(sheet, 0, 3);
        Assert.NotNull(style3);
        Assert.Equal(BColor.Blue, style3.Shading);
    }

    [Fact]
    public void ConditionalFormatEngine_Evaluates_ContainsText_BeginsWith_EndsWith()
    {
        var sheet = new SheetModel(10, 10);
        sheet[1, 0] = "Invoice #1234 Overdue";
        sheet[1, 1] = "PRE-PAID Order";
        sheet[1, 2] = "Report.xlsx";
        sheet[1, 3] = "Normal text";

        var containsRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.ContainsText,
            Formula1: "overdue",
            Style: new ConditionalFormatStyle(Shading: BColor.Red));

        var beginsRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.BeginsWith,
            Formula1: "pre-",
            Style: new ConditionalFormatStyle(Shading: BColor.FromName("cyan")));

        var endsRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.EndsWith,
            Formula1: ".xlsx",
            Style: new ConditionalFormatStyle(Shading: BColor.Green));

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(1, 0), new CellAddress(1, 9))],
            [containsRule, beginsRule, endsRule]));

        // Contains "overdue"
        var s0 = ConditionalFormatEngine.Evaluate(sheet, 1, 0);
        Assert.NotNull(s0);
        Assert.Equal(BColor.Red, s0.Shading);

        // Begins with "pre-"
        var s1 = ConditionalFormatEngine.Evaluate(sheet, 1, 1);
        Assert.NotNull(s1);
        Assert.Equal(BColor.FromName("cyan"), s1.Shading);

        // Ends with ".xlsx"
        var s2 = ConditionalFormatEngine.Evaluate(sheet, 1, 2);
        Assert.NotNull(s2);
        Assert.Equal(BColor.Green, s2.Shading);

        // Normal text does not match
        var s3 = ConditionalFormatEngine.Evaluate(sheet, 1, 3);
        Assert.Null(s3);
    }

    [Fact]
    public void ConditionalFormatEngine_Evaluates_DuplicateValues_And_UniqueValues()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "Apple";
        sheet[0, 1] = "Banana";
        sheet[0, 2] = "Apple"; // duplicate
        sheet[0, 3] = "Orange"; // unique

        var dupRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.DuplicateValues,
            Style: new ConditionalFormatStyle(Shading: BColor.FromName("magenta")));

        var uniqRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.UniqueValues,
            Style: new ConditionalFormatStyle(Shading: BColor.FromName("yellow")));

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 3))],
            [dupRule, uniqRule]));

        // (0, 0) "Apple" is a duplicate
        var s0 = ConditionalFormatEngine.Evaluate(sheet, 0, 0);
        Assert.NotNull(s0);
        Assert.Equal(BColor.FromName("magenta"), s0.Shading);

        // (0, 2) "Apple" is a duplicate
        var s2 = ConditionalFormatEngine.Evaluate(sheet, 0, 2);
        Assert.NotNull(s2);
        Assert.Equal(BColor.FromName("magenta"), s2.Shading);

        // (0, 3) "Orange" is unique
        var s3 = ConditionalFormatEngine.Evaluate(sheet, 0, 3);
        Assert.NotNull(s3);
        Assert.Equal(BColor.FromName("yellow"), s3.Shading);
    }

    [Fact]
    public void ConditionalFormatEngine_Evaluates_Blanks_And_NoBlanks()
    {
        var sheet = new SheetModel(10, 10);
        sheet[2, 0] = "";
        sheet[2, 1] = "Filled";

        var blankRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.Blanks,
            Style: new ConditionalFormatStyle(Shading: BColor.FromName("gray")));

        var noBlankRule = new ConditionalFormatRule(
            ConditionalFormatRuleType.NoBlanks,
            Style: new ConditionalFormatStyle(Shading: BColor.Green));

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(2, 0), new CellAddress(2, 1))],
            [blankRule, noBlankRule]));

        var s0 = ConditionalFormatEngine.Evaluate(sheet, 2, 0);
        Assert.NotNull(s0);
        Assert.Equal(BColor.FromName("gray"), s0.Shading);

        var s1 = ConditionalFormatEngine.Evaluate(sheet, 2, 1);
        Assert.NotNull(s1);
        Assert.Equal(BColor.Green, s1.Shading);
    }

    [Fact]
    public void ConditionalFormatEngine_Evaluates_ColorScale_2Color_And_3Color()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "0";
        sheet[1, 0] = "50";
        sheet[2, 0] = "100";

        var red = BColor.FromArgb(255, 255, 0, 0);
        var green = BColor.FromArgb(255, 0, 255, 0);
        var yellow = BColor.FromArgb(255, 255, 255, 0);

        // 3-color scale: Min (0) = red, Mid (50) = yellow, Max (100) = green
        var rule3 = new ConditionalFormatRule(
            ConditionalFormatRuleType.ColorScale,
            ColorScale: new ColorScaleConfiguration(MinColor: red, MaxColor: green, MidColor: yellow));

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 0))],
            [rule3]));

        var sMin = ConditionalFormatEngine.Evaluate(sheet, 0, 0);
        Assert.NotNull(sMin?.Shading);
        Assert.Equal(red.R, sMin.Shading.Value.R);
        Assert.Equal(red.G, sMin.Shading.Value.G);
        Assert.Equal(red.B, sMin.Shading.Value.B);

        var sMid = ConditionalFormatEngine.Evaluate(sheet, 1, 0);
        Assert.NotNull(sMid?.Shading);
        Assert.Equal(yellow.R, sMid.Shading.Value.R);
        Assert.Equal(yellow.G, sMid.Shading.Value.G);
        Assert.Equal(yellow.B, sMid.Shading.Value.B);

        var sMax = ConditionalFormatEngine.Evaluate(sheet, 2, 0);
        Assert.NotNull(sMax?.Shading);
        Assert.Equal(green.R, sMax.Shading.Value.R);
        Assert.Equal(green.G, sMax.Shading.Value.G);
        Assert.Equal(green.B, sMax.Shading.Value.B);
    }

    [Fact]
    public void ConditionalFormatEngine_Respects_StopIfTrue_And_MergesStyles()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "100";

        var rule1 = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.GreaterThan,
            Formula1: "50",
            Style: new ConditionalFormatStyle(Shading: BColor.Red, Bold: true),
            StopIfTrue: false);

        var rule2 = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.GreaterThan,
            Formula1: "20",
            Style: new ConditionalFormatStyle(Foreground: BColor.White, Italic: true),
            StopIfTrue: false);

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            [rule1, rule2]));

        var combined = ConditionalFormatEngine.Evaluate(sheet, 0, 0);
        Assert.NotNull(combined);
        // Rule 1 provided Shading and Bold
        Assert.Equal(BColor.Red, combined.Shading);
        Assert.True(combined.Bold);
        // Rule 2 merged Foreground and Italic
        Assert.Equal(BColor.White, combined.Foreground);
        Assert.True(combined.Italic);

        // Now test StopIfTrue
        sheet.ClearConditionalFormats();
        var rule1Stop = rule1 with { StopIfTrue = true };
        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            [rule1Stop, rule2]));

        var stopped = ConditionalFormatEngine.Evaluate(sheet, 0, 0);
        Assert.NotNull(stopped);
        Assert.Equal(BColor.Red, stopped.Shading);
        Assert.True(stopped.Bold);
        Assert.Null(stopped.Foreground); // Rule 2 was stopped!
        Assert.Null(stopped.Italic);
    }

    [Fact]
    public void CellGrid_Renders_ConditionalFormatting_Overlay_Without_Mutating_CellContent()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "200";

        // Verify base content has default shading and foreground
        var baseContent = sheet.GetContent(0, 0);
        Assert.Equal(default, baseContent.Shading);

        var rule = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.GreaterThan,
            Formula1: "100",
            Style: new ConditionalFormatStyle(
                Shading: BColor.FromArgb(255, 255, 199, 206),
                Foreground: BColor.FromArgb(255, 156, 0, 6),
                Bold: true));

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(0, 0))],
            [rule]));

        using var fixture = new GridFixture(sheet);
        var frame = fixture.Session.RenderFrame();

        // Shading rectangle should have been drawn with conditional formatting color
        var rects = frame.Commands.OfType<BRenderCommand.FillRect>().ToArray();
        Assert.NotEmpty(rects);
        Assert.Contains(rects, r => r.Color == BColor.FromArgb(255, 255, 199, 206));

        // Crucial invariant: Sheet content was NEVER mutated
        var contentAfter = sheet.GetContent(0, 0);
        Assert.Equal(default, contentAfter.Shading);
    }

    [Fact]
    public void SheetDocumentSerializer_Roundtrips_ConditionalFormatting_In_Xlsx_And_Ods()
    {
        var sheet = new SheetModel(5, 5);
        sheet[0, 0] = "150";
        sheet[1, 0] = "50";

        var rule = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.GreaterThan,
            Formula1: "100",
            Style: new ConditionalFormatStyle(
                Shading: BColor.FromArgb(255, 198, 239, 206),
                Foreground: BColor.FromArgb(255, 0, 97, 0)));

        sheet.AddConditionalFormat(new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 4))],
            [rule]));

        // Roundtrip via XLSX
        using var xlsxStream = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, xlsxStream, "test.xlsx");
        xlsxStream.Position = 0;

        var loadedXlsx = SheetDocumentSerializer.Load(xlsxStream, "test.xlsx");
        Assert.Single(loadedXlsx.ConditionalFormats);
        var xlsxRule = loadedXlsx.ConditionalFormats[0].Rules[0];
        Assert.Equal(ConditionalFormatRuleType.CellIs, xlsxRule.Type);
        Assert.Equal(ConditionalFormatOperator.GreaterThan, xlsxRule.Operator);
        Assert.Equal("100", xlsxRule.Formula1);
        Assert.NotNull(xlsxRule.Style);
        Assert.Equal(BColor.FromArgb(255, 198, 239, 206), xlsxRule.Style.Shading);
        Assert.Equal(BColor.FromArgb(255, 0, 97, 0), xlsxRule.Style.Foreground);

        // Roundtrip via ODS
        using var odsStream = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, odsStream, "test.ods");
        odsStream.Position = 0;

        var loadedOds = SheetDocumentSerializer.Load(odsStream, "test.ods");
        Assert.Single(loadedOds.ConditionalFormats);
        var odsRule = loadedOds.ConditionalFormats[0].Rules[0];
        Assert.Equal(ConditionalFormatRuleType.CellIs, odsRule.Type);
        Assert.Equal(ConditionalFormatOperator.GreaterThan, odsRule.Operator);
        Assert.Equal("100", odsRule.Formula1);
        Assert.NotNull(odsRule.Style);
        Assert.Equal(BColor.FromArgb(255, 198, 239, 206), odsRule.Style.Shading);
        Assert.Equal(BColor.FromArgb(255, 0, 97, 0), odsRule.Style.Foreground);
    }

    [Fact]
    public void CellsView_ConditionalFormatting_Commands_And_Panel()
    {
        var sheet = new SheetModel(10, 10);
        sheet[0, 0] = "150";

        var view = new CellsView(sheet);
        view.Measure(new BSize(800, 600));
        view.Arrange(new BRect(0, 0, 800, 600));

        Assert.False(view.IsConditionalFormatDialogVisible);

        // Trigger command
        Assert.True(view.CommandDispatcher.TryExecute("format.conditional.greater-than"));
        Assert.True(view.IsConditionalFormatDialogVisible);
        Assert.Equal(ConditionalFormatRuleType.CellIs, view.ConditionalFormatDialog.SelectedType);
        Assert.Equal(ConditionalFormatOperator.GreaterThan, view.ConditionalFormatDialog.SelectedOperator);

        // Apply rule via panel
        view.ConditionalFormatDialog.Val1Edit.Text = "100";
        view.ConditionalFormatDialog.Apply();

        Assert.False(view.IsConditionalFormatDialogVisible);
        Assert.Single(sheet.ConditionalFormats);

        // Verify applied rule matches
        var rule = sheet.ConditionalFormats[0].Rules[0];
        Assert.Equal(ConditionalFormatRuleType.CellIs, rule.Type);
        Assert.Equal(ConditionalFormatOperator.GreaterThan, rule.Operator);
        Assert.Equal("100", rule.Formula1);

        // Clear via command
        Assert.True(view.CommandDispatcher.TryExecute("format.conditional.clear-all"));
        Assert.Empty(sheet.ConditionalFormats);
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Pagination;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Paging;
using Broiler.UI;
using Xunit;

namespace Broiler.Cells.Tests;

public sealed class PrintAndExportTests
{
    [Fact]
    public void WorksheetPrintSetupPresetsAndGeometry()
    {
        var setup = new WorksheetPrintSetup();
        Assert.Equal(BPaperSize.A4, setup.PaperSize);
        Assert.Equal(BPageOrientation.Portrait, setup.Orientation);
        Assert.Equal(PageMarginsPreset.Normal, setup.MarginsPreset);
        Assert.True(setup.PrintGridlines);
        Assert.False(setup.PrintHeadings);

        var normalGeom = setup.ToPageGeometry();
        Assert.True(normalGeom.IsUsable);
        Assert.Equal(setup.Margins.Left, normalGeom.MarginLeft);
        Assert.Equal(setup.Margins.Top, normalGeom.MarginTop);

        var wideSetup = setup.WithPreset(PageMarginsPreset.Wide);
        Assert.Equal(PageMarginsPreset.Wide, wideSetup.MarginsPreset);
        Assert.Equal(72.0, wideSetup.Margins.Left);

        var narrowSetup = setup.WithPreset(PageMarginsPreset.Narrow);
        Assert.Equal(PageMarginsPreset.Narrow, narrowSetup.MarginsPreset);
        Assert.Equal(18.0, narrowSetup.Margins.Left);

        var landscapeSetup = setup with { Orientation = BPageOrientation.Landscape };
        var pageSetup = landscapeSetup.ToPageSetup();
        Assert.Equal(BPageOrientation.Landscape, pageSetup.Orientation);
        Assert.True(pageSetup.PageSize.Width > pageSetup.PageSize.Height);
    }

    [Fact]
    public void SheetModelHasDefaultPrintSetup()
    {
        var sheet = new SheetModel(5, 5);
        Assert.NotNull(sheet.PrintSetup);
        Assert.Equal(BPaperSize.A4, sheet.PrintSetup.PaperSize);

        sheet.PrintSetup = sheet.PrintSetup with { PrintGridlines = false, Orientation = BPageOrientation.Landscape };
        Assert.False(sheet.PrintSetup.PrintGridlines);
        Assert.Equal(BPageOrientation.Landscape, sheet.PrintSetup.Orientation);
    }

    [Fact]
    public void ToRichTextDocumentProjectsSheetWithGridlinesAndStyles()
    {
        var sheet = new SheetModel(3, 3);
        sheet[0, 0] = "Revenue";
        sheet[0, 1] = "1500";
        sheet.SetCellShading(0, 0, BColor.FromArgb(255, 200, 230, 201)); // Light green
        sheet.MergeCells(1, 0, 2, 2);
        sheet[1, 0] = "Merged Notes";

        var richDoc = SheetDocumentSerializer.ToRichTextDocument(sheet);
        Assert.NotNull(richDoc);
        Assert.Single(richDoc.Tables);
        Assert.True(richDoc.StyleDefaults.PrintsSheets);

        var table = richDoc.Tables[0];
        Assert.Equal(3, table.Rows.Count);

        // Check shading on cell 0,0
        var cell00 = table.Rows[0].Cells[0];
        Assert.Equal(255, cell00.Shading.A);
        Assert.Equal(200, cell00.Shading.R);

        // Cell borders should be populated with gridlines when printGridlines is true
        Assert.True(cell00.Borders.Left.IsVisible);
        Assert.True(cell00.Borders.Top.IsVisible);

        // Merged cell spans
        var cell10 = table.Rows[1].Cells[0];
        Assert.Equal(3, cell10.ColumnSpan);
        Assert.Equal(2, cell10.RowSpan);
    }

    [Fact]
    public void ExportPdfGeneratesValidPdfDocument()
    {
        var sheet = new SheetModel(4, 4);
        sheet[0, 0] = "Quarter";
        sheet[0, 1] = "Sales";
        sheet[1, 0] = "Q1";
        sheet[1, 1] = "12000";
        sheet[2, 0] = "Q2";
        sheet[2, 1] = "18500";
        sheet[3, 0] = "Total";
        sheet[3, 1] = "=SUM(B2:B3)";

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "report.pdf");

        byte[] pdfBytes = ms.ToArray();
        Assert.True(pdfBytes.Length > 200);

        string header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);

        // Search for EOF trailer marker
        string content = Encoding.ASCII.GetString(pdfBytes);
        Assert.Contains("%%EOF", content);
        Assert.Contains("/Type /Pages", content);
    }

    [Fact]
    public void ExportHtmlAndRoundTrip()
    {
        var sheet = new SheetModel(3, 3);
        sheet[0, 0] = "Product";
        sheet[0, 1] = "Price";
        sheet[1, 0] = "Apples";
        sheet[1, 1] = "2.50";
        sheet[2, 0] = "Bananas";
        sheet[2, 1] = "1.80";

        using var ms = new MemoryStream();
        SheetDocumentSerializer.Save(sheet, ms, "data.html");

        byte[] htmlBytes = ms.ToArray();
        Assert.True(htmlBytes.Length > 50);
        string html = Encoding.UTF8.GetString(htmlBytes);
        Assert.Contains("<table", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Apples", html);

        // Load back from HTML
        ms.Position = 0;
        var loaded = SheetDocumentSerializer.Load(ms, "data.html");
        Assert.NotNull(loaded);
        Assert.Equal("Product", loaded[0, 0]);
        Assert.Equal("Price", loaded[0, 1]);
        Assert.Equal("Apples", loaded[1, 0]);
        Assert.Equal("2.50", loaded[1, 1]);
    }

    [Fact]
    public void MultiSheetWorkbookPaginatesSeparatePages()
    {
        var sheet1 = new SheetModel(2, 2);
        sheet1[0, 0] = "Sheet 1 Content";

        var sheet2 = new SheetModel(2, 2);
        sheet2[0, 0] = "Sheet 2 Content";

        var workbook = new WorkbookModel(sheet1, "First");
        workbook.Add("Second", sheet2);

        var richDoc = SheetDocumentSerializer.ToRichTextDocument(sheet1);
        Assert.Equal(2, richDoc.Tables.Count);

        // Paginate through DocumentPaginator
        var paginator = new DocumentPaginator();
        var setup = sheet1.PrintSetup.ToPageSetup();
        using var paginated = paginator.Paginate(richDoc, setup, TestContext.Current.CancellationToken);

        // Should produce at least 2 pages (one for each sheet)
        Assert.True(paginated.PageCount >= 2);
        var page1 = paginated.GetPage(0);
        var page2 = paginated.GetPage(1);
        Assert.NotNull(page1);
        Assert.NotNull(page2);
    }

    [Fact]
    public void PrintCommandInvokesUiPrintHost()
    {
        var printHost = new MockPrintHost();
        var sheet = new SheetModel(3, 3);
        sheet[0, 0] = "Print Test";

        var host = new CellsUiHost(
            () => new BSize(800, 600),
            () => 1.0,
            () => { },
            _ => { });

        using var app = new CellsApp(host, () => { }, initialSheet: sheet, printHost: printHost);
        app.View.ShowPrintDialog();

        // Print job should have been passed to MockPrintHost
        Assert.True(printHost.PrintInvoked);
        Assert.NotNull(printHost.LastJob);
        Assert.True(printHost.LastJob.Document.PreferredSetup.PageSize.Width > 0);
    }

    [Fact]
    public void PasteHtmlTableIntoCellsViewPopulatesGrid()
    {
        var sheet = new SheetModel(5, 5);
        string htmlTable = "<table><tr><td>Alpha</td><td>100</td></tr><tr><td>Beta</td><td>200</td></tr></table>";

        string? clipboardText = htmlTable;
        var host = new CellsUiHost(
            () => new BSize(800, 600),
            () => 1.0,
            () => { },
            _ => { },
            getClipboardText: () => clipboardText,
            setClipboardText: t => clipboardText = t);

        using var app = new CellsApp(host, () => { }, initialSheet: sheet);
        app.CellControl.SelectCell(new CellPosition(1, 1));
        app.View.PasteSelection();

        Assert.Equal("Alpha", sheet[1, 1]);
        Assert.Equal("100", sheet[1, 2]);
        Assert.Equal("Beta", sheet[2, 1]);
        Assert.Equal("200", sheet[2, 2]);
    }

    private sealed class MockPrintHost : IUiPrintHost
    {
        public bool ShowsSystemPrintDialog => true;
        public bool PrintInvoked { get; private set; }
        public UiPrintJob? LastJob { get; private set; }

        public IReadOnlyList<UiPrinterInfo> GetPrinters() => [new UiPrinterInfo("Default Printer", true)];

        public UiPrinterCapabilities GetCapabilities(string printerName) => UiPrinterCapabilities.Unknown;

        public Task<UiPrintResult> PrintAsync(UiPrintJob job, CancellationToken cancellationToken = default)
        {
            PrintInvoked = true;
            LastJob = job;
            return Task.FromResult(new UiPrintResult(UiPrintOutcome.Printed));
        }
    }
}

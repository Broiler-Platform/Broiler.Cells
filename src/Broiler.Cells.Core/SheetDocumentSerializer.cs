using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Broiler.Documents;
using Broiler.Documents.Html;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Ods;
using Broiler.Documents.Pdf;
using Broiler.Documents.Xlsx;

namespace Broiler.Cells.Core;

/// <summary>Comprehensive CSV, TSV, Markdown, HTML, PDF, XLSX and ODS import/export for Broiler Cells.</summary>
public static class SheetDocumentSerializer
{
    public static bool IsWorkbook(string name) => Path.GetExtension(name).ToLowerInvariant() is ".xlsx" or ".ods";

    private static DocumentCodec Codec(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xlsx" => new XlsxDocumentCodec(),
        ".ods" => new OdsDocumentCodec(),
        ".html" or ".htm" => new HtmlDocumentCodec(),
        ".pdf" => new PdfDocumentCodec(),
        _ => throw new NotSupportedException("Unsupported format extension: " + Path.GetExtension(name)),
    };

    public static SheetModel Load(string path)
    {
        using var input = File.OpenRead(path);
        return Load(input, path);
    }

    public static SheetModel Load(Stream input, string name, DocumentReadOptions? options = null)
    {
        string ext = Path.GetExtension(name);
        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return SheetCsvSerializer.Load(input, rejectDataLoss: true);
        if (ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase))
            return SheetCsvSerializer.Load(input, new Broiler.Documents.Csv.CsvReadOptions(delimiter: '\t'), rejectDataLoss: true);
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase) || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
            return SheetMarkdownSerializer.Load(input);

        using var package = new MemoryStream();
        long maximumBytes = (options ?? DocumentReadOptions.Default).Limits.MaxDocumentBytes;
        var buffer = new byte[81920];
        int length;
        while ((length = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (package.Length + length > maximumBytes) throw new InvalidDataException("Workbook exceeds the document byte limit.");
            package.Write(buffer, 0, length);
        }
        package.Position = 0;

        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            var codec = new HtmlDocumentCodec();
            var readResult = codec.Read(package, options);
            if (readResult.Status is not (DocumentResultStatus.Success or DocumentResultStatus.Partial) || readResult.Document is null)
                throw new InvalidDataException("HTML document could not be read: " + string.Join("; ", readResult.Diagnostics.Select(d => d.Message)));
            var htmlWorkbook = SpreadsheetWorkbook.FromRichTextDocument(readResult.Document);
            return LoadWorkbookModel(htmlWorkbook, name, readResult.Diagnostics);
        }

        var result = Codec(name).Read(package, options);
        if (result.Status is not (DocumentResultStatus.Success or DocumentResultStatus.Partial))
            throw new InvalidDataException("Workbook could not be read: " + string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var workbook = result switch
        {
            XlsxReadResult xlsx => xlsx.Workbook,
            OdsReadResult ods => ods.Workbook,
            _ => throw new InvalidDataException("No workbook model was returned."),
        };
        // Every worksheet must fit: later export must never allocate a huge dense grid or truncate data.
        foreach (var worksheet in workbook.Sheets)
            if (worksheet.RowCount > SheetModel.MaximumRows || worksheet.ColumnCount > SheetModel.MaximumColumns
                || worksheet.Rows.SelectMany(r => r.Cells).Any(c => c.Address.Row + c.RowSpan > SheetModel.MaximumRows))
                throw new InvalidDataException($"Workbook exceeds the {SheetModel.MaximumRows} row / {SheetModel.MaximumColumns} column limit. No worksheet was truncated.");
        return LoadWorkbookModel(workbook, name, result.Diagnostics);
    }

    private static SheetModel LoadWorkbookModel(SpreadsheetWorkbook workbook, string name, IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        WorkbookModel? editing = null;
        foreach (var worksheet in workbook.Sheets)
        {
            int minRows = Math.Max(worksheet.RowCount, Math.Max(worksheet.FrozenRows, worksheet.SplitRows));
            int minCols = Math.Max(worksheet.ColumnCount, Math.Max(worksheet.FrozenColumns, worksheet.SplitColumns));
            if (worksheet.Columns != null && worksheet.Columns.Count > 0)
                minCols = Math.Max(minCols, worksheet.Columns.Max(c => c.ColumnIndex) + 1);
            if (worksheet.Rows.Count > 0)
                minRows = Math.Max(minRows, worksheet.Rows.Max(r => r.RowIndex) + 1);
            var grid = new SheetModel(Math.Max(1, minRows), Math.Max(1, minCols));
            grid.SummaryRowsBelow = worksheet.SummaryRowsBelow;
            grid.SummaryColumnsRight = worksheet.SummaryColumnsRight;

            if (worksheet.ColumnWidths is { Count: > 0 } widths)
                grid.SetColumnWidths(widths);

            if (worksheet.Columns != null)
            {
                foreach (var col in worksheet.Columns)
                {
                    if (col.ColumnIndex < grid.ColumnCount)
                    {
                        if (col.OutlineLevel > 0)
                            grid.SetColumnOutlineLevel(col.ColumnIndex, col.OutlineLevel);
                        if (col.Hidden)
                            grid.SetColumnHidden(col.ColumnIndex, true);
                    }
                }
            }

            foreach (var row in worksheet.Rows)
            {
                if (row.Height is { } h && h > 0 && row.RowIndex < grid.RowCount)
                    grid.SetRowHeight(row.RowIndex, h);
                if (row.OutlineLevel > 0 && row.RowIndex < grid.RowCount)
                    grid.SetRowOutlineLevel(row.RowIndex, row.OutlineLevel);
                if (row.Hidden && row.RowIndex < grid.RowCount)
                    grid.SetRowHidden(row.RowIndex, true);

                foreach (var cell in row.Cells)
                {
                    grid[cell.Address.Row, cell.Address.Column] = cell.CachedText;
                    if (cell.Hyperlink is not null)
                        grid.SetCellHyperlink(cell.Address.Row, cell.Address.Column, cell.Hyperlink);
                }
            }

            // Restore collapsed groups based on collapsed rows or columns
            foreach (var rg in grid.GetRowGroups())
            {
                bool anyCollapsed = false;
                for (int r = rg.Start; r <= rg.End; r++)
                {
                    var rObj = worksheet.Rows.FirstOrDefault(row => row.RowIndex == r);
                    if (rObj != null && (rObj.Collapsed || rObj.Hidden))
                    {
                        anyCollapsed = true;
                        break;
                    }
                }
                if (anyCollapsed)
                    grid.SetRowGroupCollapsed(rg.Start, rg.End, rg.Level, true);
            }

            if (worksheet.Columns != null)
            {
                foreach (var cg in grid.GetColumnGroups())
                {
                    bool anyCollapsed = false;
                    for (int c = cg.Start; c <= cg.End; c++)
                    {
                        var cObj = worksheet.Columns.FirstOrDefault(col => col.ColumnIndex == c);
                        if (cObj != null && (cObj.Collapsed || cObj.Hidden))
                        {
                            anyCollapsed = true;
                            break;
                        }
                    }
                    if (anyCollapsed)
                        grid.SetColumnGroupCollapsed(cg.Start, cg.End, cg.Level, true);
                }
            }
            grid.ImportedWorksheet = worksheet;
            if (editing is null) editing = new WorkbookModel(grid, worksheet.Name);
            else editing.Add(worksheet.Name, grid);
        }
        editing!.Select(0);
        var sheet = editing.SelectedSheet.Sheet;
        sheet.ImportNotice = $"Basic import: editing {editing.SelectedSheet.Name} (first of {workbook.Sheets.Count} worksheets). Supported same-worksheet formulas recalculate; unsupported formulas show errors. Use Save As."
            + (diagnostics.Count > 0 ? " " + string.Join("; ", diagnostics.Select(d => d.Code + ": " + d.Message)) : string.Empty);
        return sheet;
    }

    public static SpreadsheetWorkbook ToSpreadsheetWorkbook(SheetModel sheet)
    {
        var editing = sheet.Workbook ?? new WorkbookModel(sheet);
        var worksheets = new List<SpreadsheetWorksheet>();
        int count = editing.Sheets.Count;
        for (int index = 0; index < count; index++)
        {
            var entry = editing.Sheets[index];
            var grid = entry.Sheet;
            var source = grid.ImportedWorksheet;
            int rows = grid.RowCount;
            int columns = grid.ColumnCount;
            var originalCells = source?.Rows.SelectMany(r => r.Cells).ToDictionary(c => c.Address);
            var originalRows = source?.Rows.ToDictionary(r => r.RowIndex);
            var resultRows = new List<SpreadsheetRow>();
            // Pad missing rows so the Documents rich-text projection preserves sparse row addresses.
            for (int row = 0; row < rows; row++)
            {
                var cells = new List<SpreadsheetCell>();
                for (int col = 0; col < columns; col++)
                {
                    var address = new CellAddress(row, col);
                    SpreadsheetCell? old = null;
                    originalCells?.TryGetValue(address, out old);
                    string text = grid[row, col];
                    int colSpan = 1;
                    int rowSpan = 1;
                    if (grid.TryGetMerge(row, col, out int r0, out int c0, out int cs, out int rs))
                    {
                        if (row == r0 && col == c0)
                        {
                            colSpan = cs;
                            rowSpan = rs;
                        }
                    }

                    if (grid.IsCoveredCell(row, col))
                    {
                        cells.Add(new SpreadsheetCell(address, CellValue.Empty, cachedText: string.Empty, columnSpan: 1, rowSpan: 1));
                    }
                    else if (grid.HasEditedContent(row, col))
                    {
                        var content = grid.GetContent(row, col);
                        cells.Add(new SpreadsheetCell(address, content.Value, formula: content.Formula,
                            numberFormat: content.NumberFormat, cachedText: content.Value.IsEmpty && content.Formula is not null ? string.Empty : text,
                            columnSpan: colSpan,
                            rowSpan: rowSpan,
                            shading: content.Shading.A > 0 ? content.Shading : (old?.Shading ?? default))
                        {
                            Font = content.Font != default ? content.Font : (old?.Font ?? InlineStyle.Default),
                            Borders = content.Borders != default ? content.Borders : (old?.Borders ?? default),
                            Alignment = content.Alignment != default ? content.Alignment : (old?.Alignment ?? default),
                            VerticalAlignment = content.VerticalAlignment != default ? content.VerticalAlignment : (old?.VerticalAlignment ?? CellVerticalAlignment.Top),
                            WrapText = content.WrapText || (old?.WrapText ?? false),
                            Note = content.Note ?? old?.Note,
                            Hyperlink = content.Hyperlink ?? old?.Hyperlink,
                        });
                    }
                    else if (old is not null && text == old.CachedText && old.ColumnSpan == colSpan && old.RowSpan == rowSpan && old.WrapText == grid.GetCellWrapText(row, col) && old.Note == grid.GetCellNote(row, col) && old.Hyperlink == grid.GetCellHyperlink(row, col))
                    {
                        cells.Add(old);
                    }
                    else
                    {
                        var content = grid.GetContent(row, col);
                        cells.Add(new SpreadsheetCell(address, content.Value.IsEmpty ? CellValue.FromText(text) : content.Value,
                            formula: content.Formula,
                            numberFormat: content.NumberFormat,
                            cachedText: text,
                            columnSpan: colSpan,
                            rowSpan: rowSpan,
                            shading: content.Shading.A > 0 ? content.Shading : (old?.Shading ?? default))
                        {
                            Font = content.Font != default ? content.Font : (old?.Font ?? InlineStyle.Default),
                            Borders = content.Borders != default ? content.Borders : (old?.Borders ?? default),
                            Alignment = content.Alignment != default ? content.Alignment : (old?.Alignment ?? default),
                            VerticalAlignment = content.VerticalAlignment != default ? content.VerticalAlignment : (old?.VerticalAlignment ?? CellVerticalAlignment.Top),
                            WrapText = content.WrapText || (old?.WrapText ?? false),
                            Note = content.Note ?? old?.Note,
                            Hyperlink = content.Hyperlink ?? old?.Hyperlink,
                        });
                    }
                }
                SpreadsheetRow? oldRow = null;
                originalRows?.TryGetValue(row, out oldRow);
                double? rowHeight = grid.GetRowHeight(row) ?? oldRow?.Height;
                bool isHidden = grid.IsRowHidden(row) || (oldRow?.Hidden ?? false);
                int rowOutline = grid.GetRowOutlineLevel(row);
                if (rowOutline == 0 && oldRow != null) rowOutline = oldRow.OutlineLevel;
                bool isCollapsed = grid.IsRowInCollapsedGroup(row) || (oldRow?.Collapsed ?? false);
                resultRows.Add(new SpreadsheetRow(row, cells, rowHeight, isHidden, rowOutline, isCollapsed));
            }
            var colWidths = grid.GetExplicitColumnWidths() ?? source?.ColumnWidths;
            var resultColumns = new List<SpreadsheetColumn>();
            var originalCols = source?.Columns?.ToDictionary(c => c.ColumnIndex);
            for (int col = 0; col < columns; col++)
            {
                SpreadsheetColumn? oldCol = null;
                originalCols?.TryGetValue(col, out oldCol);
                int colOutline = grid.GetColumnOutlineLevel(col);
                if (colOutline == 0 && oldCol != null) colOutline = oldCol.OutlineLevel;
                bool isColHidden = grid.IsColumnHidden(col) || (oldCol?.Hidden ?? false);
                bool isColCollapsed = grid.IsColumnInCollapsedGroup(col) || (oldCol?.Collapsed ?? false);
                double? w = (col < colWidths?.Count) ? colWidths[col] : oldCol?.Width;

                if (colOutline > 0 || isColHidden || isColCollapsed || oldCol != null)
                {
                    resultColumns.Add(new SpreadsheetColumn(col, w, isColHidden, colOutline, isColCollapsed));
                }
            }

            worksheets.Add(new SpreadsheetWorksheet(entry.Name, resultRows, colWidths)
            {
                Columns = resultColumns.Count > 0 ? resultColumns : (source?.Columns),
                SummaryRowsBelow = grid.SummaryRowsBelow,
                SummaryColumnsRight = grid.SummaryColumnsRight,
                ShowGridlines = grid.ShowGridlines,
                FrozenRows = grid.FrozenRows,
                FrozenColumns = grid.FrozenColumns,
                SplitRows = grid.SplitRows,
                SplitColumns = grid.SplitColumns,
                ConditionalFormats = grid.ConditionalFormats.Count > 0 ? grid.ConditionalFormats.ToList() : null,
                DataValidations = grid.DataValidations.Count > 0 ? grid.DataValidations.ToList() : null,
                AutoFilter = grid.AutoFilter,
            });
        }
        return new SpreadsheetWorkbook(worksheets);
    }

    public static RichTextDocument ToRichTextDocument(SheetModel sheet, WorksheetPrintSetup? printSetup = null)
    {
        var setup = printSetup ?? sheet.PrintSetup;
        var workbook = ToSpreadsheetWorkbook(sheet);
        return workbook.ToRichTextDocument(
            pageGeometry: setup.ToPageGeometry(),
            printsSheets: true,
            printGridlines: setup.PrintGridlines);
    }

    public static void Save(SheetModel sheet, Stream output, string name)
    {
        string ext = Path.GetExtension(name);
        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            SheetCsvSerializer.Save(sheet, output);
            return;
        }
        if (ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            SheetCsvSerializer.Save(sheet, output, new Broiler.Documents.Csv.CsvWriteOptions(delimiter: '\t'));
            return;
        }
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase) || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            SheetMarkdownSerializer.Save(sheet, output);
            return;
        }
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var richDoc = ToRichTextDocument(sheet);
            var codec = new PdfDocumentCodec();
            var writeResult = codec.Write(richDoc, output);
            if (writeResult.Status != DocumentResultStatus.Success)
                throw new InvalidDataException("PDF export failed: " + string.Join("; ", writeResult.Diagnostics.Select(d => d.Message)));
            return;
        }
        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            var richDoc = ToRichTextDocument(sheet);
            var codec = new HtmlDocumentCodec();
            var writeResult = codec.Write(richDoc, output);
            if (writeResult.Status != DocumentResultStatus.Success)
                throw new InvalidDataException("HTML export failed: " + string.Join("; ", writeResult.Diagnostics.Select(d => d.Message)));
            return;
        }

        var workbook = ToSpreadsheetWorkbook(sheet);
        var written = Codec(name).Write(workbook.ToRichTextDocument(), output);
        if (written.Status != DocumentResultStatus.Success)
        {
            if (written.Diagnostics.Any(d => d.Code == "ods.number-format"))
                throw new NotSupportedException("ODS export does not support number format: " + string.Join("; ", written.Diagnostics.Where(d => d.Code == "ods.number-format").Select(d => d.Message)));
            throw new InvalidDataException("Workbook export was incomplete: " + string.Join("; ", written.Diagnostics.Select(d => d.Message)));
        }
    }

    public static void Save(SheetModel sheet, string path)
    {
        // A failed export must not truncate the destination.
        using var staged = new MemoryStream();
        Save(sheet, staged, path);
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, staged.ToArray());
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

using Broiler.Documents;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Ods;
using Broiler.Documents.Xlsx;

namespace Broiler.Cells.Core;

/// <summary>Basic CSV, XLSX and ODS import/export for the Cells editing grid.</summary>
public static class SheetDocumentSerializer
{
    public static bool IsWorkbook(string name) => Path.GetExtension(name).ToLowerInvariant() is ".xlsx" or ".ods";

    private static DocumentCodec Codec(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xlsx" => new XlsxDocumentCodec(),
        ".ods" => new OdsDocumentCodec(),
        _ => throw new NotSupportedException("Choose CSV, XLSX or ODS."),
    };

    public static SheetModel Load(string path)
    {
        using var input = File.OpenRead(path);
        return Load(input, path);
    }

    public static SheetModel Load(Stream input, string name, DocumentReadOptions? options = null)
    {
        if (Path.GetExtension(name).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return SheetCsvSerializer.Load(input, rejectDataLoss: true);
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
                throw new InvalidDataException("Workbook exceeds the 100 row / 100 column limit. No worksheet was truncated.");
        if (Path.GetExtension(name).Equals(".ods", StringComparison.OrdinalIgnoreCase)) workbook = WorkbookFormatting.RestoreOds(package, workbook);
        WorkbookModel? editing = null;
        foreach (var worksheet in workbook.Sheets)
        {
            var grid = new SheetModel(Math.Max(1, worksheet.RowCount), Math.Max(1, worksheet.ColumnCount));
            foreach (var row in worksheet.Rows)
                foreach (var cell in row.Cells)
                    grid[cell.Address.Row, cell.Address.Column] = cell.CachedText;
            grid.ImportedWorksheet = worksheet;
            if (editing is null) editing = new WorkbookModel(grid, worksheet.Name);
            else editing.Add(worksheet.Name, grid);
        }
        editing!.Select(0);
        var sheet = editing.SelectedSheet.Sheet;
        sheet.ImportNotice = $"Basic import: editing {editing.SelectedSheet.Name} (first of {workbook.Sheets.Count} worksheets). Supported same-worksheet formulas recalculate; unsupported formulas show errors. Use Save As."
            + (result.Diagnostics.Count > 0 ? " " + string.Join("; ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)) : string.Empty);
        return sheet;
    }

    public static void Save(SheetModel sheet, Stream output, string name)
    {
        if (Path.GetExtension(name).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            SheetCsvSerializer.Save(sheet, output);
            return;
        }
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
                    if (grid.HasEditedContent(row, col))
                    {
                        var content = grid.GetContent(row, col);
                        cells.Add(new SpreadsheetCell(address, content.Value, formula: content.Formula,
                            numberFormat: content.NumberFormat, cachedText: content.Value.IsEmpty && content.Formula is not null ? string.Empty : text,
                            columnSpan: old?.ColumnSpan ?? 1, rowSpan: old?.RowSpan ?? 1, shading: old?.Shading ?? default));
                    }
                    else if (old is not null && text == old.CachedText) cells.Add(old);
                    else
                        cells.Add(new SpreadsheetCell(address, CellValue.FromText(text), cachedText: text));
                }
                SpreadsheetRow? oldRow = null;
                originalRows?.TryGetValue(row, out oldRow);
                resultRows.Add(new SpreadsheetRow(row, cells, oldRow?.Height));
            }
            worksheets.Add(new SpreadsheetWorksheet(entry.Name, resultRows, source?.ColumnWidths));
        }
        using var staged = new MemoryStream();
        var written = Codec(name).Write(new SpreadsheetWorkbook(worksheets).ToRichTextDocument(), staged);
        if (written.Status != DocumentResultStatus.Success)
            throw new InvalidDataException("Workbook export was incomplete: " + string.Join("; ", written.Diagnostics.Select(d => d.Message)));
        WorkbookFormatting.Apply(staged, name, new SpreadsheetWorkbook(worksheets));
        staged.Position = 0;
        staged.CopyTo(output);
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

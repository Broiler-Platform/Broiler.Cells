using System.Text;
using Broiler.Documents;
using Broiler.Documents.Csv;
using Broiler.Documents.Model;

namespace Broiler.Cells.Core;

/// <summary>
/// Serializes and deserializes <see cref="SheetModel"/> spreadsheets to and from CSV
/// format using <see cref="CsvDocumentCodec"/>.
/// </summary>
public static class SheetCsvSerializer
{
    /// <summary>Serializes a sheet model to a CSV stream.</summary>
    public static void Save(SheetModel sheet, Stream destination, CsvWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(destination);

        var paragraphs = new List<RichTextParagraph>(sheet.RowCount * sheet.ColumnCount);
        var rows = new List<TableRow>(sheet.RowCount);

        for (int r = 0; r < sheet.RowCount; r++)
        {
            var cells = new List<TableCell>(sheet.ColumnCount);
            for (int c = 0; c < sheet.ColumnCount; c++)
            {
                string text = sheet[r, c];
                int start = paragraphs.Count;
                paragraphs.Add(string.IsNullOrEmpty(text) ? RichTextParagraph.Empty : RichTextParagraph.Plain(text));
                cells.Add(new TableCell(start, 1, c));
            }
            rows.Add(new TableRow(cells));
        }

        var document = RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, paragraphs.Count, rows)]);

        var codec = new CsvDocumentCodec();
        codec.Write(document, destination, options ?? CsvWriteOptions.Default);
    }

    /// <summary>Serializes a sheet model to a CSV file on disk.</summary>
    public static void Save(SheetModel sheet, string filePath, CsvWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.Create(filePath);
        Save(sheet, stream, options);
    }

    /// <summary>Serializes a sheet model to a CSV string.</summary>
    public static string ToCsvString(SheetModel sheet, CsvWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Save(sheet, stream, options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Deserializes a CSV stream into a new <see cref="SheetModel"/>.</summary>
    public static SheetModel Load(
        Stream source,
        CsvReadOptions? options = null,
        int maxRows = SheetModel.MaximumRows,
        int maxColumns = SheetModel.MaximumColumns,
        bool rejectDataLoss = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var codec = new CsvDocumentCodec();
        DocumentReadResult result = codec.Read(source, options ?? CsvReadOptions.Default);
        if (rejectDataLoss && (result.Status != DocumentResultStatus.Success || result.Diagnostics.Any(d => d.Severity != DocumentDiagnosticSeverity.Info)))
            throw new InvalidDataException("CSV could not be read without loss: " + string.Join("; ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        RichTextDocument document = result.Document;

        if (document.Tables.Count > 0)
        {
            DocumentTable table = document.Tables[0];
            int maxFoundColumns = 0;
            foreach (TableRow row in table.Rows)
            {
                foreach (TableCell cell in row.Cells)
                {
                    maxFoundColumns = Math.Max(maxFoundColumns, cell.ColumnIndex + cell.ColumnSpan);
                }
            }

            if (rejectDataLoss && (table.Rows.Count > maxRows || maxFoundColumns > maxColumns))
                throw new InvalidDataException($"CSV exceeds the {maxRows} row / {maxColumns} column sheet limit. The sheet was not truncated.");
            int rowCount = Math.Clamp(table.Rows.Count, 1, maxRows);
            int colCount = Math.Clamp(maxFoundColumns, 1, maxColumns);

            var sheet = new SheetModel(rowCount, colCount, maxRows: maxRows, maxColumns: maxColumns);

            for (int r = 0; r < rowCount; r++)
            {
                TableRow row = table.Rows[r];
                foreach (TableCell cell in row.Cells)
                {
                    if (cell.ColumnIndex < colCount)
                    {
                        var cellText = new StringBuilder();
                        for (int p = 0; p < cell.ParagraphCount && cell.ParagraphIndex + p < document.Paragraphs.Count; p++)
                        {
                            if (p > 0)
                                cellText.Append('\n');
                            cellText.Append(document.Paragraphs[cell.ParagraphIndex + p].Text);
                        }
                        sheet[r, cell.ColumnIndex] = cellText.ToString();
                    }
                }
            }

            return sheet;
        }

        // Loose paragraphs fallback
        if (document.ParagraphCount > 0)
        {
            if (rejectDataLoss && document.ParagraphCount > maxRows) throw new InvalidDataException("CSV exceeds the sheet row limit.");
            int rowCount = Math.Clamp(document.ParagraphCount, 1, maxRows);
            var sheet = new SheetModel(rowCount, 1, maxRows: maxRows, maxColumns: maxColumns);
            for (int r = 0; r < rowCount; r++)
            {
                sheet[r, 0] = document.Paragraphs[r].Text;
            }
            return sheet;
        }

        return new SheetModel(1, 1, maxRows: maxRows, maxColumns: maxColumns);
    }

    /// <summary>Deserializes a CSV file on disk into a new <see cref="SheetModel"/>.</summary>
    public static SheetModel Load(
        string filePath,
        CsvReadOptions? options = null,
        int maxRows = SheetModel.MaximumRows,
        int maxColumns = SheetModel.MaximumColumns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return Load(stream, options, maxRows, maxColumns);
    }

    /// <summary>Deserializes a CSV string into a new <see cref="SheetModel"/>.</summary>
    public static SheetModel FromCsvString(
        string csvText,
        CsvReadOptions? options = null,
        int maxRows = SheetModel.MaximumRows,
        int maxColumns = SheetModel.MaximumColumns)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        byte[] bytes = Encoding.UTF8.GetBytes(csvText);
        using var stream = new MemoryStream(bytes);
        return Load(stream, options, maxRows, maxColumns);
    }
}

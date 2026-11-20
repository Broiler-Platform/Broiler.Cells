using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Cells.Core;

/// <summary>
/// Serializes and deserializes <see cref="SheetModel"/> spreadsheets to and from Markdown table format.
/// </summary>
public static class SheetMarkdownSerializer
{
    public static void Save(SheetModel sheet, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(destination);

        int lastRow = 0;
        int lastCol = 0;
        for (int r = 0; r < sheet.RowCount; r++)
        {
            for (int c = 0; c < sheet.ColumnCount; c++)
            {
                if (!string.IsNullOrEmpty(sheet[r, c]))
                {
                    lastRow = Math.Max(lastRow, r);
                    lastCol = Math.Max(lastCol, c);
                }
            }
        }

        int rows = lastRow + 1;
        int cols = lastCol + 1;

        using var writer = new StreamWriter(destination, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);

        // Header row
        writer.Write("|");
        for (int c = 0; c < cols; c++)
        {
            string val = sheet[0, c];
            string colLabel = string.IsNullOrEmpty(val) ? CellPosition.GetColumnLabel(c) : EscapeMarkdown(val);
            writer.Write($" {colLabel} |");
        }
        writer.WriteLine();

        // Alignment / separator row
        writer.Write("|");
        for (int c = 0; c < cols; c++)
        {
            var content = sheet.GetContent(0, c);
            string sep = content.Alignment switch
            {
                TextAlignment.Right => " ---: |",
                TextAlignment.Center => " :---: |",
                _ => " :--- |",
            };
            writer.Write(sep);
        }
        writer.WriteLine();

        // Data rows (rows 1 to rows - 1)
        for (int r = 1; r < rows; r++)
        {
            writer.Write("|");
            for (int c = 0; c < cols; c++)
            {
                string val = sheet[r, c];
                writer.Write($" {EscapeMarkdown(val)} |");
            }
            writer.WriteLine();
        }

        writer.Flush();
    }

    public static void Save(SheetModel sheet, string filePath)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.Create(filePath);
        Save(sheet, stream);
    }

    public static string ToMarkdownString(SheetModel sheet)
    {
        using var stream = new MemoryStream();
        Save(sheet, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static SheetModel Load(Stream source, int maxRows = SheetModel.MaximumRows, int maxColumns = SheetModel.MaximumColumns)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
        var tableRows = new List<List<string>>();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || !line.Contains('|'))
                continue;

            // Check if this is a separator row e.g. | --- | :---: | ---: |
            string withoutPipes = line.Replace("|", "").Replace("-", "").Replace(":", "").Trim();
            if (withoutPipes.Length == 0 && tableRows.Count > 0)
                continue;

            string inner = line;
            if (inner.StartsWith('|')) inner = inner[1..];
            if (inner.EndsWith('|')) inner = inner[..^1];

            string[] cells = inner.Split('|');
            var rowValues = new List<string>(cells.Length);
            foreach (string cell in cells)
            {
                rowValues.Add(UnescapeMarkdown(cell.Trim()));
            }
            tableRows.Add(rowValues);
        }

        if (tableRows.Count == 0)
            return new SheetModel(1, 1, maxRows: maxRows, maxColumns: maxColumns);

        int rowCount = Math.Clamp(tableRows.Count, 1, maxRows);
        int colCount = 1;
        for (int i = 0; i < rowCount; i++)
            colCount = Math.Max(colCount, tableRows[i].Count);
        colCount = Math.Clamp(colCount, 1, maxColumns);

        var sheet = new SheetModel(rowCount, colCount, maxRows: maxRows, maxColumns: maxColumns);
        for (int r = 0; r < rowCount; r++)
        {
            var rowList = tableRows[r];
            for (int c = 0; c < Math.Min(colCount, rowList.Count); c++)
            {
                string text = rowList[c];
                if (!string.IsNullOrEmpty(text))
                    sheet.SetInput(r, c, text);
            }
        }

        return sheet;
    }

    public static SheetModel Load(string filePath, int maxRows = SheetModel.MaximumRows, int maxColumns = SheetModel.MaximumColumns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return Load(stream, maxRows, maxColumns);
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("|", "\\|").Replace("\r\n", "<br/>").Replace("\n", "<br/>").Replace("\r", "<br/>");
    }

    private static string UnescapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\\|", "|").Replace("<br/>", "\n").Replace("<br>", "\n");
    }
}

using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;

namespace Broiler.Cells.Core;

/// <summary>Bridges formatting omissions in the published preview.29 spreadsheet codecs.</summary>
internal static class WorkbookFormatting
{
    private static readonly XNamespace X = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace O = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace T = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace S = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace N = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
    private static readonly XNamespace C = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";
    private static XDocument Read(ZipArchive zip, string name) { using var s = zip.GetEntry(name)!.Open(); return XDocument.Load(s); }
    private static void Write(ZipArchive zip, string name, XDocument document)
    {
        zip.GetEntry(name)?.Delete();
        using var stream = zip.CreateEntry(name).Open();
        document.Save(stream);
    }

    private static bool IsBasicOdsFormat(string format)
    {
        if (format is "General" or "@" or "yyyy-mm-dd" or "yyyy-mm-dd hh:mm:ss") return true;
        string number = format.StartsWith('$') ? format[1..] : format;
        if (number.EndsWith('%')) number = number[..^1];
        if (number.StartsWith("#,##0", StringComparison.Ordinal)) number = number[5..];
        else if (number.StartsWith('0')) number = number[1..];
        else return false;
        return number.Length == 0 || (number.StartsWith('.') && number.Length is > 1 and <= 17 && number[1..].All(c => c == '0'));
    }

    public static void Apply(Stream package, string name, SpreadsheetWorkbook workbook)
    {
        package.Position = 0;
        using var zip = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        if (Path.GetExtension(name).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var styles = Read(zip, "xl/styles.xml");
            var root = styles.Root!;
            var formats = root.Element(X + "numFmts");
            if (formats is null) { formats = new XElement(X + "numFmts"); root.AddFirst(formats); }
            var xfs = root.Element(X + "cellXfs")!;
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in workbook.Sheets.SelectMany(s => s.Rows).SelectMany(r => r.Cells))
            {
                if (string.IsNullOrEmpty(cell.NumberFormat) || map.ContainsKey(cell.NumberFormat)) continue;
                int id = 164 + map.Count;
                formats.Add(new XElement(X + "numFmt", new XAttribute("numFmtId", id), new XAttribute("formatCode", cell.NumberFormat)));
                int styleIndex = xfs.Elements().Count();
                xfs.Add(new XElement(X + "xf", new XAttribute("numFmtId", id), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyNumberFormat", 1)));
                map[cell.NumberFormat] = styleIndex;
            }
            formats.SetAttributeValue("count", formats.Elements().Count());
            xfs.SetAttributeValue("count", xfs.Elements().Count());
            Write(zip, "xl/styles.xml", styles);
            for (int index = 0; index < workbook.Sheets.Count; index++)
            {
                string part = $"xl/worksheets/sheet{index + 1}.xml";
                var xml = Read(zip, part);
                var cells = workbook.Sheets[index].Rows.SelectMany(r => r.Cells).ToDictionary(c => c.Address.ToString());
                foreach (var element in xml.Descendants(X + "c"))
                {
                    if (!cells.TryGetValue((string?)element.Attribute("r") ?? "", out var cell)) continue;
                    if (cell.NumberFormat is not null && map.TryGetValue(cell.NumberFormat, out int style)) element.SetAttributeValue("s", style);
                    if (cell.Value.IsError)
                    {
                        element.SetAttributeValue("t", "e");
                        element.Elements(X + "v").Remove();
                        element.Elements(X + "is").Remove();
                        element.Add(new XElement(X + "v", (cell.Value.Error == FormulaError.Cycle ? "#REF!" : cell.Value.Error == FormulaError.Syntax ? "#VALUE!" : cell.Value.Error.ToErrorString())));
                    }
                    if (cell.Formula is not null && cell.Value.IsEmpty) element.Elements(X + "v").Remove();
                }
                Write(zip, part, xml);
            }
            return;
        }
        var content = Read(zip, "content.xml");
        var automatic = content.Root!.Element(O + "automatic-styles");
        if (automatic is null) { automatic = new XElement(O + "automatic-styles"); content.Root.AddFirst(automatic); }
        var formatMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cell in workbook.Sheets.SelectMany(s => s.Rows).SelectMany(r => r.Cells))
        {
            if (string.IsNullOrEmpty(cell.NumberFormat) || formatMap.ContainsKey(cell.NumberFormat)) continue;
            string id = "CellsFormat" + formatMap.Count;
            string format = cell.NumberFormat;
            if (!IsBasicOdsFormat(format)) throw new NotSupportedException($"ODS export does not support number format '{format}'. Use XLSX or a basic numeric/date format.");
            if (format == "General") continue;
            var date = format.Contains('y') || format.Contains('d');
            var data = new XElement(N + (date ? "date-style" : format.Contains('%') ? "percentage-style" : format.Contains('$') ? "currency-style" : format == "@" ? "text-style" : "number-style"),
                new XAttribute(S + "name", id + "Data"), new XAttribute(S + "display-name", format));
            if (date)
            {
                data.Add(new XElement(N + "year", new XAttribute(N + "style", "long")), new XElement(N + "text", "-"), new XElement(N + "month", new XAttribute(N + "style", "long")), new XElement(N + "text", "-"), new XElement(N + "day", new XAttribute(N + "style", "long")));
                if (format.Contains('h')) data.Add(new XElement(N + "text", " "), new XElement(N + "hours", new XAttribute(N + "style", "long")), new XElement(N + "text", ":"), new XElement(N + "minutes", new XAttribute(N + "style", "long")), new XElement(N + "text", ":"), new XElement(N + "seconds", new XAttribute(N + "style", "long")));
            }
            else if (format == "@") data.Add(new XElement(N + "text-content"));
            else
            {
                int dot = format.IndexOf('.');
                int decimals = dot < 0 ? 0 : format.Skip(dot + 1).TakeWhile(c => c is '0' or '#').Count();
                if (format.Contains('$')) data.Add(new XElement(N + "currency-symbol", "$"));
                data.Add(new XElement(N + "number", new XAttribute(N + "decimal-places", decimals), new XAttribute(N + "min-integer-digits", 1), new XAttribute(N + "grouping", format.Contains(',') ? "true" : "false")));
                if (format.Contains('%')) data.Add(new XElement(N + "text", "%"));
            }
            automatic.Add(data, new XElement(S + "style", new XAttribute(S + "name", id), new XAttribute(S + "family", "table-cell"), new XAttribute(S + "data-style-name", id + "Data")));
            formatMap[format] = id;
        }
        int sheetIndex = 0;
        foreach (var table in content.Descendants(T + "table"))
        {
            var sheet = workbook.Sheets[sheetIndex++];
            int row = 0;
            foreach (var rowElement in table.Elements(T + "table-row"))
            {
                int column = 0;
                foreach (var element in rowElement.Elements())
                {
                    var cell = sheet[row, column++];
                    if (cell is null) continue;
                    if (cell.NumberFormat is not null && formatMap.TryGetValue(cell.NumberFormat, out var style)) element.SetAttributeValue(T + "style-name", style);
                    if (cell.Value.IsError) element.SetAttributeValue(C + "value-type", "error");
                    if (cell.Value.IsNumber && cell.NumberFormat is "yyyy-mm-dd" or "yyyy-mm-dd hh:mm:ss")
                    {
                        element.SetAttributeValue(O + "value-type", "date");
                        element.Attribute(O + "value")?.Remove();
                        element.SetAttributeValue(O + "date-value", CellDateSerial.FromSerial(cell.Value.Number).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
                    }
                }
                row++;
            }
        }
        Write(zip, "content.xml", content);
    }

    public static SpreadsheetWorkbook RestoreOds(Stream package, SpreadsheetWorkbook workbook)
    {
        package.Position = 0;
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var content = Read(zip, "content.xml");
        var documents = new List<XDocument> { content };
        if (zip.GetEntry("styles.xml") is not null) documents.Add(Read(zip, "styles.xml"));
        var dataStyles = documents.SelectMany(d => d.Descendants()).Where(e => e.Name.Namespace == N && e.Attribute(S + "name") is not null)
            .GroupBy(e => (string)e.Attribute(S + "name")!).ToDictionary(g => g.Key, g => g.Last());
        var styles = documents.SelectMany(d => d.Descendants(S + "style")).Where(e => e.Attribute(S + "name") is not null)
            .GroupBy(e => (string)e.Attribute(S + "name")!).ToDictionary(g => g.Key, g => (string?)g.Last().Attribute(S + "data-style-name"));
        var result = new List<SpreadsheetWorksheet>();
        int sheetIndex = 0;
        foreach (var table in content.Descendants(T + "table"))
        {
            var sheet = workbook.Sheets[sheetIndex++];
            var restored = sheet.Rows.SelectMany(r => r.Cells).ToDictionary(c => c.Address);
            int row = 0;
            foreach (var rowElement in table.Elements(T + "table-row"))
            {
                int repeatRows = (int?)rowElement.Attribute(T + "number-rows-repeated") ?? 1;
                int column = 0;
                foreach (var element in rowElement.Elements())
                {
                    int repeatColumns = (int?)element.Attribute(T + "number-columns-repeated") ?? 1;
                    for (int r = row; r < Math.Min(row + repeatRows, SheetModel.MaximumRows); r++)
                    for (int c = column; c < Math.Min(column + repeatColumns, SheetModel.MaximumColumns); c++)
                    {
                        var address = new CellAddress(r, c);
                        if (!restored.TryGetValue(address, out var cell)) continue;
                        string? format = cell.NumberFormat;
                        if (styles.TryGetValue((string?)element.Attribute(T + "style-name") ?? "", out var dataName) && dataName is not null && dataStyles.TryGetValue(dataName, out var data))
                        {
                            format = (string?)data.Attribute(S + "display-name");
                            if (format is null && data.Name.LocalName == "date-style") format = "yyyy-mm-dd";
                            if (format is null && data.Element(N + "number") is { } number)
                            {
                                int decimals = Math.Clamp((int?)number.Attribute(N + "decimal-places") ?? 0, 0, 16);
                                format = ((string?)number.Attribute(N + "grouping") == "true" ? "#,##0" : "0") + (decimals > 0 ? "." + new string('0', decimals) : "");
                                if (data.Name.LocalName == "percentage-style") format += "%";
                                if (data.Name.LocalName == "currency-style") format = ((string?)data.Element(N + "currency-symbol") ?? "$") + format;
                            }
                        }
                        var value = cell.Value;
                        if ((string?)element.Attribute(C + "value-type") == "error" && FormulaErrorExtensions.TryParse(cell.CachedText, out var error)) value = CellValue.FromError(error);
                        if ((string?)element.Attribute(O + "date-value") is { } dateText && DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        { value = CellValue.FromNumber(CellDateSerial.ToSerial(date)); format ??= "yyyy-mm-dd"; }
                        restored[address] = new SpreadsheetCell(address, value, cell.Formula, format, cell.CachedText, cell.ColumnSpan, cell.RowSpan, cell.Shading);
                    }
                    column += repeatColumns;
                }
                row += repeatRows;
            }
            result.Add(new SpreadsheetWorksheet(sheet.Name, sheet.Rows.Select(r => new SpreadsheetRow(r.RowIndex, r.Cells.Select(c => restored[c.Address]).ToArray(), r.Height)).ToArray(), sheet.ColumnWidths));
        }
        return new SpreadsheetWorkbook(result);
    }
}

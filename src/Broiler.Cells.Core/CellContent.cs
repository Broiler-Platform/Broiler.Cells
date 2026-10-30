using System.Globalization;
using Broiler.Documents.Model;

namespace Broiler.Cells.Core;

public enum CellEntryMode { Auto, Text, Number, Boolean, Date, Error, Formula }

/// <summary>Typed cell content, separate from its rendered display string.</summary>
public sealed record CellContent(CellValue Value, string Input, string? Formula = null, string? NumberFormat = null, string? CachedText = null)
{
    public string Display => Formula is not null ? CachedText ?? Input : CellNumberFormatter.Format(Value, NumberFormat, CultureInfo.InvariantCulture).Text;

    internal static CellContent Parse(string input, CellEntryMode mode, string? format)
    {
        if (input.Length == 0) return new(CellValue.Empty, input, NumberFormat: format);
        if (mode == CellEntryMode.Text) return new(CellValue.FromText(input), input, NumberFormat: "@");
        if (mode == CellEntryMode.Formula)
        {
            if (!input.StartsWith('=') || input.Length < 2) throw new FormatException("Formula entry must begin with = and contain an expression. Formulas are not calculated yet.");
            return new(CellValue.Empty, input, input, format);
        }
        if (mode == CellEntryMode.Auto && input.StartsWith('\'')) return new(CellValue.FromText(input[1..]), input, NumberFormat: "@");
        if ((mode is CellEntryMode.Auto or CellEntryMode.Boolean) && bool.TryParse(input, out var boolean))
            return new(CellValue.FromBoolean(boolean), input, NumberFormat: format);
        if ((mode is CellEntryMode.Auto or CellEntryMode.Error) && FormulaErrorExtensions.TryParse(input, out var error))
            return new(CellValue.FromError(error), input, NumberFormat: format);
        if ((mode is CellEntryMode.Auto or CellEntryMode.Date) && DateTime.TryParseExact(input,
            new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new(CellValue.FromNumber(CellDateSerial.ToSerial(date)), input, NumberFormat: format is not null && (format.Contains('y') || format.Contains('d')) ? format : input.Contains('T') ? "yyyy-mm-dd hh:mm:ss" : "yyyy-mm-dd");
        bool leadingZero = input.Length > 1 && input[0] == '0' && char.IsDigit(input[1]);
        if ((mode == CellEntryMode.Number || (mode == CellEntryMode.Auto && !leadingZero))
            && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
            return new(CellValue.FromNumber(number), input, NumberFormat: format == "@" ? null : format);
        if (mode == CellEntryMode.Auto) return new(CellValue.FromText(input), input, NumberFormat: format);
        throw new FormatException($"Invalid {mode} value. Use invariant numbers, TRUE/FALSE, ISO dates, or a supported spreadsheet error.");
    }
}

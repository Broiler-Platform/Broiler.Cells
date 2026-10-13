namespace Broiler.Cells.Core;

/// <summary>Identifies a cell by zero-based row and column indices and provides coordinate address formatting.</summary>
public readonly record struct CellPosition(int Row, int Column)
{
    /// <summary>Formats this cell position as a spreadsheet address (e.g. A1, B2, CV100).</summary>
    public string ToAddress() => FormatAddress(Row, Column);

    /// <summary>Formats zero-based row and column indices as a spreadsheet address.</summary>
    public static string FormatAddress(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        return $"{GetColumnLabel(column)}{row + 1}";
    }

    /// <summary>Converts a zero-based column index to its letter representation (e.g. 0 -> A, 25 -> Z, 26 -> AA, 51 -> AZ, 52 -> BA, 99 -> CV).</summary>
    public static string GetColumnLabel(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        int dividend = column + 1;
        string label = string.Empty;
        while (dividend > 0)
        {
            int modulo = (dividend - 1) % 26;
            label = (char)('A' + modulo) + label;
            dividend = (dividend - 1) / 26;
        }
        return label;
    }

    /// <summary>Tries to parse a spreadsheet address (e.g. A1, B2, CV100) into zero-based coordinates.</summary>
    public static bool TryParseAddress(string? address, out CellPosition position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(address))
            return false;

        address = address.Trim().ToUpperInvariant();
        int letterEnd = 0;
        while (letterEnd < address.Length && address[letterEnd] >= 'A' && address[letterEnd] <= 'Z')
            letterEnd++;

        if (letterEnd == 0 || letterEnd == address.Length)
            return false;

        if (!int.TryParse(address[letterEnd..], out int rowNumber) || rowNumber < 1)
            return false;

        int columnIndex = 0;
        for (int i = 0; i < letterEnd; i++)
        {
            columnIndex = columnIndex * 26 + (address[i] - 'A' + 1);
        }
        columnIndex--; // convert 1-based to 0-based

        position = new CellPosition(rowNumber - 1, columnIndex);
        return true;
    }
}

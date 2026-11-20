using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;

namespace Broiler.Cells.Core;

/// <summary>
/// Direction in which an auto-fill operation is performed.
/// </summary>
public enum AutoFillDirection
{
    Down,
    Right,
    Up,
    Left
}

/// <summary>
/// Operational mode controlling how values, sequences, and formats are transferred.
/// </summary>
public enum AutoFillMode
{
    Default,
    CopyCells,
    FillSeries,
    FillFormattingOnly,
    FillWithoutFormatting
}

/// <summary>
/// High-performance engine for spreadsheet auto-fill, series generation, and formula reference shifting.
/// </summary>
public static class AutoFillEngine
{
    private static readonly Regex FormulaRefRegex = new(
        @"(""[^""]*"")|(?<![A-Za-z0-9_])(?:(?<sheet>'[^']+'|[A-Za-z0-9_]+)!)?(?<colFixed>\$?)(?<col>[A-Za-z]{1,3})(?<rowFixed>\$?)(?<row>[1-9][0-9]{0,6})(?!\s*\()(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    private static readonly Regex AlphanumericSeriesRegex = new(
        @"^(?<prefix>.*?)((?<num>\d+))(?<suffix>\s*)$",
        RegexOptions.Compiled);

    private static readonly string[] DaysFull =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] DaysAbbr =
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly string[] MonthsFull =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] MonthsAbbr =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>
    /// Shifts cell references in a formula expression by the specified row and column deltas.
    /// Absolute references ($A$1, $A1, A$1) have unanchored dimensions adjusted and anchored dimensions kept intact.
    /// </summary>
    public static string ShiftFormulaReferences(string formula, int deltaRow, int deltaCol)
    {
        if (string.IsNullOrEmpty(formula) || (deltaRow == 0 && deltaCol == 0))
            return formula;

        return FormulaRefRegex.Replace(formula, match =>
        {
            if (match.Groups[1].Success)
                return match.Value; // String literal, leave untouched

            string sheetPrefix = match.Groups["sheet"].Success ? match.Groups["sheet"].Value + "!" : string.Empty;
            bool isColFixed = match.Groups["colFixed"].Length > 0;
            bool isRowFixed = match.Groups["rowFixed"].Length > 0;
            string colStr = match.Groups["col"].Value;
            string rowStr = match.Groups["row"].Value;

            if (!CellAddress.TryLettersToColumn(colStr, out int colIndex) || colIndex >= SheetModel.MaximumColumns)
                return match.Value;

            if (!int.TryParse(rowStr, NumberStyles.None, CultureInfo.InvariantCulture, out int rowNumber) || rowNumber < 1)
                return match.Value;

            int rowIndex = rowNumber - 1;

            int newCol = isColFixed ? colIndex : colIndex + deltaCol;
            int newRow = isRowFixed ? rowIndex : rowIndex + deltaRow;

            if (newCol < 0 || newCol >= SheetModel.MaximumColumns || newRow < 0 || newRow >= SheetModel.MaximumRows)
                return sheetPrefix + "#REF!";

            string colLetters = CellAddress.ColumnToLetters(newCol);
            string colPart = (isColFixed ? "$" : string.Empty) + colLetters;
            string rowPart = (isRowFixed ? "$" : string.Empty) + (newRow + 1);

            return sheetPrefix + colPart + rowPart;
        });
    }

    /// <summary>
    /// Performs an auto-fill from <paramref name="sourceRange"/> to <paramref name="targetRange"/>.
    /// </summary>
    public static bool Fill(SheetModel sheet, CellRangeAddress sourceRange, CellRangeAddress targetRange, AutoFillMode mode = AutoFillMode.Default)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        // Determine direction and bounds
        if (!ResolveFillBounds(sourceRange, targetRange, out var direction, out int destR0, out int destR1, out int destC0, out int destC1))
            return false;

        bool copyFormat = mode != AutoFillMode.FillWithoutFormatting;
        bool fillFormattingOnly = mode == AutoFillMode.FillFormattingOnly;

        if (direction is AutoFillDirection.Down or AutoFillDirection.Up)
        {
            int colStart = Math.Max(sourceRange.StartCol, destC0);
            int colEnd = Math.Min(sourceRange.EndCol, destC1);

            for (int col = colStart; col <= colEnd; col++)
            {
                var sourceCells = new List<(int Row, int Col, CellContent Content)>();
                for (int r = sourceRange.StartRow; r <= sourceRange.EndRow; r++)
                {
                    sourceCells.Add((r, col, sheet.GetContent(r, col)));
                }

                var targetCoords = new List<(int Row, int Col)>();
                if (direction == AutoFillDirection.Down)
                {
                    for (int r = destR0; r <= destR1; r++)
                        targetCoords.Add((r, col));
                }
                else
                {
                    // Upward fill: order from closest to source to farthest
                    for (int r = destR1; r >= destR0; r--)
                        targetCoords.Add((r, col));
                }

                FillSlice(sheet, sourceCells, targetCoords, direction, mode, copyFormat, fillFormattingOnly);
            }
        }
        else // Right or Left
        {
            int rowStart = Math.Max(sourceRange.StartRow, destR0);
            int rowEnd = Math.Min(sourceRange.EndRow, destR1);

            for (int row = rowStart; row <= rowEnd; row++)
            {
                var sourceCells = new List<(int Row, int Col, CellContent Content)>();
                for (int c = sourceRange.StartCol; c <= sourceRange.EndCol; c++)
                {
                    sourceCells.Add((row, c, sheet.GetContent(row, c)));
                }

                var targetCoords = new List<(int Row, int Col)>();
                if (direction == AutoFillDirection.Right)
                {
                    for (int c = destC0; c <= destC1; c++)
                        targetCoords.Add((row, c));
                }
                else
                {
                    // Leftward fill: order from closest to source to farthest
                    for (int c = destC1; c >= destC0; c--)
                        targetCoords.Add((row, c));
                }

                FillSlice(sheet, sourceCells, targetCoords, direction, mode, copyFormat, fillFormattingOnly);
            }
        }

        return true;
    }

    /// <summary>
    /// Executes Excel quick-fill down: automatically detects adjacent column data height and fills down to match.
    /// Returns the resulting target range if successful, or null if no adjacent data was found.
    /// </summary>
    public static CellRangeAddress? QuickFillDown(SheetModel sheet, CellRangeAddress sourceRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        int scanCol = -1;
        if (sourceRange.StartCol > 0 && !string.IsNullOrEmpty(sheet[sourceRange.StartRow, sourceRange.StartCol - 1]))
        {
            scanCol = sourceRange.StartCol - 1;
        }
        else if (sourceRange.EndCol + 1 < sheet.ColumnCount && !string.IsNullOrEmpty(sheet[sourceRange.StartRow, sourceRange.EndCol + 1]))
        {
            scanCol = sourceRange.EndCol + 1;
        }

        if (scanCol < 0)
            return null;

        int targetEndRow = sourceRange.EndRow;
        for (int r = sourceRange.EndRow + 1; r < sheet.RowCount; r++)
        {
            if (string.IsNullOrEmpty(sheet[r, scanCol]))
                break;
            targetEndRow = r;
        }

        if (targetEndRow <= sourceRange.EndRow)
            return null;

        var targetRange = new CellRangeAddress(
            new CellAddress(sourceRange.StartRow, sourceRange.StartCol),
            new CellAddress(targetEndRow, sourceRange.EndCol));

        bool filled = Fill(sheet, sourceRange, targetRange, AutoFillMode.Default);
        return filled ? targetRange : null;
    }

    private static bool ResolveFillBounds(
        CellRangeAddress src,
        CellRangeAddress target,
        out AutoFillDirection direction,
        out int destR0, out int destR1,
        out int destC0, out int destC1)
    {
        direction = AutoFillDirection.Down;
        destR0 = destR1 = destC0 = destC1 = 0;

        if (target.EndRow > src.EndRow)
        {
            direction = AutoFillDirection.Down;
            destR0 = src.EndRow + 1;
            destR1 = target.EndRow;
            destC0 = Math.Max(src.StartCol, target.StartCol);
            destC1 = Math.Min(src.EndCol, target.EndCol);
            return destR0 <= destR1 && destC0 <= destC1;
        }

        if (target.EndCol > src.EndCol)
        {
            direction = AutoFillDirection.Right;
            destC0 = src.EndCol + 1;
            destC1 = target.EndCol;
            destR0 = Math.Max(src.StartRow, target.StartRow);
            destR1 = Math.Min(src.EndRow, target.EndRow);
            return destC0 <= destC1 && destR0 <= destR1;
        }

        if (target.StartRow < src.StartRow)
        {
            direction = AutoFillDirection.Up;
            destR0 = target.StartRow;
            destR1 = src.StartRow - 1;
            destC0 = Math.Max(src.StartCol, target.StartCol);
            destC1 = Math.Min(src.EndCol, target.EndCol);
            return destR0 <= destR1 && destC0 <= destC1;
        }

        if (target.StartCol < src.StartCol)
        {
            direction = AutoFillDirection.Left;
            destC0 = target.StartCol;
            destC1 = src.StartCol - 1;
            destR0 = Math.Max(src.StartRow, target.StartRow);
            destR1 = Math.Min(src.EndRow, target.EndRow);
            return destC0 <= destC1 && destR0 <= destR1;
        }

        return false;
    }

    private static void FillSlice(
        SheetModel sheet,
        List<(int Row, int Col, CellContent Content)> sourceCells,
        List<(int Row, int Col)> targetCoords,
        AutoFillDirection direction,
        AutoFillMode mode,
        bool copyFormat,
        bool fillFormattingOnly)
    {
        int n = sourceCells.Count;
        int m = targetCoords.Count;
        if (n == 0 || m == 0) return;

        bool isReverse = direction is AutoFillDirection.Up or AutoFillDirection.Left;

        // If formatting only, copy styling and return
        if (fillFormattingOnly)
        {
            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // If explicitly copying cells, duplicate content
        if (mode == AutoFillMode.CopyCells)
        {
            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                sheet.SetInput(dst.Row, dst.Col, src.Content.Input, CellEntryMode.Auto, force: true);
                if (copyFormat)
                    ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // 1. Formula sequence
        if (sourceCells.Any(s => s.Content.Formula is not null))
        {
            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                if (src.Content.Formula is { } formula)
                {
                    int deltaRow = dst.Row - src.Row;
                    int deltaCol = dst.Col - src.Col;
                    string shifted = ShiftFormulaReferences(formula, deltaRow, deltaCol);
                    sheet.SetInput(dst.Row, dst.Col, shifted, CellEntryMode.Formula, force: true);
                }
                else
                {
                    sheet.SetInput(dst.Row, dst.Col, src.Content.Input, CellEntryMode.Auto, force: true);
                }

                if (copyFormat)
                    ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // 2. Day of Week sequence
        if (TryExtractDays(sourceCells, out var dayIndices, out bool isDayAbbr, out StringCasing dayCasing))
        {
            int step = 1;
            if (n > 1)
            {
                int diff = dayIndices[1] - dayIndices[0];
                step = ((diff % 7) + 7) % 7;
            }
            if (isReverse)
                step = -step;

            int baseDay = isReverse ? dayIndices[0] : dayIndices[^1];
            string[] names = isDayAbbr ? DaysAbbr : DaysFull;

            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                int nextDay = ((baseDay + step * (k + 1)) % 7 + 7) % 7;
                string dayStr = ApplyCasing(names[nextDay], dayCasing);

                sheet.SetInput(dst.Row, dst.Col, dayStr, CellEntryMode.Text, force: true);
                if (copyFormat)
                    ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // 3. Month of Year sequence
        if (TryExtractMonths(sourceCells, out var monthIndices, out bool isMonthAbbr, out StringCasing monthCasing))
        {
            int step = 1;
            if (n > 1)
            {
                int diff = monthIndices[1] - monthIndices[0];
                step = ((diff % 12) + 12) % 12;
            }
            if (isReverse)
                step = -step;

            int baseMonth = isReverse ? monthIndices[0] : monthIndices[^1];
            string[] names = isMonthAbbr ? MonthsAbbr : MonthsFull;

            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                int nextMonth = ((baseMonth + step * (k + 1)) % 12 + 12) % 12;
                string monthStr = ApplyCasing(names[nextMonth], monthCasing);

                sheet.SetInput(dst.Row, dst.Col, monthStr, CellEntryMode.Text, force: true);
                if (copyFormat)
                    ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // 4. Date sequence (ISO string or date serial)
        if (TryExtractDates(sourceCells, out var dates))
        {
            double daysStep = 1.0;
            if (n > 1)
            {
                daysStep = (dates[^1] - dates[0]).TotalDays / (n - 1);
            }
            if (isReverse)
                daysStep = -daysStep;

            DateTime baseDate = isReverse ? dates[0] : dates[^1];

            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                DateTime nextDate = baseDate.AddDays(daysStep * (k + 1));
                string dateStr = nextDate.ToString(src.Content.Input.Contains('T') ? "yyyy-MM-ddTHH:mm:ss" : "yyyy-MM-dd", CultureInfo.InvariantCulture);

                sheet.SetInput(dst.Row, dst.Col, dateStr, CellEntryMode.Date, force: true);
                if (copyFormat)
                    ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // 5. Alphanumeric pattern with trailing digits (e.g. "Item 1", "Q1", "Test-001")
        if (TryExtractAlphanumeric(sourceCells, out string prefix, out string suffix, out var nums, out int numDigits))
        {
            long step = 1;
            if (n > 1)
            {
                step = (nums[^1] - nums[0]) / (n - 1);
            }
            if (isReverse)
                step = -step;

            long baseNum = isReverse ? nums[0] : nums[^1];

            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                long nextNum = baseNum + step * (k + 1);
                string formattedNum = nextNum.ToString("D" + numDigits, CultureInfo.InvariantCulture);
                string text = prefix + formattedNum + suffix;

                sheet.SetInput(dst.Row, dst.Col, text, CellEntryMode.Auto, force: true);
                if (copyFormat)
                    ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // 6. Numeric series
        if (TryExtractNumbers(sourceCells, out var numbers))
        {
            if (n == 1 && mode != AutoFillMode.FillSeries)
            {
                // Single number default in Excel: copy cell value
                for (int k = 0; k < m; k++)
                {
                    var src = sourceCells[0];
                    var dst = targetCoords[k];
                    sheet.SetInput(dst.Row, dst.Col, src.Content.Input, CellEntryMode.Number, force: true);
                    if (copyFormat)
                        ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
                }
                return;
            }

            double step = 1.0;
            if (n > 1)
            {
                step = (numbers[^1] - numbers[0]) / (n - 1);
            }
            if (isReverse)
                step = -step;

            double baseVal = isReverse ? numbers[0] : numbers[^1];

            for (int k = 0; k < m; k++)
            {
                var src = sourceCells[k % n];
                var dst = targetCoords[k];
                double nextVal = baseVal + step * (k + 1);
                string numStr = FormatNumericValue(nextVal, src.Content);

                sheet.SetInput(dst.Row, dst.Col, numStr, CellEntryMode.Number, force: true);
                if (copyFormat)
                    ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
            }
            return;
        }

        // 7. General fallback: Repeat source cells cyclically
        for (int k = 0; k < m; k++)
        {
            var src = sourceCells[k % n];
            var dst = targetCoords[k];
            sheet.SetInput(dst.Row, dst.Col, src.Content.Input, CellEntryMode.Auto, force: true);
            if (copyFormat)
                ApplyFormatting(sheet, dst.Row, dst.Col, src.Content, src.Row, src.Col);
        }
    }

    private static void ApplyFormatting(SheetModel sheet, int targetRow, int targetCol, CellContent sourceContent, int srcRow, int srcCol)
    {
        sheet.SetCellFont(targetRow, targetCol, sourceContent.Font);
        sheet.SetCellShading(targetRow, targetCol, sourceContent.Shading);
        sheet.SetCellBorders(targetRow, targetCol, sourceContent.Borders);
        sheet.SetCellAlignment(targetRow, targetCol, sourceContent.Alignment);
        sheet.SetCellVerticalAlignment(targetRow, targetCol, sourceContent.VerticalAlignment);
        sheet.SetCellWrapText(targetRow, targetCol, sourceContent.WrapText);
        sheet.SetNumberFormat(targetRow, targetCol, sourceContent.NumberFormat);

        // Propagate data validation if present
        if (sheet.GetDataValidation(srcRow, srcCol) is { } dv)
        {
            var targetAddr = new CellAddress(targetRow, targetCol);
            bool alreadyCovered = dv.Ranges.Any(r => r.Contains(targetAddr));
            if (!alreadyCovered)
            {
                var newRanges = new List<CellRangeAddress>(dv.Ranges) { new(targetAddr, targetAddr) };
                sheet.RemoveDataValidation(dv);
                sheet.AddDataValidation(dv with { Ranges = newRanges });
            }
        }
    }

    private static string FormatNumericValue(double val, CellContent template)
    {
        if (Math.Abs(val - Math.Round(val)) < 1e-9)
        {
            return ((long)Math.Round(val)).ToString(CultureInfo.InvariantCulture);
        }
        return val.ToString("G", CultureInfo.InvariantCulture);
    }

    private static bool TryExtractNumbers(List<(int Row, int Col, CellContent Content)> cells, out List<double> numbers)
    {
        numbers = new List<double>(cells.Count);
        foreach (var c in cells)
        {
            if (c.Content.Value.Type == CellValueType.Number)
            {
                numbers.Add(c.Content.Value.Number);
            }
            else if (double.TryParse(c.Content.Input, NumberStyles.Float, CultureInfo.InvariantCulture, out double val) && double.IsFinite(val))
            {
                numbers.Add(val);
            }
            else
            {
                return false;
            }
        }
        return numbers.Count > 0;
    }

    private static bool TryExtractDates(List<(int Row, int Col, CellContent Content)> cells, out List<DateTime> dates)
    {
        dates = new List<DateTime>(cells.Count);
        foreach (var c in cells)
        {
            if (DateTime.TryParseExact(c.Content.Input, ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                dates.Add(dt);
            }
            else if (c.Content.Value.Type == CellValueType.Number && c.Content.NumberFormat is { } fmt &&
                     (fmt.Contains('y', StringComparison.OrdinalIgnoreCase) || fmt.Contains('d', StringComparison.OrdinalIgnoreCase)))
            {
                var serialDt = CellDateSerial.FromSerial(c.Content.Value.Number);
                if (serialDt > DateTime.MinValue)
                    dates.Add(serialDt);
                else
                    return false;
            }
            else
            {
                return false;
            }
        }
        return dates.Count > 0;
    }

    private static bool TryExtractDays(List<(int Row, int Col, CellContent Content)> cells, out List<int> dayIndices, out bool isAbbr, out StringCasing casing)
    {
        dayIndices = [];
        isAbbr = false;
        casing = StringCasing.Title;

        if (cells.Count == 0) return false;

        for (int i = 0; i < cells.Count; i++)
        {
            string text = cells[i].Content.Input.Trim();
            if (string.IsNullOrEmpty(text)) return false;

            int idx = Array.FindIndex(DaysFull, d => string.Equals(d, text, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                if (i == 0)
                {
                    isAbbr = false;
                    casing = DetectCasing(text);
                }
                else if (isAbbr) return false;
                dayIndices.Add(idx);
                continue;
            }

            idx = Array.FindIndex(DaysAbbr, d => string.Equals(d, text, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                if (i == 0)
                {
                    isAbbr = true;
                    casing = DetectCasing(text);
                }
                else if (!isAbbr) return false;
                dayIndices.Add(idx);
                continue;
            }

            return false;
        }

        return dayIndices.Count > 0;
    }

    private static bool TryExtractMonths(List<(int Row, int Col, CellContent Content)> cells, out List<int> monthIndices, out bool isAbbr, out StringCasing casing)
    {
        monthIndices = [];
        isAbbr = false;
        casing = StringCasing.Title;

        if (cells.Count == 0) return false;

        for (int i = 0; i < cells.Count; i++)
        {
            string text = cells[i].Content.Input.Trim();
            if (string.IsNullOrEmpty(text)) return false;

            int idx = Array.FindIndex(MonthsFull, m => string.Equals(m, text, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                if (i == 0)
                {
                    isAbbr = false;
                    casing = DetectCasing(text);
                }
                else if (isAbbr) return false;
                monthIndices.Add(idx);
                continue;
            }

            idx = Array.FindIndex(MonthsAbbr, m => string.Equals(m, text, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                if (i == 0)
                {
                    isAbbr = true;
                    casing = DetectCasing(text);
                }
                else if (!isAbbr) return false;
                monthIndices.Add(idx);
                continue;
            }

            return false;
        }

        return monthIndices.Count > 0;
    }

    private static bool TryExtractAlphanumeric(List<(int Row, int Col, CellContent Content)> cells, out string prefix, out string suffix, out List<long> nums, out int numDigits)
    {
        prefix = suffix = string.Empty;
        nums = [];
        numDigits = 1;

        if (cells.Count == 0) return false;

        for (int i = 0; i < cells.Count; i++)
        {
            var match = AlphanumericSeriesRegex.Match(cells[i].Content.Input);
            if (!match.Success) return false;

            string p = match.Groups["prefix"].Value;
            string s = match.Groups["suffix"].Value;
            string numStr = match.Groups["num"].Value;

            if (!long.TryParse(numStr, NumberStyles.None, CultureInfo.InvariantCulture, out long n))
                return false;

            if (i == 0)
            {
                prefix = p;
                suffix = s;
                numDigits = numStr.Length;
            }
            else
            {
                if (!string.Equals(prefix, p, StringComparison.Ordinal) || !string.Equals(suffix, s, StringComparison.Ordinal))
                    return false;
                numDigits = Math.Max(numDigits, numStr.Length);
            }

            nums.Add(n);
        }

        // Avoid matching plain unpadded numbers (prefix and suffix both empty and no leading zeros) - they must be handled by numeric series
        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(suffix))
        {
            if (nums[0].ToString(CultureInfo.InvariantCulture).Length == numDigits)
                return false;
        }

        return nums.Count > 0;
    }

    private enum StringCasing { Lower, Upper, Title }

    private static StringCasing DetectCasing(string s)
    {
        if (s == s.ToUpperInvariant()) return StringCasing.Upper;
        if (s == s.ToLowerInvariant()) return StringCasing.Lower;
        return StringCasing.Title;
    }

    private static string ApplyCasing(string s, StringCasing casing) => casing switch
    {
        StringCasing.Upper => s.ToUpperInvariant(),
        StringCasing.Lower => s.ToLowerInvariant(),
        _ => s
    };
}

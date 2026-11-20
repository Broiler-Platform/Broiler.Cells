using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Broiler.Documents.Model;

namespace Broiler.Cells.Core;

/// <summary>
/// A column key used to order rows during a table sort operation.
/// </summary>
public sealed record SortKey(int ColumnIndex, bool Ascending = true, bool MatchCase = false);

/// <summary>
/// High-performance engine for sorting tabular cell ranges and entire tables.
/// </summary>
public static class TableSortEngine
{
    /// <summary>
    /// Performs a quick single-column sort on the specified range (or detected region).
    /// </summary>
    public static void QuickSort(SheetModel sheet, CellRangeAddress? range, int columnIndex, bool ascending, bool hasHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        CellRangeAddress target = range ?? sheet.GetDataRegion(new CellPosition(0, columnIndex));
        SortRange(sheet, target, [new SortKey(columnIndex, ascending)], hasHeaders);
    }

    /// <summary>
    /// Sorts a range of rows according to one or more sort keys.
    /// </summary>
    public static void SortRange(SheetModel sheet, CellRangeAddress range, IReadOnlyList<SortKey> keys, bool hasHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
            return;

        int startRow = hasHeaders ? range.Start.Row + 1 : range.Start.Row;
        int endRow = range.End.Row;
        int startCol = range.Start.Column;
        int endCol = range.End.Column;

        if (startRow >= endRow)
            return;

        int rowCount = endRow - startRow + 1;
        var rowIndices = new List<int>(rowCount);
        for (int r = startRow; r <= endRow; r++)
            rowIndices.Add(r);

        // Pre-fetch cell values/texts for key columns to ensure fast comparisons
        var columnCache = new Dictionary<int, string[]>();
        foreach (var key in keys)
        {
            if (!columnCache.ContainsKey(key.ColumnIndex))
            {
                var texts = new string[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    texts[i] = sheet[startRow + i, key.ColumnIndex] ?? string.Empty;
                }
                columnCache[key.ColumnIndex] = texts;
            }
        }

        // Stable multi-key sort
        rowIndices.Sort((rA, rB) =>
        {
            int idxA = rA - startRow;
            int idxB = rB - startRow;

            foreach (var key in keys)
            {
                string textA = columnCache[key.ColumnIndex][idxA];
                string textB = columnCache[key.ColumnIndex][idxB];

                bool isBlankA = string.IsNullOrWhiteSpace(textA);
                bool isBlankB = string.IsNullOrWhiteSpace(textB);

                int cmp;
                if (isBlankA && isBlankB)
                {
                    cmp = 0;
                }
                else if (isBlankA)
                {
                    // Blanks always sort to the bottom regardless of ascending/descending
                    return 1;
                }
                else if (isBlankB)
                {
                    // Blanks always sort to the bottom regardless of ascending/descending
                    return -1;
                }
                else
                {
                    // Numeric comparison
                    if (double.TryParse(textA, NumberStyles.Any, CultureInfo.InvariantCulture, out double numA) &&
                        double.TryParse(textB, NumberStyles.Any, CultureInfo.InvariantCulture, out double numB))
                    {
                        cmp = numA.CompareTo(numB);
                    }
                    // Date comparison
                    else if (DateTime.TryParse(textA, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtA) &&
                             DateTime.TryParse(textB, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtB))
                    {
                        cmp = dtA.CompareTo(dtB);
                    }
                    // Text comparison
                    else
                    {
                        cmp = key.MatchCase
                            ? string.Compare(textA, textB, StringComparison.Ordinal)
                            : string.Compare(textA, textB, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!key.Ascending)
                        cmp = -cmp;
                }

                if (cmp != 0)
                    return cmp;
            }

            return rA.CompareTo(rB);
        });

        // Check if order actually changed
        bool orderChanged = false;
        for (int i = 0; i < rowCount; i++)
        {
            if (rowIndices[i] != startRow + i)
            {
                orderChanged = true;
                break;
            }
        }

        if (!orderChanged)
            return;

        // Snapshot existing rows data
        var rowSnapshots = new List<RowSnapshot>(rowCount);
        for (int r = startRow; r <= endRow; r++)
        {
            var cellSnapshots = new List<CellSnapshot>(endCol - startCol + 1);
            for (int c = startCol; c <= endCol; c++)
            {
                var content = sheet.GetContent(r, c);
                string text = sheet[r, c];
                cellSnapshots.Add(new CellSnapshot(c, content, text));
            }
            rowSnapshots.Add(new RowSnapshot(r, cellSnapshots, sheet.GetRowHeight(r), sheet.IsRowHidden(r)));
        }

        // Apply new sorted order
        for (int i = 0; i < rowCount; i++)
        {
            int targetRow = startRow + i;
            int srcOriginalRow = rowIndices[i];
            int srcIndex = srcOriginalRow - startRow;
            var srcRow = rowSnapshots[srcIndex];
            int rowDelta = targetRow - srcOriginalRow;

            foreach (var cellSnap in srcRow.Cells)
            {
                CopyCellData(sheet, targetRow, cellSnap.Column, cellSnap, rowDelta);
            }

            if (srcRow.Height.HasValue)
                sheet.SetRowHeight(targetRow, srcRow.Height.Value);

            sheet.SetRowHidden(targetRow, srcRow.IsHidden);
        }

        // Reapply filters if AutoFilter is active
        sheet.ReapplyFilters();
    }

    private static void CopyCellData(SheetModel sheet, int targetRow, int targetCol, CellSnapshot src, int rowDelta)
    {
        var content = src.Content;
        string? formula = content.Formula;
        if (!string.IsNullOrEmpty(formula) && rowDelta != 0)
        {
            formula = AutoFillEngine.ShiftFormulaReferences(formula, rowDelta, 0);
        }

        if (!string.IsNullOrEmpty(formula))
        {
            sheet.SetInput(targetRow, targetCol, formula, CellEntryMode.Formula, force: true);
        }
        else
        {
            sheet[targetRow, targetCol] = src.Text;
        }

        sheet.SetCellFont(targetRow, targetCol, content.Font);
        sheet.SetCellShading(targetRow, targetCol, content.Shading);
        sheet.SetCellBorders(targetRow, targetCol, content.Borders);
        sheet.SetCellAlignment(targetRow, targetCol, content.Alignment);
        sheet.SetCellVerticalAlignment(targetRow, targetCol, content.VerticalAlignment);
        sheet.SetCellWrapText(targetRow, targetCol, content.WrapText);
        sheet.SetNumberFormat(targetRow, targetCol, content.NumberFormat);
        sheet.SetCellNote(targetRow, targetCol, content.Note);
    }

    private sealed record CellSnapshot(int Column, CellContent Content, string Text);
    private sealed record RowSnapshot(int OriginalRow, List<CellSnapshot> Cells, double? Height, bool IsHidden);
}

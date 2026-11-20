using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Cells.Core;

/// <summary>
/// Specifies whether search spans only the active worksheet or all worksheets in a workbook.
/// </summary>
public enum CellSearchScope
{
    Sheet,
    Workbook
}

/// <summary>
/// Configuration options for finding and replacing cell contents.
/// </summary>
public sealed record CellSearchOptions
{
    public string SearchText { get; init; } = string.Empty;
    public string ReplaceText { get; init; } = string.Empty;
    public bool MatchCase { get; init; }
    public bool MatchEntireCell { get; init; }
    public bool SearchFormulas { get; init; }
    public bool SearchNotes { get; init; }
    public CellSearchScope Scope { get; init; } = CellSearchScope.Sheet;
    public bool WrapAround { get; init; } = true;
}

/// <summary>
/// Represents a single search match within a worksheet.
/// </summary>
public sealed record CellSearchResult(
    string SheetId,
    string SheetName,
    CellPosition Position,
    string Value,
    string? Formula,
    int MatchIndex,
    int MatchLength
);

/// <summary>
/// Core search and replace engine for spreadsheet worksheets and workbooks.
/// Operates efficiently on sparse sheets by evaluating populated and referenced cells.
/// </summary>
public static class CellSearchEngine
{
    /// <summary>
    /// Determines whether the cell at the given coordinates matches the specified search options.
    /// </summary>
    public static bool IsMatch(SheetModel sheet, int row, int column, CellSearchOptions options, out int matchIndex, out int matchLength)
    {
        matchIndex = -1;
        matchLength = 0;

        if (sheet is null || options is null || string.IsNullOrEmpty(options.SearchText))
            return false;

        string? target;
        if (options.SearchNotes)
        {
            target = sheet.GetCellNote(row, column)?.Text;
        }
        else if (options.SearchFormulas)
        {
            target = sheet.GetContent(row, column).Formula ?? sheet.GetEditText(row, column);
        }
        else
        {
            target = sheet[row, column];
        }

        if (string.IsNullOrEmpty(target))
            return false;

        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (options.MatchEntireCell)
        {
            if (string.Equals(target, options.SearchText, comparison))
            {
                matchIndex = 0;
                matchLength = target.Length;
                return true;
            }
            return false;
        }

        int idx = target.IndexOf(options.SearchText, comparison);
        if (idx >= 0)
        {
            matchIndex = idx;
            matchLength = options.SearchText.Length;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds all matching cells within a single worksheet, sorted in row-major order.
    /// </summary>
    public static IReadOnlyList<CellSearchResult> FindAll(
        SheetModel sheet,
        CellSearchOptions options,
        string? sheetId = null,
        string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText))
            return [];

        var results = new List<CellSearchResult>();
        string sid = sheetId ?? "sheet1";
        string sname = sheetName ?? "Sheet1";

        var positions = sheet.PopulatedPositions
            .OrderBy(p => p.Row)
            .ThenBy(p => p.Column);

        foreach (var pos in positions)
        {
            if (IsMatch(sheet, pos.Row, pos.Column, options, out int matchIdx, out int matchLen))
            {
                string matchValue = options.SearchNotes
                    ? sheet.GetCellNote(pos.Row, pos.Column)?.Text ?? sheet[pos.Row, pos.Column]
                    : sheet[pos.Row, pos.Column];

                results.Add(new CellSearchResult(
                    sid,
                    sname,
                    pos,
                    matchValue,
                    sheet.GetContent(pos.Row, pos.Column).Formula,
                    matchIdx,
                    matchLen));
            }
        }

        return results;
    }

    /// <summary>
    /// Finds the next matching cell in the worksheet strictly after <paramref name="currentPosition"/>
    /// in row-major reading order, wrapping around to the beginning if enabled.
    /// </summary>
    public static CellSearchResult? FindNext(
        SheetModel sheet,
        CellPosition? currentPosition,
        CellSearchOptions options,
        string? sheetId = null,
        string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText))
            return null;

        var matches = FindAll(sheet, options, sheetId, sheetName);
        if (matches.Count == 0)
            return null;

        if (currentPosition is null)
            return matches[0];

        var cur = currentPosition.Value;
        foreach (var match in matches)
        {
            if (match.Position.Row > cur.Row || (match.Position.Row == cur.Row && match.Position.Column > cur.Column))
            {
                return match;
            }
        }

        return options.WrapAround ? matches[0] : null;
    }

    /// <summary>
    /// Finds the previous matching cell in the worksheet strictly before <paramref name="currentPosition"/>
    /// in reverse row-major reading order, wrapping around to the end if enabled.
    /// </summary>
    public static CellSearchResult? FindPrevious(
        SheetModel sheet,
        CellPosition? currentPosition,
        CellSearchOptions options,
        string? sheetId = null,
        string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText))
            return null;

        var matches = FindAll(sheet, options, sheetId, sheetName);
        if (matches.Count == 0)
            return null;

        if (currentPosition is null)
            return matches[^1];

        var cur = currentPosition.Value;
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            if (match.Position.Row < cur.Row || (match.Position.Row == cur.Row && match.Position.Column < cur.Column))
            {
                return match;
            }
        }

        return options.WrapAround ? matches[^1] : null;
    }

    /// <summary>
    /// Finds all matching cells across all worksheets in a workbook.
    /// </summary>
    public static IReadOnlyList<CellSearchResult> FindAllInWorkbook(WorkbookModel workbook, CellSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText))
            return [];

        var all = new List<CellSearchResult>();
        foreach (var worksheet in workbook.Sheets)
        {
            var sheetMatches = FindAll(worksheet.Sheet, options, worksheet.Id, worksheet.Name);
            all.AddRange(sheetMatches);
        }

        return all;
    }

    /// <summary>
    /// Finds the next matching cell across a workbook starting from the active sheet and cell position.
    /// </summary>
    public static CellSearchResult? FindNextInWorkbook(
        WorkbookModel workbook,
        int currentSheetIndex,
        CellPosition? currentPosition,
        CellSearchOptions options,
        out int foundSheetIndex)
    {
        foundSheetIndex = -1;
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText) || workbook.Sheets.Count == 0)
            return null;

        currentSheetIndex = Math.Clamp(currentSheetIndex, 0, workbook.Sheets.Count - 1);
        var currentWorksheet = workbook.Sheets[currentSheetIndex];

        // 1. Search remainder of current sheet
        var nextInCurrent = FindNext(currentWorksheet.Sheet, currentPosition, options with { WrapAround = false }, currentWorksheet.Id, currentWorksheet.Name);
        if (nextInCurrent is not null)
        {
            foundSheetIndex = currentSheetIndex;
            return nextInCurrent;
        }

        // 2. Search subsequent sheets
        for (int i = 1; i < workbook.Sheets.Count; i++)
        {
            int sheetIdx = (currentSheetIndex + i) % workbook.Sheets.Count;
            var ws = workbook.Sheets[sheetIdx];
            var matches = FindAll(ws.Sheet, options, ws.Id, ws.Name);
            if (matches.Count > 0)
            {
                foundSheetIndex = sheetIdx;
                return matches[0];
            }
        }

        // 3. Wrap around to beginning of current sheet (up to currentPosition)
        if (options.WrapAround && currentPosition is not null)
        {
            var wrapMatch = FindNext(currentWorksheet.Sheet, null, options with { WrapAround = false }, currentWorksheet.Id, currentWorksheet.Name);
            if (wrapMatch is not null)
            {
                foundSheetIndex = currentSheetIndex;
                return wrapMatch;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the previous matching cell across a workbook starting from the active sheet and cell position.
    /// </summary>
    public static CellSearchResult? FindPreviousInWorkbook(
        WorkbookModel workbook,
        int currentSheetIndex,
        CellPosition? currentPosition,
        CellSearchOptions options,
        out int foundSheetIndex)
    {
        foundSheetIndex = -1;
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText) || workbook.Sheets.Count == 0)
            return null;

        currentSheetIndex = Math.Clamp(currentSheetIndex, 0, workbook.Sheets.Count - 1);
        var currentWorksheet = workbook.Sheets[currentSheetIndex];

        // 1. Search prior part of current sheet
        var prevInCurrent = FindPrevious(currentWorksheet.Sheet, currentPosition, options with { WrapAround = false }, currentWorksheet.Id, currentWorksheet.Name);
        if (prevInCurrent is not null)
        {
            foundSheetIndex = currentSheetIndex;
            return prevInCurrent;
        }

        // 2. Search prior sheets in reverse
        for (int i = 1; i < workbook.Sheets.Count; i++)
        {
            int sheetIdx = (currentSheetIndex - i + workbook.Sheets.Count) % workbook.Sheets.Count;
            var ws = workbook.Sheets[sheetIdx];
            var matches = FindAll(ws.Sheet, options, ws.Id, ws.Name);
            if (matches.Count > 0)
            {
                foundSheetIndex = sheetIdx;
                return matches[^1];
            }
        }

        // 3. Wrap around to end of current sheet (down to currentPosition)
        if (options.WrapAround && currentPosition is not null)
        {
            var wrapMatch = FindPrevious(currentWorksheet.Sheet, null, options with { WrapAround = false }, currentWorksheet.Id, currentWorksheet.Name);
            if (wrapMatch is not null)
            {
                foundSheetIndex = currentSheetIndex;
                return wrapMatch;
            }
        }

        return null;
    }

    /// <summary>
    /// Replaces the match in the specified cell with the replacement text.
    /// </summary>
    public static bool Replace(SheetModel sheet, CellPosition position, CellSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText))
            return false;

        if (!IsMatch(sheet, position.Row, position.Column, options, out _, out _))
            return false;

        string target;
        if (options.SearchFormulas)
        {
            target = sheet.GetContent(position.Row, position.Column).Formula ?? sheet.GetEditText(position.Row, position.Column);
        }
        else
        {
            target = sheet.GetEditText(position.Row, position.Column);
        }

        string replacement;
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (options.MatchEntireCell)
        {
            replacement = options.ReplaceText ?? string.Empty;
        }
        else
        {
            replacement = target.Replace(options.SearchText, options.ReplaceText ?? string.Empty, comparison);
        }

        var mode = (options.SearchFormulas || replacement.StartsWith('=')) ? CellEntryMode.Formula : CellEntryMode.Auto;
        sheet.SetInput(position.Row, position.Column, replacement, mode);
        return true;
    }

    /// <summary>
    /// Replaces all matches in the worksheet and recalculates the sheet.
    /// Returns the number of cells modified.
    /// </summary>
    public static int ReplaceAll(SheetModel sheet, CellSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText))
            return 0;

        var matches = FindAll(sheet, options);
        if (matches.Count == 0)
            return 0;

        int count = 0;
        foreach (var match in matches)
        {
            if (Replace(sheet, match.Position, options))
            {
                count++;
            }
        }

        if (count > 0)
        {
            sheet.Recalculate();
        }

        return count;
    }

    /// <summary>
    /// Replaces all matches across all worksheets in the workbook.
    /// Returns the total number of cells modified across all worksheets.
    /// </summary>
    public static int ReplaceAllInWorkbook(WorkbookModel workbook, CellSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.SearchText))
            return 0;

        int total = 0;
        foreach (var worksheet in workbook.Sheets)
        {
            total += ReplaceAll(worksheet.Sheet, options);
        }

        return total;
    }
}

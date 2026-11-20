using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;

namespace Broiler.Cells.Core;

/// <summary>
/// Result of evaluating a data validation rule on cell input.
/// </summary>
public readonly record struct ValidationResult(
    bool IsValid,
    DataValidationErrorStyle ErrorStyle = DataValidationErrorStyle.Stop,
    string? ErrorTitle = null,
    string? ErrorMessage = null)
{
    public static ValidationResult Success => new(true);

    public static ValidationResult Failure(DataValidation dv, string? defaultMessage = null) =>
        new(false,
            dv.ErrorStyle,
            !string.IsNullOrWhiteSpace(dv.ErrorTitle) ? dv.ErrorTitle : "Validation Error",
            !string.IsNullOrWhiteSpace(dv.ErrorMessage) ? dv.ErrorMessage : (defaultMessage ?? "The value entered is not valid."));
}

/// <summary>
/// High-performance engine for evaluating data validation rules and extracting picklist choices.
/// </summary>
public static class DataValidationEngine
{
    /// <summary>
    /// Validates an input string against any data validation rule configured on the specified cell.
    /// </summary>
    public static ValidationResult Validate(SheetModel sheet, int row, int col, string input, DataValidation? dv = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        dv ??= sheet.GetDataValidation(row, col);

        if (dv is null || dv.Type == DataValidationType.None)
            return ValidationResult.Success;

        string text = input ?? string.Empty;

        // Blank input check
        if (string.IsNullOrWhiteSpace(text))
        {
            if (dv.AllowBlank)
                return ValidationResult.Success;
            return ValidationResult.Failure(dv, "A non-blank value is required for this cell.");
        }

        switch (dv.Type)
        {
            case DataValidationType.List:
                return ValidateList(sheet, row, col, dv, text);

            case DataValidationType.WholeNumber:
                return ValidateWholeNumber(dv, text);

            case DataValidationType.Decimal:
                return ValidateDecimal(dv, text);

            case DataValidationType.Date:
                return ValidateDate(dv, text);

            case DataValidationType.TextLength:
                return ValidateTextLength(dv, text);

            case DataValidationType.Custom:
                return ValidateCustom(dv, text);

            default:
                return ValidationResult.Success;
        }
    }

    /// <summary>
    /// Extracts the list of allowed values for a List validation rule on the specified cell.
    /// Supports literal comma-separated values and cell range references (e.g. "=A1:A5", "Sheet1!$B$1:$B$10").
    /// </summary>
    public static IReadOnlyList<string> GetValidationListItems(SheetModel sheet, int row, int col, DataValidation? dv = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        dv ??= sheet.GetDataValidation(row, col);

        if (dv is null || dv.Type != DataValidationType.List || string.IsNullOrWhiteSpace(dv.Formula1))
            return Array.Empty<string>();

        string formula = dv.Formula1.Trim();

        // Check if formula is a range reference
        if (formula.StartsWith('=') || formula.Contains('!') || formula.Contains(':') || Regex.IsMatch(formula, @"^\$?[A-Za-z]+\$?\d+$"))
        {
            string clean = formula.TrimStart('=');
            SheetModel targetSheet = sheet;

            int bangIdx = clean.IndexOf('!');
            if (bangIdx >= 0)
            {
                string sheetRef = clean[..bangIdx].Trim().Trim('\'');
                clean = clean[(bangIdx + 1)..].Trim();
                if (sheet.Workbook is not null && sheet.Workbook.TryGetSheet(sheetRef, out var resolvedSheet))
                {
                    targetSheet = resolvedSheet;
                }
            }

            // Strip absolute reference dollar signs ($A$1 -> A1)
            string unanchored = clean.Replace("$", string.Empty);
            if (CellRangeAddress.TryParse(unanchored, out CellRangeAddress range))
            {
                var list = new List<string>();
                int startRow = Math.Max(0, range.StartRow);
                int endRow = Math.Min(targetSheet.RowCount - 1, range.EndRow);
                int startCol = Math.Max(0, range.StartCol);
                int endCol = Math.Min(targetSheet.ColumnCount - 1, range.EndCol);

                for (int r = startRow; r <= endRow; r++)
                {
                    for (int c = startCol; c <= endCol; c++)
                    {
                        string cellVal = targetSheet[r, c];
                        if (!string.IsNullOrWhiteSpace(cellVal))
                            list.Add(cellVal);
                    }
                }

                return list;
            }
        }

        // Literal comma-separated list (e.g. "Apple,Banana,Orange" or "\"Apple,Banana,Orange\"")
        string literal = formula;
        if (literal.StartsWith('"') && literal.EndsWith('"') && literal.Length >= 2)
            literal = literal[1..^1];

        return literal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static ValidationResult ValidateList(SheetModel sheet, int row, int col, DataValidation dv, string text)
    {
        var items = GetValidationListItems(sheet, row, col, dv);
        if (items.Count == 0)
            return ValidationResult.Success;

        string trimmed = text.Trim();
        if (items.Any(i => string.Equals(i, trimmed, StringComparison.OrdinalIgnoreCase)))
            return ValidationResult.Success;

        return ValidationResult.Failure(dv, "The value does not match any allowed item in the list.");
    }

    private static ValidationResult ValidateWholeNumber(DataValidation dv, string text)
    {
        if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long val) &&
            !long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out val))
        {
            return ValidationResult.Failure(dv, "The value must be an integer whole number.");
        }

        bool hasF1 = long.TryParse(dv.Formula1?.Replace("$", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out long f1);
        bool hasF2 = long.TryParse(dv.Formula2?.Replace("$", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out long f2);

        if (!EvaluateOperator(dv.Operator, val, hasF1 ? f1 : 0, hasF2 ? f2 : 0))
            return ValidationResult.Failure(dv, "The whole number entered does not meet the specified criteria.");

        return ValidationResult.Success;
    }

    private static ValidationResult ValidateDecimal(DataValidation dv, string text)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double val) &&
            !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out val))
        {
            return ValidationResult.Failure(dv, "The value must be a valid number.");
        }

        bool hasF1 = double.TryParse(dv.Formula1?.Replace("$", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out double f1);
        bool hasF2 = double.TryParse(dv.Formula2?.Replace("$", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out double f2);

        if (!EvaluateOperator(dv.Operator, val, hasF1 ? f1 : 0, hasF2 ? f2 : 0))
            return ValidationResult.Failure(dv, "The number entered does not meet the specified criteria.");

        return ValidationResult.Success;
    }

    private static ValidationResult ValidateDate(DataValidation dv, string text)
    {
        if (!DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime val) &&
            !DateTime.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out val))
        {
            return ValidationResult.Failure(dv, "The value must be a valid date.");
        }

        bool hasF1 = DateTime.TryParse(dv.Formula1, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime f1) ||
                     DateTime.TryParse(dv.Formula1, CultureInfo.CurrentCulture, DateTimeStyles.None, out f1);
        bool hasF2 = DateTime.TryParse(dv.Formula2, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime f2) ||
                     DateTime.TryParse(dv.Formula2, CultureInfo.CurrentCulture, DateTimeStyles.None, out f2);

        if (!EvaluateOperator(dv.Operator, val.Ticks, hasF1 ? f1.Ticks : 0, hasF2 ? f2.Ticks : 0))
            return ValidationResult.Failure(dv, "The date entered does not meet the specified criteria.");

        return ValidationResult.Success;
    }

    private static ValidationResult ValidateTextLength(DataValidation dv, string text)
    {
        int len = text.Length;
        bool hasF1 = int.TryParse(dv.Formula1, NumberStyles.Integer, CultureInfo.InvariantCulture, out int f1);
        bool hasF2 = int.TryParse(dv.Formula2, NumberStyles.Integer, CultureInfo.InvariantCulture, out int f2);

        if (!EvaluateOperator(dv.Operator, len, hasF1 ? f1 : 0, hasF2 ? f2 : 0))
            return ValidationResult.Failure(dv, "The text length does not meet the specified criteria.");

        return ValidationResult.Success;
    }

    private static ValidationResult ValidateCustom(DataValidation dv, string text)
    {
        if (string.IsNullOrWhiteSpace(dv.Formula1))
            return ValidationResult.Success;

        string clean = dv.Formula1.Trim().TrimStart('=');
        if (bool.TryParse(clean, out bool bVal))
            return bVal ? ValidationResult.Success : ValidationResult.Failure(dv, "The custom validation formula evaluated to false.");

        return ValidationResult.Success;
    }

    private static bool EvaluateOperator<T>(DataValidationOperator op, T val, T f1, T f2) where T : IComparable<T>
    {
        return op switch
        {
            DataValidationOperator.Between => val.CompareTo(f1) >= 0 && val.CompareTo(f2) <= 0,
            DataValidationOperator.NotBetween => val.CompareTo(f1) < 0 || val.CompareTo(f2) > 0,
            DataValidationOperator.Equal => val.CompareTo(f1) == 0,
            DataValidationOperator.NotEqual => val.CompareTo(f1) != 0,
            DataValidationOperator.GreaterThan => val.CompareTo(f1) > 0,
            DataValidationOperator.LessThan => val.CompareTo(f1) < 0,
            DataValidationOperator.GreaterThanOrEqual => val.CompareTo(f1) >= 0,
            DataValidationOperator.LessThanOrEqual => val.CompareTo(f1) <= 0,
            _ => true
        };
    }
}

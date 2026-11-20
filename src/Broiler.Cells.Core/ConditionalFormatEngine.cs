using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics.Color;

namespace Broiler.Cells.Core;

/// <summary>
/// Dynamic evaluation engine for spreadsheet conditional formatting rules.
/// Evaluates rules without mutating underlying cell content or sheet state.
/// </summary>
public static class ConditionalFormatEngine
{
    /// <summary>
    /// Evaluates all active conditional formatting rules applicable to the specified cell.
    /// Returns the combined effective styling attributes, or null if no rule applies.
    /// </summary>
    public static ConditionalFormatStyle? Evaluate(SheetModel sheet, int row, int col)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (sheet.ConditionalFormats.Count == 0)
            return null;

        ConditionalFormatStyle? effectiveStyle = null;

        foreach (var format in sheet.ConditionalFormats)
        {
            CellRangeAddress? matchedRange = null;
            foreach (var r in format.Ranges)
            {
                if (r.Contains(row, col))
                {
                    matchedRange = r;
                    break;
                }
            }

            if (matchedRange is null)
                continue;

            foreach (var rule in format.Rules)
            {
                var ruleStyle = EvaluateRule(rule, sheet, row, col, matchedRange.Value);
                if (ruleStyle is not null)
                {
                    effectiveStyle = MergeStyles(effectiveStyle, ruleStyle);
                    if (rule.StopIfTrue)
                        return effectiveStyle;
                }
            }
        }

        return effectiveStyle;
    }

    private static ConditionalFormatStyle? EvaluateRule(
        ConditionalFormatRule rule,
        SheetModel sheet,
        int row,
        int col,
        CellRangeAddress range)
    {
        var content = sheet.GetContent(row, col);
        string text = sheet[row, col];
        double? numVal = GetNumericValue(content, text);

        switch (rule.Type)
        {
            case ConditionalFormatRuleType.CellIs:
                return EvaluateCellIs(rule, numVal, text);

            case ConditionalFormatRuleType.ContainsText:
                if (!string.IsNullOrEmpty(rule.Formula1) &&
                    text.Contains(rule.Formula1, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.Style;
                }
                return null;

            case ConditionalFormatRuleType.NotContainsText:
                if (!string.IsNullOrEmpty(rule.Formula1) &&
                    !text.Contains(rule.Formula1, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.Style;
                }
                return null;

            case ConditionalFormatRuleType.BeginsWith:
                if (!string.IsNullOrEmpty(rule.Formula1) &&
                    text.StartsWith(rule.Formula1, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.Style;
                }
                return null;

            case ConditionalFormatRuleType.EndsWith:
                if (!string.IsNullOrEmpty(rule.Formula1) &&
                    text.EndsWith(rule.Formula1, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.Style;
                }
                return null;

            case ConditionalFormatRuleType.Blanks:
                if (IsCellBlank(content, text))
                    return rule.Style;
                return null;

            case ConditionalFormatRuleType.NoBlanks:
                if (!IsCellBlank(content, text))
                    return rule.Style;
                return null;

            case ConditionalFormatRuleType.DuplicateValues:
                if (IsDuplicate(sheet, row, col, text, range))
                    return rule.Style;
                return null;

            case ConditionalFormatRuleType.UniqueValues:
                if (IsUnique(sheet, row, col, text, range))
                    return rule.Style;
                return null;

            case ConditionalFormatRuleType.Expression:
                if (EvaluateExpression(rule.Formula1, text, numVal))
                    return rule.Style;
                return null;

            case ConditionalFormatRuleType.ColorScale:
                if (rule.ColorScale is { } cs && numVal.HasValue)
                    return EvaluateColorScale(cs, numVal.Value, sheet, range);
                return null;

            default:
                return null;
        }
    }

    private static bool IsCellBlank(CellContent content, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            return false;
        if (content.Value.IsEmpty)
            return true;
        if (content.Value.IsText && string.IsNullOrWhiteSpace(content.Value.Text))
            return true;
        return false;
    }

    private static double? GetNumericValue(CellContent content, string text)
    {
        if (content.Value.IsNumber)
            return content.Value.Number;

        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsedVal))
            return parsedVal;

        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out double parsedVal2))
            return parsedVal2;

        return null;
    }

    private static ConditionalFormatStyle? EvaluateCellIs(ConditionalFormatRule rule, double? numVal, string text)
    {
        bool hasF1Num = double.TryParse(rule.Formula1, NumberStyles.Float, CultureInfo.InvariantCulture, out double f1Num);
        bool hasF2Num = double.TryParse(rule.Formula2, NumberStyles.Float, CultureInfo.InvariantCulture, out double f2Num);

        if (numVal.HasValue && hasF1Num)
        {
            double val = numVal.Value;
            bool match = rule.Operator switch
            {
                ConditionalFormatOperator.Equal => Math.Abs(val - f1Num) < 1e-9,
                ConditionalFormatOperator.NotEqual => Math.Abs(val - f1Num) >= 1e-9,
                ConditionalFormatOperator.GreaterThan => val > f1Num,
                ConditionalFormatOperator.GreaterThanOrEqual => val >= f1Num,
                ConditionalFormatOperator.LessThan => val < f1Num,
                ConditionalFormatOperator.LessThanOrEqual => val <= f1Num,
                ConditionalFormatOperator.Between => hasF2Num && val >= Math.Min(f1Num, f2Num) && val <= Math.Max(f1Num, f2Num),
                ConditionalFormatOperator.NotBetween => hasF2Num && (val < Math.Min(f1Num, f2Num) || val > Math.Max(f1Num, f2Num)),
                _ => false
            };
            return match ? rule.Style : null;
        }

        // Compare as text
        string f1Str = rule.Formula1?.Trim('"', '\'') ?? string.Empty;
        bool textMatch = rule.Operator switch
        {
            ConditionalFormatOperator.Equal => string.Equals(text, f1Str, StringComparison.OrdinalIgnoreCase),
            ConditionalFormatOperator.NotEqual => !string.Equals(text, f1Str, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        return textMatch ? rule.Style : null;
    }

    private static bool IsDuplicate(SheetModel sheet, int targetRow, int targetCol, string text, CellRangeAddress range)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int startR = Math.Max(0, range.StartRow);
        int endR = Math.Min(sheet.RowCount - 1, range.EndRow);
        int startC = Math.Max(0, range.StartCol);
        int endC = Math.Min(sheet.ColumnCount - 1, range.EndCol);

        int count = 0;
        for (int r = startR; r <= endR; r++)
        {
            for (int c = startC; c <= endC; c++)
            {
                if (string.Equals(sheet[r, c], text, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    if (count > 1)
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsUnique(SheetModel sheet, int targetRow, int targetCol, string text, CellRangeAddress range)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int startR = Math.Max(0, range.StartRow);
        int endR = Math.Min(sheet.RowCount - 1, range.EndRow);
        int startC = Math.Max(0, range.StartCol);
        int endC = Math.Min(sheet.ColumnCount - 1, range.EndCol);

        int count = 0;
        for (int r = startR; r <= endR; r++)
        {
            for (int c = startC; c <= endC; c++)
            {
                if (string.Equals(sheet[r, c], text, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    if (count > 1)
                        return false;
                }
            }
        }

        return count == 1;
    }

    private static bool EvaluateExpression(string? formula, string text, double? numVal)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return false;

        string clean = formula.Trim().TrimStart('=');

        // Simple boolean literal check
        if (bool.TryParse(clean, out bool bVal))
            return bVal;

        if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
            return dVal != 0;

        // Check if string matches text
        if (string.Equals(clean, text, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static ConditionalFormatStyle EvaluateColorScale(
        ColorScaleConfiguration cs,
        double numVal,
        SheetModel sheet,
        CellRangeAddress range)
    {
        double min = cs.MinValue ?? double.MaxValue;
        double max = cs.MaxValue ?? double.MinValue;

        if (cs.MinValue is null || cs.MaxValue is null)
        {
            int startR = Math.Max(0, range.StartRow);
            int endR = Math.Min(sheet.RowCount - 1, range.EndRow);
            int startC = Math.Max(0, range.StartCol);
            int endC = Math.Min(sheet.ColumnCount - 1, range.EndCol);

            for (int r = startR; r <= endR; r++)
            {
                for (int c = startC; c <= endC; c++)
                {
                    var val = GetNumericValue(sheet.GetContent(r, c), sheet[r, c]);
                    if (val.HasValue)
                    {
                        if (cs.MinValue is null && val.Value < min) min = val.Value;
                        if (cs.MaxValue is null && val.Value > max) max = val.Value;
                    }
                }
            }

            if (min > max)
            {
                min = numVal;
                max = numVal;
            }
        }

        double t = (max <= min) ? 0.5 : Math.Clamp((numVal - min) / (max - min), 0.0, 1.0);

        BColor color;
        if (cs.MidColor is null)
        {
            color = InterpolateColor(cs.MinColor, cs.MaxColor, t);
        }
        else
        {
            if (t < 0.5)
                color = InterpolateColor(cs.MinColor, cs.MidColor.Value, t * 2.0);
            else
                color = InterpolateColor(cs.MidColor.Value, cs.MaxColor, (t - 0.5) * 2.0);
        }

        return new ConditionalFormatStyle(Shading: color);
    }

    public static BColor InterpolateColor(BColor c1, BColor c2, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte a = (byte)Math.Round(c1.A + t * (c2.A - c1.A));
        byte r = (byte)Math.Round(c1.R + t * (c2.R - c1.R));
        byte g = (byte)Math.Round(c1.G + t * (c2.G - c1.G));
        byte b = (byte)Math.Round(c1.B + t * (c2.B - c1.B));
        return BColor.FromArgb(a, r, g, b);
    }

    public static ConditionalFormatStyle MergeStyles(ConditionalFormatStyle? current, ConditionalFormatStyle ruleStyle)
    {
        if (current is null)
            return ruleStyle;

        return new ConditionalFormatStyle(
            Shading: current.Shading ?? ruleStyle.Shading,
            Foreground: current.Foreground ?? ruleStyle.Foreground,
            Bold: current.Bold ?? ruleStyle.Bold,
            Italic: current.Italic ?? ruleStyle.Italic,
            Underline: current.Underline ?? ruleStyle.Underline,
            Strikethrough: current.Strikethrough ?? ruleStyle.Strikethrough,
            Borders: current.Borders ?? ruleStyle.Borders);
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Broiler.Documents.Model;

namespace Broiler.Cells.Core;

/// <summary>
/// Predefined format presets and decimal precision adjustment helpers for spreadsheet cells.
/// </summary>
public static class CellFormatUtility
{
    /// <summary>
    /// Standard predefined format presets.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string? Format)> Presets =
    [
        ("General", CellFormatPresets.General),
        ("Number", CellFormatPresets.Integer),
        ("Decimal", CellFormatPresets.Decimal2),
        ("Currency", CellFormatPresets.Currency),
        ("Percentage", CellFormatPresets.Percentage),
        ("Short Date", CellFormatPresets.ShortDate),
        ("Long Date", CellFormatPresets.LongDate),
        ("Time", CellFormatPresets.Time),
        ("Date & Time", CellFormatPresets.DateTime),
        ("Scientific", CellFormatPresets.Scientific),
        ("Text", "@")
    ];

    /// <summary>
    /// Returns the friendly name of a number format, or "Custom" if non-standard.
    /// </summary>
    public static string GetPresetName(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || string.Equals(format, "General", StringComparison.OrdinalIgnoreCase))
            return "General";

        foreach (var (name, presetFormat) in Presets)
        {
            if (presetFormat is not null && string.Equals(format, presetFormat, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        // Custom condition/color tags like [Red] or [>100]
        if (format.Contains('[') && format.Contains(']'))
            return "Custom";

        if (format.Contains('%')) return "Percentage";
        if (format.Contains('$') || format.Contains('€') || format.Contains('£')) return "Currency";
        if (format.Contains("yy", StringComparison.OrdinalIgnoreCase))
            return format.Contains("hh", StringComparison.OrdinalIgnoreCase) ? "Date & Time" : "Date";
        if (format.Contains("hh:", StringComparison.OrdinalIgnoreCase)) return "Time";
        if (format.Contains("E+", StringComparison.OrdinalIgnoreCase)) return "Scientific";
        if (format == "@") return "Text";
        if (format.Contains('.')) return "Decimal";
        if (format.Contains('#') || format.Contains('0')) return "Number";

        return "Custom";
    }

    /// <summary>
    /// Gets the next preset in sequence when cycling through presets.
    /// </summary>
    public static (string Name, string? Format) GetNextPreset(string? currentFormat)
    {
        int currentIndex = -1;
        if (!string.IsNullOrWhiteSpace(currentFormat))
        {
            for (int i = 0; i < Presets.Count; i++)
            {
                if (Presets[i].Format is { } pf && string.Equals(currentFormat, pf, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }
        }
        int nextIndex = (currentIndex + 1) % Presets.Count;
        return Presets[nextIndex];
    }

    /// <summary>
    /// Adjusts the decimal places in a number format string by <paramref name="delta"/> (+1 to increase, -1 to decrease).
    /// </summary>
    public static string AdjustDecimals(string? format, int delta)
    {
        if (delta == 0) return format ?? CellFormatPresets.General;

        if (string.IsNullOrWhiteSpace(format) || string.Equals(format, "General", StringComparison.OrdinalIgnoreCase))
        {
            return delta > 0 ? "#,##0." + new string('0', delta) : "#,##0";
        }

        var sections = format.Split(';');
        var adjustedSections = new string[sections.Length];

        for (int i = 0; i < sections.Length; i++)
        {
            var sec = sections[i];
            var match = Regex.Match(sec, @"\.(0+)");
            if (match.Success)
            {
                int currentDecimals = match.Groups[1].Length;
                int newDecimals = Math.Max(0, currentDecimals + delta);
                if (newDecimals == 0)
                {
                    adjustedSections[i] = sec.Remove(match.Index, match.Length);
                }
                else
                {
                    adjustedSections[i] = sec.Substring(0, match.Index) + "." + new string('0', newDecimals) + sec.Substring(match.Index + match.Length);
                }
            }
            else if (delta > 0)
            {
                if (sec.Contains('%'))
                {
                    int pctIdx = sec.IndexOf('%');
                    adjustedSections[i] = sec.Insert(pctIdx, "." + new string('0', delta));
                }
                else
                {
                    var matches = Regex.Matches(sec, @"[0#]");
                    if (matches.Count > 0)
                    {
                        var last = matches[^1];
                        adjustedSections[i] = sec.Insert(last.Index + last.Length, "." + new string('0', delta));
                    }
                    else
                    {
                        adjustedSections[i] = sec;
                    }
                }
            }
            else
            {
                adjustedSections[i] = sec;
            }
        }

        return string.Join(";", adjustedSections);
    }
}

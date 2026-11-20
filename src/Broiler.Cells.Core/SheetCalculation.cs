using Broiler.Documents.Model;

namespace Broiler.Cells.Core;

public sealed record FormulaIssue(CellPosition Cell, FormulaError Error, string Message);

internal sealed record SheetCalculationResult(TableEvaluationResult Evaluation, IReadOnlyList<FormulaIssue> Issues);

/// <summary>Bounded adapter to Documents' same-table dependency and formula engine.</summary>
internal static class SheetCalculation
{
    internal const int MaximumFormulaCells = int.MaxValue;
    internal const int MaximumFormulaLength = 1024;
    internal const int MaximumDependencyDepth = 128;
    internal const long MaximumTotalDependencies = 5_000_000;

    internal static SheetCalculationResult Evaluate(SheetModel sheet, Func<int, int, CellContent> read)
    {
        var inputs = new Dictionary<CellAddress, CellContent>();
        foreach (var pos in sheet.PopulatedPositions)
        {
            inputs[new CellAddress(pos.Row, pos.Column)] = read(pos.Row, pos.Column);
        }
        var formulas = inputs.Where(p => p.Value.Formula is not null).ToDictionary(p => p.Key, p => p.Value.Formula!);
        var refused = new HashSet<CellAddress>();
        if (formulas.Count > MaximumFormulaCells) refused.UnionWith(formulas.Keys);
        foreach (var (address, formula) in formulas)
        {
            int nesting = 0, operators = 0;
            if (formula.Length > MaximumFormulaLength) { refused.Add(address); continue; }
            foreach (char token in formula)
            {
                if (token == '(' && ++nesting > 64) refused.Add(address);
                if (token == ')') nesting--;
                if ("+-*/^&,=<>:%".Contains(token) && ++operators > 128) refused.Add(address);
            }
        }

        var dependencies = new Dictionary<CellAddress, IReadOnlySet<CellAddress>>();
        long totalDependencies = 0;
        foreach (var (address, formula) in formulas)
        {
            if (refused.Contains(address)) continue;
            var references = TableFormulaEngine.ExtractDependencies(formula, address, sheet.RowCount, sheet.ColumnCount);
            dependencies[address] = references;
            totalDependencies += references.Count;
            if (totalDependencies > MaximumTotalDependencies) { refused.UnionWith(formulas.Keys); break; }
            foreach (var r in references)
            {
                if (!inputs.ContainsKey(r) && r.Row < sheet.RowCount && r.Column < sheet.ColumnCount)
                    inputs[r] = read(r.Row, r.Column);
            }
        }
        // Bound recursion even when a long dependency path ends in a cycle.
        var visited = new HashSet<CellAddress>();
        var active = new HashSet<CellAddress>();
        var completedDepth = new Dictionary<CellAddress, int>();
        bool pathLimitExceeded = false;
        foreach (var root in dependencies.Keys)
        {
            if (visited.Contains(root)) continue;
            var stack = new Stack<(CellAddress Cell, IEnumerator<CellAddress> Children)>();
            active.Add(root);
            stack.Push((root, dependencies[root].Where(dependencies.ContainsKey).GetEnumerator()));
            while (stack.Count > 0)
            {
                if (stack.Count > MaximumDependencyDepth) { refused.UnionWith(formulas.Keys); pathLimitExceeded = true; break; }
                var current = stack.Peek();
                if (!current.Children.MoveNext())
                {
                    current.Children.Dispose();
                    stack.Pop();
                    active.Remove(current.Cell);
                    visited.Add(current.Cell);
                    int depth = 1 + dependencies[current.Cell].Where(completedDepth.ContainsKey).Select(a => completedDepth[a]).DefaultIfEmpty(0).Max();
                    completedDepth[current.Cell] = depth;
                    if (depth > MaximumDependencyDepth) { refused.UnionWith(formulas.Keys); pathLimitExceeded = true; break; }
                    continue;
                }
                var child = current.Children.Current;
                if (visited.Contains(child) || active.Contains(child)) continue;
                active.Add(child);
                stack.Push((child, dependencies[child].Where(dependencies.ContainsKey).GetEnumerator()));
            }
            if (pathLimitExceeded)
            {
                foreach (var pending in stack) pending.Children.Dispose();
                break;
            }
        }

        int maxRow = 0;
        foreach (var addr in inputs.Keys)
        {
            if (addr.Row > maxRow) maxRow = addr.Row;
        }
        int calcRows = Math.Min(sheet.RowCount, maxRow + 1);

        var rowGroups = new Dictionary<int, List<TableCell>>();
        foreach (var (address, input) in inputs)
        {
            if (!rowGroups.TryGetValue(address.Row, out var cellList))
            {
                cellList = [];
                rowGroups[address.Row] = cellList;
            }

            cellList.Add(new TableCell(0, 0, address.Column,
                formula: refused.Contains(address) ? null : input.Formula,
                numberFormat: input.NumberFormat,
                value: refused.Contains(address) ? CellValue.FromError(FormulaError.Num) : input.Value));
        }

        var rows = new List<TableRow>(calcRows);
        for (int row = 0; row < calcRows; row++)
        {
            if (rowGroups.TryGetValue(row, out var cells))
            {
                cells.Sort((a, b) => a.ColumnIndex.CompareTo(b.ColumnIndex));
                rows.Add(new TableRow(cells));
            }
            else
            {
                rows.Add(new TableRow([]));
            }
        }

        Func<string, CellAddress, CellValue>? crossSheetLookup = null;
        if (sheet.Workbook is { } wb)
        {
            crossSheetLookup = (sheetName, addr) =>
            {
                var target = wb.Sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
                if (target is null) return CellValue.FromError(FormulaError.Ref);
                if (addr.Row < 0 || addr.Row >= target.Sheet.RowCount || addr.Column < 0 || addr.Column >= target.Sheet.ColumnCount)
                    return CellValue.FromError(FormulaError.Ref);
                return target.Sheet.GetContent(addr.Row, addr.Column).Value;
            };
        }

        var evaluated = TableFormulaEngine.Evaluate(new DocumentTable(0, 0, rows), crossSheetLookup: crossSheetLookup);
        var issues = new List<FormulaIssue>();
        foreach (var address in formulas.Keys)
        {
            var value = evaluated.GetValue(address);
            if (!value.IsError) continue;
            string message = refused.Contains(address) ? "Formula exceeds Cells calculation limits."
                : value.Error == FormulaError.Name ? "Unsupported function or unknown name."
                : value.Error == FormulaError.Syntax ? "Invalid or unsupported formula syntax."
                : value.Error == FormulaError.Cycle ? "Circular formula dependency."
                : value.Error == FormulaError.Ref ? "Referenced cell or sheet does not exist or is unsupported."
                : "Calculation returned " + value.Error.ToErrorString();
            issues.Add(new FormulaIssue(new CellPosition(address.Row, address.Column), value.Error, message));
        }
        return new SheetCalculationResult(evaluated, issues);
    }
}

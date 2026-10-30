# Cells formula integration

## Assessment of Broiler.Documents

The published Documents preview.29 model contains `FormulaParser`,
`TableFormulaEngine.Evaluate`, dependency extraction, dependency ordering,
cycle/error propagation and typed results. It is sufficient for a bounded,
**same-worksheet** formula implementation. Cells uses that engine rather than
implementing a second evaluator.

The evaluator takes one `DocumentTable`; it has no workbook/sheet resolver.
Cross-sheet references, named ranges, external references and a full Excel/ODF
function library require further work. Valid expressions outside this subset can
therefore report an error in Cells while working in Excel or LibreOffice.

## Behavior

- Choose Formula entry and start with `=`. Auto/Text remain literal even when text
  begins with `=`. Editors show formula source; the grid shows the calculated value.
- Value/formula changes invalidate the sheet cache. The next display/read/export
  rebuilds the dependency graph and recalculates the sheet in dependency order.
  This is full-sheet invalidation, not fine-grained incremental recalculation.
- Imported cached results are not trusted. Supported formulas are recalculated;
  unsupported/invalid formulas receive an explicit error rather than retaining a
  stale answer. Source formula strings are retained for editing/export.
- A **Recalculate** toolbar command refreshes every worksheet. Inactive worksheets
  otherwise calculate lazily when inspected, selected or exported.
- Formula errors appear in cells and the active worksheet's status bar. The public
  model exposes `FormulaIssues` and `FormulaDependencies` for richer future UI.
- XLSX/ODS export retains the expression and current cached value/error. XLSX
  caches map internal cycle/syntax error tokens to standard `#REF!`/`#VALUE!`
  tokens for compatibility; formula source remains unchanged. Other applications
  may recalculate with their own broader semantics.

## Supported base

The owner supports arithmetic/comparison/concatenation expressions, A1 references,
cell ranges, and functions including SUM, AVERAGE/AVG, COUNT, COUNTA, MIN, MAX,
PRODUCT, MEDIAN, IF, ABS, ROUND, INT, TRUNC, MOD, SQRT, POWER, SIGN, AND, OR, NOT,
ISNUMBER, ISTEXT, ISBLANK, ISERROR, CONCAT/CONCATENATE, LEN, UPPER, LOWER and TRIM.
Semantics are those of Documents' table evaluator; this is not a claim of complete
Excel or LibreOffice compatibility. Existing string cells remain strings: use typed
Number entry when a numeric precedent is intended.

Unknown functions/names and unsupported syntax are diagnosed. Cross-sheet formula
strings are retained but not evaluated. Renaming/removing sheets does not rewrite
formula references. Volatile functions, dynamic arrays, named ranges, tables'
structured references, workbook references and recalculation of external data are
not supported by this integration.

## Calculation limits

The grid remains 100×100. Calculation accepts at most 512 formula cells per sheet,
1,024 characters per formula, 64 levels of parenthesis nesting, 128 operator/argument
separator characters, dependency paths of 128 formula cells, and 100,000 extracted
dependencies per sheet. Complexity checks are conservative, including punctuation
inside strings. Over-budget formulas, or the formula set for a sheet-wide budget,
receive `#NUM!` with a limit diagnostic. These checks run before the recursive owner
evaluator; cycles within the limits are handled by the owner as cycle errors.

## Remaining work

1. Add a workbook-aware reference resolver and dependency graph, with sheet rename
   and removal semantics, before claiming cross-sheet formula support.
2. Add broader spreadsheet conformance fixtures and unsupported-function reporting
   beyond the generic owner error codes.
3. Consider fine-grained recalculation, cancellation and larger-grid resource budgets
   when the dense 100×100 model is replaced.
4. Browser recovery remains a visible-sheet text snapshot; it cannot restore formula
   source or typed workbook data. Use XLSX/ODS exports as workbook backups.

Tests cover arithmetic/ranges/functions, dependency changes, cycles and recovery,
complexity limits, stale imported caches, literal text, and recalculated XLSX/ODS
round trips. Visual desktop/browser checks and external application interoperability
remain manual checks.

# ODS and XLSX support in Broiler Cells

Cells uses the published `Broiler.Documents.Ods` and `Broiler.Documents.Xlsx`
preview.29 codecs. This is basic spreadsheet editing, not full Excel or LibreOffice
compatibility. Keep original workbooks as backups.

## Available now

- Desktop Open and Save As offer CSV, XLSX and ODS. The extension selects the codec;
  `.xls`, `.xlsm`, `.xltx`, encrypted workbooks and other formats are not offered.
- The WebAssembly file picker accepts all three formats. The export dialog accepts
  a `.csv`, `.xlsx` or `.ods` filename and downloads with the corresponding MIME type.
- Worksheet tabs select a separate editing grid for each worksheet. Pending edits
  are committed before switching; each sheet retains its selected cell. Previous/next
  controls page through tabs when the window is narrow or the workbook has many sheets.
- Use **+** to add a sheet, the name field and **Rename** to rename it, and **Remove**
  followed by **Confirm remove** to delete it. Switching sheets cancels removal.
  The last worksheet cannot be removed. Names must be unique ignoring case, contain
  1–31 characters and avoid Excel's forbidden characters. Removal has no undo.
- XLSX/ODS export writes every worksheet's edits, names and structural changes, within
  Documents' supported subset. CSV exports only the currently selected worksheet.
  Newly opened workbooks select their first sheet; active-sheet persistence and
  drag reordering are not implemented.
- Unchanged cells retain their typed value, cached display text, formula, number
  format, shading and spans to the extent the Documents reader/writer supports them.
  Supported row heights and column widths are retained in the saved workbook; the
  Cells grid does not render dimensions/styles. Typed editing and number-format controls
  are available; see the completed P1 section below.
- Empty rows and columns preserve cell coordinates. Explicit blank cells work around
  the ODS writer's current positional treatment of sparse cell sequences.
- Each worksheet must fit within 100 rows and 100 columns. A larger worksheet,
  including a non-visible worksheet, causes import to fail without replacing the
  open grid. No automatic truncation is performed.
- Reader diagnostics appear in the desktop import status or browser operation status.
  Failed/rejected reads do not replace the open sheet; partial results are identified
  as basic imports. Desktop writes stage output before replacing the destination.
- Desktop Save on an imported workbook opens Save As. Choosing the original import
  path is refused; choose a different filename. Subsequent saves to that copy work.
  This protects originals from basic-codec or unsupported-feature losses.

## Important limitations

Supported same-worksheet formulas are **calculated and recalculated** using the
Documents evaluator. Imported cached answers are replaced, and unsupported formulas
show explicit errors. Cross-sheet references and the complete Excel/ODF function
sets are not supported. Formula strings remain available for editing/export.
Renaming/removing a worksheet does not rewrite references. Auto/Text entry keeps
formula-like text literal; explicit Formula mode evaluates the supported subset.
Cross-format conversion is limited to Documents' common model.

There is no package-level lossless passthrough: charts, drawings, embedded objects,
macros, pivot tables, external links, conditional formatting, validations, print
settings and other XML parts outside the model can be dropped. Retaining an untouched
worksheet means retaining its **supported model data**, not every original package part.
CSV intentionally exports only the visible worksheet's formatted strings and does not retain
workbook metadata. Numbers entered in Auto/Number mode are exported as numeric values; Text mode preserves literal strings.

Browser limits remain 2 MiB per imported/exported file, 16,384 characters per visible
cell and 250,000 total visible characters at import/export. Browser-local recovery
stores **only visible worksheet strings**. Recovery restores a `.csv` document and
warns that other worksheets, typed values, formulas and formatting were not recovered.
Export a workbook copy separately; recovery is not a workbook backup.

## Completed P1: worksheet tabs and selection

A workbook owns independent `SheetModel` grids and import metadata per worksheet.
Selection does not mark the workbook modified; editing any sheet, adding, renaming
or removing does. Desktop and WebAssembly share these operations. Workbook exports
serialize all grids rather than projecting every edit into the first worksheet.

## Completed P1: typed editing and number formatting

The **Type** button cycles through Auto, Text, Number, Boolean, Date, Error and
Formula. Enter content in the edit line and choose **Apply value** to explicitly
convert the selected cell. Ordinary in-cell/edit-line commits also use the selected
entry mode. Changing mode alone does not reinterpret an untouched cell. Invalid
explicit values leave the old cell intact and report an error; correct or cancel
the entry before switching sheets/exporting.

- Auto recognizes invariant numbers, TRUE/FALSE, ISO `yyyy-MM-dd` dates and
  `yyyy-MM-ddTHH:mm:ss` date-times, and supported spreadsheet error tokens.
  Leading-zero identifiers such as `00123` remain text. A leading apostrophe forces
  text and is omitted from display/export. Formula-like input is literal in Auto.
- Text forces literal text. Number uses a finite invariant floating-point value.
  Boolean accepts TRUE/FALSE; Date uses the ISO forms above and a numeric spreadsheet
  date serial. Error accepts the supported Documents error tokens.
- Formula requires `=` and an expression. It stores the expression and calculates the supported subset; unsupported expressions show errors.
- The grid renders formatted text; editors show raw values or formula source.
  Number-format changes preserve raw values. Enter a format and use **Apply format**:
  examples include `General`, `0`, `0.00`, `#,##0.00`, `0.0%`, `$#,##0.00`,
  `yyyy-mm-dd`, `yyyy-mm-dd hh:mm:ss`, and `@`.
- XLSX export writes number-format style records. The ODS adapter writes basic
  numeric, percentage, dollar-currency, text and ISO date/time styles and restores
  those styles on import. Other custom ODS formats are refused before replacing
  a destination; use XLSX for those. General formatting follows Documents' formatter,
  and UI formatting uses invariant culture. Arbitrary locales, conditional formats,
  advanced custom ODF styles, 1904-calendar normalization and the fictitious Excel
  leap day are not complete interoperability claims.
- Typed dates, booleans and errors round-trip alongside numbers/text/formulas on
  all worksheets. A compatibility adapter handles the published preview.29 codecs'
  omissions in format writing and ODS date/error interpretation.
- CSV and browser-local recovery remain text projections; they do not retain typed
  values, formats, formulas or additional worksheets. Use XLSX/ODS for typed backups.

## Remaining roadmap

| Priority | Work | Completion criteria |
|---|---|---|
| Done | Worksheet tabs and selection | Switch/edit/add/remove/rename sheets with independent grids; see above. |
| Done | Typed editing and number formatting | Basic typed entry and formatting; interoperability limits are documented above. |
| Done (basic) | Formula integration | Documents evaluator, dependency ordering, cache refresh and error reporting for same-worksheet formulas. |
| P1 | Workbook-aware browser recovery | Versioned typed workbook snapshots with tests for formulas, additional sheets and restore compatibility. |
| P1 | Import/export diagnostics UI | Review all diagnostics in a dialog, report write warnings, and make feature-loss decisions before export. |
| P1 follow-up | Workbook formulas | Add cross-sheet references, rename/removal semantics and broader spreadsheet conformance. |
| P2 | Grid formatting and merges | Render/edit supported styles, shading, spans, dimensions, alignments and formats; define covered-cell editing behavior. |
| P2 | Larger/sparse sheets | Replace the dense 100×100 model and bound resource use while retaining worksheet positions. |
| P2 | Fidelity extensions | Expand Documents support or preserve unsupported package parts where safe; add Excel/LibreOffice interoperability fixtures. |
| P2 | Rich spreadsheet operations | Selections, range clipboard, structural row/column editing, undo/redo, named ranges, filtering and sorting. |

## Validation

Automated tests cover both codecs: typed/formula/extra-sheet retention, multi-sheet edits and structural round trips, tab selection, edit commits, confirmed removal, naming validation, sparse
coordinates, literal formula-like text, Unicode and multiline text, oversized-sheet
rejection, protection of desktop originals, and failed exports leaving files intact.
Windows and WebAssembly builds are checked. Browser pickers/downloads and visual
spreadsheet fidelity still need manual end-to-end verification.

See [Formula integration](formula-integration.md) for the evaluator assessment, supported functions, calculation limits and remaining work.

# Cells in the browser

The browser head uses the same sheet, controls and CSV stream serializer as the
desktop application. File menu/toolbar/keyboard commands are routed through an
optional `CellsView` host callback. A browser build never uses the desktop file
dialog or treats a WASM virtual path as a saved user file.

Open selects a `.csv` file and validates it completely before installing a new
sheet. Limits: 100 rows × 100 columns, 2 MiB per file, 16,384 characters per cell
and 250,000 aggregate characters at import/export. Oversized, rejected or partial
CSV input is refused instead of silently clipped. A failed/canceled open retains
the current sheet.

Save and Save As are labeled Export download / Export as in the browser menu.
They serialize real CSV bytes into a Blob download. `ExportInitiated` does not
prove persistence and does not clear modified state. New/Open/Close/Exit ask
whether to export and keep open, explicitly discard, or cancel. Navigation uses
the browser's best-effort unsaved-content warning. The Recovery panel now offers
browser-local IndexedDB checkpoints; export a separate backup because local data
can be evicted or denied. Pending cell-editor text is included without committing
it. Restoring creates a separate modified document and retains the source draft.
Undo history, selection and display widths/heights are not checkpointed.

Recovery has distinct page/document identities, bounded debounce, explicit
restore/export/import, confirmed deletion/clear and recoverable schema migration.
Preferences hold only the checkpoint switch. Clearing recovery data leaves the
open sheet intact and pauses automatic checkpoints. Up to 30 drafts are retained;
further drafts are refused with export/delete options, rather than auto-evicting
old content. See the enclosing `docs/broiler-office-web-w5.md` and run
`tests/office-web/verify-w5.ps1` for the recovery acceptance suite.

Values are strings: a formula-looking value is not calculated. CSV preserves its
literal text; other spreadsheet programs may interpret it as a formula. CSV does
not store formatting, column widths or additional sheets. XLSX, ODS, charts,
printing and PDF are not advertised. Clipboard operations cover one selected
cell. Use Ctrl/Cmd+C, X and V; menu/toolbar actions explain these shortcuts when
there is no active native clipboard event. Undo works inside the active text
editor; sheet-wide undo/redo is unavailable.

Normal publishing uses released NuGet dependencies. The new shared Hosting browser
contracts/adapter are still unpublished, so formal adoption remains a follow-up;
there is no production source-reference fallback. Physical IME/touch, Safari and
accessibility acceptance require device testing.

In the enclosing Office checkout, run `tests/office-web/verify-w4.ps1`. It pins
the SDK/workload, runs the xUnit v3 executable suite, publishes trimmed non-AOT
WASM with locked released dependencies and tests the published artifact in three
browser engines. See the enclosing `docs/broiler-office-web-w4.md` for detailed
scope and the generated `artifacts/web-w4/browser/report.json` for actual results.

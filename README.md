# Broiler Cells

Broiler Cells is a minimal spreadsheet application in the Broiler family: a grid
for displaying text, editing it, identifying cells by address, and saving/loading spreadsheets as CSV.

**Status: Version 3 implemented with Menu and CSV persistence, runnable on Windows.**
The application displays, edits, and annotates spreadsheets using coordinate labels (A–CV, 1–100),
active-cell address tracking, a standard menu bar (File and Help), and CSV table serialization powered
by `Broiler.Documents.Csv`. The model, serialization, and cell control are separate, platform-neutral libraries.

| Version | Scope | Maximum sheet size | Status |
| --- | --- | --- | --- |
| 1 | Very simple cell display | 10 rows × 10 columns (100 cells) | Verified; checks pass ([details](docs/version-1.md)) |
| 2 | Very simple cell editing | 100 rows × 100 columns (10,000 cells) | Verified; checks pass ([details](docs/version-2.md)) |
| 3 | Row and column labels; addresses such as A1 and B2 | 100 rows × 100 columns (10,000 cells) | Implemented; checks pass ([details](docs/version-3.md)) |
| Enhancement | Menu bar (File, Help) & CSV serialization | 100 rows × 100 columns (10,000 cells) | Implemented; checks pass |

The roadmap ends at Version 3. Each version builds on the previous one.

- [Roadmap and acceptance criteria](docs/roadmap.md)
- [Browser edition: file flows, safety, limits and verification](docs/browser-cells.md)
- [Application design and reusable cell control](docs/design.md)
- [Version 1 structure and verification](docs/version-1.md)
- [Version 2 structure and verification](docs/version-2.md)
- [Version 3 structure and verification](docs/version-3.md)

## Features

- **Menu Bar**:
  - **File**:
    - **New** (`Ctrl+N`): Creates a new spreadsheet.
    - **Open** (`Ctrl+O`): Prompts with standard file dialog to open and deserialize a CSV file.
    - **Save** (`Ctrl+S`): Saves to current CSV file or prompts Save as.
    - **Save as** (`Ctrl+Shift+S`): Prompts with standard file dialog to save CSV file.
    - **Close** (`Ctrl+W`): Closes current document and resets to empty session.
    - **Exit** (`Alt+F4`): Closes the application.
  - **Help**:
    - **About**: Displays standard Broiler About dialog with product and component version metadata.

- **Toolbar (like in Broiler Writer)**:
  - Standard toolbar with vector-drawn resolution-independent icons.
  - File actions: **New**, **Open**, **Save**, **Save as**.
  - Editing actions: **Cut** (`Ctrl+X`), **Copy** (`Ctrl+C`), **Paste** (`Ctrl+V`), **Clear** (`Del`).
  - Layout actions: **AutoFit** to automatically fit the selected column width to content.
  - Tooltips, button styling, and group breaks matching Broiler Writer.

- **Cell-Edit-Line**:
  - Dedicated formula and cell-editing bar positioned between the toolbar and cell grid.
  - Address / coordinate indicator showing the active cell (e.g., `A1`, `B2`).
  - `fx` formula marker.
  - Text editor field (`StandardEdit`) synchronizing in real time with the active cell.
  - Enter commits the edit to the cell; Escape cancels and reverts to original content.

- **Cross-Platform Implementations & Build Configurations**:
  - **Windows** (`Broiler.Windows.Cells.slnx`, `Broiler.Cells.Windows`): Direct2D and Win32 clipboard integration.
  - **Linux** (`Broiler.Linux.Cells.slnx`, `Broiler.Cells.Linux`): X11/OpenGL hardware-accelerated rendering and evdev input.
  - **WebAssembly** (`Broiler.WebAssembly.Cells.slnx`, `Broiler.Cells.WebAssembly`): Browser canvas rendering, JS interop, and HTML5 shell.
  - **Android** (`Broiler.Android.Cells.slnx`, `Broiler.Cells.Android`): Android native surface and touch input (`net10.0-android36.0`).
  - **Shared UI & Controls** (`Broiler.Cells.UI`, `Broiler.Cells.Controls`, `Broiler.Cells.Core`): Platform-neutral components.
- **Spreadsheet Serialization**:
  - `SheetCsvSerializer` in `Broiler.Cells.Core` converts between `SheetModel` and RFC 4180 CSV streams and files using `Broiler.Documents.Csv`.
  - Handles multiline cell text, quotes, commas, and cell coordinates.

- **Column and Row Resizing**:
  - Drag column dividers in the column header (`A`, `B`, `C`...) to resize column widths (clamped between 20px and 1000px).
  - Drag row dividers in the row header (`1`, `2`, `3`...) to resize row heights (clamped between 16px and 500px).
  - Hovering dividers provides visual highlighting and native cursor feedback (`ResizeHorizontal` and `ResizeVertical`).
  - Double-click a column divider to auto-fit its width to cell content.
  - Double-click a row divider to reset its height to default (28px).
  - Programmatic control via `SetColumnWidth`, `SetRowHeight`, `AutoFitColumn`, `ResetColumnWidth`, and `ResetRowHeight` with `ColumnResized` and `RowResized` events.

- **Row and Column Selection**:
  - Click a row number or column letter to select the whole row or column.
  - Shift-click to select a range from the last anchor; Ctrl-click to add or remove individual rows or columns. Ctrl+Shift-click adds a range.
  - Drag across headers to select a range. Ctrl-drag preserves the existing selection.
  - Drag across data cells to select a rectangle. Shift-click or Shift-drag extends from the original cell anchor; dragging without Shift starts a new selection.
  - Selected headers and cells are highlighted. Delete clears all selected cells; typing or F2 edits the active cell.
  - Clicking a cell without Shift or using an arrow key returns to single-cell selection. Switching between row and column headers starts a selection on the new axis.
  - `SelectRow`, `SelectColumn`, `SelectedRows`, `SelectedColumns`, `IsCellSelected`, and `SelectionChanged` expose selection through the reusable control. Indices are zero-based; selection is disabled in read-only mode.

The shared control provides display, selection, in-place editing, coordinate labels,
and resizable columns/rows so Broiler Writer can embed a small table or sheet with or without headers.
Writer integration is a design consideration, not another release or an
implementation commitment in this roadmap.

## Build, run, and test

Requires Windows and the .NET 10.0.4xx SDK. Packages restore from NuGet.org; a sibling
Broiler checkout is not required.

```powershell
dotnet build Broiler.Cells.slnx -c Release
dotnet run --project src/Broiler.Cells.Windows -c Release
dotnet test tests/Broiler.Cells.Tests -c Release
```

Resize the window to expose scrollbars. Drag their thumbs, use the mouse wheel
(Shift+wheel horizontally), or Tab into an overflowing viewport and use arrow or
Page keys to scroll.

Click a cell to select it. Navigate with arrow keys. Double-click or press F2 to
edit the cell in place. Typing while a cell is selected replaces its contents.
Press Enter to commit an edit, Escape to cancel, or Delete to clear the selected
cells.

The sample is defined in `CellsWindow.cs`; sheets up to 100 × 100 can be supplied
through `new SheetModel(rows, columns, initialValue)`.

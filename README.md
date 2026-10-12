# Broiler Cells

Broiler Cells is a minimal spreadsheet application in the Broiler family: a grid
for displaying text, then editing it, then identifying cells by address.

**Status: Version 1 scaffolded and runnable on Windows.** The application displays
a read-only 10 × 10 sample sheet using Broiler.UI and Direct2D. The model and grid
control are separate, platform-neutral libraries.

| Version | Scope | Maximum sheet size | Status |
| --- | --- | --- | --- |
| 1 | Very simple cell display | 10 rows × 10 columns (100 cells) | Scaffolded; checks pass |
| 2 | Very simple cell editing | 100 rows × 100 columns (10,000 cells) | Planned |
| 3 | Row and column labels; addresses such as A1 and B2 | 100 rows × 100 columns (10,000 cells) | Planned |

The roadmap ends at Version 3. Each version builds on the previous one.

- [Roadmap and acceptance criteria](docs/roadmap.md)
- [Application design and reusable cell control](docs/design.md)
- [Version 1 structure and verification](docs/version-1.md)

The shared control currently provides display only. Editing and coordinate-label
options remain planned so Broiler Writer can eventually embed a small table or
sheet. Writer integration is a design consideration, not another release or an
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
Page keys to scroll. Clicking a cell does not select or edit it.

The sample is defined in `CellsWindow.cs`; smaller sheets can be supplied through
`new SheetModel(rows, columns, initialValue)`. Version 1 has no data-entry or file
opening interface, and all values are held in memory.

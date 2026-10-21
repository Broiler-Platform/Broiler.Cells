# Version 3 — Very simple cell annotations

This record describes the original Version 3 milestone. Later enhancements add
row and column selection through the headers; see [current selection behavior](../README.md#features).

The Windows application is runnable with coordinate annotations and active cell
address display for sheets up to 100 × 100 cells. It provides frozen column headers
(A–CV), frozen row headers (1–100), active-cell address tracking in the status
strip alongside **Session only**, and an option to hide headers for embedded table
use cases.

## Structure

| Project | Responsibility |
| --- | --- |
| `src/Broiler.Cells.Core` | `SheetModel`: bounded in-memory array of strings up to 100 × 100 cells. `CellPosition`: zero-based coordinates, column label formatting (`GetColumnLabel`: 0 → A, 25 → Z, 26 → AA, 51 → AZ, 52 → BA, 99 → CV), address formatting (`ToAddress`: A1 to CV100), and case-insensitive address parsing (`TryParseAddress`). Zero UI dependencies. |
| `src/Broiler.Cells.Controls` | `CellControl`: composite Broiler.UI element combining frozen column headers, frozen row headers, corner element, `StandardScrollView`, and `CellGrid`. Synchronizes header culling and offsets during scrolling. Supports toggling `AreHeadersVisible` for embedded tables. Pure label semantics (clicking headers does not select rows or columns). Does not consume data-cell space. |
| `src/Broiler.Cells.Windows` | Native Direct2D window and `CellsView`: displays initial selection `B2` matching the design wireframe, updates active address dynamically on cell navigation, and shows "Session only" status on the right. |
| `tests/Broiler.Cells.Tests` | 72 automated unit tests covering column label translations, 100 × 100 first/last addresses, active address notifications, frozen header synchronization, header click immunity, embedded table header toggle, and keyboard navigation. |

## Verification — 12 October 2026

- Release solution build: **passed, zero warnings and errors** on .NET SDK 10.0.401.
- Automated tests: **72 passed**. Covers:
  - Column label transitions: `0 → A`, `25 → Z`, `26 → AA`, `51 → AZ`, `52 → BA`, `99 → CV` (the 100th column).
  - First and last addresses of a 100 × 100 sheet are `A1` and `CV100`.
  - Selecting row 2, column 2 shows `B2`; navigation and scrolling keep the address and headers aligned.
  - Header space does not count toward the 100 × 100 data-cell limit (full 10,000 data cells available).
  - Hiding headers (`AreHeadersVisible = false`) preserves cell values, layout, and editing behavior for embedded tables.
  - Headers act strictly as labels; pointer clicks on corner, column, or row headers do not alter cell selection.
  - Keyboard navigation (arrows) updates the address in lockstep (e.g., `B2 → C2 → C3 → B3 → B2`).
  - Parsing spreadsheet addresses is case-insensitive, trims whitespace, and rejects invalid formats.
- Native Windows launch: verified with initial selection at `B2`, visible coordinate headers A–CV and 1–100, active address on the left and "Session only" on the right.

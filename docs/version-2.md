# Version 2 — Cell editing

The Windows application is runnable with in-place cell editing for sheets up to
100 × 100 cells. It provides cell selection with a visible focus outline, in-place
editing using Broiler.UI's `StandardEdit`, four-corner keyboard navigation, typing
replacement, Enter commit, Escape cancel, Delete clear, and a **Session only**
status strip.

## Structure

| Project | Responsibility |
| --- | --- |
| `src/Broiler.Cells.Core` | `SheetModel`: mutable in-memory strings, zero-based indexing, dimensions validated within configurable limits (default 1–100, Version 1 configuration 1–10 before allocation). `CellPosition` struct. No UI references. |
| `src/Broiler.Cells.Controls` | `CellGrid`: shared Broiler.UI element, fixed 96 × 28 DIP cells, cell selection outline, in-place `StandardEdit` integration, commit/cancel/replace/clear lifecycle, four-corner keyboard navigation, and accessibility support. `CellCommittedEventArgs`. |
| `src/Broiler.Cells.Windows` | Native Direct2D window, sample model, scroll viewport, "Session only" status strip, text input routing, and Windows automation bridge. |
| `tests/Broiler.Cells.Tests` | 33 automated tests covering model dimension limits, 100 × 100 sheet editing, selection, F2 and double-click editing, Enter commit, Escape cancel, typing replacement, Delete clearing, four-corner boundary navigation, scrolling persistence, resize alignment, and accessibility. |

## Verification — 12 October 2026

- Release solution build: **passed, zero warnings and errors** on .NET SDK 10.0.401.
- Automated tests: **33 passed**. Covers:
  - 100 × 100 sheet selection, in-place editing, Enter commit, Escape cancel, typing replacement, and Delete clear.
  - Cancel preserving old value while commit updates only the edited cell.
  - Committed values remaining intact after scrolling out of view and back.
  - Keyboard navigation reaching all four sheet corners without leaving boundaries.
  - Dimension of 101 being rejected, and Version 1's 10 × 10 limit remaining enforced under its configuration.
  - Editor and selection remaining aligned with cells after scrolling and resizing.
  - Read-only mode configuration preventing editing and selection.
  - Plain-text treatment of input starting with `=` and numbers.
- Native Windows launch: verified with "Session only" status and in-place editing.

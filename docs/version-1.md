# Version 1 scaffold

The Windows application is runnable. It displays one read-only 10 × 10 sample
sheet, with the Broiler Writer light palette, clipped single-line text, scrolling,
and a **Read only** status. It has no selected cell, editor, or coordinate headers.

## Structure

| Project | Responsibility |
| --- | --- |
| `src/Broiler.Cells.Core` | `SheetModel`: immutable strings, zero-based indexing, and dimensions validated within 1–10 before allocation. No UI references. |
| `src/Broiler.Cells.Controls` | `CellGrid`: shared Broiler.UI element, fixed 96 × 28 DIP cells, theme colors/font, per-cell clipping, and read-only accessibility labels. No native or application-shell references. |
| `src/Broiler.Cells.Windows` | Native Direct2D window, sample model, scroll viewport, status, input bridge, and Windows accessibility bridge. |
| `tests/Broiler.Cells.Tests` | Model, rendering, clipping, scrolling, resize, input, and semantic checks. |

`CellGrid` takes a `SheetModel` in its constructor. Its drawing stays in one
control; lightweight non-editable children give each cell a stable accessibility
identity. Only visible cell text is drawn. Broiler.UI's `StandardScrollView` owns
scrolling and keyboard focus when the sheet overflows the viewport.

An embedding host can compose the same control:

```csharp
var sheet = new SheetModel(2, 2, (row, column) => (row, column) switch
{
    (0, 0) => "Item",
    (0, 1) => "Quantity",
    (1, 0) => "Paper",
    (1, 1) => "12",
    _ => string.Empty,
});
var scroll = new StandardScrollView { FocusWhenScrollable = true };
scroll.AddChild(new CellGrid(sheet));
```

The types are in `Broiler.Cells.Core`, `Broiler.Cells.Controls`, and
`Broiler.UI.ScrollView.Standard`. The host attaches the scroll view to its own
Broiler.UI tree/session and supplies layout and a theme. There are no unused
editing or header flags in the Version 1 API.

## Verification — 12 October 2026

- Release solution build: **passed, zero warnings and errors** on .NET SDK 10.0.401.
- Automated tests: **20 passed**. Covers 1 × 1 and 10 × 10 rendering, invalid dimensions
  and indices, retained values, single-line display, per-cell clip commands, actual
  raster pixels in an empty neighboring cell, final-cell reachability, resize
  offset clamping, read-only input, and accessibility names.
- Native Windows launch: **passed**. The default 10 × 10 grid, sample values, long
  text clipping, and status were visually inspected. Windows UI Automation exposes
  each cell's position and value. Closing the window ended the process.
- Small-viewport scrolling and resize are covered by automated checks. Native
  scrollbar dragging, physical DPI changes, and screen-reader speech have not yet
  been manually accepted.

This is the scaffold for the requested first milestone. It adds no Writer
integration, persistence, editing, or coordinate annotations. Versions 2 and 3
remain unchanged in scope.

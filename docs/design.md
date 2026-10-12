# Broiler Cells design

Version 1's display model and control are scaffolded; editing, selection, and
coordinate labels below remain proposed. The [implementation record](version-1.md)
describes the current API. The [roadmap](roadmap.md) defines the complete release scope.

## Application and Broiler identity

Use **Broiler Cells** as the window title and visible product name; use
`Broiler.Cells` for repository and code namespaces. Follow Broiler Writer's quiet
desktop appearance: a light gray canvas, white content, dark text, neutral grid
lines, and a blue outline for the active cell.

The light appearance derives from Writer's existing `WriterPalette`:

| Role | Color |
| --- | --- |
| Canvas | `#E7EBF0` |
| Cells | `#FFFFFF` |
| Primary text | `#1E2A36` |
| Secondary text | `#5F6E7D` |
| Active cell / focus | `#2A73C5` |
| Grid / content border | `#CCD2D8` |

Use the standard Broiler.UI font, with Segoe UI on Windows. Start with 14-DIP text,
96-DIP columns, and 28-DIP rows; these are design defaults, scaled through the
toolkit. The shared control consumes theme roles so an embedding host can supply
its own appearance. The active cell uses a visible outline as well as color.

Keep the window to a title, grid, and one thin status strip. No ribbon, formula
bar, sheet tabs, or placeholder commands are needed for these versions. The grid
fills the available content area, with scrollbars only when necessary. The shared
control handles keyboard focus and identifies its cells through the toolkit's
accessibility contracts as part of the corresponding display/editing work.

This Version 3 wireframe shows a small viewport into a sheet. Column letters and
row numbers are outside the editable cells; B2 is the selected cell. Versions 1
and 2 use the same arrangement without the coordinate headers or address.

```text
┌──────────────────────────────────────────────────┐
│ Broiler Cells                                    │
├─────┬───────────┬───────────┬───────────┬──────────┤
│     │     A     │     B     │     C     │    D     │
├─────┼───────────┼───────────┼───────────┼──────────┤
│  1  │ Item      │ Quantity  │           │          │
├─────┼───────────╔═══════════╗───────────┼──────────┤
│  2  │ Paper     ║ 12        ║           │          │
├─────┼───────────╚═══════════╝───────────┼──────────┤
│  3  │ Pens      │ 4         │           │          │
├─────┼───────────┼───────────┼───────────┼──────────┤
│  4  │           │           │           │          │
├─────┴───────────┴───────────┴───────────┴──────────┤
│ B2                                  Session only │
└──────────────────────────────────────────────────┘
```

The sample values are illustrative text, without number types or calculations.

## Small reusable cell control

Keep three responsibilities separate within the implementation:

| Part | Responsibility |
| --- | --- |
| Sheet model | Row/column counts, a bounded array of strings, value access and changes; no UI or platform dependency. |
| Cell control | Grid drawing, scrolling, hit testing, selection, one in-place editor, and optional coordinate headers. |
| Application host | Broiler Cells title, status, version limits, initial data, and window lifetime. |

The model enforces its configured dimension limit before allocating storage. Use
zero-based indices internally and convert to one-based row numbers and letter
columns only for display. A maximum of 10,000 strings does not require a database
or a complex sparse-sheet engine.

Draw the grid as one control, painting the visible cells. In Version 2, place one
reusable `StandardEdit` over the cell being edited, rather than creating 10,000
text-box controls. Reuse Broiler.UI's input, theme, and scrolling infrastructure.
In Version 3, position the headers outside the scrolling cell viewport and keep
their labels synchronized with its offsets.

Only add a control option when its version needs it:

| Configuration | Version 1 | Version 2 | Version 3 | Possible Writer host |
| --- | --- | --- | --- | --- |
| Maximum rows / columns | 10 / 10 | 100 / 100 | 100 / 100 | Chosen by host, within 100 / 100 |
| Editing enabled | No | Yes | Yes | Host chooses |
| Coordinate headers visible | No | No | Yes | Usually no |
| Active address in app status | No | No | Yes | Host chooses |

The control exposes the active position and committed value changes to its host.
The host formats the address for its status strip; the control does not own an
application title, menus, file operations, or Writer document commands. The
editing option also allows a read-only embedded table. Avoid a general feature
registry or plugin framework for these few options.

Writer could supply a small sheet model and embed this control with headers
hidden, using the same display and editing behavior. Keep the control in a
reusable library in this repository, separate from the executable; Writer would
reference that library, not the Cells application. The scaffold uses
`Broiler.Cells.Core` and `Broiler.Cells.Controls`; no public NuGet release is
created by this scaffold.

Writer already has document-table layout in its rich-text editor. This control is
an embedding option for simple grids; connecting it to Writer's document model,
pagination, undo, or saved formats would require separate integration work. None
of that work is a prerequisite or an additional milestone in this roadmap.

## Existing code informing the proposal

These are source reference points in the sibling Broiler checkout. The scaffold
consumes published NuGet packages and does not require that checkout:

- [Writer palette](../../Broiler/Broiler.Writer/src/Broiler.Writer/WriterPalette.cs)
- [Writer application shell](../../Broiler/Broiler.Writer/src/Broiler.Writer/WriterApp.cs)
- [Broiler.UI control conventions](../../Broiler/Broiler.UI/README.md)
- [Standard theme roles](../../Broiler/Broiler.UI/src/Foundation/Broiler.UI.Standard/Theme/StandardThemeTokens.cs)
- [Standard text editor](../../Broiler/Broiler.UI/src/Implementations/Standard/Text/Broiler.UI.Edit.Standard/StandardEdit.cs)

These reference links assume `Broiler.Cells` and `Broiler` are sibling checkouts.

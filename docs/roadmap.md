# Broiler Cells roadmap

Status: **Version 3 implemented**. All three roadmap milestones are complete.
Automated checks pass and the native default window has been visually checked;
see the [Version 1 record](version-1.md), [Version 2 record](version-2.md), and
[Version 3 record](version-3.md). Version labels describe product milestones,
without assigning release dates or package version numbers. This roadmap contains
exactly three versions.

## Scope

One sheet, plain-text cells, and a small Broiler-branded window. Start with a
Windows desktop host using the existing .NET/Broiler.UI stack; keep the model and
control independent of that host. This is the Version 1 scaffold's implementation.

Dimensions are the total sheet size, not the number of cells visible on screen.
Each dimension must be at least 1 and must not exceed the version's limit. The host
chooses dimensions when creating the sheet; there is no sheet-size editor. Use a
10 × 10 sheet by default in every version; Versions 2 and 3 also support sheets
up to 100 × 100.

For these three versions, values live in memory for the current session. Versions
2 and 3 show **Session only** beneath the grid. File storage, import/export,
formulas, numeric calculation, formatting, merged cells, multiple sheets, cell
comments, and collaboration are outside this roadmap.

## Version 1 — Very simple cell display

**Outcome:** open Broiler Cells and see a small, read-only grid.

- Display between 1 × 1 and **10 × 10 cells** from an in-memory model.
- Draw plain text, empty cells, and thin grid lines at a uniform cell size.
- Clip long text inside its own cell; keep the full value in the model.
- Show the product name **Broiler Cells** and a small **Read only** status.
- Allow scrolling when the window is smaller than the sheet.
- Omit editing, active-cell selection, row/column labels, and cell addresses.

**Acceptance**

- [x] A 1 × 1 sheet and a 10 × 10 sheet render their supplied values correctly.
- [x] Empty cells stay visible; long text does not draw into adjacent cells.
- [x] A smaller viewport still allows every cell to be viewed (automated layout/scroll check).
- [x] Pointer and keyboard input cannot change a cell value.
- [x] Zero, negative, or greater-than-10 dimensions are rejected before allocation.

## Version 2 — Very simple cell editing

**Outcome:** select one cell, enter plain text, and change its value.

- Increase the maximum to **100 × 100 cells**.
- Click a cell to select it; use arrow keys to move selection while not editing.
- Double-click or press F2 to edit the current value in place. Typing while a cell
  is selected starts an edit with the typed text replacing its old value.
- Enter commits the edit and leaves that cell selected. Escape cancels the edit
  and restores its original value. Clicking another cell commits before selecting
  it; losing application focus preserves the pending edit.
- Delete clears the selected cell while not editing. During editing, normal text
  input and caret behavior belong to the text editor.
- Treat all content as single-line text, including numbers and text beginning
  with `=`. Reuse the toolkit's single-line editor behavior.
- Keep the selected cell visible when navigating; stop at the sheet boundaries.
- Retain the unlabeled grid from Version 1. Coordinate labels arrive in Version 3.

**Acceptance**

- [x] Select, edit, commit, cancel, replace, and clear work on a 100 × 100 sheet.
- [x] Cancel preserves the old value; commit changes only the edited cell.
- [x] Committed values remain correct after scrolling away and back.
- [x] Keyboard navigation reaches all four corners without leaving the sheet.
- [x] A dimension of 101 is rejected, and Version 1's 10 × 10 limit remains enforced
  when running its configuration.
- [x] The editor and selection remain aligned with cells after scrolling/resizing.

## Version 3 — Very simple cell annotations

**Outcome:** identify a cell through familiar spreadsheet coordinates.

Here, **annotations means coordinate labels**: column letters, row numbers, and
an active-cell address. It does not mean comments or notes attached to cells.

- Retain Version 2 editing and its **100 × 100** maximum.
- Add column headers **A–CV** and row headers **1–100**, showing only the labels
  needed for the sheet's actual dimensions.
- Keep these headers visible while the cell area scrolls.
- Show the selected cell address, such as **A1** or **B2**, in the status strip.
- Treat headers as labels; clicking one does not select a whole row or column.
- Keep labels outside the cell data, so they never consume a row or column.

**Acceptance**

- [x] Column labels transition correctly: Z → AA, AZ → BA, and the 100th column is CV.
- [x] The first and last addresses of a 100 × 100 sheet are A1 and CV100.
- [x] Selecting row 2, column 2 shows B2; navigation and scrolling keep the address
  and headers aligned with the selected cell.
- [x] Header space does not count toward the 100 × 100 data-cell limit.
- [x] Hiding headers for an embedded table preserves its values and editing behavior.

Implementation stops when Version 3 meets these criteria. No further versions are
planned here. See the [design](design.md) for the visual direction and control boundary.

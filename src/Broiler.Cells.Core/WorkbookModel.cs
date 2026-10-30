namespace Broiler.Cells.Core;

/// <summary>An editable workbook whose sheets retain their own grid and import metadata.</summary>
public sealed class WorkbookModel
{
    private readonly List<WorksheetModel> _sheets = [];
    public IReadOnlyList<WorksheetModel> Sheets => _sheets;
    public int SelectedIndex { get; private set; }
    public WorksheetModel SelectedSheet => _sheets[SelectedIndex];
    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;
    public event EventHandler? StructureChanged;

    public WorkbookModel(SheetModel initial, string name = "Sheet1")
    {
        AddCore(initial, name);
    }

    private WorksheetModel AddCore(SheetModel sheet, string name)
    {
        ValidateName(name, null);
        if (_sheets.Any(s => ReferenceEquals(s.Sheet, sheet)))
            throw new ArgumentException("The same grid cannot be added twice.");
        if (sheet.Workbook is not null && sheet.Workbook != this)
            throw new ArgumentException("The sheet already belongs to a workbook.");
        var entry = new WorksheetModel(name.Trim(), sheet);
        _sheets.Add(entry);
        sheet.Workbook = this;
        sheet.Changed += OnCellChanged;
        return entry;
    }

    private void OnCellChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    public void Select(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _sheets.Count);
        if (SelectedIndex == index) return;
        SelectedIndex = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public WorksheetModel Add(string? name = null, SheetModel? sheet = null)
    {
        if (name is null)
        {
            int number = 1;
            do { name = "Sheet" + number++; } while (_sheets.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));
        }
        var added = AddCore(sheet ?? new SheetModel(), name);
        StructureChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
        Select(_sheets.Count - 1);
        return added;
    }

    public void Rename(int index, string name)
    {
        var sheet = _sheets[index];
        ValidateName(name, sheet);
        sheet.Name = name.Trim();
        StructureChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(int index)
    {
        if (_sheets.Count == 1) throw new InvalidOperationException("A workbook must retain at least one worksheet.");
        var removed = _sheets[index];
        var selected = SelectedSheet;
        removed.Sheet.Changed -= OnCellChanged;
        removed.Sheet.Workbook = null;
        _sheets.RemoveAt(index);
        SelectedIndex = removed == selected ? Math.Min(index, _sheets.Count - 1) : _sheets.IndexOf(selected);
        StructureChanged?.Invoke(this, EventArgs.Empty);
        StructureChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ValidateName(string name, WorksheetModel? except)
    {
        ArgumentNullException.ThrowIfNull(name);
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 31 || trimmed.IndexOfAny("[]:*?/\\".ToCharArray()) >= 0
            || trimmed.StartsWith('\'') || trimmed.EndsWith('\'') || trimmed.Any(char.IsControl))
            throw new ArgumentException("Worksheet names must have 1–31 characters and cannot contain []:*?/\\ or begin/end with an apostrophe.");
        if (_sheets.Any(s => s != except && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A worksheet with that name already exists.");
    }
}

public sealed class WorksheetModel
{
    internal WorksheetModel(string name, SheetModel sheet) { Name = name; Sheet = sheet; }
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Name { get; internal set; }
    public SheetModel Sheet { get; }
}

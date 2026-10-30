using System;
using System.IO;
using System.Threading.Tasks;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.AboutDialog.Standard;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.FileDialog;
using Broiler.UI.FileDialog.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.TabView.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Window;

namespace Broiler.Cells.UI;

/// <summary>
/// The main spreadsheet view combining Menu bar, Toolbar, Cell-Edit-Line,
/// Cell grid, and Status bar.
/// </summary>
public class CellsView : UiElement
{
    public static readonly BColor Canvas = CellsPalette.Canvas;
    public static readonly StandardThemeTokens Theme = StandardThemeTokens.Light with
    {
        Surface = BColor.FromArgb(255, 255, 255, 255),
        Text = BColor.FromArgb(255, 30, 42, 54),
        TextMuted = BColor.FromArgb(255, 95, 110, 125),
        Border = BColor.FromArgb(255, 204, 210, 216),
        Accent = BColor.FromArgb(255, 42, 115, 197),
        FocusRing = BColor.FromArgb(255, 42, 115, 197),
        FontBody = new BFontStyle("Segoe UI", 14),
    };

    private readonly StandardMenu _menu;
    private readonly StandardToolbar _toolbar;
    private readonly CellEditLine _cellEditLine;
    private readonly CellControl _cellControl;
    private readonly StandardLabel _address;
    private readonly StandardLabel _status;
    private readonly StandardCommandDispatcher _dispatcher;
    private readonly bool _compactMode;

    private string? _currentFilePath;
    private readonly Action<string>? _fileAction;
    private string? _importedWorkbookPath;
    private readonly StandardTabView _sheetTabs = new() { HeaderHeight = 32, AccessibleName = "Worksheets" };
    private readonly StandardEdit _sheetName = new() { AccessibleName = "Worksheet name" };
    private readonly StandardButton _addSheet = new() { Text = "+", AccessibleName = "Add worksheet" };
    private readonly StandardButton _renameSheet = new() { Text = "Rename" };
    private readonly StandardButton _removeSheet = new() { Text = "Remove" };
    private readonly System.Collections.Generic.Dictionary<string, CellPosition> _sheetSelections = [];
    private WorkbookModel _workbook = null!;
    private WorksheetModel? _displayedWorksheet;
    private readonly StandardButton _previousSheet = new() { Text = "‹", AccessibleName = "Previous worksheet" };
    private readonly StandardButton _nextSheet = new() { Text = "›", AccessibleName = "Next worksheet" };
    private int _tabPageSize = 4;
    private bool _updatingTabs;
    private readonly StandardButton _entryModeButton = new() { Text = "Type: Auto", AccessibleName = "Cell entry mode" };
    private readonly StandardButton _applyValueButton = new() { Text = "Apply value" };
    private readonly StandardEdit _numberFormat = new() { Text = "General", AccessibleName = "Number format" };
    private readonly StandardButton _applyFormatButton = new() { Text = "Apply format" };
    private string? _pendingRemoval;


    public CellsView(SheetModel sheet, bool compactMode = false, Action<string>? fileAction = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _compactMode = compactMode;
        _fileAction = fileAction;
        AccessibleName = "Broiler Cells";

        _dispatcher = new StandardCommandDispatcher();
        _dispatcher.Add(new StandardCommand("file.new", NewDocument));
        _dispatcher.Add(new StandardCommand("file.open", () => _ = OpenDocumentAsync()));
        _dispatcher.Add(new StandardCommand("file.save", SaveDocument));
        _dispatcher.Add(new StandardCommand("file.save-as", () => _ = SaveDocumentAsAsync()));
        _dispatcher.Add(new StandardCommand("file.close", CloseDocument));
        _dispatcher.Add(new StandardCommand("file.exit", ExitApplication));
        _dispatcher.Add(new StandardCommand("edit.cut", CutSelection));
        _dispatcher.Add(new StandardCommand("edit.copy", CopySelection));
        _dispatcher.Add(new StandardCommand("edit.paste", PasteSelection));
        _dispatcher.Add(new StandardCommand("edit.clear", ClearSelection));
        _dispatcher.Add(new StandardCommand("help.about", () => ShowAbout()));
        _dispatcher.Add(new StandardCommand("help.info", () => ShowAbout()));

        _menu = CreateMenu();
        _toolbar = CreateToolbar();

        _cellControl = new CellControl(sheet, isEditingEnabled: true, areHeadersVisible: true);
        _cellControl.SelectedCellChanged += OnSelectedCellChanged;
        _cellControl.CellCommitted += (_, _) => UpdateCalculationStatus();

        _cellEditLine = new CellEditLine();
        _cellEditLine.Attach(_cellControl);

        _address = new StandardLabel(_cellControl.SelectedAddress ?? string.Empty, StandardLabelRole.Default)
        {
            Font = new BFontStyle("Segoe UI", 12)
        };
        _status = new StandardLabel("Session only", StandardLabelRole.Muted)
        {
            Font = new BFontStyle("Segoe UI", 12)
        };

        AddChild(_menu);
        AddChild(_toolbar);
        AddChild(_cellEditLine);
        AddChild(_cellControl);
        AddChild(_address);
        AddChild(_status);
        AddChild(_entryModeButton);
        AddChild(_applyValueButton);
        AddChild(_numberFormat);
        AddChild(_applyFormatButton);
        _entryModeButton.Clicked += (_, _) => SetEntryMode((CellEntryMode)(((int)_cellControl.Grid.EntryMode + 1) % Enum.GetValues<CellEntryMode>().Length));
        _applyValueButton.Clicked += (_, _) => ApplySelectedInput(_cellEditLine.Text);
        _applyFormatButton.Clicked += (_, _) => TryWorksheetOperation(() => ApplySelectedNumberFormat(_numberFormat.Text));
        _numberFormat.Submitted += (_, _) => TryWorksheetOperation(() => ApplySelectedNumberFormat(_numberFormat.Text));
        _cellControl.Grid.InputRejected += (_, message) => { _status.Text = message; Invalidate(UiInvalidationKind.Render); };
        AddChild(_sheetTabs);
        AddChild(_previousSheet);
        AddChild(_nextSheet);
        AddChild(_sheetName);
        AddChild(_addSheet);
        AddChild(_renameSheet);
        AddChild(_removeSheet);
        _sheetTabs.SelectionChanged += (_, _) => { if (!_updatingTabs && _sheetTabs.SelectedTab is { } tab) TryWorksheetOperation(() => SelectWorksheet(_workbook.Sheets.ToList().FindIndex(s => s.Id == tab.Id))); };
        _sheetTabs.Reordering += (_, e) => e.Cancel = true;
        _sheetTabs.CloseRequested += (_, e) => { SelectWorksheet(_workbook.Sheets.ToList().FindIndex(s => s.Id == e.Id)); RequestRemoveWorksheet(); };
        _previousSheet.Clicked += (_, _) => TryWorksheetOperation(() => SelectWorksheet((_workbook.SelectedIndex + _workbook.Sheets.Count - 1) % _workbook.Sheets.Count));
        _nextSheet.Clicked += (_, _) => TryWorksheetOperation(() => SelectWorksheet((_workbook.SelectedIndex + 1) % _workbook.Sheets.Count));
        _addSheet.Clicked += (_, _) => TryWorksheetOperation(AddWorksheet);
        _renameSheet.Clicked += (_, _) => TryWorksheetOperation(() => RenameWorksheet(_sheetName.Text));
        _sheetName.Submitted += (_, _) => TryWorksheetOperation(() => RenameWorksheet(_sheetName.Text));
        _removeSheet.Clicked += (_, _) => RequestRemoveWorksheet();
        SetDocument(sheet);

    }

    public StandardMenu Menu => _menu;
    public StandardToolbar Toolbar => _toolbar;
    public CellEditLine CellEditLine => _cellEditLine;
    public CellControl CellControl => _cellControl;
    public StandardLabel AddressLabel => _address;
    public StandardLabel StatusLabel => _status;
    public StandardCommandDispatcher CommandDispatcher => _dispatcher;
    public string AddressText => _address.Text;
    public string StatusText => _status.Text;
    public string? CurrentFilePath => _currentFilePath;
    public UiWindow? OwnerWindow { get; set; }
    public Action<Uri>? OpenUrl { get; set; }
    public Func<BSize>? ViewportProvider { get; set; }
    /// <summary>Returns true when the host handles a clipboard command.</summary>
    public Func<string, bool>? ClipboardCommand { get; set; }

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? TitleChanged;

    public WorkbookModel Workbook => _workbook;
    public StandardTabView WorksheetTabs => _sheetTabs;

    /// <summary>Install a complete document, including imported worksheets and metadata.</summary>
    public void SetDocument(SheetModel sheet)
    {
        if (_workbook is not null)
        {
            _workbook.StructureChanged -= WorkbookStructureChanged;
            _workbook.SelectionChanged -= WorkbookSelectionChanged;
        }
        _workbook = sheet.Workbook ?? new WorkbookModel(sheet);
        _workbook.StructureChanged += WorkbookStructureChanged;
        _workbook.SelectionChanged += WorkbookSelectionChanged;
        _displayedWorksheet = null;
        _sheetSelections.Clear();
        ShowWorksheet();
        RefreshWorksheetTabs();
    }

    private void CommitWorksheetEdit()
    {
        _cellControl.CommitEdit();
        if (_cellControl.Grid.LastInputError is { } gridError) throw new InvalidOperationException(gridError);
        _cellEditLine.CommitEdit();
        if (_cellControl.Grid.LastInputError is { } lineError) throw new InvalidOperationException(lineError);
    }

    private bool _reportedFormulaIssue;
    public void RecalculateWorkbook()
    {
        CommitWorksheetEdit();
        foreach (var worksheet in _workbook.Sheets) worksheet.Sheet.Recalculate();
        UpdateCalculationStatus();
        Invalidate(UiInvalidationKind.Render | UiInvalidationKind.Semantic);
    }

    private void UpdateCalculationStatus()
    {
        var issues = _cellControl.Sheet.FormulaIssues;
        if (issues.Count > 0)
        {
            var first = issues[0];
            _status.Text = $"{issues.Count} formula error(s): {first.Cell.ToAddress()} — {first.Message}";
            _reportedFormulaIssue = true;
        }
        else if (_reportedFormulaIssue)
        {
            _status.Text = "Formula errors resolved; worksheet recalculated.";
            _reportedFormulaIssue = false;
        }
    }

    public CellEntryMode EntryMode => _cellControl.Grid.EntryMode;
    public void SetEntryMode(CellEntryMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _cellControl.Grid.EntryMode = mode;
        _entryModeButton.Text = "Type: " + mode;
        _status.Text = mode == CellEntryMode.Formula ? "Formula entry: begin with =. Supported same-worksheet formulas calculate automatically." : "Cell entry mode: " + mode;
        Invalidate(UiInvalidationKind.Render);
    }

    public void ApplySelectedInput(string input)
    {
        if (_cellControl.SelectedCell is not { } selected) return;
        _cellControl.Grid.SetCellValue(selected, input, force: true);
        if (_cellControl.Grid.LastInputError is null) _cellEditLine.CancelEdit();
    }

    public void ApplySelectedNumberFormat(string? format)
    {
        CommitWorksheetEdit();
        if (_cellControl.SelectedCell is not { } selected) return;
        _cellControl.Sheet.SetNumberFormat(selected.Row, selected.Column, format);
        _cellControl.Grid.RefreshCellDisplay(selected);
        _cellEditLine.CancelEdit();
        _status.Text = "Format applied. Raw values are retained; supported formulas calculate automatically.";
        Invalidate(UiInvalidationKind.Render);
    }

    public void SelectWorksheet(int index)
    {
        CommitWorksheetEdit();
        _workbook.Select(index);
    }

    public void AddWorksheet()
    {
        CommitWorksheetEdit();
        _workbook.Add();
    }

    public void RenameWorksheet(string name)
    {
        _workbook.Rename(_workbook.SelectedIndex, name);
        _status.Text = "Worksheet renamed. Formula references are unchanged; review cross-sheet formulas before recalculating.";
    }

    public void RemoveWorksheet()
    {
        CommitWorksheetEdit();
        _workbook.Remove(_workbook.SelectedIndex);
    }

    private void RequestRemoveWorksheet()
    {
        if (_workbook.Sheets.Count == 1) { TryWorksheetOperation(RemoveWorksheet); return; }
        if (_pendingRemoval != _workbook.SelectedSheet.Id)
        {
            _pendingRemoval = _workbook.SelectedSheet.Id;
            _removeSheet.Text = "Confirm remove";
            _status.Text = $"Remove worksheet '{_workbook.SelectedSheet.Name}' and all its data (no undo; formula references unchanged)? Click Confirm remove, or select another worksheet to cancel.";
            Invalidate(UiInvalidationKind.Render);
            return;
        }
        TryWorksheetOperation(RemoveWorksheet);
    }

    private void TryWorksheetOperation(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            RefreshWorksheetTabs();
            _status.Text = error.Message;
            Invalidate(UiInvalidationKind.Render);
        }
    }

    private void WorkbookStructureChanged(object? sender, EventArgs args)
    {
        foreach (var id in _sheetSelections.Keys.Where(id => !_workbook.Sheets.Any(s => s.Id == id)).ToArray())
            _sheetSelections.Remove(id);
        _pendingRemoval = null;
        _removeSheet.Text = "Remove";
        RefreshWorksheetTabs();
    }
    private void WorkbookSelectionChanged(object? sender, EventArgs args)
    {
        ShowWorksheet();
        RefreshWorksheetTabs();
    }

    private void ShowWorksheet()
    {
        if (_displayedWorksheet is not null && _cellControl.SelectedCell is { } selected)
            _sheetSelections[_displayedWorksheet.Id] = selected;
        _displayedWorksheet = _workbook.SelectedSheet;
        _reportedFormulaIssue = false;
        _cellControl.SetSheet(_displayedWorksheet.Sheet);
        _cellControl.SelectCell(_sheetSelections.TryGetValue(_displayedWorksheet.Id, out var position) ? position : new CellPosition(0, 0));
        _cellEditLine.CancelEdit();
        _sheetName.Text = _displayedWorksheet.Name;
        if (_cellControl.SelectedCell is { } cell) _numberFormat.Text = _cellControl.Sheet.GetContent(cell.Row, cell.Column).NumberFormat ?? "General";
        _pendingRemoval = null;
        _removeSheet.Text = "Remove";
        _status.Text = $"Worksheet: {_displayedWorksheet.Name} ({_workbook.SelectedIndex + 1}/{_workbook.Sheets.Count}); supported formulas calculate automatically.";
        Invalidate(UiInvalidationKind.Measure);
    }

    private void RefreshWorksheetTabs()
    {
        _updatingTabs = true;
        try
        {
            foreach (var tab in _sheetTabs.Tabs.ToArray()) _sheetTabs.RemoveTab(tab.Id);
            int start = (_workbook.SelectedIndex / _tabPageSize) * _tabPageSize;
            foreach (var sheet in _workbook.Sheets.Skip(start).Take(_tabPageSize)) _sheetTabs.AddTab(sheet.Id, sheet.Name);
            _sheetTabs.SelectTab(_workbook.SelectedSheet.Id);
            _sheetName.Text = _workbook.SelectedSheet.Name;
        }
        finally { _updatingTabs = false; }
        Invalidate(UiInvalidationKind.Measure);
    }

    private StandardMenu CreateMenu()
    {
        var menu = new StandardMenu
        {
            PresentationMode = UiMenuPresentationMode.MenuBar,
            CommandDispatcher = _dispatcher,
            Background = Canvas,
        };

        var fileMenu = new UiMenuItem("menu.file", "File") { AccessKey = 'F' };
        fileMenu.Children.Add(new UiMenuItem("file.new", "New") { AccessKey = 'N', Accelerator = "Ctrl+N", CommandName = "file.new" });
        fileMenu.Children.Add(new UiMenuItem("file.open", "Open") { AccessKey = 'O', Accelerator = "Ctrl+O", CommandName = "file.open" });
        fileMenu.Children.Add(new UiMenuItem("file.save", (_fileAction is null ? "Save" : "Export download")) { AccessKey = 'S', Accelerator = "Ctrl+S", CommandName = "file.save" });
        fileMenu.Children.Add(new UiMenuItem("file.save-as", (_fileAction is null ? "Save as" : "Export as")) { AccessKey = 'A', Accelerator = "Ctrl+Shift+S", CommandName = "file.save-as" });
        fileMenu.Children.Add(new UiMenuItem("file.sep1", "") { IsSeparator = true });
        fileMenu.Children.Add(new UiMenuItem("file.close", "Close") { AccessKey = 'C', Accelerator = "Ctrl+W", CommandName = "file.close" });
        fileMenu.Children.Add(new UiMenuItem("file.sep2", "") { IsSeparator = true });
        fileMenu.Children.Add(new UiMenuItem("file.exit", "Exit") { AccessKey = 'X', Accelerator = "Alt+F4", CommandName = "file.exit" });

        var helpMenu = new UiMenuItem("menu.help", "Help") { AccessKey = 'H' };
        helpMenu.Children.Add(new UiMenuItem("help.about", "About") { AccessKey = 'A', CommandName = "help.about" });

        menu.SetItems([fileMenu, helpMenu]);
        return menu;
    }

    private StandardToolbar CreateToolbar()
    {
        double iconButtonExtent = _compactMode ? 40 : 30;
        double iconExtent = _compactMode ? 22 : 16;

        var toolbar = new StandardToolbar
        {
            Title = "Spreadsheet toolbar",
            PreferredSize = new BSize(0, _compactMode ? 48 : 38),
            Orientation = UiToolbarOrientation.Horizontal,
            Padding = 4,
            Spacing = 3,
            GroupExtent = _compactMode ? 12 : 9,
            Background = CellsPalette.ToolbarSurface,
            BorderColor = CellsPalette.MenuRule,
            SeparatorColor = CellsPalette.MenuRule,
            CornerRadius = 0,
            Foreground = CellsPalette.Title,
            PopupBackground = CellsPalette.MenuPopup,
            Font = new BFontStyle("Segoe UI", 14),
        };

        StandardButton CreateButton(string text, string tip, Action action, Action<BRenderList, BRect, BColor>? icon)
        {
            var btn = new StandardButton
            {
                Text = text,
                ToolTipText = tip,
                PreferredSize = new BSize(iconButtonExtent, iconButtonExtent),
                PaddingX = 6,
                PaddingY = 4,
                IconPainter = icon,
                IconExtent = iconExtent,
                Background = CellsPalette.ToolbarButton,
                BorderColor = CellsPalette.ToolbarButtonBorder,
                CornerRadius = 3,
                Foreground = CellsPalette.Title,
            };
            btn.Clicked += (_, _) => action();
            return btn;
        }

        StandardButton newButton = CreateButton("New", "New spreadsheet (Ctrl+N)", NewDocument, CellsIcons.NewDocument);
        StandardButton openButton = CreateButton("Open", "Open... (Ctrl+O)", () => _ = OpenDocumentAsync(), CellsIcons.OpenDocument);
        StandardButton saveButton = CreateButton(_fileAction is null ? "Save" : "Export", "Save / export (Ctrl+S)", SaveDocument, CellsIcons.Save);
        StandardButton saveAsButton = CreateButton(_fileAction is null ? "Save as" : "Export as", "Save / export as (Ctrl+Shift+S)", () => _ = SaveDocumentAsAsync(), CellsIcons.SaveAs);

        StandardButton cutButton = CreateButton("Cut", "Cut (Ctrl+X)", CutSelection, CellsIcons.Cut);
        StandardButton copyButton = CreateButton("Copy", "Copy (Ctrl+C)", CopySelection, CellsIcons.Copy);
        StandardButton pasteButton = CreateButton("Paste", "Paste (Ctrl+V)", PasteSelection, CellsIcons.Paste);
        StandardButton clearButton = CreateButton("Clear", "Clear (Del)", ClearSelection, CellsIcons.Clear);
        StandardButton autoFitButton = CreateButton("AutoFit", "Auto-fit column width", AutoFitSelected, CellsIcons.AutoFit);

        toolbar.AddChild(newButton);
        toolbar.AddChild(openButton);
        toolbar.AddChild(saveButton);
        if (!_compactMode)
            toolbar.AddChild(saveAsButton);

        toolbar.AddChild(cutButton);
        toolbar.AddChild(copyButton);
        toolbar.AddChild(pasteButton);
        toolbar.AddChild(clearButton);
        if (!_compactMode)
            toolbar.AddChild(autoFitButton);
        var recalculate = new StandardButton { Text = "Recalculate", AccessibleName = "Recalculate workbook", PreferredSize = new BSize(100, 28) };
        recalculate.Clicked += (_, _) => TryWorksheetOperation(RecalculateWorkbook);
        toolbar.AddChild(recalculate);

        toolbar.SetBreakBefore(cutButton, UiToolbarBreak.Gap);
        if (!_compactMode)
            toolbar.SetBreakBefore(autoFitButton, UiToolbarBreak.Gap);

        return toolbar;
    }

    public void NewDocument()
    {
        if (_fileAction is not null) { _fileAction("new"); return; }
        var newSheet = new SheetModel(10, 10);
        SetDocument(newSheet);
        _cellControl.SelectCell(new CellPosition(0, 0));
        _currentFilePath = null;
        _importedWorkbookPath = null;
        _status.Text = "Session only";
        TitleChanged?.Invoke(this, "Broiler Cells");
        Invalidate(UiInvalidationKind.Render);
    }

    public void CloseDocument()
    {
        if (_fileAction is not null) { _fileAction("close"); return; }
        NewDocument();
    }

    public void SaveDocument()
    {
        if (_fileAction is not null) { _fileAction("export"); return; }
        if (!string.IsNullOrEmpty(_currentFilePath) && (_importedWorkbookPath is null
            || !string.Equals(Path.GetFullPath(_currentFilePath), _importedWorkbookPath, StringComparison.OrdinalIgnoreCase)))
        {
            TryFileOperation(() => Save(_currentFilePath));
        }
        else
        {
            _ = SaveDocumentAsAsync();
        }
    }

    public async Task<bool> OpenDocumentAsync()
    {
        if (_fileAction is not null) { _fileAction("open"); return false; }
        if (OwnerWindow is null)
            return false;

        var dialog = new StandardFileDialog
        {
            Title = "Open",
            Mode = UiFileDialogMode.Open,
            DefaultExtension = ".csv",
        };
        dialog.SetFileTypeFilters([
            new UiFileDialogFilter("CSV Files (*.csv)", "*.csv", ".csv"),
            new UiFileDialogFilter("Excel Workbooks (*.xlsx)", "*.xlsx", ".xlsx"),
            new UiFileDialogFilter("OpenDocument Spreadsheets (*.ods)", "*.ods", ".ods"),
            new UiFileDialogFilter("All Files (*.*)", "*.*", ""),
        ]);

        UiDialogResult result = await dialog.ShowModal(OwnerWindow);
        if (result.Kind == UiDialogResultKind.Accepted)
        {
            string path = result.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                if (Session?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    dispatcher.Post(() => TryFileOperation(() => Load(path)));
                else
                    TryFileOperation(() => Load(path));
                return true;
            }
        }

        return false;
    }

    public async Task<bool> SaveDocumentAsAsync()
    {
        if (_fileAction is not null) { _fileAction("export-as"); return false; }
        if (OwnerWindow is null)
            return false;

        var dialog = new StandardFileDialog
        {
            Title = "Save As",
            Mode = UiFileDialogMode.Save,
            DefaultExtension = _workbook.Sheets.Count > 1 ? ".xlsx" : ".csv",
            FileName = !string.IsNullOrEmpty(_currentFilePath) ? Path.GetFileName(_currentFilePath) : (_workbook.Sheets.Count > 1 ? "Spreadsheet.xlsx" : "Spreadsheet.csv"),
        };
        dialog.SetFileTypeFilters([
            new UiFileDialogFilter("CSV Files (*.csv)", "*.csv", ".csv"),
            new UiFileDialogFilter("Excel Workbooks (*.xlsx)", "*.xlsx", ".xlsx"),
            new UiFileDialogFilter("OpenDocument Spreadsheets (*.ods)", "*.ods", ".ods"),
            new UiFileDialogFilter("All Files (*.*)", "*.*", ""),
        ]);

        UiDialogResult result = await dialog.ShowModal(OwnerWindow);
        if (result.Kind == UiDialogResultKind.Accepted)
        {
            string path = result.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (Session?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    dispatcher.Post(() => TryFileOperation(() => Save(path)));
                else
                    TryFileOperation(() => Save(path));
                return true;
            }
        }

        return false;
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_importedWorkbookPath is not null && string.Equals(Path.GetFullPath(path), _importedWorkbookPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Basic workbook editing cannot overwrite the imported original. Choose a new filename with Save As.");
        CommitWorksheetEdit();
        SheetDocumentSerializer.Save(_cellControl.Sheet, path);
        _currentFilePath = path;
        _status.Text = $"Saved: {Path.GetFileName(path)}" + (SheetDocumentSerializer.IsWorkbook(path) ? " (basic workbook; basic formula support)" : string.Empty);
        TitleChanged?.Invoke(this, $"Broiler Cells - {Path.GetFileName(path)}");
        Invalidate(UiInvalidationKind.Render);
    }

    public void Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SheetModel sheet = SheetDocumentSerializer.Load(path);
        SetDocument(sheet);
        _cellControl.SelectCell(new CellPosition(0, 0));
        _currentFilePath = path;
        _importedWorkbookPath = SheetDocumentSerializer.IsWorkbook(path) ? Path.GetFullPath(path) : null;
        _status.Text = string.IsNullOrEmpty(sheet.ImportNotice) ? Path.GetFileName(path) : sheet.ImportNotice;
        TitleChanged?.Invoke(this, $"Broiler Cells - {Path.GetFileName(path)}");
        Invalidate(UiInvalidationKind.Render);
    }

    private void TryFileOperation(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _status.Text = "File operation failed: " + error.Message;
            Invalidate(UiInvalidationKind.Render);
        }
    }

    public void CutSelection()
    {
        if (ClipboardCommand?.Invoke("cut") == true) return;
        CopySelection();
        ClearSelection();
    }

    public void CopySelection()
    {
        if (ClipboardCommand?.Invoke("copy") == true) return;
        if (_cellControl.SelectedCell is { } pos)
        {
            string val = _cellControl.Sheet[pos.Row, pos.Column];
            if (Session?.Host is IUiClipboardHost clipboard)
            {
                clipboard.SetText(val);
            }
        }
    }

    public void PasteSelection()
    {
        if (ClipboardCommand?.Invoke("paste") == true) return;
        if (_cellControl.SelectedCell is { } pos)
        {
            if (Session?.Host is IUiClipboardHost clipboard && clipboard.TryGetText(out string text))
            {
                _cellControl.SetCellValue(pos, text);
            }
        }
    }

    public void ClearSelection()
    {
        if (_cellControl.SelectedCell is { } pos)
        {
            _cellControl.SetCellValue(pos, string.Empty);
        }
    }

    public void AutoFitSelected()
    {
        if (_cellControl.SelectedCell is { } pos)
        {
            _cellControl.AutoFitColumn(pos.Column);
        }
    }

    public void ExitApplication()
    {
        if (_fileAction is not null) { _fileAction("exit"); return; }
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public StandardAboutDialog? ShowAbout()
    {
        if (OwnerWindow is null || OwnerWindow.IsDisposed || OwnerWindow.IsClosed)
        {
            return null;
        }

        _cellControl.Focus();
        BSize viewport = ViewportProvider?.Invoke() ?? (OwnerWindow.Bounds.Width > 0 && OwnerWindow.Bounds.Height > 0 ? OwnerWindow.Bounds.Size : new BSize(800, 600));
        var result = CellsAbout.Show(OwnerWindow, viewport, typeof(CellsView).Assembly, OpenUrl);
        return result;
    }

    public Task ShowAboutDialogAsync()
    {
        ShowAbout();
        return Task.CompletedTask;
    }

    private void OnSelectedCellChanged(object? sender, CellPosition? position)
    {
        _address.Text = position?.ToAddress() ?? string.Empty;
        if (position is { } cell)
            _numberFormat.Text = _cellControl.Sheet.GetContent(cell.Row, cell.Column).NumberFormat ?? "General";
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down)
        {
            bool isCtrl = input.KeyModifiers.HasFlag(KeyboardModifierState.Control);
            bool isShift = input.KeyModifiers.HasFlag(KeyboardModifierState.Shift);
            bool isAlt = input.KeyModifiers.HasFlag(KeyboardModifierState.Alt);

            if (isCtrl && !isAlt)
            {
                if (!isShift && string.Equals(input.KeyName, "N", StringComparison.OrdinalIgnoreCase))
                {
                    NewDocument();
                    return true;
                }
                if (!isShift && string.Equals(input.KeyName, "O", StringComparison.OrdinalIgnoreCase))
                {
                    _ = OpenDocumentAsync();
                    return true;
                }
                if (!isShift && string.Equals(input.KeyName, "S", StringComparison.OrdinalIgnoreCase))
                {
                    SaveDocument();
                    return true;
                }
                if (isShift && string.Equals(input.KeyName, "S", StringComparison.OrdinalIgnoreCase))
                {
                    _ = SaveDocumentAsAsync();
                    return true;
                }
                if (!isShift && string.Equals(input.KeyName, "W", StringComparison.OrdinalIgnoreCase))
                {
                    CloseDocument();
                    return true;
                }
                if (!isShift && string.Equals(input.KeyName, "X", StringComparison.OrdinalIgnoreCase))
                {
                    CutSelection();
                    return true;
                }
                if (!isShift && string.Equals(input.KeyName, "C", StringComparison.OrdinalIgnoreCase))
                {
                    CopySelection();
                    return true;
                }
                if (!isShift && string.Equals(input.KeyName, "V", StringComparison.OrdinalIgnoreCase))
                {
                    PasteSelection();
                    return true;
                }
            }
        }

        return base.OnInput(input);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 1000 : Math.Max(0, availableSize.Width);
        double height = double.IsInfinity(availableSize.Height) ? 600 : Math.Max(0, availableSize.Height);

        double menuHeight = _menu.MenuBarHeight;
        _menu.Measure(new BSize(width, menuHeight));

        double toolbarHeight = _toolbar.PreferredSize.Height;
        _toolbar.Measure(new BSize(width, toolbarHeight));

        _sheetTabs.Measure(new BSize(Math.Max(0, width - (width < 760 ? 64 : 390)), 32));
        _entryModeButton.Measure(new BSize(110, 28));
        _applyValueButton.Measure(new BSize(100, 28));
        _numberFormat.Measure(new BSize(160, 28));
        _applyFormatButton.Measure(new BSize(110, 28));
        _previousSheet.Measure(new BSize(24, 28));
        _nextSheet.Measure(new BSize(24, 28));
        _sheetName.Measure(new BSize(120, 28));
        _addSheet.Measure(new BSize(30, 28));
        _renameSheet.Measure(new BSize(70, 28));
        _removeSheet.Measure(new BSize(100, 28));

        double editLineHeight = _cellEditLine.PreferredHeight;
        _cellEditLine.Measure(new BSize(width, editLineHeight));

        double contentWidth = Math.Max(0, width - 24);
        double statusHeight = 24;
        double gridHeight = Math.Max(100, height - menuHeight - toolbarHeight - editLineHeight - statusHeight - (width < 760 ? 66 : 34) - (width < 600 ? 64 : 32) - 16);

        _cellControl.Measure(new BSize(contentWidth, gridHeight));
        _address.Measure(new BSize(contentWidth / 2, 20));
        _status.Measure(new BSize(contentWidth / 2, 20));

        return new BSize(width, height);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double width = finalRect.Width;
        double height = finalRect.Height;

        double y = finalRect.Top;
        double menuHeight = _menu.MenuBarHeight;
        _menu.Arrange(new BRect(finalRect.Left, y, width, menuHeight));
        y += menuHeight;

        double toolbarHeight = _toolbar.PreferredSize.Height;
        _toolbar.Arrange(new BRect(finalRect.Left, y, width, toolbarHeight));
        y += toolbarHeight;

        double editLineHeight = _cellEditLine.PreferredHeight;
        _cellEditLine.Arrange(new BRect(finalRect.Left + 8, y + 2, Math.Max(0, width - 16), editLineHeight));
        y += editLineHeight + 4;

        bool stackedFormat = width < 600;
        _entryModeButton.Arrange(new BRect(finalRect.Left + 8, y, 110, 28));
        _applyValueButton.Arrange(new BRect(finalRect.Left + 122, y, 100, 28));
        double formatX = stackedFormat ? finalRect.Left + 8 : finalRect.Left + 230;
        double formatY = y + (stackedFormat ? 32 : 0);
        _numberFormat.Arrange(new BRect(formatX, formatY, Math.Min(160, Math.Max(50, width - 130)), 28));
        _applyFormatButton.Arrange(new BRect(formatX + Math.Min(160, Math.Max(50, width - 130)) + 4, formatY, 110, 28));
        y += stackedFormat ? 64 : 32;

        bool stackedSheetBar = width < 760;
        double sheetBarHeight = stackedSheetBar ? 66 : 34;
        double statusHeight = 24;
        double sheetBarY = Math.Max(y, finalRect.Bottom - statusHeight - sheetBarHeight);
        double tabsWidth = Math.Max(0, width - (stackedSheetBar ? 64 : 390));
        int pageSize = Math.Max(1, (int)(tabsWidth / 110));
        if (pageSize != _tabPageSize) { _tabPageSize = pageSize; RefreshWorksheetTabs(); }
        _previousSheet.Arrange(new BRect(finalRect.Left + 8, sheetBarY, 24, 28));
        _nextSheet.Arrange(new BRect(finalRect.Left + 32, sheetBarY, 24, 28));
        _sheetTabs.Arrange(new BRect(finalRect.Left + 56, sheetBarY, tabsWidth, 32));
        double controlsX = stackedSheetBar ? finalRect.Left + 8 : finalRect.Left + 56 + tabsWidth;
        double controlsY = sheetBarY + (stackedSheetBar ? 32 : 0);
        _addSheet.Arrange(new BRect(controlsX, controlsY, 30, 28));
        _sheetName.Arrange(new BRect(controlsX + 32, controlsY, 110, 28));
        _renameSheet.Arrange(new BRect(controlsX + 144, controlsY, 70, 28));
        _removeSheet.Arrange(new BRect(controlsX + 216, controlsY, 110, 28));
        double statusY = finalRect.Bottom - statusHeight;
        double gridHeight = Math.Max(0, sheetBarY - y - 4);
        double contentWidth = Math.Max(0, width - 16);

        _cellControl.Arrange(new BRect(finalRect.Left + 8, y, contentWidth, gridHeight));

        _address.Arrange(new BRect(finalRect.Left + 12, statusY + 2, 140, 20));
        double statusWidth = Math.Min(300, Math.Max(120, contentWidth - 160));
        _status.Arrange(new BRect(finalRect.Left + 12 + Math.Max(0, contentWidth - statusWidth), statusY + 2, statusWidth, 20));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _workbook is not null)
        {
            _workbook.StructureChanged -= WorkbookStructureChanged;
            _workbook.SelectionChanged -= WorkbookSelectionChanged;
        }
        base.Dispose(disposing);
    }

    protected override void RenderCore(UiRenderContext context)
    {
        context.RenderList.PushClip(Bounds);
        context.RenderList.FillRect(Bounds, Canvas);
        UpdateCalculationStatus();
        base.RenderCore(context);
        context.RenderList.PopClip();
    }
}

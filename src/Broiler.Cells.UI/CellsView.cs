using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Documents.Model;
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
using Broiler.UI.Window.Standard;
using Broiler.Documents;
using Broiler.Documents.Html;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Pagination;
using Broiler.UI.PrintDialog;
using Broiler.UI.PrintDialog.Standard;
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
    private readonly Action<string>? _webAction;
    private string? _importedWorkbookPath;
    private readonly StandardTabView _sheetTabs = new() { HeaderHeight = 32, AccessibleName = "Worksheets", ToolTipText = "Worksheet tabs" };
    private readonly StandardEdit _sheetName = new() { AccessibleName = "Worksheet name", ToolTipText = "Worksheet name" };
    private readonly StandardButton _addSheet = new() { Text = "+", AccessibleName = "Add worksheet", ToolTipText = "Add worksheet" };
    private readonly StandardButton _renameSheet = new() { Text = "Rename", ToolTipText = "Rename worksheet" };
    private readonly StandardButton _removeSheet = new() { Text = "Remove", ToolTipText = "Remove worksheet" };
    private readonly System.Collections.Generic.Dictionary<string, CellPosition> _sheetSelections = [];
    private WorkbookModel _workbook = null!;
    private WorksheetModel? _displayedWorksheet;
    private readonly StandardButton _previousSheet = new() { Text = "‹", AccessibleName = "Previous worksheet", ToolTipText = "Previous worksheet" };
    private readonly StandardButton _nextSheet = new() { Text = "›", AccessibleName = "Next worksheet", ToolTipText = "Next worksheet" };
    private int _tabPageSize = 4;
    private bool _updatingTabs;
    private readonly StandardButton _entryModeButton = new() { Text = "Type: Auto", AccessibleName = "Cell entry mode", ToolTipText = "Cycle cell entry mode" };
    private readonly StandardButton _applyValueButton = new() { Text = "Apply value", ToolTipText = "Apply cell value" };
    private readonly StandardEdit _numberFormat = new() { Text = "General", AccessibleName = "Number format", ToolTipText = "Number format code" };
    private readonly StandardButton _applyFormatButton = new() { Text = "Apply format", ToolTipText = "Apply number format" };
    private readonly StandardButton _presetButton = new() { Text = "Preset: General", AccessibleName = "Number format presets", ToolTipText = "Cycle number format preset (General, Number, Decimal, Currency, Percent, Date, Time, Scientific, Text)" };
    private string? _pendingRemoval;
    private readonly IUiPrintHost? _printHost;
    private bool _printing;
    private readonly FindReplacePanel _findReplacePanel = new();
    private readonly NoteEditPanel _noteEditPanel = new();
    private readonly ConditionalFormatPanel _conditionalFormatPanel = new();
    private readonly DataValidationPanel _dataValidationPanel = new();
    private readonly ValidationPicklistPopup _validationPicklistPopup = new();
    private readonly ValidationAlertCard _validationAlertCard = new();
    private readonly FilterDropdownPopup _filterDropdownPopup = new();
    private readonly SortDialog _sortDialog = new();
    private readonly HyperlinkPanel _hyperlinkPanel = new();
    private string? _lastValidationError;
    private ValidationResult? _lastValidationResult;

    public ConditionalFormatPanel ConditionalFormatDialog => _conditionalFormatPanel;
    public bool IsConditionalFormatDialogVisible => _conditionalFormatPanel.IsPanelVisible;
    public DataValidationPanel DataValidationDialog => _dataValidationPanel;
    public bool IsDataValidationDialogVisible => _dataValidationPanel.IsPanelVisible;
    public HyperlinkPanel HyperlinkDialog => _hyperlinkPanel;
    public bool IsHyperlinkDialogVisible => _hyperlinkPanel.IsPanelVisible;
    public ValidationPicklistPopup ValidationPicklistPopup => _validationPicklistPopup;
    public bool IsValidationPicklistVisible => _validationPicklistPopup.IsPopupVisible;
    public ValidationAlertCard ValidationAlertCard => _validationAlertCard;
    public bool IsValidationAlertVisible => _validationAlertCard.IsCardVisible;
    public FilterDropdownPopup FilterDropdownPopup => _filterDropdownPopup;
    public bool IsFilterDropdownVisible => _filterDropdownPopup.IsPopupVisible;
    public SortDialog SortDialog => _sortDialog;
    public bool IsSortDialogVisible => _sortDialog.IsPanelVisible;
    public string? LastValidationError => _lastValidationError;
    public ValidationResult? LastValidationResult => _lastValidationResult;
    public CellsView(SheetModel sheet, bool compactMode = false, Action<string>? fileAction = null, Action<string>? webAction = null, IUiPrintHost? printHost = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _compactMode = compactMode;
        _fileAction = fileAction;
        _webAction = webAction;
        _printHost = printHost;
        AccessibleName = "Broiler Cells";
        _dispatcher = new StandardCommandDispatcher();
        _dispatcher.Add(new StandardCommand("file.new", NewDocument));
        _dispatcher.Add(new StandardCommand("file.open", () => _ = OpenDocumentAsync()));
        _dispatcher.Add(new StandardCommand("file.save", SaveDocument));
        _dispatcher.Add(new StandardCommand("file.save-as", () => _ = SaveDocumentAsAsync()));
        _dispatcher.Add(new StandardCommand("file.export-pdf", () => _ = ExportPdfAsync()));
        _dispatcher.Add(new StandardCommand("file.print", ShowPrintDialog));
        _dispatcher.Add(new StandardCommand("file.close", CloseDocument));
        _dispatcher.Add(new StandardCommand("file.exit", ExitApplication));
        _dispatcher.Add(new StandardCommand("edit.cut", CutSelection));
        _dispatcher.Add(new StandardCommand("edit.copy", CopySelection));
        _dispatcher.Add(new StandardCommand("edit.paste", PasteSelection));
        _dispatcher.Add(new StandardCommand("edit.clear", ClearSelection));
        _dispatcher.Add(new StandardCommand("edit.find", ShowFind));
        _dispatcher.Add(new StandardCommand("edit.replace", ShowReplace));
        _dispatcher.Add(new StandardCommand("edit.find-next", () => FindNext()));
        _dispatcher.Add(new StandardCommand("edit.find-previous", () => FindPrevious()));
        _dispatcher.Add(new StandardCommand("edit.insert-note", ToggleNoteEditor));
        _dispatcher.Add(new StandardCommand("edit.delete-note", DeleteSelectedNote));
        _dispatcher.Add(new StandardCommand("edit.fill-down", () => FillDown()));
        _dispatcher.Add(new StandardCommand("edit.fill-right", () => FillRight()));
        _dispatcher.Add(new StandardCommand("help.about", () => ShowAbout()));
        _dispatcher.Add(new StandardCommand("help.info", () => ShowAbout()));
        _dispatcher.Add(new StandardCommand("format.bold", ToggleBoldSelected));
        _dispatcher.Add(new StandardCommand("format.italic", ToggleItalicSelected));
        _dispatcher.Add(new StandardCommand("format.underline", ToggleUnderlineSelected));
        _dispatcher.Add(new StandardCommand("format.align-left", () => SetSelectedAlignment(Broiler.Documents.Model.TextAlignment.Left)));
        _dispatcher.Add(new StandardCommand("format.align-center", () => SetSelectedAlignment(Broiler.Documents.Model.TextAlignment.Center)));
        _dispatcher.Add(new StandardCommand("format.align-right", () => SetSelectedAlignment(Broiler.Documents.Model.TextAlignment.Right)));
        _dispatcher.Add(new StandardCommand("format.fill", () => ToggleSelectedFill(BColor.FromArgb(255, 255, 255, 153))));
        _dispatcher.Add(new StandardCommand("format.merge", ToggleMergeSelected));
        _dispatcher.Add(new StandardCommand("format.appearance", OpenCellAppearance));
        _dispatcher.Add(new StandardCommand("format.wrap-text", ToggleWrapTextSelected));
        _dispatcher.Add(new StandardCommand("view.toggle-gridlines", ToggleGridlines));
        _dispatcher.Add(new StandardCommand("view.freeze-panes", ToggleFreezePanes));
        _dispatcher.Add(new StandardCommand("view.unfreeze-panes", UnfreezePanes));
        _dispatcher.Add(new StandardCommand("view.split", ToggleSplit));
        _dispatcher.Add(new StandardCommand("view.split-horizontal", SplitHorizontal));
        _dispatcher.Add(new StandardCommand("view.split-vertical", SplitVertical));
        _dispatcher.Add(new StandardCommand("view.split-remove", RemoveSplit));
        _dispatcher.Add(new StandardCommand("window.new-window", () => _ = OpenNewWindow()));

        _dispatcher.Add(new StandardCommand("format.number.general", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.General)));
        _dispatcher.Add(new StandardCommand("format.number.integer", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Integer)));
        _dispatcher.Add(new StandardCommand("format.number.decimal", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Decimal2)));
        _dispatcher.Add(new StandardCommand("format.number.currency", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Currency)));
        _dispatcher.Add(new StandardCommand("format.number.percentage", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Percentage)));
        _dispatcher.Add(new StandardCommand("format.number.short-date", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.ShortDate)));
        _dispatcher.Add(new StandardCommand("format.number.long-date", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.LongDate)));
        _dispatcher.Add(new StandardCommand("format.number.time", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Time)));
        _dispatcher.Add(new StandardCommand("format.number.datetime", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.DateTime)));
        _dispatcher.Add(new StandardCommand("format.number.scientific", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Scientific)));
        _dispatcher.Add(new StandardCommand("format.number.text", () => ApplySelectedNumberFormat("@")));
        _dispatcher.Add(new StandardCommand("format.number.increase-decimal", () => AdjustSelectedDecimals(1)));
        _dispatcher.Add(new StandardCommand("format.number.decrease-decimal", () => AdjustSelectedDecimals(-1)));

        _dispatcher.Add(new StandardCommand("format.conditional.greater-than", () => OpenConditionalFormatting(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.GreaterThan)));
        _dispatcher.Add(new StandardCommand("format.conditional.less-than", () => OpenConditionalFormatting(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.LessThan)));
        _dispatcher.Add(new StandardCommand("format.conditional.between", () => OpenConditionalFormatting(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.Between)));
        _dispatcher.Add(new StandardCommand("format.conditional.equal-to", () => OpenConditionalFormatting(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.Equal)));
        _dispatcher.Add(new StandardCommand("format.conditional.text-contains", () => OpenConditionalFormatting(ConditionalFormatRuleType.ContainsText, ConditionalFormatOperator.None)));
        _dispatcher.Add(new StandardCommand("format.conditional.duplicate-values", () => OpenConditionalFormatting(ConditionalFormatRuleType.DuplicateValues, ConditionalFormatOperator.None)));
        _dispatcher.Add(new StandardCommand("format.conditional.color-scale", () => OpenConditionalFormatting(ConditionalFormatRuleType.ColorScale, ConditionalFormatOperator.None)));
        _dispatcher.Add(new StandardCommand("format.conditional.clear-selected", ClearSelectedConditionalFormatting));
        _dispatcher.Add(new StandardCommand("format.conditional.clear-all", ClearAllConditionalFormatting));
        _dispatcher.Add(new StandardCommand("format.conditional.manage", () => OpenConditionalFormatting(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.GreaterThan)));

        _dispatcher.Add(new StandardCommand("data.validation", OpenDataValidation));
        _dispatcher.Add(new StandardCommand("data.validation.clear", ClearSelectedDataValidation));

        _dispatcher.Add(new StandardCommand("data.sort-az", () => QuickSortActiveColumn(ascending: true)));
        _dispatcher.Add(new StandardCommand("data.sort-za", () => QuickSortActiveColumn(ascending: false)));
        _dispatcher.Add(new StandardCommand("data.sort", OpenSortDialog));
        _dispatcher.Add(new StandardCommand("data.filter", ToggleAutoFilter));
        _dispatcher.Add(new StandardCommand("data.filter.clear", ClearActiveFilter));
        _dispatcher.Add(new StandardCommand("data.filter.reapply", ReapplyFilters));
        _dispatcher.Add(new StandardCommand("insert.hyperlink", OpenHyperlinkDialog));
        _dispatcher.Add(new StandardCommand("edit.remove-hyperlink", RemoveSelectedHyperlink));
        _dispatcher.Add(new StandardCommand("data.group", () => GroupSelection()));
        _dispatcher.Add(new StandardCommand("data.ungroup", () => UngroupSelection()));
        _dispatcher.Add(new StandardCommand("data.show-detail", () => ShowDetailSelection()));
        _dispatcher.Add(new StandardCommand("data.hide-detail", () => HideDetailSelection()));
        _menu = CreateMenu(webAction);
        _toolbar = CreateToolbar();
        _cellControl = new CellControl(sheet, isEditingEnabled: true, areHeadersVisible: true);
        _cellControl.SelectedCellChanged += OnSelectedCellChanged;
        _cellControl.CellCommitted += (_, _) => UpdateCalculationStatus();
        _cellEditLine = new CellEditLine();
        _cellEditLine.Attach(_cellControl);
        _address = new StandardLabel(_cellControl.SelectedAddress ?? string.Empty, StandardLabelRole.Default)
        {
            ToolTipText = "Current cell address",
            Font = new BFontStyle("Segoe UI", 12)
        };
        _status = new StandardLabel("Session only", StandardLabelRole.Muted)
        {
            ToolTipText = "Document persistence status",
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
        AddChild(_presetButton);
        AddChild(_numberFormat);
        AddChild(_applyFormatButton);
        AddChild(_findReplacePanel);
        AddChild(_noteEditPanel);
        AddChild(_conditionalFormatPanel);
        AddChild(_dataValidationPanel);
        AddChild(_validationPicklistPopup);
        AddChild(_validationAlertCard);
        AddChild(_filterDropdownPopup);
        AddChild(_sortDialog);
        AddChild(_hyperlinkPanel);

        _hyperlinkPanel.HyperlinkApplied += (_, e) => OnHyperlinkApplied(e.Row, e.Column, e.Target, e.Tooltip, e.DisplayText);
        _hyperlinkPanel.HyperlinkRemoved += (_, e) => OnHyperlinkRemoved(e.Row, e.Column);
        _hyperlinkPanel.CloseRequested += (_, _) =>
        {
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _cellControl.Focus();
        };

        _conditionalFormatPanel.SetSheet(sheet);
        _conditionalFormatPanel.RuleApplied += (_, _) => _cellControl.InvalidateRender();
        _conditionalFormatPanel.RulesCleared += (_, _) => _cellControl.InvalidateRender();
        _conditionalFormatPanel.CloseRequested += (_, _) =>
        {
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _cellControl.Focus();
        };

        _dataValidationPanel.SetSheet(sheet);
        _dataValidationPanel.ValidationApplied += (_, _) => _cellControl.InvalidateRender();
        _dataValidationPanel.ValidationCleared += (_, _) => _cellControl.InvalidateRender();
        _dataValidationPanel.CloseRequested += (_, _) =>
        {
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _cellControl.Focus();
        };

        _validationPicklistPopup.ItemSelected += (r, c, val) =>
        {
            _cellControl.SetCellValue(r, c, val);
            _cellEditLine.Text = val;
            _cellControl.Focus();
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        };
        _validationPicklistPopup.Closed += () =>
        {
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _cellControl.Focus();
        };

        _validationAlertCard.Dismissed += () =>
        {
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _cellControl.Focus();
        };

        _cellControl.OpenDropdownRequested += (r, c, dv, rect) => ShowValidationPicklist(r, c, dv, rect);
        _cellControl.ValidationFailed += (r, c, res) => HandleValidationFailed(r, c, res);
        _cellControl.FilterDropdownRequested += (col, btnRect) => ShowFilterDropdown(col, btnRect);
        _cellControl.HyperlinkNavigated += (_, e) =>
        {
            if (e.IsHandled) return;
            if (!e.Hyperlink.IsInternal)
            {
                if (webAction is not null)
                {
                    webAction(e.Hyperlink.Target);
                    e.IsHandled = true;
                }
                else
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = e.Hyperlink.Target,
                            UseShellExecute = true
                        });
                        e.IsHandled = true;
                    }
                    catch
                    {
                        // ignore if process start fails
                    }
                }
            }
        };

        _filterDropdownPopup.SortRequested += (_, _) => _cellControl.InvalidateRender();
        _filterDropdownPopup.ClearFilterRequested += _ => _cellControl.InvalidateRender();
        _filterDropdownPopup.FilterApplied += (_, _, _) => _cellControl.InvalidateRender();
        _filterDropdownPopup.Closed += () =>
        {
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _cellControl.Focus();
        };

        _sortDialog.SortApplied += () => _cellControl.InvalidateRender();
        _sortDialog.CloseRequested += () =>
        {
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _cellControl.Focus();
        };

        _noteEditPanel.NoteSaved += (_, e) => OnNoteSaved(e.Row, e.Column, e.Text, e.Author);
        _noteEditPanel.NoteDeleted += (_, e) => OnNoteDeleted(e.Row, e.Column);
        _noteEditPanel.CloseRequested += (_, _) => OnNoteClosed();
        _findReplacePanel.FindNextRequested += (_, _) => FindNext();
        _findReplacePanel.FindPreviousRequested += (_, _) => FindPrevious();
        _findReplacePanel.ReplaceRequested += (_, _) => ReplaceNext();
        _findReplacePanel.ReplaceAllRequested += (_, _) => ReplaceAll();
        _findReplacePanel.CloseRequested += (_, _) => CloseFindReplace();
        _findReplacePanel.SearchTextChanged += (_, _) => OnSearchTextChanged();
        _findReplacePanel.OptionsChanged += (_, _) => OnSearchTextChanged();
        _presetButton.Clicked += (_, _) => TryWorksheetOperation(CycleFormatPreset);
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
    public string? CurrentFilePath { get => _currentFilePath; internal set => _currentFilePath = value; }
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
        if (_hyperlinkPanel.IsPanelVisible)
            _hyperlinkPanel.Hide();
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
        string? normalized = string.IsNullOrWhiteSpace(format) || string.Equals(format?.Trim(), "General", StringComparison.OrdinalIgnoreCase)
            ? null
            : format.Trim();

        ForEachSelectedCell((r, c) =>
        {
            _cellControl.Sheet.SetNumberFormat(r, c, normalized);
            _cellControl.Grid.RefreshCellDisplay(new CellPosition(r, c));
        });

        if (_cellControl.SelectedCell is { } selected)
        {
            var currentFormat = _cellControl.Sheet.GetContent(selected.Row, selected.Column).NumberFormat;
            _numberFormat.Text = currentFormat ?? "General";
            _presetButton.Text = "Preset: " + CellFormatUtility.GetPresetName(currentFormat);
        }
        _cellEditLine.CancelEdit();
        _status.Text = "Format applied. Raw values are retained; supported formulas calculate automatically.";
        Invalidate(UiInvalidationKind.Render);
    }

    public void CycleFormatPreset()
    {
        var current = _numberFormat.Text;
        var next = CellFormatUtility.GetNextPreset(current);
        _numberFormat.Text = next.Format ?? "General";
        _presetButton.Text = "Preset: " + next.Name;
        ApplySelectedNumberFormat(next.Format);
    }

    public void AdjustSelectedDecimals(int delta)
    {
        CommitWorksheetEdit();
        string? sampleFormat = null;
        ForEachSelectedCell((r, c) =>
        {
            var content = _cellControl.Sheet.GetContent(r, c);
            var adjusted = CellFormatUtility.AdjustDecimals(content.NumberFormat, delta);
            _cellControl.Sheet.SetNumberFormat(r, c, adjusted);
            _cellControl.Grid.RefreshCellDisplay(new CellPosition(r, c));
            if (sampleFormat is null) sampleFormat = adjusted;
        });

        if (_cellControl.SelectedCell is { } selected)
        {
            sampleFormat = _cellControl.Sheet.GetContent(selected.Row, selected.Column).NumberFormat;
        }
        _numberFormat.Text = sampleFormat ?? "General";
        _presetButton.Text = "Preset: " + CellFormatUtility.GetPresetName(sampleFormat);
        _cellEditLine.CancelEdit();
        _status.Text = "Decimal precision updated.";
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
        if (_cellControl.SelectedCell is { } cell)
        {
            var fmt = _cellControl.Sheet.GetContent(cell.Row, cell.Column).NumberFormat;
            _numberFormat.Text = fmt ?? "General";
            _presetButton.Text = "Preset: " + CellFormatUtility.GetPresetName(fmt);
        }

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

    private StandardMenu CreateMenu(Action<string>? webAction)
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
        fileMenu.Children.Add(new UiMenuItem("file.export-pdf", "Export as PDF (.pdf)") { AccessKey = 'E', CommandName = "file.export-pdf" });
        fileMenu.Children.Add(new UiMenuItem("file.print", "Print...") { AccessKey = 'P', Accelerator = "Ctrl+P", CommandName = "file.print" });
        fileMenu.Children.Add(new UiMenuItem("file.sep1", "") { IsSeparator = true });
        fileMenu.Children.Add(new UiMenuItem("file.close", "Close") { AccessKey = 'C', Accelerator = "Ctrl+W", CommandName = "file.close" });
        fileMenu.Children.Add(new UiMenuItem("file.sep2", "") { IsSeparator = true });
        fileMenu.Children.Add(new UiMenuItem("file.exit", "Exit") { AccessKey = 'X', Accelerator = "Alt+F4", CommandName = "file.exit" });
        var editMenu = new UiMenuItem("menu.edit", "Edit") { AccessKey = 'E' };
        editMenu.Children.Add(new UiMenuItem("edit.cut", "Cut") { AccessKey = 'T', Accelerator = "Ctrl+X", CommandName = "edit.cut" });
        editMenu.Children.Add(new UiMenuItem("edit.copy", "Copy") { AccessKey = 'C', Accelerator = "Ctrl+C", CommandName = "edit.copy" });
        editMenu.Children.Add(new UiMenuItem("edit.paste", "Paste") { AccessKey = 'P', Accelerator = "Ctrl+V", CommandName = "edit.paste" });
        editMenu.Children.Add(new UiMenuItem("edit.clear", "Clear") { AccessKey = 'L', Accelerator = "Delete", CommandName = "edit.clear" });
        editMenu.Children.Add(new UiMenuItem("edit.sep1", "") { IsSeparator = true });
        editMenu.Children.Add(new UiMenuItem("edit.find", "Find...") { AccessKey = 'F', Accelerator = "Ctrl+F", CommandName = "edit.find" });
        editMenu.Children.Add(new UiMenuItem("edit.replace", "Replace...") { AccessKey = 'R', Accelerator = "Ctrl+H", CommandName = "edit.replace" });
        editMenu.Children.Add(new UiMenuItem("edit.sep2", "") { IsSeparator = true });
        editMenu.Children.Add(new UiMenuItem("edit.insert-note", "Insert/Edit Note...") { AccessKey = 'N', Accelerator = "Shift+F2", CommandName = "edit.insert-note" });
        editMenu.Children.Add(new UiMenuItem("edit.delete-note", "Delete Note") { AccessKey = 'D', CommandName = "edit.delete-note" });
        editMenu.Children.Add(new UiMenuItem("edit.remove-hyperlink", "Remove Link") { AccessKey = 'K', CommandName = "edit.remove-hyperlink" });
        editMenu.Children.Add(new UiMenuItem("edit.sep3", "") { IsSeparator = true });
        var fillMenu = new UiMenuItem("edit.fill", "Fill") { AccessKey = 'I' };
        fillMenu.Children.Add(new UiMenuItem("edit.fill-down", "Down") { AccessKey = 'D', Accelerator = "Ctrl+D", CommandName = "edit.fill-down" });
        fillMenu.Children.Add(new UiMenuItem("edit.fill-right", "Right") { AccessKey = 'R', Accelerator = "Ctrl+R", CommandName = "edit.fill-right" });
        editMenu.Children.Add(fillMenu);

        var insertMenu = new UiMenuItem("menu.insert", "Insert") { AccessKey = 'I' };
        insertMenu.Children.Add(new UiMenuItem("insert.hyperlink", "Link...") { AccessKey = 'L', Accelerator = "Ctrl+K", CommandName = "insert.hyperlink" });
        insertMenu.Children.Add(new UiMenuItem("insert.note", "Note...") { AccessKey = 'N', Accelerator = "Shift+F2", CommandName = "edit.insert-note" });

        var formatMenu = new UiMenuItem("menu.format", "Format") { AccessKey = 'O' };
        formatMenu.Children.Add(new UiMenuItem("format.bold", "Bold") { AccessKey = 'B', Accelerator = "Ctrl+B", CommandName = "format.bold" });
        formatMenu.Children.Add(new UiMenuItem("format.italic", "Italic") { AccessKey = 'I', Accelerator = "Ctrl+I", CommandName = "format.italic" });
        formatMenu.Children.Add(new UiMenuItem("format.underline", "Underline") { AccessKey = 'U', Accelerator = "Ctrl+U", CommandName = "format.underline" });
        formatMenu.Children.Add(new UiMenuItem("format.appearance", "Cell Appearance...") { CommandName = "format.appearance" });
        formatMenu.Children.Add(new UiMenuItem("format.sep1", "") { IsSeparator = true });
        formatMenu.Children.Add(new UiMenuItem("format.merge", "Merge & Center") { AccessKey = 'M', CommandName = "format.merge" });
        formatMenu.Children.Add(new UiMenuItem("format.wrap-text", "Wrap Text") { AccessKey = 'W', CommandName = "format.wrap-text" });
        formatMenu.Children.Add(new UiMenuItem("format.sep2", "") { IsSeparator = true });

        var condMenu = new UiMenuItem("format.conditional", "Conditional Formatting") { AccessKey = 'C' };
        condMenu.Children.Add(new UiMenuItem("format.conditional.greater-than", "Highlight Cells > Greater Than...") { CommandName = "format.conditional.greater-than" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.less-than", "Highlight Cells < Less Than...") { CommandName = "format.conditional.less-than" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.between", "Highlight Cells Between...") { CommandName = "format.conditional.between" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.equal-to", "Highlight Cells Equal To...") { CommandName = "format.conditional.equal-to" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.text-contains", "Highlight Text that Contains...") { CommandName = "format.conditional.text-contains" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.duplicate-values", "Highlight Duplicate Values...") { CommandName = "format.conditional.duplicate-values" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.color-scale", "Apply Color Scale...") { CommandName = "format.conditional.color-scale" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.sep1", "") { IsSeparator = true });
        condMenu.Children.Add(new UiMenuItem("format.conditional.clear-selected", "Clear Rules from Selected Cells") { CommandName = "format.conditional.clear-selected" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.clear-all", "Clear Rules from Entire Sheet") { CommandName = "format.conditional.clear-all" });
        condMenu.Children.Add(new UiMenuItem("format.conditional.sep2", "") { IsSeparator = true });
        condMenu.Children.Add(new UiMenuItem("format.conditional.manage", "Manage Rules...") { CommandName = "format.conditional.manage" });
        formatMenu.Children.Add(condMenu);

        var dataMenu = new UiMenuItem("menu.data", "Data") { AccessKey = 'D' };
        dataMenu.Children.Add(new UiMenuItem("data.sort-az", "Sort A to Z") { AccessKey = 'S', CommandName = "data.sort-az" });
        dataMenu.Children.Add(new UiMenuItem("data.sort-za", "Sort Z to A") { AccessKey = 'Z', CommandName = "data.sort-za" });
        dataMenu.Children.Add(new UiMenuItem("data.sort", "Sort...") { AccessKey = 'O', CommandName = "data.sort" });
        dataMenu.Children.Add(new UiMenuItem("data.sep1", "") { IsSeparator = true });
        dataMenu.Children.Add(new UiMenuItem("data.filter", "Filter") { AccessKey = 'F', Accelerator = "Ctrl+Shift+L", CommandName = "data.filter" });
        dataMenu.Children.Add(new UiMenuItem("data.filter.clear", "Clear Filter") { AccessKey = 'L', CommandName = "data.filter.clear" });
        dataMenu.Children.Add(new UiMenuItem("data.filter.reapply", "Reapply Filter") { AccessKey = 'R', Accelerator = "Ctrl+Alt+L", CommandName = "data.filter.reapply" });
        dataMenu.Children.Add(new UiMenuItem("data.sep2", "") { IsSeparator = true });
        dataMenu.Children.Add(new UiMenuItem("data.validation", "Data Validation...") { AccessKey = 'V', CommandName = "data.validation" });
        dataMenu.Children.Add(new UiMenuItem("data.validation.clear", "Clear Validation") { AccessKey = 'C', CommandName = "data.validation.clear" });
        dataMenu.Children.Add(new UiMenuItem("data.sep3", "") { IsSeparator = true });

        var groupMenu = new UiMenuItem("data.group-outline", "Group & Outline") { AccessKey = 'G' };
        groupMenu.Children.Add(new UiMenuItem("data.group", "Group...") { AccessKey = 'G', Accelerator = "Shift+Alt+Right", CommandName = "data.group" });
        groupMenu.Children.Add(new UiMenuItem("data.ungroup", "Ungroup...") { AccessKey = 'U', Accelerator = "Shift+Alt+Left", CommandName = "data.ungroup" });
        groupMenu.Children.Add(new UiMenuItem("data.show-detail", "Show Detail") { AccessKey = 'S', CommandName = "data.show-detail" });
        groupMenu.Children.Add(new UiMenuItem("data.hide-detail", "Hide Detail") { AccessKey = 'H', CommandName = "data.hide-detail" });
        dataMenu.Children.Add(groupMenu);

        var viewMenu = new UiMenuItem("menu.view", "View") { AccessKey = 'V' };
        viewMenu.Children.Add(new UiMenuItem("view.toggle-gridlines", "Gridlines") { AccessKey = 'G', CommandName = "view.toggle-gridlines" });
        viewMenu.Children.Add(new UiMenuItem("view.freeze-panes", "Freeze Panes") { AccessKey = 'F', CommandName = "view.freeze-panes" });
        viewMenu.Children.Add(new UiMenuItem("view.unfreeze-panes", "Unfreeze Panes") { AccessKey = 'U', CommandName = "view.unfreeze-panes" });
        viewMenu.Children.Add(new UiMenuItem("view.sep1", "") { IsSeparator = true });
        viewMenu.Children.Add(new UiMenuItem("view.split", "Split") { AccessKey = 'S', CommandName = "view.split" });
        viewMenu.Children.Add(new UiMenuItem("view.split-horizontal", "Split Horizontal") { AccessKey = 'H', CommandName = "view.split-horizontal" });
        viewMenu.Children.Add(new UiMenuItem("view.split-vertical", "Split Vertical") { AccessKey = 'R', CommandName = "view.split-vertical" });
        viewMenu.Children.Add(new UiMenuItem("view.split-remove", "Remove Split") { AccessKey = 'M', CommandName = "view.split-remove" });

        var windowMenu = new UiMenuItem("menu.window", "Window") { AccessKey = 'W' };
        windowMenu.Children.Add(new UiMenuItem("window.new-window", "New Window") { AccessKey = 'N', Accelerator = "Ctrl+Shift+N", CommandName = "window.new-window" });
        var helpMenu = new UiMenuItem("menu.help", "Help") { AccessKey = 'H' };
        helpMenu.Children.Add(new UiMenuItem("help.about", "About") { AccessKey = 'A', CommandName = "help.about" });
        if (webAction is not null) {
            var officeMenu = new UiMenuItem("menu.office", "Office") { AccessKey = 'I' };
            foreach (var (id, label) in new[] { ("home", "Back to Office"), ("recovery", "Recovery…"), ("information", "Spreadsheet information…") }) {
                string action = id;
                string command = "office." + id;
                _dispatcher.Add(new StandardCommand(command, () => webAction(action)));
                officeMenu.Children.Add(new UiMenuItem(command, label) { CommandName = command });
            }
            menu.SetItems([fileMenu, editMenu, insertMenu, viewMenu, formatMenu, dataMenu, windowMenu, officeMenu, helpMenu]);
        } else {
            menu.SetItems([fileMenu, editMenu, insertMenu, viewMenu, formatMenu, dataMenu, windowMenu, helpMenu]);
        }

        return menu;
    }

    private StandardToolbar CreateToolbar()
    {
        double iconButtonExtent = _compactMode ? 40 : 30;
        double iconExtent = _compactMode ? 22 : 16;
        var toolbar = new StandardToolbar
        {
            ToolTipText = "Spreadsheet toolbar",
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
        StandardButton printButton = CreateButton("Print", "Print spreadsheet (Ctrl+P)", ShowPrintDialog, CellsIcons.Print);
        StandardButton cutButton = CreateButton("Cut", "Cut (Ctrl+X)", CutSelection, CellsIcons.Cut);
        StandardButton copyButton = CreateButton("Copy", "Copy (Ctrl+C)", CopySelection, CellsIcons.Copy);
        StandardButton pasteButton = CreateButton("Paste", "Paste (Ctrl+V)", PasteSelection, CellsIcons.Paste);
        StandardButton clearButton = CreateButton("Clear", "Clear (Del)", ClearSelection, CellsIcons.Clear);
        StandardButton autoFitButton = CreateButton("AutoFit", "Auto-fit column width", AutoFitSelected, CellsIcons.AutoFit);
        StandardButton boldButton = CreateButton("B", "Bold (Ctrl+B)", ToggleBoldSelected, CellsIcons.Bold);
        StandardButton italicButton = CreateButton("I", "Italic (Ctrl+I)", ToggleItalicSelected, CellsIcons.Italic);
        StandardButton underlineButton = CreateButton("U", "Underline (Ctrl+U)", ToggleUnderlineSelected, CellsIcons.Underline);
        StandardButton alignLeftButton = CreateButton("Left", "Align Left", () => SetSelectedAlignment(Broiler.Documents.Model.TextAlignment.Left), CellsIcons.AlignLeft);
        StandardButton alignCenterButton = CreateButton("Center", "Align Center", () => SetSelectedAlignment(Broiler.Documents.Model.TextAlignment.Center), CellsIcons.AlignCenter);
        StandardButton alignRightButton = CreateButton("Right", "Align Right", () => SetSelectedAlignment(Broiler.Documents.Model.TextAlignment.Right), CellsIcons.AlignRight);
        StandardButton appearanceButton = CreateButton("Appearance", "Cell text color, fill and borders", OpenCellAppearance, CellsIcons.FillColor);
        StandardButton fillButton = CreateButton("Fill", "Fill yellow background", () => ToggleSelectedFill(BColor.FromArgb(255, 255, 255, 153)), CellsIcons.FillColor);
        StandardButton mergeButton = CreateButton("Merge", "Merge & Center (Ctrl+M)", ToggleMergeSelected, CellsIcons.MergeCells);
        StandardButton wrapButton = CreateButton("Wrap", "Toggle text wrap on selected cells", ToggleWrapTextSelected, CellsIcons.AlignLeft);
        StandardButton currencyButton = CreateButton("$", "Format as Currency ($#,##0.00)", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Currency), CellsIcons.Currency);
        StandardButton percentButton = CreateButton("%", "Format as Percentage (0.0%)", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Percentage), CellsIcons.Percent);
        StandardButton commaButton = CreateButton(",", "Format as Number (#,##0.00)", () => ApplySelectedNumberFormat(Broiler.Documents.Model.CellFormatPresets.Decimal2), CellsIcons.Comma);
        StandardButton increaseDecButton = CreateButton(".0+", "Increase decimal places", () => AdjustSelectedDecimals(1), CellsIcons.IncreaseDecimal);
        StandardButton decreaseDecButton = CreateButton(".0-", "Decrease decimal places", () => AdjustSelectedDecimals(-1), CellsIcons.DecreaseDecimal);
        StandardButton gridlinesButton = CreateButton("Gridlines", "Toggle gridlines visibility", ToggleGridlines, CellsIcons.MergeCells);
        StandardButton freezeButton = CreateButton("Freeze", "Freeze / unfreeze panes at active cell", ToggleFreezePanes, CellsIcons.AutoFit);
        StandardButton splitButton = CreateButton("Split", "Toggle split panes at active cell", ToggleSplit, CellsIcons.Split);
        StandardButton findButton = CreateButton("Find", "Find and Replace (Ctrl+F)", ShowFind, CellsIcons.Search);
        StandardButton noteButton = CreateButton("Note", "Cell Note (Shift+F2)", ToggleNoteEditor, CellsIcons.CellNote);
        StandardButton linkButton = CreateButton("Link", "Insert / Edit Hyperlink (Ctrl+K)", OpenHyperlinkDialog, CellsIcons.Link);
        StandardButton condFormatButton = CreateButton("Cond", "Conditional Formatting", () => OpenConditionalFormatting(ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.GreaterThan), CellsIcons.ConditionalFormatting);
        StandardButton dataValidationButton = CreateButton("Valid", "Data Validation", OpenDataValidation, CellsIcons.DataValidation);
        StandardButton sortAzButton = CreateButton("A-Z", "Sort Ascending (A to Z)", () => QuickSortActiveColumn(ascending: true), CellsIcons.SortAscending);
        StandardButton sortZaButton = CreateButton("Z-A", "Sort Descending (Z to A)", () => QuickSortActiveColumn(ascending: false), CellsIcons.SortDescending);
        StandardButton filterButton = CreateButton("Filter", "Toggle Filter (Ctrl+Shift+L)", ToggleAutoFilter, CellsIcons.Filter);
        StandardButton groupButton = CreateButton("Group", "Group selected rows or columns (Shift+Alt+Right)", () => GroupSelection(), CellsIcons.Group);
        StandardButton ungroupButton = CreateButton("Ungroup", "Ungroup selected rows or columns (Shift+Alt+Left)", () => UngroupSelection(), CellsIcons.Ungroup);
        toolbar.AddChild(newButton);
        toolbar.AddChild(openButton);
        toolbar.AddChild(saveButton);
        if (!_compactMode)
        {
            toolbar.AddChild(saveAsButton);
            toolbar.AddChild(printButton);
        }

        toolbar.AddChild(cutButton);
        toolbar.AddChild(copyButton);
        toolbar.AddChild(pasteButton);
        toolbar.AddChild(clearButton);
        toolbar.AddChild(findButton);
        toolbar.AddChild(noteButton);
        toolbar.AddChild(linkButton);
        if (!_compactMode)
            toolbar.AddChild(autoFitButton);
        toolbar.AddChild(boldButton);
        toolbar.AddChild(italicButton);
        toolbar.AddChild(underlineButton);
        toolbar.AddChild(appearanceButton);
        if (!_compactMode)
        {
            toolbar.AddChild(alignLeftButton);
            toolbar.AddChild(alignCenterButton);
            toolbar.AddChild(alignRightButton);
            toolbar.AddChild(mergeButton);
            toolbar.AddChild(wrapButton);
            toolbar.AddChild(fillButton);
            toolbar.AddChild(condFormatButton);
            toolbar.AddChild(dataValidationButton);
            toolbar.AddChild(sortAzButton);
            toolbar.AddChild(sortZaButton);
            toolbar.AddChild(filterButton);
            toolbar.AddChild(groupButton);
            toolbar.AddChild(ungroupButton);
            toolbar.AddChild(currencyButton);
            toolbar.AddChild(percentButton);
            toolbar.AddChild(commaButton);
            toolbar.AddChild(increaseDecButton);
            toolbar.AddChild(decreaseDecButton);
            toolbar.AddChild(gridlinesButton);
            toolbar.AddChild(freezeButton);
            toolbar.AddChild(splitButton);
        }

        var recalculate = new StandardButton { Text = "Recalculate", AccessibleName = "Recalculate workbook", ToolTipText = "Recalculate workbook (F9)", PreferredSize = new BSize(100, 28) };
        recalculate.Clicked += (_, _) => TryWorksheetOperation(RecalculateWorkbook);
        toolbar.AddChild(recalculate);
        toolbar.SetBreakBefore(cutButton, UiToolbarBreak.Gap);
        if (!_compactMode)
            toolbar.SetBreakBefore(autoFitButton, UiToolbarBreak.Gap);
        toolbar.SetBreakBefore(boldButton, UiToolbarBreak.Gap);
        if (!_compactMode)
            toolbar.SetBreakBefore(currencyButton, UiToolbarBreak.Gap);
        if (!_compactMode)
            toolbar.SetBreakBefore(alignLeftButton, UiToolbarBreak.Gap);
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
        var formatNote = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" or ".tsv" => " (cell text only; formatting omitted)",
            ".xlsx" or ".ods" => " (basic workbook; basic formula support)",
            _ => string.Empty
        };
        _status.Text = $"Saved: {Path.GetFileName(path)}{formatNote}";
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
        if (Session?.Host is not IUiClipboardHost clipboard) return;
        var range = _cellControl.Grid.GetSelectedRange();
        if (range is not { } r)
        {
            if (_cellControl.SelectedCell is { } pos)
                clipboard.SetText(_cellControl.Sheet[pos.Row, pos.Column]);
            return;
        }

        if (r.StartRow == r.EndRow && r.StartCol == r.EndCol)
        {
            clipboard.SetText(_cellControl.Sheet[r.StartRow, r.StartCol]);
            return;
        }

        var sb = new StringBuilder();
        for (int row = r.StartRow; row <= r.EndRow; row++)
        {
            if (row > r.StartRow) sb.AppendLine();
            for (int col = r.StartCol; col <= r.EndCol; col++)
            {
                if (col > r.StartCol) sb.Append('\t');
                string text = _cellControl.Sheet[row, col];
                if (text.Contains('\t') || text.Contains('\n') || text.Contains('\r') || text.Contains('"'))
                    sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
                else
                    sb.Append(text);
            }
        }

        clipboard.SetText(sb.ToString());
    }

    public void PasteSelection()
    {
        if (ClipboardCommand?.Invoke("paste") == true) return;
        if (_cellControl.SelectedCell is not { } pos) return;
        if (Session?.Host is not IUiClipboardHost clipboard || !clipboard.TryGetText(out string text)) return;
        if (string.IsNullOrEmpty(text)) return;
        if (text.Contains("<table", StringComparison.OrdinalIgnoreCase) || text.Contains("<tr", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                var codec = new HtmlDocumentCodec();
                var result = codec.Read(stream);
                if (result.Status is DocumentResultStatus.Success or DocumentResultStatus.Partial && result.Document is not null)
                {
                    var wb = SpreadsheetWorkbook.FromRichTextDocument(result.Document);
                    if (wb.Sheets.Count > 0)
                    {
                        var sheet = wb.Sheets[0];
                        for (int r = 0; r < sheet.Rows.Count; r++)
                        {
                            int targetRow = pos.Row + r;
                            if (targetRow >= _cellControl.Sheet.RowCount) break;
                            foreach (var cell in sheet.Rows[r].Cells)
                            {
                                int targetCol = pos.Column + cell.Address.Column;
                                if (targetCol >= _cellControl.Sheet.ColumnCount) continue;
                                _cellControl.SetCellValue(new CellPosition(targetRow, targetCol), cell.CachedText ?? string.Empty);
                            }
                        }

                        return;
                    }
                }
            }
            catch
            {
                // Fall back to TSV
            }
        }

        if (text.Contains('\t') || text.Contains('\n') || text.Contains('\r'))
        {
            var rows = ParseTsv(text);
            for (int r = 0; r < rows.Count; r++)
            {
                int targetRow = pos.Row + r;
                if (targetRow >= _cellControl.Sheet.RowCount) break;
                var rowCells = rows[r];
                for (int c = 0; c < rowCells.Count; c++)
                {
                    int targetCol = pos.Column + c;
                    if (targetCol >= _cellControl.Sheet.ColumnCount) break;
                    _cellControl.SetCellValue(new CellPosition(targetRow, targetCol), rowCells[c]);
                }
            }
        }
        else
        {
            var range = _cellControl.Grid.GetSelectedRange();
            if (range is { } rng && (rng.StartRow != rng.EndRow || rng.StartCol != rng.EndCol))
            {
                ForEachSelectedCell((r, c) => _cellControl.SetCellValue(new CellPosition(r, c), text));
            }
            else
            {
                _cellControl.SetCellValue(pos, text);
            }
        }
    }

    private static List<List<string>> ParseTsv(string text)
    {
        var result = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    currentCell.Append('"');
                    i += 2;
                    continue;
                }

                inQuotes = !inQuotes;
                i++;
                continue;
            }

            if (!inQuotes)
            {
                if (c == '\t')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    i++;
                    continue;
                }

                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    result.Add(currentRow);
                    currentRow = new List<string>();
                    i++;
                    continue;
                }

                if (c == '\n')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    result.Add(currentRow);
                    currentRow = new List<string>();
                    i++;
                    continue;
                }
            }

            currentCell.Append(c);
            i++;
        }

        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            result.Add(currentRow);
        }

        return result;
    }

    public void ClearSelection()
    {
        ForEachSelectedCell((r, c) => _cellControl.SetCellValue(new CellPosition(r, c), string.Empty));
    }

    public void ToggleGridlines()
    {
        _cellControl.Sheet.ToggleGridlines();
        _cellControl.Grid.InvalidateRender();
        Invalidate(UiInvalidationKind.Render);
    }

    public void ToggleFreezePanes()
    {
        var sheet = _cellControl.Sheet;
        if (sheet.FrozenRows > 0 || sheet.FrozenColumns > 0)
        {
            sheet.UnfreezePanes();
        }
        else if (_cellControl.SelectedCell is { } sel)
        {
            sheet.FreezePanes(sel.Row, sel.Column);
        }
        else
        {
            sheet.FreezePanes(1, 0);
        }

        _cellControl.Invalidate(UiInvalidationKind.Render);
    }

    public void UnfreezePanes()
    {
        _cellControl.Sheet.UnfreezePanes();
        _cellControl.Invalidate(UiInvalidationKind.Render);
    }

    public void ToggleSplit()
    {
        var sheet = _cellControl.Sheet;
        if (sheet.HasSplit)
        {
            sheet.RemoveSplit();
        }
        else if (_cellControl.SelectedCell is { } sel)
        {
            sheet.SetSplit(sel.Row, sel.Column);
        }
        else
        {
            sheet.SetSplit(sheet.RowCount > 1 ? 1 : 0, sheet.ColumnCount > 1 ? 1 : 0);
        }
        _cellControl.Invalidate(UiInvalidationKind.Render);
    }

    public void SplitHorizontal()
    {
        var sheet = _cellControl.Sheet;
        int row = _cellControl.SelectedCell?.Row ?? (sheet.RowCount > 1 ? 1 : 0);
        sheet.SetSplit(row, 0);
        _cellControl.Invalidate(UiInvalidationKind.Render);
    }

    public void SplitVertical()
    {
        var sheet = _cellControl.Sheet;
        int col = _cellControl.SelectedCell?.Column ?? (sheet.ColumnCount > 1 ? 1 : 0);
        sheet.SetSplit(0, col);
        _cellControl.Invalidate(UiInvalidationKind.Render);
    }

    public void RemoveSplit()
    {
        _cellControl.Sheet.RemoveSplit();
        _cellControl.Invalidate(UiInvalidationKind.Render);
    }

    public event EventHandler<CellsView>? NewWindowOpened;

    public UiWindow? OpenNewWindow()
    {
        var secondaryView = new CellsView(_cellControl.Sheet, _compactMode, _fileAction, _webAction, _printHost)
        {
            CurrentFilePath = _currentFilePath,
            ViewportProvider = ViewportProvider,
            OpenUrl = OpenUrl,
        };
        var window = new StandardWindow
        {
            Title = string.IsNullOrWhiteSpace(_currentFilePath) ? "Broiler Cells (View 2)" : $"{Path.GetFileName(_currentFilePath)}:2 - Broiler Cells",
        };
        window.SetPlacement(new BRect(24, 24, 960, 640));
        window.AddChild(secondaryView);
        secondaryView.OwnerWindow = window;
        if (OwnerWindow?.Session is { } session)
        {
            session.AddRoot(window);
        }
        NewWindowOpened?.Invoke(this, secondaryView);
        return window;
    }

    public void ToggleWrapTextSelected()
    {
        CommitWorksheetEdit();
        bool anyWrapped = false;
        ForEachSelectedCell((r, c) =>
        {
            if (_cellControl.Sheet.GetCellWrapText(r, c))
                anyWrapped = true;
        });
        bool target = !anyWrapped;
        ForEachSelectedCell((r, c) =>
        {
            _cellControl.Sheet.SetCellWrapText(r, c, target);
        });
        _cellControl.Grid.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        _status.Text = target ? "Wrap text enabled on selection." : "Wrap text disabled on selection.";
    }

    public void ToggleMergeSelected()
    {
        CommitWorksheetEdit();
        var range = _cellControl.Grid.GetSelectedRange();
        if (range is not { } r)
        {
            if (_cellControl.SelectedCell is { } pos)
            {
                if (_cellControl.Sheet.TryGetMerge(pos.Row, pos.Column, out _, out _, out _, out _))
                {
                    _cellControl.Sheet.Unmerge(pos.Row, pos.Column);
                    _cellControl.Grid.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
                    _status.Text = "Cells unmerged.";
                }
            }

            return;
        }

        // If selection is already exactly a single merged cell, unmerge it
        if (_cellControl.Sheet.TryGetMerge(r.StartRow, r.StartCol, out int mRootR, out int mRootC, out int mCs, out int mRs))
        {
            if (mRootR == r.StartRow && mRootC == r.StartCol && (mRootR + mRs - 1) == r.EndRow && (mRootC + mCs - 1) == r.EndCol)
            {
                _cellControl.Sheet.Unmerge(r.StartRow, r.StartCol);
                _cellControl.Grid.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
                _status.Text = "Cells unmerged.";
                return;
            }
        }

        if (r.StartRow < r.EndRow || r.StartCol < r.EndCol)
        {
            _cellControl.Sheet.MergeCells(r.StartRow, r.StartCol, r.EndRow, r.EndCol, center: true);
            _cellControl.Grid.SelectCell(new CellPosition(r.StartRow, r.StartCol));
            _cellControl.Grid.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            _status.Text = $"Merged cells {CellPosition.GetColumnLabel(r.StartCol)}{r.StartRow + 1}:{CellPosition.GetColumnLabel(r.EndCol)}{r.EndRow + 1} and centered.";
        }
    }

    public void AutoFitSelected()
    {
        if (_cellControl.SelectedCell is { } pos)
        {
            _cellControl.AutoFitColumn(pos.Column);
        }
    }

    private void ForEachSelectedCell(Action<int, int> action)
    {
        var grid = _cellControl.Grid;
        bool any = false;
        for (int r = 0; r < grid.Sheet.RowCount; r++)
        {
            for (int c = 0; c < grid.Sheet.ColumnCount; c++)
            {
                if (grid.IsCellSelected(r, c))
                {
                    action(r, c);
                    any = true;
                }
            }
        }

        if (!any && grid.SelectedCell is { } sel)
        {
            action(sel.Row, sel.Column);
        }

        _cellControl.InvalidateRender();
    }

    private void OpenCellAppearance()
    {
        if (OwnerWindow is not { IsClosed: false, IsDisposed: false } owner)
            return;

        CommitWorksheetEdit();
        var sheet = _cellControl.Sheet;
        var selection = new HashSet<CellPosition>();
        ForEachSelectedCell((row, column) =>
        {
            if (sheet.TryGetMerge(row, column, out int rootRow, out int rootColumn, out int columns, out int rows))
            {
                for (int r = rootRow; r < rootRow + rows; r++)
                    for (int c = rootColumn; c < rootColumn + columns; c++)
                        selection.Add(new CellPosition(r, c));
            }
            else
                selection.Add(new CellPosition(row, column));
        });
        if (selection.Count == 0)
            return;

        var anchors = selection.Where(cell => !sheet.IsCoveredCell(cell.Row, cell.Column)).ToArray();
        var styles = anchors.Select(cell => sheet.GetContent(cell.Row, cell.Column))
            .Select(content => (content.Font.Foreground, content.Shading, content.Borders)).Distinct().Take(2).Count();
        CellAppearanceDialog.Show(owner, ViewportProvider?.Invoke() ?? owner.Bounds.Size,
            styles > 1 ? "Selection contains mixed formats." : $"Selected cells: {anchors.Length}", InitialColor, Apply);

        BColor InitialColor(string action)
        {
            var colors = anchors.Select(cell => sheet.GetContent(cell.Row, cell.Column)).Select(content => action switch
            {
                "text" => content.Font.Foreground,
                "fill" => content.Shading,
                _ => content.Borders.Top.Color
            }).Distinct().Take(2).ToArray();
            return colors.Length == 1 ? colors[0] : default;
        }

        void Apply(string action, BColor color)
        {
            if (ReferenceEquals(sheet, _cellControl.Sheet))
            {
                if (action is "text" or "fill")
                {
                    foreach (var cell in anchors)
                    {
                        if (action == "text")
                            sheet.SetCellFont(cell.Row, cell.Column, sheet.GetContent(cell.Row, cell.Column).Font with { Foreground = color });
                        else
                            sheet.SetCellShading(cell.Row, cell.Column, color);
                    }
                }
                else
                {
                    var preset = action switch
                    {
                        "outer" => CellBorderPreset.Outer,
                        "inner" => CellBorderPreset.Inner,
                        "all" => CellBorderPreset.All,
                        _ => CellBorderPreset.None
                    };
                    CellAppearance.ApplyBorders(sheet, selection, preset, color);
                }
                _cellControl.InvalidateRender();
            }
        }
    }

    public void ToggleBoldSelected()
    {
        bool anyBold = false;
        ForEachSelectedCell((r, c) =>
        {
            if (_cellControl.Sheet.GetContent(r, c).Font.Bold)
                anyBold = true;
        });
        bool target = !anyBold;
        ForEachSelectedCell((r, c) =>
        {
            var content = _cellControl.Sheet.GetContent(r, c);
            _cellControl.Sheet.SetCellFont(r, c, content.Font with { Bold = target });
        });
    }

    public void ToggleItalicSelected()
    {
        bool anyItalic = false;
        ForEachSelectedCell((r, c) =>
        {
            if (_cellControl.Sheet.GetContent(r, c).Font.Italic)
                anyItalic = true;
        });
        bool target = !anyItalic;
        ForEachSelectedCell((r, c) =>
        {
            var content = _cellControl.Sheet.GetContent(r, c);
            _cellControl.Sheet.SetCellFont(r, c, content.Font with { Italic = target });
        });
    }

    public bool FillDown() => _cellControl.FillDown();
    public bool FillRight() => _cellControl.FillRight();
    public void ToggleUnderlineSelected()
    {
        bool anyUnderline = false;
        ForEachSelectedCell((r, c) =>
        {
            if (_cellControl.Sheet.GetContent(r, c).Font.Underline)
                anyUnderline = true;
        });
        bool target = !anyUnderline;
        ForEachSelectedCell((r, c) =>
        {
            var content = _cellControl.Sheet.GetContent(r, c);
            _cellControl.Sheet.SetCellFont(r, c, content.Font with { Underline = target });
        });
    }

    public void SetSelectedAlignment(Broiler.Documents.Model.TextAlignment alignment)
    {
        ForEachSelectedCell((r, c) => _cellControl.Sheet.SetCellAlignment(r, c, alignment));
    }

    public void ToggleSelectedFill(BColor color)
    {
        bool anyFill = false;
        ForEachSelectedCell((r, c) =>
        {
            if (_cellControl.Sheet.GetContent(r, c).Shading.A > 0)
                anyFill = true;
        });
        var target = anyFill ? default : color;
        ForEachSelectedCell((r, c) => _cellControl.Sheet.SetCellShading(r, c, target));
    }

    public async Task<bool> ExportPdfAsync()
    {
        if (OwnerWindow is null)
            return false;
        var dialog = new StandardFileDialog
        {
            Title = "Export as PDF",
            Mode = UiFileDialogMode.Save,
            DefaultExtension = ".pdf",
            FileName = !string.IsNullOrEmpty(_currentFilePath)
                ? Path.ChangeExtension(Path.GetFileName(_currentFilePath), ".pdf")
                : "Spreadsheet.pdf",
        };
        dialog.SetFileTypeFilters([
            new UiFileDialogFilter("PDF Document (*.pdf)", "*.pdf", ".pdf"),
            new UiFileDialogFilter("All Files (*.*)", "*.*", ""),
        ]);
        UiDialogResult result = await dialog.ShowModal(OwnerWindow);
        if (result.Kind == UiDialogResultKind.Accepted)
        {
            string path = result.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    SheetDocumentSerializer.Save(_cellControl.Sheet, path);
                    _status.Text = $"Exported PDF: {Path.GetFileName(path)}";
                    Invalidate(UiInvalidationKind.Render);
                    return true;
                }
                catch (Exception ex)
                {
                    _status.Text = $"Export failed: {ex.Message}";
                    Invalidate(UiInvalidationKind.Render);
                }
            }
        }

        return false;
    }

    public void ShowPrintDialog()
    {
        if (OwnerWindow is null)
            return;
        if (_printHost is null)
        {
            _ = ExportPdfAsync();
            return;
        }

        if (_printing)
            return;
        var paginator = new DocumentPaginator(DocumentPaginator.CreateDefaultSettings() with
        {
            DefaultFontFamily = "Segoe UI",
        });
        var richDoc = SheetDocumentSerializer.ToRichTextDocument(_cellControl.Sheet);
        string docName = string.IsNullOrEmpty(CurrentFilePath) ? "Spreadsheet" : Path.GetFileNameWithoutExtension(CurrentFilePath);
        var document = new RichTextPagedDocument(richDoc, docName, paginator);
        Task<UiPrintResult> printing;
        try
        {
            printing = UiPrintDialog.PrintAsync(_printHost, document, CreatePrintDialog, OwnerWindow, GetPrintDialogPlacement());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _status.Text = "Could not print: " + exception.Message;
            Invalidate(UiInvalidationKind.Render);
            return;
        }

        _printing = true;
        _status.Text = "Printing...";
        Invalidate(UiInvalidationKind.Render);
        printing.ContinueWith(task =>
        {
            _printing = false;
            if (task.IsFaulted)
                _status.Text = "Could not print: " + (task.Exception?.GetBaseException().Message ?? "job failed");
            else if (task.Result.Outcome == UiPrintOutcome.Cancelled)
                _status.Text = "Print cancelled";
            else
                _status.Text = "Print completed successfully";
            Invalidate(UiInvalidationKind.Render);
        }, TaskScheduler.Default);
    }

    private UiPrintDialog CreatePrintDialog() =>
        new StandardPrintDialog
        {
            PreferredSize = new BSize(680, 520),
            TitleFont = new BFontStyle("Segoe UI", 14, BFontWeight.SemiBold),
            LabelFont = new BFontStyle("Segoe UI", 13),
        };
    private BRect GetPrintDialogPlacement()
    {
        BSize size = ViewportProvider?.Invoke() ?? (OwnerWindow is not null && OwnerWindow.Bounds.Width > 0 && OwnerWindow.Bounds.Height > 0 ? OwnerWindow.Bounds.Size : new BSize(800, 600));
        return new BRect(Math.Max(20, (size.Width - 680) / 2), Math.Max(20, (size.Height - 520) / 2), 680, 520);
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

    public FindReplacePanel FindReplace => _findReplacePanel;
    public bool IsFindReplaceVisible => _findReplacePanel.IsVisible;
    public void ShowFind()
    {
        _findReplacePanel.ShowFind();
        OnSearchTextChanged();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void ShowReplace()
    {
        _findReplacePanel.ShowReplace();
        OnSearchTextChanged();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void CloseFindReplace()
    {
        if (!_findReplacePanel.IsVisible)
            return;

        _findReplacePanel.Hide();
        _cellControl.Focus();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    private void OnSearchTextChanged()
    {
        var options = _findReplacePanel.GetOptions();
        if (string.IsNullOrEmpty(options.SearchText))
        {
            _findReplacePanel.UpdateMatchCount(-1, 0);
            _findReplacePanel.SetStatus(string.Empty);
            return;
        }

        var matches = options.Scope == CellSearchScope.Workbook
            ? CellSearchEngine.FindAllInWorkbook(_workbook, options)
            : CellSearchEngine.FindAll(_cellControl.Sheet, options);

        int currentIdx = -1;
        if (_cellControl.SelectedCell is { } pos)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if ((options.Scope != CellSearchScope.Workbook || matches[i].SheetId == _workbook.SelectedSheet.Id)
                    && matches[i].Position == pos)
                {
                    currentIdx = i;
                    break;
                }
            }
        }

        _findReplacePanel.UpdateMatchCount(currentIdx, matches.Count);
        _findReplacePanel.SetStatus(string.Empty);
    }

    public CellSearchResult? FindNext()
    {
        var options = _findReplacePanel.GetOptions();
        if (string.IsNullOrEmpty(options.SearchText))
        {
            _findReplacePanel.UpdateMatchCount(-1, 0);
            _findReplacePanel.SetStatus("Enter text to find.");
            return null;
        }

        CellSearchResult? result;
        int sheetIdx = -1;

        if (options.Scope == CellSearchScope.Workbook)
        {
            result = CellSearchEngine.FindNextInWorkbook(
                _workbook,
                _workbook.SelectedIndex,
                _cellControl.SelectedCell,
                options,
                out sheetIdx);

            if (result is not null && sheetIdx >= 0 && sheetIdx != _workbook.SelectedIndex)
            {
                SelectWorksheet(sheetIdx);
            }
        }
        else
        {
            result = CellSearchEngine.FindNext(
                _cellControl.Sheet,
                _cellControl.SelectedCell,
                options,
                _workbook.SelectedSheet.Id,
                _workbook.SelectedSheet.Name);
        }

        if (result is not null)
        {
            _cellControl.SelectCell(result.Position);
            _cellControl.ScrollCellIntoView(result.Position);

            var matches = options.Scope == CellSearchScope.Workbook
                ? CellSearchEngine.FindAllInWorkbook(_workbook, options)
                : CellSearchEngine.FindAll(_cellControl.Sheet, options);

            int currentIdx = -1;
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].SheetId == result.SheetId && matches[i].Position == result.Position)
                {
                    currentIdx = i;
                    break;
                }
            }

            _findReplacePanel.UpdateMatchCount(currentIdx, matches.Count);
            _findReplacePanel.SetStatus(string.Empty);
        }
        else
        {
            _findReplacePanel.UpdateMatchCount(-1, 0);
            _findReplacePanel.SetStatus("No matches found.");
        }

        Invalidate(UiInvalidationKind.Render);
        return result;
    }

    public CellSearchResult? FindPrevious()
    {
        var options = _findReplacePanel.GetOptions();
        if (string.IsNullOrEmpty(options.SearchText))
        {
            _findReplacePanel.UpdateMatchCount(-1, 0);
            _findReplacePanel.SetStatus("Enter text to find.");
            return null;
        }

        CellSearchResult? result;
        int sheetIdx = -1;

        if (options.Scope == CellSearchScope.Workbook)
        {
            result = CellSearchEngine.FindPreviousInWorkbook(
                _workbook,
                _workbook.SelectedIndex,
                _cellControl.SelectedCell,
                options,
                out sheetIdx);

            if (result is not null && sheetIdx >= 0 && sheetIdx != _workbook.SelectedIndex)
            {
                SelectWorksheet(sheetIdx);
            }
        }
        else
        {
            result = CellSearchEngine.FindPrevious(
                _cellControl.Sheet,
                _cellControl.SelectedCell,
                options,
                _workbook.SelectedSheet.Id,
                _workbook.SelectedSheet.Name);
        }

        if (result is not null)
        {
            _cellControl.SelectCell(result.Position);
            _cellControl.ScrollCellIntoView(result.Position);

            var matches = options.Scope == CellSearchScope.Workbook
                ? CellSearchEngine.FindAllInWorkbook(_workbook, options)
                : CellSearchEngine.FindAll(_cellControl.Sheet, options);

            int currentIdx = -1;
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].SheetId == result.SheetId && matches[i].Position == result.Position)
                {
                    currentIdx = i;
                    break;
                }
            }

            _findReplacePanel.UpdateMatchCount(currentIdx, matches.Count);
            _findReplacePanel.SetStatus(string.Empty);
        }
        else
        {
            _findReplacePanel.UpdateMatchCount(-1, 0);
            _findReplacePanel.SetStatus("No matches found.");
        }

        Invalidate(UiInvalidationKind.Render);
        return result;
    }

    public bool ReplaceNext()
    {
        var options = _findReplacePanel.GetOptions();
        if (string.IsNullOrEmpty(options.SearchText))
        {
            _findReplacePanel.SetStatus("Enter text to find.");
            return false;
        }

        if (_cellControl.SelectedCell is { } pos &&
            CellSearchEngine.IsMatch(_cellControl.Sheet, pos.Row, pos.Column, options, out _, out _))
        {
            CellSearchEngine.Replace(_cellControl.Sheet, pos, options);
            _cellControl.Sheet.Recalculate();
            FindNext();
            return true;
        }

        var next = FindNext();
        if (next is not null)
        {
            CellSearchEngine.Replace(_cellControl.Sheet, next.Position, options);
            _cellControl.Sheet.Recalculate();
            FindNext();
            return true;
        }

        _findReplacePanel.SetStatus("No match to replace.");
        return false;
    }

    public int ReplaceAll()
    {
        var options = _findReplacePanel.GetOptions();
        if (string.IsNullOrEmpty(options.SearchText))
        {
            _findReplacePanel.SetStatus("Enter text to find.");
            return 0;
        }

        int count;
        if (options.Scope == CellSearchScope.Workbook)
        {
            count = CellSearchEngine.ReplaceAllInWorkbook(_workbook, options);
            _findReplacePanel.SetStatus($"Replaced {count} occurrence{(count == 1 ? "" : "s")} in workbook.");
        }
        else
        {
            count = CellSearchEngine.ReplaceAll(_cellControl.Sheet, options);
            _findReplacePanel.SetStatus($"Replaced {count} occurrence{(count == 1 ? "" : "s")} in sheet.");
        }

        _findReplacePanel.UpdateMatchCount(-1, 0);
        _cellControl.Sheet.Recalculate();
        Invalidate(UiInvalidationKind.Render);
        return count;
    }

    private void OnSelectedCellChanged(object? sender, CellPosition? position)
    {
        _address.Text = position?.ToAddress() ?? string.Empty;
        if (position is { } cell)
        {
            var fmt = _cellControl.Sheet.GetContent(cell.Row, cell.Column).NumberFormat;
            _numberFormat.Text = fmt ?? "General";
            _presetButton.Text = "Preset: " + CellFormatUtility.GetPresetName(fmt);
        }

        if (_findReplacePanel.IsVisible)
        {
            OnSearchTextChanged();
        }
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

                if (!isShift && string.Equals(input.KeyName, "P", StringComparison.OrdinalIgnoreCase))
                {
                    ShowPrintDialog();
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

                if (!isShift && string.Equals(input.KeyName, "B", StringComparison.OrdinalIgnoreCase))
                {
                    ToggleBoldSelected();
                    return true;
                }

                if (!isShift && string.Equals(input.KeyName, "I", StringComparison.OrdinalIgnoreCase))
                {
                    ToggleItalicSelected();
                    return true;
                }

                if (!isShift && string.Equals(input.KeyName, "U", StringComparison.OrdinalIgnoreCase))
                {
                    ToggleUnderlineSelected();
                    return true;
                }

                if (!isShift && string.Equals(input.KeyName, "D", StringComparison.OrdinalIgnoreCase))
                {
                    FillDown();
                    return true;
                }

                if (!isShift && string.Equals(input.KeyName, "R", StringComparison.OrdinalIgnoreCase))
                {
                    FillRight();
                    return true;
                }

                if (!isShift && string.Equals(input.KeyName, "M", StringComparison.OrdinalIgnoreCase))
                {
                    ToggleMergeSelected();
                    return true;
                }

                if (!isShift && IsKey(input, 0x46, "F"))
                {
                    ShowFind();
                    return true;
                }

                if (!isShift && IsKey(input, 0x48, "H"))
                {
                    ShowReplace();
                    return true;
                }

                if (!isShift && IsKey(input, 0x4B, "K"))
                {
                    OpenHyperlinkDialog();
                    return true;
                }

                if (isShift && IsKey(input, 0x4C, "L"))
                {
                    ToggleAutoFilter();
                    return true;
                }
            }

            if (isCtrl && isAlt)
            {
                if (!isShift && IsKey(input, 0x4C, "L"))
                {
                    ReapplyFilters();
                    return true;
                }
            }

            if (isShift && isAlt && !isCtrl)
            {
                if (IsKey(input, 0x27, "Right") || string.Equals(input.KeyName, "ArrowRight", StringComparison.OrdinalIgnoreCase))
                {
                    GroupSelection();
                    return true;
                }
                if (IsKey(input, 0x25, "Left") || string.Equals(input.KeyName, "ArrowLeft", StringComparison.OrdinalIgnoreCase))
                {
                    UngroupSelection();
                    return true;
                }
            }

            if (!isCtrl && !isAlt && IsKey(input, 0x72, "F3"))
            {
                if (isShift)
                    FindPrevious();
                else
                    FindNext();
                return true;
            }

            if (!isCtrl && !isAlt && isShift && IsKey(input, 0x71, "F2"))
            {
                ToggleNoteEditor();
                return true;
            }

            if (!isCtrl && !isAlt && !isShift && IsKey(input, 0x1B, "Escape"))
            {
                if (_filterDropdownPopup.IsPopupVisible)
                {
                    _filterDropdownPopup.Hide();
                    return true;
                }
                if (_sortDialog.IsPanelVisible)
                {
                    _sortDialog.Hide();
                    return true;
                }
                if (_validationPicklistPopup.IsPopupVisible)
                {
                    _validationPicklistPopup.Hide();
                    return true;
                }
                if (_validationAlertCard.IsCardVisible)
                {
                    _validationAlertCard.Dismiss();
                    return true;
                }
                if (_dataValidationPanel.IsPanelVisible)
                {
                    _dataValidationPanel.Hide();
                    return true;
                }
                if (_conditionalFormatPanel.IsPanelVisible)
                {
                    _conditionalFormatPanel.Hide();
                    return true;
                }
                if (_noteEditPanel.IsPanelVisible)
                {
                    CloseNoteEditor();
                    return true;
                }
                if (_findReplacePanel.IsVisible)
                {
                    CloseFindReplace();
                    return true;
                }
            }
        }

        return base.OnInput(input);
    }

    private static bool IsKey(UiInputEvent input, int nativeKeyCode, string name) =>
        input.NativeKeyCode == nativeKeyCode ||
        string.Equals(input.KeyName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(input.KeyName, "VirtualKey:" + nativeKeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
        _presetButton.Measure(new BSize(120, 28));
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
        double gridHeight = Math.Max(100, height - menuHeight - toolbarHeight - editLineHeight - statusHeight - (width < 760 ? 66 : 34) - (width < 700 ? 64 : 32) - 16);
        _cellControl.Measure(new BSize(contentWidth, gridHeight));
        if (_findReplacePanel.IsVisible)
        {
            _findReplacePanel.Measure(new BSize(Math.Min(FindReplacePanel.PanelWidth, contentWidth), _findReplacePanel.PreferredHeight));
        }

        if (_noteEditPanel.IsPanelVisible)
        {
            _noteEditPanel.Measure(new BSize(Math.Min(NoteEditPanel.PanelWidth, contentWidth), NoteEditPanel.PanelHeight));
        }

        if (_conditionalFormatPanel.IsPanelVisible)
        {
            _conditionalFormatPanel.Measure(new BSize(Math.Min(ConditionalFormatPanel.PanelWidth, contentWidth), ConditionalFormatPanel.PanelHeight));
        }

        if (_dataValidationPanel.IsPanelVisible)
        {
            _dataValidationPanel.Measure(new BSize(Math.Min(DataValidationPanel.PanelWidth, contentWidth), DataValidationPanel.PanelHeight));
        }

        if (_validationAlertCard.IsCardVisible)
        {
            _validationAlertCard.Measure(new BSize(Math.Min(ValidationAlertCard.CardWidth, contentWidth), ValidationAlertCard.CardHeight));
        }

        if (_validationPicklistPopup.IsPopupVisible)
        {
            var bounds = _validationPicklistPopup.ComputeDesiredBounds(width, height);
            _validationPicklistPopup.Measure(new BSize(bounds.Width, bounds.Height));
        }

        if (_sortDialog.IsPanelVisible)
        {
            _sortDialog.Measure(new BSize(Math.Min(SortDialog.DialogWidth, contentWidth), SortDialog.DialogHeight));
        }

        if (_hyperlinkPanel.IsPanelVisible)
        {
            _hyperlinkPanel.Measure(new BSize(Math.Min(HyperlinkPanel.PanelWidth, contentWidth), HyperlinkPanel.PanelHeight));
        }

        if (_filterDropdownPopup.IsPopupVisible)
        {
            var bounds = _filterDropdownPopup.ComputeDesiredBounds(width, height);
            _filterDropdownPopup.Measure(new BSize(bounds.Width, bounds.Height));
        }

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
        bool stackedFormat = width < 700;
        _entryModeButton.Arrange(new BRect(finalRect.Left + 8, y, 110, 28));
        _applyValueButton.Arrange(new BRect(finalRect.Left + 122, y, 95, 28));
        double formatX = stackedFormat ? finalRect.Left + 8 : finalRect.Left + 225;
        double formatY = y + (stackedFormat ? 32 : 0);
        _presetButton.Arrange(new BRect(formatX, formatY, 115, 28));
        double remainingW = Math.Max(50, width - formatX - (stackedFormat ? 16 : 8) - 120);
        double editW = Math.Min(140, Math.Max(50, remainingW - 105));
        _numberFormat.Arrange(new BRect(formatX + 119, formatY, editW, 28));
        _applyFormatButton.Arrange(new BRect(formatX + 119 + editW + 4, formatY, 100, 28));
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
        if (_findReplacePanel.IsVisible)
        {
            double panelWidth = Math.Min(FindReplacePanel.PanelWidth, Math.Max(260, contentWidth - 16));
            double panelHeight = _findReplacePanel.PreferredHeight;
            double panelX = Math.Max(finalRect.Left + 8, finalRect.Left + 8 + contentWidth - panelWidth - 8);
            double panelY = y + 4;
            _findReplacePanel.Arrange(new BRect(panelX, panelY, panelWidth, panelHeight));
        }
        else
        {
            _findReplacePanel.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_noteEditPanel.IsPanelVisible)
        {
            double panelWidth = Math.Min(NoteEditPanel.PanelWidth, Math.Max(240, contentWidth - 16));
            double panelHeight = NoteEditPanel.PanelHeight;
            double topOffset = _findReplacePanel.IsVisible ? _findReplacePanel.PreferredHeight + 12 : 4;
            double panelX = Math.Max(finalRect.Left + 8, finalRect.Left + 8 + contentWidth - panelWidth - 8);
            double panelY = y + topOffset;
            _noteEditPanel.Arrange(new BRect(panelX, panelY, panelWidth, panelHeight));
        }
        else
        {
            _noteEditPanel.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_conditionalFormatPanel.IsPanelVisible)
        {
            double panelWidth = Math.Min(ConditionalFormatPanel.PanelWidth, Math.Max(260, contentWidth - 16));
            double panelHeight = ConditionalFormatPanel.PanelHeight;
            double panelX = Math.Max(finalRect.Left + 8, finalRect.Left + 8 + (contentWidth - panelWidth) / 2);
            double panelY = y + 16;
            _conditionalFormatPanel.Arrange(new BRect(panelX, panelY, panelWidth, panelHeight));
        }
        else
        {
            _conditionalFormatPanel.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_dataValidationPanel.IsPanelVisible)
        {
            double panelWidth = Math.Min(DataValidationPanel.PanelWidth, Math.Max(260, contentWidth - 16));
            double panelHeight = DataValidationPanel.PanelHeight;
            double panelX = Math.Max(finalRect.Left + 8, finalRect.Left + 8 + (contentWidth - panelWidth) / 2);
            double panelY = y + 16;
            _dataValidationPanel.Arrange(new BRect(panelX, panelY, panelWidth, panelHeight));
        }
        else
        {
            _dataValidationPanel.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_validationAlertCard.IsCardVisible)
        {
            double cardWidth = Math.Min(ValidationAlertCard.CardWidth, contentWidth - 32);
            double cardHeight = ValidationAlertCard.CardHeight;
            double cardX = Math.Max(finalRect.Left + 16, finalRect.Left + 16 + (contentWidth - cardWidth) / 2);
            double cardY = finalRect.Bottom - cardHeight - 36;
            _validationAlertCard.Arrange(new BRect(cardX, cardY, cardWidth, cardHeight));
        }
        else
        {
            _validationAlertCard.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_validationPicklistPopup.IsPopupVisible)
        {
            var bounds = _validationPicklistPopup.ComputeDesiredBounds(finalRect.Width, finalRect.Height);
            _validationPicklistPopup.Arrange(bounds);
        }
        else
        {
            _validationPicklistPopup.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_sortDialog.IsPanelVisible)
        {
            double panelWidth = Math.Min(SortDialog.DialogWidth, Math.Max(260, contentWidth - 16));
            double panelHeight = SortDialog.DialogHeight;
            double panelX = Math.Max(finalRect.Left + 8, finalRect.Left + 8 + (contentWidth - panelWidth) / 2);
            double panelY = y + 16;
            _sortDialog.Arrange(new BRect(panelX, panelY, panelWidth, panelHeight));
        }
        else
        {
            _sortDialog.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_hyperlinkPanel.IsPanelVisible)
        {
            double panelWidth = Math.Min(HyperlinkPanel.PanelWidth, Math.Max(260, contentWidth - 16));
            double panelHeight = HyperlinkPanel.PanelHeight;
            double panelX = Math.Max(finalRect.Left + 8, finalRect.Left + 8 + (contentWidth - panelWidth) / 2);
            double panelY = y + 16;
            _hyperlinkPanel.Arrange(new BRect(panelX, panelY, panelWidth, panelHeight));
        }
        else
        {
            _hyperlinkPanel.Arrange(new BRect(0, 0, 0, 0));
        }

        if (_filterDropdownPopup.IsPopupVisible)
        {
            var bounds = _filterDropdownPopup.ComputeDesiredBounds(finalRect.Width, finalRect.Height);
            _filterDropdownPopup.Arrange(bounds);
        }
        else
        {
            _filterDropdownPopup.Arrange(new BRect(0, 0, 0, 0));
        }

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

    public NoteEditPanel NoteEditor => _noteEditPanel;
    public bool IsNoteEditorVisible => _noteEditPanel.IsPanelVisible;

    public void ShowNoteEditor(int row, int column)
    {
        var addr = new Broiler.Documents.Model.CellAddress(row, column).Format();
        var existingNote = _cellControl.Sheet.GetCellNote(row, column);
        _noteEditPanel.Show(row, column, addr, existingNote);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void ToggleNoteEditor()
    {
        if (_noteEditPanel.IsPanelVisible)
        {
            CloseNoteEditor();
        }
        else
        {
            var sel = _cellControl.SelectedCell ?? new CellPosition(0, 0);
            ShowNoteEditor(sel.Row, sel.Column);
        }
    }

    public void CloseNoteEditor()
    {
        if (!_noteEditPanel.IsPanelVisible)
            return;
        _noteEditPanel.Hide();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void DeleteSelectedNote()
    {
        if (_cellControl.SelectedCell is not { } sel)
            return;
        _cellControl.Sheet.ClearCellNote(sel.Row, sel.Column);
        if (_noteEditPanel.IsPanelVisible && _noteEditPanel.TargetRow == sel.Row && _noteEditPanel.TargetColumn == sel.Column)
        {
            _noteEditPanel.Hide();
        }
        _cellControl.Grid.InvalidateRender();
        UpdateCalculationStatus();
    }

    private void OnNoteSaved(int row, int column, string text, string? author)
    {
        _cellControl.Sheet.SetCellNote(row, column, text, author);
        _cellControl.Grid.InvalidateRender();
        UpdateCalculationStatus();
    }

    private void OnNoteDeleted(int row, int column)
    {
        _cellControl.Sheet.ClearCellNote(row, column);
        _cellControl.Grid.InvalidateRender();
        UpdateCalculationStatus();
    }

    private void OnNoteClosed()
    {
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void OpenHyperlinkDialog()
    {
        var sel = _cellControl.SelectedCell ?? new CellPosition(0, 0);
        int row = sel.Row;
        int col = sel.Column;
        string addr = new Broiler.Documents.Model.CellAddress(row, col).Format();
        string currentText = _cellControl.Sheet[row, col];
        var existingLink = _cellControl.Sheet.GetCellHyperlink(row, col);

        _hyperlinkPanel.Show(row, col, addr, currentText, existingLink, _workbook);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void CloseHyperlinkDialog()
    {
        if (!_hyperlinkPanel.IsPanelVisible)
            return;
        _hyperlinkPanel.Hide();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void RemoveSelectedHyperlink()
    {
        var sel = _cellControl.SelectedCell ?? new CellPosition(0, 0);
        _cellControl.ClearCellHyperlink(sel.Row, sel.Column);
        if (_hyperlinkPanel.IsPanelVisible && _hyperlinkPanel.TargetRow == sel.Row && _hyperlinkPanel.TargetColumn == sel.Column)
        {
            _hyperlinkPanel.Hide();
        }
        _cellControl.InvalidateRender();
        UpdateCalculationStatus();
    }

    private void OnHyperlinkApplied(int row, int column, string target, string? tooltip, string? displayText)
    {
        if (!string.IsNullOrEmpty(displayText) && displayText != _cellControl.Sheet[row, column])
        {
            _cellControl.SetCellValue(row, column, displayText);
            _cellEditLine.Text = displayText;
        }

        var link = new CellHyperlink(target, tooltip);
        _cellControl.SetCellHyperlink(row, column, link);
        _cellControl.InvalidateRender();
        _cellControl.Focus();
        UpdateCalculationStatus();
    }

    private void OnHyperlinkRemoved(int row, int column)
    {
        _cellControl.ClearCellHyperlink(row, column);
        _cellControl.InvalidateRender();
        _cellControl.Focus();
        UpdateCalculationStatus();
    }

    public CellRangeAddress GetSelectedOrActiveRange()
    {
        var sel = _cellControl.SelectedCell ?? new CellPosition(0, 0);
        if (_cellControl.Grid.CellSelectionAnchor is { } anchor)
        {
            var start = new CellAddress(Math.Min(anchor.Row, sel.Row), Math.Min(anchor.Column, sel.Column));
            var end = new CellAddress(Math.Max(anchor.Row, sel.Row), Math.Max(anchor.Column, sel.Column));
            return new CellRangeAddress(start, end);
        }
        var single = new CellAddress(sel.Row, sel.Column);
        return new CellRangeAddress(single, single);
    }

    public void OpenConditionalFormatting(ConditionalFormatRuleType ruleType = ConditionalFormatRuleType.CellIs, ConditionalFormatOperator op = ConditionalFormatOperator.GreaterThan)
    {
        var range = GetSelectedOrActiveRange();
        _conditionalFormatPanel.ShowForRange(range, _cellControl.Sheet, ruleType, op);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void CloseConditionalFormatting()
    {
        if (!_conditionalFormatPanel.IsPanelVisible)
            return;
        _conditionalFormatPanel.Hide();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void ClearSelectedConditionalFormatting()
    {
        var range = GetSelectedOrActiveRange();
        var toRemove = new List<ConditionalFormat>();
        foreach (var cf in _cellControl.Sheet.ConditionalFormats)
        {
            foreach (var r in cf.Ranges)
            {
                if (r.StartRow <= range.EndRow && r.EndRow >= range.StartRow &&
                    r.StartCol <= range.EndCol && r.EndCol >= range.StartCol)
                {
                    toRemove.Add(cf);
                    break;
                }
            }
        }
        foreach (var cf in toRemove)
            _cellControl.Sheet.RemoveConditionalFormat(cf);
        _cellControl.Grid.InvalidateRender();
    }

    public void ClearAllConditionalFormatting()
    {
        _cellControl.Sheet.ClearConditionalFormats();
        _cellControl.Grid.InvalidateRender();
    }

    public void OpenDataValidation()
    {
        var range = GetSelectedOrActiveRange();
        var existing = _cellControl.Sheet.GetDataValidation(range.StartRow, range.StartCol);
        _dataValidationPanel.ShowForRange(range, _cellControl.Sheet, existing);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void CloseDataValidation()
    {
        if (!_dataValidationPanel.IsPanelVisible)
            return;
        _dataValidationPanel.Hide();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void ClearSelectedDataValidation()
    {
        var range = GetSelectedOrActiveRange();
        _cellControl.Sheet.ClearDataValidations(range);
        _cellControl.Grid.InvalidateRender();
        _status.Text = $"Cleared data validation on {range.Format()}";
    }

    public void ShowValidationPicklist(int row, int col, DataValidation dv, BRect btnBounds)
    {
        var items = DataValidationEngine.GetValidationListItems(_cellControl.Sheet, row, col, dv);
        if (items.Count == 0)
            return;

        string currentVal = _cellControl.Sheet[row, col];
        _validationPicklistPopup.Show(row, col, items, btnBounds, currentVal);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void HandleValidationFailed(int row, int col, ValidationResult res)
    {
        _lastValidationError = res.ErrorMessage;
        _lastValidationResult = res;
        _status.Text = $"{res.ErrorTitle}: {res.ErrorMessage}";
        _validationAlertCard.Show(res);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void ToggleAutoFilter()
    {
        _cellControl.ToggleAutoFilter();
        _cellControl.InvalidateRender();
        _status.Text = _cellControl.Sheet.AutoFilter is not null ? "AutoFilter enabled" : "AutoFilter removed";
    }

    public void QuickSortActiveColumn(bool ascending)
    {
        var sel = _cellControl.SelectedCell ?? new CellPosition(0, 0);
        _cellControl.QuickSort(sel.Column, ascending);
        _cellControl.InvalidateRender();
        _status.Text = $"Sorted Column {CellAddress.ColumnToLetters(sel.Column)} {(ascending ? "A to Z" : "Z to A")}";
    }

    public void OpenSortDialog()
    {
        var range = GetSelectedOrActiveRange();
        if (range.StartRow == range.EndRow && range.StartCol == range.EndCol)
        {
            range = _cellControl.Sheet.GetDataRegion(new CellPosition(range.StartRow, range.StartCol));
        }

        if (_filterDropdownPopup.IsPopupVisible)
            _filterDropdownPopup.Hide();

        _sortDialog.Show(_cellControl.Sheet, range);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void CloseSortDialog()
    {
        if (!_sortDialog.IsPanelVisible)
            return;
        _sortDialog.Hide();
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void ClearActiveFilter()
    {
        if (_cellControl.Sheet.AutoFilter is null)
            return;

        var sel = _cellControl.SelectedCell ?? new CellPosition(0, 0);
        _cellControl.Sheet.ClearFilter(sel.Column);
        _cellControl.InvalidateRender();
        _status.Text = $"Cleared filter on column {CellAddress.ColumnToLetters(sel.Column)}";
    }

    public void ReapplyFilters()
    {
        if (_cellControl.Sheet.AutoFilter is null)
            return;

        _cellControl.Sheet.ReapplyFilters();
        _cellControl.InvalidateRender();
        _status.Text = "Reapplied all filters";
    }

    public void ShowFilterDropdown(int column, BRect btnBounds)
    {
        if (_sortDialog.IsPanelVisible)
            _sortDialog.Hide();

        _filterDropdownPopup.Show(_cellControl.Sheet, column, btnBounds);
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
    }

    public void GroupSelection()
    {
        if (_cellControl.Grid.SelectedColumns.Count > 0)
        {
            _cellControl.GroupSelectedColumns();
        }
        else
        {
            _cellControl.GroupSelectedRows();
        }
        _status.Text = "Group created.";
    }

    public void UngroupSelection()
    {
        if (_cellControl.Grid.SelectedColumns.Count > 0)
        {
            _cellControl.UngroupSelectedColumns();
        }
        else
        {
            _cellControl.UngroupSelectedRows();
        }
        _status.Text = "Group removed.";
    }

    public void ShowDetailSelection()
    {
        _cellControl.ShowSelectedDetail();
        _status.Text = "Outline detail shown.";
    }

    public void HideDetailSelection()
    {
        _cellControl.HideSelectedDetail();
        _status.Text = "Outline detail hidden.";
    }
}

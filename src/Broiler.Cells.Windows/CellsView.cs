using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.AboutDialog.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.FileDialog;
using Broiler.UI.FileDialog.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Window;

namespace Broiler.Cells.Windows;

internal sealed class CellsView : UiElement
{
    internal static readonly BColor Canvas = BColor.FromArgb(255, 231, 235, 240);
    internal static readonly StandardThemeTokens Theme = StandardThemeTokens.Light with
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
    private readonly CellControl _cellControl;
    private readonly StandardLabel _address;
    private readonly StandardLabel _status;
    private readonly StandardCommandDispatcher _dispatcher;
    private string? _currentFilePath;

    public CellsView(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        AccessibleName = "Broiler Cells";

        _dispatcher = new StandardCommandDispatcher();
        _dispatcher.Add(new StandardCommand("file.new", NewDocument));
        _dispatcher.Add(new StandardCommand("file.open", () => _ = OpenDocumentAsync()));
        _dispatcher.Add(new StandardCommand("file.save", SaveDocument));
        _dispatcher.Add(new StandardCommand("file.save-as", () => _ = SaveDocumentAsAsync()));
        _dispatcher.Add(new StandardCommand("file.close", CloseDocument));
        _dispatcher.Add(new StandardCommand("file.exit", ExitApplication));
        _dispatcher.Add(new StandardCommand("help.about", () => _ = ShowAboutDialogAsync()));
        _dispatcher.Add(new StandardCommand("help.info", () => _ = ShowAboutDialogAsync()));

        _menu = new StandardMenu
        {
            PresentationMode = UiMenuPresentationMode.MenuBar,
            CommandDispatcher = _dispatcher,
            Background = Canvas,
        };

        var fileMenu = new UiMenuItem("menu.file", "File") { AccessKey = 'F' };
        fileMenu.Children.Add(new UiMenuItem("file.new", "New") { AccessKey = 'N', Accelerator = "Ctrl+N", CommandName = "file.new" });
        fileMenu.Children.Add(new UiMenuItem("file.open", "Open") { AccessKey = 'O', Accelerator = "Ctrl+O", CommandName = "file.open" });
        fileMenu.Children.Add(new UiMenuItem("file.save", "Save") { AccessKey = 'S', Accelerator = "Ctrl+S", CommandName = "file.save" });
        fileMenu.Children.Add(new UiMenuItem("file.save-as", "Save as") { AccessKey = 'A', Accelerator = "Ctrl+Shift+S", CommandName = "file.save-as" });
        fileMenu.Children.Add(new UiMenuItem("file.sep1", "") { IsSeparator = true });
        fileMenu.Children.Add(new UiMenuItem("file.close", "Close") { AccessKey = 'C', Accelerator = "Ctrl+W", CommandName = "file.close" });
        fileMenu.Children.Add(new UiMenuItem("file.sep2", "") { IsSeparator = true });
        fileMenu.Children.Add(new UiMenuItem("file.exit", "Exit") { AccessKey = 'X', Accelerator = "Alt+F4", CommandName = "file.exit" });

        var helpMenu = new UiMenuItem("menu.help", "Help") { AccessKey = 'H' };
        helpMenu.Children.Add(new UiMenuItem("help.about", "About") { AccessKey = 'A', CommandName = "help.about" });

        _menu.SetItems([fileMenu, helpMenu]);

        _cellControl = new CellControl(sheet, isEditingEnabled: true, areHeadersVisible: true);
        _cellControl.SelectedCellChanged += OnSelectedCellChanged;

        _address = new StandardLabel(_cellControl.SelectedAddress ?? string.Empty, StandardLabelRole.Default) { Font = new BFontStyle("Segoe UI", 12) };
        _status = new StandardLabel("Session only", StandardLabelRole.Muted) { Font = new BFontStyle("Segoe UI", 12) };

        AddChild(_menu);
        AddChild(_cellControl);
        AddChild(_address);
        AddChild(_status);
    }

    public StandardMenu Menu => _menu;
    public CellControl CellControl => _cellControl;
    public StandardLabel AddressLabel => _address;
    public StandardLabel StatusLabel => _status;
    public StandardCommandDispatcher CommandDispatcher => _dispatcher;
    public string AddressText => _address.Text;
    public string StatusText => _status.Text;
    public string? CurrentFilePath => _currentFilePath;
    public UiWindow? OwnerWindow { get; set; }

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? TitleChanged;

    public void NewDocument()
    {
        var newSheet = new SheetModel(10, 10);
        _cellControl.SetSheet(newSheet);
        _cellControl.SelectCell(new CellPosition(0, 0));
        _currentFilePath = null;
        _status.Text = "Session only";
        TitleChanged?.Invoke(this, "Broiler Cells");
        Invalidate(UiInvalidationKind.Render);
    }

    public void CloseDocument()
    {
        NewDocument();
    }

    public void SaveDocument()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            Save(_currentFilePath);
        }
        else
        {
            _ = SaveDocumentAsAsync();
        }
    }

    public async Task<bool> OpenDocumentAsync()
    {
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
            new UiFileDialogFilter("All Files (*.*)", "*.*", ""),
        ]);

        UiDialogResult result = await dialog.ShowModal(OwnerWindow);
        if (result.Kind == UiDialogResultKind.Accepted)
        {
            string path = result.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                if (Session?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    dispatcher.Post(() => Load(path));
                else
                    Load(path);
                return true;
            }
        }

        return false;
    }

    public async Task<bool> SaveDocumentAsAsync()
    {
        if (OwnerWindow is null)
            return false;

        var dialog = new StandardFileDialog
        {
            Title = "Save As",
            Mode = UiFileDialogMode.Save,
            DefaultExtension = ".csv",
            FileName = !string.IsNullOrEmpty(_currentFilePath) ? Path.GetFileName(_currentFilePath) : "Spreadsheet.csv",
        };
        dialog.SetFileTypeFilters([
            new UiFileDialogFilter("CSV Files (*.csv)", "*.csv", ".csv"),
            new UiFileDialogFilter("All Files (*.*)", "*.*", ""),
        ]);

        UiDialogResult result = await dialog.ShowModal(OwnerWindow);
        if (result.Kind == UiDialogResultKind.Accepted)
        {
            string path = result.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (Session?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    dispatcher.Post(() => Save(path));
                else
                    Save(path);
                return true;
            }
        }

        return false;
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SheetCsvSerializer.Save(_cellControl.Sheet, path);
        _currentFilePath = path;
        _status.Text = $"Saved: {Path.GetFileName(path)}";
        TitleChanged?.Invoke(this, $"Broiler Cells - {Path.GetFileName(path)}");
        Invalidate(UiInvalidationKind.Render);
    }

    public void Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SheetModel sheet = SheetCsvSerializer.Load(path);
        _cellControl.SetSheet(sheet);
        _cellControl.SelectCell(new CellPosition(0, 0));
        _currentFilePath = path;
        _status.Text = Path.GetFileName(path);
        TitleChanged?.Invoke(this, $"Broiler Cells - {Path.GetFileName(path)}");
        Invalidate(UiInvalidationKind.Render);
    }

    public void ExitApplication()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public async Task ShowAboutDialogAsync()
    {
        if (OwnerWindow is null)
            return;

        var dialog = new StandardAboutDialog
        {
            ProductName = "Broiler Cells",
        };
        await dialog.ShowModal(OwnerWindow);
    }

    private void OnSelectedCellChanged(object? sender, CellPosition? position) =>
        _address.Text = position?.ToAddress() ?? string.Empty;

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
            }
        }

        return base.OnInput(input);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        double menuHeight = _menu.MenuBarHeight;
        _menu.Measure(new BSize(availableSize.Width, menuHeight));

        double width = Math.Max(0, availableSize.Width - 24);
        double gridHeight = Math.Max(0, availableSize.Height - menuHeight - 54);

        _cellControl.Measure(new BSize(width, gridHeight));
        _address.Measure(new BSize(width / 2, 20));
        _status.Measure(new BSize(width / 2, 20));

        return availableSize;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double menuHeight = _menu.MenuBarHeight;
        _menu.Arrange(new BRect(finalRect.X, finalRect.Y, finalRect.Width, menuHeight));

        double width = Math.Max(0, finalRect.Width - 24);
        double gridY = finalRect.Y + menuHeight + 10;
        double gridHeight = Math.Max(0, finalRect.Height - menuHeight - 54);
        _cellControl.Arrange(new BRect(finalRect.X + 12, gridY, width, gridHeight));

        double statusY = finalRect.Y + Math.Max(0, finalRect.Height - 28);
        double statusWidth = Math.Min(300, Math.Max(120, width - 140));
        _address.Arrange(new BRect(finalRect.X + 12, statusY, 140, 20));
        _status.Arrange(new BRect(finalRect.X + 12 + Math.Max(0, width - statusWidth), statusY, statusWidth, 20));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        context.RenderList.PushClip(Bounds);
        context.RenderList.FillRect(Bounds, Canvas);
        base.RenderCore(context);
        context.RenderList.PopClip();
    }
}

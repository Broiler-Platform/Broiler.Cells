using System;
using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Tooltip.Standard;
using Broiler.UI.Window;
using Broiler.UI.Window.Standard;

namespace Broiler.Cells.UI;

/// <summary>
/// The top-level application coordinating UI session, window, menu, toolbar,
/// edit line, and grid for Broiler Cells across all platforms.
/// </summary>
public sealed class CellsApp : IDisposable
{
    private const string ApplicationName = "Broiler Cells";

    private readonly CellsUiHost _host;
    private readonly UiSession _session;
    private readonly StandardWindow _rootWindow;
    private readonly StandardTooltip _tooltip;
    private readonly StandardTooltipController _tooltips;
    private readonly CellsView _view;
    private readonly Action _requestClose;
    private readonly Action<string>? _setWindowTitle;
    private readonly bool _compactMode;

    public CellsApp(
        CellsUiHost host,
        Action requestClose,
        SheetModel? initialSheet = null,
        bool compactMode = false,
        Action<string>? setWindowTitle = null,
        Action<Uri>? openUrl = null,
        Action<string>? fileAction = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _requestClose = requestClose ?? throw new ArgumentNullException(nameof(requestClose));
        _setWindowTitle = setWindowTitle;
        _compactMode = compactMode;

        _session = new StandardUiSessionBuilder()
            .WithDispatcher(new ImmediateUiDispatcher())
            .Build(_host);

        SheetModel sheet = initialSheet ?? CreateDefaultSheet();
        _view = new CellsView(sheet, compactMode: _compactMode, fileAction: fileAction)
        {
            OpenUrl = openUrl,
            ViewportProvider = () => _host.ViewportSize,
        };

        _rootWindow = new StandardWindow
        {
            Title = ApplicationName,
            Background = CellsPalette.Canvas,
            BorderThickness = 0,
            Chrome = UiWindowChrome.None,
        };

        _tooltip = new StandardTooltip
        {
            Font = new BFontStyle("Segoe UI", 12),
            Background = CellsPalette.MenuPopup,
            Foreground = CellsPalette.Title,
            BorderColor = CellsPalette.WindowBorder,
        };
        _tooltips = new StandardTooltipController(_tooltip);

        _view.OwnerWindow = _rootWindow;
        _view.ExitRequested += (_, _) => _requestClose();
        _view.TitleChanged += (_, title) => _setWindowTitle?.Invoke(title);

        _rootWindow.AddChild(_view);
        _session.AddRoot(_rootWindow);
        _rootWindow.OpenOwnedWindow(_tooltip, new BRect(0, 0, 1, 1));
        _tooltip.Deactivate();

        StandardThemeController.Apply(_session, CellsView.Theme);
        _view.CellControl.SelectCell(new CellPosition(0, 0));
    }

    public UiSession Session => _session;
    public CellsView View => _view;
    public StandardWindow RootWindow => _rootWindow;
    public StandardToolbar Toolbar => _view.Toolbar;
    public CellEditLine CellEditLine => _view.CellEditLine;
    public CellControl CellControl => _view.CellControl;
    public string WindowTitle => string.IsNullOrEmpty(_view.CurrentFilePath)
        ? ApplicationName
        : $"{ApplicationName} - {System.IO.Path.GetFileName(_view.CurrentFilePath)}";

    public BRenderList RenderFrame() => _session.RenderFrame();

    public bool Dispatch(UiInputEvent input) => _session.DispatchInput(input);

    private static SheetModel CreateDefaultSheet()
    {
        return new SheetModel(initialValue: (row, column) => (row, column) switch
        {
            (0, 0) => "Item",
            (0, 1) => "Quantity",
            (1, 0) => "Paper",
            (1, 1) => "12",
            (2, 0) => "Pens",
            (2, 1) => "4",
            (4, 0) => "Long text stays inside this cell",
            (9, 9) => "Last cell",
            _ => string.Empty,
        });
    }

    public void Dispose()
    {
        _session.Dispose();
        _host.Dispose();
    }
}

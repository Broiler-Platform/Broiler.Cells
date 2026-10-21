using System.Runtime.InteropServices;
using Broiler.Cells.Core;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Hosting.Windows;
using Broiler.Hosting.Windows.Accessibility;
using Broiler.Hosting.Windows.Input;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.UI.Window;
using Broiler.UI.Window.Standard;

namespace Broiler.Cells.Windows;

internal sealed class CellsWindow : Direct2DWindow, IUiHost, IUiCursorHost
{
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint LoadCursorW(nint hInstance, nint lpCursorName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetCursor(nint hCursor);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetKeyState(int virtualKey);

    private nint _activeCursorHandle;
    private readonly UiSession _session;
    private readonly CellsView _view;
    private readonly StandardWindow _rootWindow;
    private readonly StandardQueuedUiDispatcher _dispatcher;
    private WindowsInputBridge? _inputBridge;
    private WindowsAutomationBridge? _automation;

    // Direct2DWindow still exposes the graphics input events. Keep this adapter at the host boundary.
#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _input = new("broiler-cells");
#pragma warning restore CS0618

    public CellsWindow() : base(new BWindowOptions
    {
        Title = "Broiler Cells",
        ClientWidth = 1000,
        ClientHeight = 400,
        ClearColor = CellsView.Canvas,
        RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
    })
    {
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(this);
        _view = new CellsView(new SheetModel(initialValue: (row, column) => (row, column) switch
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
        }));

        _rootWindow = new StandardWindow
        {
            Chrome = UiWindowChrome.None,
            BorderThickness = 0,
            Background = CellsView.Canvas,
        };
        _rootWindow.AddChild(_view);
        _view.OwnerWindow = _rootWindow;

        _view.ExitRequested += (_, _) => Close();
        _view.TitleChanged += (_, newTitle) => SetTitle(newTitle);

        _session.AddRoot(_rootWindow);
        StandardThemeController.Apply(_session, CellsView.Theme);
        _view.CellControl.SelectCell(new CellPosition(1, 1));
    }

    BSize IUiHost.ViewportSize => ClientSize;
    double IUiHost.Scale => DpiScale;
    BRenderList IUiHost.CreateRenderList(int capacity) => new(capacity);
    void IUiHost.Invalidate(UiInvalidation invalidation) => Invalidate();
    void IUiHost.Present(BRenderList renderList) { } // The native window presents BuildRenderList's result.

    protected override void OnCreated()
    {
        _inputBridge = new WindowsInputBridge(NativeHandle, RenderNativeHandle, _session,
            scaleProvider: () => DpiScale, invalidateAction: Invalidate);
        _automation = new WindowsAutomationBridge(RenderNativeHandle, _session, _rootWindow, () => DpiScale);
    }

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        DrainDispatcher();
        return _session.RenderFrame();
    }

    /// <summary>
    /// Runs the work other threads posted back to the UI. A callback that throws is logged
    /// rather than taking the window down, which is how Direct2DWindow treats callbacks
    /// posted to it.
    /// </summary>
    private void DrainDispatcher()
    {
        try
        {
            _dispatcher.Drain();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    protected override void OnResized(BSize clientSize, double dpiScale) => Invalidate();
    protected override void OnPointerDown(BPointerEventArgs e) => DispatchPointerButton(e);
    protected override void OnPointerUp(BPointerEventArgs e) => DispatchPointerButton(e);
    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_input.FromPointerMove(e));
    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_input.FromMouseWheel(e));
    protected override void OnTextInput(BTextInputEventArgs e) => Dispatch(_input.FromText(e));
    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Down));
    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Up));

    private void DispatchPointerButton(BPointerEventArgs e)
    {
        // The legacy graphics adapter drops pointer modifiers. Read the state of
        // the current Windows message so Ctrl/Shift header selection reaches the grid.
        var input = _input.FromPointerButton(e);
        InputModifiers modifiers = InputModifiers.None;
        if (GetKeyState(0x11) < 0)
            modifiers |= InputModifiers.Control;
        if (GetKeyState(0x10) < 0)
            modifiers |= InputModifiers.Shift;
        if (GetKeyState(0x12) < 0)
            modifiers |= InputModifiers.Alt;

        MouseButtons buttons = MouseButtons.None;
        if (e.Buttons.HasFlag(BMouseButtons.Left)) buttons |= MouseButtons.Left;
        if (e.Buttons.HasFlag(BMouseButtons.Right)) buttons |= MouseButtons.Right;
        if (e.Buttons.HasFlag(BMouseButtons.Middle)) buttons |= MouseButtons.Middle;
        Dispatch(UiInputEvent.FromMouseButton(new MouseButtonEvent(input.Header,
            InputPoint.ClientDeviceIndependentPixels(e.Position.X, e.Position.Y), buttons,
            input.MouseButton, input.MouseButtonTransition!.Value, InputEventSource.Synthetic, modifiers)));
    }

    private void Dispatch(UiInputEvent input)
    {
        if (_session.DispatchInput(input))
            Invalidate();
    }

    public void SetCursor(UiCursorShape shape)
    {
        nint cursor = shape switch
        {
            UiCursorShape.ResizeHorizontal => LoadCursorW(0, 32644),
            UiCursorShape.ResizeVertical => LoadCursorW(0, 32645),
            UiCursorShape.Hand => LoadCursorW(0, 32649),
            UiCursorShape.Text => LoadCursorW(0, 32513),
            UiCursorShape.Wait => LoadCursorW(0, 32514),
            _ => LoadCursorW(0, 32512),
        };
        _activeCursorHandle = cursor;
        SetCursor(cursor);
    }

    protected override void OnNativeWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == 0x0020 /* WM_SETCURSOR */ && _activeCursorHandle != 0)
        {
            SetCursor(_activeCursorHandle);
        }

        base.OnNativeWindowMessage(hwnd, message, wParam, lParam);
        if (hwnd == NativeHandle)
        {
            WindowsWindowSizing.OnMessage(hwnd, message, lParam, DpiScale);
            _inputBridge?.OnTopLevelMessage(message, wParam, lParam);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _automation?.Dispose();
            _inputBridge?.Dispose();
            _session.Dispose();
        }
        base.Dispose(disposing);
    }
}

using Broiler.Cells.Core;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Hosting.Windows;
using Broiler.Hosting.Windows.Accessibility;
using Broiler.Hosting.Windows.Input;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Cells.Windows;

internal sealed class CellsWindow : Direct2DWindow, IUiHost
{
    private readonly UiSession _session;
    private readonly CellsView _view;
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
        ClientHeight = 360,
        ClearColor = CellsView.Canvas,
        RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
    })
    {
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(() => _dispatcher!.Drain()));
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
        _session.AddRoot(_view);
        StandardThemeController.Apply(_session, CellsView.Theme);
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
        _automation = new WindowsAutomationBridge(RenderNativeHandle, _session, _view, () => DpiScale);
    }

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        _dispatcher.Drain();
        return _session.RenderFrame();
    }

    protected override void OnResized(BSize clientSize, double dpiScale) => Invalidate();
    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_input.FromPointerMove(e));
    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_input.FromMouseWheel(e));
    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Down));
    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Up));

    private void Dispatch(UiInputEvent input)
    {
        if (_session.DispatchInput(input))
            Invalidate();
    }

    protected override void OnNativeWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
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

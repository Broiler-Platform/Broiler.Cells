using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.WebAssembly;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Input.Text;
using Broiler.UI;

namespace Broiler.Cells.WebAssembly;

[SupportedOSPlatform("browser")]
internal static class BrowserCellsApp
{
    private const string CanvasSelector = "#cells-canvas";
    private static readonly InputDeviceId PointerDevice = InputDeviceId.FromOpaqueValue("browser-primary-pointer");
    private static readonly InputDeviceId KeyboardDevice = InputDeviceId.FromOpaqueValue("browser-keyboard");
    private static readonly InputDeviceId TextDevice = InputDeviceId.FromOpaqueValue("browser-text");

    private static BrowserCanvasRenderer? _renderer;
    private static BrowserCanvasUiHost? _host;
    private static BrowserUiDispatcher? _dispatcher;
    private static CellsApp? _app;
    private static long _sequence;
    private static MouseButtons _buttons;

    internal static async Task StartAsync()
    {
        Broiler.Graphics.Imaging.BImageCodecs.Use(new Broiler.Media.MediaCodecCatalog(
            Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        await BrowserCanvasRenderer.LoadModuleAsync("../broiler.graphics.webassembly.js");

        var renderer = new BrowserCanvasRenderer();
        renderer.Initialize(CanvasSelector);
        _renderer = renderer;

        _dispatcher = new BrowserUiDispatcher(BrowserInterop.ScheduleFrame);
        _host = new BrowserCanvasUiHost(renderer);

        var appUiHost = new CellsUiHost(
            () => _host.ViewportSize,
            () => _host.Scale,
            BrowserInterop.ScheduleFrame,
            renderList => _host.Present(renderList),
            () => _host.TryGetText(out string text) ? text : null,
            text => _host.SetText(text),
            caret => { if (caret is not null) _host.PublishCaret(caret); else _host.ClearCaret(null!); },
            getRenderer: null,
            createImage: bytes => _host.CreateImage(bytes),
            createImagePixels: pixels => _host.CreateImage(pixels),
            releaseImage: image => _host.ReleaseImage(image));

        _app = new CellsApp(
            appUiHost,
            requestClose: static () => { },
            setWindowTitle: BrowserInterop.SetTitle,
            openUrl: url => BrowserInterop.OpenUrl(url.AbsoluteUri));

        _app.RenderFrame();
        BrowserInterop.Ready(_host.ViewportSize.Width, _host.ViewportSize.Height);
    }

    internal static void Resize(double width, double height, double dpr) =>
        _dispatcher?.Post(() =>
        {
            if (_host is not null && _host.Resize(width, height, dpr))
            {
                _app?.RootWindow.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            }
        });

    internal static void RenderFrame()
    {
        var sw = Stopwatch.StartNew();
        int drained = _dispatcher?.Drain() ?? 0;
        var renderList = _app?.RenderFrame();
        sw.Stop();
        if (sw.ElapsedMilliseconds > 16 || drained > 0)
        {
            Console.WriteLine($"[Cells-WASM] RenderFrame: drained {drained} events, {renderList?.Commands.Count ?? 0} commands, {sw.ElapsedMilliseconds}ms");
        }
    }

    internal static void PointerMove(double x, double y, int buttons, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            _buttons = (MouseButtons)buttons;
            _app?.Dispatch(UiInputEvent.FromMouseMove(new MouseMoveEvent(
                Header(PointerDevice, timestampMilliseconds),
                InputPoint.ClientDeviceIndependentPixels(x, y),
                _buttons)));
        });

    internal static void PointerButton(double x, double y, int buttons, int domButton, bool down, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            Console.WriteLine($"[Cells-WASM] PointerButton: button={domButton}, down={down}, pos=({x:F1}, {y:F1})");
            _buttons = (MouseButtons)buttons;
            _app?.Dispatch(UiInputEvent.FromMouseButton(new MouseButtonEvent(
                Header(PointerDevice, timestampMilliseconds),
                InputPoint.ClientDeviceIndependentPixels(x, y),
                _buttons,
                MapButton(domButton),
                down ? MouseButtonTransition.Down : MouseButtonTransition.Up)));
        });

    internal static void PointerWheel(double x, double y, int buttons, bool horizontal, double deltaNotches, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            _buttons = (MouseButtons)buttons;
            _app?.Dispatch(UiInputEvent.FromMouseWheel(new MouseWheelEvent(
                Header(PointerDevice, timestampMilliseconds),
                InputPoint.ClientDeviceIndependentPixels(x, y),
                _buttons,
                horizontal ? MouseWheelAxis.Horizontal : MouseWheelAxis.Vertical,
                deltaNotches)));
        });

    internal static void KeyboardKey(string keyName, bool down, int modifiers, int nativeKeyCode, bool repeat, int location, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            var modifierState = (KeyboardModifierState)modifiers;
            var input = new KeyboardKeyEvent(
                Header(KeyboardDevice, timestampMilliseconds),
                Broiler.Input.Keyboard.KeyboardKey.FromName(keyName),
                down ? KeyboardKeyTransition.Down : KeyboardKeyTransition.Up,
                modifierState,
                nativeKeyCode,
                ScanCode: 0,
                RepeatCount: repeat ? 2 : 1,
                IsExtended: location != 0,
                WasDown: repeat,
                Location: Enum.IsDefined((KeyboardKeyLocation)location) ? (KeyboardKeyLocation)location : KeyboardKeyLocation.Standard);

            _app?.Dispatch(UiInputEvent.FromKeyboardKey(input));
        });

    internal static void TextInput(string text, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            if (string.IsNullOrEmpty(text))
                return;

            _app?.Dispatch(UiInputEvent.FromTextInput(new TextInputEvent(
                Header(TextDevice, timestampMilliseconds),
                text)));
        });

    internal static string ClipboardEvent(string operation, string text)
    {
        if (_host is null)
            return string.Empty;

        _host.BeginClipboardEvent(string.Equals(operation, "paste", StringComparison.Ordinal) ? text : null);
        if (string.Equals(operation, "cut", StringComparison.OrdinalIgnoreCase))
            _app?.View.CutSelection();
        else if (string.Equals(operation, "copy", StringComparison.OrdinalIgnoreCase))
            _app?.View.CopySelection();
        else if (string.Equals(operation, "paste", StringComparison.OrdinalIgnoreCase))
            _app?.View.PasteSelection();

        string output = _host.EndClipboardEvent();
        BrowserInterop.ScheduleFrame();
        return output;
    }

    internal static void LoadDocument(string fileName, string base64Data) =>
        _dispatcher?.Post(() =>
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64Data ?? string.Empty);
                using var ms = new MemoryStream(bytes);
                SheetModel sheet = SheetCsvSerializer.Load(ms);
                _app?.CellControl.SetSheet(sheet);
                _app?.CellControl.SelectCell(new CellPosition(0, 0));
                BrowserInterop.SetTitle($"Broiler Cells - {fileName}");
            }
            catch (Exception ex)
            {
                BrowserInterop.Failed(ex.GetType().Name, ex.ToString());
            }
        });

    internal static void Dispose()
    {
        _app?.Dispose();
        _app = null;
        _host?.Dispose();
        _host = null;
        _renderer?.Dispose();
        _renderer = null;
    }

    private static InputEventHeader Header(InputDeviceId device, double timestampMilliseconds) =>
        new(device, new InputTimestamp((long)Math.Max(0, timestampMilliseconds * 1000), 1_000_000, "browser-performance"), ++_sequence);

    private static MouseButton MapButton(int domButton) => domButton switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        3 => MouseButton.X1,
        4 => MouseButton.X2,
        _ => MouseButton.None,
    };
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserCellsExports
{
    [JSExport]
    internal static void ResizeUi(double logicalWidth, double logicalHeight, double dpr) =>
        BrowserCellsApp.Resize(logicalWidth, logicalHeight, dpr);

    [JSExport]
    internal static void RenderUiFrame() => BrowserCellsApp.RenderFrame();

    [JSExport]
    internal static void UiPointerMove(double x, double y, int buttons, double timestampMilliseconds) =>
        BrowserCellsApp.PointerMove(x, y, buttons, timestampMilliseconds);

    [JSExport]
    internal static void UiPointerButton(double x, double y, int buttons, int button, bool down, double timestampMilliseconds) =>
        BrowserCellsApp.PointerButton(x, y, buttons, button, down, timestampMilliseconds);

    [JSExport]
    internal static void UiPointerWheel(double x, double y, int buttons, bool horizontal, double deltaNotches, double timestampMilliseconds) =>
        BrowserCellsApp.PointerWheel(x, y, buttons, horizontal, deltaNotches, timestampMilliseconds);

    [JSExport]
    internal static void UiKeyboardKey(string keyName, bool down, int modifiers, int nativeKeyCode, bool repeat, int location, double timestampMilliseconds) =>
        BrowserCellsApp.KeyboardKey(keyName, down, modifiers, nativeKeyCode, repeat, location, timestampMilliseconds);

    [JSExport]
    internal static void UiTextInput(string text, double timestampMilliseconds) =>
        BrowserCellsApp.TextInput(text, timestampMilliseconds);

    [JSExport]
    internal static string UiClipboardEvent(string operation, string text) =>
        BrowserCellsApp.ClipboardEvent(operation, text);

    [JSExport]
    internal static void LoadDocument(string fileName, string base64Data) =>
        BrowserCellsApp.LoadDocument(fileName, base64Data);

    [JSExport]
    internal static void Dispose() => BrowserCellsApp.Dispose();
}

using System;
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
using Broiler.UI.Edit.Standard;

namespace Broiler.Cells.WebAssembly;

[SupportedOSPlatform("browser")]
internal static partial class BrowserCellsApp
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
            openUrl: url => BrowserInterop.OpenUrl(url.AbsoluteUri),
            fileAction: FileAction);
        AttachSheet(_app.CellControl.Sheet);
        _app.View.ClipboardCommand = operation => {
            if (_host.IsClipboardEventActive) return false;
            BrowserInterop.ReportOperation("Clipboard", "Use Ctrl/Cmd+C, X or V to copy, cut or paste the selected cell through the browser clipboard.");
            return true;
        };

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

        _dispatcher?.Drain();
        ObservePendingEditor();
        _app?.RenderFrame();
        // The browser textarea must follow the managed caret to receive committed text.
        var caret = _host?.CurrentCaret;
        BrowserInterop.PublishFrame(
            _host?.FrameIndex ?? 0, caret is not null,
            caret?.Bounds.X ?? 0, caret?.Bounds.Y ?? 0,
            caret?.Bounds.Width ?? 0, caret?.Bounds.Height ?? 0,
            caret?.CaretIndex ?? 0, caret?.SelectionStart ?? 0,
            caret?.SelectionLength ?? 0, caret is not null, string.Empty, false);
        PublishDocumentState();
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
            if (string.IsNullOrEmpty(text) || !AcceptText(text))
                return;

            _app?.Dispatch(UiInputEvent.FromTextInput(new TextInputEvent(
                Header(TextDevice, timestampMilliseconds),
                text)));
            _modified = true; _revision++;
        });

    internal static string ClipboardEvent(string operation, string text)
    {
        _dispatcher?.Drain();
        if (_host is null || _app is null) return string.Empty;
        if (!CommitPendingEdits()) return string.Empty;
        _host.BeginClipboardEvent(operation == "paste" ? text : null);
        try {
            if (operation == "copy") _app.View.CopySelection();
            else if (operation == "cut") _app.View.CutSelection();
            else if (operation == "paste") {
                if (text.Length == 0) { BrowserInterop.ReportOperation("Clipboard", "Clipboard has no plain text; selected cell retained."); return string.Empty; }
                if (text.Length > MaximumCellCharacters) { BrowserInterop.ReportOperation("Failed", "Clipboard text exceeds the 16,384 character cell limit."); return string.Empty; }
                _app.View.PasteSelection();
            }
            return _host.EndClipboardEvent();
        } finally { _host.EndClipboardEvent(); BrowserInterop.ScheduleFrame(); }
    }

    private static bool _compositionStartedEdit;

    private static bool AcceptText(string text)
    {
        var edit = _app?.Session.FocusedElement as StandardEdit;
        if ((edit?.Text.Length ?? 0) - (edit?.SelectionLength ?? 0) + text.Length <= MaximumCellCharacters) return true;
        BrowserInterop.ReportOperation("Failed", "Input exceeds the 16,384 character cell limit; input was not inserted.");
        return false;
    }

    internal static void TextComposition(string text, int state, double timestamp) => _dispatcher?.Post(() => {
        if (_app is null) return;
        // A composition starts cell editing before preedit is dispatched to StandardEdit.
        if (state == 0) {
            _compositionStartedEdit = _app.Session.FocusedElement is not StandardEdit;
            if (_compositionStartedEdit) _app.CellControl.StartEditing(string.Empty);
        }
        if ((state == 1 || state == 2) && !AcceptText(text)) state = 3;
        _app.Dispatch(UiInputEvent.FromTextComposition(new TextCompositionEvent(
            Header(TextDevice, timestamp), text, (TextCompositionState)state, 0, 0)));
        if (state == 2 && text.Length > 0) { _modified = true; _revision++; }
        if (state == 3 && _compositionStartedEdit) _app.CellControl.CancelEdit();
    });

    internal static void CancelPointer(double timestamp) => _dispatcher?.Post(() => {
        if (_app is null) return;
        var outside = InputPoint.ClientDeviceIndependentPixels(-1, -1);
        _app.Dispatch(UiInputEvent.FromMouseMove(new MouseMoveEvent(Header(PointerDevice, timestamp), outside, _buttons)));
        for (int button = 0; button < 5; button++) {
            if ((_buttons & (MouseButtons)(button == 1 ? 4 : button == 2 ? 2 : 1 << button)) == 0) continue;
            _app.Dispatch(UiInputEvent.FromMouseButton(new MouseButtonEvent(Header(PointerDevice, timestamp), outside, MouseButtons.None, MapButton(button), MouseButtonTransition.Up)));
        }
        _buttons = MouseButtons.None;
        if (_app.Session.CapturedElement is UiElement captured) _app.Session.ReleaseInputCapture(captured);
    });

    internal static void Dispose()
    {
        if (_observedWorkbook is not null) _observedWorkbook.Changed -= SheetChanged;
        if (_observedSheet is not null) _observedSheet.Changed -= SheetChanged;
        _observedSheet = null;
        _observedWorkbook = null;
        _dispatcher = null;
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

    [JSExport] internal static void UiTextComposition(string text, int state, double timestamp) => BrowserCellsApp.TextComposition(text, state, timestamp);
    [JSExport] internal static void UiCancelPointer(double timestamp) => BrowserCellsApp.CancelPointer(timestamp);
    [JSExport] internal static void CompleteAction(string action) => BrowserCellsApp.CompleteAction(action);
    [JSExport] internal static string PrepareExport(string name) => BrowserCellsApp.PrepareExport(name);
    [JSExport] internal static void ExportCompleted(string outcome, string message) => BrowserCellsApp.ExportCompleted(outcome, message);
    [JSExport] internal static void FileMessage(string outcome, string message) => BrowserInterop.ReportOperation(outcome, message);
    [JSExport] internal static string RecoverySnapshot() => BrowserCellsApp.RecoverySnapshot();
    [JSExport] internal static bool RestoreRecovery(string name, string payload) => BrowserCellsApp.RestoreRecovery(name, payload);
    [JSExport] internal static string SelectedCellText() => BrowserCellsApp.SelectedCellText();
    [JSExport] internal static string SheetSnapshot() => BrowserCellsApp.SheetSnapshot();

    [JSExport]
    internal static void Dispose() => BrowserCellsApp.Dispose();
}

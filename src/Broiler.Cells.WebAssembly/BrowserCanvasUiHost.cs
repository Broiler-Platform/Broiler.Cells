using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Resources;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.WebAssembly;
using Broiler.UI;

namespace Broiler.Cells.WebAssembly;

[SupportedOSPlatform("browser")]
internal sealed class BrowserUiDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = [];
    private readonly Action _schedule;

    internal BrowserUiDispatcher(Action schedule) => _schedule = schedule;

    public bool CheckAccess() => true;

    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _queue.Enqueue(callback);
        _schedule();
    }

    internal int Drain()
    {
        int count = 0;
        while (_queue.TryDequeue(out Action? callback))
        {
            callback();
            count++;
        }
        return count;
    }
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserCanvasUiHost :
    IUiHost, IUiTextInputHost, IUiClipboardHost, IUiCursorHost, IUiImageHost, IDisposable
{
    private readonly BrowserCanvasRenderer _renderer;
    private UiTextCaretInfo? _caret;
    private bool _clipboardEventActive;
    private string? _clipboardReadText;
    private string? _clipboardWriteText;
    private bool _disposed;

    internal BrowserCanvasUiHost(BrowserCanvasRenderer renderer)
    {
        _renderer = renderer;
    }

    public BSize ViewportSize { get; private set; } = new(1000, 600);
    public double Scale { get; private set; } = 1;
    public BColor ClearColor { get; set; } = BColor.White;
    internal UiTextCaretInfo? CurrentCaret => _caret;
    internal bool IsClipboardEventActive => _clipboardEventActive;
    internal long FrameIndex => _renderer.FrameIndex;

    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        try
        {
            using BBitmap bitmap = BBitmap.Decode(encodedImage);
            var result = _renderer.CreateImage(bitmap.ToPixelBuffer());
            return result;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return BImageHandle.Invalid;
        }
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        try
        {
            var result = _renderer.CreateImage(pixels);
            return result;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return BImageHandle.Invalid;
        }
    }

    public void ReleaseImage(BImageHandle image)
    {
        _renderer.ReleaseImage(image);
    }

    public void Invalidate(UiInvalidation invalidation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BrowserInterop.ScheduleFrame();
    }

    public void Present(BRenderList renderList)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        int backingWidth = Math.Max(1, (int)Math.Ceiling(ViewportSize.Width * Scale));
        int backingHeight = Math.Max(1, (int)Math.Ceiling(ViewportSize.Height * Scale));
        var result = _renderer.PresentFrame(
            renderList,
            backingWidth,
            backingHeight,
            Scale,
            ViewportSize.Width,
            ViewportSize.Height,
            ClearColor);

    }

    public void PublishCaret(UiTextCaretInfo caret) => _caret = caret;

    public void ClearCaret(UiElement owner)
    {
        if (owner is null || ReferenceEquals(_caret?.Owner, owner))
            _caret = null;
    }

    public bool TryGetText(out string text)
    {
        text = _clipboardEventActive ? _clipboardReadText ?? string.Empty : string.Empty;
        return _clipboardEventActive && _clipboardReadText is not null;
    }

    public void SetText(string text)
    {
        if (_clipboardEventActive)
            _clipboardWriteText = text ?? string.Empty;
    }

    public void SetCursor(UiCursorShape shape) => BrowserInterop.SetCursor(shape.ToString());

    internal void BeginClipboardEvent(string? readText)
    {
        _clipboardEventActive = true;
        _clipboardReadText = readText;
        _clipboardWriteText = null;
    }

    internal string EndClipboardEvent()
    {
        string result = _clipboardWriteText ?? string.Empty;
        _clipboardEventActive = false;
        _clipboardReadText = null;
        _clipboardWriteText = null;
        return result;
    }

    internal bool Resize(double width, double height, double dpr)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(dpr) ||
            width <= 0 || height <= 0 || dpr <= 0)
            return false;

        if (ViewportSize.Width.Equals(width) && ViewportSize.Height.Equals(height) && Scale.Equals(dpr))
            return true;

        ViewportSize = new BSize(width, height);
        Scale = dpr;
        BrowserInterop.ScheduleFrame();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _caret = null;
        _clipboardEventActive = false;
        _clipboardReadText = null;
        _clipboardWriteText = null;
    }
}

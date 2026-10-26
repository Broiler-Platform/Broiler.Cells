using System;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.UI;

namespace Broiler.Cells.UI;

/// <summary>
/// Platform-neutral <see cref="IUiHost"/> for Broiler Cells, supporting rendering,
/// clipboard text access, text input carets, and image handles.
/// </summary>
public class CellsUiHost : IUiHost, IUiClipboardHost, IUiTextInputHost, IUiImageHost, IDisposable
{
    private readonly Func<BSize> _getViewportSize;
    private readonly Func<double> _getScale;
    private readonly Action _invalidate;
    private readonly Action<BRenderList> _present;
    private readonly Func<string?>? _getClipboardText;
    private readonly Action<string>? _setClipboardText;
    private readonly Action<UiTextCaretInfo?>? _caretChanged;
    private readonly Func<IBroilerRenderer?>? _getRenderer;
    private readonly Func<ReadOnlySpan<byte>, BImageHandle>? _createImageBytes;
    private readonly Func<BPixelBuffer, BImageHandle>? _createImagePixels;
    private readonly Action<BImageHandle>? _releaseImage;

    public CellsUiHost(
        Func<BSize> getViewportSize,
        Func<double> getScale,
        Action invalidate,
        Action<BRenderList> present,
        Func<string?>? getClipboardText = null,
        Action<string>? setClipboardText = null,
        Action<UiTextCaretInfo?>? caretChanged = null,
        Func<IBroilerRenderer?>? getRenderer = null,
        Func<ReadOnlySpan<byte>, BImageHandle>? createImage = null,
        Func<BPixelBuffer, BImageHandle>? createImagePixels = null,
        Action<BImageHandle>? releaseImage = null)
    {
        _getViewportSize = getViewportSize ?? throw new ArgumentNullException(nameof(getViewportSize));
        _getScale = getScale ?? throw new ArgumentNullException(nameof(getScale));
        _invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
        _present = present ?? throw new ArgumentNullException(nameof(present));
        _getClipboardText = getClipboardText;
        _setClipboardText = setClipboardText;
        _caretChanged = caretChanged;
        _getRenderer = getRenderer;
        _createImageBytes = createImage;
        _createImagePixels = createImagePixels;
        _releaseImage = releaseImage;
    }

    public BSize ViewportSize => _getViewportSize();
    public double Scale => _getScale();
    public bool IsInvalidated { get; private set; } = true;
    public BRenderList? LastRenderList { get; private set; }
    public UiTextCaretInfo? LastCaret { get; private set; }

    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);

    public void RequestInvalidate()
    {
        IsInvalidated = true;
        _invalidate();
    }

    public void Invalidate(UiInvalidation invalidation) => RequestInvalidate();

    public void Present(BRenderList renderList)
    {
        _present(renderList);
        LastRenderList = renderList;
        IsInvalidated = false;
    }

    public bool TryGetText(out string text)
    {
        text = _getClipboardText?.Invoke() ?? string.Empty;
        return text.Length > 0;
    }

    public void SetText(string text) => _setClipboardText?.Invoke(text ?? string.Empty);

    public void PublishCaret(UiTextCaretInfo caret)
    {
        LastCaret = caret;
        _caretChanged?.Invoke(caret);
    }

    public void ClearCaret(UiElement owner)
    {
        if (LastCaret?.Owner == owner)
        {
            LastCaret = null;
            _caretChanged?.Invoke(null);
        }
    }

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        if (encodedImage.IsEmpty)
            return BImageHandle.Invalid;

        if (_createImageBytes is not null)
            return _createImageBytes(encodedImage);

        IBroilerRenderer? renderer = _getRenderer?.Invoke();
        if (renderer is null)
            return BImageHandle.Invalid;

        try
        {
            return renderer.CreateImage(encodedImage);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return BImageHandle.Invalid;
        }
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        if (_createImagePixels is not null)
            return _createImagePixels(pixels);

        IBroilerRenderer? renderer = _getRenderer?.Invoke();
        if (renderer is null)
            return BImageHandle.Invalid;

        try
        {
            return renderer.CreateImage(pixels);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return BImageHandle.Invalid;
        }
    }

    public void ReleaseImage(BImageHandle image)
    {
        if (!image.IsValid)
            return;

        if (_releaseImage is not null)
        {
            _releaseImage(image);
            return;
        }

        _getRenderer?.Invoke()?.ReleaseImage(image);
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

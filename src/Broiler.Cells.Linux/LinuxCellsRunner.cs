using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Cells.UI;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Linux;
using Broiler.Graphics.Linux.OpenGL;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Hosting.Linux;

namespace Broiler.Cells.Linux;

internal static class LinuxCellsRunner
{
    private static readonly TimeSpan InteractivePollInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan OffscreenPollInterval = TimeSpan.FromMilliseconds(1);
    private static readonly BRenderOptions RenderOptions = new(Antialias: true, VSync: true, SubpixelText: true);

    public static async Task<int> RunAsync(LinuxCellsOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        Console.WriteLine("Broiler Cells");
        Console.WriteLine();

        bool closeRequested = false;
        long frameIndex = 0;
        BFrameContext lastFrameContext = BFrameContext.Default;

        using LinuxOpenGlRenderer renderer = new();
        BSurfaceDescriptor descriptor = BSurfaceDescriptor.Default(new BSize(options.Width, options.Height));
        using IBroilerSurface surface = options.OpenWindow
            ? renderer.CreateX11WindowSurface(descriptor, "Broiler Cells")
            : renderer.CreateSurface(descriptor);

        LinuxOpenGlX11WindowSurface? x11Window = surface as LinuxOpenGlX11WindowSurface;
        bool canUseEvdev = options.EnableEvdevInput && x11Window is not null;

        using LinuxX11Clipboard? clipboard = x11Window is not null ? LinuxX11Clipboard.TryOpen() : null;

        using CellsUiHost host = new(
            () => surface.Size,
            () => surface.DpiScale,
            static () => { },
            renderList =>
            {
                lastFrameContext = new BFrameContext(CellsPalette.Canvas, frameIndex++, RenderOptions);
                renderer.Render(surface, renderList, lastFrameContext);
            },
            () => clipboard is not null && clipboard.TryGetText(out string text) ? text : null,
            text => clipboard?.SetText(text),
            getRenderer: () => renderer);

        using CellsApp app = new(
            host,
            () => closeRequested = true,
            setWindowTitle: x11Window is null ? null : x11Window.SetTitle);

        await using LinuxInputCoordinator input = new(canUseEvdev, Console.WriteLine, externalPointer: x11Window is not null, applicationName: "Broiler Cells");
        await input.InitializeAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset start = DateTimeOffset.UtcNow;
        using PeriodicTimer timer = new(options.OpenWindow ? InteractivePollInterval : OffscreenPollInterval);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool processedWindowEvents = false;

            clipboard?.ProcessPendingEvents();
            if (x11Window is not null)
            {
                processedWindowEvents = x11Window.ProcessPendingEvents();
                bool inputActive = options.IgnoreInputFocus || x11Window.IsFocused;
                await input.SetActiveAsync(inputActive, cancellationToken).ConfigureAwait(false);
            }

            input.SetViewport(host.ViewportSize);
            if (x11Window is not null && x11Window.TryGetPointerPosition(out int pointerX, out int pointerY, out _))
            {
                double scale = host.Scale <= 0 ? 1.0 : host.Scale;
                input.SetExternalPointer(pointerX / scale, pointerY / scale);
            }

            input.Drain(evt => app.Dispatch(evt));

            if (host.IsInvalidated || processedWindowEvents)
            {
                app.RenderFrame();
            }

            if (!options.OpenWindow)
                break;

            if (!options.RunUntilClose &&
                (DateTimeOffset.UtcNow - start).TotalMilliseconds >= options.DurationMilliseconds)
            {
                break;
            }
        }
        while (!closeRequested &&
               (x11Window is null || !x11Window.IsCloseRequested) &&
               await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));

        await input.SetActiveAsync(false, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.UI.AboutDialog.Standard;
using Broiler.UI.Window;

namespace Broiler.Cells.UI;

/// <summary>
/// About dialog composition shared by desktop, mobile and browser Cells.
/// </summary>
public static class CellsAbout
{
    private static readonly Lazy<BImageResource?> Artwork = new(() =>
    {
        try
        {
            using Stream? stream = typeof(CellsAbout).Assembly.GetManifestResourceStream("Broiler.Cells.UI.About.png");
            if (stream is null)
                return null;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return BImageResource.FromEncoded(bytes.ToArray(), "image/png");
        }
        catch
        {
            return null;
        }
    });

    public static StandardAboutDialog? Show(UiWindow owner, BSize viewport, Assembly productAssembly, Action<Uri>? openUrl = null)
    {
        Console.WriteLine($"[Cells-WASM] CellsAbout.Show: owner={owner?.GetType().Name}, viewport={viewport.Width}x{viewport.Height}, assembly={productAssembly?.FullName}");
        if (owner == null || owner.IsDisposed || owner.IsClosed)
        {
            Console.WriteLine("[Cells-WASM] CellsAbout.Show: owner is null, disposed, or closed. Aborting.");
            return null;
        }

        var sw = Stopwatch.StartNew();
        Console.WriteLine("[Cells-WASM] CellsAbout.Show: creating StandardAboutDialog...");
        var dialog = new StandardAboutDialog();
        Console.WriteLine($"[Cells-WASM] CellsAbout.Show: StandardAboutDialog created in {sw.ElapsedMilliseconds}ms.");

        sw.Restart();
        ArgumentNullException.ThrowIfNull(productAssembly);
        var components = new[] { productAssembly, typeof(CellsAbout).Assembly, typeof(StandardAboutDialog).Assembly };
        Console.WriteLine($"[Cells-WASM] CellsAbout.Show: calling PopulateFromAssemblies with {components.Length} explicit components...");
        dialog.PopulateFromAssemblies(productAssembly, components!);
        Console.WriteLine($"[Cells-WASM] CellsAbout.Show: PopulateFromAssemblies finished in {sw.ElapsedMilliseconds}ms. Product={dialog.ProductName}, Version={dialog.ProductVersion}");

        dialog.ProductName = "Broiler Cells";
        dialog.Title = "About Broiler Cells";
        if (OperatingSystem.IsBrowser())
        {
            // On WebAssembly, the owner-drawn window chrome close button ('x') uses a rotated rect.
            // CanvasFramePlanner forces whole-frame CPU fallback on rotated rects, which runs the
            // managed software rasterizer for up to ~98 seconds on high-resolution canvases.
            // Suppressing the title-bar close button (the dialog is dismissed via OK or Escape)
            // avoids the rotation transform and keeps direct GPU Canvas 2D rendering at 60 FPS.
            dialog.CanClose = false;
        }

        sw.Restart();
        Console.WriteLine("[Cells-WASM] CellsAbout.Show: evaluating Artwork.Value...");
        try
        {
            if (Artwork.Value is { } picture)
            {
                Console.WriteLine($"[Cells-WASM] CellsAbout.Show: Artwork loaded in {sw.ElapsedMilliseconds}ms (size={picture.PixelWidth}x{picture.PixelHeight}).");
                dialog.Picture = picture;
                dialog.PictureAltText = "Broiler Cells logo";
            }
            else
            {
                Console.WriteLine($"[Cells-WASM] CellsAbout.Show: Artwork is null ({sw.ElapsedMilliseconds}ms).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cells-WASM] CellsAbout.Show: Artwork loading error: {ex.Message}");
        }

        dialog.PreferredSize = new BSize(720, 380);
        dialog.ProductUrl = new Uri("https://broiler-platform.github.io/");
        dialog.ProductUrlRequested += (_, url) =>
        {
            Console.WriteLine($"[Cells-WASM] CellsAbout: ProductUrlRequested: {url}");
            try
            {
                (openUrl ?? OpenDesktopUrl)(url);
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                Console.WriteLine($"[Cells-WASM] CellsAbout: Error opening url: {error.Message}");
                dialog.WebsiteButton.Text = "Unable to open website";
            }
        };

        double width = Math.Min(dialog.PreferredSize.Width, Math.Max(0, viewport.Width - 32));
        double height = Math.Min(dialog.PreferredSize.Height, Math.Max(0, viewport.Height - 32));
        var placement = new BRect(
            Math.Max(0, (viewport.Width - width) / 2),
            Math.Max(0, (viewport.Height - height) / 2), width, height);

        Console.WriteLine($"[Cells-WASM] CellsAbout.Show: calling ShowModal with placement={placement}...");
        sw.Restart();
        _ = dialog.ShowModal(owner, placement);
        Console.WriteLine($"[Cells-WASM] CellsAbout.Show: ShowModal returned in {sw.ElapsedMilliseconds}ms. Dialog IsModal={dialog.IsModal}, IsPresented={dialog.IsPresented}.");

        return dialog;
    }

    private static void OpenDesktopUrl(Uri url)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new NotSupportedException("This host must supply website navigation.");
        using Process? process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
}

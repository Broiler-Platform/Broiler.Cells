using System.Diagnostics;
using System;
using System.ComponentModel;
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
        if (owner == null || owner.IsDisposed || owner.IsClosed)
        {
            return null;
        }

        var dialog = new StandardAboutDialog();

        ArgumentNullException.ThrowIfNull(productAssembly);
        var components = new[] { productAssembly, typeof(CellsAbout).Assembly, typeof(StandardAboutDialog).Assembly };
        dialog.PopulateFromAssemblies(productAssembly, components!);

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

        try
        {
            if (Artwork.Value is { } picture)
            {
                dialog.Picture = picture;
                dialog.PictureAltText = "Broiler Cells logo";
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            dialog.PictureAltText = "Broiler Cells logo unavailable";
        }

        dialog.PreferredSize = new BSize(720, 380);
        dialog.ProductUrl = new Uri("https://broiler-platform.github.io/");
        dialog.ProductUrlRequested += (_, url) =>
        {
            try
            {
                (openUrl ?? OpenDesktopUrl)(url);
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                dialog.WebsiteButton.Text = "Unable to open website";
            }
        };

        double width = Math.Min(dialog.PreferredSize.Width, Math.Max(0, viewport.Width - 32));
        double height = Math.Min(dialog.PreferredSize.Height, Math.Max(0, viewport.Height - 32));
        var placement = new BRect(
            Math.Max(0, (viewport.Width - width) / 2),
            Math.Max(0, (viewport.Height - height) / 2), width, height);

        _ = dialog.ShowModal(owner, placement);

        return dialog;
    }

    private static void OpenDesktopUrl(Uri url)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new NotSupportedException("This host must supply website navigation.");
        using Process? process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
}

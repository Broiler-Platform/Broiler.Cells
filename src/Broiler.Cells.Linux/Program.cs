using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Cells.Linux;

/// <summary>Linux entry point for Broiler Cells.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Broiler.Graphics.Imaging.BImageCodecs.Use(
            new Broiler.Media.MediaCodecCatalog(Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));
        Broiler.Graphics.Text.BSystemFonts.InstallFontFileResolver();

        LinuxCellsOptions options = LinuxCellsOptions.Parse(args);
        if (options.ShowHelp)
        {
            LinuxCellsOptions.PrintHelp();
            return 0;
        }

        if (options.OpenWindow && !OperatingSystem.IsLinux())
        {
            Console.WriteLine("Windowed Broiler Cells uses the Linux X11/OpenGL backend; running offscreen on this OS.");
            options = options with { OpenWindow = false, EnableEvdevInput = false, RunUntilClose = false };
        }

        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            return await LinuxCellsRunner.RunAsync(options, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

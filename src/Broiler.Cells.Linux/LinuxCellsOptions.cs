using System;
using System.Globalization;

namespace Broiler.Cells.Linux;

/// <summary>Command-line and windowing options for the Linux Cells host.</summary>
internal sealed record LinuxCellsOptions(
    bool OpenWindow,
    bool EnableEvdevInput,
    bool RunUntilClose,
    int DurationMilliseconds,
    int Width,
    int Height,
    string? ArtifactDirectory,
    bool IgnoreInputFocus,
    bool ShowHelp)
{
    public static LinuxCellsOptions Parse(string[] args)
    {
        bool openWindow = OperatingSystem.IsLinux();
        bool enableEvdevInput = openWindow;
        bool runUntilClose = false;
        bool ignoreInputFocus = false;
        bool showHelp = false;
        int? durationMilliseconds = null;
        int width = 1000;
        int height = 560;
        string? artifactDirectory = null;

        foreach (string arg in args)
        {
            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else if (arg.Equals("--window", StringComparison.OrdinalIgnoreCase))
            {
                openWindow = true;
                enableEvdevInput = true;
            }
            else if (arg.Equals("--offscreen", StringComparison.OrdinalIgnoreCase))
            {
                openWindow = false;
                enableEvdevInput = false;
                runUntilClose = false;
            }
            else if (arg.Equals("--enable-evdev-input", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--input", StringComparison.OrdinalIgnoreCase))
            {
                enableEvdevInput = true;
            }
            else if (arg.Equals("--no-input", StringComparison.OrdinalIgnoreCase))
            {
                enableEvdevInput = false;
            }
            else if (arg.Equals("--interactive", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--until-close", StringComparison.OrdinalIgnoreCase))
            {
                runUntilClose = true;
            }
            else if (arg.Equals("--input-ignore-focus", StringComparison.OrdinalIgnoreCase))
            {
                ignoreInputFocus = true;
            }
            else if (arg.StartsWith("--duration-ms=", StringComparison.OrdinalIgnoreCase))
            {
                string value = arg["--duration-ms=".Length..];
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    durationMilliseconds = parsed;
            }
            else if (arg.StartsWith("--artifact-dir=", StringComparison.OrdinalIgnoreCase))
            {
                artifactDirectory = arg["--artifact-dir=".Length..];
            }
            else if (arg.StartsWith("--size=", StringComparison.OrdinalIgnoreCase))
            {
                ParseSize(arg["--size=".Length..], ref width, ref height);
            }
            else if (arg.StartsWith("--width=", StringComparison.OrdinalIgnoreCase))
            {
                string value = arg["--width=".Length..];
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    width = parsed;
            }
            else if (arg.StartsWith("--height=", StringComparison.OrdinalIgnoreCase))
            {
                string value = arg["--height=".Length..];
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    height = parsed;
            }
        }

        if (openWindow && !runUntilClose && !durationMilliseconds.HasValue)
            runUntilClose = true;

        int defaultDuration = openWindow ? 10_000 : 1_000;
        return new LinuxCellsOptions(
            openWindow,
            openWindow && enableEvdevInput,
            runUntilClose,
            durationMilliseconds ?? defaultDuration,
            width,
            height,
            artifactDirectory,
            ignoreInputFocus,
            showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Broiler Cells (Linux)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --window                  Run in an X11 window (default on Linux)");
        Console.WriteLine("  --offscreen               Run headless without an X11 display");
        Console.WriteLine("  --input, --no-input       Enable or disable evdev hardware input");
        Console.WriteLine("  --until-close             Keep running until closed or Ctrl+C");
        Console.WriteLine("  --duration-ms=<ms>        Time to run before exiting");
        Console.WriteLine("  --size=<W>x<H>            Initial window/viewport size");
        Console.WriteLine("  --width=<px>              Initial width in pixels");
        Console.WriteLine("  --height=<px>             Initial height in pixels");
        Console.WriteLine("  --help, -h                Show this help");
    }

    private static void ParseSize(string value, ref int width, ref int height)
    {
        int index = value.IndexOf('x');
        if (index <= 0 || index >= value.Length - 1)
            return;

        if (int.TryParse(value[..index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w > 0 &&
            int.TryParse(value[(index + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && h > 0)
        {
            width = w;
            height = h;
        }
    }
}

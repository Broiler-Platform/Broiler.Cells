using System.Runtime.InteropServices.JavaScript;

namespace Broiler.Cells.WebAssembly;

/// <summary>
/// Managed-to-page bridge for the browser Cells application.
/// </summary>
internal static partial class BrowserInterop
{
    [JSImport("cells.openUrl", "main.js")]
    internal static partial void OpenUrl(string url);

    [JSImport("cells.scheduleFrame", "main.js")]
    internal static partial void ScheduleFrame();

    [JSImport("cells.setTitle", "main.js")]
    internal static partial void SetTitle(string title);

    [JSImport("cells.setCursor", "main.js")]
    internal static partial void SetCursor(string cursor);

    [JSImport("cells.ready", "main.js")]
    internal static partial void Ready(double logicalWidth, double logicalHeight);

    [JSImport("cells.failed", "main.js")]
    internal static partial void Failed(string exceptionType, string details);

    [JSImport("cells.publishFrame", "main.js")]
    internal static partial void PublishFrame(
        double frameIndex,
        bool caretActive,
        double caretX,
        double caretY,
        double caretWidth,
        double caretHeight,
        int caretIndex,
        int selectionStart,
        int selectionLength,
        bool focusedIsText,
        string statusText,
        bool darkTheme);

    [JSImport("cells.prefersDarkScheme", "main.js")]
    internal static partial bool PrefersDarkScheme();

    [JSImport("cells.prefersReducedMotion", "main.js")]
    internal static partial bool PrefersReducedMotion();

    [JSImport("cells.requestOpenFile", "main.js")]
    internal static partial void RequestOpenFile(string acceptExtensions);

    [JSImport("cells.downloadFile", "main.js")]
    internal static partial void DownloadFile(string fileName, string base64Data);

    [JSImport("cells.promptFileName", "main.js")]
    internal static partial string PromptFileName(string defaultName);
}

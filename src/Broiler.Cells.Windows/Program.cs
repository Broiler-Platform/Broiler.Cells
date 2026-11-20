using System.Runtime.InteropServices;
using Broiler.Graphics.Imaging;
using Broiler.Media;
using Broiler.Media.Image.Managed;

namespace Broiler.Cells.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        _ = SetProcessDpiAwarenessContext(new nint(-4));
        BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs()));
        try
        {
            using var window = new CellsWindow();
            return window.Run();
        }
        catch (Exception error)
        {
            _ = MessageBox(0, error.ToString(), "Broiler Cells", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);
}

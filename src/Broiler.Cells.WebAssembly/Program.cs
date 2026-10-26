using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Cells.WebAssembly;

internal static class Program
{
    public static async Task Main()
    {
        try
        {
            await BrowserCellsApp.StartAsync();
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            BrowserInterop.Failed(exception.GetType().Name, exception.ToString());
        }

        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}

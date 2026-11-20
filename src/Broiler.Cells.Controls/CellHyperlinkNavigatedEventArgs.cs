using Broiler.Documents.Model;

namespace Broiler.Cells.Controls;

/// <summary>Provides data for the cell hyperlink navigated event.</summary>
public sealed class CellHyperlinkNavigatedEventArgs(CellHyperlink hyperlink, bool isHandled = false) : EventArgs
{
    public CellHyperlink Hyperlink { get; } = hyperlink;
    public bool IsHandled { get; set; } = isHandled;
}

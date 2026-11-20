using System;
using Broiler.Documents.Model;
using Broiler.Graphics.Paging;

namespace Broiler.Cells.Core;

/// <summary>Standard margin presets for spreadsheet printing.</summary>
public enum PageMarginsPreset
{
    Normal,
    Wide,
    Narrow,
    Custom,
}

/// <summary>
/// Page geometry and print options for a worksheet.
/// </summary>
public sealed record WorksheetPrintSetup
{
    public BPaperSize PaperSize { get; init; } = BPaperSize.A4;
    public BPageOrientation Orientation { get; init; } = BPageOrientation.Portrait;
    public PageMarginsPreset MarginsPreset { get; init; } = PageMarginsPreset.Normal;
    public BPageMargins Margins { get; init; } = NormalMargins;
    public bool PrintGridlines { get; init; } = true;
    public bool PrintHeadings { get; init; } = false;
    public int? FitToPagesWide { get; init; }
    public int? FitToPagesTall { get; init; }
    public double Scale { get; init; } = 1.0;
    public string? HeaderLeft { get; init; }
    public string? HeaderCenter { get; init; }
    public string? HeaderRight { get; init; }
    public string? FooterLeft { get; init; }
    public string? FooterCenter { get; init; } = "&[Page] of &[Pages]";
    public string? FooterRight { get; init; }

    public static BPageMargins NormalMargins { get; } = new(50.4, 54.0, 50.4, 54.0);
    public static BPageMargins WideMargins { get; } = new(72.0, 72.0, 72.0, 72.0);
    public static BPageMargins NarrowMargins { get; } = new(18.0, 18.0, 18.0, 18.0);

    public static WorksheetPrintSetup Default { get; } = new();

    public WorksheetPrintSetup WithPreset(PageMarginsPreset preset) => preset switch
    {
        PageMarginsPreset.Normal => this with { MarginsPreset = preset, Margins = NormalMargins },
        PageMarginsPreset.Wide => this with { MarginsPreset = preset, Margins = WideMargins },
        PageMarginsPreset.Narrow => this with { MarginsPreset = preset, Margins = NarrowMargins },
        _ => this with { MarginsPreset = PageMarginsPreset.Custom },
    };

    public BPageSetup ToPageSetup() => new(PaperSize, Orientation, Margins);

    public PageGeometry ToPageGeometry()
    {
        var orientedSize = PaperSize.Oriented(Orientation);
        return new PageGeometry(
            Width: orientedSize.Width,
            Height: orientedSize.Height,
            MarginLeft: Margins.Left,
            MarginRight: Margins.Right,
            MarginTop: Margins.Top,
            MarginBottom: Margins.Bottom,
            HeaderDistance: Math.Max(0, Margins.Top - 18),
            FooterDistance: Math.Max(0, Margins.Bottom - 18));
    }
}

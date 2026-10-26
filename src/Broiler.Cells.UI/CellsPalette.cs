using Broiler.Graphics.Color;

namespace Broiler.Cells.UI;

/// <summary>
/// Color constants matching the Broiler visual identity for spreadsheet surfaces,
/// toolbar buttons, edit line, and borders.
/// </summary>
public static class CellsPalette
{
    public static readonly BColor Canvas = BColor.FromArgb(255, 231, 235, 240);
    public static readonly BColor Surface = BColor.FromArgb(255, 255, 255, 255);
    public static readonly BColor Title = BColor.FromArgb(255, 30, 42, 54);
    public static readonly BColor Muted = BColor.FromArgb(255, 95, 110, 125);
    public static readonly BColor Accent = BColor.FromArgb(255, 42, 115, 197);
    public static readonly BColor AccentLight = BColor.FromArgb(255, 220, 235, 252);
    public static readonly BColor WindowBorder = BColor.FromArgb(255, 204, 210, 216);
    public static readonly BColor MenuRule = BColor.FromArgb(255, 218, 224, 230);
    public static readonly BColor MenuPopup = BColor.FromArgb(255, 255, 255, 255);
    public static readonly BColor MenuSelected = BColor.FromArgb(255, 235, 240, 246);
    public static readonly BColor ToolbarSurface = BColor.FromArgb(255, 238, 241, 245);
    public static readonly BColor ToolbarButton = BColor.FromArgb(255, 242, 244, 247);
    public static readonly BColor ToolbarButtonHover = BColor.FromArgb(255, 226, 232, 240);
    public static readonly BColor ToolbarButtonActive = BColor.FromArgb(255, 214, 224, 236);
    public static readonly BColor ToolbarButtonBorder = BColor.FromArgb(255, 204, 212, 220);
    public static readonly BColor EditLineBackground = BColor.FromArgb(255, 245, 247, 250);
}

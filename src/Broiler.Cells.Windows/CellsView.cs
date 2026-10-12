using Broiler.Cells.Controls;
using Broiler.Cells.Core;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Label.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Cells.Windows;

internal sealed class CellsView : UiElement
{
    internal static readonly BColor Canvas = BColor.FromArgb(255, 231, 235, 240);
    internal static readonly StandardThemeTokens Theme = StandardThemeTokens.Light with
    {
        Surface = BColor.FromArgb(255, 255, 255, 255),
        Text = BColor.FromArgb(255, 30, 42, 54),
        TextMuted = BColor.FromArgb(255, 95, 110, 125),
        Border = BColor.FromArgb(255, 204, 210, 216),
        Accent = BColor.FromArgb(255, 42, 115, 197),
        FocusRing = BColor.FromArgb(255, 42, 115, 197),
        FontBody = new BFontStyle("Segoe UI", 14),
    };

    private readonly StandardScrollView _scroll;
    private readonly StandardLabel _status;

    public CellsView(SheetModel sheet)
    {
        AccessibleName = "Broiler Cells";
        _scroll = new StandardScrollView
        {
            Background = Theme.Surface,
            FocusWhenScrollable = true,
            AccessibleName = "Sheet viewport",
        };
        _scroll.AddChild(new CellGrid(sheet));
        _status = new StandardLabel("Read only", StandardLabelRole.Muted) { Font = new BFontStyle("Segoe UI", 12) };
        AddChild(_scroll);
        AddChild(_status);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        _scroll.Measure(new BSize(Math.Max(0, availableSize.Width - 24), Math.Max(0, availableSize.Height - 54)));
        _status.Measure(new BSize(Math.Max(0, availableSize.Width - 24), 20));
        return availableSize;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double width = Math.Max(0, finalRect.Width - 24);
        _scroll.Arrange(new BRect(finalRect.X + 12, finalRect.Y + 12, width, Math.Max(0, finalRect.Height - 54)));
        _status.Arrange(new BRect(finalRect.X + 12, finalRect.Y + Math.Max(0, finalRect.Height - 28), width, 20));
    }

    protected override void RenderCore(UiRenderContext context)
    {
        context.RenderList.PushClip(Bounds);
        context.RenderList.FillRect(Bounds, Canvas);
        base.RenderCore(context);
        context.RenderList.PopClip();
    }
}

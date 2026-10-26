using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;

namespace Broiler.Cells.UI;

/// <summary>
/// Vector toolbar icons for Broiler Cells drawn resolution-independently.
/// </summary>
public static class CellsIcons
{
    public const double DesignExtent = 16;
    private const double Stroke = 1.4;

    public static void NewDocument(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        pen.Bar(3, 1.5, 6, Stroke);
        pen.Bar(3, 13.1, 9, Stroke);
        pen.Column(3, 1.5, 13, Stroke);
        pen.Column(10.6, 5.2, 9.3, Stroke);
        pen.Triangle(9, 1.5, 12, 4.5, 9, 4.5);
    }

    public static void OpenDocument(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        pen.Rect(1.5, 2.6, 6.2, 2.2);
        pen.Rect(1.5, 5.2, 13, 8.2);
    }

    public static void Save(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        pen.Outline(2, 2, 12, 12, Stroke);
        pen.Rect(5.5, 3.4, 5, 3.4);
        pen.Rect(4.6, 8.8, 6.8, 3.2);
    }

    public static void SaveAs(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        pen.Outline(0.8, 1, 9, 9, Stroke);
        pen.Rect(3.4, 2, 3.8, 2.4);
        pen.Rect(2.8, 6, 5, 2.2);
        pen.Bar(10.2, 12.6, 5.6, Stroke);
        pen.Column(12.3, 9.8, 5.6, Stroke);
    }

    public static void Cut(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        // Scissors handles
        pen.Circle(2, 9, 4, 1.2);
        pen.Circle(10, 9, 4, 1.2);
        // Blades crossing
        pen.Line(3.5, 9, 12.5, 2.5, 1.3);
        pen.Line(12.5, 9, 3.5, 2.5, 1.3);
    }

    public static void Copy(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        // Back page
        pen.Outline(2, 1.5, 8.5, 10, Stroke);
        // Front page
        pen.Rect(5, 4.5, 9, 10);
        var inner = new Pen(list, box, BColor.FromArgb(255, 255, 255, 255));
        inner.Rect(5.5, 5, 8, 9);
        var pen2 = new Pen(list, box, color);
        pen2.Outline(5, 4.5, 9, 10, Stroke);
    }

    public static void Paste(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        // Clipboard backboard
        pen.Outline(2, 3, 12, 11.5, Stroke);
        // Clip at top
        pen.RoundedRect(5.5, 1.5, 5, 3, 1);
        // Document sheet on clipboard
        pen.Rect(4.5, 5.5, 7, 7.5);
    }

    public static void Clear(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        // Diagonal cross
        pen.Line(3.5, 3.5, 12.5, 12.5, 1.8);
        pen.Line(12.5, 3.5, 3.5, 12.5, 1.8);
    }

    public static void AutoFit(BRenderList list, BRect box, BColor color)
    {
        var pen = new Pen(list, box, color);
        // Left bar
        pen.Column(2.5, 2, 12, 1.4);
        // Right bar
        pen.Column(13.5, 2, 12, 1.4);
        // Horizontal arrows pointing outwards
        pen.Bar(4.5, 8, 7, 1.4);
        pen.Triangle(6, 6, 6, 10, 4, 8);
        pen.Triangle(10, 6, 10, 10, 12, 8);
    }

    public static void AppMark(BRenderList list, BRect box, BColor page, BColor tile, BColor ink)
    {
        var backdrop = new Pen(list, box, tile);
        backdrop.RoundedRect(0, 0, 16, 16, 3.4);

        var sheet = new Pen(list, box, page);
        sheet.Rect(2.5, 2.5, 11, 11);

        var grid = new Pen(list, box, ink);
        grid.Outline(2.5, 2.5, 11, 11, 1.2);
        grid.Bar(2.5, 6.2, 11, 1.0);
        grid.Bar(2.5, 9.8, 11, 1.0);
        grid.Column(6.2, 2.5, 11, 1.0);
        grid.Column(9.8, 2.5, 11, 1.0);
    }

    public static BPixelBuffer RenderAppIcon(int size, BColor page, BColor tile, BColor ink)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "An icon needs a positive size.");

        var list = new BRenderList();
        AppMark(list, new BRect(0, 0, size, size), page, tile, ink);

        using var renderer = new BImageRenderer();
        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(size, size)),
            new BFrameContext(BColor.FromArgb(0, 0, 0, 0)));

        return bitmap.ToPixelBuffer();
    }

    private readonly struct Pen(BRenderList list, BRect box, BColor color)
    {
        private readonly double _scale = box.Width / DesignExtent;

        private double X(double x) => box.Left + (x * _scale);
        private double Y(double y) => box.Top + (y * _scale);
        private double S(double v) => v * _scale;

        public void Bar(double x, double y, double length, double thickness) =>
            Rect(x, y - (thickness / 2), length, thickness);

        public void Column(double x, double y, double length, double thickness) =>
            Rect(x, y, thickness, length);

        public void Rect(double x, double y, double width, double height) =>
            list.FillRect(new BRect(X(x), Y(y), S(width), S(height)), color);

        public void RoundedRect(double x, double y, double width, double height, double radius) =>
            list.FillRoundedRect(new BRect(X(x), Y(y), S(width), S(height)), color, S(radius), S(radius));

        public void Circle(double x, double y, double diameter, double thickness) =>
            list.StrokeRoundedRect(
                new BRect(X(x), Y(y), S(diameter), S(diameter)),
                color,
                S(diameter / 2),
                S(diameter / 2),
                S(thickness));

        public void Outline(double x, double y, double width, double height, double thickness)
        {
            Rect(x, y, width, thickness);
            Rect(x, y + height - thickness, width, thickness);
            Rect(x, y, thickness, height);
            Rect(x + width - thickness, y, thickness, height);
        }

        public void Triangle(double ax, double ay, double bx, double by, double cx, double cy) =>
            list.FillTriangle(new BPoint(X(ax), Y(ay)), new BPoint(X(bx), Y(by)), new BPoint(X(cx), Y(cy)), color);

        public void Line(double ax, double ay, double bx, double by, double thickness)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 0)
                return;

            double nx = -dy / length * (thickness / 2);
            double ny = dx / length * (thickness / 2);

            Triangle(ax + nx, ay + ny, bx + nx, by + ny, bx - nx, by - ny);
            Triangle(ax + nx, ay + ny, bx - nx, by - ny, ax - nx, ay - ny);
        }
    }
}

namespace TheInfinity.Core.Geometry;

public readonly record struct CanvasPoint(double X, double Y);

public readonly record struct CanvasRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public CanvasPoint Center => new(X + Width / 2, Y + Height / 2);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Intersects(CanvasRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public bool Contains(double x, double y) =>
        x >= X && x < Right && y >= Y && y < Bottom;

    public CanvasRect Inflate(double margin) =>
        new(X - margin, Y - margin, Width + margin * 2, Height + margin * 2);

    public CanvasRect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public static CanvasRect Union(IEnumerable<CanvasRect> rects)
    {
        var any = false;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        foreach (var rect in rects)
        {
            if (rect.IsEmpty)
                continue;
            if (!any)
            {
                minX = rect.X;
                minY = rect.Y;
                maxX = rect.Right;
                maxY = rect.Bottom;
                any = true;
                continue;
            }

            minX = Math.Min(minX, rect.X);
            minY = Math.Min(minY, rect.Y);
            maxX = Math.Max(maxX, rect.Right);
            maxY = Math.Max(maxY, rect.Bottom);
        }

        return any ? new CanvasRect(minX, minY, maxX - minX, maxY - minY) : default;
    }
}

public readonly record struct GuideLine(bool Vertical, double Position);

public readonly record struct SnapAdjustment(double Dx, double Dy, IReadOnlyList<GuideLine> Guides);

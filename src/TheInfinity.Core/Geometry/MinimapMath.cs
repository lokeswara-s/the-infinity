namespace TheInfinity.Core.Geometry;

public readonly record struct MinimapProjection(
    CanvasRect WorldBounds,
    double Scale,
    double OriginX,
    double OriginY);

public static class MinimapMath
{
    public static CanvasRect WorldBounds(IEnumerable<CanvasRect> items, CanvasRect viewportWorld, double pad = 120)
    {
        var union = CanvasRect.Union(items.Append(viewportWorld));
        if (union.IsEmpty)
            union = viewportWorld.IsEmpty ? new CanvasRect(0, 0, 1920, 1080) : viewportWorld;
        return union.Inflate(pad);
    }

    public static MinimapProjection Project(CanvasRect world, double mapWidth, double mapHeight)
    {
        var safe = world.Width <= 1 || world.Height <= 1
            ? new CanvasRect(world.X, world.Y, Math.Max(world.Width, 1), Math.Max(world.Height, 1))
            : world;
        var scale = Math.Min(mapWidth / safe.Width, mapHeight / safe.Height);
        var usedW = safe.Width * scale;
        var usedH = safe.Height * scale;
        return new MinimapProjection(safe, scale, (mapWidth - usedW) / 2, (mapHeight - usedH) / 2);
    }

    public static CanvasRect ToMinimap(CanvasRect rect, MinimapProjection projection) =>
        new(
            projection.OriginX + (rect.X - projection.WorldBounds.X) * projection.Scale,
            projection.OriginY + (rect.Y - projection.WorldBounds.Y) * projection.Scale,
            Math.Max(2, rect.Width * projection.Scale),
            Math.Max(2, rect.Height * projection.Scale));

    public static CanvasPoint ToWorld(double mapX, double mapY, MinimapProjection projection)
    {
        if (projection.Scale <= 0)
            return new CanvasPoint(projection.WorldBounds.X, projection.WorldBounds.Y);
        return new CanvasPoint(
            projection.WorldBounds.X + (mapX - projection.OriginX) / projection.Scale,
            projection.WorldBounds.Y + (mapY - projection.OriginY) / projection.Scale);
    }
}

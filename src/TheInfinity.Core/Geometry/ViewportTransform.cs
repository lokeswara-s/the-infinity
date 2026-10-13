namespace TheInfinity.Core.Geometry;

/// <summary>
/// Maps logical workspace coordinates to the screen (or overview) viewport.
/// Screen = (canvas - offset) * scale. Offset is the canvas point at the viewport origin.
/// </summary>
public sealed class ViewportTransform
{
    public const double DefaultMinScale = 0.15;
    public const double DefaultMaxScale = 1;

    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Scale { get; set; } = 1;
    public double MinScale { get; set; } = DefaultMinScale;
    public double MaxScale { get; set; } = DefaultMaxScale;

    public CanvasPoint ScreenToCanvas(double screenX, double screenY) =>
        new(OffsetX + screenX / Scale, OffsetY + screenY / Scale);

    public (double X, double Y) CanvasToScreen(double canvasX, double canvasY) =>
        ((canvasX - OffsetX) * Scale, (canvasY - OffsetY) * Scale);

    public CanvasRect CanvasToScreen(CanvasRect rect)
    {
        var (x, y) = CanvasToScreen(rect.X, rect.Y);
        return new CanvasRect(x, y, rect.Width * Scale, rect.Height * Scale);
    }

    public CanvasRect ScreenViewportToCanvas(double viewportWidth, double viewportHeight)
    {
        var origin = ScreenToCanvas(0, 0);
        var far = ScreenToCanvas(viewportWidth, viewportHeight);
        return new CanvasRect(origin.X, origin.Y, far.X - origin.X, far.Y - origin.Y);
    }

    public void PanByScreenDelta(double dx, double dy)
    {
        if (Scale <= 0)
            return;
        OffsetX -= dx / Scale;
        OffsetY -= dy / Scale;
    }

    public bool ZoomAt(double screenX, double screenY, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            return false;

        var anchor = ScreenToCanvas(screenX, screenY);
        var next = Math.Clamp(Scale * factor, MinScale, MaxScale);
        if (Math.Abs(next - Scale) < 0.0000001)
            return false;

        Scale = next;
        OffsetX = anchor.X - screenX / Scale;
        OffsetY = anchor.Y - screenY / Scale;
        return true;
    }

    public void Reset(double offsetX = 0, double offsetY = 0)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        Scale = 1;
    }

    public void ZoomToFit(CanvasRect bounds, double viewportWidth, double viewportHeight, double padding = 64, double? maxFitScale = null)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1 || viewportWidth <= padding * 2 || viewportHeight <= padding * 2)
        {
            Reset(bounds.X, bounds.Y);
            return;
        }

        var upper = Math.Min(MaxScale, maxFitScale ?? MaxScale);
        var scale = Math.Clamp(
            Math.Min((viewportWidth - padding * 2) / bounds.Width, (viewportHeight - padding * 2) / bounds.Height),
            MinScale,
            Math.Max(MinScale, upper));
        Scale = scale;
        var screenX = (viewportWidth - bounds.Width * scale) / 2;
        var screenY = (viewportHeight - bounds.Height * scale) / 2;
        OffsetX = bounds.X - screenX / scale;
        OffsetY = bounds.Y - screenY / scale;
    }

    public void CenterOn(CanvasRect target, double viewportWidth, double viewportHeight)
    {
        var screenW = target.Width * Scale;
        var screenH = target.Height * Scale;
        OffsetX = target.X - (viewportWidth - screenW) / 2 / Scale;
        OffsetY = target.Y - (viewportHeight - screenH) / 2 / Scale;
    }
}

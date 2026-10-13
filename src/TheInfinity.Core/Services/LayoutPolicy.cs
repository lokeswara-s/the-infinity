using TheInfinity.Core.Geometry;

namespace TheInfinity.Core.Services;

public readonly record struct PlacementDecision(bool ShouldShow, CanvasRect ScreenRect);

/// <summary>
/// Decides whether a logical window belongs on the physical desktop.
/// Windows that leave the virtual screen are minimized rather than moved to extreme coordinates.
/// </summary>
public static class LayoutPolicy
{
    public static PlacementDecision Decide(
        CanvasRect canvas,
        ViewportTransform viewport,
        CanvasRect virtualScreen,
        double margin,
        bool userMinimized,
        bool overviewActive)
    {
        var screen = viewport.CanvasToScreen(canvas);
        if (overviewActive || userMinimized)
            return new PlacementDecision(false, screen);

        if (screen.Width < 40 || screen.Height < 30)
            return new PlacementDecision(false, screen);

        if (!screen.Intersects(virtualScreen.Inflate(margin)))
            return new PlacementDecision(false, screen);

        return new PlacementDecision(true, screen);
    }
}

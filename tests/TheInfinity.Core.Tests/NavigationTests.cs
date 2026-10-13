using TheInfinity.Core.Geometry;
using TheInfinity.Core.Models;
using TheInfinity.Core.Services;

namespace TheInfinity.Core.Tests;

public class DragGestureTests
{
    [Fact]
    public void Deltas_come_from_real_cursor_positions_and_sum_to_the_total_travel()
    {
        var gesture = new DragGesture();
        gesture.Begin(500, 400);
        int totalX = 0, totalY = 0;
        foreach (var (x, y) in new[] { (502, 401), (510, 405), (530, 420), (580, 460), (700, 520) })
        {
            if (gesture.TryMove(x, y, out var dx, out var dy))
            {
                totalX += dx;
                totalY += dy;
            }
        }

        Assert.Equal(200, totalX);
        Assert.Equal(120, totalY);
        Assert.True(gesture.End());
    }

    [Fact]
    public void Small_movement_is_a_click_not_a_drag()
    {
        var gesture = new DragGesture();
        gesture.Begin(100, 100);
        Assert.False(gesture.TryMove(101, 101, out _, out _));
        Assert.False(gesture.End());
    }

    [Fact]
    public void Moves_without_a_button_down_are_ignored()
    {
        var gesture = new DragGesture();
        Assert.False(gesture.TryMove(50, 50, out _, out _));
    }
}

public class OverviewNavigationTests
{
    private static WorkspaceSession NewSession(double screenX = 0)
    {
        var session = new WorkspaceSession { VirtualScreen = new CanvasRect(screenX, 0, 1920, 1080) };
        session.ApplyPreferencesToViewport();
        return session;
    }

    private static ManagedWindow NewWindow(double x, double y, double width = 600, double height = 400) =>
        new() { Hwnd = 1, ProcessId = 1, CanvasX = x, CanvasY = y, Width = width, Height = height };

    [Fact]
    public void The_zoom_level_decides_the_mode()
    {
        var session = NewSession();
        Assert.False(session.OverviewActive);
        session.Viewport.ZoomAt(960, 540, 1 / 1.15);
        Assert.True(session.OverviewActive);
        session.Viewport.ZoomAt(960, 540, 1.15);
        Assert.False(session.OverviewActive);
    }

    [Fact]
    public void Real_size_is_the_zoom_ceiling()
    {
        var session = NewSession();
        Assert.False(session.Viewport.ZoomAt(100, 100, 2));
        Assert.Equal(1, session.Viewport.Scale);
    }

    [Fact]
    public void Zooming_back_in_keeps_whatever_is_under_the_cursor_under_the_cursor()
    {
        var session = NewSession();
        session.Viewport.ZoomAt(1500, 700, 0.4);
        var before = session.Viewport.ScreenToCanvas(300, 200);
        session.Viewport.ZoomAt(300, 200, 10);
        var after = session.Viewport.ScreenToCanvas(300, 200);
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
        Assert.False(session.OverviewActive);
    }

    [Fact]
    public void Leaving_without_moving_restores_the_exact_desktop_view()
    {
        var session = NewSession();
        session.Viewport.OffsetX = 300;
        session.Viewport.OffsetY = -120;
        session.RememberWorkView();
        session.FitToContent();
        Assert.True(session.OverviewActive);
        session.LeaveOverview();
        Assert.Equal(300, session.Viewport.OffsetX);
        Assert.Equal(-120, session.Viewport.OffsetY);
        Assert.Equal(1, session.Viewport.Scale);
    }

    [Fact]
    public void Leaving_after_navigating_zooms_in_around_the_middle_of_the_screen()
    {
        var session = NewSession(screenX: -1920);
        session.Windows.Add(NewWindow(0, 0, 8000, 3000));
        session.RememberWorkView();
        session.FitToContent();
        session.Viewport.PanByScreenDelta(-200, 50);
        session.MarkNavigated();
        var middle = session.Viewport.ScreenToCanvas(-1920 + 960, 540);
        session.LeaveOverview();
        var back = session.Viewport.ScreenToCanvas(-1920 + 960, 540);
        Assert.Equal(middle.X, back.X, 3);
        Assert.Equal(middle.Y, back.Y, 3);
        Assert.Equal(1, session.Viewport.Scale);
    }

    [Fact]
    public void Fit_always_zooms_out_and_keeps_everything_on_a_screen_with_a_negative_origin()
    {
        var session = NewSession(screenX: -1920);
        session.Windows.Add(NewWindow(100, 100, 400, 300));
        session.FitToContent();
        Assert.True(session.OverviewActive);
        var screen = session.Viewport.CanvasToScreen(session.Windows[0].CanvasBounds);
        Assert.InRange(screen.X, -1920, 0);
        Assert.InRange(screen.Right, -1920, 0);
        Assert.InRange(screen.Y, 0, 1080);
    }

    [Fact]
    public void Fit_for_many_spread_windows_shows_them_all()
    {
        var session = NewSession();
        session.Windows.Add(NewWindow(0, 0, 9000, 3000));
        session.FitToContent();
        Assert.True(session.Viewport.Scale < 0.3);
        var screen = session.Viewport.CanvasToScreen(session.Windows[0].CanvasBounds);
        Assert.InRange(screen.X, 0, 1920);
        Assert.InRange(screen.Right, 0, 1920);
    }

    [Fact]
    public void Centering_respects_a_virtual_screen_with_a_negative_origin_at_any_zoom()
    {
        var session = NewSession(screenX: -1920);
        session.Viewport.Scale = 0.5;
        session.CenterOnCanvas(5000, 2000);
        var (x, y) = session.Viewport.CanvasToScreen(5000, 2000);
        Assert.Equal(-1920 + 960, x, 3);
        Assert.Equal(540, y, 3);
    }

    [Fact]
    public void Zooming_into_a_window_grows_it_in_place_and_keeps_it_on_screen()
    {
        var session = NewSession();
        var window = NewWindow(1000, 500);
        session.Windows.Add(window);
        session.Viewport.Scale = 0.6;
        session.Viewport.OffsetX = 200;
        session.Viewport.OffsetY = 0;
        var thumb = session.Viewport.CanvasToScreen(window.CanvasBounds);
        session.ZoomToWindow(window);
        Assert.False(session.OverviewActive);
        var full = session.Viewport.CanvasToScreen(window.CanvasBounds);
        Assert.Equal(600, full.Width, 3);
        Assert.InRange(full.X, 0, 1920 - 600);
        Assert.InRange(full.Y, 0, 1080 - 400);
        // Roomy case: no nudge was needed, so the centre did not move.
        Assert.Equal(thumb.Center.X, full.Center.X, 3);
        Assert.Equal(thumb.Center.Y, full.Center.Y, 3);
    }

    [Fact]
    public void Zooming_into_a_window_near_the_edge_pulls_it_fully_on_screen()
    {
        var session = NewSession();
        var window = NewWindow(0, 0);
        session.Windows.Add(window);
        session.Viewport.Scale = 0.2;
        session.Viewport.OffsetX = -50;
        session.Viewport.OffsetY = -50;
        session.ZoomToWindow(window);
        var full = session.Viewport.CanvasToScreen(window.CanvasBounds);
        Assert.True(full.X >= 0 && full.Y >= 0);
        Assert.True(full.Right <= 1920 && full.Bottom <= 1080);
    }

    [Fact]
    public void Windows_returning_to_view_are_revealed_not_activated()
    {
        var session = NewSession();
        var window = new ManagedWindow { Hwnd = 9, ProcessId = 1, CanvasX = 100, CanvasY = 100, Width = 600, Height = 400, IsMinimized = true, ParkedByWorkspace = true };
        session.Windows.Add(window);
        var driver = new RecordingDriver();
        session.ApplyLayout(driver, () => 1);
        Assert.Contains(9, driver.Revealed);
        Assert.DoesNotContain(9, driver.Restored);
    }

    private sealed class RecordingDriver : IWindowDriver
    {
        public List<nint> Revealed { get; } = [];
        public List<nint> Restored { get; } = [];
        public void Move(nint hwnd, int x, int y, int width, int height) { }
        public void Minimize(nint hwnd) { }
        public void Maximize(nint hwnd) { }
        public void Restore(nint hwnd) => Restored.Add(hwnd);
        public void Reveal(nint hwnd) => Revealed.Add(hwnd);
        public void Close(nint hwnd) { }
        public void Activate(nint hwnd) { }
        public bool IsAlive(nint hwnd, int processId) => true;
        public WindowObservation? Observe(nint hwnd) => null;
    }
}

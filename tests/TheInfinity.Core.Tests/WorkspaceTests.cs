using TheInfinity.Core.Geometry;
using TheInfinity.Core.Models;
using TheInfinity.Core.Services;

namespace TheInfinity.Core.Tests;

public class ViewportTransformTests
{
    [Fact]
    public void ZoomAt_keeps_the_cursor_anchor_stable()
    {
        var viewport = new ViewportTransform();
        viewport.ZoomAt(180, 90, 1.35);
        var anchor = viewport.ScreenToCanvas(180, 90);
        Assert.Equal(180, anchor.X, 3);
        Assert.Equal(90, anchor.Y, 3);
    }

    [Fact]
    public void Screen_and_canvas_round_trip()
    {
        var viewport = new ViewportTransform { OffsetX = 400, OffsetY = -50, Scale = 1.25 };
        var canvas = viewport.ScreenToCanvas(320, 200);
        var (x, y) = viewport.CanvasToScreen(canvas.X, canvas.Y);
        Assert.Equal(320, x, 3);
        Assert.Equal(200, y, 3);
    }

    [Fact]
    public void Zoom_is_clamped()
    {
        var viewport = new ViewportTransform { MinScale = 0.2, MaxScale = 2, Scale = 2 };
        Assert.False(viewport.ZoomAt(10, 10, 2));
        Assert.Equal(2, viewport.Scale);
        viewport.Scale = 0.2;
        Assert.False(viewport.ZoomAt(10, 10, 0.5));
        Assert.Equal(0.2, viewport.Scale);
    }

    [Fact]
    public void Pan_moves_the_camera_opposite_the_drag()
    {
        var viewport = new ViewportTransform();
        viewport.PanByScreenDelta(50, -20);
        Assert.Equal(-50, viewport.OffsetX);
        Assert.Equal(20, viewport.OffsetY);
    }

    [Fact]
    public void ZoomToFit_places_content_inside_the_viewport()
    {
        var viewport = new ViewportTransform();
        var bounds = new CanvasRect(1000, 2000, 400, 200);
        viewport.ZoomToFit(bounds, 800, 600, padding: 40);
        var screen = viewport.CanvasToScreen(bounds);
        Assert.InRange(screen.X, 0, 800);
        Assert.InRange(screen.Y, 0, 600);
        Assert.InRange(screen.Right, 0, 800);
        Assert.InRange(screen.Bottom, 0, 600);
    }
}

public class SnapAndMinimapTests
{
    [Fact]
    public void Snap_aligns_a_near_edge()
    {
        var moving = new CanvasRect(102, 10, 100, 80);
        var other = new CanvasRect(0, 400, 100, 80);
        var snap = SnapEngine.SnapMove(moving, [other], 14);
        Assert.Equal(-2, snap.Dx);
        Assert.Equal(0, snap.Dy);
        Assert.Contains(snap.Guides, guide => guide.Vertical && guide.Position == 100);
    }

    [Fact]
    public void Minimap_round_trip_maps_back_to_the_same_world_point()
    {
        var world = new CanvasRect(-2000, -1000, 8000, 4000);
        var projection = MinimapMath.Project(world, 200, 120);
        var mapped = MinimapMath.ToMinimap(new CanvasRect(100, 50, 40, 20), projection);
        var back = MinimapMath.ToWorld(mapped.X, mapped.Y, projection);
        Assert.Equal(100, back.X, 3);
        Assert.Equal(50, back.Y, 3);
    }
}

public class WindowMatcherTests
{
    [Fact]
    public void Match_key_uses_process_metadata_and_not_the_hwnd()
    {
        var key = WindowIdentity.BuildKey(@"C:\Apps\Code.exe", "Code", "Chrome_WidgetWin_1", "Program.cs - Visual Studio Code");
        Assert.DoesNotContain("hwnd", key, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code.exe", key, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("program.cs", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Matcher_assigns_each_window_once_and_prefers_the_title()
    {
        var savedA = Window("code.exe", "One");
        var savedB = Window("code.exe", "Two");
        var liveA = Observation(10, "code.exe", "Two");
        var liveB = Observation(11, "code.exe", "One");
        var matches = WindowMatcher.Match([savedA, savedB], [liveA, liveB]);
        Assert.Equal(savedB.Id, matches[liveA].Id);
        Assert.Equal(savedA.Id, matches[liveB].Id);
    }

    [Fact]
    public void Different_executables_do_not_match()
    {
        var saved = Window("code.exe", "Notes");
        var live = Observation(4, "notepad.exe", "Notes");
        Assert.True(WindowMatcher.Score(saved, live) < WindowMatcher.MinimumScore);
    }

    private static ManagedWindow Window(string exe, string title) => new()
    {
        ExecutablePath = $@"C:\Apps\{exe}",
        ProcessName = Path.GetFileNameWithoutExtension(exe),
        WindowClass = "Window",
        Title = title
    };

    private static WindowObservation Observation(int hwnd, string exe, string title) => new(
        hwnd, 100, Path.GetFileNameWithoutExtension(exe), $@"C:\Apps\{exe}", "Window", title,
        0, 0, 800, 600, 0, 0, 800, 600, false, false, true, "\\\\.\\DISPLAY1");
}

public class LayoutPolicyTests
{
    [Fact]
    public void Windows_outside_the_virtual_screen_are_not_shown()
    {
        var viewport = new ViewportTransform();
        var decision = LayoutPolicy.Decide(new CanvasRect(20000, 10, 400, 300), viewport, new CanvasRect(0, 0, 1920, 1080), 32, false, false);
        Assert.False(decision.ShouldShow);
    }

    [Fact]
    public void Windows_inside_the_virtual_screen_are_shown()
    {
        var viewport = new ViewportTransform();
        var decision = LayoutPolicy.Decide(new CanvasRect(100, 80, 800, 600), viewport, new CanvasRect(0, 0, 1920, 1080), 32, false, false);
        Assert.True(decision.ShouldShow);
        Assert.Equal(100, decision.ScreenRect.X);
    }

    [Fact]
    public void ApplyLayout_minimizes_far_windows_and_ignores_echoed_moves()
    {
        var session = new WorkspaceSession
        {
            VirtualScreen = new CanvasRect(0, 0, 1920, 1080)
        };
        session.Windows.Add(new ManagedWindow { Hwnd = 7, ProcessId = 3, CanvasX = 40000, CanvasY = 20, Width = 300, Height = 200 });
        var driver = new FakeDriver();
        session.ApplyLayout(driver, () => 1000);
        Assert.Contains(7, driver.Minimized);
        Assert.DoesNotContain(driver.Moves, move => Math.Abs(move.X) > 4000);

        var echoed = false;
        var reentrant = new FakeDriver
        {
            OnMove = () => echoed = session.TryApplyExternalBounds(1, 1, 10, 10, 100, 100, false, false, 1000)
        };
        session.Windows.Add(new ManagedWindow { Hwnd = 1, ProcessId = 1, CanvasX = 10, CanvasY = 10, Width = 200, Height = 200 });
        session.ApplyLayout(reentrant, () => 1000);
        Assert.False(echoed);
    }

    private sealed class FakeDriver : IWindowDriver
    {
        public List<WindowMove> Moves { get; } = [];
        public List<nint> Minimized { get; } = [];
        public Action? OnMove { get; init; }
        public void Move(nint hwnd, int x, int y, int width, int height)
        {
            Moves.Add(new WindowMove(hwnd, x, y, width, height));
            OnMove?.Invoke();
        }

        public void Minimize(nint hwnd) => Minimized.Add(hwnd);
        public void Maximize(nint hwnd) { }
        public void Restore(nint hwnd) { }
        public void Close(nint hwnd) { }
        public void Activate(nint hwnd) { }
        public bool IsAlive(nint hwnd, int processId) => true;
        public WindowObservation? Observe(nint hwnd) => null;
    }
}

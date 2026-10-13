using TheInfinity.Core.Geometry;
using TheInfinity.Core.Models;

namespace TheInfinity.Core.Services;

/// <summary>
/// Logical workspace state. Win32 callbacks are applied here so programmatic moves
/// cannot echo back into another move.
/// </summary>
public sealed class WorkspaceSession
{
    private readonly HashSet<Guid> _selection = [];
    private (double X, double Y)? _workOffset;

    public WorkspaceModel Workspace { get; set; } = new();
    public UserPreferences Preferences { get; set; } = new();
    public ViewportTransform Viewport { get; } = new();
    public List<ManagedWindow> Windows { get; } = [];
    /// <summary>
    /// Real windows cannot be scaled, so the zoom level is the mode: at 100% the real windows sit on the
    /// desktop; below that they are shown as live thumbnails. There is no separate toggle to get out of sync.
    /// </summary>
    public bool OverviewActive => Viewport.Scale < OverviewThreshold;
    public const double OverviewThreshold = 0.999;
    /// <summary>Largest scale used when the whole canvas is fitted, so a fit is always visibly zoomed out.</summary>
    public const double OverviewFitScale = 0.85;
    public bool ApplyingLayout { get; private set; }
    public long SuppressEventsUntil { get; private set; }
    public CanvasRect VirtualScreen { get; set; } = new(0, 0, 1920, 1080);
    private const double ParkMargin = 32;
    public bool CleanShutdown { get; set; } = true;
    public IReadOnlyCollection<Guid> Selection => _selection;
    public IReadOnlyList<GuideLine> ActiveGuides { get; private set; } = [];

    /// <summary>Raised after any change to the windows, selection or viewport bookkeeping.</summary>
    public event Action? Changed;

    public void ApplyPreferencesToViewport()
    {
        Viewport.MinScale = Math.Clamp(Preferences.MinZoom, 0.05, 1);
        // Windows cannot be enlarged past their real size, so 100% is the ceiling.
        Viewport.MaxScale = 1;
    }

    public void LoadPersisted(
        WorkspaceModel workspace,
        IEnumerable<ManagedWindow> windows,
        double offsetX,
        double offsetY,
        double scale)
    {
        Workspace = workspace;
        Windows.Clear();
        Windows.AddRange(windows);
        Viewport.OffsetX = offsetX;
        Viewport.OffsetY = offsetY;
        Viewport.Scale = scale <= 0 ? 1 : scale;
        ApplyPreferencesToViewport();
        Changed?.Invoke();
    }

    public void Synchronize(IReadOnlyList<WindowObservation> live, bool restoreSavedPositions)
    {
        var consumedLive = new HashSet<nint>();
        var consumedWindows = new HashSet<Guid>();

        foreach (var observation in live)
        {
            var existing = Windows.FirstOrDefault(window =>
                !window.IsPlaceholder &&
                window.Hwnd == observation.Hwnd &&
                window.ProcessId == observation.ProcessId);
            if (existing is null)
                continue;

            TouchMetadata(existing, observation, keepCanvas: true);
            consumedLive.Add(observation.Hwnd);
            consumedWindows.Add(existing.Id);
        }

        var unbound = Windows.Where(window => !consumedWindows.Contains(window.Id)).ToList();
        var unmatchedLive = live.Where(observation => !consumedLive.Contains(observation.Hwnd)).ToList();
        foreach (var (observation, window) in WindowMatcher.Match(unbound, unmatchedLive))
        {
            TouchMetadata(window, observation, keepCanvas: restoreSavedPositions);
            window.IsPlaceholder = false;
            consumedLive.Add(observation.Hwnd);
            consumedWindows.Add(window.Id);
        }

        foreach (var observation in live.Where(item => !consumedLive.Contains(item.Hwnd)))
        {
            var created = new ManagedWindow();
            TouchMetadata(created, observation, keepCanvas: false);
            Windows.Add(created);
            consumedWindows.Add(created.Id);
        }

        foreach (var window in Windows.Where(item => !item.IsPlaceholder && !consumedWindows.Contains(item.Id) && item.Hwnd != 0).ToList())
        {
            window.Hwnd = 0;
            window.IsPlaceholder = Preferences.ShowPlaceholders;
            if (!Preferences.ShowPlaceholders)
                Windows.Remove(window);
        }

        if (!Preferences.ShowPlaceholders)
            Windows.RemoveAll(window => window.IsPlaceholder && window.Hwnd == 0);

        Changed?.Invoke();
    }

    public bool TryApplyExternalBounds(nint hwnd, int processId, int x, int y, int width, int height, bool minimized, bool maximized, long now)
    {
        if (ApplyingLayout || now < SuppressEventsUntil || OverviewActive)
            return false;

        var window = Windows.FirstOrDefault(item => item.Hwnd == hwnd && item.ProcessId == processId && !item.IsPlaceholder);
        if (window is null || window.IsPinned)
            return false;

        if (minimized)
        {
            window.IsMinimized = true;
            window.UserMinimized = !window.ParkedByWorkspace;
            Changed?.Invoke();
            return true;
        }

        window.UserMinimized = false;
        window.ParkedByWorkspace = false;
        window.IsMinimized = false;
        window.IsMaximized = maximized;
        if (!maximized && width > 40 && height > 30)
        {
            var canvas = Viewport.ScreenToCanvas(x, y);
            window.CanvasX = canvas.X;
            window.CanvasY = canvas.Y;
            window.Width = width;
            window.Height = height;
        }

        window.ScreenX = x;
        window.ScreenY = y;
        window.ScreenWidth = width;
        window.ScreenHeight = height;
        Changed?.Invoke();
        return true;
    }

    public void NoteForeground(nint hwnd)
    {
        var window = Windows.FirstOrDefault(item => item.Hwnd == hwnd);
        if (window is null)
            return;
        window.LastActive = DateTimeOffset.Now;
        Changed?.Invoke();
    }

    public void DragTo(IReadOnlyDictionary<Guid, CanvasPoint> origins, double dx, double dy, bool snap)
    {
        if (origins.Count == 0)
            return;

        var primaryId = origins.Keys.First();
        var primary = Windows.FirstOrDefault(window => window.Id == primaryId);
        if (primary is null)
            return;

        var proposed = new CanvasRect(origins[primaryId].X + dx, origins[primaryId].Y + dy, primary.Width, primary.Height);
        var adjustment = snap && Preferences.SnapEnabled
            ? SnapEngine.SnapMove(
                proposed,
                Windows.Where(window => !origins.ContainsKey(window.Id)).Select(window => window.CanvasBounds),
                Preferences.SnapThreshold)
            : new SnapAdjustment(0, 0, []);
        ActiveGuides = adjustment.Guides;

        foreach (var (id, origin) in origins)
        {
            var window = Windows.FirstOrDefault(item => item.Id == id);
            if (window is null || window.IsPinned)
                continue;
            window.CanvasX = origin.X + dx + adjustment.Dx;
            window.CanvasY = origin.Y + dy + adjustment.Dy;
        }

        Changed?.Invoke();
    }

    public void ClearGuides()
    {
        if (ActiveGuides.Count == 0)
            return;
        ActiveGuides = [];
        Changed?.Invoke();
    }

    public void MoveLogical(IEnumerable<Guid> ids, double dx, double dy, bool snap)
    {
        var moving = Windows.Where(window => ids.Contains(window.Id)).ToList();
        if (moving.Count == 0)
            return;

        var guides = snap && Preferences.SnapEnabled
            ? SnapEngine.SnapMove(
                moving[0].CanvasBounds.Offset(dx, dy),
                Windows.Where(window => !ids.Contains(window.Id)).Select(window => window.CanvasBounds),
                Preferences.SnapThreshold)
            : new SnapAdjustment(0, 0, []);

        foreach (var window in moving)
        {
            if (window.IsPinned)
                continue;
            window.CanvasX += dx + guides.Dx;
            window.CanvasY += dy + guides.Dy;
        }

        Changed?.Invoke();
    }

    public void TogglePin(ManagedWindow window)
    {
        window.IsPinned = !window.IsPinned;
        if (window.IsPinned)
        {
            window.PinScreenX = window.ScreenX;
            window.PinScreenY = window.ScreenY;
            window.PinWidth = Math.Max(80, window.ScreenWidth);
            window.PinHeight = Math.Max(40, window.ScreenHeight);
        }

        Changed?.Invoke();
    }

    public void SelectOnly(Guid id)
    {
        _selection.Clear();
        _selection.Add(id);
        Changed?.Invoke();
    }

    public void ToggleSelection(Guid id)
    {
        if (!_selection.Add(id))
            _selection.Remove(id);
        Changed?.Invoke();
    }

    public void Select(IEnumerable<Guid> ids, bool append)
    {
        if (!append)
            _selection.Clear();
        foreach (var id in ids)
            _selection.Add(id);
        Changed?.Invoke();
    }

    public void ClearSelection()
    {
        if (_selection.Count == 0)
            return;
        _selection.Clear();
        Changed?.Invoke();
    }

    public void ApplyLayout(IWindowDriver driver, Func<long> now)
    {
        if (OverviewActive)
            return;

        ApplyingLayout = true;
        SuppressEventsUntil = now() + 180;
        driver.BeginLayout();
        try
        {
            foreach (var window in Windows)
            {
                if (window.IsPlaceholder || window.Hwnd == 0)
                    continue;

                if (window.IsPinned)
                {
                    if (window.IsMinimized)
                        driver.Reveal(window.Hwnd);
                    driver.Move(window.Hwnd, window.PinScreenX, window.PinScreenY, Math.Max(80, window.PinWidth), Math.Max(40, window.PinHeight));
                    var canvas = Viewport.ScreenToCanvas(window.PinScreenX, window.PinScreenY);
                    window.CanvasX = canvas.X;
                    window.CanvasY = canvas.Y;
                    window.IsMinimized = false;
                    window.ParkedByWorkspace = false;
                    continue;
                }

                var decision = LayoutPolicy.Decide(
                    window.CanvasBounds,
                    Viewport,
                    VirtualScreen,
                    ParkMargin,
                    window.UserMinimized,
                    overviewActive: false);

                if (!decision.ShouldShow)
                {
                    if (!window.IsMinimized)
                    {
                        driver.Minimize(window.Hwnd);
                        window.ParkedByWorkspace = !window.UserMinimized;
                    }

                    window.IsMinimized = true;
                    continue;
                }

                if (window.IsMinimized)
                    driver.Reveal(window.Hwnd);

                if (window.IsMaximized)
                {
                    driver.Maximize(window.Hwnd);
                    window.IsMinimized = false;
                    window.ParkedByWorkspace = false;
                    continue;
                }

                var screen = decision.ScreenRect;
                var x = (int)Math.Round(screen.X);
                var y = (int)Math.Round(screen.Y);
                var width = Math.Max(80, (int)Math.Round(screen.Width));
                var height = Math.Max(40, (int)Math.Round(screen.Height));
                if (Math.Abs(window.ScreenX - x) <= 1 &&
                    Math.Abs(window.ScreenY - y) <= 1 &&
                    Math.Abs(window.ScreenWidth - width) <= 1 &&
                    Math.Abs(window.ScreenHeight - height) <= 1 &&
                    !window.IsMinimized &&
                    !window.ParkedByWorkspace)
                    continue;

                driver.Move(window.Hwnd, x, y, width, height);
                window.ScreenX = x;
                window.ScreenY = y;
                window.ScreenWidth = width;
                window.ScreenHeight = height;
                window.IsMinimized = false;
                window.ParkedByWorkspace = false;
            }
        }
        finally
        {
            driver.EndLayout();
            ApplyingLayout = false;
        }
    }

    /// <summary>True once the user panned, zoomed or jumped while the overview was open.</summary>
    public bool OverviewNavigated { get; private set; }

    public void MarkNavigated() => OverviewNavigated = true;

    /// <summary>Call just before zooming out of the desktop view so Esc can return to exactly this spot.</summary>
    public void RememberWorkView()
    {
        if (OverviewActive)
            return;
        _workOffset = (Viewport.OffsetX, Viewport.OffsetY);
        OverviewNavigated = false;
    }

    /// <summary>
    /// Returns to 100%. If the user has not moved around since zooming out, the desktop is exactly as they
    /// left it; otherwise the middle of the screen stays put and the view zooms in around it.
    /// </summary>
    public void LeaveOverview()
    {
        if (!OverviewActive)
            return;
        if (!OverviewNavigated && _workOffset is { } saved)
        {
            Viewport.Scale = 1;
            Viewport.OffsetX = saved.X;
            Viewport.OffsetY = saved.Y;
            return;
        }

        var center = Viewport.ScreenToCanvas(VirtualScreen.X + VirtualScreen.Width / 2, VirtualScreen.Y + VirtualScreen.Height / 2);
        CenterInVirtualScreen(center.X, center.Y);
    }

    /// <summary>Zooms out just far enough to see every window.</summary>
    public void FitToContent(double padding = 140)
    {
        Viewport.ZoomToFit(ContentBounds(), VirtualScreen.Width, VirtualScreen.Height, padding, OverviewFitScale);
        // ZoomToFit assumes the viewport starts at (0,0); viewport coordinates are absolute screen coordinates.
        Viewport.OffsetX -= VirtualScreen.X / Viewport.Scale;
        Viewport.OffsetY -= VirtualScreen.Y / Viewport.Scale;
    }

    /// <summary>The part of the canvas currently on the physical virtual screen.</summary>
    public CanvasRect VisibleCanvas()
    {
        var origin = Viewport.ScreenToCanvas(VirtualScreen.X, VirtualScreen.Y);
        var far = Viewport.ScreenToCanvas(VirtualScreen.Right, VirtualScreen.Bottom);
        return new CanvasRect(origin.X, origin.Y, far.X - origin.X, far.Y - origin.Y);
    }

    /// <summary>Keeps the current zoom and puts the canvas point in the middle of the virtual screen.</summary>
    public void CenterOnCanvas(double canvasX, double canvasY)
    {
        var scale = Viewport.Scale <= 0 ? 1 : Viewport.Scale;
        Viewport.OffsetX = canvasX - (VirtualScreen.X + VirtualScreen.Width / 2) / scale;
        Viewport.OffsetY = canvasY - (VirtualScreen.Y + VirtualScreen.Height / 2) / scale;
    }

    /// <summary>Sets scale 1 and places the canvas point at the middle of the physical virtual screen.</summary>
    public void CenterInVirtualScreen(double canvasX, double canvasY)
    {
        Viewport.Scale = 1;
        CenterOnCanvas(canvasX, canvasY);
    }

    /// <summary>
    /// Zooms to 100% around the window's thumbnail so it grows where it already is, then nudges the view
    /// just enough that the full-size window is on the screen.
    /// </summary>
    public void ZoomToWindow(ManagedWindow window)
    {
        var thumb = Viewport.CanvasToScreen(window.CanvasBounds);
        var anchor = window.CanvasBounds.Center;
        Viewport.Scale = 1;
        Viewport.OffsetX = anchor.X - (thumb.X + thumb.Width / 2);
        Viewport.OffsetY = anchor.Y - (thumb.Y + thumb.Height / 2);
        var full = Viewport.CanvasToScreen(window.CanvasBounds);
        Viewport.OffsetX -= Nudge(full.X, full.Width, VirtualScreen.X, VirtualScreen.Width);
        Viewport.OffsetY -= Nudge(full.Y, full.Height, VirtualScreen.Y, VirtualScreen.Height);
    }

    private static double Nudge(double position, double size, double min, double span)
    {
        if (size >= span || position < min)
            return min - position;
        var overflow = position + size - (min + span);
        return overflow > 0 ? -overflow : 0;
    }

    public CanvasRect ContentBounds()
    {
        var rects = Windows.Select(window => window.CanvasBounds).ToList();
        var union = CanvasRect.Union(rects);
        return union.IsEmpty ? new CanvasRect(Viewport.OffsetX, Viewport.OffsetY, 1280, 720) : union;
    }

    private void AdoptScreenAsCanvas(ManagedWindow window, WindowObservation observation)
    {
        var width = observation.IsMaximized || observation.IsMinimized ? observation.NormalWidth : observation.Width;
        var height = observation.IsMaximized || observation.IsMinimized ? observation.NormalHeight : observation.Height;
        var x = observation.IsMaximized || observation.IsMinimized ? observation.NormalX : observation.X;
        var y = observation.IsMaximized || observation.IsMinimized ? observation.NormalY : observation.Y;
        if (width < 40 || height < 30)
        {
            width = Math.Max(width, 800);
            height = Math.Max(height, 600);
        }

        var scale = Viewport.Scale <= 0 ? 1 : Viewport.Scale;
        var canvas = Viewport.ScreenToCanvas(x, y);
        window.CanvasX = canvas.X;
        window.CanvasY = canvas.Y;
        window.Width = width / scale;
        window.Height = height / scale;
    }

    private void TouchMetadata(ManagedWindow window, WindowObservation observation, bool keepCanvas)
    {
        window.Hwnd = observation.Hwnd;
        window.ProcessId = observation.ProcessId;
        window.ProcessName = observation.ProcessName;
        window.ExecutablePath = observation.ExecutablePath;
        window.WindowClass = observation.WindowClass;
        window.Title = string.IsNullOrWhiteSpace(observation.Title) ? observation.ProcessName : observation.Title;
        window.MatchKey = WindowIdentity.BuildKey(observation.ExecutablePath, observation.ProcessName, observation.WindowClass, window.Title);
        window.IsMinimized = observation.IsMinimized;
        window.IsMaximized = observation.IsMaximized;
        window.MonitorDevice = observation.MonitorDevice;
        window.ScreenX = observation.X;
        window.ScreenY = observation.Y;
        window.ScreenWidth = observation.Width;
        window.ScreenHeight = observation.Height;
        window.IsPlaceholder = false;
        if (observation.IsMinimized && !window.ParkedByWorkspace)
            window.UserMinimized = true;
        else if (!observation.IsMinimized)
            window.UserMinimized = false;
        if (!keepCanvas)
            AdoptScreenAsCanvas(window, observation);
        if (window.IsPinned && window.PinWidth == 0)
        {
            window.PinScreenX = observation.X;
            window.PinScreenY = observation.Y;
            window.PinWidth = Math.Max(80, observation.NormalWidth);
            window.PinHeight = Math.Max(40, observation.NormalHeight);
        }
    }
}

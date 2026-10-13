using Microsoft.Extensions.Logging;
using TheInfinity.Core.Geometry;
using TheInfinity.Core.Models;
using TheInfinity.Core.Persistence;
using TheInfinity.Core.Services;
using TheInfinity.Win32;

namespace TheInfinity.App.Services;

/// <summary>
/// Connects the workspace model to the real desktop: navigation, window actions and the app's lifetime.
/// The hooks, saving and window events live in small helpers that this class owns.
/// </summary>
public sealed class CanvasController : IDisposable
{
    private const double KeyboardPanStep = 240;
    private const double KeyboardZoomStep = 1.25;

    private readonly ILogger<CanvasController> _logger;
    private readonly IWindowCatalog _catalog;
    private readonly IWindowDriver _driver;
    private readonly WorkspacePersistence _persistence;
    private readonly WindowWatcher _watcher;
    private readonly GlobalInput _input;
    private readonly ViewportAnimator _animator;
    private readonly ThumbnailController _thumbnails = new();
    private readonly HotkeyBinder _hotkeys = new();
    private readonly UiTimer _refreshTimer;
    private bool _started;
    private bool _wasOverview;

    public CanvasController(
        ILogger<CanvasController> logger,
        IWorkspaceStore store,
        IWindowCatalog catalog,
        IWindowDriver driver,
        WorkspaceSession session)
    {
        _logger = logger;
        _catalog = catalog;
        _driver = driver;
        Session = session;

        var ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _persistence = new WorkspacePersistence(store, session, logger);
        _watcher = new WindowWatcher(session, driver, ui);
        _input = new GlobalInput(ui);
        _animator = new ViewportAnimator(session.Viewport, AfterViewportChange);
        _refreshTimer = new UiTimer(TimeSpan.FromSeconds(8), () => Refresh(silent: true));
    }

    public WorkspaceSession Session { get; }
    public ThumbnailController Thumbnails => _thumbnails;
    public bool RecoveredFromInterrupt { get; private set; }
    public IReadOnlyList<Hotkey> FailedHotkeys => _hotkeys.Failed;
    public event Action? StateChanged;
    public event Action? FocusSearchRequested;

    // ---- Lifetime ---------------------------------------------------------------------------------

    public async Task StartAsync()
    {
        var state = await _persistence.OpenAsync();
        RecoveredFromInterrupt = !state.CleanShutdown;
        Session.Preferences = state.Preferences;
        Session.CleanShutdown = false;
        Session.VirtualScreen = DisplayMetrics.VirtualScreen();
        Session.LoadPersisted(state.Workspace, state.Windows, state.ViewportOffsetX, state.ViewportOffsetY, state.ViewportScale);
        // Always start on the real desktop; zoom out from there.
        Session.Viewport.Scale = 1;
        Session.ApplyPreferencesToViewport();
        Session.Synchronize(_catalog.Enumerate(), state.Preferences.RestoreLayoutOnStartup);
        if (state.Preferences.LaunchMissingApps)
            MissingAppLauncher.Launch(Session, _logger);
        if (state.Preferences.RestoreLayoutOnStartup)
            Session.ApplyLayout(_driver, Now);

        Session.Changed += _persistence.QueueSave;
        _watcher.BoundsChanged += OnBoundsChanged;
        _watcher.ForegroundChanged += RaiseStateChanged;
        _watcher.RefreshNeeded += OnRefreshNeeded;
        _watcher.Start();

        _input.Enabled = state.Preferences.EnableGlobalPan;
        _input.Pan += (dx, dy) => Pan(dx, dy);
        _input.Zoom += (x, y, factor) => Zoom(x, y, factor);
        _input.PanEnded += _persistence.QueueSave;
        _input.Start();

        _refreshTimer.Start();
        await _persistence.SaveAsync(clean: false);
        _started = true;
        _persistence.Started = true;
        if (RecoveredFromInterrupt)
            _logger.LogWarning("Previous session did not shut down cleanly. Restored the last saved workspace.");
        _logger.LogInformation("Tracking {Count} windows. Database: {Path}", Session.Windows.Count, _persistence.DatabasePath);
        StateChanged?.Invoke();
    }

    /// <summary>Releases hooks, hotkeys and thumbnails. Must run on the UI thread. Safe to call twice.</summary>
    public void ReleaseNative()
    {
        _refreshTimer.Stop();
        _persistence.StopAutosave();
        _animator.Cancel();
        _watcher.Dispose();
        _input.Dispose();
        _hotkeys.Detach();
        _thumbnails.Dispose();
        Session.Changed -= _persistence.QueueSave;
    }

    public async Task ShutdownAsync()
    {
        if (!_started)
            return;
        _started = false;
        await _persistence.SaveAsync(clean: true);
        _logger.LogInformation("Shutdown complete.");
    }

    public void Dispose() => ReleaseNative();

    // ---- Hotkeys ----------------------------------------------------------------------------------

    public void AttachHotkeys(nint hwnd) => _hotkeys.Attach((IntPtr)hwnd);

    public void HandleHotkey(int id)
    {
        switch ((Hotkey)id)
        {
            case Hotkey.Overview: ToggleOverview(); break;
            case Hotkey.Home: ResetView(); break;
            case Hotkey.Fit: Fit(); break;
            case Hotkey.Refresh: Refresh(silent: false); break;
            case Hotkey.JumpToWindow: JumpToSelected(); break;
            case Hotkey.Search: RequestSearch(); break;
            case Hotkey.PanLeft: Pan(KeyboardPanStep, 0); break;
            case Hotkey.PanUp: Pan(0, KeyboardPanStep); break;
            case Hotkey.PanRight: Pan(-KeyboardPanStep, 0); break;
            case Hotkey.PanDown: Pan(0, -KeyboardPanStep); break;
            case Hotkey.ZoomIn: ZoomAtCenter(KeyboardZoomStep); break;
            case Hotkey.ZoomOut: ZoomAtCenter(1 / KeyboardZoomStep); break;
        }
    }

    public void RequestSearch() => FocusSearchRequested?.Invoke();

    private void ZoomAtCenter(double factor)
    {
        var screen = Session.VirtualScreen;
        Zoom(screen.X + screen.Width / 2, screen.Y + screen.Height / 2, factor);
    }

    // ---- Navigation -------------------------------------------------------------------------------

    public void ShowOverview() => EnterOverview();

    public void ToggleOverview()
    {
        if (Session.OverviewActive)
            ExitOverview();
        else
            EnterOverview();
    }

    /// <summary>Zooms out to show every window. Esc or the same hotkey returns to the exact desktop view.</summary>
    public void EnterOverview()
    {
        if (Session.OverviewActive)
        {
            StateChanged?.Invoke();
            return;
        }

        Session.RememberWorkView();
        Session.FitToContent();
        AfterViewportChange();
    }

    public void ExitOverview()
    {
        if (!Session.OverviewActive)
            return;
        Session.LeaveOverview();
        AfterViewportChange();
    }

    /// <summary>
    /// The one place that reacts to a changed viewport. The zoom level decides the mode: below 100% the
    /// overview (live thumbnails) is shown, at 100% the real windows are placed on the desktop.
    /// </summary>
    private void AfterViewportChange()
    {
        var overview = Session.OverviewActive;
        _input.AnyMiddle = overview;
        if (overview != _wasOverview)
        {
            _wasOverview = overview;
            if (!overview)
                _thumbnails.Clear();
        }

        if (!overview)
            Session.ApplyLayout(_driver, Now);
        _persistence.QueueSave();
        StateChanged?.Invoke();
    }

    public void Pan(double dx, double dy)
    {
        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01)
            return;
        Session.MarkNavigated();
        Session.Viewport.PanByScreenDelta(dx, dy);
        AfterViewportChange();
    }

    public void EndPan() => _persistence.QueueSave();

    public void CenterOnWorld(double canvasX, double canvasY)
    {
        Session.MarkNavigated();
        Session.CenterOnCanvas(canvasX, canvasY);
        AfterViewportChange();
    }

    public void PanCanvas(double canvasDx, double canvasDy)
    {
        Session.MarkNavigated();
        Session.Viewport.OffsetX += canvasDx;
        Session.Viewport.OffsetY += canvasDy;
        AfterViewportChange();
    }

    /// <summary>Zooms around a point in absolute screen coordinates. Zooming out of the desktop opens the overview.</summary>
    public void Zoom(double screenX, double screenY, double factor)
    {
        var wasOverview = Session.OverviewActive;
        if (!wasOverview && factor >= 1)
            return; // already at real size, which is the ceiling
        Session.RememberWorkView();
        if (!Session.Viewport.ZoomAt(screenX, screenY, factor))
            return;
        Session.MarkNavigated();
        AfterViewportChange();
    }

    public void ResetView()
    {
        Session.Viewport.Reset();
        AfterViewportChange();
    }

    public void Fit()
    {
        Session.RememberWorkView();
        Session.MarkNavigated();
        Session.FitToContent();
        AfterViewportChange();
    }

    public void JumpTo(Guid id, bool activate)
    {
        var window = FindWindow(id);
        if (window is null)
            return;
        Session.SelectOnly(id);
        var center = window.CanvasBounds.Center;
        if (Session.OverviewActive)
        {
            Session.MarkNavigated();
            var scale = Session.Viewport.Scale;
            var screen = Session.VirtualScreen;
            _animator.AnimateTo(center.X - (screen.X + screen.Width / 2) / scale, center.Y - (screen.Y + screen.Height / 2) / scale);
            _persistence.QueueSave();
            StateChanged?.Invoke();
            return;
        }

        Session.CenterInVirtualScreen(center.X, center.Y);
        window.UserMinimized = false;
        AfterViewportChange();
        if (activate && window.Hwnd != 0)
        {
            _driver.Activate(window.Hwnd);
            window.LastActive = DateTimeOffset.Now;
        }
    }

    private void JumpToSelected()
    {
        var id = Session.Selection.FirstOrDefault();
        if (id == Guid.Empty)
            id = Session.Windows.OrderByDescending(window => window.LastActive).Select(window => window.Id).FirstOrDefault();
        if (id != Guid.Empty)
            JumpTo(id, activate: !Session.OverviewActive);
    }

    /// <summary>
    /// Zooms into a window to real size where it already is, brings it to the front and gives it focus,
    /// so the next click or keystroke goes straight to it.
    /// </summary>
    public void ActivateWindow(Guid id)
    {
        var window = FindWindow(id);
        if (window is null)
            return;
        if (window.IsPlaceholder || window.Hwnd == 0)
        {
            JumpTo(id, activate: false);
            return;
        }

        Session.SelectOnly(id);
        Session.ZoomToWindow(window);
        window.UserMinimized = false;
        AfterViewportChange();
        _driver.Activate(window.Hwnd);
        window.LastActive = DateTimeOffset.Now;
    }

    // ---- Keeping the model in step with the desktop ------------------------------------------------

    public void Refresh(bool silent)
    {
        // Never fight the user while they are dragging a real window.
        if (_watcher.UserMoving && silent)
            return;
        Session.VirtualScreen = DisplayMetrics.VirtualScreen();
        Session.Synchronize(_catalog.Enumerate(), restoreSavedPositions: true);
        if (!Session.OverviewActive)
            Session.ApplyLayout(_driver, Now);
        if (!silent)
            _logger.LogInformation("Refreshed window list. {Count} entries.", Session.Windows.Count);
        StateChanged?.Invoke();
    }

    public void OnDisplayChanged()
    {
        Session.VirtualScreen = DisplayMetrics.VirtualScreen();
        if (!Session.OverviewActive)
            Session.ApplyLayout(_driver, Now);
        StateChanged?.Invoke();
    }

    private void OnBoundsChanged()
    {
        _persistence.QueueSave();
        StateChanged?.Invoke();
    }

    private void OnRefreshNeeded() => Refresh(silent: true);

    private void RaiseStateChanged() => StateChanged?.Invoke();

    // ---- Windows and selection --------------------------------------------------------------------

    public void MoveWindows(IReadOnlyDictionary<Guid, CanvasPoint> origins, double dx, double dy)
    {
        Session.DragTo(origins, dx, dy, snap: true);
        StateChanged?.Invoke();
    }

    public void FinishWindowDrag()
    {
        Session.ClearGuides();
        if (!Session.OverviewActive)
            Session.ApplyLayout(_driver, Now);
        _persistence.QueueSave();
        StateChanged?.Invoke();
    }

    public void Select(Guid id, bool append)
    {
        if (append)
            Session.ToggleSelection(id);
        else
            Session.SelectOnly(id);
        StateChanged?.Invoke();
    }

    public void Marquee(CanvasRect canvasRect, bool append)
    {
        var hits = Session.Windows.Where(window => window.CanvasBounds.Intersects(canvasRect)).Select(window => window.Id);
        Session.Select(hits, append);
        StateChanged?.Invoke();
    }

    public void ClearSelection()
    {
        Session.ClearSelection();
        StateChanged?.Invoke();
    }

    public void Minimize(Guid id) => WithWindow(id, window =>
    {
        window.UserMinimized = true;
        if (window.Hwnd != 0)
            _driver.Minimize(window.Hwnd);
        window.IsMinimized = true;
    });

    public void Maximize(Guid id) => WithWindow(id, window =>
    {
        window.UserMinimized = false;
        window.IsMaximized = true;
        if (window.Hwnd != 0)
        {
            _driver.Restore(window.Hwnd);
            _driver.Maximize(window.Hwnd);
        }
    });

    public void Restore(Guid id) => WithWindow(id, window =>
    {
        window.UserMinimized = false;
        window.IsMaximized = false;
        window.ParkedByWorkspace = false;
        if (window.Hwnd != 0)
            _driver.Restore(window.Hwnd);
        if (!Session.OverviewActive)
            Session.ApplyLayout(_driver, Now);
    });

    public void Close(Guid id) => WithWindow(id, window =>
    {
        if (window.Hwnd != 0)
            _driver.Close(window.Hwnd);
    });

    public void TogglePin(Guid id) => WithWindow(id, window => Session.TogglePin(window));

    /// <summary>Removes a remembered window of an app that is not running.</summary>
    public void Forget(Guid id)
    {
        var window = FindWindow(id);
        if (window is null || !window.IsPlaceholder)
            return;
        Session.Windows.Remove(window);
        Session.ClearSelection();
        _persistence.QueueSave();
        StateChanged?.Invoke();
    }

    private ManagedWindow? FindWindow(Guid id) => Session.Windows.FirstOrDefault(item => item.Id == id);

    private void WithWindow(Guid id, Action<ManagedWindow> action)
    {
        var window = FindWindow(id);
        if (window is null)
            return;
        action(window);
        _persistence.QueueSave();
        StateChanged?.Invoke();
    }

    // ---- Settings, saving and layouts -------------------------------------------------------------

    public void UpdatePreferences(UserPreferences preferences)
    {
        Session.Preferences = preferences;
        Session.ApplyPreferencesToViewport();
        StartupRegistration.SetEnabled(preferences.StartWithWindows);
        _input.Enabled = preferences.EnableGlobalPan;
        _persistence.QueueSave();
        StateChanged?.Invoke();
    }

    public async Task SaveAsync() => await _persistence.SaveAsync(clean: false);

    public async Task SaveNamedLayoutAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        await _persistence.SaveLayoutAsync(name.Trim());
        StateChanged?.Invoke();
    }

    public async Task<IReadOnlyList<StoredLayout>> LayoutsAsync() => await _persistence.ListLayoutsAsync();

    public async Task DeleteLayoutAsync(Guid id) => await _persistence.DeleteLayoutAsync(id);

    public async Task RestoreLayoutAsync(Guid id)
    {
        var layout = await _persistence.LoadLayoutAsync(id);
        if (layout is null)
            return;
        Session.Windows.Clear();
        Session.Windows.AddRange(layout.Windows);
        Session.Viewport.OffsetX = layout.ViewportOffsetX;
        Session.Viewport.OffsetY = layout.ViewportOffsetY;
        Session.Viewport.Scale = layout.ViewportScale <= 0 ? 1 : layout.ViewportScale;
        Session.Synchronize(_catalog.Enumerate(), restoreSavedPositions: true);
        if (!Session.OverviewActive)
            Session.ApplyLayout(_driver, Now);
        _persistence.QueueSave();
        StateChanged?.Invoke();
    }

    // ---- Thumbnails -------------------------------------------------------------------------------

    public void SetThumbnailDestination(nint hwnd) => _thumbnails.SetDestination(hwnd);

    public HashSet<nint> SyncThumbnails(IReadOnlyList<(nint Source, PixelRect Destination)> items) =>
        _thumbnails.Sync(items);

    private static long Now() => Environment.TickCount64;
}

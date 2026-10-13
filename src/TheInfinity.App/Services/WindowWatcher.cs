using TheInfinity.Core.Services;
using TheInfinity.Win32;

namespace TheInfinity.App.Services;

/// <summary>
/// Listens to window events from Windows (moves, focus, new or closed windows) and folds them into the
/// session. Events arrive on the UI thread, the one that installed the hooks.
/// </summary>
internal sealed class WindowWatcher : IDisposable
{
    private readonly WorkspaceSession _session;
    private readonly IWindowDriver _driver;
    private readonly SynchronizationContext _ui;
    private readonly WindowEventListener _events = new();
    private readonly UiTimer _refreshDebounce;
    private readonly HashSet<nint> _pendingLocations = [];
    private bool _flushQueued;

    public WindowWatcher(WorkspaceSession session, IWindowDriver driver, SynchronizationContext ui)
    {
        _session = session;
        _driver = driver;
        _ui = ui;
        _refreshDebounce = new UiTimer(TimeSpan.FromMilliseconds(250), OnRefreshDebounced);
    }

    /// <summary>True while the user is dragging or resizing a real window.</summary>
    public bool UserMoving { get; private set; }

    /// <summary>Windows were moved or resized by something other than the workspace.</summary>
    public event Action? BoundsChanged;

    /// <summary>The focused window changed.</summary>
    public event Action? ForegroundChanged;

    /// <summary>Windows appeared or disappeared, so the list should be re-read.</summary>
    public event Action? RefreshNeeded;

    public void Start()
    {
        _events.Raised += OnWindowEvent;
        _events.Start();
    }

    public void Dispose()
    {
        _refreshDebounce.Stop();
        _events.Dispose();
    }

    private void OnWindowEvent(WindowHookEvent evt)
    {
        switch (evt.EventType)
        {
            case WindowEvents.LocationChanged:
                if (IsManaged(evt.Hwnd))
                    QueueLocation(evt.Hwnd);
                break;
            case WindowEvents.MoveSizeStart:
                if (IsManaged(evt.Hwnd))
                    UserMoving = true;
                break;
            case WindowEvents.MoveSizeEnd:
                if (UserMoving || IsManaged(evt.Hwnd))
                {
                    UserMoving = false;
                    QueueLocation(evt.Hwnd);
                }

                break;
            case WindowEvents.Foreground:
                if (IsManaged(evt.Hwnd))
                {
                    _session.NoteForeground(evt.Hwnd);
                    ForegroundChanged?.Invoke();
                }

                break;
            default:
                _refreshDebounce.Restart();
                break;
        }
    }

    private bool IsManaged(nint hwnd) =>
        hwnd != 0 && _session.Windows.Any(window => window.Hwnd == hwnd);

    // Location events come in bursts while a window is dragged, so they are collected and handled together.
    private void QueueLocation(nint hwnd)
    {
        _pendingLocations.Add(hwnd);
        if (_flushQueued)
            return;
        _flushQueued = true;
        _ui.Post(_ =>
        {
            _flushQueued = false;
            var batch = _pendingLocations.ToArray();
            _pendingLocations.Clear();
            var changed = false;
            foreach (var pending in batch)
                changed |= ApplyLocation(pending);
            if (changed)
                BoundsChanged?.Invoke();
        }, null);
    }

    private bool ApplyLocation(nint hwnd)
    {
        var observation = _driver.Observe(hwnd);
        if (observation is null)
            return false;
        return _session.TryApplyExternalBounds(
            observation.Hwnd,
            observation.ProcessId,
            observation.X,
            observation.Y,
            observation.Width,
            observation.Height,
            observation.IsMinimized,
            observation.IsMaximized,
            Environment.TickCount64);
    }

    private void OnRefreshDebounced()
    {
        _refreshDebounce.Stop();
        if (UserMoving)
        {
            _refreshDebounce.Restart();
            return;
        }

        RefreshNeeded?.Invoke();
    }
}

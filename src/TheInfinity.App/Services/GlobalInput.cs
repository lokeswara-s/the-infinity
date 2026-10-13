using TheInfinity.Win32;

namespace TheInfinity.App.Services;

/// <summary>
/// Wraps the global mouse hook. A hook callback must return quickly, so pans are summed up and zooms are
/// posted, and everything is delivered from the UI message queue instead of from inside the callback.
/// </summary>
internal sealed class GlobalInput : IDisposable
{
    private const double ZoomStep = 1.15;

    private readonly MiddleMouseHook _hook = new();
    private readonly SynchronizationContext _ui;
    private int _panQueued;
    private int _panDx;
    private int _panDy;

    public GlobalInput(SynchronizationContext ui)
    {
        _ui = ui;
        _hook.PanDelta += OnPanDelta;
        _hook.ZoomRequested += OnZoomRequested;
        _hook.PanEnded += () => PanEnded?.Invoke();
    }

    /// <summary>Accumulated pan in screen pixels since the last event.</summary>
    public event Action<int, int>? Pan;

    /// <summary>Absolute screen position and the zoom factor to apply around it.</summary>
    public event Action<int, int, double>? Zoom;

    public event Action? PanEnded;

    public bool Enabled
    {
        get => _hook.Enabled;
        set => _hook.Enabled = value;
    }

    /// <summary>When true a plain middle-drag pans (used while zoomed out).</summary>
    public bool AnyMiddle
    {
        get => _hook.AnyMiddle;
        set => _hook.AnyMiddle = value;
    }

    public void Start() => _hook.Start();

    public void Dispose() => _hook.Dispose();

    private void OnPanDelta(int dx, int dy)
    {
        Interlocked.Add(ref _panDx, dx);
        Interlocked.Add(ref _panDy, dy);
        if (Interlocked.Exchange(ref _panQueued, 1) == 1)
            return;
        _ui.Post(_ =>
        {
            var x = Interlocked.Exchange(ref _panDx, 0);
            var y = Interlocked.Exchange(ref _panDy, 0);
            Interlocked.Exchange(ref _panQueued, 0);
            Pan?.Invoke(x, y);
        }, null);
    }

    private void OnZoomRequested(int x, int y, int delta)
    {
        var factor = delta > 0 ? ZoomStep : 1 / ZoomStep;
        _ui.Post(_ => Zoom?.Invoke(x, y, factor), null);
    }
}

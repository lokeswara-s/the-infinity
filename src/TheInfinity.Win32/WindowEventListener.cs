namespace TheInfinity.Win32;

public static class WindowEvents
{
    public const uint Foreground = 0x0003;
    public const uint MoveSizeStart = 0x000A;
    public const uint MoveSizeEnd = 0x000B;
    public const uint MinimizeStart = 0x0016;
    public const uint MinimizeEnd = 0x0017;
    public const uint Created = 0x8000;
    public const uint Destroyed = 0x8001;
    public const uint Shown = 0x8002;
    public const uint Hidden = 0x8003;
    public const uint LocationChanged = 0x800B;
    public const uint NameChanged = 0x800C;
}

public readonly record struct WindowHookEvent(uint EventType, nint Hwnd);

/// <summary>
/// Installs narrow WinEvent hooks. A single wide range would call into managed code for
/// every accessibility event on the system.
/// </summary>
public sealed class WindowEventListener : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback;
    private readonly List<IntPtr> _hooks = [];

    public WindowEventListener()
    {
        _callback = OnEvent;
    }

    public event Action<WindowHookEvent>? Raised;

    public void Start()
    {
        if (_hooks.Count > 0)
            return;
        Install(WindowEvents.Foreground, WindowEvents.Foreground);
        Install(WindowEvents.MoveSizeStart, WindowEvents.MoveSizeEnd);
        Install(WindowEvents.MinimizeStart, WindowEvents.MinimizeEnd);
        Install(WindowEvents.Created, WindowEvents.Hidden);
        Install(WindowEvents.LocationChanged, WindowEvents.NameChanged);
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
    }

    private void Install(uint min, uint max)
    {
        var hook = NativeMethods.SetWinEventHook(
            min,
            max,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        if (hook != IntPtr.Zero)
            _hooks.Add(hook);
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != NativeMethods.ObjidWindow || idChild != 0 || hwnd == IntPtr.Zero)
            return;
        try
        {
            Raised?.Invoke(new WindowHookEvent(eventType, hwnd));
        }
        catch
        {
            // Never throw out of a WinEvent callback.
        }
    }
}

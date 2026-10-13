using TheInfinity.Core.Models;
using TheInfinity.Core.Services;

namespace TheInfinity.Win32;

public sealed class WindowDriver : IWindowDriver
{
    private readonly WindowCatalog _catalog = new();
    private readonly WindowSnapshotCache _snapshots;
    private int _layoutDepth;

    public WindowDriver(WindowSnapshotCache snapshots)
    {
        _snapshots = snapshots;
    }

    public void BeginLayout()
    {
        if (_layoutDepth++ == 0)
            NativeMethods.LockSetForegroundWindow(NativeMethods.LsfwLock);
    }

    public void EndLayout()
    {
        if (_layoutDepth == 0)
            return;
        if (--_layoutDepth == 0)
            NativeMethods.LockSetForegroundWindow(NativeMethods.LsfwUnlock);
    }

    public void Move(nint hwnd, int x, int y, int width, int height)
    {
        if (hwnd == 0)
            return;
        NativeMethods.SetWindowPos((IntPtr)hwnd, IntPtr.Zero, x, y, width, height, NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
    }

    public void Minimize(nint hwnd)
    {
        if (hwnd == 0)
            return;
        // Keep a still of the window: once minimized there is no live image to show in the overview.
        _snapshots.Capture(hwnd);
        NativeMethods.ShowWindow((IntPtr)hwnd, NativeMethods.SwShowMinNoActive);
    }

    public void Maximize(nint hwnd)
    {
        if (hwnd != 0)
            NativeMethods.ShowWindow((IntPtr)hwnd, NativeMethods.SwMaximize);
    }

    public void Restore(nint hwnd)
    {
        if (hwnd == 0)
            return;
        if (NativeMethods.IsIconic((IntPtr)hwnd))
            NativeMethods.ShowWindow((IntPtr)hwnd, NativeMethods.SwRestore);
        else
            NativeMethods.ShowWindow((IntPtr)hwnd, NativeMethods.SwShowNoActivate);
    }

    public void Reveal(nint hwnd)
    {
        if (hwnd == 0)
            return;
        // SW_SHOWNOACTIVATE restores a minimized window without taking focus, unlike SW_RESTORE.
        NativeMethods.ShowWindow((IntPtr)hwnd, NativeMethods.SwShowNoActivate);
    }

    public void Close(nint hwnd)
    {
        if (hwnd != 0)
            NativeMethods.PostMessage((IntPtr)hwnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    public void Activate(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow((IntPtr)hwnd))
            return;

        var target = (IntPtr)hwnd;
        if (NativeMethods.IsIconic(target))
            NativeMethods.ShowWindow(target, NativeMethods.SwRestore);

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(target, out _);
        var current = NativeMethods.GetCurrentThreadId();
        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != current)
                attachedForeground = NativeMethods.AttachThreadInput(current, foregroundThread, true);
            if (targetThread != 0 && targetThread != current && targetThread != foregroundThread)
                attachedTarget = NativeMethods.AttachThreadInput(current, targetThread, true);
            NativeMethods.BringWindowToTop(target);
            NativeMethods.SetForegroundWindow(target);
        }
        finally
        {
            if (attachedForeground)
                NativeMethods.AttachThreadInput(current, foregroundThread, false);
            if (attachedTarget)
                NativeMethods.AttachThreadInput(current, targetThread, false);
        }
    }

    public bool IsAlive(nint hwnd, int processId)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow((IntPtr)hwnd))
            return false;
        NativeMethods.GetWindowThreadProcessId((IntPtr)hwnd, out var pid);
        return pid == (uint)processId;
    }

    public WindowObservation? Observe(nint hwnd) =>
        _catalog.TryObserve((IntPtr)hwnd, out var observation) ? observation : null;
}

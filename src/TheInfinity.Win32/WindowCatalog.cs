using System.Text;
using TheInfinity.Core.Models;

namespace TheInfinity.Win32;

public sealed class WindowCatalog : Core.Services.IWindowCatalog
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow", "DV2ControlHost", "MsgrIMEWindowClass",
        "SysShadow", "DummyDWMListenerWindow", "EdgeUiInputTopWndClass",
        "Windows.Internal.Shell.TabProxyWindow"
    };

    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost",
        "TextInputHost", "LockApp", "dwm", "csrss"
    };

    private readonly int _ownProcessId = Environment.ProcessId;

    public IReadOnlyList<WindowObservation> Enumerate()
    {
        var list = new List<WindowObservation>();
        var paths = new Dictionary<uint, string?>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (TryObserve(hwnd, paths, out var observation) && observation is not null)
                list.Add(observation);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public bool TryObserve(IntPtr hwnd, out WindowObservation? observation)
    {
        var paths = new Dictionary<uint, string?>();
        return TryObserve(hwnd, paths, out observation);
    }

    private bool TryObserve(IntPtr hwnd, Dictionary<uint, string?> paths, out WindowObservation? observation)
    {
        observation = null;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            return false;

        var exStyle = (int)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        var isAppWindow = (exStyle & NativeMethods.WsExAppWindow) != 0;
        var isTool = (exStyle & NativeMethods.WsExToolWindow) != 0;
        if (isTool && !isAppWindow)
            return false;
        if (!isAppWindow && NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero)
            return false;
        if (IsCloaked(hwnd))
            return false;

        var className = ReadClass(hwnd);
        if (ExcludedClasses.Contains(className))
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)_ownProcessId)
            return false;

        var path = ResolvePath(pid, paths);
        var processName = string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(processName))
        {
            try
            {
                processName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                processName = "";
            }
        }

        if (ExcludedProcesses.Contains(processName))
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            return false;

        var title = ReadTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title) && rect.Width < 160)
            return false;
        if (className.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(title))
            return false;

        var placement = new NativeMethods.WindowPlacement { Length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WindowPlacement>() };
        var hasPlacement = NativeMethods.GetWindowPlacement(hwnd, ref placement);
        var normal = hasPlacement ? placement.NormalPosition : rect;
        var normalWidth = Math.Max(0, normal.Width);
        var normalHeight = Math.Max(0, normal.Height);

        observation = new WindowObservation(
            hwnd,
            (int)pid,
            processName,
            string.IsNullOrEmpty(path) ? null : path,
            className,
            title,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            normalWidth > 0 ? normal.Left : rect.Left,
            normalHeight > 0 ? normal.Top : rect.Top,
            normalWidth > 0 ? normalWidth : rect.Width,
            normalHeight > 0 ? normalHeight : rect.Height,
            NativeMethods.IsIconic(hwnd),
            NativeMethods.IsZoomed(hwnd),
            true,
            ReadMonitor(hwnd));
        return true;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        var hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    private static string ReadClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WmGetTextLength, IntPtr.Zero, IntPtr.Zero, NativeMethods.SmtoAbortIfHung, 120, out var lengthPtr);
        var length = lengthPtr.ToInt32();
        if (length <= 0)
            return "";
        var buffer = new StringBuilder(length + 1);
        NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WmGetText, (IntPtr)buffer.Capacity, buffer, NativeMethods.SmtoAbortIfHung, 120, out _);
        return buffer.ToString();
    }

    private static string? ResolvePath(uint pid, Dictionary<uint, string?> cache)
    {
        if (cache.TryGetValue(pid, out var cached))
            return cached;

        string? path = null;
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (process != IntPtr.Zero)
        {
            try
            {
                var capacity = 1024;
                var buffer = new StringBuilder(capacity);
                if (NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref capacity))
                    path = buffer.ToString();
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }
        }

        cache[pid] = path;
        return path;
    }

    private static string? ReadMonitor(IntPtr hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return null;
        var info = new NativeMethods.MonitorInfoEx { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.Device : null;
    }
}

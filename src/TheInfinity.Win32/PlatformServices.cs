using Microsoft.Win32;

namespace TheInfinity.Win32;

public static class DisplayMetrics
{
    public static Core.Geometry.CanvasRect VirtualScreen()
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen));
        var height = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
        return new Core.Geometry.CanvasRect(x, y, width, height);
    }

    public static double SystemDpiScale()
    {
        var dpi = NativeMethods.GetDpiForSystem();
        return dpi == 0 ? 1 : dpi / 96.0;
    }
}

public static class NativeWindow
{
    /// <summary>Places a window at an exact physical-pixel rectangle in the topmost band.</summary>
    public static void PlaceTopmost(IntPtr hwnd, Core.Geometry.CanvasRect rect, bool show)
    {
        if (hwnd == IntPtr.Zero)
            return;
        var flags = NativeMethods.SwpNoActivate | (show ? NativeMethods.SwpShowWindow : 0);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, flags);
    }

    /// <summary>Raises a window to the top of the topmost band without activating it.</summary>
    public static void RaiseTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize);
    }
}

public static class DesktopWallpaper
{
    /// <summary>Path of the current wallpaper image, or null. Read-only; never changes the user's wallpaper.</summary>
    public static string? FindPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            if (key?.GetValue("WallPaper") is string path && path.Length > 0 && System.IO.File.Exists(path))
                return path;
            var transcoded = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return System.IO.File.Exists(transcoded) ? transcoded : null;
        }
        catch
        {
            return null;
        }
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TheInfinity";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
                key.SetValue(ValueName, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

public static class FileIcon
{
    public static IntPtr LoadSmall(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return IntPtr.Zero;
        var info = new NativeMethods.ShFileInfo();
        NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ShFileInfo>(), NativeMethods.ShgfiIcon | NativeMethods.ShgfiSmallIcon);
        return info.Icon;
    }

    public static void Destroy(IntPtr icon)
    {
        if (icon != IntPtr.Zero)
            NativeMethods.DestroyIcon(icon);
    }
}

/// <summary>Global shortcuts. Every one is Ctrl+Alt plus a key; the id is also the WM_HOTKEY id.</summary>
public enum Hotkey
{
    Overview = 1,
    Home,
    Fit,
    Refresh,
    JumpToWindow,
    Search,
    PanLeft,
    PanUp,
    PanRight,
    PanDown,
    ZoomIn,
    ZoomOut
}

public sealed class HotkeyBinder
{
    private const uint Modifiers = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat;

    private static readonly (Hotkey Id, uint VirtualKey)[] Bindings =
    [
        (Hotkey.Overview, 0x4F),     // O
        (Hotkey.Home, 0x48),         // H
        (Hotkey.Fit, 0x46),          // F
        (Hotkey.Refresh, 0x52),      // R
        (Hotkey.JumpToWindow, 0x4A), // J
        (Hotkey.Search, 0x53),       // S
        (Hotkey.PanLeft, 0x25),      // Left arrow
        (Hotkey.PanUp, 0x26),        // Up arrow
        (Hotkey.PanRight, 0x27),     // Right arrow
        (Hotkey.PanDown, 0x28),      // Down arrow
        (Hotkey.ZoomIn, 0xBB),       // =
        (Hotkey.ZoomOut, 0xBD),      // -
    ];

    private readonly List<Hotkey> _registered = [];
    private IntPtr _hwnd;

    /// <summary>Hotkeys that could not be registered because another app owns them.</summary>
    public List<Hotkey> Failed { get; } = [];

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        foreach (var (id, key) in Bindings)
        {
            if (NativeMethods.RegisterHotKey(hwnd, (int)id, Modifiers, key))
                _registered.Add(id);
            else
                Failed.Add(id);
        }
    }

    public void Detach()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        foreach (var id in _registered)
            NativeMethods.UnregisterHotKey(_hwnd, (int)id);
        _registered.Clear();
    }
}
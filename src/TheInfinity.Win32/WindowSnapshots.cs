using System.Runtime.InteropServices;

namespace TheInfinity.Win32;

/// <summary>A downscaled copy of a window's pixels (32-bit BGRA, top-down) taken while it was visible.</summary>
public sealed record WindowSnapshot(nint Hwnd, int ProcessId, int Width, int Height, byte[] Bgra, DateTimeOffset Taken);

/// <summary>
/// Windows cannot give a live image of a minimized window, and the workspace minimizes windows that
/// are off the current screen. So a still is taken with PrintWindow just before a window is minimized
/// and used in the overview until the window is live again. PrintWindow is a documented API and does
/// not inject anything into the target process.
/// </summary>
public sealed class WindowSnapshotCache
{
    public const int MaxWidth = 640;
    private const int MaxEntries = 64;
    private readonly Dictionary<nint, WindowSnapshot> _items = [];

    public WindowSnapshot? Get(nint hwnd, int processId) =>
        hwnd != 0 && _items.TryGetValue(hwnd, out var snapshot) && snapshot.ProcessId == processId ? snapshot : null;

    public void Capture(nint hwnd)
    {
        try
        {
            var snapshot = Grab(hwnd);
            if (snapshot is null)
                return;
            _items[hwnd] = snapshot;
            if (_items.Count > MaxEntries)
            {
                var oldest = _items.OrderBy(pair => pair.Value.Taken).First().Key;
                _items.Remove(oldest);
            }
        }
        catch
        {
            // A missing preview must never break window management.
        }
    }

    private static WindowSnapshot? Grab(nint hwnd)
    {
        var window = (IntPtr)hwnd;
        if (hwnd == 0 || !NativeMethods.IsWindow(window) || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window))
            return null;
        if (!Gdi.GetWindowRect(window, out var rect))
            return null;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width < 40 || height < 30 || width > 16384 || height > 16384)
            return null;
        NativeMethods.GetWindowThreadProcessId(window, out var pid);

        var screen = Gdi.GetDC(IntPtr.Zero);
        var full = Gdi.CreateCompatibleDC(screen);
        var fullBitmap = Gdi.CreateCompatibleBitmap(screen, width, height);
        var fullOld = Gdi.SelectObject(full, fullBitmap);
        IntPtr small = IntPtr.Zero, smallBitmap = IntPtr.Zero, smallOld = IntPtr.Zero;
        try
        {
            // PW_RENDERFULLCONTENT also captures DirectX/Chromium/Electron windows.
            if (!Gdi.PrintWindow(window, full, Gdi.PwRenderFullContent))
                return null;

            var targetWidth = Math.Min(width, MaxWidth);
            var targetHeight = Math.Max(1, (int)Math.Round(height * (targetWidth / (double)width)));
            var source = full;
            if (targetWidth != width)
            {
                small = Gdi.CreateCompatibleDC(screen);
                smallBitmap = Gdi.CreateCompatibleBitmap(screen, targetWidth, targetHeight);
                smallOld = Gdi.SelectObject(small, smallBitmap);
                Gdi.SetStretchBltMode(small, Gdi.Halftone);
                Gdi.SetBrushOrgEx(small, 0, 0, IntPtr.Zero);
                if (!Gdi.StretchBlt(small, 0, 0, targetWidth, targetHeight, full, 0, 0, width, height, Gdi.SrcCopy))
                    return null;
                source = small;
            }

            var header = new Gdi.BitmapInfoHeader
            {
                Size = Marshal.SizeOf<Gdi.BitmapInfoHeader>(),
                Width = targetWidth,
                Height = -targetHeight, // negative = top-down
                Planes = 1,
                BitCount = 32
            };
            var pixels = new byte[targetWidth * targetHeight * 4];
            var bitmap = small != IntPtr.Zero ? smallBitmap : fullBitmap;
            if (Gdi.GetDIBits(source, bitmap, 0, (uint)targetHeight, pixels, ref header, 0) == 0)
                return null;

            var anyColor = false;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0)
                    anyColor = true;
                pixels[i + 3] = 255;
            }

            // An all-black result means the app refused to paint; keep the previous good snapshot instead.
            return anyColor ? new WindowSnapshot(hwnd, (int)pid, targetWidth, targetHeight, pixels, DateTimeOffset.Now) : null;
        }
        finally
        {
            if (small != IntPtr.Zero)
            {
                Gdi.SelectObject(small, smallOld);
                Gdi.DeleteObject(smallBitmap);
                Gdi.DeleteDC(small);
            }

            Gdi.SelectObject(full, fullOld);
            Gdi.DeleteObject(fullBitmap);
            Gdi.DeleteDC(full);
            Gdi.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static class Gdi
    {
        public const uint PwRenderFullContent = 0x2;
        public const int Halftone = 4;
        public const uint SrcCopy = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr handle);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
        [DllImport("gdi32.dll")]
        public static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint rop);
        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    }
}

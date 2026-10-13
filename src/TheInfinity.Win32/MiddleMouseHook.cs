using System.Runtime.InteropServices;
using TheInfinity.Core.Geometry;

namespace TheInfinity.Win32;

/// <summary>
/// Global canvas gestures, so the desktop can be navigated without any app having focus:
///   Ctrl+Alt + middle-drag  pans (plain middle-drag too while the overview is open)
///   Ctrl+Alt + wheel        zooms around the cursor
/// Mouse moves are never swallowed, so the cursor keeps moving and every pan delta comes from a real
/// position. A middle click without a drag is replayed so other apps still receive it.
/// </summary>
public sealed class MiddleMouseHook : IDisposable
{
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int WmMouseWheel = 0x020A;

    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly DragGesture _gesture = new();
    private IntPtr _hook;

    public MiddleMouseHook()
    {
        _callback = OnMouse;
    }

    public event Action<int, int>? PanDelta;
    public event Action? PanEnded;
    /// <summary>Screen x, screen y, wheel delta (positive = away from the user).</summary>
    public event Action<int, int, int>? ZoomRequested;
    public bool Enabled { get; set; } = true;
    /// <summary>When true a plain middle-drag pans, without needing Ctrl+Alt.</summary>
    public bool AnyMiddle { get; set; }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
            return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _callback, NativeMethods.GetModuleHandle(null), 0);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static bool ChordDown() =>
        (NativeMethods.GetAsyncKeyState(VkControl) & 0x8000) != 0 &&
        (NativeMethods.GetAsyncKeyState(VkMenu) & 0x8000) != 0;

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= NativeMethods.HcAction && lParam != IntPtr.Zero)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.MsLlHookStruct>(lParam);
                var injected = (data.Flags & NativeMethods.LlmhfInjected) != 0;
                var message = (int)wParam;
                if (!injected)
                {
                    if (message == NativeMethods.WmMButtonDown && Enabled && (AnyMiddle || ChordDown()))
                    {
                        _gesture.Begin(data.Point.X, data.Point.Y);
                        return (IntPtr)1;
                    }

                    if (message == WmMouseWheel && Enabled && ChordDown())
                    {
                        var delta = (short)((data.MouseData >> 16) & 0xFFFF);
                        ZoomRequested?.Invoke(data.Point.X, data.Point.Y, delta);
                        return (IntPtr)1;
                    }

                    if (message == NativeMethods.WmMouseMove && _gesture.IsDown)
                    {
                        if (_gesture.TryMove(data.Point.X, data.Point.Y, out var dx, out var dy))
                            PanDelta?.Invoke(dx, dy);
                    }
                    else if (message == NativeMethods.WmMButtonUp && _gesture.IsDown)
                    {
                        if (_gesture.End())
                        {
                            PanEnded?.Invoke();
                            return (IntPtr)1;
                        }

                        // A plain middle click: replay it so the app under the cursor still sees it.
                        NativeMethods.mouse_event(NativeMethods.MouseeventfMiddleDown, 0, 0, 0, UIntPtr.Zero);
                        NativeMethods.mouse_event(NativeMethods.MouseeventfMiddleUp, 0, 0, 0, UIntPtr.Zero);
                        return (IntPtr)1;
                    }
                }
            }
            catch
            {
                // A low-level hook must never throw.
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

using TheInfinity.Core.Models;

namespace TheInfinity.Core.Services;

public readonly record struct WindowMove(nint Hwnd, int X, int Y, int Width, int Height);

public interface IWindowDriver
{
    void BeginLayout() { }
    void EndLayout() { }
    void Move(nint hwnd, int x, int y, int width, int height);
    void Minimize(nint hwnd);
    void Maximize(nint hwnd);
    void Restore(nint hwnd);

    /// <summary>Brings a window back from a workspace-parked minimize without stealing focus.</summary>
    void Reveal(nint hwnd) => Restore(hwnd);

    void Close(nint hwnd);
    void Activate(nint hwnd);
    bool IsAlive(nint hwnd, int processId);
    WindowObservation? Observe(nint hwnd);
}

public interface IWindowCatalog
{
    IReadOnlyList<WindowObservation> Enumerate();
}

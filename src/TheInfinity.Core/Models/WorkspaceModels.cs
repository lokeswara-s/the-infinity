using TheInfinity.Core.Geometry;

namespace TheInfinity.Core.Models;

public sealed class ManagedWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [System.Text.Json.Serialization.JsonIgnore]
    public nint Hwnd { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string? ExecutablePath { get; set; }
    public string WindowClass { get; set; } = "";
    public string Title { get; set; } = "";
    public string MatchKey { get; set; } = "";
    public double CanvasX { get; set; }
    public double CanvasY { get; set; }
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 600;
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public bool IsMinimized { get; set; }
    public bool IsMaximized { get; set; }
    public bool UserMinimized { get; set; }
    public bool ParkedByWorkspace { get; set; }
    public bool IsPinned { get; set; }
    public int PinScreenX { get; set; }
    public int PinScreenY { get; set; }
    public int PinWidth { get; set; }
    public int PinHeight { get; set; }
    public bool IsPlaceholder { get; set; }
    public string? MonitorDevice { get; set; }
    public DateTimeOffset LastActive { get; set; } = DateTimeOffset.Now;

    public CanvasRect CanvasBounds => new(CanvasX, CanvasY, Width, Height);
}

public sealed class WorkspaceModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Workspace";
    public bool IsActive { get; set; } = true;
}

public sealed class UserPreferences
{
    public double MinZoom { get; set; } = ViewportTransform.DefaultMinScale;
    public bool SnapEnabled { get; set; } = true;
    public double SnapThreshold { get; set; } = 14;
    public bool ShowPlaceholders { get; set; } = true;
    public bool RestoreLayoutOnStartup { get; set; } = true;
    public bool LaunchMissingApps { get; set; }
    public bool StartWithWindows { get; set; }
    public bool AutoSave { get; set; } = true;
    public bool EnableGlobalPan { get; set; } = true;
}

public sealed class LayoutSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layout";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed record WindowObservation(
    nint Hwnd,
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string WindowClass,
    string Title,
    int X,
    int Y,
    int Width,
    int Height,
    int NormalX,
    int NormalY,
    int NormalWidth,
    int NormalHeight,
    bool IsMinimized,
    bool IsMaximized,
    bool IsVisible,
    string? MonitorDevice);

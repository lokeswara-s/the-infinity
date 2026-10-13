using System.Windows.Media;
using TheInfinity.App.Mvvm;

namespace TheInfinity.App.ViewModels;

public sealed class WindowItemViewModel : ObservableObject
{
    private string _title = "";
    private string _processName = "";
    private double _viewLeft;
    private double _viewTop;
    private double _viewWidth;
    private double _viewHeight;
    private bool _isSelected;
    private bool _isPinned;
    private bool _isPlaceholder;
    private bool _isMinimized;
    private bool _isDimmed;
    private bool _showFallback = true;
    private ImageSource? _icon;
    private ImageSource? _snapshot;

    public Guid Id { get; init; }
    public nint Hwnd { get; set; }
    public string? ExecutablePath { get; set; }

    public string Title { get => _title; set => Set(ref _title, value); }
    public string ProcessName { get => _processName; set => Set(ref _processName, value); }
    public double ViewLeft { get => _viewLeft; set => Set(ref _viewLeft, value); }
    public double ViewTop { get => _viewTop; set => Set(ref _viewTop, value); }
    public double ViewWidth { get => _viewWidth; set => Set(ref _viewWidth, value); }
    public double ViewHeight { get => _viewHeight; set => Set(ref _viewHeight, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }
    public bool IsPlaceholder { get => _isPlaceholder; set => Set(ref _isPlaceholder, value); }
    public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }
    public bool IsDimmed { get => _isDimmed; set => Set(ref _isDimmed, value); }
    public bool ShowFallback
    {
        get => _showFallback;
        set
        {
            if (Set(ref _showFallback, value))
                RaiseFallbackKinds();
        }
    }

    /// <summary>A still taken just before the window was minimized; shown when there is no live thumbnail.</summary>
    public ImageSource? Snapshot
    {
        get => _snapshot;
        set
        {
            if (Set(ref _snapshot, value))
                RaiseFallbackKinds();
        }
    }

    public DateTimeOffset SnapshotTaken { get; set; }
    public bool ShowSnapshot => ShowFallback && Snapshot is not null;
    public bool ShowIcon => ShowFallback && Snapshot is null;

    private void RaiseFallbackKinds()
    {
        OnPropertyChanged(nameof(ShowSnapshot));
        OnPropertyChanged(nameof(ShowIcon));
    }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
}

public sealed class GuideItemViewModel
{
    public bool Vertical { get; init; }
    public double Position { get; init; }
    public double Length { get; init; }
}

public sealed class LayoutItemViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Created { get; init; } = "";
}

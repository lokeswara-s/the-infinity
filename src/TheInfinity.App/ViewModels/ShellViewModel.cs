using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using TheInfinity.App.Mvvm;
using TheInfinity.App.Services;
using TheInfinity.Core.Geometry;
using TheInfinity.Core.Models;
using TheInfinity.Win32;

namespace TheInfinity.App.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly CanvasController _controller;
    private readonly WindowSnapshotCache _snapshots;
    private readonly Dictionary<Guid, WindowItemViewModel> _windows = [];
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<nint> _liveThumbnails = [];
    private string _searchText = "";
    private string _status = "Starting";
    private string _zoomLabel = "100%";
    private bool _isOverview;
    private string _layoutName = "Layout";
    private bool _searchOpen;

    public ShellViewModel(CanvasController controller, WindowSnapshotCache snapshots)
    {
        _controller = controller;
        _snapshots = snapshots;
        Windows = [];
        Guides = [];
        SearchResults = [];
        Layouts = [];
        ToggleOverviewCommand = new RelayCommand(() => _controller.ToggleOverview());
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
        ResetCommand = new RelayCommand(() => _controller.ResetView());
        FitCommand = new RelayCommand(() => _controller.Fit());
        SaveCommand = new RelayCommand(async () => await _controller.SaveAsync());
        ClearSelectionCommand = new RelayCommand(() => _controller.ClearSelection());
        SaveLayoutCommand = new RelayCommand(async () =>
        {
            await _controller.SaveNamedLayoutAsync(LayoutName);
            await ReloadLayoutsAsync();
            Status = $"Saved layout \"{LayoutName.Trim()}\"";
        });
        ApplyLayoutCommand = new RelayCommand(parameter => _ = ApplyLayoutAsync(Parse(parameter)));
        DeleteLayoutCommand = new RelayCommand(parameter => _ = DeleteLayoutAsync(Parse(parameter)));
        _controller.StateChanged += Pull;
    }

    public ObservableCollection<WindowItemViewModel> Windows { get; }
    public ObservableCollection<GuideItemViewModel> Guides { get; }
    public ObservableCollection<WindowItemViewModel> SearchResults { get; }
    public ObservableCollection<LayoutItemViewModel> Layouts { get; }
    public RelayCommand ToggleOverviewCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand FitCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand SaveLayoutCommand { get; }
    public RelayCommand ApplyLayoutCommand { get; }
    public RelayCommand DeleteLayoutCommand { get; }

    /// <summary>Top-left of the virtual screen. Overview-local coordinates + this = viewport coordinates.</summary>
    public double OriginX { get; private set; }
    public double OriginY { get; private set; }
    public double OverviewWidth { get; private set; } = 1600;
    public double OverviewHeight { get; private set; } = 900;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
                UpdateSearch();
        }
    }

    public string LayoutName { get => _layoutName; set => Set(ref _layoutName, value); }
    public bool SearchOpen { get => _searchOpen; private set => Set(ref _searchOpen, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string ZoomLabel { get => _zoomLabel; set => Set(ref _zoomLabel, value); }
    public bool IsOverview
    {
        get => _isOverview;
        private set
        {
            if (Set(ref _isOverview, value))
            {
                OnPropertyChanged(nameof(ModeLabel));
                OnPropertyChanged(nameof(OverviewButtonLabel));
            }
        }
    }

    public string ModeLabel => IsOverview ? "Overview" : "Desktop";
    public string OverviewButtonLabel => IsOverview ? "Back to desktop" : "Overview";
    public RelayCommand OpenSettingsCommand { get; }
    public event Action? SettingsRequested;
    public bool GlobalPanEnabled => _controller.Session.Preferences.EnableGlobalPan;
    public UserPreferences Preferences => _controller.Session.Preferences;
    public CanvasController Controller => _controller;

    public void Pull()
    {
        var session = _controller.Session;
        OverviewWidth = Math.Max(1, session.VirtualScreen.Width);
        OverviewHeight = Math.Max(1, session.VirtualScreen.Height);
        OriginX = session.VirtualScreen.X;
        OriginY = session.VirtualScreen.Y;
        IsOverview = session.OverviewActive;
        ZoomLabel = $"{session.Viewport.Scale * 100:0}%";
        var query = SearchText.Trim();
        var seenWindows = new HashSet<Guid>();
        foreach (var window in session.Windows)
        {
            seenWindows.Add(window.Id);
            if (!_windows.TryGetValue(window.Id, out var item))
            {
                item = new WindowItemViewModel { Id = window.Id };
                _windows[window.Id] = item;
                Windows.Add(item);
            }

            var screen = session.Viewport.CanvasToScreen(window.CanvasBounds);
            item.Hwnd = window.Hwnd;
            item.ExecutablePath = window.ExecutablePath;
            item.Title = string.IsNullOrWhiteSpace(window.Title) ? window.ProcessName : window.Title;
            item.ProcessName = window.ProcessName;
            item.ViewLeft = screen.X - OriginX;
            item.ViewTop = screen.Y - OriginY;
            item.ViewWidth = Math.Max(36, screen.Width);
            item.ViewHeight = Math.Max(28, screen.Height);
            item.IsSelected = session.Selection.Contains(window.Id);
            item.IsPinned = window.IsPinned;
            item.IsPlaceholder = window.IsPlaceholder || window.Hwnd == 0;
            item.IsMinimized = window.IsMinimized;
            item.ShowFallback = item.IsPlaceholder || !_liveThumbnails.Contains(window.Hwnd);
            item.IsDimmed = query.Length > 0 && !Matches(window, query);
            item.Icon ??= IconFor(window.ExecutablePath);
            UpdateSnapshot(item, window);
        }

        foreach (var stale in _windows.Keys.Where(id => !seenWindows.Contains(id)).ToList())
        {
            Windows.Remove(_windows[stale]);
            _windows.Remove(stale);
        }

        Guides.Clear();
        foreach (var guide in session.ActiveGuides)
        {
            var (x, y) = session.Viewport.CanvasToScreen(guide.Vertical ? guide.Position : 0, guide.Vertical ? 0 : guide.Position);
            Guides.Add(new GuideItemViewModel
            {
                Vertical = guide.Vertical,
                Position = guide.Vertical ? x - OriginX : y - OriginY,
                Length = guide.Vertical ? OverviewHeight : OverviewWidth
            });
        }

        UpdateSearch();
        var running = session.Windows.Count(window => window.Hwnd != 0);
        Status = session.OverviewActive
            ? $"{ZoomLabel} · click a window to zoom into it · Esc to return"
            : $"{running} windows · Ctrl+Alt+scroll to zoom out";
        OnPropertyChanged(nameof(GlobalPanEnabled));
    }

    public void SetLiveThumbnails(HashSet<nint> live)
    {
        _liveThumbnails.Clear();
        foreach (var hwnd in live)
            _liveThumbnails.Add(hwnd);
        foreach (var item in Windows)
            item.ShowFallback = item.IsPlaceholder || item.Hwnd == 0 || !_liveThumbnails.Contains(item.Hwnd);
    }

    public async Task ReloadLayoutsAsync()
    {
        var layouts = await _controller.LayoutsAsync();
        Layouts.Clear();
        foreach (var layout in layouts)
        {
            Layouts.Add(new LayoutItemViewModel
            {
                Id = layout.Id,
                Name = layout.Name,
                Created = layout.CreatedAt.ToLocalTime().ToString("g")
            });
        }
    }

    public async Task ApplyLayoutAsync(Guid id)
    {
        await _controller.RestoreLayoutAsync(id);
        var name = Layouts.FirstOrDefault(layout => layout.Id == id)?.Name;
        if (name is not null)
            Status = $"Applied layout \"{name}\"";
    }

    public async Task DeleteLayoutAsync(Guid id)
    {
        await _controller.DeleteLayoutAsync(id);
        await ReloadLayoutsAsync();
    }

    public void Activate(Guid id) => _controller.ActivateWindow(id);

    public void Select(Guid id, bool append) => _controller.Select(id, append);

    /// <summary>Overview-local pixels to canvas. The viewport works in absolute screen coordinates.</summary>
    public CanvasPoint ScreenToCanvas(double x, double y) =>
        _controller.Session.Viewport.ScreenToCanvas(x + OriginX, y + OriginY);

    public IReadOnlyDictionary<Guid, CanvasPoint> CaptureOrigins(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        return _controller.Session.Windows
            .Where(window => set.Contains(window.Id))
            .ToDictionary(window => window.Id, window => new CanvasPoint(window.CanvasX, window.CanvasY));
    }

    public IReadOnlyList<Guid> SelectedIds() => _controller.Session.Selection.ToList();

    private void UpdateSearch()
    {
        SearchResults.Clear();
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            foreach (var item in Windows)
                item.IsDimmed = false;
            SearchOpen = false;
            return;
        }

        foreach (var item in Windows)
        {
            var match = item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        item.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase);
            item.IsDimmed = !match;
            if (match)
                SearchResults.Add(item);
        }

        SearchOpen = SearchResults.Count > 0;
    }

    private static bool Matches(ManagedWindow window, string query) =>
        window.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        window.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void UpdateSnapshot(WindowItemViewModel item, ManagedWindow window)
    {
        var snapshot = window.IsPlaceholder ? null : _snapshots.Get(window.Hwnd, window.ProcessId);
        if (snapshot is null)
        {
            if (item.Snapshot is not null)
                item.Snapshot = null;
            return;
        }

        if (item.Snapshot is not null && item.SnapshotTaken == snapshot.Taken)
            return;
        var image = BitmapSource.Create(snapshot.Width, snapshot.Height, 96, 96, PixelFormats.Bgra32, null, snapshot.Bgra, snapshot.Width * 4);
        image.Freeze();
        item.SnapshotTaken = snapshot.Taken;
        item.Snapshot = image;
    }

    private ImageSource? IconFor(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (_icons.TryGetValue(path, out var cached))
            return cached;
        var handle = FileIcon.LoadSmall(path);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            _icons[path] = image;
            return image;
        }
        finally
        {
            FileIcon.Destroy(handle);
        }
    }

    private static Guid Parse(object? parameter) =>
        parameter switch
        {
            Guid id => id,
            string text when Guid.TryParse(text, out var id) => id,
            _ => Guid.Empty
        };
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TheInfinity.App.ViewModels;
using TheInfinity.Core.Geometry;
using TheInfinity.Win32;

namespace TheInfinity.App.Views;

public partial class OverviewWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private IReadOnlyDictionary<Guid, CanvasPoint>? _origins;
    private Point _start;
    private Point _last;
    private bool _draggingWindow;
    private bool _moved;
    private Guid _clickTarget;
    private bool _panning;
    private bool _marquee;

    public OverviewWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.Controller.StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKey;
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle && _panning)
                EndGesture();
        };
        SizeChanged += (_, _) =>
        {
            UpdateRootMetrics();
            if (IsVisible)
                OnStateChanged();
        };
        DpiChanged += (_, _) => UpdateRootMetrics();
    }

    /// <summary>Raised after the overview is first shown so chrome windows can be raised above it.</summary>
    public event Action? Presented;

    public void OnStateChanged()
    {
        if (_viewModel.IsOverview)
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            if (!IsVisible)
            {
                LoadWallpaper();
                var screen = DisplayMetrics.VirtualScreen();
                NativeWindow.PlaceTopmost(hwnd, screen, show: false);
                Show();
                // WPF may re-apply its own size on first show, so place the exact pixel rectangle again.
                NativeWindow.PlaceTopmost(hwnd, screen, show: true);
                Activate();
                UpdateRootMetrics();
                Presented?.Invoke();
            }

            _viewModel.Controller.SetThumbnailDestination(hwnd);
            SyncThumbnails();
        }
        else if (IsVisible)
        {
            EndGesture();
            _viewModel.Controller.Thumbnails.Clear();
            Hide();
        }

        PaintGuides();
    }

    private bool _wallpaperLoaded;

    private void LoadWallpaper()
    {
        if (_wallpaperLoaded)
            return;
        _wallpaperLoaded = true;
        var path = DesktopWallpaper.FindPath();
        if (path is null)
            return;
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 3840;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            Wallpaper.Source = image;
        }
        catch
        {
            // Unreadable wallpaper: the plain dark background is fine.
        }
    }

    private void UpdateRootMetrics()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        var width = Math.Round(ActualWidth * scaleX);
        var height = Math.Round(ActualHeight * scaleY);
        if (Math.Abs(Root.Width - width) > 0.5 || double.IsNaN(Root.Width))
            Root.Width = width;
        if (Math.Abs(Root.Height - height) > 0.5 || double.IsNaN(Root.Height))
            Root.Height = height;
        if (Root.LayoutTransform is not ScaleTransform current || Math.Abs(current.ScaleX - 1 / scaleX) > 0.0001 || Math.Abs(current.ScaleY - 1 / scaleY) > 0.0001)
            Root.LayoutTransform = new ScaleTransform(1 / scaleX, 1 / scaleY);
    }

    private void SyncThumbnails()
    {
        if (double.IsNaN(Root.Width) || Root.Width <= 1)
            return;
        var items = new List<(nint Source, PixelRect Destination)>();
        foreach (var window in _viewModel.Windows)
        {
            // Minimized windows are included: Windows still draws live previews of them.
            if (window.Hwnd == 0 || window.IsPlaceholder)
                continue;
            if (window.ViewLeft + window.ViewWidth < 0 || window.ViewTop + window.ViewHeight < 0 ||
                window.ViewLeft > Root.Width || window.ViewTop > Root.Height)
                continue;
            // Inset by the 2 px tile border; the label lives above the tile, so no vertical reserve is needed.
            var x = (int)Math.Round(window.ViewLeft + 2);
            var y = (int)Math.Round(window.ViewTop + 2);
            var width = (int)Math.Round(Math.Max(2, window.ViewWidth - 4));
            var height = (int)Math.Round(Math.Max(2, window.ViewHeight - 4));
            items.Add((window.Hwnd, new PixelRect(x, y, width, height)));
        }

        _viewModel.SetLiveThumbnails(_viewModel.Controller.SyncThumbnails(items));
    }

    private void PaintGuides()
    {
        Guides.Children.Clear();
        var brush = new SolidColorBrush(Color.FromRgb(124, 156, 255));
        brush.Freeze();
        foreach (var guide in _viewModel.Guides)
        {
            var line = new Line { Stroke = brush, StrokeThickness = 1, IsHitTestVisible = false };
            if (guide.Vertical)
            {
                line.X1 = line.X2 = guide.Position;
                line.Y1 = 0;
                line.Y2 = Root.Height;
            }
            else
            {
                line.Y1 = line.Y2 = guide.Position;
                line.X1 = 0;
                line.X2 = Root.Width;
            }

            Guides.Children.Add(line);
        }
    }

    private void EndGesture()
    {
        if (Root.IsMouseCaptured)
            Root.ReleaseMouseCapture();
        if (_marquee)
            Marquee.Visibility = Visibility.Collapsed;
        _draggingWindow = _panning = _marquee = false;
        _origins = null;
    }

    // Mouse capture must be on Root: the move/up handlers are attached to Root, and a captured
    // Window would receive those events instead, leaving drags and pans dead.
    private void BeginGesture(MouseButtonEventArgs e)
    {
        _start = _last = e.GetPosition(Root);
        _moved = false;
        Root.CaptureMouse();
        e.Handled = true;
    }

    private void BackgroundDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not Grid)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _marquee = true;
            Marquee.Visibility = Visibility.Visible;
            Canvas.SetLeft(Marquee, e.GetPosition(Root).X);
            Canvas.SetTop(Marquee, e.GetPosition(Root).Y);
            Marquee.Width = Marquee.Height = 0;
        }
        else
        {
            _panning = true;
            _viewModel.ClearSelectionCommand.Execute(null);
        }

        BeginGesture(e);
    }

    private void BackgroundMove(object sender, MouseEventArgs e)
    {
        if (!Root.IsMouseCaptured)
            return;
        var position = e.GetPosition(Root);
        if (!_moved && Math.Abs(position.X - _start.X) + Math.Abs(position.Y - _start.Y) < 3)
            return;
        _moved = true;
        var scale = Math.Max(0.05, _viewModel.Controller.Session.Viewport.Scale);
        if (_draggingWindow && _origins is not null)
        {
            _viewModel.Controller.MoveWindows(_origins, (position.X - _start.X) / scale, (position.Y - _start.Y) / scale);
        }
        else if (_panning)
        {
            _viewModel.Controller.Pan(position.X - _last.X, position.Y - _last.Y);
        }
        else if (_marquee)
        {
            Canvas.SetLeft(Marquee, Math.Min(position.X, _start.X));
            Canvas.SetTop(Marquee, Math.Min(position.Y, _start.Y));
            Marquee.Width = Math.Abs(position.X - _start.X);
            Marquee.Height = Math.Abs(position.Y - _start.Y);
        }

        _last = position;
    }

    private void BackgroundUp(object sender, MouseButtonEventArgs e)
    {
        if (!Root.IsMouseCaptured)
            return;
        var click = _draggingWindow && !_moved ? _clickTarget : Guid.Empty;
        _clickTarget = Guid.Empty;
        if (_draggingWindow && _moved)
            _viewModel.Controller.FinishWindowDrag();
        if (_marquee)
        {
            var end = e.GetPosition(Root);
            var a = _viewModel.ScreenToCanvas(Math.Min(_start.X, end.X), Math.Min(_start.Y, end.Y));
            var b = _viewModel.ScreenToCanvas(Math.Max(_start.X, end.X), Math.Max(_start.Y, end.Y));
            _viewModel.Controller.Marquee(new CanvasRect(a.X, a.Y, b.X - a.X, b.Y - a.Y), append: false);
        }

        if (_panning && _moved)
            _viewModel.Controller.EndPan();
        EndGesture();
        if (click != Guid.Empty)
            _viewModel.Activate(click);
    }

    // Scroll zooms around the cursor (zooming all the way in lands back on the real desktop).
    // Shift+scroll pans sideways, Alt+scroll pans vertically.
    private void BackgroundWheel(object sender, MouseWheelEventArgs e)
    {
        var position = e.GetPosition(Root);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _viewModel.Controller.Pan(e.Delta, 0);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            _viewModel.Controller.Pan(0, e.Delta);
        else
            _viewModel.Controller.Zoom(position.X + _viewModel.OriginX, position.Y + _viewModel.OriginY, e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    private void WindowDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WindowItemViewModel item })
            return;
        var multi = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // A plain click (no drag) zooms into the window and focuses it; see BackgroundUp.
        _clickTarget = multi ? Guid.Empty : item.Id;
        if (!item.IsSelected || multi)
            _viewModel.Select(item.Id, multi);
        var ids = _viewModel.SelectedIds();
        _origins = ids.Contains(item.Id) ? _viewModel.CaptureOrigins(ids) : _viewModel.CaptureOrigins([item.Id]);
        _draggingWindow = true;
        BeginGesture(e);
    }

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;

        if (e.Key == Key.Escape)
        {
            _viewModel.Controller.ExitOverview();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var selected = _viewModel.SelectedIds().FirstOrDefault();
            if (selected != Guid.Empty)
                _viewModel.Activate(selected);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D0)
        {
            _viewModel.ResetCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.F)
        {
            _viewModel.Controller.RequestSearch();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
        {
            var factor = e.Key is Key.OemPlus or Key.Add ? 1.25 : 1 / 1.25;
            _viewModel.Controller.Zoom(Root.Width / 2 + _viewModel.OriginX, Root.Height / 2 + _viewModel.OriginY, factor);
            e.Handled = true;
            return;
        }

        var nudge = e.Key switch
        {
            Key.Left => (-24d, 0d),
            Key.Right => (24d, 0d),
            Key.Up => (0d, -24d),
            Key.Down => (0d, 24d),
            _ => ((double?)null, (double?)null)
        };
        if (nudge.Item1 is double dx && nudge.Item2 is double dy)
        {
            var ids = _viewModel.SelectedIds();
            if (ids.Count > 0)
                _viewModel.Controller.MoveWindows(_viewModel.CaptureOrigins(ids), dx, dy);
            else
                _viewModel.Controller.Pan(-dx * 4, -dy * 4);
            _viewModel.Controller.FinishWindowDrag();
            e.Handled = true;
        }
    }

    private void WindowMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement { DataContext: WindowItemViewModel window })
            return;
        var forget = menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Tag, "forget"));
        if (forget is not null)
            forget.Visibility = window.IsPlaceholder ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MenuActivate(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Activate(window.Id));
    private void MenuFrame(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Controller.JumpTo(window.Id, activate: false));
    private void MenuMinimize(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Controller.Minimize(window.Id));
    private void MenuMaximize(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Controller.Maximize(window.Id));
    private void MenuRestore(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Controller.Restore(window.Id));
    private void MenuClose(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Controller.Close(window.Id));
    private void MenuPin(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Controller.TogglePin(window.Id));
    private void MenuForget(object sender, RoutedEventArgs e) => WithMenuWindow(sender, window => _viewModel.Controller.Forget(window.Id));

    private static void WithMenuWindow(object sender, Action<WindowItemViewModel> action)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: WindowItemViewModel window } } })
            action(window);
    }

    // Fallback when the global middle-button hook is disabled in settings.
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && !_viewModel.GlobalPanEnabled && !Root.IsMouseCaptured)
        {
            _panning = true;
            BeginGesture(e);
        }
    }
}

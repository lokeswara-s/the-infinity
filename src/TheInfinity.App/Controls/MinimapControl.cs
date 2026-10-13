using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TheInfinity.Core.Geometry;

namespace TheInfinity.App.Controls;

public sealed class MinimapControl : FrameworkElement
{
    private MinimapProjection _projection;
    private bool _dragging;
    private bool _moved;
    private Point _last;

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<CanvasRect>), typeof(MinimapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewportWorldProperty = DependencyProperty.Register(
        nameof(ViewportWorld), typeof(CanvasRect), typeof(MinimapControl),
        new FrameworkPropertyMetadata(default(CanvasRect), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(CanvasRect), typeof(MinimapControl),
        new FrameworkPropertyMetadata(default(CanvasRect), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<CanvasRect>? Items
    {
        get => (IReadOnlyList<CanvasRect>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public CanvasRect ViewportWorld
    {
        get => (CanvasRect)GetValue(ViewportWorldProperty);
        set => SetValue(ViewportWorldProperty, value);
    }

    public CanvasRect Selected
    {
        get => (CanvasRect)GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public event Action<CanvasPoint>? Navigate;
    public event Action<double, double>? PanWorld;

    public MinimapControl()
    {
        Width = 220;
        Height = 148;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var background = new SolidColorBrush(Color.FromArgb(230, 18, 22, 30));
        background.Freeze();
        var border = new Pen(new SolidColorBrush(Color.FromRgb(70, 82, 104)), 1);
        border.Brush.Freeze();
        border.Freeze();
        drawingContext.DrawRoundedRectangle(background, border, new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);
        var items = Items ?? [];
        _projection = MinimapMath.Project(MinimapMath.WorldBounds(items, ViewportWorld), Math.Max(1, ActualWidth - 12), Math.Max(1, ActualHeight - 12));
        _projection = _projection with { OriginX = _projection.OriginX + 6, OriginY = _projection.OriginY + 6 };
        var fill = new SolidColorBrush(Color.FromArgb(180, 124, 156, 255));
        fill.Freeze();
        foreach (var item in items)
        {
            var rect = ToRect(MinimapMath.ToMinimap(item, _projection));
            drawingContext.DrawRectangle(fill, null, rect);
        }

        if (!Selected.IsEmpty)
        {
            var selected = ToRect(MinimapMath.ToMinimap(Selected, _projection));
            var pen = new Pen(Brushes.White, 1.4);
            pen.Freeze();
            drawingContext.DrawRectangle(null, pen, selected);
        }

        var viewport = ToRect(MinimapMath.ToMinimap(ViewportWorld, _projection));
        var viewPen = new Pen(new SolidColorBrush(Color.FromRgb(61, 220, 151)), 1.5);
        viewPen.Brush.Freeze();
        viewPen.Freeze();
        var viewFill = new SolidColorBrush(Color.FromArgb(40, 61, 220, 151));
        viewFill.Freeze();
        drawingContext.DrawRectangle(viewFill, viewPen, viewport);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _last = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed || _projection.Scale <= 0)
            return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _last.X) + Math.Abs(point.Y - _last.Y) < 3 && !_moved)
            return;
        _moved = true;
        var dx = (point.X - _last.X) / _projection.Scale;
        var dy = (point.Y - _last.Y) / _projection.Scale;
        _last = point;
        PanWorld?.Invoke(dx, dy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging && !_moved)
            NavigateTo(e.GetPosition(this));
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void NavigateTo(Point point)
    {
        var world = MinimapMath.ToWorld(point.X, point.Y, _projection);
        Navigate?.Invoke(world);
    }

    private static Rect ToRect(CanvasRect rect) => new(rect.X, rect.Y, Math.Max(2, rect.Width), Math.Max(2, rect.Height));
}

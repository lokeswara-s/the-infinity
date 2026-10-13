namespace TheInfinity.Core.Geometry;

/// <summary>
/// Turns raw pointer positions into pan deltas. The pointer must keep moving normally
/// while the gesture runs, so deltas are taken from real cursor positions.
/// </summary>
public sealed class DragGesture
{
    public const int Threshold = 4;

    private bool _down;
    private bool _dragging;
    private int _startX;
    private int _startY;
    private int _lastX;
    private int _lastY;

    public bool IsDown => _down;
    public bool IsDragging => _dragging;

    public void Begin(int x, int y)
    {
        _down = true;
        _dragging = false;
        _startX = _lastX = x;
        _startY = _lastY = y;
    }

    public bool TryMove(int x, int y, out int dx, out int dy)
    {
        dx = dy = 0;
        if (!_down)
            return false;

        if (!_dragging)
        {
            if (Math.Abs(x - _startX) + Math.Abs(y - _startY) < Threshold)
                return false;
            _dragging = true;
        }

        dx = x - _lastX;
        dy = y - _lastY;
        _lastX = x;
        _lastY = y;
        return dx != 0 || dy != 0;
    }

    /// <summary>Ends the gesture. Returns true when it was a drag, false for a plain click.</summary>
    public bool End()
    {
        var wasDrag = _dragging;
        _down = false;
        _dragging = false;
        return wasDrag;
    }
}

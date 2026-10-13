using System.Windows.Media;
using TheInfinity.Core.Geometry;

namespace TheInfinity.App.Services;

/// <summary>Eases the viewport offset to a target over a short time, one frame at a time.</summary>
internal sealed class ViewportAnimator
{
    private const double DurationMs = 280;

    private readonly ViewportTransform _viewport;
    private readonly Action _frame;
    private EventHandler? _onRender;
    private long _startedAt;
    private double _fromX, _fromY, _toX, _toY;

    /// <param name="viewport">The viewport whose offset is animated.</param>
    /// <param name="frame">Called after each step so the caller can react to the new viewport.</param>
    public ViewportAnimator(ViewportTransform viewport, Action frame)
    {
        _viewport = viewport;
        _frame = frame;
    }

    /// <summary>Starts (or retargets) an animation from the current offset to the given one.</summary>
    public void AnimateTo(double offsetX, double offsetY)
    {
        _fromX = _viewport.OffsetX;
        _fromY = _viewport.OffsetY;
        _toX = offsetX;
        _toY = offsetY;
        _startedAt = Environment.TickCount64;
        if (_onRender is not null)
            return;

        _onRender = (_, _) => Step();
        CompositionTarget.Rendering += _onRender;
    }

    public void Cancel()
    {
        if (_onRender is null)
            return;
        CompositionTarget.Rendering -= _onRender;
        _onRender = null;
    }

    private void Step()
    {
        var t = Math.Clamp((Environment.TickCount64 - _startedAt) / DurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        _viewport.OffsetX = _fromX + (_toX - _fromX) * eased;
        _viewport.OffsetY = _fromY + (_toY - _fromY) * eased;
        if (t >= 1)
            Cancel();
        _frame();
    }
}

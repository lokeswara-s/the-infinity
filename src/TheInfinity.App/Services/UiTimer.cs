using System.Windows.Threading;

namespace TheInfinity.App.Services;

/// <summary>A restartable <see cref="DispatcherTimer"/>. Create it on the UI thread.</summary>
internal sealed class UiTimer
{
    private readonly DispatcherTimer _timer;

    public UiTimer(TimeSpan interval, Action tick)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => tick();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }
}

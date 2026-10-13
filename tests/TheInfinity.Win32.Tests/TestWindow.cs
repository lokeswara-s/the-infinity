using System.Drawing;
using System.Windows.Forms;

namespace TheInfinity.Win32.Tests;

/// <summary>Marks a test that needs a real interactive desktop. Set <c>INFINITY_SKIP_DESKTOP_TESTS=1</c> to skip them.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (!Environment.UserInteractive)
            Skip = "Needs an interactive desktop session.";
        else if (Environment.GetEnvironmentVariable("INFINITY_SKIP_DESKTOP_TESTS") == "1")
            Skip = "Skipped by INFINITY_SKIP_DESKTOP_TESTS.";
    }
}

/// <summary>A real top-level window on its own UI thread, shown without taking focus from the user.</summary>
internal sealed class TestWindow : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _shown = new();
    private Form? _form;

    public TestWindow(string title, Rectangle bounds)
    {
        _thread = new Thread(() =>
        {
            _form = new QuietForm { Text = title, StartPosition = FormStartPosition.Manual, Bounds = bounds };
            _form.Shown += (_, _) => _shown.Set();
            Application.Run(_form);
        })
        {
            IsBackground = true
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_shown.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The test window did not appear.");
    }

    public nint Handle => Read(form => form.Handle);
    public Rectangle Bounds => Read(form => form.Bounds);
    public FormWindowState State => Read(form => form.WindowState);
    public bool Exists => _form is { IsDisposed: false };

    private T Read<T>(Func<Form, T> read)
    {
        var form = _form ?? throw new InvalidOperationException("The test window is gone.");
        return (T)form.Invoke(() => read(form))!;
    }

    public void Dispose()
    {
        if (_form is { IsDisposed: false } form)
        {
            try
            {
                form.Invoke(form.Close);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private sealed class QuietForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
}

internal static class Wait
{
    /// <summary>Polls until the condition holds, because Windows applies some window changes a moment later.</summary>
    public static bool Until(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(25);
        }

        return condition();
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace TheInfinity.Win32.Tests;

public class WindowDriverTests
{
    private static WindowDriver NewDriver(out WindowSnapshotCache snapshots)
    {
        snapshots = new WindowSnapshotCache();
        return new WindowDriver(snapshots);
    }

    [DesktopFact]
    public void Move_places_the_window_at_the_requested_rectangle()
    {
        var driver = NewDriver(out _);
        using var window = new TestWindow("Infinity test: move", new Rectangle(120, 120, 420, 300));

        driver.Move(window.Handle, 200, 150, 520, 360);

        Assert.True(Wait.Until(() => window.Bounds == new Rectangle(200, 150, 520, 360)),
            $"Window ended up at {window.Bounds}.");
    }

    [DesktopFact]
    public void Minimize_keeps_a_still_image_and_Restore_brings_the_window_back()
    {
        var driver = NewDriver(out var snapshots);
        using var window = new TestWindow("Infinity test: minimize", new Rectangle(120, 120, 420, 300));
        var handle = window.Handle;

        driver.Minimize(handle);

        Assert.True(Wait.Until(() => window.State == FormWindowState.Minimized));
        var still = snapshots.Get(handle, Environment.ProcessId);
        Assert.NotNull(still);
        Assert.InRange(still.Width, 40, WindowSnapshotCache.MaxWidth);
        Assert.Equal(still.Width * still.Height * 4, still.Bgra.Length);

        driver.Restore(handle);
        Assert.True(Wait.Until(() => window.State == FormWindowState.Normal));
    }

    [DesktopFact]
    public void IsAlive_needs_both_a_live_window_and_the_right_process()
    {
        var driver = NewDriver(out _);
        nint handle;
        using (var window = new TestWindow("Infinity test: alive", new Rectangle(120, 120, 420, 300)))
        {
            handle = window.Handle;
            Assert.True(driver.IsAlive(handle, Environment.ProcessId));
            Assert.False(driver.IsAlive(handle, Environment.ProcessId + 1));
        }

        Assert.False(driver.IsAlive(handle, Environment.ProcessId));
    }

    [DesktopFact]
    public void The_app_never_observes_its_own_process()
    {
        // The workspace must not try to manage its own overview, command bar or settings windows.
        var driver = NewDriver(out _);
        using var window = new TestWindow("Infinity test: own process", new Rectangle(120, 120, 420, 300));

        Assert.Null(driver.Observe(window.Handle));
    }

    [Fact]
    public void Calls_with_no_window_are_ignored()
    {
        var driver = NewDriver(out var snapshots);

        driver.Move(0, 0, 0, 100, 100);
        driver.Minimize(0);
        driver.Restore(0);
        driver.Close(0);
        driver.Activate(0);

        Assert.Null(snapshots.Get(0, 0));
        Assert.False(driver.IsAlive(0, 0));
    }
}

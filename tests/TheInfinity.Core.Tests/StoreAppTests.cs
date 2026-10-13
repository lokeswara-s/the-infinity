using TheInfinity.Core.Models;
using TheInfinity.Core.Services;

namespace TheInfinity.Core.Tests;

public class StoreAppTests
{
    private const string Host = @"C:\Windows\System32\ApplicationFrameHost.exe";

    private static WindowObservation Store(nint hwnd, string title) =>
        new(hwnd, 100 + (int)hwnd, "ApplicationFrameHost", Host, "ApplicationFrameWindow", title,
            100, 100, 500, 600, 100, 100, 500, 600, false, false, true, null);

    [Fact]
    public void Every_store_app_window_is_tracked()
    {
        var session = new WorkspaceSession();

        session.Synchronize([Store(1, "Calculator"), Store(2, "Settings")], restoreSavedPositions: true);

        Assert.Equal(2, session.Windows.Count);
    }
}

using TheInfinity.Core.Models;
using TheInfinity.Persistence;

namespace TheInfinity.Core.Tests;

public class SqliteWorkspaceStoreTests
{
    [Fact]
    public async Task Save_and_load_round_trip_without_treating_hwnd_as_identity()
    {
        var path = Path.Combine(Path.GetTempPath(), "infinity-tests", Guid.NewGuid().ToString("N"), "workspace.db");
        var store = new SqliteWorkspaceStore(path);
        await store.InitializeAsync();
        var window = new ManagedWindow
        {
            Hwnd = 999,
            ProcessId = 42,
            ProcessName = "Code",
            ExecutablePath = @"C:\Apps\Code.exe",
            WindowClass = "Chrome_WidgetWin_1",
            Title = "Program.cs",
            MatchKey = "code",
            CanvasX = 320,
            CanvasY = -40,
            Width = 900,
            Height = 700,
            IsPinned = true,
            PinScreenX = 12,
            PinScreenY = 18,
            PinWidth = 900,
            PinHeight = 700
        };
        var state = new Core.Persistence.WorkspaceState
        {
            Workspace = new WorkspaceModel { Name = "Main" },
            Windows = [window],
            ViewportOffsetX = 15,
            ViewportOffsetY = 25,
            ViewportScale = 1,
            Preferences = new UserPreferences { SnapEnabled = false, LaunchMissingApps = false },
            CleanShutdown = true
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();
        var restored = Assert.Single(loaded.Windows);
        Assert.Equal(0, restored.Hwnd);
        Assert.True(restored.IsPlaceholder);
        Assert.Equal(320, restored.CanvasX);
        Assert.Equal("Code", restored.ProcessName);
        Assert.False(loaded.Preferences.SnapEnabled);
        Assert.False(loaded.Preferences.LaunchMissingApps);
        Assert.Equal(15, loaded.ViewportOffsetX);

        var layoutId = await store.SaveLayoutAsync("Morning", state);
        var layout = await store.LoadLayoutAsync(layoutId);
        Assert.NotNull(layout);
        Assert.Equal(0, Assert.Single(layout.Windows).Hwnd);
    }

    [Fact]
    public async Task Saving_a_layout_under_the_same_name_replaces_it_and_layouts_can_be_deleted()
    {
        var path = Path.Combine(Path.GetTempPath(), "infinity-tests", Guid.NewGuid().ToString("N"), "workspace.db");
        var store = new SqliteWorkspaceStore(path);
        await store.InitializeAsync();
        var state = await store.LoadAsync();

        await store.SaveLayoutAsync("Morning", state);
        var second = await store.SaveLayoutAsync("morning", state);
        var other = await store.SaveLayoutAsync("Focus", state);

        var layouts = await store.ListLayoutsAsync();
        Assert.Equal(2, layouts.Count);
        Assert.Contains(layouts, layout => layout.Id == second);

        await store.DeleteLayoutAsync(other);
        Assert.Equal(second, Assert.Single(await store.ListLayoutsAsync()).Id);
    }
}
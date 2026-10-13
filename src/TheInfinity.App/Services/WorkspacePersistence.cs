using Microsoft.Extensions.Logging;
using TheInfinity.Core.Persistence;
using TheInfinity.Core.Services;

namespace TheInfinity.App.Services;

/// <summary>Loads and saves the workspace, debounces autosave, and manages named layouts.</summary>
internal sealed class WorkspacePersistence
{
    private readonly IWorkspaceStore _store;
    private readonly WorkspaceSession _session;
    private readonly ILogger _logger;
    private readonly UiTimer _autosave;
    private bool _saveQueued;

    public WorkspacePersistence(IWorkspaceStore store, WorkspaceSession session, ILogger logger)
    {
        _store = store;
        _session = session;
        _logger = logger;
        _autosave = new UiTimer(TimeSpan.FromMilliseconds(1200), FlushAutosave);
    }

    public string DatabasePath => _store.DatabasePath;

    /// <summary>Autosave only runs once startup has finished, so a half-loaded workspace is never written.</summary>
    public bool Started { get; set; }

    public async Task<WorkspaceState> OpenAsync()
    {
        await _store.InitializeAsync();
        return await _store.LoadAsync();
    }

    /// <summary>Schedules a save shortly from now. Repeated calls restart the wait.</summary>
    public void QueueSave()
    {
        if (!Started || !_session.Preferences.AutoSave)
            return;
        _saveQueued = true;
        _autosave.Restart();
    }

    public async Task SaveAsync(bool clean)
    {
        _session.CleanShutdown = clean;
        await _store.SaveAsync(Capture(clean));
    }

    public void StopAutosave() => _autosave.Stop();

    public async Task SaveLayoutAsync(string name) =>
        await _store.SaveLayoutAsync(name, Capture(clean: false));

    public async Task<IReadOnlyList<StoredLayout>> ListLayoutsAsync() => await _store.ListLayoutsAsync();

    public async Task<WorkspaceState?> LoadLayoutAsync(Guid id) => await _store.LoadLayoutAsync(id);

    public async Task DeleteLayoutAsync(Guid id) => await _store.DeleteLayoutAsync(id);

    private async void FlushAutosave()
    {
        _autosave.Stop();
        if (!_saveQueued)
            return;
        _saveQueued = false;
        try
        {
            await SaveAsync(clean: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic save failed.");
        }
    }

    private WorkspaceState Capture(bool clean) => new()
    {
        Workspace = _session.Workspace,
        Preferences = _session.Preferences,
        Windows = _session.Windows.ToList(),
        ViewportOffsetX = _session.Viewport.OffsetX,
        ViewportOffsetY = _session.Viewport.OffsetY,
        ViewportScale = _session.Viewport.Scale,
        CleanShutdown = clean
    };
}

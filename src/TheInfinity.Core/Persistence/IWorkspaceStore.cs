using TheInfinity.Core.Models;

namespace TheInfinity.Core.Persistence;

public sealed class WorkspaceState
{
    public WorkspaceModel Workspace { get; set; } = new();
    public UserPreferences Preferences { get; set; } = new();
    public List<ManagedWindow> Windows { get; set; } = [];
    public double ViewportOffsetX { get; set; }
    public double ViewportOffsetY { get; set; }
    public double ViewportScale { get; set; } = 1;
    public bool CleanShutdown { get; set; } = true;
    public List<StoredLayout> Layouts { get; set; } = [];
}

public sealed class StoredLayout
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public WorkspaceState State { get; set; } = new();
}

public interface IWorkspaceStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default);
    Task<Guid> SaveLayoutAsync(string name, WorkspaceState state, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredLayout>> ListLayoutsAsync(CancellationToken cancellationToken = default);
    Task DeleteLayoutAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkspaceState?> LoadLayoutAsync(Guid id, CancellationToken cancellationToken = default);
    string DatabasePath { get; }
}

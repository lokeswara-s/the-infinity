using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheInfinity.Core.Models;
using TheInfinity.Core.Persistence;

namespace TheInfinity.Persistence;

public sealed class SqliteWorkspaceStore : IWorkspaceStore
{
    private static readonly string[] MigrationV1 =
    [
        """
        CREATE TABLE workspaces (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            is_active INTEGER NOT NULL,
            offset_x REAL NOT NULL,
            offset_y REAL NOT NULL,
            scale REAL NOT NULL,
            updated_at TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE placements (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            match_key TEXT NOT NULL,
            process_name TEXT NOT NULL,
            executable_path TEXT NULL,
            window_class TEXT NOT NULL,
            title TEXT NOT NULL,
            canvas_x REAL NOT NULL,
            canvas_y REAL NOT NULL,
            width REAL NOT NULL,
            height REAL NOT NULL,
            group_id TEXT NULL,
            is_pinned INTEGER NOT NULL,
            pin_x INTEGER NOT NULL,
            pin_y INTEGER NOT NULL,
            pin_w INTEGER NOT NULL,
            pin_h INTEGER NOT NULL,
            last_active TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE groups_ (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            name TEXT NOT NULL,
            color TEXT NOT NULL,
            x REAL NOT NULL,
            y REAL NOT NULL,
            width REAL NOT NULL,
            height REAL NOT NULL
        )
        """,
        """
        CREATE TABLE preferences (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE layouts (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            created_at TEXT NOT NULL,
            payload TEXT NOT NULL
        )
        """
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteTransaction? _transaction;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };
    private SqliteConnection? _connection;

    public SqliteWorkspaceStore(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheInfinity",
            "workspace.db");
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadWriteCreate;Cache=Shared");
            await _connection.OpenAsync(cancellationToken);
            var version = await ScalarIntAsync("PRAGMA user_version;");
            if (version < 1)
            {
                foreach (var statement in MigrationV1)
                    await ExecuteAsync(statement);
                await ExecuteAsync("PRAGMA user_version = 1;");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = new WorkspaceState();
            await using var workspaceCmd = _connection!.CreateCommand();
            workspaceCmd.CommandText = "SELECT id, name, is_active, offset_x, offset_y, scale FROM workspaces WHERE is_active = 1 LIMIT 1";
            await using var reader = await workspaceCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                state.Workspace = new WorkspaceModel
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Name = reader.GetString(1),
                    IsActive = reader.GetInt32(2) != 0
                };
                state.ViewportOffsetX = reader.GetDouble(3);
                state.ViewportOffsetY = reader.GetDouble(4);
                state.ViewportScale = reader.GetDouble(5);
            }

            state.Windows = await LoadPlacementsAsync(state.Workspace.Id, cancellationToken);
            state.Preferences = await LoadPreferencesAsync(cancellationToken);
            state.CleanShutdown = await ReadPrefAsync("clean_shutdown", "true") == "true";
            state.Layouts = (await ListLayoutsCoreAsync(cancellationToken)).ToList();
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = await _connection!.BeginTransactionAsync(cancellationToken);
            _transaction = (SqliteTransaction)tx;
            await ExecuteAsync("DELETE FROM placements WHERE workspace_id = $id", ("$id", state.Workspace.Id.ToString()));
            // Groups were removed from the app. The table and column stay so older databases still open; old rows are cleared here.
            await ExecuteAsync("DELETE FROM groups_ WHERE workspace_id = $id", ("$id", state.Workspace.Id.ToString()));
            await ExecuteAsync("DELETE FROM workspaces WHERE id = $id", ("$id", state.Workspace.Id.ToString()));
            await ExecuteAsync(
                """
                INSERT INTO workspaces (id, name, is_active, offset_x, offset_y, scale, updated_at)
                VALUES ($id, $name, 1, $ox, $oy, $scale, $updated)
                """,
                ("$id", state.Workspace.Id.ToString()),
                ("$name", state.Workspace.Name),
                ("$ox", state.ViewportOffsetX),
                ("$oy", state.ViewportOffsetY),
                ("$scale", state.ViewportScale),
                ("$updated", DateTimeOffset.Now.ToString("O")));

            foreach (var window in state.Windows)
            {
                await ExecuteAsync(
                    """
                    INSERT INTO placements (id, workspace_id, match_key, process_name, executable_path, window_class, title,
                        canvas_x, canvas_y, width, height, is_pinned, pin_x, pin_y, pin_w, pin_h, last_active)
                    VALUES ($id, $ws, $key, $proc, $exe, $cls, $title, $x, $y, $w, $h, $pin, $px, $py, $pw, $ph, $active)
                    """,
                    ("$id", window.Id.ToString()),
                    ("$ws", state.Workspace.Id.ToString()),
                    ("$key", window.MatchKey),
                    ("$proc", window.ProcessName),
                    ("$exe", (object?)window.ExecutablePath ?? DBNull.Value),
                    ("$cls", window.WindowClass),
                    ("$title", window.Title),
                    ("$x", window.CanvasX),
                    ("$y", window.CanvasY),
                    ("$w", window.Width),
                    ("$h", window.Height),
                    ("$pin", window.IsPinned ? 1 : 0),
                    ("$px", window.PinScreenX),
                    ("$py", window.PinScreenY),
                    ("$pw", window.PinWidth),
                    ("$ph", window.PinHeight),
                    ("$active", window.LastActive.ToString("O")));
            }

            await WritePreferencesAsync(state);
            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _transaction = null;
            _gate.Release();
        }
    }

    public async Task<Guid> SaveLayoutAsync(string name, WorkspaceState state, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new LayoutPayload
        {
            Windows = state.Windows,
            OffsetX = state.ViewportOffsetX,
            OffsetY = state.ViewportOffsetY,
            Scale = state.ViewportScale
        }, _json);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Saving under an existing name replaces that layout instead of piling up duplicates.
            await ExecuteAsync("DELETE FROM layouts WHERE name = $name COLLATE NOCASE", ("$name", name));
            await ExecuteAsync(
                "INSERT INTO layouts (id, name, created_at, payload) VALUES ($id, $name, $at, $payload)",
                ("$id", id.ToString()),
                ("$name", name),
                ("$at", DateTimeOffset.Now.ToString("O")),
                ("$payload", payload));
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteLayoutAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync("DELETE FROM layouts WHERE id = $id", ("$id", id.ToString()));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StoredLayout>> ListLayoutsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ListLayoutsCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceState?> LoadLayoutAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection!.CreateCommand();
            command.CommandText = "SELECT payload FROM layouts WHERE id = $id";
            command.Parameters.AddWithValue("$id", id.ToString());
            var payload = (string?)await command.ExecuteScalarAsync(cancellationToken);
            if (string.IsNullOrEmpty(payload))
                return null;
            var layout = JsonSerializer.Deserialize<LayoutPayload>(payload, _json);
            if (layout is null)
                return null;
            foreach (var window in layout.Windows)
            {
                window.Hwnd = 0;
                window.IsPlaceholder = true;
            }

            return new WorkspaceState
            {
                Windows = layout.Windows,
                ViewportOffsetX = layout.OffsetX,
                ViewportOffsetY = layout.OffsetY,
                ViewportScale = layout.Scale
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<StoredLayout>> ListLayoutsCoreAsync(CancellationToken cancellationToken)
    {
        var list = new List<StoredLayout>();
        await using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT id, name, created_at, payload FROM layouts ORDER BY created_at DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = JsonSerializer.Deserialize<LayoutPayload>(reader.GetString(3), _json) ?? new LayoutPayload();
            foreach (var window in payload.Windows)
            {
                window.Hwnd = 0;
                window.IsPlaceholder = true;
            }

            list.Add(new StoredLayout
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(2)),
                State = new WorkspaceState
                {
                    Windows = payload.Windows,
                    ViewportOffsetX = payload.OffsetX,
                    ViewportOffsetY = payload.OffsetY,
                    ViewportScale = payload.Scale
                }
            });
        }

        return list;
    }

    private async Task<List<ManagedWindow>> LoadPlacementsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var list = new List<ManagedWindow>();
        await using var command = _connection!.CreateCommand();
        command.CommandText = """
            SELECT id, match_key, process_name, executable_path, window_class, title, canvas_x, canvas_y, width, height,
                   is_pinned, pin_x, pin_y, pin_w, pin_h, last_active
            FROM placements WHERE workspace_id = $id
            """;
        command.Parameters.AddWithValue("$id", workspaceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ManagedWindow
            {
                Id = Guid.Parse(reader.GetString(0)),
                Hwnd = 0,
                IsPlaceholder = true,
                MatchKey = reader.GetString(1),
                ProcessName = reader.GetString(2),
                ExecutablePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                WindowClass = reader.GetString(4),
                Title = reader.GetString(5),
                CanvasX = reader.GetDouble(6),
                CanvasY = reader.GetDouble(7),
                Width = reader.GetDouble(8),
                Height = reader.GetDouble(9),
                IsPinned = reader.GetInt32(10) != 0,
                PinScreenX = reader.GetInt32(11),
                PinScreenY = reader.GetInt32(12),
                PinWidth = reader.GetInt32(13),
                PinHeight = reader.GetInt32(14),
                LastActive = DateTimeOffset.Parse(reader.GetString(15))
            });
        }

        return list;
    }

    private async Task<UserPreferences> LoadPreferencesAsync(CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT key, value FROM preferences";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            map[reader.GetString(0)] = reader.GetString(1);

        var defaults = new UserPreferences();
        return new UserPreferences
        {
            MinZoom = ReadDouble(map, "min_zoom", defaults.MinZoom),
            SnapEnabled = ReadBool(map, "snap", defaults.SnapEnabled),
            SnapThreshold = ReadDouble(map, "snap_threshold", defaults.SnapThreshold),
            ShowPlaceholders = ReadBool(map, "placeholders", defaults.ShowPlaceholders),
            RestoreLayoutOnStartup = ReadBool(map, "restore", defaults.RestoreLayoutOnStartup),
            LaunchMissingApps = ReadBool(map, "launch_missing", defaults.LaunchMissingApps),
            StartWithWindows = ReadBool(map, "start_with_windows", defaults.StartWithWindows),
            AutoSave = ReadBool(map, "autosave", defaults.AutoSave),
            EnableGlobalPan = ReadBool(map, "global_pan", defaults.EnableGlobalPan)
        };
    }

    private async Task WritePreferencesAsync(WorkspaceState state)
    {
        var prefs = state.Preferences;
        await WritePrefAsync("min_zoom", prefs.MinZoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await WritePrefAsync("snap", prefs.SnapEnabled ? "true" : "false");
        await WritePrefAsync("snap_threshold", prefs.SnapThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await WritePrefAsync("placeholders", prefs.ShowPlaceholders ? "true" : "false");
        await WritePrefAsync("restore", prefs.RestoreLayoutOnStartup ? "true" : "false");
        await WritePrefAsync("launch_missing", prefs.LaunchMissingApps ? "true" : "false");
        await WritePrefAsync("start_with_windows", prefs.StartWithWindows ? "true" : "false");
        await WritePrefAsync("autosave", prefs.AutoSave ? "true" : "false");
        await WritePrefAsync("global_pan", prefs.EnableGlobalPan ? "true" : "false");
        await WritePrefAsync("clean_shutdown", state.CleanShutdown ? "true" : "false");
    }

    private async Task WritePrefAsync(string key, string value) =>
        await ExecuteAsync(
            "INSERT INTO preferences (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$k", key),
            ("$v", value));

    private async Task<string> ReadPrefAsync(string key, string fallback)
    {
        await using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT value FROM preferences WHERE key = $k";
        command.Parameters.AddWithValue("$k", key);
        var value = (string?)await command.ExecuteScalarAsync();
        return value ?? fallback;
    }

    private async Task<int> ScalarIntAsync(string sql)
    {
        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static double ReadDouble(Dictionary<string, string> map, string key, double fallback) =>
        map.TryGetValue(key, out var value) && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static bool ReadBool(Dictionary<string, string> map, string key, bool fallback) =>
        map.TryGetValue(key, out var value) ? value == "true" : fallback;

    private sealed class LayoutPayload
    {
        public List<ManagedWindow> Windows { get; set; } = [];
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double Scale { get; set; } = 1;
    }
}

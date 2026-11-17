using System.Text.Json;
using HermesDesktop.Core.Models;
using Microsoft.Data.Sqlite;

namespace HermesDesktop.Core.Storage;

public sealed record AppSettingsData
{
    public Guid? ActiveDashboardId { get; set; }
    public string? ActiveProfile { get; set; }
    public List<string> V2TabTokens { get; set; } = ["bots", "chats", "tasks", "-pulse", "-artifacts", "settings"];
    public bool ShowBesideChatDefault { get; set; } = true;
    public string LanguagePreference { get; set; } = "system";
    public string ThemePreference { get; set; } = "system";
}

public sealed class LocalCacheStore
{
    private readonly string _connectionString;
    private readonly string _settingsPath;

    public LocalCacheStore(string? dbPath = null, string? settingsPath = null)
    {
        var resolvedDb = dbPath ?? AppDataPaths.CacheDatabasePath;
        _settingsPath = settingsPath ?? AppDataPaths.SettingsFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedDb)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = resolvedDb,
            Pooling = false
        }.ToString();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dashboards (
                id TEXT PRIMARY KEY,
                label TEXT NOT NULL,
                normalized_url TEXT NOT NULL,
                username TEXT,
                auth_mode INTEGER NOT NULL,
                use_cf_access INTEGER NOT NULL,
                is_combined INTEGER NOT NULL,
                last_connected_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS drafts (
                session_key TEXT PRIMARY KEY,
                draft_text TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS session_cache (
                dashboard_id TEXT NOT NULL,
                profile TEXT NOT NULL,
                session_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (dashboard_id, profile, session_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public AppSettingsData LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new AppSettingsData();
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
        }
        catch
        {
            return new AppSettingsData();
        }
    }

    public void SaveSettings(AppSettingsData settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    public IReadOnlyList<SavedDashboard> LoadDashboards()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, label, normalized_url, username, auth_mode, use_cf_access, is_combined, last_connected_at FROM dashboards ORDER BY last_connected_at DESC";
        using var reader = cmd.ExecuteReader();
        var list = new List<SavedDashboard>();
        while (reader.Read())
        {
            if (!Guid.TryParse(reader.GetString(0), out var id)) continue;
            list.Add(new SavedDashboard
            {
                Id = id,
                Label = reader.GetString(1),
                NormalizedUrl = reader.GetString(2),
                Username = reader.IsDBNull(3) ? null : reader.GetString(3),
                AuthMode = (AuthMode)reader.GetInt32(4),
                UseCloudflareAccess = reader.GetInt32(5) != 0,
                IsCombined = reader.GetInt32(6) != 0,
                LastConnectedAt = DateTimeOffset.TryParse(reader.GetString(7), out var dt) ? dt : DateTimeOffset.UtcNow
            });
        }
        return list;
    }

    public void SaveDashboard(SavedDashboard dashboard)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dashboards (id, label, normalized_url, username, auth_mode, use_cf_access, is_combined, last_connected_at)
            VALUES ($id, $label, $url, $user, $mode, $cf, $comb, $ts)
            ON CONFLICT(id) DO UPDATE SET
                label = excluded.label,
                normalized_url = excluded.normalized_url,
                username = excluded.username,
                auth_mode = excluded.auth_mode,
                use_cf_access = excluded.use_cf_access,
                is_combined = excluded.is_combined,
                last_connected_at = excluded.last_connected_at;
            """;
        cmd.Parameters.AddWithValue("$id", dashboard.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$label", dashboard.Label);
        cmd.Parameters.AddWithValue("$url", dashboard.NormalizedUrl);
        cmd.Parameters.AddWithValue("$user", (object?)dashboard.Username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", (int)dashboard.AuthMode);
        cmd.Parameters.AddWithValue("$cf", dashboard.UseCloudflareAccess ? 1 : 0);
        cmd.Parameters.AddWithValue("$comb", dashboard.IsCombined ? 1 : 0);
        cmd.Parameters.AddWithValue("$ts", dashboard.LastConnectedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteDashboard(Guid dashboardId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dashboards WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    public string? LoadDraft(string sessionKey)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT draft_text FROM drafts WHERE session_key = $key";
        cmd.Parameters.AddWithValue("$key", sessionKey);
        return cmd.ExecuteScalar() as string;
    }

    public void SaveDraft(string sessionKey, string text)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrEmpty(text))
        {
            cmd.CommandText = "DELETE FROM drafts WHERE session_key = $key";
            cmd.Parameters.AddWithValue("$key", sessionKey);
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO drafts (session_key, draft_text, updated_at)
                VALUES ($key, $text, $ts)
                ON CONFLICT(session_key) DO UPDATE SET draft_text = excluded.draft_text, updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$key", sessionKey);
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        }
        cmd.ExecuteNonQuery();
    }
}

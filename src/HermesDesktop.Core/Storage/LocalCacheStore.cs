using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HermesDesktop.Core.Diagnostics;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rpc;
using Microsoft.Data.Sqlite;

namespace HermesDesktop.Core.Storage;

public sealed record AppSettingsData
{
    public Guid? ActiveDashboardId { get; set; }
    public string? ActiveProfile { get; set; }
    public List<string> V2TabTokens { get; set; } = ["bots", "chats", "groups", "tasks", "-pulse", "-artifacts", "settings"];
    public bool ShowBesideChatDefault { get; set; } = true;
    public string LanguagePreference { get; set; } = "system";
    public string ThemePreference { get; set; } = "system";
    public string? LastAttachmentDirectory { get; set; }
    public int MaxAttachmentMegabytes { get; set; } = AttachmentUploadHelper.DefaultMaxAttachmentMegabytes;
    public Dictionary<string, List<string>> LocallyHiddenSessions { get; set; } = new();
}

public sealed class LocalCacheStore
{
    private readonly string _connectionString;
    private readonly string _settingsPath;
    private readonly string _mediaCacheRoot;
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LocalCacheStore(string? dbPath = null, string? settingsPath = null, string? mediaCacheRoot = null)
    {
        var resolvedDb = dbPath ?? AppDataPaths.CacheDatabasePath;
        _settingsPath = settingsPath ?? AppDataPaths.SettingsFilePath;
        _mediaCacheRoot = mediaCacheRoot ?? (dbPath is not null
            ? Path.Combine(Path.GetDirectoryName(resolvedDb)!, "media")
            : AppDataPaths.MediaCacheDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedDb)!);
        Directory.CreateDirectory(_mediaCacheRoot);
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

            CREATE TABLE IF NOT EXISTS session_list_cache (
                dashboard_id TEXT NOT NULL,
                profile TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (dashboard_id, profile)
            );

            CREATE TABLE IF NOT EXISTS media_index (
                dashboard_id TEXT NOT NULL,
                session_key TEXT NOT NULL,
                media_key TEXT NOT NULL,
                local_file_path TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (dashboard_id, session_key, media_key)
            );

            CREATE TABLE IF NOT EXISTS session_aliases (
                dashboard_id TEXT NOT NULL,
                alias_id TEXT NOT NULL,
                canonical_id TEXT NOT NULL,
                PRIMARY KEY (dashboard_id, alias_id)
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
        var dashStr = dashboardId.ToString("D");
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM dashboards WHERE id = $id;
                DELETE FROM session_cache WHERE dashboard_id = $id;
                DELETE FROM session_aliases WHERE dashboard_id = $id;
                DELETE FROM session_list_cache WHERE dashboard_id = $id;
                DELETE FROM media_index WHERE dashboard_id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", dashStr);
            cmd.ExecuteNonQuery();

            var dashMediaDir = Path.Combine(_mediaCacheRoot, dashboardId.ToString("N"));
            if (Directory.Exists(dashMediaDir))
            {
                Directory.Delete(dashMediaDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", $"DeleteDashboard cleanup failed id={dashStr}", ex);
        }
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

    // ---- Session List Cache ----

    public void SaveSessionListCache(Guid dashboardId, string? profile, IReadOnlyList<SessionSummary> sessions)
    {
        try
        {
            var dashStr = dashboardId.ToString("D");
            var normProfile = NormalizeProfileKey(profile);
            var json = JsonSerializer.Serialize(sessions, CacheJsonOptions);
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO session_list_cache (dashboard_id, profile, payload_json, updated_at)
                VALUES ($dash, $prof, $json, $ts)
                ON CONFLICT(dashboard_id, profile) DO UPDATE SET
                    payload_json = excluded.payload_json,
                    updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$dash", dashStr);
            cmd.Parameters.AddWithValue("$prof", normProfile);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", "SaveSessionListCache failed", ex);
        }
    }

    public IReadOnlyList<SessionSummary> LoadSessionListCache(Guid dashboardId, string? profile)
    {
        try
        {
            var dashStr = dashboardId.ToString("D");
            var normProfile = NormalizeProfileKey(profile);
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT payload_json FROM session_list_cache WHERE dashboard_id = $dash AND profile = $prof";
            cmd.Parameters.AddWithValue("$dash", dashStr);
            cmd.Parameters.AddWithValue("$prof", normProfile);
            if (cmd.ExecuteScalar() is string json && !string.IsNullOrWhiteSpace(json))
            {
                return JsonSerializer.Deserialize<List<SessionSummary>>(json, CacheJsonOptions) ?? [];
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", "LoadSessionListCache failed", ex);
        }
        return [];
    }

    // ---- Session Snapshot Cache (Messages + Context + Metadata) ----

    public void SaveSessionSnapshot(
        Guid dashboardId,
        string? profile,
        IReadOnlyList<string> aliasIds,
        CachedSessionSnapshot snapshot)
    {
        if (aliasIds.Count == 0) return;
        try
        {
            var canonicalId = aliasIds[0];
            var dashStr = dashboardId.ToString("D");
            var normProfile = NormalizeProfileKey(profile);
            var json = JsonSerializer.Serialize(snapshot, CacheJsonOptions);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            // Clean any older rows keyed by non-canonical aliases of the same session
            foreach (var alias in aliasIds.Skip(1))
            {
                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM session_cache WHERE dashboard_id = $dash AND session_id = $sid";
                del.Parameters.AddWithValue("$dash", dashStr);
                del.Parameters.AddWithValue("$sid", alias);
                del.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO session_cache (dashboard_id, profile, session_id, payload_json, updated_at)
                VALUES ($dash, $prof, $sid, $json, $ts)
                ON CONFLICT(dashboard_id, profile, session_id) DO UPDATE SET
                    payload_json = excluded.payload_json,
                    updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$dash", dashStr);
            cmd.Parameters.AddWithValue("$prof", normProfile);
            cmd.Parameters.AddWithValue("$sid", canonicalId);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();

            foreach (var alias in aliasIds)
            {
                using var aliasCmd = conn.CreateCommand();
                aliasCmd.Transaction = tx;
                aliasCmd.CommandText = """
                    INSERT INTO session_aliases (dashboard_id, alias_id, canonical_id)
                    VALUES ($dash, $aid, $cid)
                    ON CONFLICT(dashboard_id, alias_id) DO UPDATE SET canonical_id = excluded.canonical_id;
                    """;
                aliasCmd.Parameters.AddWithValue("$dash", dashStr);
                aliasCmd.Parameters.AddWithValue("$aid", alias);
                aliasCmd.Parameters.AddWithValue("$cid", canonicalId);
                aliasCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", $"SaveSessionSnapshot failed key={aliasIds[0]}", ex);
        }
    }

    public CachedSessionSnapshot? LoadSessionSnapshot(
        Guid dashboardId,
        string? profile,
        IReadOnlyList<string> aliasIds)
    {
        if (aliasIds.Count == 0) return null;
        try
        {
            var dashStr = dashboardId.ToString("D");
            var normProfile = NormalizeProfileKey(profile);
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var candidateIds = new List<string>(aliasIds);
            foreach (var alias in aliasIds)
            {
                using var mapCmd = conn.CreateCommand();
                mapCmd.CommandText = "SELECT canonical_id FROM session_aliases WHERE dashboard_id = $dash AND alias_id = $aid LIMIT 1";
                mapCmd.Parameters.AddWithValue("$dash", dashStr);
                mapCmd.Parameters.AddWithValue("$aid", alias);
                if (mapCmd.ExecuteScalar() is string mapped && !candidateIds.Contains(mapped, StringComparer.Ordinal))
                {
                    candidateIds.Add(mapped);
                }
            }

            foreach (var cid in candidateIds)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT payload_json FROM session_cache
                    WHERE dashboard_id = $dash AND session_id = $sid AND (profile = $prof OR profile = 'default')
                    ORDER BY CASE WHEN profile = $prof THEN 0 ELSE 1 END
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$dash", dashStr);
                cmd.Parameters.AddWithValue("$prof", normProfile);
                cmd.Parameters.AddWithValue("$sid", cid);
                if (cmd.ExecuteScalar() is string json && !string.IsNullOrWhiteSpace(json))
                {
                    return JsonSerializer.Deserialize<CachedSessionSnapshot>(json, CacheJsonOptions);
                }
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", "LoadSessionSnapshot failed", ex);
        }
        return null;
    }

    // ---- Session Media & File Cache ----

    public void SaveCachedMediaBytes(Guid dashboardId, string sessionKey, string mediaKey, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(sessionKey) || string.IsNullOrWhiteSpace(mediaKey) || bytes.Length == 0)
            return;
        try
        {
            var localPath = BuildMediaDiskPath(dashboardId, sessionKey, mediaKey);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.WriteAllBytes(localPath, bytes);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO media_index (dashboard_id, session_key, media_key, local_file_path, size_bytes, updated_at)
                VALUES ($dash, $skey, $mkey, $path, $size, $ts)
                ON CONFLICT(dashboard_id, session_key, media_key) DO UPDATE SET
                    local_file_path = excluded.local_file_path,
                    size_bytes = excluded.size_bytes,
                    updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$dash", dashboardId.ToString("D"));
            cmd.Parameters.AddWithValue("$skey", sessionKey);
            cmd.Parameters.AddWithValue("$mkey", mediaKey);
            cmd.Parameters.AddWithValue("$path", localPath);
            cmd.Parameters.AddWithValue("$size", bytes.LongLength);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", $"SaveCachedMediaBytes failed mediaKey={mediaKey}", ex);
        }
    }

    public byte[]? TryLoadCachedMediaBytes(Guid dashboardId, IReadOnlyList<string> sessionAliases, string mediaKey)
    {
        if (sessionAliases.Count == 0 || string.IsNullOrWhiteSpace(mediaKey)) return null;
        try
        {
            var dashStr = dashboardId.ToString("D");
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            foreach (var skey in sessionAliases)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT local_file_path FROM media_index
                    WHERE dashboard_id = $dash AND session_key = $skey AND media_key = $mkey
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$dash", dashStr);
                cmd.Parameters.AddWithValue("$skey", skey);
                cmd.Parameters.AddWithValue("$mkey", mediaKey);
                if (cmd.ExecuteScalar() is string path && File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", $"TryLoadCachedMediaBytes failed mediaKey={mediaKey}", ex);
        }
        return null;
    }

    public void SaveCachedTextAsset(Guid dashboardId, string sessionKey, string remotePath, string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        SaveCachedMediaBytes(dashboardId, sessionKey, $"text::{remotePath}", Encoding.UTF8.GetBytes(content));
    }

    public string? TryLoadCachedTextAsset(Guid dashboardId, IReadOnlyList<string> sessionAliases, string remotePath)
    {
        var bytes = TryLoadCachedMediaBytes(dashboardId, sessionAliases, $"text::{remotePath}");
        return bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : null;
    }

    /// <summary>
    /// Removes ALL local cached state and files associated with a deleted session:
    /// - session_cache snapshots across all alias IDs
    /// - session_list_cache entries matching any alias ID
    /// - drafts matching any alias ID
    /// - media_index entries & physical cached images/files/directories on disk
    /// Returns every purged mediaKey so in-memory caches can evict them cleanly.
    /// </summary>
    public IReadOnlyList<string> DeleteSessionCacheAndFiles(
        Guid dashboardId,
        string? profile,
        IReadOnlyList<string> aliasIds)
    {
        var purgedMediaKeys = new HashSet<string>(StringComparer.Ordinal);
        if (aliasIds.Count == 0) return [];

        try
        {
            var dashStr = dashboardId.ToString("D");
            var normProfile = NormalizeProfileKey(profile);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // 1. Collect referenced media keys from cached session snapshot before deleting
            if (LoadSessionSnapshot(dashboardId, normProfile, aliasIds) is { } snap)
            {
                foreach (var mk in snap.ReferencedMediaKeys) purgedMediaKeys.Add(mk);
                foreach (var mk in SessionCacheHelper.CollectMediaKeys(snap.Messages)) purgedMediaKeys.Add(mk);
            }

            // 2. Collect physical cached media file paths & delete from disk
            var filesToDelete = new List<string>();
            foreach (var alias in aliasIds)
            {
                using var q = conn.CreateCommand();
                q.CommandText = "SELECT media_key, local_file_path FROM media_index WHERE dashboard_id = $dash AND session_key = $skey";
                q.Parameters.AddWithValue("$dash", dashStr);
                q.Parameters.AddWithValue("$skey", alias);
                using var reader = q.ExecuteReader();
                while (reader.Read())
                {
                    var mk = reader.GetString(0);
                    purgedMediaKeys.Add(mk.StartsWith("text::", StringComparison.Ordinal) ? mk["text::".Length..] : mk);
                    filesToDelete.Add(reader.GetString(1));
                }
            }

            foreach (var filePath in filesToDelete)
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                }
                catch (Exception fex)
                {
                    AppDiagnostics.LogError("CACHE_DEL_ERR", $"Failed deleting cached file {filePath}", fex);
                }
            }

            // Clean per-session media subdirectories if present
            foreach (var alias in aliasIds)
            {
                var sessionMediaDir = Path.Combine(_mediaCacheRoot, dashboardId.ToString("N"), SanitizeSegment(alias));
                try
                {
                    if (Directory.Exists(sessionMediaDir))
                    {
                        Directory.Delete(sessionMediaDir, recursive: true);
                    }
                }
                catch (Exception dex)
                {
                    AppDiagnostics.LogError("CACHE_DEL_ERR", $"Failed deleting dir {sessionMediaDir}", dex);
                }
            }

            // 3. Delete SQLite rows in media_index, session_cache, and drafts
            using var tx = conn.BeginTransaction();
            foreach (var alias in aliasIds)
            {
                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    DELETE FROM media_index WHERE dashboard_id = $dash AND session_key = $sid;
                    DELETE FROM session_cache WHERE dashboard_id = $dash AND session_id = $sid;
                    DELETE FROM session_aliases WHERE dashboard_id = $dash AND (alias_id = $sid OR canonical_id = $sid);
                    DELETE FROM drafts WHERE session_key = $sid OR session_key = $draftPrefixed;
                    """;
                del.Parameters.AddWithValue("$dash", dashStr);
                del.Parameters.AddWithValue("$sid", alias);
                del.Parameters.AddWithValue("$draftPrefixed", $"{dashStr}:{alias}");
                del.ExecuteNonQuery();
            }

            // 4. Prune deleted session IDs from session_list_cache
            var aliasSet = aliasIds.ToHashSet(StringComparer.Ordinal);
            var updatedLists = new List<(string Prof, string Json)>();
            using (var listQuery = conn.CreateCommand())
            {
                listQuery.Transaction = tx;
                listQuery.CommandText = "SELECT profile, payload_json FROM session_list_cache WHERE dashboard_id = $dash";
                listQuery.Parameters.AddWithValue("$dash", dashStr);
                using var listReader = listQuery.ExecuteReader();
                while (listReader.Read())
                {
                    var prof = listReader.GetString(0);
                    var rawJson = listReader.GetString(1);
                    var items = JsonSerializer.Deserialize<List<SessionSummary>>(rawJson, CacheJsonOptions);
                    if (items is null) continue;
                    var removed = items.RemoveAll(s =>
                        aliasSet.Contains(s.Id)
                        || (s.StoredSessionId is not null && aliasSet.Contains(s.StoredSessionId))
                        || (s.RuntimeSessionId is not null && aliasSet.Contains(s.RuntimeSessionId))
                        || s.AlternateIds.Any(a => aliasSet.Contains(a)));
                    if (removed > 0)
                    {
                        updatedLists.Add((prof, JsonSerializer.Serialize(items, CacheJsonOptions)));
                    }
                }
            }

            foreach (var (prof, nextJson) in updatedLists)
            {
                using var up = conn.CreateCommand();
                up.Transaction = tx;
                up.CommandText = """
                    UPDATE session_list_cache SET payload_json = $json, updated_at = $ts
                    WHERE dashboard_id = $dash AND profile = $prof;
                    """;
                up.Parameters.AddWithValue("$dash", dashStr);
                up.Parameters.AddWithValue("$prof", prof);
                up.Parameters.AddWithValue("$json", nextJson);
                up.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
                up.ExecuteNonQuery();
            }

            tx.Commit();
            AppDiagnostics.Log("CACHE", $"Purged local cache & {filesToDelete.Count} media files for dashboard={dashStr}, aliases=[{string.Join(",", aliasIds)}]");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("CACHE_ERR", "DeleteSessionCacheAndFiles failed", ex);
        }

        return purgedMediaKeys.ToList();
    }

    private string BuildMediaDiskPath(Guid dashboardId, string sessionKey, string mediaKey)
    {
        var rawPath = mediaKey.StartsWith("text::", StringComparison.Ordinal) ? mediaKey["text::".Length..] : mediaKey;
        var fileName = Path.GetFileName(rawPath.Split('?')[0]);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "asset.bin";
        fileName = SanitizeSegment(fileName);
        if (fileName.Length > 64) fileName = fileName[^64..];

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(mediaKey));
        var hashPrefix = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();

        return Path.Combine(
            _mediaCacheRoot,
            dashboardId.ToString("N"),
            SanitizeSegment(sessionKey),
            $"{hashPrefix}_{fileName}");
    }

    private static string NormalizeProfileKey(string? profile) =>
        string.IsNullOrWhiteSpace(profile) ? "default" : profile.Trim().ToLowerInvariant();

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '/' || ch == '\\' || ch == ':' || Array.IndexOf(invalid, ch) >= 0) sb.Append('_');
            else sb.Append(ch);
        }
        var res = sb.ToString().Trim();
        return string.IsNullOrEmpty(res) ? "default" : res;
    }
}

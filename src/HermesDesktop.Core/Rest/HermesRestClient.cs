using System.Net;
using System.Text;
using System.Text.Json;
using HermesDesktop.Core.Auth;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Security;

namespace HermesDesktop.Core.Rest;

public sealed class HermesRestClient : IDisposable
{
    private readonly string _baseUrl;
    private readonly DashboardSecrets? _secrets;
    private readonly HttpClient _http;
    private bool _supportsMutationSnapshot = true;

    public HermesRestClient(
        string baseUrl,
        AuthSessionTicket auth,
        DashboardSecrets? secrets = null,
        HttpMessageHandler? messageHandler = null)
    {
        _baseUrl = ConnectionURLPolicy.NormalizeBaseUrl(baseUrl);
        _secrets = secrets;
        var handler = messageHandler ?? new HttpClientHandler
        {
            CookieContainer = auth.CookieJar,
            UseCookies = true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<IReadOnlyList<ModelProviderOption>> GetServerDefinedProvidersAsync(
        string? profile = null,
        bool refresh = false,
        CancellationToken ct = default)
    {
        var hasCustomProfile = !string.IsNullOrWhiteSpace(profile)
            && !string.Equals(profile.Trim(), "default", StringComparison.OrdinalIgnoreCase);
        var profileSuffix = hasCustomProfile ? $"profile={Uri.EscapeDataString(profile!.Trim())}" : "";

        async Task<IReadOnlyList<ModelProviderOption>> TryFetchModelOptionsAsync(bool? explicitOnly)
        {
            var query = new List<string>();
            if (explicitOnly.HasValue) query.Add(explicitOnly.Value ? "explicit_only=true" : "explicit_only=false");
            if (hasCustomProfile) query.Add(profileSuffix);
            if (refresh) query.Add("refresh=true");
            var path = query.Count > 0 ? $"/api/model/options?{string.Join("&", query)}" : "/api/model/options";
            try
            {
                using var doc = await GetJsonAsync(path, ct);
                return Rpc.HermesWsClient.DecodeModelProviders(doc.RootElement, defaultIsConfigured: explicitOnly == true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("REST_PROVIDERS", $"GET {path} failed: {ex.Message}");
                return [];
            }
        }

        async Task<IReadOnlyList<ModelProviderOption>> TryFetchCustomEndpointsAsync(string? targetProfile)
        {
            try
            {
                var path = !string.IsNullOrWhiteSpace(targetProfile)
                    && !string.Equals(targetProfile.Trim(), "default", StringComparison.OrdinalIgnoreCase)
                    ? $"/api/providers/custom-endpoints?profile={Uri.EscapeDataString(targetProfile.Trim())}"
                    : "/api/providers/custom-endpoints";
                using var doc = await GetJsonAsync(path, ct);
                return DecodeCustomEndpointProviders(doc.RootElement);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return [];
            }
        }

        async Task<IReadOnlyList<ModelProviderOption>> TryFetchOAuthAsync()
        {
            try
            {
                using var doc = await GetJsonAsync("/api/providers/oauth", ct);
                return DecodeOAuthProviders(doc.RootElement, signedInOnly: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return [];
            }
        }

        async Task<IReadOnlyList<ModelProviderOption>> TryFetchEnvAsync(string? targetProfile)
        {
            try
            {
                var path = !string.IsNullOrWhiteSpace(targetProfile)
                    && !string.Equals(targetProfile.Trim(), "default", StringComparison.OrdinalIgnoreCase)
                    ? $"/api/env?profile={Uri.EscapeDataString(targetProfile.Trim())}"
                    : "/api/env";
                using var doc = await GetJsonAsync(path, ct);
                return DecodeEnvProviders(doc.RootElement, includeUnconfigured: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return [];
            }
        }

        var explicitOptionsTask = TryFetchModelOptionsAsync(true);
        var fullOptionsTask = TryFetchModelOptionsAsync(false);
        var defaultOptionsTask = TryFetchModelOptionsAsync(null);
        var profileEndpointsTask = TryFetchCustomEndpointsAsync(profile);
        var defaultEndpointsTask = hasCustomProfile ? TryFetchCustomEndpointsAsync(null) : Task.FromResult<IReadOnlyList<ModelProviderOption>>([]);
        var oauthTask = TryFetchOAuthAsync();
        var profileEnvTask = TryFetchEnvAsync(profile);
        var defaultEnvTask = hasCustomProfile ? TryFetchEnvAsync(null) : Task.FromResult<IReadOnlyList<ModelProviderOption>>([]);

        await Task.WhenAll(
            explicitOptionsTask,
            fullOptionsTask,
            defaultOptionsTask,
            profileEndpointsTask,
            defaultEndpointsTask,
            oauthTask,
            profileEnvTask,
            defaultEnvTask);

        return Rpc.HermesWsClient.MergeModelProviders(
            explicitOptionsTask.Result,
            fullOptionsTask.Result,
            defaultOptionsTask.Result,
            profileEndpointsTask.Result,
            defaultEndpointsTask.Result,
            oauthTask.Result,
            profileEnvTask.Result,
            defaultEnvTask.Result);
    }

    public async Task SaveProviderApiKeyAsync(
        string providerSlug,
        string? envVarName,
        string apiKey,
        string? profile = null,
        CancellationToken ct = default)
    {
        var key = apiKey.Trim();
        var slug = providerSlug.Trim();
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("API key cannot be empty.", nameof(apiKey));

        var hasCustomProfile = !string.IsNullOrWhiteSpace(profile)
            && !string.Equals(profile.Trim(), "default", StringComparison.OrdinalIgnoreCase);

        try
        {
            var poolPath = hasCustomProfile
                ? $"/api/credentials/pool?profile={Uri.EscapeDataString(profile!.Trim())}"
                : "/api/credentials/pool";
            await SendJsonAsync(HttpMethod.Post, poolPath, new
            {
                provider = slug,
                api_key = key,
                label = "HermesDesktop"
            }, ct);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !string.IsNullOrWhiteSpace(envVarName))
        {
            HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("REST_PROVIDERS", $"POST /api/credentials/pool failed ({ex.Message}); trying PUT /api/env");
        }

        if (!string.IsNullOrWhiteSpace(envVarName))
        {
            var envPath = hasCustomProfile
                ? $"/api/env?profile={Uri.EscapeDataString(profile!.Trim())}"
                : "/api/env";
            await SendJsonAsync(HttpMethod.Put, envPath, new
            {
                key = envVarName.Trim(),
                value = key,
                profile = string.IsNullOrWhiteSpace(profile) ? "default" : profile.Trim(),
                provider_setup = true
            }, ct);
        }
    }

    public static IReadOnlyList<ModelProviderOption> DecodeCustomEndpointProviders(JsonElement root)
    {
        JsonElement arr = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            arr = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (!(root.TryGetProperty("endpoints", out arr) && arr.ValueKind == JsonValueKind.Array))
            {
                return [];
            }
        }
        else
        {
            return [];
        }

        var list = new List<ModelProviderOption>();
        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var rawId = FirstNonEmpty(GetString(row, "id"), GetString(row, "endpoint_id"), GetString(row, "name"));
            if (rawId is null) continue;

            var bareId = rawId.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)
                ? rawId["custom:".Length..].Trim()
                : rawId;
            if (string.IsNullOrWhiteSpace(bareId)) continue;

            // Hermes' primary custom endpoint uses bare slug "custom" (not "custom:custom")
            var slug = string.Equals(bareId, "custom", StringComparison.OrdinalIgnoreCase)
                ? "custom"
                : $"custom:{bareId}";
            var displayName = FirstNonEmpty(GetString(row, "name"), GetString(row, "label"), bareId) ?? bareId;
            var isCurrent = GetBool(row, "is_current") == true;
            var aliases = new List<string> { slug };
            if (!aliases.Contains(bareId, StringComparer.OrdinalIgnoreCase)) aliases.Add(bareId);
            if (string.Equals(bareId, "custom", StringComparison.OrdinalIgnoreCase)
                && !aliases.Contains("custom:custom", StringComparer.OrdinalIgnoreCase))
            {
                aliases.Add("custom:custom");
            }

            var models = new List<string>();
            var modelSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddModel(string? m)
            {
                if (!string.IsNullOrWhiteSpace(m) && modelSeen.Add(m.Trim())) models.Add(m.Trim());
            }

            AddModel(GetString(row, "model"));
            if (row.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modelsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        AddModel(item.GetString());
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        AddModel(FirstNonEmpty(GetString(item, "id"), GetString(item, "model"), GetString(item, "name")));
                    }
                }
            }

            list.Add(new ModelProviderOption(slug, displayName, aliases, isCurrent, models, IsConfigured: true, SourceTag: "custom"));
        }

        return list;
    }

    public static IReadOnlyList<ModelProviderOption> DecodeOAuthProviders(JsonElement root, bool signedInOnly = true)
    {
        JsonElement arr = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            arr = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (!(root.TryGetProperty("providers", out arr) && arr.ValueKind == JsonValueKind.Array))
            {
                return [];
            }
        }
        else
        {
            return [];
        }

        var list = new List<ModelProviderOption>();
        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var id = FirstNonEmpty(GetString(row, "id"), GetString(row, "provider"), GetString(row, "slug"));
            if (id is null) continue;

            var signedIn = GetBool(row, "logged_in") == true;
            if (!signedIn && row.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.Object)
            {
                signedIn = GetBool(statusEl, "logged_in") == true;
            }
            if (signedInOnly && !signedIn) continue;

            var name = FirstNonEmpty(GetString(row, "name"), GetString(row, "label"), id) ?? id;
            list.Add(new ModelProviderOption(id, name, [id], false, [], IsConfigured: signedIn, SourceTag: "oauth"));
        }

        return list;
    }

    public static IReadOnlyList<ModelProviderOption> DecodeConfiguredEnvProviders(JsonElement root) =>
        DecodeEnvProviders(root, includeUnconfigured: false);

    public static IReadOnlyList<ModelProviderOption> DecodeEnvProviders(JsonElement root, bool includeUnconfigured = true)
    {
        if (root.ValueKind != JsonValueKind.Object) return [];
        var map = root.TryGetProperty("env", out var inner) && inner.ValueKind == JsonValueKind.Object
            ? inner
            : root;

        var bySlug = new Dictionary<string, (string Label, bool IsSet, string? EnvVar)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        void Upsert(string slug, string label, bool isSet, string? envVar)
        {
            if (!bySlug.TryGetValue(slug, out var cur))
            {
                order.Add(slug);
                bySlug[slug] = (label, isSet, envVar);
                return;
            }

            var nextLabel = string.Equals(cur.Label, slug, StringComparison.OrdinalIgnoreCase) && !string.Equals(label, slug, StringComparison.OrdinalIgnoreCase)
                ? label
                : cur.Label;
            bySlug[slug] = (nextLabel, cur.IsSet || isSet, cur.EnvVar ?? envVar);
        }

        foreach (var prop in map.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var row = prop.Value;
            if (!string.Equals(GetString(row, "category"), "provider", StringComparison.OrdinalIgnoreCase)) continue;

            var isSet = GetBool(row, "is_set") == true;
            if (!includeUnconfigured && !isSet) continue;

            var isPassword = GetBool(row, "is_password") == true;
            var envVar = isPassword ? prop.Name : null;
            var matchedAnyProfile = false;

            if (row.TryGetProperty("provider_profiles", out var profilesEl) && profilesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in profilesEl.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    var primary = p.TryGetProperty("primary", out _) ? GetBool(p, "primary") == true : true;
                    if (isPassword && !primary) continue;

                    var slug = FirstNonEmpty(GetString(p, "provider"));
                    if (slug is null) continue;
                    matchedAnyProfile = true;

                    var label = FirstNonEmpty(GetString(p, "provider_label"), slug) ?? slug;
                    Upsert(slug, label, isSet, envVar);
                }
            }

            if (!matchedAnyProfile)
            {
                var slug = FirstNonEmpty(GetString(row, "provider"));
                if (slug is null) continue;
                var primary = isPassword ? (GetBool(row, "provider_primary") ?? true) : true;
                if (!primary) continue;

                var label = FirstNonEmpty(GetString(row, "provider_label"), slug) ?? slug;
                Upsert(slug, label, isSet, envVar);
            }
        }

        return order
            .Select(slug =>
            {
                var info = bySlug[slug];
                return new ModelProviderOption(
                    slug,
                    info.Label,
                    [slug],
                    false,
                    [],
                    IsConfigured: info.IsSet,
                    EnvVarName: info.EnvVar,
                    SourceTag: "env");
            })
            .ToList();
    }

    public async Task<IReadOnlyList<FunctionalApiKeyInfo>> GetFunctionalApiKeysAsync(
        string? profile = null,
        CancellationToken ct = default)
    {
        var hasCustomProfile = !string.IsNullOrWhiteSpace(profile)
            && !string.Equals(profile.Trim(), "default", StringComparison.OrdinalIgnoreCase);
        var envPath = hasCustomProfile
            ? $"/api/env?profile={Uri.EscapeDataString(profile!.Trim())}"
            : "/api/env";
        var byKey = new Dictionary<string, FunctionalApiKeyInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = await GetJsonAsync(envPath, ct);
            foreach (var item in DecodeFunctionalApiKeys(doc.RootElement))
            {
                byKey[item.Key] = item;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("REST_ENV", $"GET {envPath} failed: {ex.Message}");
        }

        // Toolset config endpoints (e.g. web/config for Tavily/Firecrawl) can also surface functional env_vars
        foreach (var toolset in new[] { "web", "image_gen", "tts" })
        {
            var snap = await GetToolsetConfigSnapshotAsync(toolset, profile, ct);
            if (snap is null) continue;
            foreach (var envVar in snap.SetEnvVars)
            {
                if (!byKey.ContainsKey(envVar.Key))
                {
                    byKey[envVar.Key] = envVar;
                }
            }
        }

        return byKey.Values
            .OrderBy(k => k.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<FunctionalApiKeyInfo> DecodeFunctionalApiKeys(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return [];
        var map = root.TryGetProperty("env", out var inner) && inner.ValueKind == JsonValueKind.Object
            ? inner
            : root;

        var list = new List<FunctionalApiKeyInfo>();
        foreach (var prop in map.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var row = prop.Value;
            var category = FirstNonEmpty(GetString(row, "category")) ?? "";
            if (string.Equals(category, "provider", StringComparison.OrdinalIgnoreCase)) continue;
            if (GetBool(row, "is_set") != true) continue;

            var isPassword = GetBool(row, "is_password") == true;
            var keyUpper = prop.Name.ToUpperInvariant();
            var looksFunctionalKey = isPassword
                || keyUpper.EndsWith("_API_KEY", StringComparison.Ordinal)
                || keyUpper.EndsWith("_KEY", StringComparison.Ordinal)
                || keyUpper.EndsWith("_TOKEN", StringComparison.Ordinal)
                || keyUpper.EndsWith("_SECRET", StringComparison.Ordinal)
                || category.Equals("tools", StringComparison.OrdinalIgnoreCase)
                || category.Equals("web", StringComparison.OrdinalIgnoreCase)
                || category.Equals("search", StringComparison.OrdinalIgnoreCase)
                || category.Equals("voice", StringComparison.OrdinalIgnoreCase)
                || category.Equals("tts", StringComparison.OrdinalIgnoreCase)
                || category.Equals("image_gen", StringComparison.OrdinalIgnoreCase)
                || category.Equals("mcp", StringComparison.OrdinalIgnoreCase);

            if (!looksFunctionalKey) continue;

            var label = FirstNonEmpty(GetString(row, "label"), GetString(row, "description"), prop.Name) ?? prop.Name;
            list.Add(new FunctionalApiKeyInfo(prop.Name, string.IsNullOrEmpty(category) ? "tools" : category, label, true));
        }

        return list;
    }

    public async Task<ToolsetBackendSnapshot?> GetToolsetConfigSnapshotAsync(
        string toolsetName,
        string? profile = null,
        CancellationToken ct = default)
    {
        var safeName = Uri.EscapeDataString(toolsetName.Trim());
        var hasCustomProfile = !string.IsNullOrWhiteSpace(profile)
            && !string.Equals(profile.Trim(), "default", StringComparison.OrdinalIgnoreCase);
        var path = hasCustomProfile
            ? $"/api/tools/toolsets/{safeName}/config?profile={Uri.EscapeDataString(profile!.Trim())}"
            : $"/api/tools/toolsets/{safeName}/config";

        try
        {
            using var doc = await GetJsonAsync(path, ct);
            return DecodeToolsetConfigSnapshot(toolsetName.Trim(), doc.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task SetToolsetProviderAsync(
        string toolsetName,
        string provider,
        string? capability = null,
        string? profile = null,
        CancellationToken ct = default)
    {
        var safeName = Uri.EscapeDataString(toolsetName.Trim());
        var hasCustomProfile = !string.IsNullOrWhiteSpace(profile)
            && !string.Equals(profile.Trim(), "default", StringComparison.OrdinalIgnoreCase);
        var path = hasCustomProfile
            ? $"/api/tools/toolsets/{safeName}/provider?profile={Uri.EscapeDataString(profile!.Trim())}"
            : $"/api/tools/toolsets/{safeName}/provider";

        var body = new Dictionary<string, object?> { ["provider"] = provider.Trim() };
        if (!string.IsNullOrWhiteSpace(capability)) body["capability"] = capability.Trim();
        if (hasCustomProfile) body["profile"] = profile!.Trim();

        await SendJsonAsync(HttpMethod.Put, path, body, ct);
    }

    public static ToolsetBackendSnapshot DecodeToolsetConfigSnapshot(string toolsetName, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ToolsetBackendSnapshot(toolsetName, null, null, null, []);
        }

        var activeProvider = FirstNonEmpty(GetString(root, "active_provider"));
        var activeSearch = FirstNonEmpty(GetString(root, "active_search_backend"));
        var activeExtract = FirstNonEmpty(GetString(root, "active_extract_backend"));

        var envVars = new List<FunctionalApiKeyInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("providers", out var providersEl) && providersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in providersEl.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                var providerName = FirstNonEmpty(GetString(p, "name"), GetString(p, "web_backend"), toolsetName) ?? toolsetName;
                if (p.TryGetProperty("env_vars", out var varsEl) && varsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ev in varsEl.EnumerateArray())
                    {
                        if (ev.ValueKind != JsonValueKind.Object) continue;
                        var key = FirstNonEmpty(GetString(ev, "key"));
                        if (key is null || GetBool(ev, "is_set") != true || !seen.Add(key)) continue;
                        envVars.Add(new FunctionalApiKeyInfo(key, toolsetName, providerName, true));
                    }
                }
            }
        }

        return new ToolsetBackendSnapshot(toolsetName, activeProvider, activeSearch, activeExtract, envVars);
    }

    public async Task<IReadOnlyList<BotProfile>> GetProfilesAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/profiles", ct);
        var list = new List<BotProfile>();
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("profiles", out var p) && p.ValueKind == JsonValueKind.Array
                ? p
                : default;

        if (arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var strName = item.GetString()?.Trim();
                if (string.IsNullOrEmpty(strName)) continue;
                list.Add(new BotProfile
                {
                    Name = strName,
                    DisplayName = strName,
                    IsDefault = strName.Equals("default", StringComparison.OrdinalIgnoreCase)
                });
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = FirstNonEmpty(GetString(item, "name"), GetString(item, "id"));
            if (string.IsNullOrWhiteSpace(name)) continue;

            var displayName = FirstNonEmpty(
                GetString(item, "display_name"),
                GetString(item, "bot_title"),
                GetString(item, "label"),
                name) ?? name;

            list.Add(new BotProfile
            {
                Name = name,
                BotTitle = FirstNonEmpty(GetString(item, "bot_title")),
                DisplayName = displayName,
                Description = FirstNonEmpty(GetString(item, "description")),
                Model = FirstNonEmpty(GetString(item, "model")),
                Provider = FirstNonEmpty(GetString(item, "provider")),
                HasAvatar = GetBool(item, "has_avatar") == true,
                IsPinned = GetBool(item, "pinned") == true,
                AppearanceColorHex = FirstNonEmpty(GetString(item, "color"), GetString(item, "accent_color")),
                CanonicalSessionId = FirstNonEmpty(GetString(item, "canonical_session_id")),
                LastPreview = FirstNonEmpty(GetString(item, "last_preview")),
                IsDefault = GetBool(item, "is_default") == true || name.Equals("default", StringComparison.OrdinalIgnoreCase)
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<SessionSummary>> GetProfileSessionsAsync(
        string? profile = null,
        int limit = 100,
        int offset = 0,
        bool archivedOnly = false,
        CancellationToken ct = default)
    {
        var archivedParam = archivedOnly ? "only" : "exclude";
        var path = $"/api/profiles/sessions?limit={limit}&offset={offset}&min_messages=1&archived={archivedParam}&order=recent&exclude_sources={SessionSourceExtensions.ChatListExcludedSources}";
        if (!string.IsNullOrWhiteSpace(profile))
        {
            path += $"&profile={Uri.EscapeDataString(profile)}";
        }

        using var doc = await GetJsonAsync(path, ct);
        var list = new List<SessionSummary>();
        if (!doc.RootElement.TryGetProperty("sessions", out var sessionsEl) || sessionsEl.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in sessionsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var runtimeId = FirstNonEmpty(GetString(item, "session_id"), GetString(item, "runtime_session_id"));
            var rawId = FirstNonEmpty(GetString(item, "id"));
            var id = runtimeId ?? rawId;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var storedId = FirstNonEmpty(
                GetString(item, "stored_session_id"),
                GetString(item, "storedSessionId"))
                ?? (runtimeId is not null ? rawId : null)
                ?? id;

            var altIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in new[] { "session_id", "id", "stored_session_id", "runtime_session_id", "session_key" })
            {
                var v = FirstNonEmpty(GetString(item, k));
                if (!string.IsNullOrEmpty(v) && v != id) altIds.Add(v);
            }

            var msgCount = GetInt(item, "message_count") ?? GetInt(item, "messageCount");
            if (msgCount == 0) continue;

            var lastActivity = GetDouble(item, "last_activity_at") ?? GetDouble(item, "updated_at");
            var updatedLabel = FirstNonEmpty(GetString(item, "updated_label")) ?? FormatRelativeEpoch(lastActivity);
            var title = FirstNonEmpty(GetString(item, "title"), GetString(item, "preview")) ?? "Untitled chat";
            var preview = FirstNonEmpty(GetString(item, "preview"));
            if (string.Equals(preview, title, StringComparison.Ordinal)) preview = null;

            list.Add(new SessionSummary
            {
                Id = id,
                StoredSessionId = storedId,
                RuntimeSessionId = runtimeId,
                AlternateIds = [.. altIds],
                Title = title,
                Model = FirstNonEmpty(GetString(item, "model")) ?? "",
                UpdatedLabel = updatedLabel,
                LastActivityAt = lastActivity,
                Profile = FirstNonEmpty(GetString(item, "profile"), GetString(item, "profile_name"), profile),
                Source = SessionSourceExtensions.Parse(GetString(item, "source")),
                IsActive = GetBool(item, "is_active") == true || GetBool(item, "running") == true,
                IsArchived = GetBool(item, "archived") == true,
                MessageCount = msgCount,
                IsUnread = GetBool(item, "unread"),
                ReadWatermark = GetDouble(item, "last_read_at"),
                IsPinned = GetBool(item, "pinned"),
                Preview = preview
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetSessionMessagesAsync(
        string sessionId,
        string? profile = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        var pageLimit = Math.Clamp(limit, 50, 500);
        var collectedPages = new List<IReadOnlyList<ChatMessage>>();
        var offset = 0;
        var honorsTailContract = false;

        for (var pageIdx = 0; pageIdx < 5; pageIdx++)
        {
            var path = $"/api/sessions/{Uri.EscapeDataString(sessionId)}/messages?limit={pageLimit}&offset={offset}&order=latest&include_compacted=true";
            if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                path += $"&profile={Uri.EscapeDataString(profile)}";
            }

            using var doc = await GetJsonAsync(path, ct);
            var arr = ExtractMessagesJsonArray(doc.RootElement);
            if (arr.ValueKind != JsonValueKind.Array) break;

            var parsed = ParseMessagesArray(arr);
            var rawRowCount = arr.GetArrayLength();
            var pageOrder = ReadPaginationString(doc.RootElement, "order");
            var hasMore = ReadPaginationBool(doc.RootElement, "has_more");

            if (pageIdx == 0)
            {
                honorsTailContract = string.Equals(pageOrder, "latest", StringComparison.OrdinalIgnoreCase);
                // Older backends ignore `order=latest` and return from oldest row 0 when `limit` is present;
                // if page 0 hit the limit without honoring tail contract, fall back to one-shot full fetch.
                if (!honorsTailContract && rawRowCount >= pageLimit)
                {
                    var legacyPath = $"/api/sessions/{Uri.EscapeDataString(sessionId)}/messages";
                    if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        legacyPath += $"?profile={Uri.EscapeDataString(profile)}";
                    }
                    using var legacyDoc = await GetJsonAsync(legacyPath, ct);
                    var legacyArr = ExtractMessagesJsonArray(legacyDoc.RootElement);
                    var fullList = legacyArr.ValueKind == JsonValueKind.Array ? ParseMessagesArray(legacyArr) : parsed;
                    HermesDesktop.Core.Diagnostics.AppDiagnostics.Log(
                        "HISTORY",
                        $"Legacy full fetch session={sessionId} rows={fullList.Count}");
                    return fullList;
                }
            }

            if (parsed.Count > 0)
            {
                collectedPages.Add(parsed);
            }

            if (!honorsTailContract || rawRowCount == 0 || hasMore == false || (hasMore is null && rawRowCount < pageLimit))
            {
                break;
            }
            offset += rawRowCount;
        }

        if (collectedPages.Count == 0) return [];
        if (collectedPages.Count == 1)
        {
            HermesDesktop.Core.Diagnostics.AppDiagnostics.Log(
                "HISTORY",
                $"Fetched tail page session={sessionId} tailOrder={honorsTailContract} messages={collectedPages[0].Count}");
            return collectedPages[0];
        }

        // Prepend older pages before newer pages while deduplicating by RowId
        var merged = new List<ChatMessage>();
        var seenRowIds = new HashSet<int>();
        for (var p = collectedPages.Count - 1; p >= 0; p--)
        {
            foreach (var m in collectedPages[p])
            {
                if (m.RowId.HasValue && !seenRowIds.Add(m.RowId.Value)) continue;
                merged.Add(m);
            }
        }
        HermesDesktop.Core.Diagnostics.AppDiagnostics.Log(
            "HISTORY",
            $"Fetched {collectedPages.Count} tail pages session={sessionId} merged={merged.Count}");
        return merged;
    }

    private static JsonElement ExtractMessagesJsonArray(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Array
                ? m
                : default;

    private static string? ReadPaginationString(JsonElement root, string prop)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (GetString(root, prop) is { } top) return top;
        if (root.TryGetProperty("pagination", out var pag) && pag.ValueKind == JsonValueKind.Object)
        {
            return GetString(pag, prop);
        }
        return null;
    }

    private static bool? ReadPaginationBool(JsonElement root, string prop)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (GetBool(root, prop) is { } top) return top;
        if (root.TryGetProperty("pagination", out var pag) && pag.ValueKind == JsonValueKind.Object)
        {
            return GetBool(pag, prop);
        }
        return null;
    }

    public static IReadOnlyList<ChatMessage> ParseMessagesArray(JsonElement arr)
    {
        var list = new List<ChatMessage>();
        if (arr.ValueKind != JsonValueKind.Array) return list;

        var callsById = new Dictionary<string, int>(StringComparer.Ordinal);
        var idx = 0;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var displayKind = FirstNonEmpty(GetString(item, "display_kind"));
            if (string.Equals(displayKind, "hidden", StringComparison.OrdinalIgnoreCase)) continue;

            var rawRole = (GetString(item, "role") ?? GetString(item, "type") ?? "assistant").ToLowerInvariant();
            var isToolResult = rawRole == "tool" || rawRole.Contains("tool_result") || rawRole.Contains("tool-result");
            var rowId = GetInt(item, "id") ?? GetInt(item, "row_id");
            var messageIdStr = rowId?.ToString() ?? $"hist-{idx}";
            var (extractedText, partAttachments) = ExtractContentAndParts(item, messageIdStr);
            var content = FirstNonEmpty(GetString(item, "display_content"))
                ?? (string.IsNullOrEmpty(extractedText) ? (GetString(item, "text") ?? "") : extractedText);
            var reasoning = FirstNonEmpty(
                GetString(item, "display_reasoning"),
                GetString(item, "reasoning"),
                GetString(item, "reasoning_content"));
            var rawTs = FirstNonEmpty(GetString(item, "timestamp"), GetString(item, "created_at"));
            var ts = FormatTimestamp(rawTs);

            if (isToolResult)
            {
                var callId = FirstNonEmpty(
                    GetString(item, "tool_call_id"),
                    GetString(item, "call_id"),
                    GetString(item, "tool_use_id"));
                var toolName = FirstNonEmpty(GetString(item, "tool_name"), GetString(item, "name")) ?? "tool";

                if (!string.IsNullOrEmpty(callId) && callsById.TryGetValue(callId, out var existingIdx))
                {
                    var existing = list[existingIdx];
                    if (existing.Tool is not null)
                    {
                        list[existingIdx] = existing with
                        {
                            Tool = existing.Tool with
                            {
                                Output = string.IsNullOrEmpty(content) ? existing.Tool.Output : content,
                                Status = ToolStatus.Complete
                            }
                        };
                    }
                }
                else
                {
                    list.Add(new ChatMessage
                    {
                        Id = rowId?.ToString() ?? $"tool-{idx++}",
                        Role = MessageRole.Tool,
                        Content = toolName,
                        Timestamp = ts,
                        Tool = new ToolActivity(callId, toolName, null, content, ToolStatus.Complete),
                        RowId = rowId
                    });
                }
                continue;
            }

            var role = rawRole switch
            {
                "user" => MessageRole.User,
                "system" => MessageRole.System,
                "reasoning" or "thinking" => MessageRole.Reasoning,
                _ => MessageRole.Assistant
            };

            if (role == MessageRole.Assistant)
            {
                // Extract tool_calls array if present (matching MessageNormalizer)
                var hasToolCalls = item.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array && tcArr.GetArrayLength() > 0;
                if (hasToolCalls)
                {
                    if (!string.IsNullOrWhiteSpace(reasoning))
                    {
                        list.Add(new ChatMessage
                        {
                            Id = $"{rowId?.ToString() ?? idx.ToString()}-reason",
                            Role = MessageRole.Reasoning,
                            Content = "",
                            Reasoning = reasoning,
                            Timestamp = ts
                        });
                        reasoning = null;
                    }

                    foreach (var tc in tcArr.EnumerateArray())
                    {
                        if (tc.ValueKind != JsonValueKind.Object) continue;
                        var callId = FirstNonEmpty(GetString(tc, "id"), GetString(tc, "tool_call_id"), GetString(tc, "call_id"));
                        var fnName = FirstNonEmpty(GetString(tc, "name"));
                        string? fnArgs = null;
                        if (tc.TryGetProperty("function", out var fnObj) && fnObj.ValueKind == JsonValueKind.Object)
                        {
                            fnName ??= FirstNonEmpty(GetString(fnObj, "name"));
                            fnArgs = GetAnyString(fnObj, "arguments");
                        }
                        fnArgs ??= GetAnyString(tc, "arguments") ?? GetAnyString(tc, "input");
                        fnName ??= "tool";

                        var msgIdx = list.Count;
                        list.Add(new ChatMessage
                        {
                            Id = callId ?? $"tc-{idx++}",
                            Role = MessageRole.Tool,
                            Content = fnName,
                            Timestamp = ts,
                            Tool = new ToolActivity(callId, fnName, fnArgs, null, ToolStatus.Complete)
                        });
                        if (!string.IsNullOrEmpty(callId)) callsById[callId] = msgIdx;
                    }

                    if (string.IsNullOrWhiteSpace(content)) continue;
                }
                else if (string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(reasoning))
                {
                    list.Add(new ChatMessage
                    {
                        Id = rowId?.ToString() ?? $"reason-{idx++}",
                        Role = MessageRole.Reasoning,
                        Content = "",
                        Reasoning = reasoning,
                        Timestamp = ts,
                        RowId = rowId
                    });
                    continue;
                }
            }

            IReadOnlyList<Attachment>? finalAttachments = partAttachments.Count > 0 ? partAttachments : null;
            if (role == MessageRole.User)
            {
                var (cleaned, extractedAttachments) = UserAttachmentExtractor.Extract(content, messageIdStr, partAttachments);
                content = cleaned;
                finalAttachments = extractedAttachments.Count > 0 ? extractedAttachments : null;
            }

            list.Add(new ChatMessage
            {
                Id = rowId?.ToString() ?? $"hist-{idx++}",
                Role = role,
                Content = content,
                Timestamp = ts,
                Reasoning = reasoning,
                Attachments = finalAttachments,
                DisplayKind = displayKind,
                RowId = rowId
            });
        }

        return list;
    }

    public Task DeleteProfileAsync(string name, CancellationToken ct = default) =>
        DeleteAsync($"/api/profiles/{Uri.EscapeDataString(name)}", ct);

    public Task SetSessionPinnedAsync(string sessionId, bool pinned, string? profile = null, CancellationToken ct = default) =>
        SendSessionMutationAsync(
            HttpMethod.Patch,
            sessionId,
            new Dictionary<string, object?> { ["pinned"] = pinned },
            profile,
            ct);

    /// <summary>
    /// Deletes a conversation session on the Hermes Gateway, attaching mutation-snapshot identity
    /// headers/query parameters when supported by the host (and falling back cleanly on older hosts).
    /// </summary>
    public Task DeleteSessionAsync(string sessionId, string? profile = null, CancellationToken ct = default) =>
        SendSessionMutationAsync(HttpMethod.Delete, sessionId, bodyFields: null, profile, ct);

    public Task ArchiveSessionAsync(string sessionId, bool archived = true, string? profile = null, CancellationToken ct = default) =>
        SendSessionMutationAsync(
            HttpMethod.Patch,
            sessionId,
            new Dictionary<string, object?> { ["archived"] = archived },
            profile,
            ct);

    public Task RenameSessionAsync(string sessionId, string title, string? profile = null, CancellationToken ct = default) =>
        SendSessionMutationAsync(
            HttpMethod.Patch,
            sessionId,
            new Dictionary<string, object?> { ["title"] = title },
            profile,
            ct);

    private sealed record SessionMutationSnapshot(long Revision, long? Generation);

    private static string? NormalizeNonDefaultProfile(string? profile) =>
        string.IsNullOrWhiteSpace(profile) || profile.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? null
            : profile.Trim();

    private async Task SendSessionMutationAsync(
        HttpMethod method,
        string sessionId,
        IReadOnlyDictionary<string, object?>? bodyFields,
        string? profile,
        CancellationToken ct)
    {
        var encodedId = Uri.EscapeDataString(sessionId);
        var scopedProfile = NormalizeNonDefaultProfile(profile);
        var basePath = $"/api/sessions/{encodedId}";
        if (scopedProfile is not null)
            basePath += $"?profile={Uri.EscapeDataString(scopedProfile)}";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var snapshot = _supportsMutationSnapshot
                ? await TryGetSessionMutationSnapshotAsync(encodedId, scopedProfile, ct)
                : null;

            var requestPath = basePath;
            HttpContent? content = null;

            if (method == HttpMethod.Delete)
            {
                if (snapshot is not null)
                {
                    var sep = requestPath.Contains('?') ? "&" : "?";
                    requestPath += $"{sep}expected_revision={snapshot.Revision}&request_id={Guid.NewGuid():D}";
                    if (snapshot.Generation is long gen)
                        requestPath += $"&expected_generation={gen}";
                }
            }
            else
            {
                var payload = new Dictionary<string, object?>();
                if (bodyFields is not null)
                {
                    foreach (var kv in bodyFields) payload[kv.Key] = kv.Value;
                }
                if (scopedProfile is not null)
                    payload["profile"] = scopedProfile;
                if (snapshot is not null)
                {
                    payload["request_id"] = Guid.NewGuid().ToString("D");
                    payload["expected_revision"] = snapshot.Revision;
                    if (snapshot.Generation is long gen)
                        payload["expected_generation"] = gen;
                }
                content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            }

            var uri = new Uri($"{_baseUrl}{requestPath}");
            using var req = new HttpRequestMessage(method, uri) { Content = content };
            HermesAuthClient.ApplySecurityHeaders(req.Headers, uri, _secrets);
            using var resp = await _http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode) return;

            var detail = await resp.Content.ReadAsStringAsync(ct);
            if (attempt == 0 && resp.StatusCode == HttpStatusCode.Conflict && IsRetryableMutationConflict(detail))
            {
                _supportsMutationSnapshot = true;
                continue;
            }

            var message = string.IsNullOrWhiteSpace(detail)
                ? $"HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})"
                : $"HTTP {(int)resp.StatusCode}: {detail.Trim()}";
            throw new HttpRequestException(message, null, resp.StatusCode);
        }
    }

    private async Task<SessionMutationSnapshot?> TryGetSessionMutationSnapshotAsync(
        string encodedId,
        string? profile,
        CancellationToken ct)
    {
        var path = $"/api/sessions/{encodedId}/mutation-snapshot";
        if (!string.IsNullOrWhiteSpace(profile))
            path += $"?profile={Uri.EscapeDataString(profile)}";

        try
        {
            var uri = new Uri($"{_baseUrl}{path}");
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            HermesAuthClient.ApplySecurityHeaders(req.Headers, uri, _secrets);
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!body.Contains("session", StringComparison.OrdinalIgnoreCase))
                    _supportsMutationSnapshot = false;
                return null;
            }
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var rev = GetInt64(doc.RootElement, "runtime_revision");
            if (rev is null or < 0) return null;
            var gen = GetInt64(doc.RootElement, "runtime_generation");
            return new SessionMutationSnapshot(rev.Value, gen is >= 0 ? gen : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRetryableMutationConflict(string? detail) =>
        !string.IsNullOrEmpty(detail)
        && (detail.Contains("revision_conflict", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("stale_generation", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("mutation_identity_required", StringComparison.OrdinalIgnoreCase));

    public async Task<string> ReadWorkspaceTextAsync(string filePath, string? profile = null, CancellationToken ct = default)
    {
        var path = $"/api/fs/read-text?path={Uri.EscapeDataString(filePath)}";
        if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            path += $"&profile={Uri.EscapeDataString(profile)}";
        using var doc = await GetJsonAsync(path, ct);
        return GetString(doc.RootElement, "text") ?? "";
    }

    public async Task<byte[]?> ReadMediaBytesAsync(string pathOrUrl, string? profile = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return null;
        var trimmed = pathOrUrl.Trim();

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeDataUrl(trimmed);
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var extUri = new Uri(trimmed);
            using var req = new HttpRequestMessage(HttpMethod.Get, extUri);
            if (trimmed.StartsWith(_baseUrl, StringComparison.OrdinalIgnoreCase))
            {
                HermesAuthClient.ApplySecurityHeaders(req.Headers, extUri, _secrets);
            }
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }

        // 1. Try /api/fs/read-data-url first (fast base64 media read)
        try
        {
            var dataUrlPath = $"/api/fs/read-data-url?path={Uri.EscapeDataString(trimmed)}";
            if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                dataUrlPath += $"&profile={Uri.EscapeDataString(profile)}";
            }
            using var doc = await GetJsonAsync(dataUrlPath, ct);
            var dataUrl = GetString(doc.RootElement, "dataUrl");
            if (!string.IsNullOrWhiteSpace(dataUrl))
            {
                var bytes = DecodeDataUrl(dataUrl);
                if (bytes is { Length: > 0 }) return bytes;
            }
        }
        catch
        {
            // Fall through to raw download
        }

        // 2. Fallback to /api/fs/download
        try
        {
            var dlPath = $"/api/fs/download?path={Uri.EscapeDataString(trimmed)}";
            if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                dlPath += $"&profile={Uri.EscapeDataString(profile)}";
            }
            var dlUri = new Uri($"{_baseUrl}{dlPath}");
            using var req = new HttpRequestMessage(HttpMethod.Get, dlUri);
            HermesAuthClient.ApplySecurityHeaders(req.Headers, dlUri, _secrets);
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadAsByteArrayAsync(ct);
            }
        }
        catch
        {
            // Unavailable
        }

        return null;
    }

    private static byte[]? DecodeDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0) return null;
        var header = dataUrl[..comma];
        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch
        {
            return null;
        }
    }

    private static (string Text, List<Attachment> Attachments) ExtractContentAndParts(JsonElement item, string messageId)
    {
        var attachments = new List<Attachment>();
        if (!item.TryGetProperty("content", out var contentEl))
        {
            return ("", attachments);
        }

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            return (contentEl.GetString() ?? "", attachments);
        }

        if (contentEl.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            var imgIdx = 0;
            foreach (var part in contentEl.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object) continue;
                var type = GetString(part, "type")?.ToLowerInvariant();
                if (type is "text" or "input_text")
                {
                    var t = GetString(part, "text");
                    if (!string.IsNullOrEmpty(t))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(t);
                    }
                }
                else if (type is "image_url" or "input_image" or "image")
                {
                    string? url = null;
                    if (part.TryGetProperty("image_url", out var iu))
                    {
                        url = iu.ValueKind == JsonValueKind.String ? iu.GetString() : GetString(iu, "url");
                    }
                    url ??= GetString(part, "url") ?? GetString(part, "data");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        attachments.Add(new Attachment(
                            Id: $"{messageId}-part-img-{imgIdx++}",
                            Name: url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "Image" : Path.GetFileName(url),
                            Uri: url,
                            MimeType: null,
                            Kind: AttachmentKind.Image));
                    }
                }
            }
            return (sb.ToString(), attachments);
        }

        return ("", attachments);
    }

    public async Task<IReadOnlyList<CronJobSummary>> GetCronJobsAsync(string? profile = null, CancellationToken ct = default)
    {
        var path = "/api/cron/jobs";
        if (!string.IsNullOrWhiteSpace(profile)) path += $"?profile={Uri.EscapeDataString(profile)}";
        using var doc = await GetJsonAsync(path, ct);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("jobs", out var j) && j.ValueKind == JsonValueKind.Array
                ? j
                : default;

        var list = new List<CronJobSummary>();
        if (arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = FirstNonEmpty(GetString(item, "id"));
            if (id is null) continue;

            var scheduleDisplay = FirstNonEmpty(GetString(item, "schedule_display"), GetString(item, "scheduleDisplay"));
            string? rawSchedule = null;
            if (item.TryGetProperty("schedule", out var schedEl))
            {
                if (schedEl.ValueKind == JsonValueKind.String)
                {
                    rawSchedule = schedEl.GetString()?.Trim();
                    scheduleDisplay ??= rawSchedule;
                }
                else if (schedEl.ValueKind == JsonValueKind.Object)
                {
                    var expr = FirstNonEmpty(GetString(schedEl, "expr"));
                    var mins = GetInt(schedEl, "minutes");
                    var runAt = FirstNonEmpty(GetString(schedEl, "run_at"), GetString(schedEl, "runAt"));
                    var disp = FirstNonEmpty(GetString(schedEl, "display"));
                    rawSchedule = expr ?? (mins.HasValue ? $"every {mins.Value}m" : null) ?? runAt ?? disp;
                    scheduleDisplay ??= disp ?? rawSchedule;
                }
            }
            rawSchedule ??= scheduleDisplay;

            list.Add(new CronJobSummary(
                Id: id,
                Name: FirstNonEmpty(GetString(item, "name")) ?? id,
                Enabled: GetBool(item, "enabled") ?? true,
                ScheduleDisplay: scheduleDisplay ?? "Scheduled",
                State: FirstNonEmpty(GetString(item, "state")),
                LastRunAt: FirstNonEmpty(GetString(item, "last_run_at"), GetString(item, "lastRunAt")),
                NextRunAt: FirstNonEmpty(GetString(item, "next_run_at"), GetString(item, "nextRunAt")),
                LastError: FirstNonEmpty(GetString(item, "last_error"), GetString(item, "lastError")),
                Prompt: FirstNonEmpty(GetString(item, "prompt"), GetString(item, "script")),
                RawSchedule: rawSchedule,
                Deliver: FirstNonEmpty(GetString(item, "deliver")) ?? "local"));
        }
        return list;
    }

    public async Task CronJobActionAsync(string jobId, string action, string? profile = null, CancellationToken ct = default)
    {
        var path = $"/api/cron/jobs/{Uri.EscapeDataString(jobId)}/{action}";
        if (!string.IsNullOrWhiteSpace(profile)) path += $"?profile={Uri.EscapeDataString(profile)}";
        await SendJsonAsync(HttpMethod.Post, path, new { profile }, ct);
    }

    public async Task CreateCronJobAsync(
        string name,
        string prompt,
        string schedule,
        string deliver = "local",
        string? profile = null,
        CancellationToken ct = default)
    {
        var path = "/api/cron/jobs";
        if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            path += $"?profile={Uri.EscapeDataString(profile)}";
        await SendJsonAsync(HttpMethod.Post, path, new
        {
            name = name.Trim(),
            prompt = prompt.Trim(),
            schedule = schedule.Trim(),
            deliver = string.IsNullOrWhiteSpace(deliver) ? "local" : deliver.Trim()
        }, ct);
    }

    public async Task UpdateCronJobAsync(
        string jobId,
        string name,
        string prompt,
        string schedule,
        string deliver = "local",
        string? profile = null,
        CancellationToken ct = default)
    {
        var path = $"/api/cron/jobs/{Uri.EscapeDataString(jobId)}";
        if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            path += $"?profile={Uri.EscapeDataString(profile)}";
        await SendJsonAsync(HttpMethod.Put, path, new
        {
            updates = new
            {
                name = name.Trim(),
                prompt = prompt.Trim(),
                schedule = schedule.Trim(),
                deliver = string.IsNullOrWhiteSpace(deliver) ? "local" : deliver.Trim()
            }
        }, ct);
    }

    public async Task DeleteCronJobAsync(string jobId, string? profile = null, CancellationToken ct = default)
    {
        var path = $"/api/cron/jobs/{Uri.EscapeDataString(jobId)}";
        if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            path += $"?profile={Uri.EscapeDataString(profile)}";
        await DeleteAsync(path, ct);
    }

    public async Task<IReadOnlyList<BotProfileCapability>> GetSkillsAsync(string? profile = null, CancellationToken ct = default)
    {
        var path = "/api/skills";
        if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            path += $"?profile={Uri.EscapeDataString(profile)}";
        using var doc = await GetJsonAsync(path, ct);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("skills", out var s) && s.ValueKind == JsonValueKind.Array
                ? s
                : default;
        var list = new List<BotProfileCapability>();
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = FirstNonEmpty(GetString(item, "name"));
            if (string.IsNullOrWhiteSpace(name)) continue;
            list.Add(new BotProfileCapability(
                Name: name,
                Label: FirstNonEmpty(GetString(item, "category")),
                Description: GetString(item, "description") ?? "",
                ToolCount: null,
                Enabled: GetBool(item, "enabled") ?? true));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public async Task<IReadOnlyList<BotProfileCapability>> GetToolsetsAsync(string? profile = null, CancellationToken ct = default)
    {
        var path = "/api/tools/toolsets";
        if (!string.IsNullOrWhiteSpace(profile) && !profile.Equals("default", StringComparison.OrdinalIgnoreCase))
            path += $"?profile={Uri.EscapeDataString(profile)}";
        using var doc = await GetJsonAsync(path, ct);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("toolsets", out var t) && t.ValueKind == JsonValueKind.Array
                ? t
                : default;
        var list = new List<BotProfileCapability>();
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = FirstNonEmpty(GetString(item, "name"));
            if (string.IsNullOrWhiteSpace(name)) continue;
            int? toolCount = item.TryGetProperty("tools", out var toolsEl) && toolsEl.ValueKind == JsonValueKind.Array
                ? toolsEl.GetArrayLength()
                : GetInt(item, "tool_count");
            list.Add(new BotProfileCapability(
                Name: name,
                Label: FirstNonEmpty(GetString(item, "label")),
                Description: GetString(item, "description") ?? "",
                ToolCount: toolCount,
                Enabled: GetBool(item, "enabled") ?? true));
        }
        return list;
    }

    public async Task ToggleSkillAsync(string name, bool enabled, string? profile = null, CancellationToken ct = default)
    {
        await SendJsonAsync(HttpMethod.Put, "/api/skills/toggle", new
        {
            name,
            enabled,
            profile = string.IsNullOrWhiteSpace(profile) ? "default" : profile
        }, ct);
    }

    public async Task ToggleToolsetAsync(string name, bool enabled, string? profile = null, CancellationToken ct = default)
    {
        var path = $"/api/tools/toolsets/{Uri.EscapeDataString(name)}";
        await SendJsonAsync(HttpMethod.Put, path, new
        {
            enabled,
            profile = string.IsNullOrWhiteSpace(profile) ? "default" : profile
        }, ct);
    }

    public async Task<IReadOnlyList<KanbanBoardSummary>> GetKanbanBoardsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/plugins/kanban/boards", ct);
        var list = new List<KanbanBoardSummary>();
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("boards", out var b) && b.ValueKind == JsonValueKind.Array
                ? b
                : default;
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var slug = FirstNonEmpty(GetString(item, "slug"), GetString(item, "id"));
            if (slug is null) continue;
            list.Add(new KanbanBoardSummary(
                Slug: slug,
                Title: FirstNonEmpty(GetString(item, "title"), GetString(item, "name"), slug) ?? slug,
                TaskCount: GetInt(item, "task_count") ?? GetInt(item, "count") ?? 0));
        }
        return list;
    }

    public async Task<IReadOnlyList<KanbanTaskSummary>> GetKanbanBoardTasksAsync(string slug, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/plugins/kanban/board?board={Uri.EscapeDataString(slug)}", ct);
        var list = new List<KanbanTaskSummary>();
        var arr = doc.RootElement.TryGetProperty("tasks", out var t) && t.ValueKind == JsonValueKind.Array
            ? t
            : default;
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = FirstNonEmpty(GetString(item, "id"));
            if (id is null) continue;
            list.Add(new KanbanTaskSummary(
                Id: id,
                Title: FirstNonEmpty(GetString(item, "title")) ?? id,
                Status: FirstNonEmpty(GetString(item, "status")) ?? "backlog",
                Assignee: FirstNonEmpty(GetString(item, "assignee")),
                Priority: FirstNonEmpty(GetString(item, "priority")),
                LatestSummary: FirstNonEmpty(GetString(item, "latest_summary"), GetString(item, "summary"))));
        }
        return list;
    }

    public async Task<IReadOnlyList<PulseItemSummary>> GetPulseFeedAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/plugins/conduit_push/pulse/feed", ct);
        var list = new List<PulseItemSummary>();
        var arr = doc.RootElement.TryGetProperty("cards", out var c) && c.ValueKind == JsonValueKind.Array
            ? c
            : doc.RootElement.TryGetProperty("items", out var i) && i.ValueKind == JsonValueKind.Array
                ? i
                : default;
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = FirstNonEmpty(GetString(item, "id")) ?? Guid.NewGuid().ToString("N");
            list.Add(new PulseItemSummary(
                Id: id,
                Title: FirstNonEmpty(GetString(item, "title")) ?? "Brief",
                Body: FirstNonEmpty(GetString(item, "body"), GetString(item, "summary")) ?? "",
                Topic: FirstNonEmpty(GetString(item, "topic"), GetString(item, "kind")),
                CreatedAt: FirstNonEmpty(GetString(item, "created_at"))));
        }
        return list;
    }

    public async Task<IReadOnlyList<ArtifactSummary>> GetArtifactsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/plugins/conduit_push/artifacts?limit=60", ct);
        var list = new List<ArtifactSummary>();
        var arr = doc.RootElement.TryGetProperty("artifacts", out var a) && a.ValueKind == JsonValueKind.Array
            ? a
            : doc.RootElement.TryGetProperty("items", out var i) && i.ValueKind == JsonValueKind.Array
                ? i
                : default;
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var path = FirstNonEmpty(GetString(item, "path"), GetString(item, "file")) ?? "";
            list.Add(new ArtifactSummary(
                Id: FirstNonEmpty(GetString(item, "id"), path) ?? Guid.NewGuid().ToString("N"),
                Name: FirstNonEmpty(GetString(item, "name"), GetString(item, "title"), Path.GetFileName(path)) ?? "Artifact",
                Path: path,
                MimeType: FirstNonEmpty(GetString(item, "mime_type"), GetString(item, "mimeType")),
                IsPinned: GetBool(item, "pinned") == true));
        }
        return list;
    }

    public async Task<HermesUpdateCheck> GetHermesUpdateCheckAsync(bool force = false, CancellationToken ct = default)
    {
        var path = force ? "/api/hermes/update/check?force=true" : "/api/hermes/update/check";
        using var doc = await GetJsonAsync(path, ct);
        return ParseHermesUpdateCheck(doc.RootElement);
    }

    public async Task<HermesUpdateStartResult> StartHermesUpdateAsync(CancellationToken ct = default)
    {
        using var doc = await PostJsonForDocumentAsync("/api/hermes/update", body: null, ct);
        return ParseHermesUpdateStart(doc.RootElement);
    }

    public async Task<ActionStatusSnapshot> GetActionStatusAsync(string actionName, int lines = 40, CancellationToken ct = default)
    {
        var safeAction = Uri.EscapeDataString(string.IsNullOrWhiteSpace(actionName) ? "hermes-update" : actionName.Trim());
        using var doc = await GetJsonAsync($"/api/actions/{safeAction}/status?lines={lines}", ct);
        return ParseActionStatus(doc.RootElement);
    }

    public async Task RestartGatewayAsync(CancellationToken ct = default)
    {
        using var _ = await PostJsonForDocumentAsync("/api/gateway/restart", body: null, ct);
    }

    public async Task<GatewayRuntimeStatus> GetGatewayStatusAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/status", ct);
        return ParseGatewayRuntimeStatus(doc.RootElement);
    }

    public static HermesUpdateCheck ParseHermesUpdateCheck(JsonElement root)
    {
        var commits = new List<HermesUpdateCommit>();
        if (root.TryGetProperty("commits", out var commitsEl) && commitsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in commitsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var sha = FirstNonEmpty(GetString(item, "sha"));
                if (sha is null) continue;
                commits.Add(new HermesUpdateCommit(sha, GetString(item, "summary")?.Trim() ?? ""));
            }
        }

        return new HermesUpdateCheck(
            InstallMethod: FirstNonEmpty(GetString(root, "install_method")) ?? "unknown",
            CurrentVersion: FirstNonEmpty(GetString(root, "current_version")) ?? "",
            Behind: GetInt(root, "behind"),
            UpdateAvailable: GetBool(root, "update_available") == true,
            CanApply: GetBool(root, "can_apply") == true,
            UpdateCommand: FirstNonEmpty(GetString(root, "update_command")),
            Message: FirstNonEmpty(GetString(root, "message")),
            SourceVersion: FirstNonEmpty(GetString(root, "source_version")),
            RollbackTo: FirstNonEmpty(GetString(root, "rollback_to")),
            Commits: commits);
    }

    public static HermesUpdateStartResult ParseHermesUpdateStart(JsonElement root)
    {
        var ok = GetBool(root, "ok") ?? true;
        var name = FirstNonEmpty(GetString(root, "name")) ?? "hermes-update";
        return new HermesUpdateStartResult(
            Ok: ok,
            ActionName: name,
            ActionId: FirstNonEmpty(GetString(root, "action_id")),
            Message: FirstNonEmpty(GetString(root, "message")),
            UpdateCommand: FirstNonEmpty(GetString(root, "update_command")));
    }

    public static ActionStatusSnapshot ParseActionStatus(JsonElement root)
    {
        var running = GetBool(root, "running") == true;
        var exitCode = GetInt(root, "exit_code");
        var lines = new List<string>();
        if (root.TryGetProperty("lines", out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in linesEl.EnumerateArray())
            {
                lines.Add(l.ValueKind == JsonValueKind.String ? (l.GetString() ?? "") : l.GetRawText());
            }
        }

        string? receiptOutcome = null;
        string? receiptActionId = null;
        if (root.TryGetProperty("receipt", out var receiptEl) && receiptEl.ValueKind == JsonValueKind.Object)
        {
            receiptOutcome = FirstNonEmpty(GetString(receiptEl, "outcome"));
            receiptActionId = FirstNonEmpty(GetString(receiptEl, "action_id"));
        }

        return new ActionStatusSnapshot(running, exitCode, lines, receiptOutcome, receiptActionId);
    }

    public static GatewayRuntimeStatus ParseGatewayRuntimeStatus(JsonElement root) => new(
        Running: GetBool(root, "gateway_running") == true,
        State: FirstNonEmpty(GetString(root, "gateway_state")),
        Pid: GetInt(root, "gateway_pid"));

    public static ActionPollVerdict EvaluateHermesUpdateVerdict(ActionStatusSnapshot status, string? actionId)
    {
        if (status.Running) return ActionPollVerdict.Running;
        if (!string.IsNullOrWhiteSpace(actionId))
        {
            var marker = $"=== hermes-update completed {actionId} ===";
            foreach (var line in status.Lines)
            {
                if (line.Contains(marker, StringComparison.Ordinal))
                    return ActionPollVerdict.Succeeded;
            }
            if (string.Equals(status.ReceiptActionId, actionId, StringComparison.Ordinal))
            {
                if (status.ReceiptOutcome is "success" or "partial") return ActionPollVerdict.Succeeded;
                if (status.ReceiptOutcome == "failed") return ActionPollVerdict.Failed;
            }
        }
        if (status.ExitCode.HasValue)
            return status.ExitCode.Value == 0 ? ActionPollVerdict.Succeeded : ActionPollVerdict.Failed;
        return ActionPollVerdict.Unknown;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        var uri = new Uri($"{_baseUrl}{path}");
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        HermesAuthClient.ApplySecurityHeaders(req.Headers, uri, _secrets);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("REST_HTTP_ERR", $"GET {path} -> {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task<JsonDocument> PostJsonForDocumentAsync(string path, object? body, CancellationToken ct)
    {
        var uri = new Uri($"{_baseUrl}{path}");
        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HermesAuthClient.ApplySecurityHeaders(req.Headers, uri, _secrets);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("REST_HTTP_ERR", $"POST {path} -> {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task SendJsonAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        var uri = new Uri($"{_baseUrl}{path}");
        using var req = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        HermesAuthClient.ApplySecurityHeaders(req.Headers, uri, _secrets);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        var uri = new Uri($"{_baseUrl}{path}");
        using var req = new HttpRequestMessage(HttpMethod.Delete, uri);
        HermesAuthClient.ApplySecurityHeaders(req.Headers, uri, _secrets);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();

    private static string FormatRelativeEpoch(double? epochSeconds)
    {
        if (epochSeconds is null or <= 0) return "";
        var dt = DateTimeOffset.FromUnixTimeSeconds((long)epochSeconds.Value).ToLocalTime();
        var now = DateTimeOffset.Now;
        if (dt.Date == now.Date) return dt.ToString("HH:mm");
        if (dt.Date == now.AddDays(-1).Date) return "Yesterday";
        return dt.ToString("MM/dd");
    }

    private static string FormatTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (DateTimeOffset.TryParse(raw, out var dto))
            return dto.ToLocalTime().ToString("HH:mm");
        if (double.TryParse(raw, out var epoch) && epoch > 1_000_000_000)
            return DateTimeOffset.FromUnixTimeSeconds((long)epoch).ToLocalTime().ToString("HH:mm");
        return raw;
    }

    private static string? GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static string? GetAnyString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText())
            : null;

    private static bool? GetBool(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : null;

    private static int? GetInt(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : null;

    private static long? GetInt64(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) ? v : null;

    private static double? GetDouble(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v) ? v : null;

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)) return c.Trim();
        }
        return null;
    }
}

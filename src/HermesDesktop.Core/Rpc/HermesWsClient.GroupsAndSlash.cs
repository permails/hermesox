using System.Text.Json;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;

namespace HermesDesktop.Core.Rpc;

public sealed partial class HermesWsClient
{
    public async Task<(double Percent, int Used, int Max)?> GetContextBreakdownAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var res = await RpcAsync(
            "session.context_breakdown",
            new Dictionary<string, object?> { ["session_id"] = sessionId },
            timeout: TimeSpan.FromSeconds(10),
            ct: ct);

        var max = GetInt(res, "context_max") ?? 0;
        var used = GetInt(res, "context_used") ?? GetInt(res, "estimated_total") ?? 0;
        if (used <= 0 && res.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            var sum = 0;
            foreach (var c in cats.EnumerateArray())
            {
                sum += GetInt(c, "tokens") ?? 0;
            }
            if (sum > 0) used = sum;
        }
        var rawPct = GetDouble(res, "context_percent");
        if (max <= 0 && used <= 0 && rawPct is null) return null;
        var pct = StreamEventParser.NormalizeContextPercent(rawPct, used, max);
        return (pct, used, max);
    }

    public async Task<IReadOnlyList<ModelProviderOption>> GetModelProvidersAsync(
        string? sessionId = null,
        bool refresh = false,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?> { ["explicit_only"] = true };
        if (!string.IsNullOrWhiteSpace(sessionId)) p["session_id"] = sessionId;
        if (refresh) p["refresh"] = true;

        JsonElement res;
        try
        {
            res = await RpcAsync("model.options", p, timeout: TimeSpan.FromSeconds(refresh ? 30 : 12), ct: ct);
        }
        catch (RpcException) when (refresh)
        {
            p.Remove("refresh");
            res = await RpcAsync("model.options", p, timeout: TimeSpan.FromSeconds(12), ct: ct);
        }

        var providers = DecodeModelProviders(res);
        if (providers.Count == 0)
        {
            var fallbackParams = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(sessionId)) fallbackParams["session_id"] = sessionId;
            try
            {
                var fallbackRes = await RpcAsync("model.options", fallbackParams, timeout: TimeSpan.FromSeconds(12), ct: ct);
                providers = DecodeModelProviders(fallbackRes);
            }
            catch
            {
                // Older gateways or empty explicit catalog fall back to REST provider catalog
            }
        }
        return providers;
    }

    public static IReadOnlyList<ModelProviderOption> DecodeModelProviders(JsonElement root)
    {
        var providers = new List<ModelProviderOption>();
        JsonElement arr = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            arr = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (!(root.TryGetProperty("providers", out arr) && arr.ValueKind == JsonValueKind.Array)
                && !(root.TryGetProperty("options", out arr) && arr.ValueKind == JsonValueKind.Array))
            {
                return providers;
            }
        }
        else
        {
            return providers;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var slug = TrimNonEmpty(GetString(item, "slug"))
                ?? TrimNonEmpty(GetString(item, "id"))
                ?? TrimNonEmpty(GetString(item, "provider"))
                ?? TrimNonEmpty(GetString(item, "name"));
            if (string.IsNullOrWhiteSpace(slug)) continue;

            var displayName = TrimNonEmpty(GetString(item, "name"))
                ?? TrimNonEmpty(GetString(item, "label"))
                ?? TrimNonEmpty(GetString(item, "display_name"))
                ?? slug;
            var isCurrent = GetBool(item, "is_current") == true
                || GetBool(item, "current") == true
                || GetBool(item, "active") == true;

            var aliases = new List<string>();
            var aliasSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddAlias(string? candidate)
            {
                var trimmed = TrimNonEmpty(candidate);
                if (trimmed is not null && aliasSeen.Add(trimmed))
                {
                    aliases.Add(trimmed);
                }
            }

            AddAlias(slug);
            AddAlias(GetString(item, "id"));
            AddAlias(GetString(item, "provider"));
            AddAlias(GetString(item, "endpoint_id"));
            if (item.TryGetProperty("aliases", out var aliasesEl) && aliasesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in aliasesEl.EnumerateArray())
                {
                    if (a.ValueKind == JsonValueKind.String) AddAlias(a.GetString());
                }
            }

            var models = new List<string>();
            var modelSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddModel(string? candidate)
            {
                var trimmed = TrimNonEmpty(candidate);
                if (trimmed is not null && modelSeen.Add(trimmed))
                {
                    models.Add(trimmed);
                }
            }

            AddModel(GetString(item, "default_model"));
            AddModel(GetString(item, "model"));
            if (item.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in modelsEl.EnumerateArray())
                {
                    if (m.ValueKind == JsonValueKind.String)
                    {
                        AddModel(m.GetString());
                    }
                    else if (m.ValueKind == JsonValueKind.Object)
                    {
                        var midObj = TrimNonEmpty(GetString(m, "id"))
                            ?? TrimNonEmpty(GetString(m, "model"))
                            ?? TrimNonEmpty(GetString(m, "name"))
                            ?? TrimNonEmpty(GetString(m, "slug"));
                        AddModel(midObj);
                    }
                }
            }

            providers.Add(new ModelProviderOption(slug, displayName, aliases, isCurrent, models));
        }

        return providers;
    }

    public static IReadOnlyList<ModelProviderOption> MergeModelProviders(
        params IEnumerable<ModelProviderOption>[] sources)
    {
        var merged = new List<ModelProviderOption>();

        static bool IsCustomSlug(string s) => s.StartsWith("custom:", StringComparison.OrdinalIgnoreCase);

        static int FindMatchIndex(List<ModelProviderOption> list, ModelProviderOption candidate)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Slug, candidate.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            var candCustom = IsCustomSlug(candidate.Slug);
            for (var i = 0; i < list.Count; i++)
            {
                var existing = list[i];
                // Never collapse custom:<id> endpoints into identically named built-in providers
                if (IsCustomSlug(existing.Slug) != candCustom)
                {
                    continue;
                }

                if (existing.Aliases.Any(a => string.Equals(a, candidate.Slug, StringComparison.OrdinalIgnoreCase))
                    || candidate.Aliases.Any(a => string.Equals(a, existing.Slug, StringComparison.OrdinalIgnoreCase)))
                {
                    return i;
                }
            }

            return -1;
        }

        foreach (var source in sources)
        {
            if (source is null) continue;
            foreach (var item in source)
            {
                if (string.IsNullOrWhiteSpace(item.Slug)) continue;
                var idx = FindMatchIndex(merged, item);
                if (idx < 0)
                {
                    var normAliases = item.Aliases
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Select(a => a.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var normModels = item.Models
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Select(m => m.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    merged.Add(new ModelProviderOption(
                        item.Slug.Trim(),
                        string.IsNullOrWhiteSpace(item.DisplayName) ? item.Slug.Trim() : item.DisplayName.Trim(),
                        normAliases,
                        item.IsCurrent,
                        normModels));
                    continue;
                }

                var current = merged[idx];
                var preferIncomingSlug = IsCustomSlug(item.Slug) && !IsCustomSlug(current.Slug);
                var nextSlug = preferIncomingSlug ? item.Slug.Trim() : current.Slug;
                var nextDisplay = string.Equals(current.DisplayName, current.Slug, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.DisplayName)
                    && !string.Equals(item.DisplayName, item.Slug, StringComparison.OrdinalIgnoreCase)
                        ? item.DisplayName.Trim()
                        : current.DisplayName;

                var aliasList = new List<string>(current.Aliases.Count + item.Aliases.Count + 2);
                var aliasSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in current.Aliases.Concat([current.Slug, item.Slug]).Concat(item.Aliases))
                {
                    var trimmed = TrimNonEmpty(a);
                    if (trimmed is not null && aliasSeen.Add(trimmed)) aliasList.Add(trimmed);
                }

                var modelList = new List<string>(current.Models.Count + item.Models.Count);
                var modelSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in current.Models.Concat(item.Models))
                {
                    var trimmed = TrimNonEmpty(m);
                    if (trimmed is not null && modelSeen.Add(trimmed)) modelList.Add(trimmed);
                }

                merged[idx] = new ModelProviderOption(
                    nextSlug,
                    nextDisplay,
                    aliasList,
                    current.IsCurrent || item.IsCurrent,
                    modelList);
            }
        }

        return merged;
    }

    private static string? TrimNonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task ConfigureBotModelAsync(
        string name,
        string provider,
        string model,
        bool confirmExpensive = true,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["model"] = model.Trim()
        };
        if (!string.IsNullOrWhiteSpace(provider)) p["provider"] = provider.Trim();
        if (confirmExpensive) p["confirm_expensive_model"] = true;

        await _profileConfigLock.WaitAsync(ct);
        try
        {
            await RpcAsync("profiles.configure", p, timeout: TimeSpan.FromSeconds(45), ct: ct);
        }
        finally
        {
            _profileConfigLock.Release();
        }
    }

    public async Task RenameSessionAsync(string sessionId, string title, string? profile = null, CancellationToken ct = default)
    {
        if (!SpeaksSessionAuthority)
        {
            var p = new Dictionary<string, object?>
            {
                ["session_id"] = sessionId,
                ["title"] = title
            };
            if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile.Trim();
            try
            {
                await RpcAsync("session.title", p, ct: ct);
                return;
            }
            catch (RpcException ex) when (SpeaksSessionAuthority || ex.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                // Fall through to session.mutate below
            }
        }

        var mutate = new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["operation"] = "rename",
            ["payload"] = new Dictionary<string, object?> { ["title"] = title }
        };
        if (!string.IsNullOrWhiteSpace(profile)) mutate["profile"] = profile.Trim();
        await RpcAsync("session.mutate", mutate, ct: ct);
    }

    public async Task<string> CompressSessionWithSummaryAsync(
        string sessionId,
        string? focusTopic = null,
        string? profile = null,
        CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromSeconds(180);
        var trimmedFocus = focusTopic?.Trim();
        JsonElement result;

        if (!SpeaksSessionAuthority)
        {
            var p = new Dictionary<string, object?> { ["session_id"] = sessionId };
            if (!string.IsNullOrWhiteSpace(trimmedFocus)) p["focus_topic"] = trimmedFocus;
            if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile.Trim();
            result = await RpcAsync("session.compress", p, timeout: timeout, ct: ct);
        }
        else
        {
            var payload = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(trimmedFocus)) payload["focus"] = trimmedFocus;
            var mutate = new Dictionary<string, object?>
            {
                ["session_id"] = sessionId,
                ["operation"] = "compress",
                ["payload"] = payload
            };
            if (_sessionGenerations.TryGetValue(sessionId, out var gen))
            {
                mutate["execution_generation"] = gen;
            }
            if (!string.IsNullOrWhiteSpace(profile)) mutate["profile"] = profile.Trim();
            result = await RpcAsync("session.mutate", mutate, timeout: timeout, ct: ct);
        }

        var msg = GetString(result, "message");
        var status = GetString(result, "status");
        var removed = GetInt(result, "removed") ?? GetInt(result, "compressed_messages");
        if (!string.IsNullOrWhiteSpace(msg)) return msg;
        if (removed.HasValue)
        {
            return AppLocalization.T(
                $"Compressed session context ({removed.Value} messages compacted).",
                $"已压缩会话上下文（共精简 {removed.Value} 条消息）。");
        }
        return !string.IsNullOrWhiteSpace(status)
            ? AppLocalization.T($"Context compression: {status}", $"上下文压缩结果：{status}")
            : AppLocalization.T("Session context compressed.", "会话上下文已压缩。");
    }

    public async Task<IReadOnlyList<SlashCommandDescriptor>> CommandsCatalogAsync(CancellationToken ct = default)
    {
        var result = await RpcAsync("commands.catalog", null, timeout: TimeSpan.FromSeconds(10), ct: ct);
        return SlashCommandParser.DecodeCatalog(result);
    }

    public async Task<SlashExecOutcome> ExecuteSlashAsync(string sessionId, string command, CancellationToken ct = default)
    {
        var cleaned = command.Trim().TrimStart('/');
        var result = await RpcAsync(
            "slash.exec",
            new Dictionary<string, object?>
            {
                ["session_id"] = sessionId,
                ["command"] = cleaned
            },
            timeout: TimeSpan.FromSeconds(45),
            ct: ct);
        return SlashCommandParser.DecodeExecOutcome(result);
    }

    public async Task<SlashExecOutcome> DispatchCommandAsync(string sessionId, string name, string arg, CancellationToken ct = default)
    {
        var cleanedName = name.Trim().TrimStart('/');
        var result = await RpcAsync(
            "command.dispatch",
            new Dictionary<string, object?>
            {
                ["session_id"] = sessionId,
                ["name"] = cleanedName,
                ["arg"] = arg
            },
            timeout: TimeSpan.FromSeconds(45),
            ct: ct);
        return SlashCommandParser.DecodeExecOutcome(result);
    }

    public async Task<string?> StartSideAgentAsync(string method, string sessionId, string text, CancellationToken ct = default)
    {
        var result = await RpcAsync(
            method,
            new Dictionary<string, object?>
            {
                ["session_id"] = sessionId,
                ["text"] = text
            },
            timeout: TimeSpan.FromSeconds(30),
            ct: ct);
        return GetString(result, "task_id") ?? GetString(result, "id");
    }

    public async Task<string> ReloadMcpAsync(string sessionId, bool confirm = true, CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?> { ["session_id"] = sessionId };
        if (confirm) p["confirm"] = true;
        var result = await RpcAsync("reload.mcp", p, timeout: TimeSpan.FromSeconds(60), ct: ct);
        return GetString(result, "output")
               ?? GetString(result, "message")
               ?? AppLocalization.T("MCP servers reloaded.", "MCP 服务已重载。");
    }

    public async Task<string> ReloadSkillsAsync(string sessionId, CancellationToken ct = default)
    {
        var result = await RpcAsync(
            "skills.reload",
            new Dictionary<string, object?> { ["session_id"] = sessionId },
            timeout: TimeSpan.FromSeconds(30),
            ct: ct);
        return GetString(result, "output")
               ?? GetString(result, "message")
               ?? AppLocalization.T("Skills reloaded.", "技能目录已重载。");
    }

    public async Task<int> StopProcessesAsync(string sessionId, CancellationToken ct = default)
    {
        var result = await RpcAsync(
            "process.stop",
            new Dictionary<string, object?> { ["session_id"] = sessionId },
            timeout: TimeSpan.FromSeconds(15),
            ct: ct);
        return GetInt(result, "killed") ?? 0;
    }

    // ---- Multi-Agent Group Chats (groups.*) ----

    public async Task<GroupCapabilities> GroupsCapabilitiesAsync(CancellationToken ct = default)
    {
        var result = await RpcAsync("groups.capabilities", null, timeout: TimeSpan.FromSeconds(10), ct: ct);
        return GroupDecoders.DecodeCapabilities(result);
    }

    public async Task<IReadOnlyList<GroupRoom>> GroupsListAsync(
        bool includeDisbanded = false,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?> { ["include_disbanded"] = includeDisbanded };
        if (limit.HasValue) p["limit"] = limit.Value;
        if (offset.HasValue) p["offset"] = offset.Value;
        var result = await RpcAsync("groups.list", p, ct: ct);
        return GroupDecoders.DecodeRooms(result);
    }

    public async Task<GroupRoom?> GroupsCreateAsync(
        string roomId,
        string name,
        IReadOnlyList<GroupMember> members,
        CancellationToken ct = default)
    {
        var wireMembers = members.Select(m => new Dictionary<string, object?>
        {
            ["member_id"] = m.MemberId,
            ["profile"] = m.Profile,
            ["handle"] = m.Handle,
            ["display_name"] = m.DisplayName
        }).ToList();

        var result = await RpcAsync(
            "groups.create",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["name"] = name,
                ["members"] = wireMembers
            },
            ct: ct);
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("room", out var roomEl))
        {
            return GroupDecoders.DecodeRoom(roomEl);
        }
        return GroupDecoders.DecodeRoom(result);
    }

    public async Task<GroupStateSnapshot?> GroupsStateAsync(
        string roomId,
        bool includeDisbanded = true,
        CancellationToken ct = default)
    {
        var result = await RpcAsync(
            "groups.state",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["include_disbanded"] = includeDisbanded
            },
            ct: ct);
        return GroupDecoders.DecodeState(result);
    }

    public async Task<GroupLogPage> GroupsLogAsync(
        string roomId,
        int sinceSeq,
        int? limit = null,
        bool includeDisbanded = false,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["room_id"] = roomId,
            ["since_seq"] = sinceSeq
        };
        if (limit.HasValue) p["limit"] = limit.Value;
        if (includeDisbanded) p["include_disbanded"] = true;
        var result = await RpcAsync("groups.log", p, ct: ct);
        return GroupDecoders.DecodeLogPage(result);
    }

    public async Task<GroupSendResult> GroupsSendAsync(
        string roomId,
        string eventId,
        string text,
        string? threadId = null,
        CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?> { ["text"] = text };
        if (!string.IsNullOrWhiteSpace(threadId)) payload["thread_id"] = threadId;

        var result = await RpcAsync(
            "groups.send",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["event_id"] = eventId,
                ["payload"] = payload
            },
            ct: ct);
        return GroupDecoders.DecodeSendResult(result);
    }

    public async Task GroupsStopAsync(string roomId, CancellationToken ct = default)
    {
        await RpcAsync("groups.stop", new Dictionary<string, object?> { ["room_id"] = roomId }, ct: ct);
    }

    public async Task GroupsRenameAsync(string roomId, string name, CancellationToken ct = default)
    {
        await RpcAsync("groups.rename", new Dictionary<string, object?>
        {
            ["room_id"] = roomId,
            ["name"] = name
        }, ct: ct);
    }

    public async Task GroupsDisbandAsync(string roomId, CancellationToken ct = default)
    {
        await RpcAsync("groups.disband", new Dictionary<string, object?> { ["room_id"] = roomId }, ct: ct);
    }

    public async Task GroupsContextAsync(
        string roomId,
        string eventId,
        string purpose,
        string notes,
        string? routingMode = null,
        string? leadMemberId = null,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["room_id"] = roomId,
            ["event_id"] = eventId,
            ["purpose"] = purpose,
            ["notes"] = notes
        };
        if (!string.IsNullOrWhiteSpace(routingMode))
        {
            var routing = new Dictionary<string, object?> { ["mode"] = routingMode };
            if (!string.IsNullOrWhiteSpace(leadMemberId)) routing["lead_member_id"] = leadMemberId;
            p["routing"] = routing;
        }
        await RpcAsync("groups.context", p, ct: ct);
    }

    public async Task GroupsContinueAsync(string roomId, string discussionEventId, CancellationToken ct = default)
    {
        await RpcAsync("groups.continue", new Dictionary<string, object?>
        {
            ["room_id"] = roomId,
            ["discussion_event_id"] = discussionEventId
        }, ct: ct);
    }

    public async Task GroupsApproveAsync(
        string roomId,
        GroupPendingApproval approval,
        string choice,
        CancellationToken ct = default)
    {
        await RpcAsync("groups.approve", new Dictionary<string, object?>
        {
            ["room_id"] = roomId,
            ["member_id"] = approval.MemberId,
            ["task_id"] = approval.TaskId,
            ["execution_generation"] = approval.ExecutionGeneration,
            ["choice"] = choice,
            ["request_id"] = approval.RequestId
        }, ct: ct);
    }
}

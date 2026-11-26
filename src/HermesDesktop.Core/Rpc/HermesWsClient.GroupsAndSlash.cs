using System.Text.Json;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;

namespace HermesDesktop.Core.Rpc;

public sealed partial class HermesWsClient
{
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

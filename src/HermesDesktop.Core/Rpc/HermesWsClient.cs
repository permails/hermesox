using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HermesDesktop.Core.Auth;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Security;

namespace HermesDesktop.Core.Rpc;

public sealed class RpcException(int? code, string message, string? reason = null, string? detail = null)
    : Exception(message)
{
    public int? Code { get; } = code;
    public string? Reason { get; } = reason;
    public string? Detail { get; } = detail;
}

public sealed class HermesWsClient : IAsyncDisposable
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private int _nextId;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _profileConfigLock = new(1, 1);

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, (string SessionId, int Generation)> _authorityPrompts = new();
    private readonly ConcurrentDictionary<string, string> _serverClarifyRequests = new();
    private readonly ConcurrentDictionary<string, int> _sessionGenerations = new();

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public bool SpeaksSessionAuthority { get; private set; }
    public bool SendsChangeEvents { get; private set; }

    public event Action<StreamEvent>? EventReceived;
    public event Action<string?>? Disconnected;

    public async Task ConnectAsync(
        string baseUrl,
        AuthSessionTicket auth,
        DashboardSecrets? secrets = null,
        CancellationToken ct = default)
    {
        await DisconnectAsync();

        var wsUri = ConnectionURLPolicy.BuildWebSocketUri(baseUrl, "/api/ws", new Dictionary<string, string>
        {
            ["ticket"] = auth.Ticket
        });

        var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);

        if (auth.CookieJar.Count > 0)
        {
            ws.Options.Cookies = auth.CookieJar;
        }
        if (!string.IsNullOrWhiteSpace(auth.SessionToken))
        {
            ws.Options.SetRequestHeader("X-Hermes-Session-Token", auth.SessionToken);
        }
        else if (!string.IsNullOrWhiteSpace(auth.BearerToken))
        {
            ws.Options.SetRequestHeader("Authorization", $"Bearer {auth.BearerToken}");
        }

        if (wsUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase)
            && secrets is not null
            && !string.IsNullOrWhiteSpace(secrets.CfAccessClientId)
            && !string.IsNullOrWhiteSpace(secrets.CfAccessClientSecret))
        {
            ws.Options.SetRequestHeader("CF-Access-Client-Id", secrets.CfAccessClientId.Trim());
            ws.Options.SetRequestHeader("CF-Access-Client-Secret", secrets.CfAccessClientSecret.Trim());
        }

        await ws.ConnectAsync(wsUri, ct);

        _socket = ws;
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(ws, _receiveCts.Token));
    }

    public async Task DisconnectAsync()
    {
        var cts = Interlocked.Exchange(ref _receiveCts, null);
        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        var ws = Interlocked.Exchange(ref _socket, null);
        if (ws is not null)
        {
            try
            {
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", closeCts.Token);
                }
            }
            catch
            {
                // Best-effort close
            }
            ws.Dispose();
        }

        foreach (var kvp in _pending)
        {
            if (_pending.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }

        _authorityPrompts.Clear();
        _serverClarifyRequests.Clear();
        _sessionGenerations.Clear();
        SpeaksSessionAuthority = false;
        SendsChangeEvents = false;
    }

    public async Task<JsonElement> RpcAsync(
        string method,
        object? @params = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var ws = _socket;
        if (ws is null || ws.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var frame = JsonSerializer.SerializeToUtf8Bytes(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params
            });

            await _sendLock.WaitAsync(ct);
            try
            {
                if (_socket != ws || ws.State != WebSocketState.Open)
                    throw new InvalidOperationException("WebSocket is not connected.");
                await ws.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task<string> CreateSessionAsync(string? profile = null, string? cwd = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?> { ["source"] = "desktop" };
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;
        if (!string.IsNullOrWhiteSpace(cwd)) p["cwd"] = cwd;

        var res = await RpcAsync("session.create", p, ct: ct);
        return GetString(res, "session_id")
            ?? GetString(res, "id")
            ?? throw new InvalidOperationException("Gateway did not return session_id.");
    }

    public async Task<SessionResumeResult> OpenSessionAsync(
        string sessionId,
        string? profile = null,
        bool omitMessages = true,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["cols"] = 96,
            ["source"] = "desktop"
        };
        if (omitMessages) p["omit_messages"] = true;
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;

        var res = await RpcAsync("session.resume", p, timeout: TimeSpan.FromSeconds(omitMessages ? 30 : 60), ct: ct);

        var resolvedId = GetString(res, "session_id") ?? sessionId;
        var storedId = GetString(res, "stored_session_id") ?? GetString(res, "storedSessionId");
        var gen = GetInt(res, "execution_generation");
        if (gen.HasValue)
        {
            _sessionGenerations[resolvedId] = gen.Value;
            _sessionGenerations[sessionId] = gen.Value;
        }

        if (SpeaksSessionAuthority && res.TryGetProperty("prompts", out var promptsEl) && promptsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var prompt in promptsEl.EnumerateArray())
            {
                if (prompt.ValueKind == JsonValueKind.Object && GetString(prompt, "kind") == "clarify")
                {
                    var clarify = StreamEventParser.ParseClarify(prompt);
                    if (clarify is not null)
                    {
                        if (gen.HasValue) _authorityPrompts[clarify.RequestId] = (resolvedId, gen.Value);
                        EventReceived?.Invoke(new ClarifyRequestEvent(resolvedId, clarify));
                    }
                }
            }
        }

        return new SessionResumeResult(
            SessionId: resolvedId,
            StoredSessionId: storedId,
            IsRunning: GetBool(res, "running") == true,
            ExecutionGeneration: gen,
            Revision: GetInt(res, "revision"),
            Model: GetString(res, "model"),
            Provider: GetString(res, "provider"),
            Cwd: GetString(res, "cwd"),
            ContextPercent: GetDouble(res, "context_percent") ?? 0,
            ContextUsed: GetInt(res, "context_used") ?? 0,
            ContextMax: GetInt(res, "context_max") ?? 0,
            Messages: []);
    }

    public async Task<PromptSubmissionOutcome> SubmitPromptAsync(
        string sessionId,
        string text,
        string? profile = null,
        bool queued = false,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["text"] = text
        };
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;
        if (queued) p["queued"] = true;
        if (SpeaksSessionAuthority)
        {
            p["submission_id"] = Guid.NewGuid().ToString("D");
        }

        var res = await RpcAsync("prompt.submit", p, ct: ct);
        var gen = GetInt(res, "execution_generation");
        if (gen.HasValue) _sessionGenerations[sessionId] = gen.Value;

        var status = GetString(res, "status")?.ToLowerInvariant();
        return status == "queued" ? PromptSubmissionOutcome.Queued : PromptSubmissionOutcome.Accepted;
    }

    public async Task SteerAsync(string sessionId, string text, string? profile = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["text"] = text
        };
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;
        if (_sessionGenerations.TryGetValue(sessionId, out var gen)) p["execution_generation"] = gen;
        await RpcAsync("session.steer", p, ct: ct);
    }

    public async Task InterruptAsync(string sessionId, string? profile = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?> { ["session_id"] = sessionId };
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;
        if (_sessionGenerations.TryGetValue(sessionId, out var gen)) p["execution_generation"] = gen;
        await RpcAsync("session.interrupt", p, ct: ct);
    }

    public async Task RespondToApprovalAsync(
        string sessionId,
        string requestId,
        string choice,
        string? profile = null,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["choice"] = choice
        };
        if (SpeaksSessionAuthority && _authorityPrompts.TryGetValue(requestId, out var info))
        {
            p["prompt_id"] = requestId;
            p["execution_generation"] = info.Generation;
        }
        else if (!string.IsNullOrWhiteSpace(requestId))
        {
            p["request_id"] = requestId;
        }
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;
        await RpcAsync("approval.respond", p, ct: ct);
    }

    public async Task RespondToClarifyAsync(
        string sessionId,
        string requestId,
        string answer,
        string? questionId = null,
        string? profile = null,
        CancellationToken ct = default)
    {
        if (_serverClarifyRequests.TryRemove(requestId, out _))
        {
            await SendServerRequestReplyAsync(requestId, new { answer }, ct);
            return;
        }

        var p = new Dictionary<string, object?> { ["answer"] = answer };
        if (SpeaksSessionAuthority && _authorityPrompts.TryGetValue(requestId, out var info))
        {
            p["session_id"] = info.SessionId;
            p["execution_generation"] = info.Generation;
            p["prompt_id"] = requestId;
        }
        else
        {
            p["request_id"] = requestId;
            if (!string.IsNullOrWhiteSpace(questionId)) p["question_id"] = questionId;
        }
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;

        await RpcAsync("clarify.respond", p, ct: ct);
    }

    public async Task CompressSessionAsync(string sessionId, string? profile = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?> { ["session_id"] = sessionId };
        if (!string.IsNullOrWhiteSpace(profile)) p["profile"] = profile;
        await RpcAsync("session.compress", p, timeout: TimeSpan.FromSeconds(60), ct: ct);
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        await RpcAsync("ping", null, timeout: TimeSpan.FromSeconds(10), ct: ct);
    }

    public async Task<BotRosterSnapshot> GetBotRosterAsync(CancellationToken ct = default)
    {
        var res = await RpcAsync("profiles.list", null, ct: ct);
        if (res.ValueKind != JsonValueKind.Object || !res.TryGetProperty("profiles", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Gateway returned invalid profiles.list response.");
        }

        var supportsProtocol = GetBool(res, "bot_mode_protocol") == true;
        var list = new List<BotProfile>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = GetString(item, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            string? botTitle = null;
            string? metaColor = null;
            if (item.TryGetProperty("ui_meta", out var uiMeta) && uiMeta.ValueKind == JsonValueKind.Object
                && uiMeta.TryGetProperty("hermes-bots", out var botsMeta) && botsMeta.ValueKind == JsonValueKind.Object)
            {
                botTitle = GetString(botsMeta, "title")?.Trim();
                metaColor = GetString(botsMeta, "color")?.Trim();
            }

            var canonicalSession = item.TryGetProperty("canonical_session", out var cs) && cs.ValueKind == JsonValueKind.Object
                ? GetString(cs, "session_id") ?? GetString(cs, "id")
                : GetString(item, "canonical_session_id");

            var displayName = !string.IsNullOrWhiteSpace(botTitle)
                ? botTitle
                : GetString(item, "display_name") ?? GetString(item, "bot_title") ?? name;

            string? modelName = null;
            string? providerName = null;
            if (item.TryGetProperty("model", out var modelEl))
            {
                if (modelEl.ValueKind == JsonValueKind.String) modelName = modelEl.GetString();
                else if (modelEl.ValueKind == JsonValueKind.Object)
                {
                    modelName = GetString(modelEl, "default") ?? GetString(modelEl, "model");
                    providerName = GetString(modelEl, "provider");
                }
            }
            providerName ??= GetString(item, "provider");

            list.Add(new BotProfile
            {
                Name = name,
                BotTitle = botTitle,
                DisplayName = displayName,
                Description = GetString(item, "description"),
                Model = modelName,
                Provider = providerName,
                HasAvatar = GetBool(item, "has_avatar") == true,
                IsPinned = GetBool(item, "pinned") == true,
                AppearanceColorHex = metaColor ?? GetString(item, "color"),
                CanonicalSessionId = canonicalSession,
                LastPreview = GetString(item, "last_preview"),
                IsDefault = GetBool(item, "is_default") == true || name.Equals("default", StringComparison.OrdinalIgnoreCase)
            });
        }

        return new BotRosterSnapshot(list, supportsProtocol);
    }

    public async Task<BotProfileDetails> DescribeBotProfileAsync(string name, CancellationToken ct = default)
    {
        var res = await RpcAsync("profiles.describe", new { name }, ct: ct);
        if (res.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Gateway returned invalid profiles.describe response.");
        }

        var provider = "";
        var model = "";
        if (res.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.Object)
        {
            provider = GetString(modelEl, "provider") ?? "";
            model = GetString(modelEl, "default") ?? "";
        }

        return new BotProfileDetails(
            Name: name,
            Description: GetString(res, "description") ?? "",
            Soul: GetString(res, "soul") ?? "",
            Provider: provider,
            Model: model,
            Skills: ParseCapabilityList(res, "skills", defaultEnabled: true, sortByName: true),
            Toolsets: ParseCapabilityList(res, "toolsets", defaultEnabled: true, sortByName: false),
            McpServers: ParseCapabilityList(res, "mcp_servers", defaultEnabled: true, sortByName: false));
    }

    public async Task<JsonElement> ConfigureBotProfileAsync(
        string name,
        string? description = null,
        string? soul = null,
        string? displayTitle = null,
        IReadOnlyList<string>? disabledSkills = null,
        IReadOnlyList<string>? enabledToolsets = null,
        IReadOnlyList<string>? enabledMcpServers = null,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?> { ["name"] = name };
        if (description is not null) p["description"] = description;
        if (soul is not null) p["soul"] = soul;
        if (disabledSkills is not null) p["disabled_skills"] = disabledSkills;
        if (enabledToolsets is not null) p["enabled_toolsets"] = enabledToolsets;
        if (enabledMcpServers is not null) p["enabled_mcp_servers"] = enabledMcpServers;
        if (displayTitle is not null)
        {
            p["ui_meta"] = new Dictionary<string, object?>
            {
                ["hermes-bots"] = string.IsNullOrWhiteSpace(displayTitle)
                    ? new Dictionary<string, object?>()
                    : new Dictionary<string, object?> { ["title"] = displayTitle.Trim() }
            };
        }

        await _profileConfigLock.WaitAsync(ct);
        try
        {
            var res = await RpcAsync("profiles.configure", p, timeout: TimeSpan.FromSeconds(45), ct: ct);
            var checkedSections = new List<string>();
            if (soul is not null) checkedSections.Add("soul");
            if (description is not null) checkedSections.Add("description");
            var failed = BotSoulHelper.ExtractFailedSections(res, [.. checkedSections]);
            if (failed.Count > 0)
            {
                throw new InvalidOperationException($"Gateway refused sections: {string.Join(", ", failed)}");
            }
            return res;
        }
        finally
        {
            _profileConfigLock.Release();
        }
    }

    public async Task<JsonElement> CreateBotProfileAsync(
        string slug,
        string description,
        string soul,
        string? cloneFrom = null,
        bool noSkills = false,
        bool shareAuth = true,
        string? provider = null,
        string? model = null,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, object?>
        {
            ["name"] = slug,
            ["description"] = description,
            ["soul"] = soul,
            ["no_skills"] = noSkills,
            ["share_auth"] = shareAuth
        };
        if (!string.IsNullOrWhiteSpace(cloneFrom)) p["clone_from"] = cloneFrom.Trim();
        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(model))
        {
            p["provider"] = provider.Trim();
            p["model"] = model.Trim();
        }

        return await RpcAsync("profiles.create", p, timeout: TimeSpan.FromSeconds(60), ct: ct);
    }

    private static IReadOnlyList<BotProfileCapability> ParseCapabilityList(
        JsonElement obj,
        string prop,
        bool defaultEnabled,
        bool sortByName)
    {
        if (!obj.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<BotProfileCapability>();
        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var capName = GetString(row, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(capName)) continue;
            var label = GetString(row, "label")?.Trim();
            list.Add(new BotProfileCapability(
                Name: capName,
                Label: string.IsNullOrEmpty(label) ? null : label,
                Description: GetString(row, "description") ?? "",
                ToolCount: GetInt(row, "tool_count"),
                Enabled: GetBool(row, "enabled") ?? defaultEnabled));
        }

        if (sortByName)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        return list;
    }

    private async Task SendServerRequestReplyAsync(string requestId, object result, CancellationToken ct = default)
    {
        var ws = _socket;
        if (ws is null || ws.State != WebSocketState.Open) return;
        var frame = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id = requestId,
            result
        });
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_socket == ws && ws.State == WebSocketState.Open)
                await ws.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        string? disconnectReason = null;

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(buffer, ct);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        disconnectReason = res.CloseStatusDescription ?? "Server closed connection";
                        return;
                    }
                    ms.Write(buffer, 0, res.Count);
                }
                while (!res.EndOfMessage);

                if (res.MessageType != WebSocketMessageType.Text) continue;
                HandleInboundFrame(ms.ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            // Intentional disconnect
        }
        catch (Exception ex)
        {
            disconnectReason = ex.Message;
        }
        finally
        {
            if (_socket == ws)
            {
                Disconnected?.Invoke(disconnectReason);
            }
        }
    }

    private void HandleInboundFrame(byte[] utf8)
    {
        try
        {
            using var doc = JsonDocument.Parse(utf8);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            // 1. Response to our integer-ID RPC
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var reqId))
            {
                if (_pending.TryRemove(reqId, out var tcs))
                {
                    if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
                    {
                        var code = GetInt(errEl, "code");
                        var msg = GetString(errEl, "message") ?? "RPC error";
                        string? reason = null;
                        string? detail = null;
                        if (errEl.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
                        {
                            reason = GetString(dataEl, "reason");
                            detail = GetString(dataEl, "detail");
                        }
                        tcs.TrySetException(new RpcException(code, msg, reason, detail));
                    }
                    else if (root.TryGetProperty("result", out var resEl))
                    {
                        tcs.TrySetResult(resEl.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(default);
                    }
                }
                return;
            }

            // 2. Server->Client request (string id like srq-...)
            if (root.TryGetProperty("id", out var strIdEl) && strIdEl.ValueKind == JsonValueKind.String
                && root.TryGetProperty("method", out var srvMethodEl) && srvMethodEl.ValueKind == JsonValueKind.String)
            {
                var srqId = strIdEl.GetString()!;
                var srvMethod = srvMethodEl.GetString()!;
                root.TryGetProperty("params", out var srvParams);
                HandleServerRequest(srqId, srvMethod, srvParams);
                return;
            }

            // 3. Stream event notification
            if (GetString(root, "method") == "event" && root.TryGetProperty("params", out var paramsEl))
            {
                var sessionId = GetString(paramsEl, "session_id") ?? "";
                var gen = GetInt(paramsEl, "execution_generation");
                if (!string.IsNullOrEmpty(sessionId) && gen.HasValue)
                {
                    _sessionGenerations[sessionId] = gen.Value;
                }

                var parsed = StreamEventParser.Parse(paramsEl);
                if (parsed is GatewayReadyEvent ready)
                {
                    SpeaksSessionAuthority = ready.SessionAuthority;
                    SendsChangeEvents = ready.SendsChangeEvents;
                    _ = Task.Run(() => RpcAsync("client.capabilities", new { server_requests = true }));
                }
                else if (parsed is ApprovalRequestEvent approvalEv && gen.HasValue)
                {
                    _authorityPrompts[approvalEv.Activity.RequestId] = (approvalEv.SessionId, gen.Value);
                }
                else if (parsed is ClarifyRequestEvent clarifyEv && gen.HasValue)
                {
                    _authorityPrompts[clarifyEv.Activity.RequestId] = (clarifyEv.SessionId, gen.Value);
                }

                if (parsed is not null)
                {
                    EventReceived?.Invoke(parsed);
                }
            }
        }
        catch
        {
            // Ignore malformed inbound frame
        }
    }

    private void HandleServerRequest(string srqId, string method, JsonElement @params)
    {
        var sessionId = @params.ValueKind == JsonValueKind.Object ? GetString(@params, "session_id") ?? "" : "";
        if (method == "clarify" && @params.ValueKind == JsonValueKind.Object)
        {
            var clarify = StreamEventParser.ParseClarify(@params);
            if (clarify is not null)
            {
                _serverClarifyRequests[clarify.RequestId] = srqId;
                EventReceived?.Invoke(new ClarifyRequestEvent(sessionId, clarify));
            }
        }
        else if (method == "approval" && @params.ValueKind == JsonValueKind.Object)
        {
            var approval = StreamEventParser.ParseApproval(@params, sessionId);
            if (approval is not null)
            {
                EventReceived?.Invoke(new ApprovalRequestEvent(sessionId, approval));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
        _profileConfigLock.Dispose();
    }

    private static string? GetString(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool? GetBool(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : null;

    private static int? GetInt(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v
            : null;

    private static double? GetDouble(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v)
            ? v
            : null;
}

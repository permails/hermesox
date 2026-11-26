using System.Text.Json;
using HermesDesktop.Core.Models;

namespace HermesDesktop.Core.Rpc;

/// <summary>
/// Pure-function stream event parser for Hermes gateway WebSocket events.
/// </summary>
public static class StreamEventParser
{
    public static StreamEvent? Parse(JsonElement paramsEl)
    {
        if (paramsEl.ValueKind != JsonValueKind.Object) return null;

        var type = GetString(paramsEl, "type") ?? string.Empty;
        var sessionId = GetString(paramsEl, "session_id") ?? string.Empty;
        var hasPayload = paramsEl.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object;

        switch (type)
        {
            case "gateway.ready":
                return new GatewayReadyEvent(
                    SessionAuthority: hasPayload && GetBool(payload, "session_authority") == true,
                    SendsChangeEvents: hasPayload && GetBool(payload, "change_events") == true);

            case "sessions.changed":
                return new SessionsChangedEvent();

            case "message.start":
                return new MessageStartEvent(sessionId);

            case "message.delta":
            {
                var text = (hasPayload ? GetString(payload, "text") : null) ?? GetString(paramsEl, "text") ?? string.Empty;
                return string.IsNullOrEmpty(text) ? null : new MessageDeltaEvent(sessionId, text);
            }

            case "reasoning.delta":
            case "message.reasoning":
            case "message.reasoning.delta":
            {
                var text = (hasPayload
                    ? GetString(payload, "reasoning")
                        ?? GetString(payload, "reasoning_content")
                        ?? GetString(payload, "text")
                        ?? GetString(payload, "content")
                    : null)
                    ?? GetString(paramsEl, "reasoning")
                    ?? GetString(paramsEl, "text")
                    ?? string.Empty;
                return string.IsNullOrEmpty(text) ? null : new ReasoningDeltaEvent(sessionId, text);
            }

            case "message.complete":
            {
                var messageId = (hasPayload
                    ? GetString(payload, "message_id") ?? GetString(payload, "id")
                    : null)
                    ?? GetString(paramsEl, "message_id")
                    ?? GetString(paramsEl, "id");

                if (messageId is null && hasPayload && payload.TryGetProperty("message", out var msgObj) && msgObj.ValueKind == JsonValueKind.Object)
                {
                    messageId = GetString(msgObj, "id");
                }

                var content = hasPayload
                    ? GetString(payload, "content") ?? GetString(payload, "text") ?? GetString(payload, "rendered")
                    : null;
                var reasoning = hasPayload ? GetString(payload, "reasoning") : null;
                var alreadyShown = hasPayload && (GetBool(payload, "response_previewed") == true || GetBool(payload, "response_reused") == true);
                return new MessageCompleteEvent(sessionId, messageId, content, reasoning, alreadyShown);
            }

            case "message.interim":
            {
                var text = hasPayload ? GetString(payload, "text") ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return null;
                var alreadyStreamed = hasPayload && GetBool(payload, "already_streamed") == true;
                return new MessageInterimEvent(sessionId, text, alreadyStreamed);
            }

            case "error":
            {
                var message = (hasPayload ? GetString(payload, "message") : null) ?? "Hermes reported an error.";
                return new MessageErrorEvent(sessionId, message);
            }

            case "message.interrupted":
            case "session.interrupted":
                return new MessageInterruptedEvent(sessionId);

            case "session.busy":
            {
                var busy = hasPayload && GetBool(payload, "busy") == true;
                return new SessionBusyEvent(sessionId, busy);
            }

            case "session.title":
            {
                if (!hasPayload) return null;
                var storedId = GetString(payload, "session_id") ?? string.Empty;
                var title = (GetString(payload, "title") ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(storedId) || string.IsNullOrEmpty(title)) return null;
                return new SessionTitleEvent(sessionId, storedId, title);
            }

            case "status.update":
            {
                if (string.IsNullOrEmpty(sessionId)) return null;
                var kind = hasPayload ? GetString(payload, "kind") ?? string.Empty : string.Empty;
                var text = hasPayload ? GetString(payload, "text") : null;
                return new StatusUpdateEvent(sessionId, kind, text);
            }

            case "tool.start":
            case "tool_call":
            {
                if (!hasPayload) return new ToolStartEvent(sessionId, "", null, null);
                var toolId = FirstNonEmpty(
                    GetString(payload, "tool_id"),
                    GetString(payload, "tool_call_id"),
                    GetString(payload, "call_id"));
                var name = GetString(payload, "name") ?? string.Empty;
                var input = FirstNonEmpty(
                    GetDescriptiveString(payload, "args_text"),
                    GetDescriptiveString(payload, "context"),
                    GetDescriptiveString(payload, "input"),
                    GetDescriptiveString(payload, "arguments"),
                    GetDescriptiveString(payload, "args"));
                return new ToolStartEvent(sessionId, name, input, toolId);
            }

            case "tool.complete":
            case "tool_result":
            {
                if (!hasPayload) return new ToolCompleteEvent(sessionId, "", null, null);
                var toolId = FirstNonEmpty(
                    GetString(payload, "tool_id"),
                    GetString(payload, "tool_call_id"),
                    GetString(payload, "call_id"));
                var name = GetString(payload, "name") ?? string.Empty;
                var output = FirstNonEmpty(
                    GetDescriptiveString(payload, "output"),
                    GetDescriptiveString(payload, "result"));
                return new ToolCompleteEvent(sessionId, name, output, toolId);
            }

            case "clarify":
            case "clarify.request":
            {
                if (!hasPayload) return null;
                var clarify = ParseClarify(payload);
                return clarify is null ? null : new ClarifyRequestEvent(sessionId, clarify);
            }

            case "clarify.expire":
            {
                if (!hasPayload) return null;
                var reqId = (GetString(payload, "request_id") ?? GetString(payload, "requestId") ?? string.Empty).Trim();
                return string.IsNullOrEmpty(reqId) ? null : new ClarifyExpireEvent(sessionId, reqId, null);
            }

            case "request.cancel":
            {
                if (!hasPayload) return null;
                var method = GetString(payload, "method") ?? string.Empty;
                var reqId = (GetString(payload, "id") ?? string.Empty).Trim();
                var reason = GetString(payload, "reason") ?? string.Empty;
                if (string.IsNullOrEmpty(reqId)) return null;
                if (method == "clarify")
                {
                    if (reason == "resolved") return null;
                    return new ClarifyExpireEvent(sessionId, reqId, reason);
                }
                if (method is "sudo" or "secret" or "vault.unlock_prompt" or "vault.save_login" or "vault.code")
                {
                    return new InputPromptExpireEvent(sessionId, reqId, reason);
                }
                return null;
            }

            case "approval.request":
            {
                if (!hasPayload) return null;
                var approval = ParseApproval(payload, sessionId);
                return approval is null ? null : new ApprovalRequestEvent(sessionId, approval);
            }

            case "context.update":
            case "session.context":
            {
                if (!hasPayload) return null;
                var used = GetInt(payload, "context_used") ?? 0;
                var max = GetInt(payload, "context_max") ?? 0;
                var pct = NormalizeContextPercent(GetDouble(payload, "context_percent"), used, max);
                return new ContextUpdateEvent(sessionId, pct, used, max);
            }

            case "session.usage":
            {
                if (!hasPayload || !payload.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                    return null;
                var max = GetInt(usage, "context_max");
                if (max is null or <= 0) return null;
                var used = GetInt(usage, "context_used") ?? 0;
                var pct = NormalizeContextPercent(GetDouble(usage, "context_percent"), used, max.Value);
                return new ContextUpdateEvent(sessionId, pct, used, max.Value);
            }

            case "cwd.update":
            case "workspace.update":
                return new CwdUpdateEvent(sessionId, hasPayload ? GetString(payload, "cwd") ?? "" : "");

            case "model.update":
                return new ModelUpdateEvent(
                    sessionId,
                    hasPayload ? GetString(payload, "model") ?? "" : "",
                    hasPayload ? GetString(payload, "provider") ?? "" : "");

            case "background.complete":
            case "btw.complete":
            {
                if (!hasPayload) return null;
                var text = (GetString(payload, "text") ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(text)) return null;
                var question = (GetString(payload, "question") ?? string.Empty).Trim();
                var taskId = GetString(payload, "task_id") ?? string.Empty;
                return new SideAgentCompleteEvent(sessionId, type == "btw.complete", taskId, text, string.IsNullOrEmpty(question) ? null : question);
            }

            default:
                if (type.StartsWith("subagent.", StringComparison.Ordinal))
                {
                    if (!hasPayload) return null;
                    return new DelegateAgentEvent(sessionId, ParseDelegateAgent(payload, type, sessionId));
                }
                return new UnparsedStreamEvent(sessionId, type);
        }
    }

    public static ClarifyActivity? ParseClarify(JsonElement payload)
    {
        var reqId = FirstNonEmpty(GetString(payload, "request_id"), GetString(payload, "requestId"), GetString(payload, "prompt_id"), GetString(payload, "id"));
        var question = FirstNonEmpty(GetString(payload, "question"), GetString(payload, "prompt"), GetString(payload, "title"));
        if (string.IsNullOrWhiteSpace(reqId) || string.IsNullOrWhiteSpace(question)) return null;

        var choices = new List<ClarifyChoice>();
        if (payload.TryGetProperty("choices", out var choicesEl) && choicesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in choicesEl.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.String)
                {
                    var v = c.GetString() ?? "";
                    if (!string.IsNullOrEmpty(v)) choices.Add(new ClarifyChoice(v, v));
                }
                else if (c.ValueKind == JsonValueKind.Object)
                {
                    var label = GetString(c, "label") ?? GetString(c, "title") ?? GetString(c, "value") ?? "";
                    var value = GetString(c, "value") ?? label;
                    if (!string.IsNullOrEmpty(label)) choices.Add(new ClarifyChoice(label, value));
                }
            }
        }
        else if (payload.TryGetProperty("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in optsEl.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.String)
                {
                    var v = c.GetString() ?? "";
                    if (!string.IsNullOrEmpty(v)) choices.Add(new ClarifyChoice(v, v));
                }
                else if (c.ValueKind == JsonValueKind.Object)
                {
                    var label = GetString(c, "label") ?? GetString(c, "value") ?? "";
                    var value = GetString(c, "value") ?? label;
                    if (!string.IsNullOrEmpty(label)) choices.Add(new ClarifyChoice(label, value));
                }
            }
        }

        return new ClarifyActivity(
            RequestId: reqId!,
            QuestionId: GetString(payload, "question_id"),
            Question: question!,
            Choices: choices,
            AllowCustomInput: GetBool(payload, "allow_custom") ?? true,
            IsMultiSelect: GetBool(payload, "multi_select") ?? false);
    }

    public static ApprovalActivity? ParseApproval(JsonElement payload, string sessionId)
    {
        var reqId = FirstNonEmpty(
            GetString(payload, "request_id"),
            GetString(payload, "prompt_id"),
            GetString(payload, "id")) ?? string.Empty;
        var command = FirstNonEmpty(GetString(payload, "command"), GetString(payload, "cmd"), GetString(payload, "title"));
        if (string.IsNullOrWhiteSpace(command)) return null;

        var patterns = new List<string>();
        if (payload.TryGetProperty("patterns", out var patEl) && patEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in patEl.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                    patterns.Add(p.GetString()!);
            }
        }

        return new ApprovalActivity(
            RequestId: reqId,
            SessionId: FirstNonEmpty(GetString(payload, "session_id"), sessionId) ?? sessionId,
            Command: command!,
            Description: FirstNonEmpty(GetString(payload, "description"), GetString(payload, "reason")),
            RiskLevel: FirstNonEmpty(GetString(payload, "risk"), GetString(payload, "risk_level"), GetString(payload, "severity")),
            Patterns: patterns);
    }

    private static DelegateAgentActivity ParseDelegateAgent(JsonElement payload, string eventType, string sessionId)
    {
        var subagentId = FirstNonEmpty(GetString(payload, "subagent_id"));
        var goal = GetString(payload, "goal") ?? GetString(payload, "task") ?? string.Empty;
        var taskIndex = GetInt(payload, "task_index") ?? 0;
        var delegationId = FirstNonEmpty(GetString(payload, "delegation_id"));
        var id = subagentId
            ?? FirstNonEmpty(GetString(payload, "id"), GetString(payload, "agent_id"))
            ?? (delegationId is not null ? $"{sessionId}/{delegationId}#{taskIndex}" : $"{sessionId}/#{taskIndex}:{goal}");

        var status = GetString(payload, "status")?.ToLowerInvariant() switch
        {
            "error" or "timeout" => "failed",
            { Length: > 0 } s => s,
            _ when eventType.Contains("fail") => "failed",
            _ when eventType.Contains("interrupt") => "interrupted",
            _ when eventType.Contains("complete") || eventType.Contains("finish") => "completed",
            _ when eventType.Contains("spawn") => "queued",
            _ => "running"
        };

        var text = FirstNonEmpty(GetString(payload, "text"), GetString(payload, "message"), GetString(payload, "summary")) ?? string.Empty;
        var kind = eventType.Contains("tool") ? "tool"
            : eventType.Contains("thinking") ? "thinking"
            : eventType.Contains("progress") ? "progress"
            : "summary";
        var lines = string.IsNullOrEmpty(text)
            ? Array.Empty<DelegateAgentLine>()
            : [new DelegateAgentLine(kind, text, status == "failed")];

        return new DelegateAgentActivity(
            Id: id,
            Goal: goal,
            Model: FirstNonEmpty(GetString(payload, "model")),
            Status: status,
            TaskCount: GetInt(payload, "task_count") ?? 1,
            TaskIndex: taskIndex,
            CurrentTool: FirstNonEmpty(GetString(payload, "tool_name"), GetString(payload, "tool"), GetString(payload, "current_tool")),
            Summary: FirstNonEmpty(GetString(payload, "summary")),
            Stream: lines,
            DurationSeconds: GetDouble(payload, "duration_seconds"));
    }

    private static string? GetString(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static string? GetDescriptiveString(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.GetRawText()
        };
    }

    private static bool? GetBool(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? GetInt(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Number) return null;
        return el.TryGetInt32(out var i) ? i : null;
    }

    private static double? GetDouble(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Number) return null;
        return el.TryGetDouble(out var d) ? d : null;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)) return c.Trim();
        }
        return null;
    }

    public static double NormalizeContextPercent(double? rawPercent, int used, int max)
    {
        if (used > 0 && max > 0)
        {
            return Math.Clamp((double)used / max * 100.0, 0.0, 100.0);
        }
        if (rawPercent is > 0)
        {
            var val = rawPercent.Value <= 1.0 ? rawPercent.Value * 100.0 : rawPercent.Value;
            return Math.Clamp(val, 0.0, 100.0);
        }
        return 0.0;
    }
}

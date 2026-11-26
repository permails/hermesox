using System.Text.Json;

namespace HermesDesktop.Core.Models;

public sealed record GroupCapabilities(
    int ProtocolVersion,
    bool DriverSupported,
    string? AuthorityGatewayId,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Methods,
    int MaxLogLimit)
{
    public bool Supports(string method) =>
        Methods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase));
}

public sealed record GroupMember(
    string MemberId,
    string Profile,
    string Handle,
    string DisplayName,
    string Role = "member")
{
    public string HandleDisplay => "@" + (string.IsNullOrWhiteSpace(Handle) ? MemberId : Handle);
}

public sealed record GroupRoom(
    string RoomId,
    string Name,
    string State,
    int LatestSeq,
    long? CreatedAtMs,
    long? UpdatedAtMs,
    string? LastMessagePreview,
    string? LastActorName,
    IReadOnlyList<GroupMember> Members)
{
    public bool IsDisbanded => string.Equals(State, "disbanded", StringComparison.OrdinalIgnoreCase);

    public string MemberCountLabel => $"{Members.Count}";
}

public sealed record GroupActor(
    string Kind,
    string Id,
    string? Profile,
    string DisplayName)
{
    public bool IsUser => string.Equals(Kind, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsSystem => string.Equals(Kind, "system", StringComparison.OrdinalIgnoreCase);
    public bool IsMember => string.Equals(Kind, "member", StringComparison.OrdinalIgnoreCase);
}

public sealed record GroupEvent(
    string RoomId,
    int Seq,
    string EventId,
    string Kind,
    GroupActor Actor,
    string Text,
    string? ThreadId,
    string? Summary,
    long? CreatedAtMs,
    IReadOnlyList<Attachment> Attachments)
{
    public bool IsMessage => Kind is "message.user" or "message.member";
    public bool IsUserMessage => string.Equals(Kind, "message.user", StringComparison.OrdinalIgnoreCase);
    public bool IsMemberMessage => string.Equals(Kind, "message.member", StringComparison.OrdinalIgnoreCase);
}

public sealed record GroupLogPage(
    IReadOnlyList<GroupEvent> Events,
    int Cursor,
    int LatestSeq,
    bool HasMore);

public sealed record GroupRouting(
    string Mode,
    string? LeadMemberId,
    int? MaxRounds);

public sealed record GroupRoomContext(
    string Purpose,
    string Notes,
    GroupRouting Routing);

public sealed record GroupPendingApproval(
    string MemberId,
    string TaskId,
    int ExecutionGeneration,
    string RequestId,
    string Command,
    string? Description,
    string? Profile);

public sealed record GroupMemberActivity(
    string MemberId,
    string State,
    string? Summary,
    int? Round);

public sealed record GroupDriverStatus(
    bool Running,
    int WorkingCount,
    int BlockedCount,
    bool CanContinue,
    string? ContinueDiscussionEventId,
    IReadOnlyList<GroupPendingApproval> PendingApprovals,
    IReadOnlyList<GroupMemberActivity> MemberActivities,
    IReadOnlyList<string> RoutingModes);

public sealed record GroupStateSnapshot(
    GroupRoom Room,
    GroupDriverStatus DriverStatus,
    GroupRoomContext Context);

public sealed record GroupSendResult(
    GroupEvent? Event,
    bool Accepted,
    bool DriverStarted);

public static class GroupDecoders
{
    public static GroupCapabilities DecodeCapabilities(JsonElement root)
    {
        var protocol = TryGetInt(root, "protocol_version") ?? 1;
        var driver = TryGetBool(root, "driver") ?? true;
        var gateway = TryGetString(root, "authority_gateway_id");
        var features = ReadStringArray(root, "features");
        var methods = ReadStringArray(root, "methods");
        var maxLimit = TryGetInt(root, "max_log_limit") ?? 200;
        return new GroupCapabilities(protocol, driver, gateway, features, methods, maxLimit);
    }

    public static IReadOnlyList<GroupRoom> DecodeRooms(JsonElement root)
    {
        var list = new List<GroupRoom>();
        if (root.TryGetProperty("rooms", out var roomsEl) && roomsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in roomsEl.EnumerateArray())
            {
                if (DecodeRoom(item) is { } room) list.Add(room);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (DecodeRoom(item) is { } room) list.Add(room);
            }
        }
        return list;
    }

    public static GroupRoom? DecodeRoom(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var roomId = TryGetString(el, "room_id") ?? TryGetString(el, "id");
        if (string.IsNullOrWhiteSpace(roomId)) return null;

        var name = TryGetString(el, "name") ?? roomId;
        var state = TryGetString(el, "state") ?? "active";
        var latestSeq = TryGetInt(el, "latest_seq") ?? TryGetInt(el, "seq") ?? 0;
        var created = ReadTimestampMs(el, "created_at", "created_at_ms");
        var updated = ReadTimestampMs(el, "updated_at", "updated_at_ms");
        var preview = TryGetString(el, "last_message_preview") ?? TryGetString(el, "preview");
        var lastActor = TryGetString(el, "last_actor_name");

        var members = new List<GroupMember>();
        if (el.TryGetProperty("members", out var membersEl) && membersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in membersEl.EnumerateArray())
            {
                if (DecodeMember(m) is { } member) members.Add(member);
            }
        }

        return new GroupRoom(roomId, name, state, latestSeq, created, updated, preview, lastActor, members);
    }

    public static GroupMember? DecodeMember(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var memberId = TryGetString(el, "member_id") ?? TryGetString(el, "id") ?? TryGetString(el, "handle");
        var profile = TryGetString(el, "profile") ?? TryGetString(el, "bot_id") ?? memberId ?? "default";
        var handle = TryGetString(el, "handle") ?? memberId ?? profile;
        var displayName = TryGetString(el, "display_name") ?? TryGetString(el, "name") ?? profile;
        var role = TryGetString(el, "role") ?? "member";
        if (string.IsNullOrWhiteSpace(memberId)) memberId = handle;
        return new GroupMember(memberId, profile, handle, displayName, role);
    }

    public static GroupLogPage DecodeLogPage(JsonElement root)
    {
        var events = new List<GroupEvent>();
        if (root.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in eventsEl.EnumerateArray())
            {
                if (DecodeEvent(item) is { } ev) events.Add(ev);
            }
        }
        var cursor = TryGetInt(root, "cursor") ?? (events.Count > 0 ? events[^1].Seq : 0);
        var latestSeq = TryGetInt(root, "latest_seq") ?? cursor;
        var hasMore = TryGetBool(root, "has_more") ?? false;
        return new GroupLogPage(events, cursor, latestSeq, hasMore);
    }

    public static GroupEvent? DecodeEvent(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var roomId = TryGetString(el, "room_id") ?? "";
        var seq = TryGetInt(el, "seq") ?? 0;
        var eventId = TryGetString(el, "event_id") ?? $"seq-{seq}";
        var kind = TryGetString(el, "kind") ?? "system";
        var created = ReadTimestampMs(el, "created_at", "created_at_ms");

        var actor = new GroupActor("system", "system", null, "System");
        if (el.TryGetProperty("actor", out var actorEl) && actorEl.ValueKind == JsonValueKind.Object)
        {
            var actorKind = TryGetString(actorEl, "kind") ?? "system";
            var actorId = TryGetString(actorEl, "id") ?? TryGetString(actorEl, "member_id") ?? "system";
            var profile = TryGetString(actorEl, "profile");
            var displayName = TryGetString(actorEl, "display_name")
                              ?? TryGetString(actorEl, "name")
                              ?? profile
                              ?? actorId;
            actor = new GroupActor(actorKind, actorId, profile, displayName);
        }

        var text = "";
        string? threadId = null;
        string? summary = null;
        var attachments = new List<Attachment>();

        if (el.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind == JsonValueKind.Object)
        {
            text = TryGetString(payloadEl, "text")
                   ?? TryGetString(payloadEl, "message")
                   ?? TryGetString(payloadEl, "summary")
                   ?? "";
            threadId = TryGetString(payloadEl, "thread_id");
            summary = TryGetString(payloadEl, "summary");
            if (payloadEl.TryGetProperty("attachments", out var attEl) && attEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in attEl.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    var uri = TryGetString(a, "uri") ?? TryGetString(a, "url") ?? TryGetString(a, "path");
                    var name = TryGetString(a, "name") ?? TryGetString(a, "filename") ?? "file";
                    if (!string.IsNullOrWhiteSpace(uri))
                    {
                        var id = TryGetString(a, "id") ?? Guid.NewGuid().ToString("N");
                        var mime = TryGetString(a, "mime_type");
                        var kindStr = TryGetString(a, "kind") ?? mime;
                        var attKind = AttachmentKind.Document;
                        if (kindStr?.Contains("image", StringComparison.OrdinalIgnoreCase) == true)
                            attKind = AttachmentKind.Image;
                        else if (kindStr?.Contains("video", StringComparison.OrdinalIgnoreCase) == true)
                            attKind = AttachmentKind.Video;
                        attachments.Add(new Attachment(id, name, uri, mime, attKind));
                    }
                }
            }
        }

        return new GroupEvent(roomId, seq, eventId, kind, actor, text, threadId, summary, created, attachments);
    }

    public static GroupStateSnapshot? DecodeState(JsonElement root)
    {
        if (!root.TryGetProperty("room", out var roomEl) || DecodeRoom(roomEl) is not { } room)
        {
            if (DecodeRoom(root) is not { } directRoom) return null;
            room = directRoom;
        }

        var purpose = "";
        var notes = "";
        var routingMode = "auto";
        string? leadId = null;
        int? maxRounds = null;

        if (root.TryGetProperty("context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.Object)
        {
            purpose = TryGetString(ctxEl, "purpose") ?? "";
            notes = TryGetString(ctxEl, "notes") ?? "";
            leadId = TryGetString(ctxEl, "lead_member_id");
            if (ctxEl.TryGetProperty("routing", out var routingEl) && routingEl.ValueKind == JsonValueKind.Object)
            {
                routingMode = TryGetString(routingEl, "mode") ?? routingMode;
                leadId ??= TryGetString(routingEl, "lead_member_id");
                maxRounds = TryGetInt(routingEl, "max_rounds");
            }
            else if (ctxEl.TryGetProperty("routing", out var routingStrEl) && routingStrEl.ValueKind == JsonValueKind.String)
            {
                routingMode = routingStrEl.GetString() ?? routingMode;
            }
        }

        var running = false;
        var working = 0;
        var blocked = 0;
        var canContinue = false;
        string? continueEventId = null;
        var approvals = new List<GroupPendingApproval>();
        var activities = new List<GroupMemberActivity>();
        var routingModes = new List<string>();

        if (root.TryGetProperty("driver_status", out var drvEl) && drvEl.ValueKind == JsonValueKind.Object)
        {
            running = TryGetBool(drvEl, "running") ?? false;
            working = TryGetInt(drvEl, "working") ?? 0;
            blocked = TryGetInt(drvEl, "blocked") ?? 0;

            if (drvEl.TryGetProperty("counts", out var countsEl) && countsEl.ValueKind == JsonValueKind.Object)
            {
                working = TryGetInt(countsEl, "working") ?? working;
                blocked = TryGetInt(countsEl, "blocked") ?? blocked;
            }

            if (drvEl.TryGetProperty("routing_modes", out var modesEl) && modesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in modesEl.EnumerateArray())
                {
                    if (m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } mode)
                        routingModes.Add(mode);
                }
            }

            if (drvEl.TryGetProperty("max_continues", out var mcEl) && mcEl.ValueKind == JsonValueKind.Object)
            {
                canContinue = TryGetBool(mcEl, "can_continue") ?? false;
                continueEventId = TryGetString(mcEl, "discussion_event_id");
            }

            if (drvEl.TryGetProperty("pending_actions", out var pendingEl) && pendingEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pendingEl.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    var requestId = TryGetString(p, "request_id") ?? "";
                    var memberId = TryGetString(p, "member_id") ?? "";
                    var taskId = TryGetString(p, "task_id") ?? "";
                    var gen = TryGetInt(p, "execution_generation") ?? 0;
                    var cmd = TryGetString(p, "command") ?? TryGetString(p, "title") ?? "Pending action";
                    var desc = TryGetString(p, "description");
                    var prof = TryGetString(p, "profile");
                    if (!string.IsNullOrWhiteSpace(requestId) || !string.IsNullOrWhiteSpace(taskId))
                    {
                        approvals.Add(new GroupPendingApproval(memberId, taskId, gen, requestId, cmd, desc, prof));
                    }
                }
            }

            if (drvEl.TryGetProperty("member_activity", out var actEl))
            {
                if (actEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in actEl.EnumerateArray())
                    {
                        if (a.ValueKind != JsonValueKind.Object) continue;
                        var mId = TryGetString(a, "member_id") ?? TryGetString(a, "id") ?? "";
                        var st = TryGetString(a, "state") ?? TryGetString(a, "status") ?? "idle";
                        var sum = TryGetString(a, "summary");
                        var rnd = TryGetInt(a, "round");
                        if (!string.IsNullOrWhiteSpace(mId))
                            activities.Add(new GroupMemberActivity(mId, st, sum, rnd));
                    }
                }
                else if (actEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in actEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            var st = TryGetString(prop.Value, "state") ?? TryGetString(prop.Value, "status") ?? "idle";
                            var sum = TryGetString(prop.Value, "summary");
                            var rnd = TryGetInt(prop.Value, "round");
                            activities.Add(new GroupMemberActivity(prop.Name, st, sum, rnd));
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            activities.Add(new GroupMemberActivity(prop.Name, prop.Value.GetString() ?? "idle", null, null));
                        }
                    }
                }
            }
        }

        if (routingModes.Count == 0)
        {
            routingModes.AddRange(["auto", "lead", "round_robin", "mentions_only"]);
        }

        return new GroupStateSnapshot(
            room,
            new GroupDriverStatus(running, working, blocked, canContinue, continueEventId, approvals, activities, routingModes),
            new GroupRoomContext(purpose, notes, new GroupRouting(routingMode, leadId, maxRounds)));
    }

    public static GroupSendResult DecodeSendResult(JsonElement root)
    {
        GroupEvent? ev = null;
        if (root.TryGetProperty("event", out var evEl))
        {
            ev = DecodeEvent(evEl);
        }
        var accepted = TryGetBool(root, "accepted") ?? true;
        var driverStarted = TryGetBool(root, "driver_started") ?? false;
        return new GroupSendResult(ev, accepted, driverStarted);
    }

    private static List<string> ReadStringArray(JsonElement root, string name)
    {
        var items = new List<string>();
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                {
                    items.Add(s);
                }
            }
        }
        return items;
    }

    private static string? TryGetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static int? TryGetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v)) return v;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static bool? TryGetBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static long? ReadTimestampMs(JsonElement el, string primary, string secondary)
    {
        foreach (var key in new[] { secondary, primary })
        {
            if (!el.TryGetProperty(key, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d))
            {
                return d > 10_000_000_000 ? (long)d : (long)(d * 1000.0);
            }
            if (prop.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(prop.GetString(), out var dto))
            {
                return dto.ToUnixTimeMilliseconds();
            }
        }
        return null;
    }
}

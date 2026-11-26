using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rpc;

namespace HermesDesktop.Core.Sessions;

/// <summary>
/// Pure helper methods for deduplicating saved dashboards, matching session aliases,
/// and applying live session title updates safely.
/// </summary>
public static class SessionCatalogHelper
{
    public static (IReadOnlyList<SavedDashboard> Kept, IReadOnlyList<SavedDashboard> Removed) DeduplicateDashboardsByUrl(
        IEnumerable<SavedDashboard> dashboards,
        Guid? activeDashboardId = null)
    {
        var list = dashboards.ToList();
        var kept = new List<SavedDashboard>();
        var removed = new List<SavedDashboard>();

        foreach (var group in list.GroupBy(d => d.NormalizedUrl, StringComparer.OrdinalIgnoreCase))
        {
            var winner = group
                .OrderByDescending(d => activeDashboardId.HasValue && d.Id == activeDashboardId.Value)
                .ThenByDescending(d => d.LastConnectedAt)
                .First();

            kept.Add(winner);
            foreach (var item in group)
            {
                if (item.Id != winner.Id)
                {
                    removed.Add(item);
                }
            }
        }

        kept.Sort((a, b) => b.LastConnectedAt.CompareTo(a.LastConnectedAt));
        return (kept, removed);
    }

    public static SavedDashboard? ResolveStartupDashboard(
        IEnumerable<SavedDashboard> dashboards,
        Guid? activeDashboardId)
    {
        var list = dashboards.ToList();
        if (list.Count == 0) return null;

        if (activeDashboardId.HasValue)
        {
            var exact = list.FirstOrDefault(d => d.Id == activeDashboardId.Value);
            if (exact is not null) return exact;
        }

        return list.OrderByDescending(d => d.LastConnectedAt).FirstOrDefault();
    }

    public static bool SessionsMatch(SessionSummary candidate, SessionSummary target, string? targetRuntimeId = null)
    {
        if (ReferenceEquals(candidate, target)) return true;

        var candidateIds = CollectSessionIds(candidate);
        if (candidateIds.Count == 0) return false;

        foreach (var id in CollectSessionIds(target, targetRuntimeId))
        {
            if (candidateIds.Contains(id)) return true;
        }
        return false;
    }

    public static HashSet<string> CollectSessionIds(SessionSummary session, string? extraRuntimeId = null)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        AddIfNonEmpty(ids, session.Id);
        AddIfNonEmpty(ids, session.StoredSessionId);
        AddIfNonEmpty(ids, session.RuntimeSessionId);
        AddIfNonEmpty(ids, extraRuntimeId);
        foreach (var alt in session.AlternateIds)
        {
            AddIfNonEmpty(ids, alt);
        }
        return ids;
    }

    public static void MergeAliases(SessionSummary target, SessionSummary source, string? extraRuntimeId = null)
    {
        if (string.IsNullOrWhiteSpace(target.RuntimeSessionId))
        {
            target.RuntimeSessionId = !string.IsNullOrWhiteSpace(extraRuntimeId)
                ? extraRuntimeId
                : (!string.IsNullOrWhiteSpace(source.RuntimeSessionId) ? source.RuntimeSessionId : null);
        }

        if (string.IsNullOrWhiteSpace(target.StoredSessionId) && !string.IsNullOrWhiteSpace(source.StoredSessionId))
        {
            target.StoredSessionId = source.StoredSessionId;
        }

        var merged = new HashSet<string>(target.AlternateIds.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
        AddIfNonEmpty(merged, source.Id);
        AddIfNonEmpty(merged, source.StoredSessionId);
        AddIfNonEmpty(merged, source.RuntimeSessionId);
        AddIfNonEmpty(merged, extraRuntimeId);
        foreach (var alt in source.AlternateIds)
        {
            AddIfNonEmpty(merged, alt);
        }
        merged.Remove(target.Id);
        target.AlternateIds = [.. merged];
    }

    public static IReadOnlyList<SessionSummary> DeduplicateSessions(IEnumerable<SessionSummary> sessions)
    {
        var result = new List<SessionSummary>();
        foreach (var candidate in sessions)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id)) continue;

            var existing = result.FirstOrDefault(s =>
                string.Equals(s.Profile ?? "", candidate.Profile ?? "", StringComparison.OrdinalIgnoreCase)
                && SessionsMatch(s, candidate));

            if (existing is null)
            {
                result.Add(candidate);
            }
            else
            {
                MergeAliases(existing, candidate);
            }
        }
        return result;
    }

    public static bool ApplyTitleUpdate(
        IEnumerable<SessionSummary> sessions,
        string? storedSessionId,
        string? runtimeSessionId,
        string title)
    {
        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle)) return false;

        var incomingIds = new HashSet<string>(StringComparer.Ordinal);
        AddIfNonEmpty(incomingIds, storedSessionId);
        AddIfNonEmpty(incomingIds, runtimeSessionId);
        if (incomingIds.Count == 0) return false;

        var updated = false;
        foreach (var s in sessions)
        {
            var known = CollectSessionIds(s);
            if (!known.Overlaps(incomingIds)) continue;

            s.Title = trimmedTitle;
            if (string.IsNullOrWhiteSpace(s.StoredSessionId) && !string.IsNullOrWhiteSpace(storedSessionId))
            {
                s.StoredSessionId = storedSessionId.Trim();
            }
            if (string.IsNullOrWhiteSpace(s.RuntimeSessionId) && !string.IsNullOrWhiteSpace(runtimeSessionId))
            {
                s.RuntimeSessionId = runtimeSessionId.Trim();
            }
            var alts = new HashSet<string>(s.AlternateIds.Where(a => !string.IsNullOrWhiteSpace(a)), StringComparer.Ordinal);
            foreach (var id in incomingIds)
            {
                if (id != s.Id) alts.Add(id);
            }
            s.AlternateIds = [.. alts];
            updated = true;
        }
        return updated;
    }

    public static IReadOnlyList<(SessionSummary Session, BackgroundTaskState NewState)> CarryOverBackgroundStatus(
        IEnumerable<SessionSummary> previousSessions,
        IEnumerable<SessionSummary> nextSessions,
        SessionSummary? activeSession,
        bool isActiveTurnRunning)
    {
        var prevList = previousSessions.ToList();
        var transitions = new List<(SessionSummary, BackgroundTaskState)>();

        foreach (var next in nextSessions)
        {
            if (activeSession is not null && SessionsMatch(next, activeSession))
            {
                next.IsActive = isActiveTurnRunning || next.IsActive;
                next.IsUnread = false;
                next.TaskStatus = BackgroundTaskState.None;
                next.BackgroundStatusDetail = null;
                continue;
            }

            var prev = prevList.FirstOrDefault(p => SessionsMatch(p, next));
            if (next.IsActive)
            {
                next.TaskStatus = BackgroundTaskState.Running;
                if (prev is not null && string.IsNullOrWhiteSpace(next.BackgroundStatusDetail))
                {
                    next.BackgroundStatusDetail = prev.BackgroundStatusDetail;
                }
                continue;
            }

            if (prev is not null)
            {
                if (prev.TaskStatus == BackgroundTaskState.Running || prev.IsActive)
                {
                    next.TaskStatus = BackgroundTaskState.Completed;
                    next.IsUnread = true;
                    transitions.Add((next, BackgroundTaskState.Completed));
                    continue;
                }

                if (prev.TaskStatus is BackgroundTaskState.Completed or BackgroundTaskState.NeedsAttention or BackgroundTaskState.Failed)
                {
                    next.TaskStatus = prev.TaskStatus;
                    next.BackgroundStatusDetail = prev.BackgroundStatusDetail;
                    next.IsUnread = true;
                    continue;
                }
            }

            if (next.IsUnread == true)
            {
                next.TaskStatus = BackgroundTaskState.Completed;
            }
        }

        return transitions;
    }

    public static (SessionSummary? Session, BackgroundTaskState? NotificationState) UpdateBackgroundSessionForEvent(
        IEnumerable<SessionSummary> sessions,
        StreamEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.SessionId)) return (null, null);

        var matched = sessions.FirstOrDefault(s => CollectSessionIds(s).Contains(ev.SessionId));
        if (matched is null) return (null, null);

        switch (ev)
        {
            case ToolStartEvent tool:
                matched.IsActive = true;
                if (matched.TaskStatus != BackgroundTaskState.NeedsAttention)
                {
                    matched.TaskStatus = BackgroundTaskState.Running;
                }
                matched.BackgroundStatusDetail = string.IsNullOrWhiteSpace(tool.ToolName) ? null : tool.ToolName;
                return (matched, null);

            case MessageStartEvent:
            case MessageDeltaEvent:
            case ReasoningDeltaEvent:
            case ToolCompleteEvent:
            case StatusUpdateEvent:
            case SessionBusyEvent { Busy: true }:
                matched.IsActive = true;
                if (matched.TaskStatus != BackgroundTaskState.NeedsAttention)
                {
                    matched.TaskStatus = BackgroundTaskState.Running;
                }
                return (matched, null);

            case ApprovalRequestEvent approval:
            {
                var notify = matched.TaskStatus != BackgroundTaskState.NeedsAttention;
                matched.TaskStatus = BackgroundTaskState.NeedsAttention;
                matched.IsUnread = true;
                matched.BackgroundStatusDetail = approval.Activity.Command;
                return (matched, notify ? BackgroundTaskState.NeedsAttention : null);
            }

            case ClarifyRequestEvent clarify:
            {
                var notify = matched.TaskStatus != BackgroundTaskState.NeedsAttention;
                matched.TaskStatus = BackgroundTaskState.NeedsAttention;
                matched.IsUnread = true;
                matched.BackgroundStatusDetail = clarify.Activity.Question;
                return (matched, notify ? BackgroundTaskState.NeedsAttention : null);
            }

            case ApprovalWithdrawnEvent:
            case ClarifyExpireEvent:
            case InputPromptExpireEvent:
                if (matched.TaskStatus == BackgroundTaskState.NeedsAttention)
                {
                    matched.TaskStatus = matched.IsActive ? BackgroundTaskState.Running : BackgroundTaskState.None;
                    matched.BackgroundStatusDetail = null;
                }
                return (matched, null);

            case MessageCompleteEvent:
            {
                var notify = matched.TaskStatus != BackgroundTaskState.Completed;
                matched.IsActive = false;
                matched.IsUnread = true;
                matched.TaskStatus = BackgroundTaskState.Completed;
                matched.BackgroundStatusDetail = null;
                return (matched, notify ? BackgroundTaskState.Completed : null);
            }

            case SessionBusyEvent { Busy: false }:
                if (matched.TaskStatus == BackgroundTaskState.Running || matched.IsActive)
                {
                    var notify = matched.TaskStatus != BackgroundTaskState.Completed;
                    matched.IsActive = false;
                    matched.IsUnread = true;
                    matched.TaskStatus = BackgroundTaskState.Completed;
                    matched.BackgroundStatusDetail = null;
                    return (matched, notify ? BackgroundTaskState.Completed : null);
                }
                return (matched, null);

            case MessageErrorEvent err:
            {
                var notify = matched.TaskStatus != BackgroundTaskState.Failed;
                matched.IsActive = false;
                matched.IsUnread = true;
                matched.TaskStatus = BackgroundTaskState.Failed;
                matched.BackgroundStatusDetail = err.Message;
                return (matched, notify ? BackgroundTaskState.Failed : null);
            }

            case MessageInterruptedEvent:
                matched.IsActive = false;
                matched.TaskStatus = BackgroundTaskState.None;
                matched.BackgroundStatusDetail = null;
                return (matched, null);

            default:
                return (matched, null);
        }
    }

    /// <summary>
    /// Filters out sessions whose primary ID or any alias matches a locally hidden session ID set.
    /// </summary>
    public static IReadOnlyList<SessionSummary> FilterHiddenSessions(
        IEnumerable<SessionSummary> sessions,
        IReadOnlyCollection<string>? hiddenSessionIds)
    {
        if (hiddenSessionIds is null || hiddenSessionIds.Count == 0)
        {
            return sessions as IReadOnlyList<SessionSummary> ?? [.. sessions];
        }

        var hiddenSet = hiddenSessionIds as HashSet<string>
            ?? new HashSet<string>(hiddenSessionIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        if (hiddenSet.Count == 0)
        {
            return sessions as IReadOnlyList<SessionSummary> ?? [.. sessions];
        }

        return sessions
            .Where(s => !CollectSessionIds(s).Any(hiddenSet.Contains))
            .ToList();
    }

    /// <summary>
    /// Returns true when a server error message indicates an interrupted gateway durable-queue execution lock.
    /// </summary>
    public static bool IsUnknownExecutionConflict(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && (message.Contains("unknown_execution", StringComparison.OrdinalIgnoreCase)
            || message.Contains("outcome-unknown", StringComparison.OrdinalIgnoreCase)
            || message.Contains("outcome_unknown", StringComparison.OrdinalIgnoreCase)
            || message.Contains("durable queue", StringComparison.OrdinalIgnoreCase));

    private static void AddIfNonEmpty(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            set.Add(value.Trim());
        }
    }
}

using HermesDesktop.Core.Models;

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

    private static void AddIfNonEmpty(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            set.Add(value.Trim());
        }
    }
}

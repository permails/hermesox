using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rpc;
using HermesDesktop.Core.Sessions;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class SessionCatalogHelperTests
{
    [Fact]
    public void DeduplicateDashboardsByUrl_KeepsActiveOrNewestEntry()
    {
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var dashboards = new[]
        {
            new SavedDashboard
            {
                Id = olderId,
                Label = "Pi Older",
                NormalizedUrl = "http://192.168.10.67:9119",
                LastConnectedAt = DateTimeOffset.UtcNow.AddHours(-2)
            },
            new SavedDashboard
            {
                Id = newerId,
                Label = "Pi Newer",
                NormalizedUrl = "HTTP://192.168.10.67:9119",
                LastConnectedAt = DateTimeOffset.UtcNow
            },
            new SavedDashboard
            {
                Id = otherId,
                Label = "Mac Local",
                NormalizedUrl = "http://localhost:9119",
                LastConnectedAt = DateTimeOffset.UtcNow.AddHours(-1)
            }
        };

        var (kept, removed) = SessionCatalogHelper.DeduplicateDashboardsByUrl(dashboards);
        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, d => d.Id == newerId);
        Assert.Contains(kept, d => d.Id == otherId);
        Assert.Single(removed);
        Assert.Equal(olderId, removed[0].Id);

        var (keptActive, removedActive) = SessionCatalogHelper.DeduplicateDashboardsByUrl(dashboards, activeDashboardId: olderId);
        Assert.Contains(keptActive, d => d.Id == olderId);
        Assert.Single(removedActive);
        Assert.Equal(newerId, removedActive[0].Id);
    }

    [Fact]
    public void ApplyTitleUpdate_DoesNotRenameUnrelatedSessionWithEmptyRuntimeId()
    {
        var previousSession = new SessionSummary
        {
            Id = "old-stored-1",
            StoredSessionId = "old-stored-1",
            RuntimeSessionId = "",
            Title = "Previous Topic"
        };
        var newSession = new SessionSummary
        {
            Id = "new-runtime-2",
            StoredSessionId = "new-stored-2",
            RuntimeSessionId = "new-runtime-2",
            Title = "New Chat"
        };

        var updated = SessionCatalogHelper.ApplyTitleUpdate(
            [previousSession, newSession],
            storedSessionId: "new-stored-2",
            runtimeSessionId: "",
            title: "Fresh Generated Title");

        Assert.True(updated);
        Assert.Equal("Previous Topic", previousSession.Title);
        Assert.Equal("Fresh Generated Title", newSession.Title);
    }

    [Fact]
    public void DeduplicateSessions_MergesRuntimeAndStoredAliasesWithoutDroppingIds()
    {
        var runtimeOnly = new SessionSummary
        {
            Id = "rt-1",
            RuntimeSessionId = "rt-1",
            AlternateIds = ["stored-1"],
            Title = "New Chat",
            Profile = "default"
        };
        var storedRow = new SessionSummary
        {
            Id = "stored-1",
            StoredSessionId = "stored-1",
            Title = "Server Title",
            Profile = "default"
        };
        var unrelated = new SessionSummary
        {
            Id = "stored-2",
            StoredSessionId = "stored-2",
            Title = "Other Chat",
            Profile = "default"
        };

        var deduped = SessionCatalogHelper.DeduplicateSessions([storedRow, runtimeOnly, unrelated]);

        Assert.Equal(2, deduped.Count);
        Assert.Equal("stored-1", deduped[0].Id);
        Assert.Equal("rt-1", deduped[0].RuntimeSessionId);
        Assert.Contains("rt-1", deduped[0].AlternateIds);
    }

    [Fact]
    public void ResolveStartupDashboard_PrefersActiveThenFallsBackToMostRecent()
    {
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var dashboards = new[]
        {
            new SavedDashboard
            {
                Id = olderId,
                Label = "Server A",
                NormalizedUrl = "http://192.168.10.67:9119",
                LastConnectedAt = DateTimeOffset.UtcNow.AddHours(-3)
            },
            new SavedDashboard
            {
                Id = newerId,
                Label = "Server B",
                NormalizedUrl = "http://localhost:9119",
                LastConnectedAt = DateTimeOffset.UtcNow
            }
        };

        // Prefers ActiveDashboardId even when another server has a newer timestamp.
        var activePick = SessionCatalogHelper.ResolveStartupDashboard(dashboards, olderId);
        Assert.Equal(olderId, activePick?.Id);

        // Falls back to the most recently connected server when ActiveDashboardId is missing or deleted.
        var fallbackPick = SessionCatalogHelper.ResolveStartupDashboard(dashboards, Guid.NewGuid());
        Assert.Equal(newerId, fallbackPick?.Id);

        Assert.Null(SessionCatalogHelper.ResolveStartupDashboard([], olderId));
    }

    [Fact]
    public void UpdateBackgroundSessionForEvent_TracksRunningApprovalCompletionAndFailure()
    {
        var bg = new SessionSummary
        {
            Id = "bg-stored",
            StoredSessionId = "bg-stored",
            RuntimeSessionId = "bg-rt",
            Title = "Long Research"
        };

        var (_, n1) = SessionCatalogHelper.UpdateBackgroundSessionForEvent([bg], new ToolStartEvent("bg-rt", "web_search", null, "t1"));
        Assert.Null(n1);
        Assert.Equal(BackgroundTaskState.Running, bg.TaskStatus);
        Assert.True(bg.ShowRunningDot);
        Assert.False(bg.ShowAlertDot);
        Assert.Equal("web_search", bg.BackgroundStatusDetail);

        var (_, n2) = SessionCatalogHelper.UpdateBackgroundSessionForEvent(
            [bg],
            new ApprovalRequestEvent("bg-rt", new ApprovalActivity("req-1", "bg-rt", "rm -rf /tmp/test", "confirm", "high", [])));
        Assert.Equal(BackgroundTaskState.NeedsAttention, n2);
        Assert.Equal(BackgroundTaskState.NeedsAttention, bg.TaskStatus);
        Assert.True(bg.ShowAlertDot);

        var (_, n3) = SessionCatalogHelper.UpdateBackgroundSessionForEvent([bg], new MessageCompleteEvent("bg-rt", "m1", "done", null, false));
        Assert.Equal(BackgroundTaskState.Completed, n3);
        Assert.Equal(BackgroundTaskState.Completed, bg.TaskStatus);
        Assert.True(bg.IsUnread);
        Assert.True(bg.ShowAlertDot);

        // A trailing SessionBusy(false) after MessageComplete must not re-fire a duplicate notification.
        var (_, n4) = SessionCatalogHelper.UpdateBackgroundSessionForEvent([bg], new SessionBusyEvent("bg-rt", false));
        Assert.Null(n4);

        var (_, n5) = SessionCatalogHelper.UpdateBackgroundSessionForEvent([bg], new MessageErrorEvent("bg-rt", "timeout"));
        Assert.Equal(BackgroundTaskState.Failed, n5);
        Assert.Equal(BackgroundTaskState.Failed, bg.TaskStatus);
        Assert.Equal("timeout", bg.BackgroundStatusDetail);
    }

    [Fact]
    public void CarryOverBackgroundStatus_DetectsCompletionAndPreservesUnreadState()
    {
        var active = new SessionSummary { Id = "active-1", Title = "Current Chat", IsActive = true };
        var prevRunning = new SessionSummary { Id = "bg-1", Title = "Task 1", IsActive = true, TaskStatus = BackgroundTaskState.Running };
        var prevDone = new SessionSummary { Id = "bg-2", Title = "Task 2", IsUnread = true, TaskStatus = BackgroundTaskState.Completed };

        var nextActive = new SessionSummary { Id = "active-1", Title = "Current Chat", IsActive = true, IsUnread = true };
        var nextFinished = new SessionSummary { Id = "bg-1", Title = "Task 1", IsActive = false };
        var nextStillDone = new SessionSummary { Id = "bg-2", Title = "Task 2", IsActive = false };

        var transitions = SessionCatalogHelper.CarryOverBackgroundStatus(
            [active, prevRunning, prevDone],
            [nextActive, nextFinished, nextStillDone],
            activeSession: nextActive,
            isActiveTurnRunning: true);

        Assert.Single(transitions);
        Assert.Equal("bg-1", transitions[0].Session.Id);
        Assert.Equal(BackgroundTaskState.Completed, transitions[0].NewState);
        Assert.Equal(BackgroundTaskState.Completed, nextFinished.TaskStatus);
        Assert.True(nextFinished.IsUnread);

        Assert.Equal(BackgroundTaskState.Completed, nextStillDone.TaskStatus);
        Assert.True(nextStillDone.IsUnread);

        Assert.Equal(BackgroundTaskState.None, nextActive.TaskStatus);
        Assert.False(nextActive.IsUnread);
    }
}

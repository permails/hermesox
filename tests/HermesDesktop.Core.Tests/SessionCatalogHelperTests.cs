using HermesDesktop.Core.Models;
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
}

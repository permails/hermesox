using HermesDesktop.Core.Models;
using HermesDesktop.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class SessionCacheTests
{
    [Fact]
    public void SessionSnapshot_MediaFilesAndCascadeDelete_RemovesDbRowsDraftsAndDiskFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hermes-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new LocalCacheStore(
                Path.Combine(tempDir, "cache.db"),
                Path.Combine(tempDir, "settings.json"),
                Path.Combine(tempDir, "media"));

            var dashId = Guid.NewGuid();
            var aliases = new[] { "stored-1", "runtime-1" };
            var messages = new List<ChatMessage>
            {
                new()
                {
                    Id = "10",
                    RowId = 10,
                    Role = MessageRole.User,
                    Content = "See attached image",
                    Timestamp = "13:00",
                    Attachments = [new Attachment("att-1", "shot.png", "/api/uploads/shot.png", "image/png", AttachmentKind.Image)]
                },
                new()
                {
                    Id = "11",
                    RowId = 11,
                    Role = MessageRole.Assistant,
                    Content = "Generated [report.md](/workspace/report.md)",
                    Timestamp = "13:01"
                }
            };

            var mediaKeys = SessionCacheHelper.CollectMediaKeys(messages);
            Assert.Contains("/api/uploads/shot.png", mediaKeys);
            Assert.Contains("/workspace/report.md", mediaKeys);

            var snapshot = new CachedSessionSnapshot(
                Revision: 3,
                ContextUsed: 12000,
                ContextMax: 128000,
                ContextPercent: 9.375,
                Model: "claude-sonnet",
                Cwd: "/workspace",
                Messages: messages,
                ReferencedMediaKeys: mediaKeys);

            store.SaveSessionSnapshot(dashId, "default", aliases, snapshot);
            store.SaveSessionListCache(dashId, "default", [
                new SessionSummary { Id = "stored-1", StoredSessionId = "stored-1", RuntimeSessionId = "runtime-1", Title = "Test Chat", Profile = "default" },
                new SessionSummary { Id = "stored-2", StoredSessionId = "stored-2", Title = "Keep Chat", Profile = "default" }
            ]);
            store.SaveDraft($"{dashId:D}:stored-1", "unsent draft");

            var sampleImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
            store.SaveCachedMediaBytes(dashId, "stored-1", "/api/uploads/shot.png", sampleImage);
            store.SaveCachedTextAsset(dashId, "stored-1", "/workspace/report.md", "# Report\nDone");

            // Verify hydration works via either alias ID
            var loaded = store.LoadSessionSnapshot(dashId, "default", ["runtime-1"]);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Messages.Count);
            Assert.Equal(12000, loaded.ContextUsed);
            Assert.Equal(128000, loaded.ContextMax);
            Assert.Equal(sampleImage, store.TryLoadCachedMediaBytes(dashId, aliases, "/api/uploads/shot.png"));
            Assert.Equal("# Report\nDone", store.TryLoadCachedTextAsset(dashId, aliases, "/workspace/report.md"));

            var diskFilesBefore = Directory.GetFiles(Path.Combine(tempDir, "media"), "*", SearchOption.AllDirectories);
            Assert.Equal(2, diskFilesBefore.Length);

            // Cascade delete
            var purged = store.DeleteSessionCacheAndFiles(dashId, "default", aliases);
            Assert.Contains("/api/uploads/shot.png", purged);
            Assert.Contains("/workspace/report.md", purged);

            Assert.Null(store.LoadSessionSnapshot(dashId, "default", aliases));
            Assert.Null(store.TryLoadCachedMediaBytes(dashId, aliases, "/api/uploads/shot.png"));
            Assert.Null(store.TryLoadCachedTextAsset(dashId, aliases, "/workspace/report.md"));
            Assert.Null(store.LoadDraft($"{dashId:D}:stored-1"));

            var remainingList = store.LoadSessionListCache(dashId, "default");
            Assert.Single(remainingList);
            Assert.Equal("stored-2", remainingList[0].Id);

            var diskFilesAfter = Directory.GetFiles(Path.Combine(tempDir, "media"), "*", SearchOption.AllDirectories);
            Assert.Empty(diskFilesAfter);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SessionCacheHelper_AreMessagesEquivalent_DetectsContentOrRowChanges()
    {
        var a = new ChatMessage { Id = "1", RowId = 1, Role = MessageRole.User, Content = "hi", Timestamp = "10:00" };
        var b = new ChatMessage { Id = "1", RowId = 1, Role = MessageRole.User, Content = "hi", Timestamp = "10:00" };
        var c = new ChatMessage { Id = "2", RowId = 2, Role = MessageRole.Assistant, Content = "hello", Timestamp = "10:01" };

        Assert.True(SessionCacheHelper.AreMessagesEquivalent([a], [b]));
        Assert.False(SessionCacheHelper.AreMessagesEquivalent([a], [a, c]));
        Assert.False(SessionCacheHelper.AreMessagesEquivalent([a], [a with { Content = "edited" }]));
    }
}

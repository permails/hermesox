using System.Text.Json;
using HermesDesktop.Core.Rpc;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class SlashCommandParserTests
{
    [Theory]
    [InlineData("/compress", "/compress", "")]
    [InlineData("  /compress focus on errors  ", "/compress", "focus on errors")]
    [InlineData("/COMPACT", "/compact", "")]
    [InlineData("/btw quick question", "/btw", "quick question")]
    public void TryParse_ParsesValidSlashCommands(string input, string expectedNorm, string expectedArgs)
    {
        Assert.True(SlashCommandParser.TryParse(input, out var parsed));
        Assert.Equal(expectedNorm, parsed.NormalizedName);
        Assert.Equal(expectedArgs, parsed.ArgsText);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/etc/hosts")]
    [InlineData("/Users/ningmini/doc.md")]
    [InlineData("hello /compress")]
    public void TryParse_RejectsPathsAndNonLeadingSlash(string input)
    {
        Assert.False(SlashCommandParser.TryParse(input, out _));
    }

    [Fact]
    public void FilterSuggestions_MatchesPrefixAndHidesAfterArgs()
    {
        var matches = SlashCommandParser.FilterSuggestions("/com", SlashCommandParser.BuiltInCommands);
        Assert.Contains(matches, m => m.Name == "/compress");
        Assert.Contains(matches, m => m.Name == "/compact");

        var withArgs = SlashCommandParser.FilterSuggestions("/compress something", SlashCommandParser.BuiltInCommands);
        Assert.Empty(withArgs);
    }

    [Fact]
    public void DecodeCatalogAndOutcome_ParsesServerResponses()
    {
        using var catDoc = JsonDocument.Parse("""
        {
          "commands": [
            { "name": "compress", "description": "Compress history", "args_hint": "[focus]", "category": "session" }
          ]
        }
        """);
        var cmds = SlashCommandParser.DecodeCatalog(catDoc.RootElement);
        Assert.Single(cmds);
        Assert.Equal("/compress", cmds[0].Name);

        using var outDoc = JsonDocument.Parse("""
        {
          "type": "exec",
          "title": "Status",
          "text": "OK"
        }
        """);
        var outcome = SlashCommandParser.DecodeExecOutcome(outDoc.RootElement);
        Assert.Equal("exec", outcome.Type);
        Assert.Equal("Status", outcome.Title);
        Assert.Equal("OK", outcome.Text);

        using var skillDoc = JsonDocument.Parse("""
        {
          "type": "skill",
          "notice": "Expanded from skill",
          "message": "Analyze this repository"
        }
        """);
        var skillOutcome = SlashCommandParser.DecodeExecOutcome(skillDoc.RootElement);
        Assert.Equal("skill", skillOutcome.Type);
        Assert.Equal("Analyze this repository", skillOutcome.Message);

        using var errDoc = JsonDocument.Parse("""
        {
          "ok": false,
          "error": "Session is busy"
        }
        """);
        var errOutcome = SlashCommandParser.DecodeExecOutcome(errDoc.RootElement);
        Assert.Equal("error", errOutcome.Type);
        Assert.Equal("Session is busy", errOutcome.Message);
    }

    [Fact]
    public void DecodeCompressResult_ParsesPendingLockHeldAndStructuredSummary()
    {
        using var pendingDoc = JsonDocument.Parse("""
        {
          "status": "pending",
          "execution_generation": 9,
          "session_id": "rt-2"
        }
        """);
        var pending = HermesWsClient.DecodeCompressResult(pendingDoc.RootElement);
        Assert.True(pending.IsPending);
        Assert.False(pending.LockHeld);
        Assert.Equal(9, pending.ExecutionGeneration);
        Assert.Equal("rt-2", pending.ResolvedSessionId);
        Assert.False(string.IsNullOrWhiteSpace(pending.SummaryText));

        using var lockDoc = JsonDocument.Parse("""
        {
          "lock_held": true,
          "message": "Compaction already running"
        }
        """);
        var locked = HermesWsClient.DecodeCompressResult(lockDoc.RootElement);
        Assert.True(locked.LockHeld);
        Assert.Equal("Compaction already running", locked.SummaryText);

        using var doneDoc = JsonDocument.Parse("""
        {
          "status": "compressed",
          "removed": 12,
          "execution_generation": 10,
          "summary": {
            "headline": "Compacted 12 messages",
            "token_line": "32k -> 8k tokens",
            "note": "Preserved tool outputs"
          },
          "messages": [
            { "id": "m1", "role": "assistant", "content": "Summary" }
          ]
        }
        """);
        var done = HermesWsClient.DecodeCompressResult(doneDoc.RootElement);
        Assert.False(done.IsPending);
        Assert.False(done.IsAborted);
        Assert.True(done.HasMessagesPayload);
        Assert.Single(done.Messages);
        Assert.Equal(12, done.Removed);
        Assert.Equal(10, done.ExecutionGeneration);
        Assert.Contains("Compacted 12 messages", done.SummaryText);
        Assert.Contains("32k -> 8k tokens", done.SummaryText);
    }

    [Fact]
    public void DecodePendingQueue_ExtractsUnknownAndQueuedEntriesOrderedBySequence()
    {
        using var doc = JsonDocument.Parse("""
        {
          "pending": [
            {
              "admission_id": "adm-2",
              "input_id": "sub-2",
              "text": "follow up",
              "status": "queued",
              "execution_generation": 4,
              "sequence": 2
            },
            {
              "admission_id": "adm-1",
              "input_id": "sub-1",
              "text": "interrupted task",
              "status": "unknown",
              "execution_generation": 3,
              "sequence": 1
            }
          ]
        }
        """);

        var pending = HermesWsClient.DecodePendingQueue(doc.RootElement);
        Assert.Equal(2, pending.Count);
        Assert.Equal("adm-1", pending[0].AdmissionId);
        Assert.Equal("unknown", pending[0].Status);
        Assert.Equal(3, pending[0].ExecutionGeneration);
        Assert.Equal("adm-2", pending[1].AdmissionId);
        Assert.Equal("queued", pending[1].Status);
    }
}

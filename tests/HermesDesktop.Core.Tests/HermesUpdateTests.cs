using System.Text.Json;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rest;
using Xunit;

namespace HermesDesktop.Core.Tests;

public sealed class HermesUpdateTests
{
    [Fact]
    public void ParseHermesUpdateCheck_ParsesCommitsAndCapabilities()
    {
        const string json = """
        {
          "install_method": "git",
          "current_version": "0.24.1",
          "behind": 2,
          "update_available": true,
          "can_apply": true,
          "commits": [
            { "sha": "abc1234", "summary": "Fix gateway restart race" },
            { "sha": "def5678", "summary": "Improve streaming resume" }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var check = HermesRestClient.ParseHermesUpdateCheck(doc.RootElement);

        Assert.Equal("git", check.InstallMethod);
        Assert.Equal("0.24.1", check.CurrentVersion);
        Assert.Equal(2, check.Behind);
        Assert.True(check.UpdateAvailable);
        Assert.True(check.CanApply);
        Assert.True(check.CanUpdateHere);
        Assert.Equal(2, check.Commits.Count);
        Assert.Equal("Fix gateway restart race", check.Commits[0].Summary);
    }

    [Fact]
    public void ParseHermesUpdateCheck_ManualInstallSetsCommandAndDisablesInPlaceUpdate()
    {
        const string json = """
        {
          "install_method": "docker",
          "current_version": "0.24.0",
          "update_available": true,
          "can_apply": false,
          "update_command": "docker compose pull && docker compose up -d",
          "message": "Update container on host"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var check = HermesRestClient.ParseHermesUpdateCheck(doc.RootElement);

        Assert.False(check.CanUpdateHere);
        Assert.Equal("docker compose pull && docker compose up -d", check.UpdateCommand);
        Assert.Equal("Update container on host", check.Message);
    }

    [Fact]
    public void EvaluateHermesUpdateVerdict_MatchesCompletedBannerAndReceipt()
    {
        var completedByBanner = new ActionStatusSnapshot(
            Running: false,
            ExitCode: null,
            Lines: ["=== hermes-update started ===", "Pulling changes...", "=== hermes-update completed act-99 ==="]);
        Assert.Equal(ActionPollVerdict.Succeeded, HermesRestClient.EvaluateHermesUpdateVerdict(completedByBanner, "act-99"));

        var completedByReceipt = new ActionStatusSnapshot(
            Running: false,
            ExitCode: null,
            Lines: ["Finished"],
            ReceiptOutcome: "partial",
            ReceiptActionId: "act-42");
        Assert.Equal(ActionPollVerdict.Succeeded, HermesRestClient.EvaluateHermesUpdateVerdict(completedByReceipt, "act-42"));

        var mismatchedReceipt = completedByReceipt with { ReceiptActionId = "old-action" };
        Assert.Equal(ActionPollVerdict.Unknown, HermesRestClient.EvaluateHermesUpdateVerdict(mismatchedReceipt, "act-42"));
    }

    [Fact]
    public void ActionStatusLatestLine_SkipsBannerAndBlanks()
    {
        var snap = new ActionStatusSnapshot(
            Running: true,
            ExitCode: null,
            Lines: ["=== gateway-restart started ===", "Stopping gateway", "", "=== trailing ==="]);

        Assert.Equal("Stopping gateway", snap.LatestLine);
    }

    [Fact]
    public void GatewayRuntimeStatus_IsServingChecksState()
    {
        Assert.True(new GatewayRuntimeStatus(true, "running", 1234).IsServing);
        Assert.True(new GatewayRuntimeStatus(true, "degraded", 1234).IsServing);
        Assert.False(new GatewayRuntimeStatus(true, "draining", 1234).IsServing);
        Assert.False(new GatewayRuntimeStatus(true, "starting", 1234).IsServing);
        Assert.False(new GatewayRuntimeStatus(false, "running", 1234).IsServing);
    }
}

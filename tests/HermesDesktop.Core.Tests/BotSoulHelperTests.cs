using System.Text.Json;
using HermesDesktop.Core.Models;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class BotSoulHelperTests
{
    [Fact]
    public void ToSlug_ProducesValidProfileSlugForUnicodeAndSpaces()
    {
        Assert.Equal("math-tutor", BotSoulHelper.ToSlug("Math Tutor"));
        Assert.True(BotSoulHelper.IsValidSlug("math-tutor"));
        Assert.False(BotSoulHelper.IsValidSlug("_bad"));
        Assert.False(BotSoulHelper.IsValidSlug("BadUpper"));

        var unicodeSlug = BotSoulHelper.ToSlug("数学老师");
        Assert.True(BotSoulHelper.IsValidSlug(unicodeSlug));
    }

    [Fact]
    public void Compose_RespectsServerInjectsProtocolFlag()
    {
        var roster = new[]
        {
            new BotProfile { Name = "default", DisplayName = "Hermes", Description = "Main" },
            new BotProfile { Name = "atlas", DisplayName = "Atlas", Description = "Research" }
        };

        var identityOnly = BotSoulHelper.Compose(
            "ada",
            "Ada Lovelace",
            "Math tutor",
            customSoul: "",
            serverInjectsProtocol: true,
            roster);

        Assert.StartsWith("# Ada Lovelace", identityOnly);
        Assert.Contains("**Mission:** Math tutor", identityOnly);
        Assert.DoesNotContain(BotSoulHelper.ProtocolHeading, identityOnly);

        var withProtocol = BotSoulHelper.Compose(
            "nova",
            "Nova",
            "",
            customSoul: "You are Nova.",
            serverInjectsProtocol: false,
            roster);

        Assert.Contains("You are Nova.", withProtocol);
        Assert.Contains(BotSoulHelper.ProtocolHeading, withProtocol);
        Assert.Contains("- `atlas` — Research", withProtocol);
    }

    [Fact]
    public void ExtractFailedSections_ReturnsFalseFlagsOnly()
    {
        using var doc = JsonDocument.Parse("""{"applied":{"soul":false,"description":true}}""");
        var failed = BotSoulHelper.ExtractFailedSections(doc.RootElement, "description", "soul");

        var item = Assert.Single(failed);
        Assert.Equal("soul", item);
    }
}

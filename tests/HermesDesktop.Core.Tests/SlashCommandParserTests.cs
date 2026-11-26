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
    }
}

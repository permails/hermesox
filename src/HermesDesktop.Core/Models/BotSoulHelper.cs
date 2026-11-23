using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HermesDesktop.Core.Models;

/// <summary>
/// BotSlug / BotSoul / BotConfigureSections helpers for creating
/// and configuring multi-profile SOUL.md personalities across Hermes gateways.
/// </summary>
public static class BotSoulHelper
{
    public const int MaxSlugLength = 64;
    public const string ProtocolHeading = "## Messaging other agents";

    public static bool IsValidSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length > MaxSlugLength) return false;
        if (!IsLowerAsciiLetterOrDigit(slug[0])) return false;
        for (var i = 1; i < slug.Length; i++)
        {
            var c = slug[i];
            if (!IsLowerAsciiLetterOrDigit(c) && c != '_' && c != '-') return false;
        }
        return true;
    }

    public static string ToSlug(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;

        var trimmed = rawName.Trim();
        var folded = new StringBuilder();
        var hadNonAscii = false;

        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (IsAsciiAlphanumeric(rune) || rune.Value is '_' or '-')
            {
                folded.Append(rune.ToString());
                continue;
            }
            if (!IsLetterOrNumber(rune))
            {
                folded.Append('-');
                continue;
            }
            var nfkd = rune.ToString().Normalize(NormalizationForm.FormKD);
            var baseChars = new StringBuilder();
            foreach (var r2 in nfkd.EnumerateRunes())
            {
                var cat = Rune.GetUnicodeCategory(r2);
                if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                    continue;
                baseChars.Append(r2.ToString());
            }
            var baseStr = baseChars.ToString();
            if (baseStr.Length > 0 && baseStr.EnumerateRunes().All(IsAsciiAlphanumeric))
            {
                folded.Append(baseStr);
            }
            else
            {
                hadNonAscii = true;
                folded.Append('-');
            }
        }

        var slug = new StringBuilder();
        var pendingDash = false;
        foreach (var c in folded.ToString().ToLowerInvariant())
        {
            if (IsLowerAsciiLetterOrDigit(c) || c == '_' || c == '-')
            {
                if (c == '-')
                {
                    pendingDash = slug.Length > 0;
                    continue;
                }
                if (pendingDash && slug.Length > 0)
                {
                    slug.Append('-');
                }
                pendingDash = false;
                slug.Append(c);
            }
            else
            {
                pendingDash = slug.Length > 0;
            }
        }

        var collapsed = CollapseDashes(slug.ToString()).Trim('-');
        if (collapsed.Length > 0 && !IsLowerAsciiLetterOrDigit(collapsed[0]))
        {
            collapsed = collapsed.TrimStart('_', '-');
        }

        if (hadNonAscii)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
            var shortHex = Convert.ToHexString(hash.AsSpan(0, 3)).ToLowerInvariant();
            if (string.IsNullOrEmpty(collapsed))
            {
                return $"bot-{shortHex}";
            }
            var prefixMax = MaxSlugLength - shortHex.Length - 1;
            var prefix = collapsed.Length > prefixMax ? collapsed[..prefixMax].Trim('-', '_') : collapsed;
            return string.IsNullOrEmpty(prefix) ? $"bot-{shortHex}" : $"{prefix}-{shortHex}";
        }

        if (collapsed.Length <= MaxSlugLength) return collapsed;

        var cut = collapsed[..MaxSlugLength];
        if (collapsed[MaxSlugLength] == '-') return cut.Trim('-');
        var lastDash = cut.LastIndexOf('-');
        if (lastDash > 0) return cut[..lastDash].Trim('-');
        return cut.Trim('-');
    }

    public static string Compose(
        string slug,
        string? title,
        string? description,
        string? customSoul,
        bool serverInjectsProtocol,
        IReadOnlyList<BotProfile> roster)
    {
        var custom = customSoul?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(custom))
        {
            if (serverInjectsProtocol || custom.Contains(ProtocolHeading, StringComparison.Ordinal))
                return custom;
            return $"{custom}\n\n{BuildMessagingProtocol(slug, roster)}";
        }

        var display = string.IsNullOrWhiteSpace(title) ? slug : title.Trim();
        var mission = description?.Trim() ?? string.Empty;
        var lines = new List<string> { $"# {display}", string.Empty };
        if (!string.IsNullOrEmpty(mission))
        {
            lines.Add($"**Mission:** {mission}");
            lines.Add(string.Empty);
        }
        lines.Add($"You are {display}, a persistent named agent (profile `{slug}`) on this machine.");
        lines.Add("You keep your own memory, skills, and conversation history across sessions.");

        var identity = string.Join("\n", lines);
        return serverInjectsProtocol
            ? identity
            : $"{identity}\n\n{BuildMessagingProtocol(slug, roster)}";
    }

    public static string BuildMessagingProtocol(string slug, IReadOnlyList<BotProfile> roster)
    {
        var teammates = roster.Where(b => !string.Equals(b.Name, slug, StringComparison.OrdinalIgnoreCase)).ToList();
        var lines = new List<string>
        {
            ProtocolHeading,
            string.Empty,
            "You work alongside other named agents. Every agent (including you) has",
            "ONE canonical conversation titled \"Bot Chat\" — created with the agent,",
            "so it always exists. Agent-to-agent messages are delivered straight",
            "into it, like a DM. To message a teammate, run:",
            string.Empty,
            "```",
            $"hermes -p <agent-name> chat --in ~ -c \"Bot Chat\" --create-if-missing -Q -q \"Message from 🤖 {slug} (@{slug}): your message\"",
            string.Empty,
            "Run the send with background=true and notify_on_complete=true on the",
            "terminal tool, then finish your turn — the reply arrives later as a",
            "background process notification. Never block waiting for it.",
            "```",
            string.Empty,
            "The roster grows over time — run `hermes profile list` for the LIVE",
            "teammate list before a handoff. Teammates when you were created:"
        };

        if (teammates.Count == 0)
        {
            lines.Add("- (none yet)");
        }
        else
        {
            foreach (var bot in teammates)
            {
                var detail = bot.Description?.Trim() ?? string.Empty;
                lines.Add(string.IsNullOrEmpty(detail) ? $"- `{bot.Name}`" : $"- `{bot.Name}` — {detail}");
            }
        }
        return string.Join("\n", lines);
    }

    public static IReadOnlyList<string> ExtractFailedSections(JsonElement configureResult, params string[] sections)
    {
        if (configureResult.ValueKind != JsonValueKind.Object
            || !configureResult.TryGetProperty("applied", out var applied)
            || applied.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var failed = new List<string>();
        foreach (var section in sections)
        {
            if (applied.TryGetProperty(section, out var flag) && flag.ValueKind == JsonValueKind.False)
            {
                failed.Add(section);
            }
        }
        return failed;
    }

    private static string CollapseDashes(string value)
    {
        var sb = new StringBuilder(value.Length);
        char prev = '\0';
        foreach (var c in value)
        {
            if (c == '-' && prev == '-') continue;
            sb.Append(c);
            prev = c;
        }
        return sb.ToString();
    }

    private static bool IsLowerAsciiLetterOrDigit(char c) => (c is >= 'a' and <= 'z') || (c is >= '0' and <= '9');

    private static bool IsAsciiAlphanumeric(Rune r) =>
        (r.Value is >= 'a' and <= 'z') || (r.Value is >= 'A' and <= 'Z') || (r.Value is >= '0' and <= '9');

    private static bool IsLetterOrNumber(Rune r) => Rune.GetUnicodeCategory(r) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or
        UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
}

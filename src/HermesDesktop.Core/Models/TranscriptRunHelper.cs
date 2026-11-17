using System.Text;
using System.Text.Json;
using HermesDesktop.Core.Localization;

namespace HermesDesktop.Core.Models;

public static class TranscriptRunHelper
{
    public static bool IsPreToolNarration(
        string? content,
        bool hasAttachments,
        bool isStreaming,
        bool followedByStepInTurn)
    {
        if (hasAttachments || isStreaming || !followedByStepInTurn) return false;
        if (string.IsNullOrWhiteSpace(content)) return false;

        var trimmed = content.Trim();
        if (trimmed.Length > 140) return false;
        if (trimmed.Contains("\n\n", StringComparison.Ordinal)) return false;
        if (trimmed.Contains("```", StringComparison.Ordinal)) return false;
        if (trimmed.StartsWith('#')) return false;
        if (trimmed.Contains("\n- ", StringComparison.Ordinal) ||
            trimmed.Contains("\n* ", StringComparison.Ordinal) ||
            trimmed.Contains("\n1. ", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    public static (IReadOnlyList<string> CallNames, string? PrimaryArg) ExtractBatchToolCalls(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput)) return ([], null);
        var trimmed = rawInput.Trim();
        if (!trimmed.StartsWith('{')) return ([], null);

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return ([], null);
            if (!doc.RootElement.TryGetProperty("calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
                return ([], null);

            var names = new List<string>();
            string? firstArg = null;
            foreach (var call in calls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object) continue;
                var name = call.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? nEl.GetString()?.Trim()
                    : null;
                if (!string.IsNullOrEmpty(name)) names.Add(name);

                if (firstArg is null)
                {
                    if (call.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                        firstArg = ExtractInterestingString(argsEl);
                    else if (call.TryGetProperty("input", out var inEl) && inEl.ValueKind == JsonValueKind.Object)
                        firstArg = ExtractInterestingString(inEl);
                }
            }
            return (names, firstArg);
        }
        catch
        {
            return ([], null);
        }
    }

    public static string FormatStepRunSummary(
        int totalSteps,
        IReadOnlyList<string> distinctLabels,
        bool isLive,
        bool? isChineseOverride = null)
    {
        var zh = isChineseOverride ?? AppLocalization.IsChinese;
        var prefix = isLive
            ? (zh ? $"正在执行过程 · {totalSteps} 个步骤" : $"Working · {totalSteps} steps")
            : (zh ? $"已完成 {totalSteps} 个操作步骤" : $"Completed {totalSteps} steps");

        if (distinctLabels.Count == 0) return prefix;

        var top = distinctLabels.Take(4);
        var sep = zh ? "、" : ", ";
        var suffix = distinctLabels.Count > 4 ? (zh ? " 等" : "…") : "";
        return $"{prefix} · {string.Join(sep, top)}{suffix}";
    }

    private static string? ExtractInterestingString(JsonElement obj)
    {
        string[] keys = ["title", "command", "cmd", "code", "script", "path", "file_path", "query", "url", "goal", "prompt", "name"];
        foreach (var k in keys)
        {
            if (obj.TryGetProperty(k, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var v = el.GetString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        if (obj.TryGetProperty("cards", out var cardsEl) && cardsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cardsEl.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.Object)
                {
                    var cardTitle = ExtractInterestingString(c);
                    if (!string.IsNullOrWhiteSpace(cardTitle)) return cardTitle;
                }
            }
        }
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var v = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(v)) return $"{prop.Name}: {v}";
            }
        }
        return null;
    }
}

using System.Text.Json;

namespace HermesDesktop.Core.Rpc;

public sealed record SlashCommandDescriptor(
    string Name,
    string Description,
    string ArgsHint = "",
    string Category = "builtin");

public sealed record ParsedSlashCommand(
    string RawCommand,
    string Name,
    string NormalizedName,
    string ArgsText,
    IReadOnlyList<string> Args);

public sealed record SlashExecOutcome(
    string Type,
    string? Title,
    string? Text,
    string? Message,
    string? NewSessionId,
    string? Prompt);

public static class SlashCommandParser
{
    public static readonly IReadOnlyList<SlashCommandDescriptor> BuiltInCommands =
    [
        new("/compress", "Compress older conversation context cleanly", "[focus]", "session"),
        new("/compact", "Alias for /compress", "[focus]", "session"),
        new("/new", "Start a fresh session", "", "session"),
        new("/reset", "Reset and start a fresh session", "", "session"),
        new("/clear", "Clear active view into a fresh session", "", "session"),
        new("/rename", "Rename the current session", "<title>", "session"),
        new("/stop", "Stop the running response or background tasks", "", "runtime"),
        new("/status", "Show active session status and usage", "", "runtime"),
        new("/usage", "Show session token usage", "", "runtime"),
        new("/insights", "Show session usage insights", "", "runtime"),
        new("/model", "Switch or inspect the active model", "[name]", "runtime"),
        new("/help", "List available native Hermes slash commands", "", "runtime"),
        new("/btw", "Ask a quick side question without altering main context", "<question>", "agent"),
        new("/bg", "Run a prompt asynchronously as a background agent", "<prompt>", "agent"),
        new("/background", "Alias for /bg", "<prompt>", "agent"),
        new("/steer", "Inject live steering into the running turn", "<instruction>", "agent"),
        new("/queue", "Queue a follow-up prompt behind the running turn", "<prompt>", "agent"),
        new("/reload-mcp", "Reload MCP server connections on the Hermes host", "", "system"),
        new("/reload-skills", "Reload skills from the Hermes host", "", "system")
    ];

    public static bool TryParse(string? input, out ParsedSlashCommand parsed)
    {
        parsed = default!;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();
        if (!trimmed.StartsWith('/')) return false;

        // Avoid treating Unix file paths like /etc/hosts or /Users/foo/bar as slash commands
        var spaceIdx = IndexOfWhitespace(trimmed);
        var head = spaceIdx >= 0 ? trimmed[..spaceIdx] : trimmed;
        if (head.Length <= 1 || head[1..].Contains('/')) return false;

        // Reject if multi-line prose starting with /something only in edge path? Wait: single command head is valid.
        var normalized = head.ToLowerInvariant();
        var argsText = spaceIdx >= 0 ? trimmed[(spaceIdx + 1)..].Trim() : "";
        var args = string.IsNullOrEmpty(argsText)
            ? []
            : argsText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        parsed = new ParsedSlashCommand(trimmed, head, normalized, argsText, args);
        return true;
    }

    public static IReadOnlyList<SlashCommandDescriptor> FilterSuggestions(
        string? composerText,
        IEnumerable<SlashCommandDescriptor> catalog,
        int maxResults = 8)
    {
        if (string.IsNullOrWhiteSpace(composerText)) return [];
        var trimmed = composerText.TrimStart();
        if (!trimmed.StartsWith('/') || trimmed.Contains('\n')) return [];

        var spaceIdx = IndexOfWhitespace(trimmed);
        if (spaceIdx >= 0) return []; // user already moved on to arguments

        var prefix = trimmed.ToLowerInvariant();
        if (prefix.Length > 1 && prefix[1..].Contains('/')) return [];

        return catalog
            .GroupBy(c => c.Name.ToLowerInvariant())
            .Select(g => g.First())
            .Where(c => prefix == "/" || c.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    public static IReadOnlyList<SlashCommandDescriptor> DecodeCatalog(JsonElement root)
    {
        var result = new List<SlashCommandDescriptor>();
        if (root.TryGetProperty("commands", out var cmdsEl) && cmdsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in cmdsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var name = TryGetString(item, "name") ?? TryGetString(item, "command");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!name.StartsWith('/')) name = "/" + name;
                var desc = TryGetString(item, "description") ?? TryGetString(item, "summary") ?? "";
                var hint = TryGetString(item, "args_hint") ?? TryGetString(item, "usage") ?? "";
                var cat = TryGetString(item, "category") ?? "server";
                result.Add(new SlashCommandDescriptor(name, desc, hint, cat));
            }
        }
        return result;
    }

    public static SlashExecOutcome DecodeExecOutcome(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            var rawText = root.ValueKind == JsonValueKind.String ? root.GetString() : null;
            return new SlashExecOutcome("exec", null, rawText, rawText, null, null);
        }

        var src = root;
        if (root.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind == JsonValueKind.Object)
        {
            src = payloadEl;
        }

        var type = TryGetString(root, "type")
                   ?? TryGetString(src, "type")
                   ?? TryGetString(root, "kind")
                   ?? TryGetString(src, "kind")
                   ?? "exec";
        var okFlag = TryGetBool(root, "ok") ?? TryGetBool(src, "ok");

        var title = TryGetString(root, "title") ?? TryGetString(src, "title");
        var text = FirstNonEmpty(
            TryGetString(root, "text"),
            TryGetString(src, "text"),
            TryGetString(root, "output"),
            TryGetString(src, "output"),
            TryGetString(root, "result"),
            TryGetString(src, "result"),
            TryGetString(root, "markdown"),
            TryGetString(src, "markdown"),
            TryGetString(root, "content"),
            TryGetString(src, "content"),
            TryGetString(root, "summary"),
            TryGetString(src, "summary"));
        var message = FirstNonEmpty(
            TryGetString(root, "message"),
            TryGetString(src, "message"),
            TryGetString(root, "notice"),
            TryGetString(src, "notice"),
            TryGetString(root, "detail"),
            TryGetString(src, "detail"),
            TryGetString(root, "error"),
            TryGetString(src, "error"));

        if (okFlag == false && !string.IsNullOrWhiteSpace(message))
        {
            type = "error";
        }

        var newSessionId = FirstNonEmpty(
            TryGetString(root, "session_id"),
            TryGetString(src, "session_id"),
            TryGetString(root, "new_session_id"),
            TryGetString(src, "new_session_id"));
        var prompt = FirstNonEmpty(
            TryGetString(root, "prompt"),
            TryGetString(src, "prompt"),
            TryGetString(root, "resolved_prompt"),
            TryGetString(src, "resolved_prompt"));

        return new SlashExecOutcome(type, title, text, message, newSessionId, prompt);
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)) return c;
        }
        return null;
    }

    private static bool? TryGetBool(JsonElement el, string propName)
    {
        if (!el.TryGetProperty(propName, out var prop)) return null;
        return prop.ValueKind is JsonValueKind.True or JsonValueKind.False ? prop.GetBoolean() : null;
    }

    private static int IndexOfWhitespace(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i])) return i;
        }
        return -1;
    }

    private static string? TryGetString(JsonElement el, string propName)
    {
        if (!el.TryGetProperty(propName, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}

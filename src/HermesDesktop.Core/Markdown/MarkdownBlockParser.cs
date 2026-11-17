using System.Text.RegularExpressions;
using HermesDesktop.Core.Rest;

namespace HermesDesktop.Core.Markdown;

public enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    CodeBlock,
    MathBlock,
    Table,
    Quote,
    List,
    HorizontalRule,
    PreviewFile,
    Image
}

public sealed record MarkdownListItem(string Marker, string Body, bool IsTask = false, bool IsChecked = false);

public sealed record MarkdownBlock(
    string Key,
    MarkdownBlockKind Kind,
    string Text = "",
    int HeadingLevel = 0,
    string CodeLanguage = "",
    IReadOnlyList<string>? TableHeaders = null,
    IReadOnlyList<IReadOnlyList<string>>? TableRows = null,
    IReadOnlyList<MarkdownListItem>? ListItems = null);

public static class MarkdownBlockParser
{
    private static readonly Regex PreviewDirectiveRegex = new(
        @"^::preview\{file=""([^""]+)""\}\s*$",
        RegexOptions.Compiled);

    private static readonly Regex MediaDirectiveRegex = new(
        @"^MEDIA:(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownImageLineRegex = new(
        @"^!\[([^\]]*)\]\(([^)\r\n]+)\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex BeginMathEnvRegex = new(
        @"^\\begin\{(equation\*?|align\*?|aligned|gather\*?|cases|dcases|pmatrix|bmatrix|vmatrix|Vmatrix|matrix|split)\}",
        RegexOptions.Compiled);

    private static readonly Regex KeycapEmojiRegex = new(
        @"([0-9#*])\uFE0F?\u20E3",
        RegexOptions.Compiled);

    public static string NormalizeTypography(string text)
    {
        if (text.IndexOf('\u20E3') < 0) return text;
        return KeycapEmojiRegex.Replace(text, m => m.Groups[1].Value switch
        {
            "0" => "⓪",
            "1" => "①",
            "2" => "②",
            "3" => "③",
            "4" => "④",
            "5" => "⑤",
            "6" => "⑥",
            "7" => "⑦",
            "8" => "⑧",
            "9" => "⑨",
            "#" => "#",
            "*" => "*",
            _ => m.Groups[1].Value
        });
    }

    public static IReadOnlyList<MarkdownBlock> Parse(string? text)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(text)) return blocks;

        var normalized = NormalizeTypography(text).Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var start = i;
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                i++;
                continue;
            }

            // 1. ::preview{file="..."}
            var previewMatch = PreviewDirectiveRegex.Match(trimmed);
            if (previewMatch.Success)
            {
                var filePath = previewMatch.Groups[1].Value;
                blocks.Add(new MarkdownBlock($"preview:{filePath}", MarkdownBlockKind.PreviewFile, Text: filePath));
                i++;
                continue;
            }

            // 2. MEDIA:/path/to/file
            var mediaMatch = MediaDirectiveRegex.Match(trimmed);
            if (mediaMatch.Success)
            {
                var filePath = mediaMatch.Groups[1].Value.Trim();
                var kind = UserAttachmentExtractor.IsImagePath(filePath)
                    ? MarkdownBlockKind.Image
                    : MarkdownBlockKind.PreviewFile;
                blocks.Add(new MarkdownBlock($"media:{filePath}", kind, Text: filePath));
                i++;
                continue;
            }

            // 2b. Standalone ![alt](url_or_path)
            var imgMatch = MarkdownImageLineRegex.Match(trimmed);
            if (imgMatch.Success)
            {
                var alt = imgMatch.Groups[1].Value.Trim();
                var imgPath = imgMatch.Groups[2].Value.Trim();
                blocks.Add(new MarkdownBlock($"img:{imgPath}", MarkdownBlockKind.Image, Text: imgPath, CodeLanguage: alt));
                i++;
                continue;
            }

            // 3. Fenced code block
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var lang = trimmed[3..].Trim();
                i++;
                var codeLines = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                var codeText = string.Join("\n", codeLines);
                blocks.Add(new MarkdownBlock($"code:{lang}:{codeText}", MarkdownBlockKind.CodeBlock, Text: codeText, CodeLanguage: lang));
                continue;
            }

            // 3b. Display Math Block: $$...$$ or \[...\] or standalone \begin{...}
            if (TryConsumeMathBlock(lines, ref i, out var mathTex))
            {
                blocks.Add(new MarkdownBlock($"math:{mathTex}", MarkdownBlockKind.MathBlock, Text: mathTex));
                continue;
            }

            // 4. Markdown Table
            if (trimmed.StartsWith('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var headerCells = ParseTableRow(line);
                i += 2;
                var rows = new List<IReadOnlyList<string>>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    rows.Add(ParseTableRow(lines[i]));
                    i++;
                }
                var rawKey = string.Join("\n", lines, start, i - start);
                blocks.Add(new MarkdownBlock($"table:{rawKey}", MarkdownBlockKind.Table, TableHeaders: headerCells, TableRows: rows));
                continue;
            }

            // 5. Horizontal rule
            if (trimmed is "---" or "***" or "___")
            {
                blocks.Add(new MarkdownBlock("hr", MarkdownBlockKind.HorizontalRule));
                i++;
                continue;
            }

            // 6. Headings (# through ######)
            if (TryParseHeading(trimmed, out var level, out var headingText))
            {
                blocks.Add(new MarkdownBlock($"h{level}:{headingText}", MarkdownBlockKind.Heading, Text: headingText, HeadingLevel: level));
                i++;
                continue;
            }

            // 7. Blockquote
            if (trimmed.StartsWith('>'))
            {
                var quoteLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    quoteLines.Add(lines[i].TrimStart().TrimStart('>').TrimStart());
                    i++;
                }
                var qText = string.Join("\n", quoteLines);
                blocks.Add(new MarkdownBlock($"quote:{qText}", MarkdownBlockKind.Quote, Text: qText));
                continue;
            }

            // 8. Bullet or numbered list
            if (IsListLine(trimmed))
            {
                var items = new List<MarkdownListItem>();
                while (i < lines.Length && IsListLine(lines[i].Trim()))
                {
                    items.Add(SplitListItem(lines[i].Trim()));
                    i++;
                }
                var rawKey = string.Join("\n", lines, start, i - start);
                blocks.Add(new MarkdownBlock($"list:{rawKey}", MarkdownBlockKind.List, ListItems: items));
                continue;
            }

            // 9. Normal paragraph — ALWAYS consume the starting line first so i strictly advances (prevents infinite loops on '#tag' or partial '###').
            var paraLines = new List<string> { lines[i] };
            i++;
            while (i < lines.Length)
            {
                var cur = lines[i].Trim();
                if (string.IsNullOrEmpty(cur)
                    || cur.StartsWith("```", StringComparison.Ordinal)
                    || TryParseHeading(cur, out _, out _)
                    || cur.StartsWith('>')
                    || IsListLine(cur)
                    || PreviewDirectiveRegex.IsMatch(cur)
                    || MediaDirectiveRegex.IsMatch(cur)
                    || MarkdownImageLineRegex.IsMatch(cur)
                    || cur.StartsWith("$$", StringComparison.Ordinal)
                    || cur.StartsWith(@"\[", StringComparison.Ordinal)
                    || BeginMathEnvRegex.IsMatch(cur)
                    || (cur.StartsWith('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1])))
                {
                    break;
                }
                paraLines.Add(lines[i]);
                i++;
            }
            var paraText = string.Join("\n", paraLines);
            blocks.Add(new MarkdownBlock($"p:{paraText}", MarkdownBlockKind.Paragraph, Text: paraText));
        }

        return blocks;
    }

    private static bool TryConsumeMathBlock(string[] lines, ref int i, out string mathTex)
    {
        mathTex = string.Empty;
        var trimmed = lines[i].Trim();

        // $$ ... $$ single line
        if (trimmed.StartsWith("$$", StringComparison.Ordinal))
        {
            if (trimmed.Length > 4 && trimmed.EndsWith("$$", StringComparison.Ordinal))
            {
                mathTex = trimmed[2..^2].Trim();
                i++;
                return true;
            }

            var buf = new List<string>();
            var firstRest = trimmed[2..].Trim();
            if (!string.IsNullOrEmpty(firstRest)) buf.Add(firstRest);
            i++;
            while (i < lines.Length)
            {
                var cur = lines[i].Trim();
                if (cur.EndsWith("$$", StringComparison.Ordinal))
                {
                    var tail = cur[..^2].Trim();
                    if (!string.IsNullOrEmpty(tail)) buf.Add(tail);
                    i++;
                    break;
                }
                buf.Add(lines[i]);
                i++;
            }
            mathTex = string.Join("\n", buf).Trim();
            return true;
        }

        // \[ ... \] single line or multiline
        if (trimmed.StartsWith(@"\[", StringComparison.Ordinal))
        {
            if (trimmed.Length > 4 && trimmed.EndsWith(@"\]", StringComparison.Ordinal))
            {
                mathTex = trimmed[2..^2].Trim();
                i++;
                return true;
            }

            var buf = new List<string>();
            var firstRest = trimmed[2..].Trim();
            if (!string.IsNullOrEmpty(firstRest)) buf.Add(firstRest);
            i++;
            while (i < lines.Length)
            {
                var cur = lines[i].Trim();
                if (cur.EndsWith(@"\]", StringComparison.Ordinal))
                {
                    var tail = cur[..^2].Trim();
                    if (!string.IsNullOrEmpty(tail)) buf.Add(tail);
                    i++;
                    break;
                }
                buf.Add(lines[i]);
                i++;
            }
            mathTex = string.Join("\n", buf).Trim();
            return true;
        }

        // \begin{env}...\end{env}
        var envMatch = BeginMathEnvRegex.Match(trimmed);
        if (envMatch.Success)
        {
            var env = envMatch.Groups[1].Value;
            var endTag = $@"\end{{{env}}}";
            var buf = new List<string> { trimmed };
            if (trimmed.Contains(endTag, StringComparison.Ordinal))
            {
                i++;
                mathTex = trimmed;
                return true;
            }
            i++;
            while (i < lines.Length)
            {
                buf.Add(lines[i]);
                var cur = lines[i].Trim();
                i++;
                if (cur.Contains(endTag, StringComparison.Ordinal)) break;
            }
            mathTex = string.Join("\n", buf).Trim();
            return true;
        }

        return false;
    }

    private static bool TryParseHeading(string trimmed, out int level, out string headingText)
    {
        level = 0;
        headingText = string.Empty;
        if (!trimmed.StartsWith('#')) return false;
        while (level < trimmed.Length && level < 6 && trimmed[level] == '#') level++;
        if (level == 0 || level >= trimmed.Length || trimmed[level] != ' ') return false;
        headingText = trimmed[(level + 1)..].Trim();
        return true;
    }

    private static bool IsTableSeparator(string line)
    {
        var t = line.Trim();
        return t.StartsWith('|') && t.Contains("---");
    }

    private static List<string> ParseTableRow(string line)
    {
        var trimmed = line.Trim().Trim('|');
        return trimmed.Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static bool IsListLine(string trimmed)
    {
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            return true;
        var dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
        return dot is > 0 and <= 3 && trimmed[..dot].All(char.IsDigit);
    }

    private static MarkdownListItem SplitListItem(string trimmed)
    {
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            var body = trimmed[2..].TrimStart();
            if (body.StartsWith("[ ] ", StringComparison.Ordinal))
                return new MarkdownListItem("☐", body[4..].TrimStart(), IsTask: true, IsChecked: false);
            if (body.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase))
                return new MarkdownListItem("☑", body[4..].TrimStart(), IsTask: true, IsChecked: true);
            return new MarkdownListItem("•", body);
        }
        var dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
        return new MarkdownListItem(trimmed[..(dot + 1)], trimmed[(dot + 2)..].TrimStart());
    }
}

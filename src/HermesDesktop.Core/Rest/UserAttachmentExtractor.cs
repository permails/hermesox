using System.Text.RegularExpressions;
using HermesDesktop.Core.Models;

namespace HermesDesktop.Core.Rest;

public static class UserAttachmentExtractor
{
    private static readonly Regex ImageReferenceRegex = new(
        @"@image:(?:`([^`\r\n]+)`|""([^""\r\n]+)""|'([^'\r\n]+)'|([^\s]+))",
        RegexOptions.Compiled);

    private static readonly Regex ImageHintRegex = new(
        @"\[Image attached at: ([^\n\]]+)\]",
        RegexOptions.Compiled);

    private static readonly Regex FileReferenceLineRegex = new(
        @"^@file:(?:`([^`\r\n]+)`|""([^""\r\n]+)""|'([^'\r\n]+)'|(\S+))$",
        RegexOptions.Compiled);

    private static readonly Regex ContextFooterRegex = new(
        @"(?:^|\r?\n)--- (?:Context Warnings|Attached Context) ---[ \t]*(?:\r?\n|$)",
        RegexOptions.Compiled);

    private static readonly Regex CompactionRestatementRegex = new(
        @"^\[STILL IN PROGRESS\s+[^\]]+\]\s*",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tiff", ".ico"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi", ".wmv", ".flv", ".3gp"
    };

    public static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(StripQuery(path));
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    public static AttachmentKind ClassifyKind(string path)
    {
        var ext = Path.GetExtension(StripQuery(path));
        if (!string.IsNullOrEmpty(ext))
        {
            if (ImageExtensions.Contains(ext)) return AttachmentKind.Image;
            if (VideoExtensions.Contains(ext)) return AttachmentKind.Video;
        }
        return AttachmentKind.Document;
    }

    public static (string CleanContent, List<Attachment> Attachments) Extract(
        string rawContent,
        string messageId,
        IReadOnlyList<Attachment>? existingPartAttachments = null)
    {
        var attachments = existingPartAttachments is not null
            ? new List<Attachment>(existingPartAttachments)
            : [];
        var seenUris = new HashSet<string>(attachments.Select(a => a.Uri), StringComparer.Ordinal);
        var text = rawContent ?? string.Empty;

        // 0. Strip Hermes compaction restatement boilerplate if followed by actual user message
        var compactionMatch = CompactionRestatementRegex.Match(text);
        if (compactionMatch.Success && compactionMatch.Length < text.Length)
        {
            text = text[compactionMatch.Length..];
        }

        // 1. Strip Hermes expanded context footer when reference tokens are present
        var footerMatch = ContextFooterRegex.Match(text);
        if (footerMatch.Success && (text.Contains("@file:", StringComparison.Ordinal) || text.Contains("@image:", StringComparison.Ordinal)))
        {
            text = text[..footerMatch.Index].TrimEnd();
        }

        // 2. Extract @image:... tokens
        var imgIndex = 0;
        text = ImageReferenceRegex.Replace(text, m =>
        {
            var path = FirstGroup(m);
            if (!string.IsNullOrWhiteSpace(path) && seenUris.Add(path))
            {
                attachments.Add(new Attachment(
                    Id: $"{messageId}-img-{imgIndex++}",
                    Name: SafeFileName(path, "Image"),
                    Uri: path,
                    MimeType: null,
                    Kind: AttachmentKind.Image));
            }
            return string.Empty;
        });

        // 3. Extract [Image attached at: ...] hints
        text = ImageHintRegex.Replace(text, m =>
        {
            var path = m.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(path) && seenUris.Add(path))
            {
                attachments.Add(new Attachment(
                    Id: $"{messageId}-img-{imgIndex++}",
                    Name: SafeFileName(path, "Image"),
                    Uri: path,
                    MimeType: null,
                    Kind: AttachmentKind.Image));
            }
            return string.Empty;
        });

        // 4. Extract leading @file:... lines
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var fileIndex = 0;
        while (lines.Count > 0)
        {
            var line = lines[0].TrimEnd();
            var fm = FileReferenceLineRegex.Match(line);
            if (!fm.Success) break;
            var path = FirstGroup(fm);
            if (string.IsNullOrWhiteSpace(path)) break;
            if (seenUris.Add(path))
            {
                attachments.Add(new Attachment(
                    Id: $"{messageId}-file-{fileIndex++}",
                    Name: SafeFileName(path, "Attachment"),
                    Uri: path,
                    MimeType: null,
                    Kind: ClassifyKind(path)));
            }
            lines.RemoveAt(0);
        }

        var cleaned = string.Join("\n", lines).Trim();
        return (cleaned, attachments);
    }

    private static string FirstGroup(Match m)
    {
        for (var i = 1; i < m.Groups.Count; i++)
        {
            if (m.Groups[i].Success && !string.IsNullOrWhiteSpace(m.Groups[i].Value))
                return m.Groups[i].Value.Trim();
        }
        return string.Empty;
    }

    private static string SafeFileName(string path, string fallback)
    {
        var name = Path.GetFileName(StripQuery(path));
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string StripQuery(string path)
    {
        var q = path.IndexOf('?');
        return q >= 0 ? path[..q] : path;
    }
}

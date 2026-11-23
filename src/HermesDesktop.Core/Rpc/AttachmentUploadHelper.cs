using System.Text.Json;

namespace HermesDesktop.Core.Rpc;

/// <summary>
/// Helpers for preparing file/image uploads and formatting Hermes @file: references.
/// </summary>
public static class AttachmentUploadHelper
{
    public const int DefaultMaxAttachmentMegabytes = 256;
    public const int MaxConfigurableAttachmentMegabytes = 512;
    public const long MaxAttachmentBytes = (long)DefaultMaxAttachmentMegabytes * 1024 * 1024;

    public static long ClampByteLimit(int megabytes)
    {
        var clamped = megabytes <= 0
            ? DefaultMaxAttachmentMegabytes
            : Math.Clamp(megabytes, 16, MaxConfigurableAttachmentMegabytes);
        return (long)clamped * 1024 * 1024;
    }

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".tiff"] = "image/tiff",
        [".ico"] = "image/x-icon",
        [".svg"] = "image/svg+xml",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".csv"] = "text/csv",
        [".log"] = "text/plain",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".py"] = "text/x-python",
        [".cs"] = "text/plain",
        [".js"] = "text/javascript",
        [".ts"] = "text/typescript",
        [".sh"] = "text/x-shellscript",
        [".ps1"] = "text/plain",
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/x-m4v",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".wmv"] = "video/x-ms-wmv",
        [".flv"] = "video/x-flv",
        [".m2ts"] = "video/mp2t",
        [".3gp"] = "video/3gpp",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
        [".ogg"] = "audio/ogg",
        [".flac"] = "audio/flac",
        [".aac"] = "audio/aac",
        [".zip"] = "application/zip"
    };

    private static readonly char[] QuotingChars =
        [' ', '\t', '\r', '\n', '(', ')', '[', ']', '{', '}', '<', '>', '"', '\'', '`'];

    public static TimeSpan UploadTimeoutForPayloadLength(long charCount) =>
        TimeSpan.FromSeconds(Math.Max(120, charCount / 64_000.0));

    public static string GuessMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (!string.IsNullOrEmpty(ext) && MimeByExtension.TryGetValue(ext, out var mime))
        {
            return mime;
        }
        return "application/octet-stream";
    }

    public static string BuildDataUrl(byte[] bytes, string mimeType) =>
        $"data:{(string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType)};base64,{Convert.ToBase64String(bytes)}";

    public static string FormatFileSize(long byteCount)
    {
        if (byteCount < 1024) return $"{byteCount} B";
        if (byteCount < 1024 * 1024) return $"{byteCount / 1024.0:0.#} KB";
        return $"{byteCount / (1024.0 * 1024.0):0.#} MB";
    }

    public static string FormatReferenceValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(QuotingChars) < 0) return value;
        foreach (var quote in new[] { "`", "\"", "'" })
        {
            if (!value.Contains(quote, StringComparison.Ordinal))
            {
                return $"{quote}{value}{quote}";
            }
        }
        return value;
    }

    public static string? BuildFileReferenceFromRpcResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return null;

        if (result.TryGetProperty("ref_text", out var refEl) && refEl.ValueKind == JsonValueKind.String)
        {
            var refText = refEl.GetString()?.Trim();
            if (!string.IsNullOrEmpty(refText)
                && refText.StartsWith("@file:", StringComparison.Ordinal)
                && refText.Length > "@file:".Length)
            {
                return refText;
            }
        }

        string? path = null;
        foreach (var key in new[] { "ref_path", "path" })
        {
            if (result.TryGetProperty(key, out var pEl) && pEl.ValueKind == JsonValueKind.String)
            {
                var candidate = pEl.GetString()?.Trim();
                if (!string.IsNullOrEmpty(candidate))
                {
                    path = candidate;
                    break;
                }
            }
        }

        return string.IsNullOrEmpty(path) ? null : $"@file:{FormatReferenceValue(path)}";
    }

    public static string? ExtractPathFromFileReference(string? fileReference)
    {
        if (string.IsNullOrWhiteSpace(fileReference)) return null;
        var trimmed = fileReference.Trim();
        if (!trimmed.StartsWith("@file:", StringComparison.Ordinal)) return null;
        var raw = trimmed["@file:".Length..].Trim();
        if (raw.Length >= 2 && ((raw[0] == '`' && raw[^1] == '`')
            || (raw[0] == '"' && raw[^1] == '"')
            || (raw[0] == '\'' && raw[^1] == '\'')))
        {
            raw = raw[1..^1];
        }
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    public static string BuildPromptWithFileReferences(string typedText, IReadOnlyList<string> fileReferences)
    {
        var refs = string.Join("\n", fileReferences.Where(r => !string.IsNullOrWhiteSpace(r)));
        if (string.IsNullOrEmpty(refs)) return typedText;
        return string.IsNullOrWhiteSpace(typedText) ? refs : $"{refs}\n{typedText}";
    }

    public static bool IsImageAttachRefusal(Exception ex)
    {
        if (ex is not RpcException rpcEx) return false;
        var msg = rpcEx.Message ?? string.Empty;
        if (msg.Contains("session not found", StringComparison.OrdinalIgnoreCase)) return false;
        return msg.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("format", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("extension", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("too large", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("size", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("invalid image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rpcEx.Reason, "invalid_params", StringComparison.OrdinalIgnoreCase);
    }
}

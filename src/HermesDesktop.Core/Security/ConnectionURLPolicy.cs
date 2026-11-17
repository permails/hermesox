namespace HermesDesktop.Core.Security;

public enum ConnectionUrlErrorKind
{
    InvalidUrl,
    InsecureTransport
}

public sealed class ConnectionUrlException(ConnectionUrlErrorKind kind, string message) : Exception(message)
{
    public ConnectionUrlErrorKind Kind { get; } = kind;
}

/// <summary>
/// Connection URL transport policy:
/// - Allows https:// for any host.
/// - Allows http:// only for loopback, RFC1918 private LAN, and Tailscale (*.ts.net or 100.64.0.0/10).
/// - Rejects embedded userinfo, query, and fragment on base URLs.
/// </summary>
public static class ConnectionURLPolicy
{
    public static string NormalizeBaseUrl(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed) || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new ConnectionUrlException(ConnectionUrlErrorKind.InvalidUrl, "Enter a valid dashboard URL.");
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.DnsSafeHost?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ConnectionUrlException(ConnectionUrlErrorKind.InvalidUrl, "Enter a valid dashboard URL.");
        }

        if (scheme != "https" && scheme != "http")
        {
            throw new ConnectionUrlException(ConnectionUrlErrorKind.InvalidUrl, "Enter a valid dashboard URL.");
        }

        if (scheme == "http" && !IsInsecureTransportAllowed(host))
        {
            throw new ConnectionUrlException(
                ConnectionUrlErrorKind.InsecureTransport,
                "Remote dashboards must use HTTPS; HTTP is allowed only for local networks (localhost, private LAN, and Tailscale).");
        }

        while (trimmed.Length > scheme.Length + 3 && trimmed.EndsWith('/'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed;
    }

    public static bool IsAllowedTransport(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return false;
        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(host) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        return scheme == "https" || (scheme == "http" && IsInsecureTransportAllowed(host));
    }

    public static Uri BuildWebSocketUri(string baseUrl, string path, IReadOnlyDictionary<string, string>? query = null)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        var baseUri = new Uri(normalized, UriKind.Absolute);

        var wsScheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var basePath = baseUri.AbsolutePath.Trim('/');
        var routePath = path.Trim('/');
        var joinedPath = string.Join('/', new[] { basePath, routePath }.Where(s => !string.IsNullOrEmpty(s)));

        var builder = new UriBuilder(baseUri)
        {
            Scheme = wsScheme,
            Path = "/" + joinedPath,
            Fragment = string.Empty
        };

        if (query is { Count: > 0 })
        {
            builder.Query = string.Join("&", query.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        }
        else
        {
            builder.Query = string.Empty;
        }

        return builder.Uri;
    }

    public static bool IsInsecureTransportAllowed(string rawHost)
    {
        var host = rawHost.ToLowerInvariant().Trim('[', ']');
        return IsLoopbackHost(host) || IsPrivateLanHost(host) || IsTailscaleHost(host);
    }

    public static bool IsLoopbackHost(string rawHost)
    {
        var host = rawHost.ToLowerInvariant().Trim('[', ']');
        return host is "localhost" or "127.0.0.1" or "::1";
    }

    public static bool IsTailscaleHost(string rawHost)
    {
        var host = rawHost.ToLowerInvariant().Trim('[', ']');
        if (host.EndsWith(".ts.net", StringComparison.Ordinal)) return true;
        if (!TryParseIpv4Octets(host, out var octets)) return false;
        return octets[0] == 100 && octets[1] >= 64 && octets[1] <= 127;
    }

    public static bool IsPrivateLanHost(string rawHost)
    {
        var host = rawHost.ToLowerInvariant().Trim('[', ']');
        if (!TryParseIpv4Octets(host, out var octets)) return false;
        return octets[0] switch
        {
            10 => true,
            172 => octets[1] is >= 16 and <= 31,
            192 => octets[1] == 168,
            _ => false
        };
    }

    public static bool TryParseIpv4Octets(string value, out int[] octets)
    {
        octets = [];
        var parts = value.Split('.');
        if (parts.Length != 4) return false;

        var result = new int[4];
        for (var i = 0; i < 4; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || (part.Length > 1 && part[0] == '0')) return false;
            foreach (var c in part)
            {
                if (c < '0' || c > '9') return false;
            }
            if (!int.TryParse(part, out var num) || num < 0 || num > 255) return false;
            result[i] = num;
        }

        octets = result;
        return true;
    }
}

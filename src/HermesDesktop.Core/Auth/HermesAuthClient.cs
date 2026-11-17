using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Security;

namespace HermesDesktop.Core.Auth;

public sealed record DashboardSecrets
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? SessionToken { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? CfAccessClientId { get; init; }
    public string? CfAccessClientSecret { get; init; }
    public IReadOnlyList<string> Cookies { get; init; } = [];
}

public sealed record AuthProviderDiscovery(
    bool SupportsPassword,
    bool SupportsSessionToken,
    bool SupportsNativePkce,
    bool InteractiveSignInRequired);

public sealed record AuthSessionTicket(
    string Ticket,
    CookieContainer CookieJar,
    IReadOnlyList<string> SerializedCookies,
    string? BearerToken,
    string? SessionToken);

public sealed class AuthClientException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed class HermesAuthClient
{
    public static void ApplySecurityHeaders(HttpRequestHeaders headers, Uri targetUri, DashboardSecrets? secrets)
    {
        if (secrets is null) return;

        if (!string.IsNullOrWhiteSpace(secrets.SessionToken))
        {
            headers.TryAddWithoutValidation("X-Hermes-Session-Token", secrets.SessionToken.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(secrets.AccessToken))
        {
            headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.AccessToken.Trim());
        }

        // Cloudflare Access service tokens are ONLY injected over HTTPS/WSS.
        var isTls = targetUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || targetUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);

        if (isTls
            && !string.IsNullOrWhiteSpace(secrets.CfAccessClientId)
            && !string.IsNullOrWhiteSpace(secrets.CfAccessClientSecret))
        {
            headers.TryAddWithoutValidation("CF-Access-Client-Id", secrets.CfAccessClientId.Trim());
            headers.TryAddWithoutValidation("CF-Access-Client-Secret", secrets.CfAccessClientSecret.Trim());
        }
    }

    public async Task<AuthProviderDiscovery> DiscoverAsync(
        string baseUrl,
        DashboardSecrets? secrets = null,
        CancellationToken ct = default)
    {
        var normalized = ConnectionURLPolicy.NormalizeBaseUrl(baseUrl);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        var providersUri = new Uri($"{normalized}/api/auth/providers");
        using var req = new HttpRequestMessage(HttpMethod.Get, providersUri);
        ApplySecurityHeaders(req.Headers, providersUri, secrets);

        var resp = await client.SendAsync(req, ct);
        if ((int)resp.StatusCode is >= 300 and <= 399)
        {
            return new AuthProviderDiscovery(false, false, false, InteractiveSignInRequired: true);
        }

        var supportsPassword = false;
        var supportsPkce = false;
        var supportsSessionToken = false;

        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("providers", out var providersEl) && providersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in providersEl.EnumerateArray())
                {
                    if (p.TryGetProperty("supports_password", out var sp) && sp.ValueKind == JsonValueKind.True)
                    {
                        supportsPassword = true;
                    }
                }
            }
            if (doc.RootElement.TryGetProperty("auth_flows", out var flowsEl) && flowsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in flowsEl.EnumerateArray())
                {
                    if (f.GetString() == "native_pkce") supportsPkce = true;
                }
            }
        }

        // Check /api/status fallback for session-token and native_pkce advertisements
        var statusUri = new Uri($"{normalized}/api/status");
        using var statusReq = new HttpRequestMessage(HttpMethod.Get, statusUri);
        ApplySecurityHeaders(statusReq.Headers, statusUri, secrets);
        var statusResp = await client.SendAsync(statusReq, ct);
        if (statusResp.IsSuccessStatusCode)
        {
            var statusBody = await statusResp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(statusBody);
            if (doc.RootElement.TryGetProperty("auth_flows", out var flowsEl) && flowsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in flowsEl.EnumerateArray())
                {
                    if (f.GetString() == "native_pkce") supportsPkce = true;
                }
            }
            if (doc.RootElement.TryGetProperty("auth_providers", out var apEl) && apEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var ap in apEl.EnumerateArray())
                {
                    if (ap.GetString() == "session-token") supportsSessionToken = true;
                }
            }
        }

        return new AuthProviderDiscovery(supportsPassword, supportsSessionToken, supportsPkce, InteractiveSignInRequired: false);
    }

    public async Task<AuthSessionTicket> ConnectWithPasswordAsync(
        string baseUrl,
        string username,
        string password,
        DashboardSecrets? proxySecrets = null,
        CancellationToken ct = default)
    {
        var normalized = ConnectionURLPolicy.NormalizeBaseUrl(baseUrl);
        var cookieJar = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieJar,
            UseCookies = true,
            AllowAutoRedirect = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        var loginUri = new Uri($"{normalized}/auth/password-login");
        var payload = JsonSerializer.Serialize(new
        {
            provider = "basic",
            username,
            password
        });

        using var loginReq = new HttpRequestMessage(HttpMethod.Post, loginUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        ApplySecurityHeaders(loginReq.Headers, loginUri, proxySecrets);

        var loginResp = await client.SendAsync(loginReq, ct);
        if (!loginResp.IsSuccessStatusCode)
        {
            var msg = loginResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "Login failed. Check your dashboard credentials and try again."
                : $"Login failed: HTTP {(int)loginResp.StatusCode}";
            throw new AuthClientException(msg, loginResp.StatusCode);
        }

        return await MintWsTicketInternalAsync(normalized, client, cookieJar, proxySecrets, ct);
    }

    public async Task<AuthSessionTicket> ConnectWithTokenAsync(
        string baseUrl,
        DashboardSecrets secrets,
        CancellationToken ct = default)
    {
        var normalized = ConnectionURLPolicy.NormalizeBaseUrl(baseUrl);
        var cookieJar = new CookieContainer();
        var baseUri = new Uri(normalized);
        foreach (var rawCookie in secrets.Cookies)
        {
            try
            {
                cookieJar.SetCookies(baseUri, rawCookie);
            }
            catch
            {
                // Ignore malformed persisted cookie
            }
        }

        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieJar,
            UseCookies = true,
            AllowAutoRedirect = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        return await MintWsTicketInternalAsync(normalized, client, cookieJar, secrets, ct);
    }

    private static async Task<AuthSessionTicket> MintWsTicketInternalAsync(
        string normalizedBaseUrl,
        HttpClient client,
        CookieContainer cookieJar,
        DashboardSecrets? secrets,
        CancellationToken ct)
    {
        var ticketUri = new Uri($"{normalizedBaseUrl}/api/auth/ws-ticket");
        using var ticketReq = new HttpRequestMessage(HttpMethod.Post, ticketUri);
        ApplySecurityHeaders(ticketReq.Headers, ticketUri, secrets);

        var ticketResp = await client.SendAsync(ticketReq, ct);
        if (!ticketResp.IsSuccessStatusCode)
        {
            throw new AuthClientException(
                $"Could not mint WebSocket ticket: HTTP {(int)ticketResp.StatusCode}",
                ticketResp.StatusCode);
        }

        var body = await ticketResp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("ticket", out var ticketEl) || string.IsNullOrWhiteSpace(ticketEl.GetString()))
        {
            throw new AuthClientException("Dashboard returned an empty WebSocket ticket.", ticketResp.StatusCode);
        }

        var ticket = ticketEl.GetString()!.Trim();
        var cookies = cookieJar.GetCookies(new Uri(normalizedBaseUrl))
            .Cast<Cookie>()
            .Select(c => $"{c.Name}={c.Value}")
            .ToList();

        return new AuthSessionTicket(
            ticket,
            cookieJar,
            cookies,
            secrets?.AccessToken,
            secrets?.SessionToken);
    }

    public static (string Verifier, string Challenge) GeneratePkcePair()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(bytes);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(hash);
        return (verifier, challenge);
    }

    public static string BuildPkceAuthorizeUrl(string baseUrl, string codeChallenge, string redirectUri, string state, string? provider = null)
    {
        var normalized = ConnectionURLPolicy.NormalizeBaseUrl(baseUrl);
        var sb = new StringBuilder($"{normalized}/auth/native/authorize?");
        sb.Append("code_challenge=").Append(Uri.EscapeDataString(codeChallenge));
        sb.Append("&code_challenge_method=S256");
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        sb.Append("&state=").Append(Uri.EscapeDataString(state));
        if (!string.IsNullOrWhiteSpace(provider))
        {
            sb.Append("&provider=").Append(Uri.EscapeDataString(provider));
        }
        return sb.ToString();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

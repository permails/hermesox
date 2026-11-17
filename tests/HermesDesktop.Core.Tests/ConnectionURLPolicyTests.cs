using HermesDesktop.Core.Security;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class ConnectionURLPolicyTests
{
    [Theory]
    [InlineData("https://hermes.example.com/", "https://hermes.example.com")]
    [InlineData("https://hermes.example.com/subpath/", "https://hermes.example.com/subpath")]
    [InlineData("http://localhost:9119/", "http://localhost:9119")]
    [InlineData("http://127.0.0.1:9119", "http://127.0.0.1:9119")]
    [InlineData("http://192.168.1.42:9119", "http://192.168.1.42:9119")]
    [InlineData("http://10.0.0.5:9119", "http://10.0.0.5:9119")]
    [InlineData("http://172.16.0.8:9119", "http://172.16.0.8:9119")]
    [InlineData("http://my-mac.tailnet.ts.net:9119", "http://my-mac.tailnet.ts.net:9119")]
    [InlineData("http://100.64.0.12:9119", "http://100.64.0.12:9119")]
    [InlineData("http://100.127.255.254:9119", "http://100.127.255.254:9119")]
    public void NormalizeBaseUrl_AllowsValidLocalTailscaleAndHttps(string input, string expected)
    {
        var actual = ConnectionURLPolicy.NormalizeBaseUrl(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("http://100.63.255.255")]
    [InlineData("http://100.128.0.1")]
    [InlineData("http://010.0.0.1")]
    [InlineData("http://100.64.attacker.example")]
    public void NormalizeBaseUrl_RejectsRemoteHttpAndBypassHosts(string input)
    {
        var ex = Assert.Throws<ConnectionUrlException>(() => ConnectionURLPolicy.NormalizeBaseUrl(input));
        Assert.Equal(ConnectionUrlErrorKind.InsecureTransport, ex.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://localhost:9119")]
    [InlineData("https://user:pass@hermes.example.com")]
    [InlineData("https://hermes.example.com?foo=bar")]
    public void NormalizeBaseUrl_RejectsInvalidSchemesAndUserInfo(string input)
    {
        var ex = Assert.Throws<ConnectionUrlException>(() => ConnectionURLPolicy.NormalizeBaseUrl(input));
        Assert.Equal(ConnectionUrlErrorKind.InvalidUrl, ex.Kind);
    }

    [Fact]
    public void BuildWebSocketUri_PreservesBasePathAndMapsScheme()
    {
        var uri = ConnectionURLPolicy.BuildWebSocketUri(
            "https://hermes.example.com/gw/",
            "/api/ws",
            new Dictionary<string, string> { ["ticket"] = "abc-123" });

        Assert.Equal("wss://hermes.example.com/gw/api/ws?ticket=abc-123", uri.ToString());
    }
}

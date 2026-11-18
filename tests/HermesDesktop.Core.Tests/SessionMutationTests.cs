using System.Net;
using System.Text;
using HermesDesktop.Core.Auth;
using HermesDesktop.Core.Rest;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class SessionMutationTests
{
    private sealed class StubHttpHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var index = Requests.Count;
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return responder(request, index);
        }
    }

    private static AuthSessionTicket CreateTicket() => new("test-ticket", new CookieContainer(), [], null, "test-session-token");

    [Fact]
    public async Task DeleteSessionAsync_UsesMutationSnapshotAndRetriesOnConflict()
    {
        var snapshotCalls = 0;
        var deleteCalls = 0;
        var handler = new StubHttpHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/mutation-snapshot"))
            {
                snapshotCalls++;
                var rev = snapshotCalls == 1 ? 3 : 4;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"runtime_revision\":{rev},\"runtime_generation\":2}}", Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Delete)
            {
                deleteCalls++;
                if (deleteCalls == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = new StringContent("{\"detail\":\"revision_conflict\"}", Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        using var client = new HermesRestClient("http://localhost:9119", CreateTicket(), messageHandler: handler);
        await client.DeleteSessionAsync("sess-123", "mybot");

        Assert.Equal(2, snapshotCalls);
        Assert.Equal(2, deleteCalls);
        Assert.Contains("profile=mybot", handler.Requests[^1].PathAndQuery);
        Assert.Contains("expected_revision=4", handler.Requests[^1].PathAndQuery);
        Assert.Contains("expected_generation=2", handler.Requests[^1].PathAndQuery);
        Assert.Contains("request_id=", handler.Requests[^1].PathAndQuery);
    }

    [Fact]
    public async Task DeleteSessionAsync_FallsBackCleanlyWhenSnapshotRouteMissing()
    {
        var handler = new StubHttpHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/mutation-snapshot"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"detail\":\"Not Found\"}", Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        using var client = new HermesRestClient("http://localhost:9119", CreateTicket(), messageHandler: handler);
        await client.DeleteSessionAsync("sess-legacy");
        await client.DeleteSessionAsync("sess-legacy-2");

        // Second delete should remember snapshot route is missing and skip the extra GET.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/api/sessions/sess-legacy-2", handler.Requests[2].PathAndQuery);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
    }

    [Fact]
    public async Task DeleteSessionAsync_OmitsDefaultProfileQueryParameter()
    {
        var handler = new StubHttpHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/mutation-snapshot"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"detail\":\"Not Found\"}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        using var client = new HermesRestClient("http://localhost:9119", CreateTicket(), messageHandler: handler);
        await client.DeleteSessionAsync("sess-default-1", "default");

        Assert.DoesNotContain("profile=", handler.Requests[0].PathAndQuery);
        Assert.Equal("/api/sessions/sess-default-1", handler.Requests[1].PathAndQuery);
    }
}

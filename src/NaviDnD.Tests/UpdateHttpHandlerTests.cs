using System.Net;
using System.Net.Http.Headers;
using NaviDnD.Installer;

namespace NaviDnD.Tests;

public class UpdateHttpHandlerTests
{
    private sealed class Server(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(respond(++Calls));
    }

    [Fact]
    public async Task RetriesAfterServerDelayAndReturnsPayload()
    {
        var server = new Server(n => n == 1 ? Limited(TimeSpan.FromSeconds(42))
            : new(HttpStatusCode.OK) { Content = new StringContent("payload") });
        var waits = new List<TimeSpan>();
        using var client = new HttpClient(new UpdateHttpHandler(server, (wait, _) => { waits.Add(wait); return Task.CompletedTask; }));
        Assert.Equal("payload", await client.GetStringAsync("https://example.test/update"));
        Assert.Equal([TimeSpan.FromSeconds(42)], waits);
        Assert.Equal(2, server.Calls);
    }

    [Fact]
    public async Task PersistentLimitHasBoundedRetries()
    {
        var server = new Server(_ => Limited(null));
        var waits = new List<TimeSpan>();
        using var client = new HttpClient(new UpdateHttpHandler(server, (wait, _) => { waits.Add(wait); return Task.CompletedTask; }));
        using var response = await client.GetAsync("https://example.test/update");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(4, server.Calls);
        Assert.Equal(new[] { 30d, 60d, 120d }, waits.Select(w => w.TotalSeconds));
    }

    [Fact]
    public async Task CancellationStopsWaitingWithoutAnotherRequest()
    {
        var server = new Server(_ => Limited(null));
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new UpdateHttpHandler(server, (_, token) =>
        {
            cancellation.Cancel();
            return Task.Delay(Timeout.Infinite, token);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/update", cancellation.Token));
        Assert.Equal(1, server.Calls);
    }

    [Fact]
    public async Task DoesNotRetryBeforeLongServerDeadline()
    {
        var server = new Server(_ => Limited(TimeSpan.FromHours(1)));
        using var client = new HttpClient(new UpdateHttpHandler(server, (_, _) => throw new Exception("Unexpected retry")));
        using var response = await client.GetAsync("https://example.test/update");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, server.Calls);
    }

    private static HttpResponseMessage Limited(TimeSpan? wait)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (wait is { } value) response.Headers.RetryAfter = new RetryConditionHeaderValue(value);
        return response;
    }
}

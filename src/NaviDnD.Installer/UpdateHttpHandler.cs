using System.Net;

namespace NaviDnD.Installer;

// Shared by the game (Windows/macOS) and the standalone installer.
internal sealed class UpdateHttpHandler : DelegatingHandler
{
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    internal UpdateHttpHandler(HttpMessageHandler? inner = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : base(inner ?? new HttpClientHandler()) => this.delay = delay ?? Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            var response = await base.SendAsync(request, token);
            if (request.Method != HttpMethod.Get || response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 3)
                return response;

            var retry = response.Headers.RetryAfter;
            TimeSpan wait = retry?.Delta ?? (retry?.Date is { } date
                ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(30 * (1 << attempt)));
            // Do not retry earlier than the server permits, or hold an update indefinitely.
            if (wait > TimeSpan.FromMinutes(5)) return response;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            response.Dispose();
            await delay(wait, token);
        }
    }
}

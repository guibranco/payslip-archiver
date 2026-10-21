using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;

namespace PayslipArchiver.Infrastructure.Monitoring;

/// <summary>
/// Healthchecks.io-style pings: <c>/start</c> when a cycle begins, the bare URL on success, <c>/fail</c>
/// on failure (with the reason as the body so it shows up in the check's log). Never throws.
/// </summary>
public sealed partial class HealthchecksHeartbeat : IHeartbeat
{
    public const string HttpClientName = "healthchecks";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _pingUrl;
    private readonly ILogger<HealthchecksHeartbeat> _logger;

    public HealthchecksHeartbeat(IHttpClientFactory httpClientFactory, IOptions<HeartbeatOptions> options, ILogger<HealthchecksHeartbeat> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.PingUrl);

        _httpClientFactory = httpClientFactory;
        _pingUrl = options.Value.PingUrl.TrimEnd('/');
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => PingAsync("/start", body: null, cancellationToken);

    public Task SuccessAsync(string summary, CancellationToken cancellationToken) => PingAsync(string.Empty, summary, cancellationToken);

    public Task FailureAsync(string reason, CancellationToken cancellationToken) => PingAsync("/fail", reason, cancellationToken);

    private async Task PingAsync(string suffix, string? body, CancellationToken cancellationToken)
    {
        var url = _pingUrl + suffix;
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new StringContent(body ?? string.Empty);
            using var response = await client.PostAsync(new Uri(url), content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedStatus(url, (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            LogPingFailed(ex, url);
        }
    }

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning, Message = "Heartbeat ping to {Url} returned HTTP {StatusCode}")]
    private partial void LogUnexpectedStatus(string url, int statusCode);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Heartbeat ping to {Url} failed")]
    private partial void LogPingFailed(Exception exception, string url);
}

/// <summary>Used when no ping URL is configured.</summary>
public sealed class NoOpHeartbeat : IHeartbeat
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SuccessAsync(string summary, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FailureAsync(string reason, CancellationToken cancellationToken) => Task.CompletedTask;
}
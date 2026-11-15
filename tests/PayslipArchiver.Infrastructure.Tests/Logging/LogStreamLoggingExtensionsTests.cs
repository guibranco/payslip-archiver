using System.Collections.Concurrent;
using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PayslipArchiver.Infrastructure.Logging;

namespace PayslipArchiver.Infrastructure.Tests.Logging;

public sealed partial class LogStreamLoggingExtensionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"LogStream:{v.Key}", v.Value)))
            .Build();

    private static IConfiguration Complete(string server = "https://logs.example.com") => Config(
        ("Server", server), ("ApiKey", "payslip-archiver"), ("ApiToken", "secret-token"), ("AppId", "vinhedo3"));

    [Fact]
    public async Task ShipsEntries_WithTheClientCredentials_AndFlushesOnDispose()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddLogStream(Complete(), handler));

        await using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<ILogger<LogStreamLoggingExtensionsTests>>()
                .ArchivedPayslips(3);
        }

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://logs.example.com/api/logs", request.Uri);
        Assert.Equal("payslip-archiver", request.ApiKey);
        Assert.Equal("secret-token", request.ApiToken);
        Assert.Contains("Archived 3 payslips", request.Body, StringComparison.Ordinal);
        Assert.Contains("vinhedo3", request.Body, StringComparison.Ordinal);
        Assert.Contains(typeof(LogStreamLoggingExtensionsTests).FullName!, request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RespectsTheMinimumLevel()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(Complete())
            .AddInMemoryCollection([new("LogStream:MinimumLevel", "Warning")])
            .Build();
        services.AddLogging(logging => logging.AddLogStream(configuration, handler));

        await using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<ILogger<LogStreamLoggingExtensionsTests>>().NotShipped();
        }

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null, "key", "token", true, false)]
    [InlineData("https://logs.example.com", null, "token", true, false)]
    [InlineData("https://logs.example.com", "key", " ", true, false)]
    [InlineData("https://logs.example.com", "key", "token", false, false)]
    [InlineData("https://logs.example.com", "key", "token", true, true)]
    public void IsConfigured_OnlyWithServerKeyTokenAndEnabled(string? server, string? key, string? token, bool enabled, bool expected)
    {
        var options = new LogStreamOptions { Server = server, ApiKey = key, ApiToken = token, Enabled = enabled };

        Assert.Equal(expected, options.IsConfigured);
    }

    [Fact]
    public void DoesNothing_WhenNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddLogStream(Config(("Server", "https://logs.example.com"))));

        using var provider = services.BuildServiceProvider();
        Assert.DoesNotContain(provider.GetServices<ILoggerProvider>(), p => p.GetType().Name.Contains("Serilog", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://logs.example.com", "https://logs.example.com/")]
    [InlineData("https://logs.example.com/api/logs/", "https://logs.example.com/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void ServerUri_AcceptsTheBaseOrTheFullEndpoint(string server, string expected) =>
        Assert.Equal(expected, new LogStreamOptions { Server = server }.GetServerUri().ToString());

    [Theory]
    [InlineData("http://logs.example.com")]
    [InlineData("not a url")]
    public void ServerUri_RefusesPlainHttpOffLoopback(string server) =>
        Assert.Throws<InvalidOperationException>(() => new LogStreamOptions { Server = server }.GetServerUri());

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? ApiKey, string? ApiToken, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new RecordedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("X-Api-Key", out var key) ? key.Single() : null,
                request.Headers.TryGetValues("X-Api-Token", out var token) ? token.Single() : null,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}

internal static partial class LogStreamTestLogs
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Archived {Count} payslips")]
    public static partial void ArchivedPayslips(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "not shipped")]
    public static partial void NotShipped(this ILogger logger);
}
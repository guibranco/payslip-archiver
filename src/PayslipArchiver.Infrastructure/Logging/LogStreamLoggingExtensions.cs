using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Extensions.Logging;

namespace PayslipArchiver.Infrastructure.Logging;

public static class LogStreamLoggingExtensions
{
    /// <summary>
    /// When the <c>LogStream</c> section is complete, adds a logging provider that ships every log entry to
    /// LogStream in background batches (over UDP by default), next to the existing console provider.
    /// Otherwise does nothing.
    /// </summary>
    /// <remarks>
    /// A slow or unreachable server never blocks the archive cycle: up to 10,000 entries wait in memory.
    /// Over UDP delivery is fire-and-forget; over HTTP (and the UDP fallback for oversized entries) failing
    /// batches are retried for 5 minutes and then dropped. Disposing the host flushes what is left.
    /// </remarks>
    public static ILoggingBuilder AddLogStream(this ILoggingBuilder logging, IConfiguration configuration) =>
        logging.AddLogStream(configuration, messageHandler: null);

    internal static ILoggingBuilder AddLogStream(this ILoggingBuilder logging, IConfiguration configuration, HttpMessageHandler? messageHandler)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(LogStreamOptions.Section).Get<LogStreamOptions>() ?? new LogStreamOptions();
        if (!options.IsConfigured)
        {
            return logging;
        }

        var serverUri = options.GetServerUri();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose() // Microsoft.Extensions.Logging's Logging:LogLevel rules filter first
            .WriteTo.LogStream(
                sink =>
                {
                    sink.ServerUrl = serverUri;
                    sink.ApiKey = options.ApiKey!.Trim();
                    sink.ApiToken = options.ApiToken!.Trim();
                    sink.AppId = string.IsNullOrWhiteSpace(options.AppId) ? Environment.MachineName : options.AppId.Trim();
                    sink.RequestTimeout = TimeSpan.FromSeconds(10);
                    sink.MessageHandler = messageHandler;
                    sink.Transport = options.Transport;
                    sink.UdpPort = options.UdpPort;
                    sink.UdpHost = string.IsNullOrWhiteSpace(options.UdpHost) ? "127.0.0.1" : options.UdpHost.Trim();
                },
                batching =>
                {
                    batching.QueueLimit = 10_000;
                    batching.RetryTimeLimit = TimeSpan.FromMinutes(5);
                },
                restrictedToMinimumLevel: options.MinimumLevel)
            .CreateLogger();

        // Registered through a factory, not AddProvider(instance): the container only disposes what it created,
        // and disposing the provider is what flushes the last batch when the host stops.
        logging.Services.AddSingleton<ILoggerProvider>(_ => new SerilogLoggerProvider(logger, dispose: true));
        return logging;
    }
}
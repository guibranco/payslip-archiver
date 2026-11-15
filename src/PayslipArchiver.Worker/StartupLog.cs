using PayslipArchiver.Infrastructure.Logging;

namespace PayslipArchiver.Worker;

internal static partial class StartupLog
{
    /// <summary>One line at startup saying whether logs are also going to LogStream, and if not, why.</summary>
    public static void LogStreamState(ILogger logger, IConfiguration configuration)
    {
        var options = configuration.GetSection(LogStreamOptions.Section).Get<LogStreamOptions>() ?? new LogStreamOptions();
        var state = options.IsConfigured ? "enabled"
            : !options.Enabled ? "disabled (LogStream:Enabled=false)"
            : "not configured (LogStream:Server, ApiKey and ApiToken are all needed)";
        LogLogStreamState(logger, state);
    }

    [LoggerMessage(EventId = 5100, Level = LogLevel.Information, Message = "LogStream shipping is {LogStreamState}")]
    private static partial void LogLogStreamState(ILogger logger, string logStreamState);
}
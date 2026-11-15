using Serilog.Events;

namespace PayslipArchiver.Infrastructure.Logging;

/// <summary>
/// Bound from the <c>LogStream</c> section: ships logs to a
/// <see href="https://github.com/guibranco/logstream-server">logstream-server</see> with the per-client
/// credentials (<c>X-Api-Key</c> + <c>X-Api-Token</c>). Console / journald logging is unaffected.
/// </summary>
public sealed class LogStreamOptions
{
    public const string Section = "LogStream";

    /// <summary>Kill switch: <c>false</c> stops all LogStream traffic, e.g. while the server is down.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base URL of the server, e.g. <c>https://logs.example.com</c>; <c>/api/logs</c> is appended.</summary>
    public string? Server { get; set; }

    /// <summary>The client's key (its app_key), sent as <c>X-Api-Key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The client's token, sent as <c>X-Api-Token</c>. Keep it in a Grimoire secret.</summary>
    public string? ApiToken { get; set; }

    /// <summary>Instance identifier stored as <c>app_id</c>. Defaults to the host name.</summary>
    public string? AppId { get; set; }

    /// <summary>Lowest level shipped. <c>Logging:LogLevel</c> still filters first.</summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>Shipping is on only when enabled and the server, key and token are all set.</summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Server)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiToken);

    /// <summary>
    /// The server's base address. Accepts a URL with or without the <c>/api/logs</c> path (the sink
    /// appends it). The token travels on every request, so plain HTTP is only tolerated on loopback.
    /// </summary>
    /// <exception cref="InvalidOperationException">The URL is not absolute or not https.</exception>
    public Uri GetServerUri()
    {
        var server = (Server ?? string.Empty).Trim().TrimEnd('/');
        if (server.EndsWith("/api/logs", StringComparison.OrdinalIgnoreCase))
        {
            server = server[..^"/api/logs".Length];
        }

        if (!Uri.TryCreate(server + "/", UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"LogStream:Server '{Server}' is not an absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw new InvalidOperationException($"LogStream:Server must use https (got '{uri.Scheme}://{uri.Authority}').");
        }

        return uri;
    }
}
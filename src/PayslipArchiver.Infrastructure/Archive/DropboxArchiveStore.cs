using Dropbox.Api;
using Dropbox.Api.Files;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;

namespace PayslipArchiver.Infrastructure.Archive;

/// <summary>Uploads documents with the official Dropbox SDK, refreshing the access token as needed.</summary>
public sealed partial class DropboxArchiveStore : IArchiveStore, IDisposable
{
    private readonly DropboxOptions _options;
    private readonly ILogger<DropboxArchiveStore> _logger;
    private readonly Lazy<DropboxClient> _client;

    public DropboxArchiveStore(IOptions<DropboxOptions> options, IHttpClientFactory httpClientFactory, ILogger<DropboxArchiveStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _options = options.Value;
        _logger = logger;
        _client = new Lazy<DropboxClient>(() => CreateClient(_options, httpClientFactory));
    }

    public async Task<string> StoreAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var fullPath = CombinePaths(_options.RootPath, path);

        using var body = new MemoryStream(content.ToArray(), writable: false);
        var arg = new UploadArg(fullPath, WriteMode.Add.Instance, autorename: true, mute: _options.MuteNotifications);

        // The SDK does not take a CancellationToken; the caller's token still cancels the await.
        var metadata = await _client.Value.Files.UploadAsync(arg, body).WaitAsync(cancellationToken).ConfigureAwait(false);

        var storedAt = metadata.PathDisplay ?? fullPath;
        if (!string.Equals(storedAt, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            LogRenamed(fullPath, storedAt);
        }

        return storedAt;
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    internal static string CombinePaths(string root, string path)
    {
        var trimmedRoot = (root ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        var trimmedPath = (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        var combined = trimmedRoot.Length == 0 ? "/" + trimmedPath : trimmedRoot + "/" + trimmedPath;
        return combined.StartsWith('/') ? combined : "/" + combined;
    }

    private static DropboxClient CreateClient(DropboxOptions options, IHttpClientFactory httpClientFactory)
    {
        var config = new DropboxClientConfig("payslip-archiver")
        {
            HttpClient = httpClientFactory.CreateClient(nameof(DropboxArchiveStore)),
        };

        return string.IsNullOrWhiteSpace(options.AppSecret)
            ? new DropboxClient(options.RefreshToken, options.AppKey, config)
            : new DropboxClient(options.RefreshToken, options.AppKey, options.AppSecret, config);
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Dropbox renamed {RequestedPath} to {StoredPath} to avoid a conflict")]
    private partial void LogRenamed(string requestedPath, string storedPath);
}
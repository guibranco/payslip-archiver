using System.Globalization;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Application.Options;
using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Infrastructure.Mail;

/// <summary>IMAP source backed by MailKit. One connection per session; nothing is kept between cycles.</summary>
public sealed partial class ImapMailboxSource : IMailboxSource
{
    private readonly MailboxOptions _options;
    private readonly MessageFilterOptions _filter;
    private readonly TimeProvider _clock;
    private readonly ILogger<ImapMailboxSource> _logger;

    public ImapMailboxSource(IOptions<MailboxOptions> options, IOptions<MessageFilterOptions> filter, TimeProvider clock, ILogger<ImapMailboxSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(filter);

        _options = options.Value;
        _filter = filter.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IMailboxSession> OpenAsync(CancellationToken cancellationToken)
    {
        var client = new ImapClient { Timeout = (int)_options.Timeout.TotalMilliseconds };
        try
        {
            // StartTls (not StartTlsWhenAvailable): if the server cannot upgrade the connection, fail rather than
            // send the mailbox password in the clear to whoever stripped the STARTTLS capability.
            var security = _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken).ConfigureAwait(false);
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken).ConfigureAwait(false);

            var folder = string.IsNullOrWhiteSpace(_options.Folder) || _options.Folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await client.GetFolderAsync(_options.Folder, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);

            return new Session(client, folder, _options, _filter, _clock, _logger);
        }
        catch
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false);
            }

            client.Dispose();
            throw;
        }
    }

    private sealed partial class Session : IMailboxSession
    {
        private readonly ImapClient _client;
        private readonly IMailFolder _folder;
        private readonly MailboxOptions _options;
        private readonly MessageFilterOptions _filter;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;
        private readonly Dictionary<string, UniqueId> _uids = new(StringComparer.Ordinal);
        private IMailFolder? _processedFolder;

        public Session(ImapClient client, IMailFolder folder, MailboxOptions options, MessageFilterOptions filter, TimeProvider clock, ILogger logger)
        {
            _client = client;
            _folder = folder;
            _options = options;
            _filter = filter;
            _clock = clock;
            _logger = logger;
        }

        public async Task<IReadOnlyList<string>> ListMessageKeysAsync(CancellationToken cancellationToken)
        {
            var query = _options.OnlyUnseen ? SearchQuery.NotSeen : SearchQuery.All;
            if (!string.IsNullOrWhiteSpace(_filter.FromContains))
            {
                query = query.And(SearchQuery.FromContains(_filter.FromContains));
            }

            if (!string.IsNullOrWhiteSpace(_filter.SubjectContains))
            {
                query = query.And(SearchQuery.SubjectContains(_filter.SubjectContains));
            }

            var uids = await _folder.SearchAsync(query, cancellationToken).ConfigureAwait(false);

            _uids.Clear();
            var keys = new List<string>(uids.Count);
            foreach (var uid in uids.OrderBy(u => u.Id))
            {
                var key = string.Create(CultureInfo.InvariantCulture, $"imap:{uid.Validity}:{uid.Id}");
                _uids[key] = uid;
                keys.Add(key);
            }

            return keys;
        }

        public async Task<IncomingMessage> FetchAsync(string key, CancellationToken cancellationToken)
        {
            var uid = Uid(key);
            var message = await _folder.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);

            // The server's delivery timestamp stands in for a missing/unparseable Date header.
            var summaries = await _folder.FetchAsync([uid], MessageSummaryItems.InternalDate, cancellationToken).ConfigureAwait(false);
            var fallback = summaries.FirstOrDefault()?.InternalDate ?? _clock.GetUtcNow();

            return MimeMessageMapper.ToIncomingMessage(key, message, fallback);
        }

        public async Task MarkHandledAsync(string key, CancellationToken cancellationToken)
        {
            var uid = Uid(key);
            await _folder.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(_options.ProcessedFolder))
            {
                return;
            }

            _processedFolder ??= await GetOrCreateProcessedFolderAsync(cancellationToken).ConfigureAwait(false);
            await _folder.MoveToAsync(uid, _processedFolder, cancellationToken).ConfigureAwait(false);
            LogMoved(key, _processedFolder.FullName);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_client.IsConnected)
                {
                    await _client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _client.Dispose();
            }
        }

        private UniqueId Uid(string key)
            => _uids.TryGetValue(key, out var uid)
                ? uid
                : throw new KeyNotFoundException($"Message '{key}' was not returned by the last listing.");

        private async Task<IMailFolder> GetOrCreateProcessedFolderAsync(CancellationToken cancellationToken)
        {
            var name = _options.ProcessedFolder!;
            var root = _client.GetFolder(_client.PersonalNamespaces[0]);

            try
            {
                return await root.GetSubfolderAsync(name, cancellationToken).ConfigureAwait(false);
            }
            catch (FolderNotFoundException)
            {
                LogCreatingFolder(name);
                return await root.CreateAsync(name, isMessageFolder: true, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException($"The server did not return the created folder '{name}'.");
            }
        }

        [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "Moved {Key} to {Folder}")]
        private partial void LogMoved(string key, string folder);

        [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Creating IMAP folder {Folder}")]
        private partial void LogCreatingFolder(string folder);
    }
}
using MailKit.Net.Pop3;
using MailKit.Security;

using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Infrastructure.Mail;

/// <summary>
/// POP3 source backed by MailKit. POP3 has no flags and no server-side search, so every message is a
/// candidate each cycle; the ledger is what keeps the pipeline idempotent. Requires UIDL support.
/// </summary>
public sealed class Pop3MailboxSource : IMailboxSource
{
    private readonly MailboxOptions _options;
    private readonly TimeProvider _clock;

    public Pop3MailboxSource(IOptions<MailboxOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _clock = clock;
    }

    public async Task<IMailboxSession> OpenAsync(CancellationToken cancellationToken)
    {
        var client = new Pop3Client { Timeout = (int)_options.Timeout.TotalMilliseconds };
        try
        {
            // StartTls (not StartTlsWhenAvailable): if the server cannot upgrade the connection, fail rather than
            // send the mailbox password in the clear to whoever stripped the STARTTLS capability.
            var security = _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken).ConfigureAwait(false);
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken).ConfigureAwait(false);

            if (!client.Capabilities.HasFlag(Pop3Capabilities.UIDL))
            {
                throw new NotSupportedException(
                    "The POP3 server does not support UIDL, so messages cannot be identified reliably between cycles. Use IMAP instead.");
            }

            return new Session(client, _options, _clock);
        }
        catch
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(false, CancellationToken.None).ConfigureAwait(false);
            }

            client.Dispose();
            throw;
        }
    }

    private sealed class Session : IMailboxSession
    {
        private readonly Pop3Client _client;
        private readonly MailboxOptions _options;
        private readonly TimeProvider _clock;
        private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

        public Session(Pop3Client client, MailboxOptions options, TimeProvider clock)
        {
            _client = client;
            _options = options;
            _clock = clock;
        }

        public async Task<IReadOnlyList<string>> ListMessageKeysAsync(CancellationToken cancellationToken)
        {
            var uids = await _client.GetMessageUidsAsync(cancellationToken).ConfigureAwait(false);

            _indexes.Clear();
            var keys = new List<string>(uids.Count);
            for (var index = 0; index < uids.Count; index++)
            {
                var key = "pop3:" + uids[index];
                _indexes[key] = index;
                keys.Add(key);
            }

            return keys;
        }

        public async Task<IncomingMessage> FetchAsync(string key, CancellationToken cancellationToken)
        {
            var message = await _client.GetMessageAsync(Index(key), cancellationToken).ConfigureAwait(false);

            // POP3 has no server-side delivery timestamp, so "now" stands in for a missing Date header.
            return MimeMessageMapper.ToIncomingMessage(key, message, _clock.GetUtcNow());
        }

        public async Task MarkHandledAsync(string key, CancellationToken cancellationToken)
        {
            if (_options.DeleteAfterProcessing)
            {
                // Takes effect when the session disconnects with quit=true.
                await _client.DeleteMessageAsync(Index(key), cancellationToken).ConfigureAwait(false);
            }
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

        private int Index(string key)
            => _indexes.TryGetValue(key, out var index)
                ? index
                : throw new KeyNotFoundException($"Message '{key}' was not returned by the last listing.");
    }
}
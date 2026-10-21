using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Application.Abstractions;

/// <summary>
/// Opens a session against the payroll mailbox. One session is opened per archive cycle and
/// disposed at the end of it, so the underlying connection never outlives a cycle.
/// </summary>
public interface IMailboxSource
{
    Task<IMailboxSession> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A connected mailbox. Listing is cheap (keys only); fetching downloads the message with attachments.
/// </summary>
public interface IMailboxSession : IAsyncDisposable
{
    /// <summary>
    /// Keys of the candidate messages, oldest first. Sources that can filter server-side (IMAP) do so;
    /// others return everything they have and let the pipeline filter after fetching.
    /// </summary>
    Task<IReadOnlyList<string>> ListMessageKeysAsync(CancellationToken cancellationToken);

    /// <summary>Downloads one message, including attachment content.</summary>
    Task<IncomingMessage> FetchAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the mailbox the message has been dealt with (IMAP: flag as seen and move to the processed
    /// folder; POP3: optionally delete). Called only after the ledger has recorded the outcome.
    /// </summary>
    Task MarkHandledAsync(string key, CancellationToken cancellationToken);
}
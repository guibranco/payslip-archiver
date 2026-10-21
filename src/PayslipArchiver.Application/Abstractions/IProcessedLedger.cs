using PayslipArchiver.Domain.Archiving;
using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Application.Abstractions;

public enum ProcessingStatus
{
    /// <summary>At least one attachment was decrypted and stored.</summary>
    Archived,

    /// <summary>The message was looked at and deliberately left alone (filter mismatch, no PDF, duplicate content).</summary>
    Ignored,

    /// <summary>The last attempt failed; the message will be retried until <c>Pipeline:MaxAttempts</c>.</summary>
    Failed,
}

/// <summary>What the ledger remembers about a message key.</summary>
public sealed record LedgerEntry(
    string Key,
    ProcessingStatus Status,
    int Attempts,
    string? LastError,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Durable record of what has been processed. This is what makes the pipeline idempotent across
/// restarts and across mailboxes that cannot flag messages (POP3).
/// </summary>
public interface IProcessedLedger
{
    Task<LedgerEntry?> FindAsync(string key, CancellationToken cancellationToken);

    /// <summary>True when a document with this content hash has already been archived (from any message).</summary>
    Task<bool> IsContentArchivedAsync(string sha256, CancellationToken cancellationToken);

    Task RecordArchivedAsync(IncomingMessage message, IReadOnlyList<ArchivedDocument> documents, CancellationToken cancellationToken);

    Task RecordIgnoredAsync(IncomingMessage message, string reason, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt (incrementing the attempt counter) for a key that may not have been fetched yet.</summary>
    Task RecordFailureAsync(string key, string errorMessage, CancellationToken cancellationToken);
}
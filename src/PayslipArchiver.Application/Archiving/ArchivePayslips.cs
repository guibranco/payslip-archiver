using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Application.Options;
using PayslipArchiver.Domain.Archiving;
using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Application.Archiving;

/// <summary>
/// The whole job, once: open the mailbox, walk the candidate messages, decrypt each payslip, store it,
/// record it in the ledger and mark the e-mail as handled.
/// </summary>
/// <remarks>
/// Ordering matters for idempotency: the ledger is written <em>before</em> the mailbox is told the
/// message is handled. If marking fails the next cycle sees the ledger entry and skips the upload;
/// if the upload fails nothing is recorded as archived and the message is retried.
/// </remarks>
public sealed partial class ArchivePayslips
{
    private readonly IMailboxSource _mailbox;
    private readonly IPdfDecryptor _decryptor;
    private readonly IArchiveStore _archive;
    private readonly IProcessedLedger _ledger;
    private readonly MessageFilter _filter;
    private readonly PipelineOptions _pipeline;
    private readonly ArchiveNamingOptions _naming;
    private readonly PdfOptions _pdf;
    private readonly ILogger<ArchivePayslips> _logger;

    public ArchivePayslips(
        IMailboxSource mailbox,
        IPdfDecryptor decryptor,
        IArchiveStore archive,
        IProcessedLedger ledger,
        MessageFilter filter,
        IOptions<PipelineOptions> pipelineOptions,
        IOptions<ArchiveNamingOptions> namingOptions,
        IOptions<PdfOptions> pdfOptions,
        ILogger<ArchivePayslips> logger)
    {
        ArgumentNullException.ThrowIfNull(pipelineOptions);
        ArgumentNullException.ThrowIfNull(namingOptions);
        ArgumentNullException.ThrowIfNull(pdfOptions);

        _mailbox = mailbox;
        _decryptor = decryptor;
        _archive = archive;
        _ledger = ledger;
        _filter = filter;
        _pipeline = pipelineOptions.Value;
        _naming = namingOptions.Value;
        _pdf = pdfOptions.Value;
        _logger = logger;
    }

    public async Task<CycleReport> RunAsync(CancellationToken cancellationToken)
    {
        int archived = 0, documents = 0, ignored = 0, skipped = 0, failed = 0;

        await using var session = await _mailbox.OpenAsync(cancellationToken).ConfigureAwait(false);
        var keys = await session.ListMessageKeysAsync(cancellationToken).ConfigureAwait(false);
        LogListed(keys.Count);

        // The cap counts messages actually attempted, not keys listed: POP3 lists the whole mailbox and IMAP keeps
        // ignored (never flagged) messages in the unseen set, so a cap on the listing would let old, already-known
        // keys starve new payslips forever.
        var attempted = 0;
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempted >= _pipeline.MaxMessagesPerCycle)
            {
                LogCapReached(_pipeline.MaxMessagesPerCycle, keys.Count);
                break;
            }

            var entry = await _ledger.FindAsync(key, cancellationToken).ConfigureAwait(false);
            if (entry is { Status: ProcessingStatus.Archived or ProcessingStatus.Ignored })
            {
                skipped++;
                continue;
            }

            if (entry is { Attempts: var attempts } && attempts >= _pipeline.MaxAttempts)
            {
                LogRetryBudgetExhausted(key, attempts, entry.LastError);
                skipped++;
                continue;
            }

            attempted++;
            try
            {
                var outcome = await ProcessAsync(session, key, cancellationToken).ConfigureAwait(false);
                documents += outcome.Documents;
                if (outcome.Documents > 0)
                {
                    archived++;
                }
                else
                {
                    ignored++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // One bad message must not stop the others; the failure is recorded and retried.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failed++;
                LogMessageFailed(ex, key);
                await _ledger.RecordFailureAsync(key, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        var report = new CycleReport(keys.Count, archived, documents, ignored, skipped, failed);
        LogCycleFinished(report);
        return report;
    }

    private async Task<(int Documents, string Outcome)> ProcessAsync(IMailboxSession session, string key, CancellationToken cancellationToken)
    {
        var message = await session.FetchAsync(key, cancellationToken).ConfigureAwait(false);

        if (!_filter.Matches(message))
        {
            LogIgnored(key, message.From, message.Subject, "does not match the payslip filter");
            await _ledger.RecordIgnoredAsync(message, "filter mismatch", cancellationToken).ConfigureAwait(false);
            return (0, "ignored");
        }

        var stored = new List<ArchivedDocument>();
        foreach (var attachment in _filter.MatchingAttachments(message))
        {
            var decrypted = _decryptor.Decrypt(attachment.Content, _pdf.Password);
            var sha256 = ContentFingerprint.Sha256Hex(decrypted);

            if (await _ledger.IsContentArchivedAsync(sha256, cancellationToken).ConfigureAwait(false))
            {
                LogDuplicateContent(key, attachment.FileName, sha256);
                continue;
            }

            var resolved = PayPeriodResolver.Resolve(attachment.FileName, message.Subject, message.ReceivedAt, _naming.PeriodOffsetMonths);
            var path = ArchivePathTemplate.Render(_naming.PathTemplate, resolved.Period, attachment.FileName);
            var finalPath = await _archive.StoreAsync(path, decrypted, cancellationToken).ConfigureAwait(false);

            stored.Add(new ArchivedDocument(finalPath, sha256, decrypted.Length, resolved.Period, attachment.FileName));
            LogArchived(key, attachment.FileName, finalPath, resolved.Period, resolved.Source, decrypted.Length);
        }

        if (stored.Count == 0)
        {
            await _ledger.RecordIgnoredAsync(message, "all attachments were already archived", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _ledger.RecordArchivedAsync(message, stored, cancellationToken).ConfigureAwait(false);
        }

        await session.MarkHandledAsync(key, cancellationToken).ConfigureAwait(false);
        return (stored.Count, stored.Count == 0 ? "duplicate" : "archived");
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Mailbox listed {Count} candidate message(s)")]
    private partial void LogListed(int count);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Archived {FileName} from {Key} to {Path} (period {Period} from {PeriodSource}, {Bytes} bytes)")]
    private partial void LogArchived(string key, string fileName, string path, PayPeriod period, PayPeriodSource periodSource, int bytes);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Ignored {Key} from {From} with subject '{Subject}': {Reason}")]
    private partial void LogIgnored(string key, string from, string subject, string reason);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Skipped {FileName} from {Key}: identical content already archived ({Sha256})")]
    private partial void LogDuplicateContent(string key, string fileName, string sha256);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Giving up on {Key} after {Attempts} failed attempt(s); last error: {LastError}")]
    private partial void LogRetryBudgetExhausted(string key, int attempts, string? lastError);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Processing {Key} failed; it will be retried next cycle")]
    private partial void LogMessageFailed(Exception exception, string key);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Cycle finished: {Report}")]
    private partial void LogCycleFinished(CycleReport report);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Reached the cap of {Cap} message(s) for this cycle out of {Listed} listed; the rest will be picked up next cycle")]
    private partial void LogCapReached(int cap, int listed);
}
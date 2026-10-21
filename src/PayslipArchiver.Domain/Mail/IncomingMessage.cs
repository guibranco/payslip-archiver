namespace PayslipArchiver.Domain.Mail;

/// <summary>
/// An e-mail message pulled from the payroll mailbox.
/// </summary>
/// <param name="Key">
/// Stable, source-specific identifier used for idempotency (e.g. <c>imap:{uidvalidity}:{uid}</c> or <c>pop3:{uidl}</c>).
/// </param>
/// <param name="MessageId">The RFC 5322 <c>Message-ID</c> header, when present.</param>
/// <param name="From">The sender as rendered by the mail library, e.g. <c>"Payroll" &lt;payroll@example.com&gt;</c>.</param>
/// <param name="Subject">The subject line (empty when missing).</param>
/// <param name="ReceivedAt">The message date.</param>
/// <param name="Attachments">All attachments, already decoded.</param>
public sealed record IncomingMessage(
    string Key,
    string? MessageId,
    string From,
    string Subject,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<MailAttachment> Attachments)
{
    public IEnumerable<MailAttachment> PdfAttachments => Attachments.Where(a => a.IsPdf);
}
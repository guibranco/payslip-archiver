using MimeKit;

using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Infrastructure.Mail;

/// <summary>Translates a MimeKit message into the domain's <see cref="IncomingMessage"/>.</summary>
internal static class MimeMessageMapper
{
    private const string PdfMimeType = "application/pdf";

    public static IncomingMessage ToIncomingMessage(string key, MimeMessage message)
    {
        var attachments = new List<MailAttachment>();

        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            var isPdf = part.ContentType.IsMimeType("application", "pdf")
                        || (part.FileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ?? false);

            // Payroll systems sometimes send the slip as an inline part rather than a proper attachment,
            // so PDFs are taken wherever they sit; everything else must be a declared attachment.
            if (!part.IsAttachment && !isPdf)
            {
                continue;
            }

            // A part can legitimately have no content (e.g. an empty attachment); nothing to archive then.
            if (part.Content is null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            part.Content.DecodeTo(buffer);

            var fileName = string.IsNullOrWhiteSpace(part.FileName)
                ? (isPdf ? "attachment.pdf" : "attachment.bin")
                : part.FileName;
            var contentType = isPdf ? PdfMimeType : part.ContentType.MimeType;

            attachments.Add(new MailAttachment(fileName, contentType, buffer.ToArray()));
        }

        return new IncomingMessage(
            key,
            string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId,
            message.From.ToString(),
            message.Subject ?? string.Empty,
            message.Date,
            attachments);
    }
}
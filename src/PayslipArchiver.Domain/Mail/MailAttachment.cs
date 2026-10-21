namespace PayslipArchiver.Domain.Mail;

/// <summary>
/// An attachment pulled off an e-mail, fully materialised in memory (payslips are a few hundred KB at most).
/// </summary>
public sealed record MailAttachment(string FileName, string ContentType, ReadOnlyMemory<byte> Content)
{
    private const string PdfMimeType = "application/pdf";

    /// <summary>
    /// True when the attachment looks like a PDF by name, declared MIME type or the <c>%PDF</c> magic header.
    /// Payroll providers are not consistent about any single one of these, so we accept any.
    /// </summary>
    public bool IsPdf =>
        FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
        || ContentType.Equals(PdfMimeType, StringComparison.OrdinalIgnoreCase)
        || Content.Span.StartsWith("%PDF"u8);

    public long SizeBytes => Content.Length;
}
using MimeKit;

using PayslipArchiver.Infrastructure.Mail;

namespace PayslipArchiver.Infrastructure.Tests.Mail;

public sealed class MimeMessageMapperTests
{
    private static readonly DateTimeOffset Fallback = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MapsHeaders_AndDecodesPdfAttachments()
    {
        var pdfBytes = "%PDF-1.4 fake"u8.ToArray();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Payroll", "payroll@example.com"));
        message.To.Add(new MailboxAddress("Me", "me@example.com"));
        message.Subject = "Your payslip for October 2026";
        message.MessageId = "abc@example.com";
        message.Date = new DateTimeOffset(2026, 10, 25, 8, 30, 0, TimeSpan.Zero);

        var builder = new BodyBuilder { TextBody = "See attached." };
        builder.Attachments.Add("Payslip_2026-10.pdf", pdfBytes, ContentType.Parse("application/pdf"));
        builder.Attachments.Add("notes.txt", "not a pdf"u8.ToArray(), ContentType.Parse("text/plain"));
        message.Body = builder.ToMessageBody();

        var incoming = MimeMessageMapper.ToIncomingMessage("imap:1:42", message, Fallback);

        Assert.Equal("imap:1:42", incoming.Key);
        Assert.Equal("abc@example.com", incoming.MessageId);
        Assert.Contains("payroll@example.com", incoming.From, StringComparison.Ordinal);
        Assert.Equal("Your payslip for October 2026", incoming.Subject);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 8, 30, 0, TimeSpan.Zero), incoming.ReceivedAt);

        Assert.Equal(2, incoming.Attachments.Count);
        var pdf = Assert.Single(incoming.PdfAttachments);
        Assert.Equal("Payslip_2026-10.pdf", pdf.FileName);
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal(pdfBytes, pdf.Content.ToArray());
    }

    [Fact]
    public void TreatsAnInlinePdfPart_AsAnAttachment()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Payroll", "payroll@example.com"));
        message.Subject = "Payslip";

        var pdfPart = new MimePart("application", "pdf")
        {
            Content = new MimeContent(new MemoryStream("%PDF-1.4 inline"u8.ToArray())),
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
            ContentTransferEncoding = ContentEncoding.Base64,
        };
        message.Body = new Multipart("mixed") { new TextPart("plain") { Text = "hi" }, pdfPart };

        var incoming = MimeMessageMapper.ToIncomingMessage("pop3:x", message, Fallback);

        var pdf = Assert.Single(incoming.Attachments);
        Assert.True(pdf.IsPdf);
        Assert.Equal("attachment.pdf", pdf.FileName);
    }

    [Fact]
    public void CopesWithAMessageThatHasNoSubjectOrMessageId()
    {
        var message = new MimeMessage { Body = new TextPart("plain") { Text = "hi" } };
        message.From.Add(new MailboxAddress("Payroll", "payroll@example.com"));

        var incoming = MimeMessageMapper.ToIncomingMessage("pop3:y", message, Fallback);

        Assert.Equal(string.Empty, incoming.Subject);
        Assert.Empty(incoming.Attachments);
    }

    [Fact]
    public void UsesTheFallbackDate_WhenTheDateHeaderIsMissing()
    {
        // Parsed from the wire with no Date header: MimeKit reports DateTimeOffset.MinValue.
        var raw = "From: payroll@example.com\r\nSubject: Payslip\r\n\r\nno date header\r\n";
        var message = MimeMessage.Load(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(raw)));
        Assert.Equal(DateTimeOffset.MinValue, message.Date);

        var incoming = MimeMessageMapper.ToIncomingMessage("pop3:z", message, Fallback);

        Assert.Equal(Fallback, incoming.ReceivedAt);
    }

    [Fact]
    public void KeepsTheDateHeader_WhenPresent()
    {
        var message = new MimeMessage { Body = new TextPart("plain") { Text = "hi" } };
        message.From.Add(new MailboxAddress("Payroll", "payroll@example.com"));
        message.Date = new DateTimeOffset(2026, 10, 25, 8, 30, 0, TimeSpan.Zero);

        var incoming = MimeMessageMapper.ToIncomingMessage("pop3:w", message, Fallback);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 8, 30, 0, TimeSpan.Zero), incoming.ReceivedAt);
    }
}
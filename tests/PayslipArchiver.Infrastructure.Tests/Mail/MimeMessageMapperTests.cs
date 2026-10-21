using MimeKit;

using PayslipArchiver.Infrastructure.Mail;

namespace PayslipArchiver.Infrastructure.Tests.Mail;

public sealed class MimeMessageMapperTests
{
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

        var incoming = MimeMessageMapper.ToIncomingMessage("imap:1:42", message);

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

        var incoming = MimeMessageMapper.ToIncomingMessage("pop3:x", message);

        var pdf = Assert.Single(incoming.Attachments);
        Assert.True(pdf.IsPdf);
        Assert.Equal("attachment.pdf", pdf.FileName);
    }

    [Fact]
    public void CopesWithAMessageThatHasNoSubjectOrMessageId()
    {
        var message = new MimeMessage { Body = new TextPart("plain") { Text = "hi" } };
        message.From.Add(new MailboxAddress("Payroll", "payroll@example.com"));

        var incoming = MimeMessageMapper.ToIncomingMessage("pop3:y", message);

        Assert.Equal(string.Empty, incoming.Subject);
        Assert.Empty(incoming.Attachments);
    }
}
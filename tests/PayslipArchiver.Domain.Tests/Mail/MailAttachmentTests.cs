using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Domain.Tests.Mail;

public sealed class MailAttachmentTests
{
    [Theory]
    [InlineData("Payslip.pdf", "application/octet-stream", "not really")]
    [InlineData("PAYSLIP.PDF", "application/octet-stream", "not really")]
    [InlineData("blob", "application/pdf", "not really")]
    [InlineData("blob", "application/octet-stream", "%PDF-1.7 ...")]
    public void IsPdf_AcceptsAnyOfName_MimeType_OrMagicHeader(string fileName, string contentType, string content)
    {
        var attachment = new MailAttachment(fileName, contentType, System.Text.Encoding.ASCII.GetBytes(content));

        Assert.True(attachment.IsPdf);
    }

    [Fact]
    public void IsPdf_RejectsEverythingElse()
    {
        var attachment = new MailAttachment("notes.txt", "text/plain", "hello"u8.ToArray());

        Assert.False(attachment.IsPdf);
    }
}
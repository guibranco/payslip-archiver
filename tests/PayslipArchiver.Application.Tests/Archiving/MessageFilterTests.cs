using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Archiving;
using PayslipArchiver.Application.Options;
using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Application.Tests.Archiving;

public sealed class MessageFilterTests
{
    private static readonly MailAttachment Pdf = new("Payslip_2026-10.pdf", "application/pdf", "%PDF"u8.ToArray());
    private static readonly MailAttachment Text = new("notes.txt", "text/plain", "x"u8.ToArray());

    [Fact]
    public void Matches_WhenSenderSubjectAndAttachmentAllLineUp()
    {
        var filter = Create(new MessageFilterOptions { FromContains = "@payroll.example.com", SubjectContains = "payslip", AttachmentPattern = "Payslip_*.pdf" });

        Assert.True(filter.Matches(Message("\"HR\" <noreply@payroll.example.com>", "Your Payslip", Pdf, Text)));
    }

    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        var filter = Create(new MessageFilterOptions { FromContains = "PAYROLL", SubjectContains = "PAYSLIP", AttachmentPattern = "payslip_*.PDF" });

        Assert.True(filter.Matches(Message("payroll@example.com", "payslip", Pdf)));
    }

    [Fact]
    public void Rejects_WrongSender()
    {
        var filter = Create(new MessageFilterOptions { FromContains = "@payroll.example.com" });

        Assert.False(filter.Matches(Message("phisher@evil.example", "Your Payslip", Pdf)));
    }

    [Fact]
    public void Rejects_WrongSubject()
    {
        var filter = Create(new MessageFilterOptions { SubjectContains = "payslip" });

        Assert.False(filter.Matches(Message("payroll@example.com", "Office party", Pdf)));
    }

    [Fact]
    public void Rejects_MessagesWithoutAMatchingPdf()
    {
        var filter = Create(new MessageFilterOptions { AttachmentPattern = "Payslip_*.pdf" });

        Assert.False(filter.Matches(Message("payroll@example.com", "Payslip", Text)));
        Assert.False(filter.Matches(Message("payroll@example.com", "Payslip", new MailAttachment("P60.pdf", "application/pdf", "%PDF"u8.ToArray()))));
    }

    [Fact]
    public void MatchingAttachments_ReturnsOnlyPdfsThatMatchThePattern()
    {
        var filter = Create(new MessageFilterOptions { AttachmentPattern = "*.pdf" });
        var other = new MailAttachment("P60.pdf", "application/pdf", "%PDF"u8.ToArray());

        var matches = filter.MatchingAttachments(Message("payroll@example.com", "Payslip", Pdf, Text, other)).ToList();

        Assert.Equal([Pdf, other], matches);
    }

    [Fact]
    public void EmptyOptions_AcceptAnyMessageWithAPdf()
    {
        var filter = Create(new MessageFilterOptions { FromContains = " ", SubjectContains = null, AttachmentPattern = "" });

        Assert.True(filter.Matches(Message("anyone@example.com", "anything", Pdf)));
        Assert.False(filter.Matches(Message("anyone@example.com", "anything", Text)));
    }

    private static MessageFilter Create(MessageFilterOptions options) => new(Microsoft.Extensions.Options.Options.Create(options));

    private static IncomingMessage Message(string from, string subject, params MailAttachment[] attachments)
        => new("imap:1:1", null, from, subject, DateTimeOffset.UtcNow, attachments);
}
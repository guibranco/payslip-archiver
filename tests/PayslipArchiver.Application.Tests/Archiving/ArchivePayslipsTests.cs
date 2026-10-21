using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute.ExceptionExtensions;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Application.Archiving;
using PayslipArchiver.Application.Options;
using PayslipArchiver.Domain.Archiving;
using PayslipArchiver.Domain.Mail;

using Opts = Microsoft.Extensions.Options.Options;

namespace PayslipArchiver.Application.Tests.Archiving;

public sealed class ArchivePayslipsTests
{
    private const string Password = "pw";
    private static readonly byte[] Encrypted = "encrypted"u8.ToArray();
    private static readonly byte[] Decrypted = "%PDF decrypted"u8.ToArray();
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    private readonly IMailboxSource _mailbox = Substitute.For<IMailboxSource>();
    private readonly IMailboxSession _session = Substitute.For<IMailboxSession>();
    private readonly IPdfDecryptor _decryptor = Substitute.For<IPdfDecryptor>();
    private readonly IArchiveStore _archive = Substitute.For<IArchiveStore>();
    private readonly IProcessedLedger _ledger = Substitute.For<IProcessedLedger>();
    private readonly PipelineOptions _pipeline = new();
    private readonly ArchiveNamingOptions _naming = new();

    public ArchivePayslipsTests()
    {
        _mailbox.OpenAsync(Arg.Any<CancellationToken>()).Returns(_session);
        _decryptor.Decrypt(Arg.Any<ReadOnlyMemory<byte>>(), Password).Returns(Decrypted);
        _archive.StoreAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(0));
        _ledger.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((LedgerEntry?)null);
        _ledger.IsContentArchivedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
    }

    [Fact]
    public async Task ArchivesTheDecryptedPdf_RecordsIt_ThenMarksTheMessageHandled()
    {
        var message = Message("imap:1:1", "Payslip_2026-10.pdf");
        ListAndFetch(message);

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(new CycleReport(Listed: 1, Archived: 1, Documents: 1, Ignored: 0, Skipped: 0, Failed: 0), report);

        _decryptor.Received(1).Decrypt(Arg.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(Encrypted)), Password);
        await _archive.Received(1).StoreAsync(
            "/2026/2026-10 Payslip.pdf",
            Arg.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(Decrypted)),
            Arg.Any<CancellationToken>());

        Received.InOrder(async () =>
        {
            await _ledger.RecordArchivedAsync(
                message,
                Arg.Is<IReadOnlyList<ArchivedDocument>>(docs =>
                    docs.Count == 1
                    && docs[0].Path == "/2026/2026-10 Payslip.pdf"
                    && docs[0].Sha256 == ContentFingerprint.Sha256Hex(Decrypted)
                    && docs[0].Period == new PayPeriod(2026, 10)
                    && docs[0].SourceFileName == "Payslip_2026-10.pdf"),
                Arg.Any<CancellationToken>());
            await _session.MarkHandledAsync("imap:1:1", Arg.Any<CancellationToken>());
        });

        await _session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task UsesTheFinalPathReturnedByTheStore()
    {
        ListAndFetch(Message("imap:1:1", "Payslip_2026-10.pdf"));
        _archive.StoreAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns("/2026/2026-10 Payslip (1).pdf");

        await Sut().RunAsync(CancellationToken.None);

        await _ledger.Received(1).RecordArchivedAsync(
            Arg.Any<IncomingMessage>(),
            Arg.Is<IReadOnlyList<ArchivedDocument>>(docs => docs[0].Path == "/2026/2026-10 Payslip (1).pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsMessagesTheLedgerAlreadyKnows_WithoutFetchingThem()
    {
        _session.ListMessageKeysAsync(Arg.Any<CancellationToken>()).Returns(["imap:1:1", "imap:1:2"]);
        _ledger.FindAsync("imap:1:1", Arg.Any<CancellationToken>()).Returns(Entry("imap:1:1", ProcessingStatus.Archived));
        _ledger.FindAsync("imap:1:2", Arg.Any<CancellationToken>()).Returns(Entry("imap:1:2", ProcessingStatus.Ignored));

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(2, report.Skipped);
        Assert.Equal(0, report.Archived);
        await _session.DidNotReceive().FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivesUpOnAMessage_OnceTheRetryBudgetIsSpent()
    {
        _pipeline.MaxAttempts = 3;
        _session.ListMessageKeysAsync(Arg.Any<CancellationToken>()).Returns(["imap:1:1"]);
        _ledger.FindAsync("imap:1:1", Arg.Any<CancellationToken>()).Returns(Entry("imap:1:1", ProcessingStatus.Failed, attempts: 3, lastError: "bad password"));

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Skipped);
        await _session.DidNotReceive().FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetriesAFailedMessage_WhileAttemptsRemain()
    {
        _pipeline.MaxAttempts = 3;
        var message = Message("imap:1:1", "Payslip_2026-10.pdf");
        ListAndFetch(message);
        _ledger.FindAsync("imap:1:1", Arg.Any<CancellationToken>()).Returns(Entry("imap:1:1", ProcessingStatus.Failed, attempts: 2, lastError: "transient"));

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Archived);
    }

    [Fact]
    public async Task IgnoresMessagesThatDoNotMatchTheFilter_AndLeavesThemUnhandled()
    {
        var message = Message("pop3:abc", ["newsletter.pdf"], from: "marketing@example.com");
        ListAndFetch(message);

        var report = await Sut(new MessageFilterOptions { FromContains = "payroll@" }).RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Ignored);
        await _ledger.Received(1).RecordIgnoredAsync(message, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _session.DidNotReceive().MarkHandledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _archive.DidNotReceiveWithAnyArgs().StoreAsync(default!, default, default);
    }

    [Fact]
    public async Task SkipsDuplicateContent_ButStillMarksTheMessageHandled()
    {
        var message = Message("imap:1:7", "Payslip_2026-10.pdf");
        ListAndFetch(message);
        _ledger.IsContentArchivedAsync(ContentFingerprint.Sha256Hex(Decrypted), Arg.Any<CancellationToken>()).Returns(true);

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(new CycleReport(1, Archived: 0, Documents: 0, Ignored: 1, Skipped: 0, Failed: 0), report);
        await _archive.DidNotReceiveWithAnyArgs().StoreAsync(default!, default, default);
        await _ledger.Received(1).RecordIgnoredAsync(message, Arg.Is<string>(r => r.Contains("already archived", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await _session.Received(1).MarkHandledAsync("imap:1:7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArchivesEveryMatchingAttachment_InOneMessage()
    {
        var message = Message("imap:1:1", "Payslip_2026-09.pdf", "Payslip_2026-10.pdf");
        ListAndFetch(message);

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(2, report.Documents);
        Assert.Equal(1, report.Archived);
        await _archive.Received(1).StoreAsync("/2026/2026-09 Payslip.pdf", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        await _archive.Received(1).StoreAsync("/2026/2026-10 Payslip.pdf", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailure_IsRecorded_AndDoesNotStopTheOtherMessages()
    {
        var broken = Message("imap:1:1", "Payslip_2026-09.pdf");
        var fine = Message("imap:1:2", "Payslip_2026-10.pdf");
        _session.ListMessageKeysAsync(Arg.Any<CancellationToken>()).Returns(["imap:1:1", "imap:1:2"]);
        _session.FetchAsync("imap:1:1", Arg.Any<CancellationToken>()).Returns(broken);
        _session.FetchAsync("imap:1:2", Arg.Any<CancellationToken>()).Returns(fine);
        _decryptor.Decrypt(Arg.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(Encrypted)), Password)
            .Returns(
                _ => throw new PdfDecryptionException("wrong password"),
                _ => Decrypted);

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(new CycleReport(2, Archived: 1, Documents: 1, Ignored: 0, Skipped: 0, Failed: 1), report);
        await _ledger.Received(1).RecordFailureAsync("imap:1:1", "wrong password", Arg.Any<CancellationToken>());
        await _session.DidNotReceive().MarkHandledAsync("imap:1:1", Arg.Any<CancellationToken>());
        await _session.Received(1).MarkHandledAsync("imap:1:2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreFailure_LeavesNothingRecordedAsArchived()
    {
        ListAndFetch(Message("imap:1:1", "Payslip_2026-10.pdf"));
        _archive.StoreAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("dropbox down"));

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Failed);
        await _ledger.DidNotReceiveWithAnyArgs().RecordArchivedAsync(default!, default!, default);
        await _ledger.Received(1).RecordFailureAsync("imap:1:1", "dropbox down", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HonoursMaxMessagesPerCycle()
    {
        _pipeline.MaxMessagesPerCycle = 2;
        var keys = new[] { "imap:1:1", "imap:1:2", "imap:1:3" };
        _session.ListMessageKeysAsync(Arg.Any<CancellationToken>()).Returns(keys);
        foreach (var key in keys)
        {
            _session.FetchAsync(key, Arg.Any<CancellationToken>()).Returns(Message(key, $"Payslip_{key[^1]}.pdf"));
        }

        var report = await Sut().RunAsync(CancellationToken.None);

        Assert.Equal(3, report.Listed);
        Assert.Equal(2, report.Archived);
        await _session.DidNotReceive().FetchAsync("imap:1:3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_PropagatesInsteadOfBeingRecordedAsAFailure()
    {
        using var cts = new CancellationTokenSource();
        ListAndFetch(Message("imap:1:1", "Payslip_2026-10.pdf"));
        _archive.StoreAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sut().RunAsync(cts.Token));

        await _ledger.DidNotReceiveWithAnyArgs().RecordFailureAsync(default!, default!, default);
    }

    private ArchivePayslips Sut(MessageFilterOptions? filter = null)
        => new(
            _mailbox,
            _decryptor,
            _archive,
            _ledger,
            new MessageFilter(Opts.Create(filter ?? new MessageFilterOptions())),
            Opts.Create(_pipeline),
            Opts.Create(_naming),
            Opts.Create(new PdfOptions { Password = Password }),
            NullLogger<ArchivePayslips>.Instance);

    private void ListAndFetch(IncomingMessage message)
    {
        _session.ListMessageKeysAsync(Arg.Any<CancellationToken>()).Returns([message.Key]);
        _session.FetchAsync(message.Key, Arg.Any<CancellationToken>()).Returns(message);
    }

    private static IncomingMessage Message(string key, params string[] fileNames) => Message(key, fileNames, "payroll@example.com");

    private static IncomingMessage Message(string key, string[] fileNames, string from)
        => new(
            key,
            null,
            from,
            "Your payslip",
            ReceivedAt,
            fileNames.Select(name => new MailAttachment(name, "application/pdf", Encrypted)).ToArray());

    private static LedgerEntry Entry(string key, ProcessingStatus status, int attempts = 0, string? lastError = null)
        => new(key, status, attempts, lastError, ReceivedAt);
}
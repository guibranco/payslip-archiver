using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Domain.Archiving;
using PayslipArchiver.Domain.Mail;
using PayslipArchiver.Infrastructure.Ledger;

namespace PayslipArchiver.Infrastructure.Tests.Ledger;

public sealed class SqliteProcessedLedgerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("payslip-ledger-tests-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 21, 14, 0, 0, TimeSpan.Zero));
    private readonly SqliteProcessedLedger _sut;

    public SqliteProcessedLedgerTests()
    {
        _sut = new SqliteProcessedLedger(Options.Create(new LedgerOptions { Path = Path.Combine(_directory, "nested", "ledger.db") }), _clock);
    }

    [Fact]
    public async Task Find_ReturnsNull_ForAnUnknownKey()
    {
        Assert.Null(await _sut.FindAsync("imap:1:999", CancellationToken.None));
    }

    [Fact]
    public async Task RecordArchived_IsVisibleThroughFind_AndContentLookup()
    {
        var message = Message("imap:1:1");
        var document = new ArchivedDocument("/2026/2026-10 Payslip.pdf", "abc123", 1234, new PayPeriod(2026, 10), "payslip.pdf");

        await _sut.RecordArchivedAsync(message, [document], CancellationToken.None);

        var entry = await _sut.FindAsync("imap:1:1", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(ProcessingStatus.Archived, entry.Status);
        Assert.Equal(0, entry.Attempts);
        Assert.Equal(_clock.GetUtcNow(), entry.UpdatedAt);
        Assert.True(await _sut.IsContentArchivedAsync("abc123", CancellationToken.None));
        Assert.False(await _sut.IsContentArchivedAsync("other", CancellationToken.None));
    }

    [Fact]
    public async Task RecordFailure_IncrementsAttempts_AndKeepsTheLastError()
    {
        await _sut.RecordFailureAsync("imap:1:2", "first", CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(30));
        await _sut.RecordFailureAsync("imap:1:2", "second", CancellationToken.None);

        var entry = await _sut.FindAsync("imap:1:2", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(ProcessingStatus.Failed, entry.Status);
        Assert.Equal(2, entry.Attempts);
        Assert.Equal("second", entry.LastError);
        Assert.Equal(_clock.GetUtcNow(), entry.UpdatedAt);
    }

    [Fact]
    public async Task RecordArchived_AfterFailures_KeepsTheAttemptCount()
    {
        await _sut.RecordFailureAsync("imap:1:3", "boom", CancellationToken.None);

        await _sut.RecordArchivedAsync(Message("imap:1:3"), [], CancellationToken.None);

        var entry = await _sut.FindAsync("imap:1:3", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(ProcessingStatus.Archived, entry.Status);
        Assert.Equal(1, entry.Attempts);
        Assert.Null(entry.LastError);
    }

    [Fact]
    public async Task RecordIgnored_StoresTheStatus()
    {
        await _sut.RecordIgnoredAsync(Message("pop3:abc"), "filter mismatch", CancellationToken.None);

        var entry = await _sut.FindAsync("pop3:abc", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(ProcessingStatus.Ignored, entry.Status);
    }

    [Fact]
    public async Task TheDatabase_SurvivesANewLedgerInstance()
    {
        await _sut.RecordIgnoredAsync(Message("imap:1:4"), "x", CancellationToken.None);

        using var reopened = new SqliteProcessedLedger(Options.Create(new LedgerOptions { Path = Path.Combine(_directory, "nested", "ledger.db") }), _clock);

        Assert.NotNull(await reopened.FindAsync("imap:1:4", CancellationToken.None));
    }

    public void Dispose()
    {
        _sut.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private static IncomingMessage Message(string key)
        => new(key, "<id@example.com>", "payroll@example.com", "Payslip", new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), []);
}
using PayslipArchiver.Domain.Archiving;

namespace PayslipArchiver.Domain.Tests.Archiving;

public sealed class PayPeriodResolverTests
{
    private static readonly DateTimeOffset Received = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Payslip_2026-10.pdf", 2026, 10)]
    [InlineData("Payslip_2026_10.pdf", 2026, 10)]
    [InlineData("Payslip 2026.10.pdf", 2026, 10)]
    [InlineData("payslip202610.pdf", 2026, 10)]
    [InlineData("payslip20261015.pdf", 2026, 10)]
    [InlineData("payslip-2026-10-15.pdf", 2026, 10)]
    [InlineData("Payslip 10-2026.pdf", 2026, 10)]
    [InlineData("Payslip 10/2026.pdf", 2026, 10)]
    [InlineData("Payslip 15-10-2026.pdf", 2026, 10)]
    [InlineData("Payslip_Oct2026.pdf", 2026, 10)]
    [InlineData("Payslip October 2026.pdf", 2026, 10)]
    [InlineData("Payslip OCTOBER-2026.pdf", 2026, 10)]
    [InlineData("2026 October payslip.pdf", 2026, 10)]
    [InlineData("Payslip_Sept2026.pdf", 2026, 9)]
    [InlineData("Payslip_Jan2027.pdf", 2027, 1)]
    public void TryParse_RecognisesCommonFileNameShapes(string text, int year, int month)
    {
        Assert.True(PayPeriodResolver.TryParse(text, out var period));
        Assert.Equal(new PayPeriod(year, month), period);
    }

    [Theory]
    [InlineData("Payslip.pdf")]
    [InlineData("Payslip_123456789.pdf")]
    [InlineData("Payslip 13-2026.pdf")]
    [InlineData("Payslip 2026-13.pdf")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsTextWithoutAPeriod(string? text)
    {
        Assert.False(PayPeriodResolver.TryParse(text, out _));
    }

    [Fact]
    public void Resolve_PrefersTheFileName_OverTheSubject()
    {
        var resolved = PayPeriodResolver.Resolve("Payslip_2026-09.pdf", "Payslip October 2026", Received);

        Assert.Equal(new PayPeriod(2026, 9), resolved.Period);
        Assert.Equal(PayPeriodSource.FileName, resolved.Source);
    }

    [Fact]
    public void Resolve_FallsBackToTheSubject()
    {
        var resolved = PayPeriodResolver.Resolve("Payslip.pdf", "Payslip October 2026", Received);

        Assert.Equal(new PayPeriod(2026, 10), resolved.Period);
        Assert.Equal(PayPeriodSource.Subject, resolved.Source);
    }

    [Fact]
    public void Resolve_FallsBackToTheReceivedDate_WithOffset()
    {
        var resolved = PayPeriodResolver.Resolve("Payslip.pdf", "Your payslip is attached", Received, fallbackOffsetMonths: -1);

        Assert.Equal(new PayPeriod(2026, 10), resolved.Period);
        Assert.Equal(PayPeriodSource.ReceivedDate, resolved.Source);
    }
}
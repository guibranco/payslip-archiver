using PayslipArchiver.Domain.Archiving;

namespace PayslipArchiver.Domain.Tests.Archiving;

public sealed class ArchivePathTemplateTests
{
    private static readonly PayPeriod October = new(2026, 10);

    [Fact]
    public void Default_FilesByYearAndPeriod()
    {
        Assert.Equal("/2026/2026-10 Payslip.pdf", ArchivePathTemplate.Render(ArchivePathTemplate.Default, October, "whatever.pdf"));
    }

    [Theory]
    [InlineData("/{year}/{month}/{original}{ext}", "/2026/10/Payslip_Oct2026.pdf")]
    [InlineData("payslips/{period}.pdf", "/payslips/2026-10.pdf")]
    [InlineData("/{YEAR}/{Period}-{Original}.PDF", "/2026/2026-10-Payslip_Oct2026.PDF")]
    [InlineData("//{year}//{period}.pdf", "/2026/2026-10.pdf")]
    [InlineData("\\{year}\\{period}.pdf", "/2026/2026-10.pdf")]
    public void Render_SubstitutesTokens_AndNormalisesSlashes(string template, string expected)
    {
        Assert.Equal(expected, ArchivePathTemplate.Render(template, October, "Payslip_Oct2026.pdf"));
    }

    [Fact]
    public void Render_LeavesUnknownTokensAlone()
    {
        Assert.Equal("/2026/{nope}.pdf", ArchivePathTemplate.Render("/{year}/{nope}.pdf", October, "a.pdf"));
    }

    [Fact]
    public void Render_SanitisesTheOriginalName()
    {
        var path = ArchivePathTemplate.Render("/{original}{ext}", October, "pay:slip*2026?.pdf");

        Assert.Equal("/pay_slip_2026_.pdf", path);
    }

    [Theory]
    [InlineData("...pdf")]
    [InlineData("..pdf")]
    [InlineData(". .pdf")]
    public void Render_NeverEmitsATraversalSegment_FromAnAttackerChosenName(string attachmentName)
    {
        var path = ArchivePathTemplate.Render("/{original}/{period}.pdf", October, attachmentName);

        Assert.Equal("/_/2026-10.pdf", path);
        Assert.DoesNotContain("..", path, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DefaultsTheExtensionToPdf()
    {
        Assert.Equal("/2026-10.pdf", ArchivePathTemplate.Render("/{period}{ext}", October, "attachment"));
    }

    [Fact]
    public void Render_RejectsAnEmptyTemplate()
    {
        Assert.Throws<ArgumentException>(() => ArchivePathTemplate.Render(" ", October, "a.pdf"));
    }
}

public sealed class PayPeriodTests
{
    [Fact]
    public void ToString_IsSortable()
    {
        Assert.Equal("2026-03", new PayPeriod(2026, 3).ToString());
    }

    [Theory]
    [InlineData(2026, 12, 1, 2027, 1)]
    [InlineData(2026, 1, -1, 2025, 12)]
    [InlineData(2026, 6, 0, 2026, 6)]
    public void AddMonths_RollsOverYears(int year, int month, int delta, int expectedYear, int expectedMonth)
    {
        Assert.Equal(new PayPeriod(expectedYear, expectedMonth), new PayPeriod(year, month).AddMonths(delta));
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1899, 1)]
    public void Constructor_RejectsOutOfRangeValues(int year, int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PayPeriod(year, month));
    }

    [Fact]
    public void Periods_CompareChronologically()
    {
        Assert.True(new PayPeriod(2026, 9) < new PayPeriod(2026, 10));
        Assert.True(new PayPeriod(2025, 12) < new PayPeriod(2026, 1));
    }
}
using System.Globalization;
using System.Text.RegularExpressions;

namespace PayslipArchiver.Domain.Archiving;

/// <summary>Where a resolved <see cref="PayPeriod"/> came from, for logging.</summary>
public enum PayPeriodSource
{
    FileName,
    Subject,
    ReceivedDate,
}

public sealed record ResolvedPayPeriod(PayPeriod Period, PayPeriodSource Source);

/// <summary>
/// Works out which month a payslip is for. Tries to read a period out of the attachment file name,
/// then the subject, and finally falls back to the message date (shifted by an optional offset, for
/// providers that send the slip for the previous month at the start of the next one).
/// </summary>
public static partial class PayPeriodResolver
{
    private static readonly string[] MonthNames =
    [
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
    ];

    public static ResolvedPayPeriod Resolve(
        string? attachmentFileName,
        string? subject,
        DateTimeOffset receivedAt,
        int fallbackOffsetMonths = 0)
    {
        if (TryParse(attachmentFileName, out var fromFileName))
        {
            return new ResolvedPayPeriod(fromFileName, PayPeriodSource.FileName);
        }

        if (TryParse(subject, out var fromSubject))
        {
            return new ResolvedPayPeriod(fromSubject, PayPeriodSource.Subject);
        }

        return new ResolvedPayPeriod(PayPeriod.From(receivedAt).AddMonths(fallbackOffsetMonths), PayPeriodSource.ReceivedDate);
    }

    /// <summary>
    /// Recognises <c>2026-10</c>, <c>2026_10</c>, <c>202610</c>, <c>10-2026</c>, <c>10/2026</c>,
    /// <c>Oct 2026</c>, <c>October2026</c>, <c>2026 October</c> and similar, in any surrounding text.
    /// </summary>
    public static bool TryParse(string? text, out PayPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var yearMonth = YearMonthPattern().Match(text);
        if (yearMonth.Success && TryCreate(yearMonth.Groups["y"].Value, yearMonth.Groups["m"].Value, out period))
        {
            return true;
        }

        var monthYear = MonthYearPattern().Match(text);
        if (monthYear.Success && TryCreate(monthYear.Groups["y"].Value, monthYear.Groups["m"].Value, out period))
        {
            return true;
        }

        var named = NamedMonthPattern().Match(text);
        if (named.Success)
        {
            var monthIndex = MonthIndex(named.Groups["mon"].Value);
            if (monthIndex > 0 && TryCreate(named.Groups["y"].Value, monthIndex.ToString(CultureInfo.InvariantCulture), out period))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreate(string year, string month, out PayPeriod period)
    {
        period = default;
        if (!int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || m is < 1 or > 12)
        {
            return false;
        }

        period = new PayPeriod(y, m);
        return true;
    }

    private static int MonthIndex(string name)
    {
        var lower = name.ToLowerInvariant();
        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (MonthNames[i].StartsWith(lower, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return 0;
    }

    // 2026-10 / 2026_10 / 2026.10 / 2026 10 / 202610, optionally followed by a day (2026-10-15, 20261015).
    // The year must not be glued to other digits.
    [GeneratedRegex(
        @"(?<!\d)(?<y>(?:19|20)\d{2})[-_. ]?(?<m>0[1-9]|1[0-2])(?:[-_. ]?(?:0[1-9]|[12]\d|3[01]))?(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex YearMonthPattern();

    // 10-2026 / 10_2026 / 10/2026 / 10.2026 (a separator is required here to avoid eating day numbers).
    [GeneratedRegex(@"(?<!\d)(?<m>0?[1-9]|1[0-2])[-_./](?<y>(?:19|20)\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex MonthYearPattern();

    // Oct 2026 / October-2026 / October2026 / Payslip_Oct2026 / 2026 October / 2026-Oct
    // (\b would not fire after an underscore, so letter look-arounds are used instead.)
    [GeneratedRegex(
        @"(?<![a-z])(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*[-_. ]*(?<y>(?:19|20)\d{2})(?!\d)|(?<!\d)(?<y>(?:19|20)\d{2})[-_. ]*(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*(?![a-z])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NamedMonthPattern();
}
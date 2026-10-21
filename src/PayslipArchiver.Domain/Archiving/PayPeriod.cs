using System.Globalization;

namespace PayslipArchiver.Domain.Archiving;

/// <summary>
/// The month a payslip belongs to. Rendered as <c>yyyy-MM</c>, which sorts naturally in a folder listing.
/// </summary>
public readonly record struct PayPeriod : IComparable<PayPeriod>
{
    public PayPeriod(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1900);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public static PayPeriod From(DateTimeOffset date) => new(date.Year, date.Month);

    public static PayPeriod From(DateOnly date) => new(date.Year, date.Month);

    public PayPeriod AddMonths(int months)
    {
        var shifted = new DateOnly(Year, Month, 1).AddMonths(months);
        return new PayPeriod(shifted.Year, shifted.Month);
    }

    public int CompareTo(PayPeriod other) => (Year, Month).CompareTo((other.Year, other.Month));

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:0000}-{Month:00}");

    public static bool operator <(PayPeriod left, PayPeriod right) => left.CompareTo(right) < 0;

    public static bool operator <=(PayPeriod left, PayPeriod right) => left.CompareTo(right) <= 0;

    public static bool operator >(PayPeriod left, PayPeriod right) => left.CompareTo(right) > 0;

    public static bool operator >=(PayPeriod left, PayPeriod right) => left.CompareTo(right) >= 0;
}
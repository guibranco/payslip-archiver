using System.ComponentModel.DataAnnotations;

using PayslipArchiver.Domain.Archiving;

namespace PayslipArchiver.Application.Options;

/// <summary>Bound from the <c>Naming</c> configuration section.</summary>
public sealed class ArchiveNamingOptions
{
    public const string Section = "Naming";

    /// <summary>See <see cref="ArchivePathTemplate"/> for the supported tokens.</summary>
    [Required]
    public string PathTemplate { get; set; } = ArchivePathTemplate.Default;

    /// <summary>
    /// Applied only when no period can be read from the file name or subject: the message month is
    /// shifted by this many months (use <c>-1</c> when slips for a month arrive early the next month).
    /// </summary>
    [Range(-12, 12)]
    public int PeriodOffsetMonths { get; set; }
}
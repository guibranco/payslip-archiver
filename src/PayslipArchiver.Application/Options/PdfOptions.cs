using System.ComponentModel.DataAnnotations;

namespace PayslipArchiver.Application.Options;

/// <summary>Bound from the <c>Pdf</c> configuration section.</summary>
public sealed class PdfOptions
{
    public const string Section = "Pdf";

    /// <summary>The password the payroll provider protects the slips with. Keep it out of appsettings.json.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Password { get; set; } = string.Empty;
}
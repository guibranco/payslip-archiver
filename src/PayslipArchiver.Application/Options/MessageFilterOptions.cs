namespace PayslipArchiver.Application.Options;

/// <summary>Bound from the <c>Filter</c> configuration section. Decides which e-mails are payslips.</summary>
public sealed class MessageFilterOptions
{
    public const string Section = "Filter";

    /// <summary>Case-insensitive substring the sender must contain, e.g. <c>@payroll.example.com</c>. Empty = any sender.</summary>
    public string? FromContains { get; set; }

    /// <summary>Case-insensitive substring the subject must contain, e.g. <c>Payslip</c>. Empty = any subject.</summary>
    public string? SubjectContains { get; set; }

    /// <summary>
    /// Glob the attachment file name must match (<c>*</c> and <c>?</c> wildcards, case-insensitive).
    /// Only PDF attachments are ever considered, regardless of this pattern.
    /// </summary>
    public string AttachmentPattern { get; set; } = "*.pdf";
}
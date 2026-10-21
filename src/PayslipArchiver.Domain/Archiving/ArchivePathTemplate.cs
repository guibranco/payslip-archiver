using System.Globalization;
using System.Text.RegularExpressions;

namespace PayslipArchiver.Domain.Archiving;

/// <summary>
/// Renders the destination path of an archived payslip from a small token template.
/// </summary>
/// <remarks>
/// Supported tokens (case-insensitive):
/// <list type="bullet">
///   <item><c>{year}</c> – four-digit year of the pay period</item>
///   <item><c>{month}</c> – two-digit month of the pay period</item>
///   <item><c>{period}</c> – <c>yyyy-MM</c></item>
///   <item><c>{original}</c> – the original attachment file name, sanitised, without extension</item>
///   <item><c>{ext}</c> – the original extension including the dot (defaults to <c>.pdf</c>)</item>
/// </list>
/// Paths are normalised to forward slashes with a single leading slash, so they are valid both for
/// Dropbox and (after stripping the leading slash) for a local directory.
/// </remarks>
public static partial class ArchivePathTemplate
{
    public const string Default = "/{year}/{period} Payslip.pdf";

    public static string Render(string template, PayPeriod period, string originalFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        var original = Path.GetFileNameWithoutExtension(originalFileName ?? string.Empty);
        var extension = Path.GetExtension(originalFileName ?? string.Empty);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".pdf";
        }

        var rendered = TokenPattern().Replace(template, match => match.Groups["token"].Value.ToLowerInvariant() switch
        {
            "year" => period.Year.ToString("0000", CultureInfo.InvariantCulture),
            "month" => period.Month.ToString("00", CultureInfo.InvariantCulture),
            "period" => period.ToString(),
            "original" => Sanitise(original),
            "ext" => Sanitise(extension),
            _ => match.Value,
        });

        return Normalise(rendered);
    }

    /// <summary>Strips characters that are illegal in file names on Windows, Dropbox or Linux.</summary>
    public static string Sanitise(string segment)
    {
        var cleaned = IllegalFileNameCharacters().Replace(segment, "_").Trim();
        return cleaned.Length == 0 ? "_" : cleaned;
    }

    private static string Normalise(string path)
    {
        var slashes = path.Replace('\\', '/');
        var collapsed = RepeatedSlashes().Replace(slashes, "/");
        return collapsed.StartsWith('/') ? collapsed : "/" + collapsed;
    }

    [GeneratedRegex(@"\{(?<token>[a-zA-Z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"[\\/:*?""<>|\p{Cc}]+", RegexOptions.CultureInvariant)]
    private static partial Regex IllegalFileNameCharacters();

    [GeneratedRegex("/{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedSlashes();
}
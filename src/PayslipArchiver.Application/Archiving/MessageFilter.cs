using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Options;
using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Application.Archiving;

/// <summary>
/// Decides whether a message is a payslip e-mail and which of its attachments are the slips.
/// </summary>
public sealed class MessageFilter
{
    private readonly MessageFilterOptions _options;
    private readonly Regex _attachmentPattern;

    public MessageFilter(IOptions<MessageFilterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _attachmentPattern = GlobToRegex(string.IsNullOrWhiteSpace(_options.AttachmentPattern) ? "*" : _options.AttachmentPattern);
    }

    public bool Matches(IncomingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!string.IsNullOrWhiteSpace(_options.FromContains)
            && !message.From.Contains(_options.FromContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_options.SubjectContains)
            && !message.Subject.Contains(_options.SubjectContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchingAttachments(message).Any();
    }

    public IEnumerable<MailAttachment> MatchingAttachments(IncomingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.PdfAttachments.Where(a => _attachmentPattern.IsMatch(a.FileName));
    }

    private static Regex GlobToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
using System.ComponentModel.DataAnnotations;

namespace PayslipArchiver.Infrastructure.Mail;

public enum MailboxProtocol
{
    Imap,
    Pop3,
}

/// <summary>Bound from the <c>Mailbox</c> configuration section.</summary>
public sealed class MailboxOptions
{
    public const string Section = "Mailbox";

    public MailboxProtocol Protocol { get; set; } = MailboxProtocol.Imap;

    [Required]
    public string Host { get; set; } = string.Empty;

    /// <summary>993 for IMAP over TLS, 143 for IMAP+STARTTLS, 995 for POP3 over TLS, 110 for POP3+STARTTLS.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 993;

    /// <summary>
    /// True = TLS on connect (implicit, ports 993/995); false = STARTTLS, which is <em>required</em>: the
    /// connection is refused if the server cannot upgrade it. Plaintext authentication is never used.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>IMAP only: the folder to watch.</summary>
    public string Folder { get; set; } = "INBOX";

    /// <summary>IMAP only: handled messages are moved here (created if missing). Empty = leave them in place.</summary>
    public string? ProcessedFolder { get; set; } = "Processed";

    /// <summary>IMAP only: when true only unread messages are candidates; when false every message in the folder is (the ledger still prevents re-processing).</summary>
    public bool OnlyUnseen { get; set; } = true;

    /// <summary>POP3 only: delete handled messages from the server. Off by default — the ledger already prevents re-processing.</summary>
    public bool DeleteAfterProcessing { get; set; }

    /// <summary>Network timeout for the mail connection.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
using System.ComponentModel.DataAnnotations;

namespace PayslipArchiver.Infrastructure.Archive;

public enum ArchiveProvider
{
    /// <summary>Upload through the Dropbox API (default).</summary>
    Dropbox,

    /// <summary>Write to a local directory — a folder synced by the Dropbox desktop client, or just somewhere to look at the output while developing.</summary>
    FileSystem,
}

/// <summary>Bound from the <c>Archive</c> configuration section.</summary>
public sealed class ArchiveOptions
{
    public const string Section = "Archive";

    public ArchiveProvider Provider { get; set; } = ArchiveProvider.Dropbox;

    /// <summary>Root directory when <see cref="Provider"/> is <see cref="ArchiveProvider.FileSystem"/>.</summary>
    public string? Directory { get; set; }
}

/// <summary>Bound from the <c>Dropbox</c> configuration section.</summary>
public sealed class DropboxOptions
{
    public const string Section = "Dropbox";

    /// <summary>The app key from the Dropbox App Console.</summary>
    [Required]
    public string AppKey { get; set; } = string.Empty;

    /// <summary>Optional: the app secret. Leave empty to use the PKCE flow (recommended for a single-user tool).</summary>
    public string? AppSecret { get; set; }

    /// <summary>Long-lived refresh token obtained once with <c>payslip-archiver authorize-dropbox</c>.</summary>
    [Required]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Folder the archive lives under. With an "App folder" app this is relative to <c>/Apps/&lt;app name&gt;</c>,
    /// so <c>/</c> is usually right; with "Full Dropbox" access use something like <c>/Documents/Payslips</c>.
    /// </summary>
    public string RootPath { get; set; } = "/";

    /// <summary>Suppress the desktop notification Dropbox shows for each uploaded file.</summary>
    public bool MuteNotifications { get; set; } = true;
}
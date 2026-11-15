namespace PayslipArchiver.Infrastructure.Configuration;

/// <summary>
/// Bootstrap settings for loading the rest of the configuration from a
/// <see href="https://github.com/guibranco/grimoire-api">Grimoire</see> server. Bound from the
/// <c>Grimoire</c> section, which therefore has to come from somewhere else (env file, user secrets).
/// </summary>
public sealed class GrimoireOptions
{
    public const string Section = "Grimoire";

    /// <summary>
    /// Configuration keys that are read from Grimoire <em>secrets</em> rather than configuration entries.
    /// The consumer API cannot list secrets, so they are fetched by name; a secret missing in Grimoire
    /// is simply skipped and left to the usual startup validation.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSecretKeys =
    [
        "Mailbox:Password",
        "Pdf:Password",
        "Dropbox:AppSecret",
        "Dropbox:RefreshToken",
        "Heartbeat:PingUrl",
        "LogStream:ApiToken",
    ];

    /// <summary>Root URL of the Grimoire API, e.g. <c>https://api.grimoire.straccini.com</c>. Empty disables Grimoire.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The application's consumer key (<c>grm_…</c>), sent as <c>X-Api-Key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Grimoire environment slug to read.</summary>
    public string Environment { get; set; } = "production";

    /// <summary>Overrides <see cref="DefaultSecretKeys"/> when set.</summary>
    public IReadOnlyList<string>? SecretKeys { get; set; }

    /// <summary>Per-request timeout while loading.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>
    /// Grimoire secret name for a configuration key: <c>Mailbox:Password</c> → <c>Mailbox--Password</c>,
    /// the same convention as the Azure Key Vault configuration provider.
    /// </summary>
    public static string SecretNameFor(string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        return configurationKey.Replace(":", "--", StringComparison.Ordinal);
    }
}
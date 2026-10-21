namespace PayslipArchiver.Infrastructure.Monitoring;

/// <summary>Bound from the <c>Heartbeat</c> configuration section.</summary>
public sealed class HeartbeatOptions
{
    public const string Section = "Heartbeat";

    /// <summary>
    /// Healthchecks.io ping URL for this check, e.g. <c>https://hc-ping.com/&lt;uuid&gt;</c>.
    /// Empty disables the heartbeat. <c>/start</c> and <c>/fail</c> are appended as needed.
    /// </summary>
    public string? PingUrl { get; set; }
}
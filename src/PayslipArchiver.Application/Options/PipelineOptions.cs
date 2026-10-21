using System.ComponentModel.DataAnnotations;

namespace PayslipArchiver.Application.Options;

/// <summary>Bound from the <c>Pipeline</c> configuration section.</summary>
public sealed class PipelineOptions
{
    public const string Section = "Pipeline";

    /// <summary>Upper bound on messages fetched in one cycle, so a backlog is drained gradually.</summary>
    [Range(1, 10_000)]
    public int MaxMessagesPerCycle { get; set; } = 50;

    /// <summary>
    /// After this many failed attempts a message is left alone (and logged) instead of being retried
    /// every cycle forever — e.g. a slip encrypted with a different password.
    /// </summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 5;
}
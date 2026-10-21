namespace PayslipArchiver.Application.Abstractions;

/// <summary>
/// Dead man's switch for the scheduler (Healthchecks.io or similar). Implementations must never throw:
/// a monitoring hiccup is not a reason to fail a cycle.
/// </summary>
public interface IHeartbeat
{
    Task StartAsync(CancellationToken cancellationToken);

    Task SuccessAsync(string summary, CancellationToken cancellationToken);

    Task FailureAsync(string reason, CancellationToken cancellationToken);
}
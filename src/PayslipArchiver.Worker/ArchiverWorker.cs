using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Application.Archiving;

namespace PayslipArchiver.Worker;

/// <summary>
/// Runs <see cref="ArchivePayslips"/> immediately on start and then every <see cref="ScheduleOptions.Interval"/>,
/// wrapping each cycle in heartbeat pings. In <see cref="ScheduleOptions.RunOnce"/> mode it runs once and stops the host.
/// </summary>
public sealed partial class ArchiverWorker : BackgroundService
{
    private readonly ArchivePayslips _archivePayslips;
    private readonly IHeartbeat _heartbeat;
    private readonly ScheduleOptions _schedule;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ArchiverWorker> _logger;

    public ArchiverWorker(
        ArchivePayslips archivePayslips,
        IHeartbeat heartbeat,
        IOptions<ScheduleOptions> schedule,
        IHostApplicationLifetime lifetime,
        ILogger<ArchiverWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        _archivePayslips = archivePayslips;
        _heartbeat = heartbeat;
        _schedule = schedule.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_schedule.RunOnce)
            {
                var succeeded = await RunCycleAsync(stoppingToken).ConfigureAwait(false);
                Environment.ExitCode = succeeded ? 0 : 1;
                _lifetime.StopApplication();
                return;
            }

            LogScheduled(_schedule.Interval);
            using var timer = new PeriodicTimer(_schedule.Interval);
            do
            {
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopping();
        }
    }

    private async Task<bool> RunCycleAsync(CancellationToken cancellationToken)
    {
        LogCycleStarting();
        await _heartbeat.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var report = await _archivePayslips.RunAsync(cancellationToken).ConfigureAwait(false);
            if (report.HasFailures)
            {
                await _heartbeat.FailureAsync(report.ToString(), cancellationToken).ConfigureAwait(false);
                return false;
            }

            await _heartbeat.SuccessAsync(report.ToString(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // The service must survive a bad cycle (mail server down, Dropbox outage) and try again later.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCycleCrashed(ex);
            await _heartbeat.FailureAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Archiver scheduled every {Interval}")]
    private partial void LogScheduled(TimeSpan interval);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Archive cycle starting")]
    private partial void LogCycleStarting();

    [LoggerMessage(EventId = 5002, Level = LogLevel.Error, Message = "Archive cycle crashed; will retry on the next tick")]
    private partial void LogCycleCrashed(Exception exception);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information, Message = "Archiver stopping")]
    private partial void LogStopping();
}
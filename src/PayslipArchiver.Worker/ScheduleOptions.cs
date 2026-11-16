namespace PayslipArchiver.Worker;

/// <summary>Bound from the <c>Schedule</c> configuration section.</summary>
public sealed class ScheduleOptions
{
    public const string Section = "Schedule";

    /// <summary>Time between cycles. Payslips are monthly, so the default of 6 hours is plenty; anything from 15 minutes up works.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Run a single cycle and exit (exit code 1 when any message failed). For cron or systemd timers.</summary>
    public bool RunOnce { get; set; }
}
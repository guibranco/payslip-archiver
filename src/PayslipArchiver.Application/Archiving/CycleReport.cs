namespace PayslipArchiver.Application.Archiving;

/// <summary>Outcome counts of one archive cycle.</summary>
/// <param name="Listed">Candidate messages the mailbox reported.</param>
/// <param name="Archived">Messages with at least one newly stored document.</param>
/// <param name="Documents">Documents stored across all messages.</param>
/// <param name="Ignored">Messages inspected and deliberately skipped (filter mismatch, duplicate content).</param>
/// <param name="Skipped">Messages not touched because the ledger already knew them (done, or past the retry budget).</param>
/// <param name="Failed">Messages whose processing threw; they stay unhandled and are retried next cycle.</param>
public sealed record CycleReport(int Listed, int Archived, int Documents, int Ignored, int Skipped, int Failed)
{
    public bool HasFailures => Failed > 0;

    public override string ToString()
        => $"listed={Listed} archived={Archived} documents={Documents} ignored={Ignored} skipped={Skipped} failed={Failed}";
}
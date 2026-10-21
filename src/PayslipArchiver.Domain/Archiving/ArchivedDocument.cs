namespace PayslipArchiver.Domain.Archiving;

/// <summary>
/// A payslip that has been decrypted and written to the archive.
/// </summary>
/// <param name="Path">The final path in the archive (after any auto-rename on conflict).</param>
/// <param name="Sha256">Hex SHA-256 of the decrypted bytes, used to recognise re-sent duplicates.</param>
/// <param name="SizeBytes">Size of the decrypted document.</param>
/// <param name="Period">The pay period the document was filed under.</param>
/// <param name="SourceFileName">The attachment name as it arrived in the e-mail.</param>
public sealed record ArchivedDocument(
    string Path,
    string Sha256,
    long SizeBytes,
    PayPeriod Period,
    string SourceFileName);
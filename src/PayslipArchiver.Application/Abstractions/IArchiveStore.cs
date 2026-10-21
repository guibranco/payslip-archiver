namespace PayslipArchiver.Application.Abstractions;

/// <summary>
/// Where decrypted payslips end up (Dropbox, a synced folder, ...).
/// </summary>
public interface IArchiveStore
{
    /// <summary>
    /// Writes <paramref name="content"/> at <paramref name="path"/> (forward slashes, leading slash,
    /// relative to the store root). Implementations never overwrite: on a name clash they rename
    /// the new file and return the path actually used.
    /// </summary>
    /// <returns>The final path of the stored document.</returns>
    Task<string> StoreAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
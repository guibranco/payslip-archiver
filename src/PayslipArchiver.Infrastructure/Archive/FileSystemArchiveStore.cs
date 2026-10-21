using System.Globalization;

using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;

namespace PayslipArchiver.Infrastructure.Archive;

/// <summary>Writes documents under a local directory, never overwriting an existing file.</summary>
public sealed class FileSystemArchiveStore : IArchiveStore
{
    private readonly string _root;

    public FileSystemArchiveStore(IOptions<ArchiveOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.Directory) ? "archive" : options.Value.Directory);
    }

    public async Task<string> StoreAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var relative = path.Replace('\\', '/').TrimStart('/');
        var target = Path.GetFullPath(Path.Combine(_root, relative));

        // Compare against the root *plus a separator* so a sibling such as "<root>-other" cannot pass as a prefix.
        var rootWithSeparator = Path.EndsInDirectorySeparator(_root) ? _root : _root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to write outside the archive root: {path}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var attempt = 0;
        while (true)
        {
            var candidate = attempt == 0 ? target : WithSuffix(target, attempt);
            try
            {
                await using var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) && attempt < 100)
            {
                attempt++;
            }
        }
    }

    private static string WithSuffix(string path, int attempt)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{name} ({attempt}){extension}"));
    }
}
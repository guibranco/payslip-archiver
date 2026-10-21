using System.Diagnostics;

using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;

namespace PayslipArchiver.Infrastructure.Pdf;

/// <summary>
/// Fallback decryptor that runs <c>qpdf --decrypt</c>. qpdf copes with every encryption scheme in the
/// wild and only needs the user password. The password is passed through a temporary file rather than
/// the command line so it never shows up in <c>ps</c> or process accounting.
/// </summary>
public sealed class QpdfDecryptor : IPdfDecryptor
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);
    private readonly string _qpdfPath;

    public QpdfDecryptor(IOptions<PdfEngineOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _qpdfPath = string.IsNullOrWhiteSpace(options.Value.QpdfPath) ? "qpdf" : options.Value.QpdfPath;
    }

    public byte[] Decrypt(ReadOnlyMemory<byte> encryptedPdf, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var workDir = Directory.CreateTempSubdirectory("payslip-archiver-");
        try
        {
            var inputPath = Path.Combine(workDir.FullName, "in.pdf");
            var outputPath = Path.Combine(workDir.FullName, "out.pdf");
            var passwordPath = Path.Combine(workDir.FullName, "password");

            File.WriteAllBytes(inputPath, encryptedPdf.ToArray());
            WritePrivate(passwordPath, password);

            var startInfo = new ProcessStartInfo(_qpdfPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--decrypt");
            startInfo.ArgumentList.Add("--password-file=" + passwordPath);
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo)
                ?? throw new PdfDecryptionException($"Could not start '{_qpdfPath}'.");

            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(ProcessTimeout))
            {
                process.Kill(entireProcessTree: true);
                throw new PdfDecryptionException($"'{_qpdfPath}' did not finish within {ProcessTimeout.TotalSeconds:0}s.");
            }

            // Exit code 3 means "succeeded with warnings", which is fine for our purposes.
            if (process.ExitCode is not (0 or 3))
            {
                throw new PdfDecryptionException(
                    $"'{_qpdfPath}' exited with code {process.ExitCode}: {stderr.GetAwaiter().GetResult().Trim()} {stdout.GetAwaiter().GetResult().Trim()}".Trim());
            }

            return File.ReadAllBytes(outputPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new PdfDecryptionException($"Could not run '{_qpdfPath}': {ex.Message}", ex);
        }
        finally
        {
            try
            {
                workDir.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Best effort: the directory is under the temp folder and will be cleaned up by the OS eventually.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }
    }

    private static void WritePrivate(string path, string content)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
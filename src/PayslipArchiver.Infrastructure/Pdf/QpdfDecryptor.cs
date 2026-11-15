using System.Diagnostics;

using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;

namespace PayslipArchiver.Infrastructure.Pdf;

/// <summary>
/// Fallback decryptor that runs <c>qpdf --decrypt</c>. qpdf copes with every encryption scheme in the
/// wild and only needs the user password. The password is written to qpdf's standard input
/// (<c>--password-file=-</c>) rather than the command line, so it never shows up in <c>ps</c> or process
/// accounting, and never touches the disk either.
/// </summary>
/// <remarks>
/// The temporary files keep a <c>.pdf</c> extension: Ubuntu 26.04 ships an AppArmor profile for qpdf that
/// only lets it open files with known extensions (<c>.pdf</c>, <c>.json</c>, <c>.qdf</c>) under <c>/tmp</c>.
/// </remarks>
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

            File.WriteAllBytes(inputPath, encryptedPdf.ToArray());

            var startInfo = new ProcessStartInfo(_qpdfPath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--decrypt");
            startInfo.ArgumentList.Add("--password-file=-");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo)
                ?? throw new PdfDecryptionException($"Could not start '{_qpdfPath}'.");

            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();

            // qpdf reads the first line of the "file"; closing stdin marks its end.
            process.StandardInput.Write(password);
            process.StandardInput.Close();

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
}
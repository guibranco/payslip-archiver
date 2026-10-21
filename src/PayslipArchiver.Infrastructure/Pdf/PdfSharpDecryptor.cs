using PayslipArchiver.Application.Abstractions;

using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayslipArchiver.Infrastructure.Pdf;

/// <summary>
/// Removes the password with PDFsharp by importing every page into a fresh, unencrypted document.
/// </summary>
/// <remarks>
/// Payroll providers normally hand out only the <em>user</em> password, and PDFsharp refuses to open
/// such a document for modification (it wants the owner password for that). Opening in
/// <see cref="PdfDocumentOpenMode.Import"/> and copying the pages sidesteps that: the copy is a new
/// document with no security handler at all, and the pages keep their content and resources.
/// </remarks>
public sealed class PdfSharpDecryptor : IPdfDecryptor
{
    /// <summary>
    /// PDFsharp 6.5+ throws <c>PdfSharpLicenseException</c> until a licence is declared. This project is
    /// distributed under the AGPL-3.0, so the AGPL option applies; it only needs to be set once per process.
    /// </summary>
    private static readonly Lazy<bool> LicenseDeclared = new(() =>
    {
        PdfSharpLicense.SetAgplLicense();
        return true;
    });

    public byte[] Decrypt(ReadOnlyMemory<byte> encryptedPdf, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        _ = LicenseDeclared.Value;

        try
        {
            using var input = new MemoryStream(encryptedPdf.ToArray(), writable: false);
            using var source = PdfReader.Open(input, password, PdfDocumentOpenMode.Import);
            using var target = new PdfDocument();

            target.Info.Title = source.Info.Title;
            target.Info.Author = source.Info.Author;
            target.Info.Subject = source.Info.Subject;
            target.Info.Creator = source.Info.Creator;

            foreach (var page in source.Pages)
            {
                target.AddPage(page);
            }

            using var output = new MemoryStream();
            target.Save(output, closeStream: false);
            return output.ToArray();
        }
        catch (PdfReaderException ex)
        {
            throw new PdfDecryptionException($"PDFsharp could not open the document: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new PdfDecryptionException($"PDFsharp could not process the document: {ex.Message}", ex);
        }
    }
}
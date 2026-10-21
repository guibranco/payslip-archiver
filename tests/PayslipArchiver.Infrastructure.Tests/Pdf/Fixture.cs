using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayslipArchiver.Infrastructure.Tests.Pdf;

internal static class Fixture
{
    public static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>
    /// The user password every encrypted fixture was generated with. It lives in a data file next to the
    /// PDFs (shared with <c>scripts/make-fixtures.sh</c>) rather than as a literal here.
    /// </summary>
    public static readonly string Password = File.ReadAllText(Path.Combine(Directory, "password.txt")).Trim();

    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Directory, name));

    /// <summary>A PDF with no <c>/Encrypt</c> entry in its trailer is, by definition, not encrypted.</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> pdf) => pdf.IndexOf("/Encrypt"u8) >= 0;

    /// <summary>
    /// Opens a decrypted document for inspection. PDFsharp 6.5+ needs a licence declared in every process
    /// that uses it, the test runner included; the decryptor under test may not be the PDFsharp one.
    /// </summary>
    public static PdfDocument Open(byte[] pdf)
    {
        PdfSharpLicense.SetAgplLicense();
        return PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
    }
}
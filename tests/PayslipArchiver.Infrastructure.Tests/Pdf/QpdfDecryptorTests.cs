using System.Diagnostics;

using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Infrastructure.Pdf;

using PdfSharp.Pdf.IO;

namespace PayslipArchiver.Infrastructure.Tests.Pdf;

/// <summary>Skips automatically when qpdf is not installed on the machine running the tests.</summary>
public sealed class QpdfFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> QpdfAvailable = new(Probe);

    public QpdfFactAttribute()
    {
        if (!QpdfAvailable.Value)
        {
            Skip = "qpdf is not installed";
        }
    }

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("qpdf", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

public sealed class QpdfTheoryAttribute : TheoryAttribute
{
    public QpdfTheoryAttribute()
    {
        Skip = new QpdfFactAttribute().Skip;
    }
}

public sealed class QpdfDecryptorTests
{
    private readonly QpdfDecryptor _sut = new(Options.Create(new PdfEngineOptions()));

    [QpdfTheory]
    [InlineData("rc4-128-user.pdf")]
    [InlineData("aes128-user.pdf")]
    [InlineData("aes256-user.pdf")]
    [InlineData("aes256-same.pdf")]
    public void Decrypt_RemovesEncryption_AndKeepsThePage(string fixture)
    {
        var decrypted = _sut.Decrypt(Fixture.Read(fixture), Fixture.Password);

        Assert.False(Fixture.IsEncrypted(decrypted));
        using var document = PdfReader.Open(new MemoryStream(decrypted), PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
    }

    [QpdfFact]
    public void Decrypt_WithTheWrongPassword_Throws()
    {
        var ex = Assert.Throws<PdfDecryptionException>(() => _sut.Decrypt(Fixture.Read("aes256-user.pdf"), "nope"));

        Assert.Contains("qpdf", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decrypt_WhenTheExecutableIsMissing_ThrowsADescriptiveError()
    {
        var sut = new QpdfDecryptor(Options.Create(new PdfEngineOptions { QpdfPath = "definitely-not-qpdf-" + Guid.NewGuid().ToString("N") }));

        var ex = Assert.Throws<PdfDecryptionException>(() => sut.Decrypt(Fixture.Read("plain.pdf"), Fixture.Password));

        Assert.Contains("Could not run", ex.Message, StringComparison.Ordinal);
    }
}
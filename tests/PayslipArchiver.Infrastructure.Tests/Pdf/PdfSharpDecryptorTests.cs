using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Infrastructure.Pdf;

namespace PayslipArchiver.Infrastructure.Tests.Pdf;

public sealed class PdfSharpDecryptorTests
{
    private readonly PdfSharpDecryptor _sut = new();

    [Theory]
    [InlineData("rc4-128-user.pdf")]
    [InlineData("aes128-user.pdf")]
    [InlineData("aes256-user.pdf")]
    [InlineData("aes256-same.pdf")]
    public void Decrypt_RemovesEncryption_AndKeepsThePage(string fixture)
    {
        var encrypted = Fixture.Read(fixture);
        Assert.True(Fixture.IsEncrypted(encrypted), "fixture should be encrypted");

        var decrypted = _sut.Decrypt(encrypted, Fixture.Password);

        Assert.False(Fixture.IsEncrypted(decrypted), "output must not carry an /Encrypt dictionary");
        using var document = Fixture.Open(decrypted);
        Assert.Equal(1, document.PageCount);
        Assert.Equal("Payslip fixture", document.Info.Title);
    }

    [Fact]
    public void Decrypt_PassesThroughAnUnencryptedDocument()
    {
        var plain = Fixture.Read("plain.pdf");

        var result = _sut.Decrypt(plain, Fixture.Password);

        using var document = Fixture.Open(result);
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void Decrypt_WithTheWrongPassword_Throws()
    {
        var encrypted = Fixture.Read("aes256-user.pdf");

        var ex = Assert.Throws<PdfDecryptionException>(() => _sut.Decrypt(encrypted, "nope"));

        Assert.Contains("PDFsharp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decrypt_IsIdempotent()
    {
        var encrypted = Fixture.Read("aes128-user.pdf");

        var once = _sut.Decrypt(encrypted, Fixture.Password);
        var twice = _sut.Decrypt(once, Fixture.Password);

        using var document = Fixture.Open(twice);
        Assert.Equal(1, document.PageCount);
    }
}
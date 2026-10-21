namespace PayslipArchiver.Application.Abstractions;

/// <summary>
/// Produces an unencrypted copy of a password-protected PDF. Documents that are not encrypted
/// are passed through (re-saved) unchanged in content.
/// </summary>
public interface IPdfDecryptor
{
    /// <exception cref="PdfDecryptionException">The password is wrong or the document cannot be read.</exception>
    byte[] Decrypt(ReadOnlyMemory<byte> encryptedPdf, string password);
}

public sealed class PdfDecryptionException : Exception
{
    public PdfDecryptionException()
    {
    }

    public PdfDecryptionException(string message)
        : base(message)
    {
    }

    public PdfDecryptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
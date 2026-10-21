namespace PayslipArchiver.Infrastructure.Pdf;

public enum PdfEngine
{
    /// <summary>In-process, via the PDFsharp library (default).</summary>
    PdfSharp,

    /// <summary>Shells out to the <c>qpdf</c> CLI. Handles anything PDFsharp cannot; needs qpdf installed.</summary>
    Qpdf,
}

/// <summary>Bound from the <c>Pdf</c> configuration section (alongside the password the application layer reads).</summary>
public sealed class PdfEngineOptions
{
    public const string Section = "Pdf";

    public PdfEngine Engine { get; set; } = PdfEngine.PdfSharp;

    /// <summary>Path or name of the qpdf executable when <see cref="Engine"/> is <see cref="PdfEngine.Qpdf"/>.</summary>
    public string QpdfPath { get; set; } = "qpdf";
}
namespace PayslipArchiver.Infrastructure.Pdf;

public enum PdfEngine
{
    /// <summary>In-process, via the PDFsharp library. Needs no external tool, but cannot open every encryption scheme.</summary>
    PdfSharp,

    /// <summary>Shells out to the <c>qpdf</c> CLI (default). Handles every encryption scheme; needs qpdf installed.</summary>
    Qpdf,
}

/// <summary>Bound from the <c>Pdf</c> configuration section (alongside the password the application layer reads).</summary>
public sealed class PdfEngineOptions
{
    public const string Section = "Pdf";

    public PdfEngine Engine { get; set; } = PdfEngine.Qpdf;

    /// <summary>Path or name of the qpdf executable when <see cref="Engine"/> is <see cref="PdfEngine.Qpdf"/>.</summary>
    public string QpdfPath { get; set; } = "qpdf";
}
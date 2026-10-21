using System.ComponentModel.DataAnnotations;

namespace PayslipArchiver.Infrastructure.Ledger;

/// <summary>Bound from the <c>Ledger</c> configuration section.</summary>
public sealed class LedgerOptions
{
    public const string Section = "Ledger";

    /// <summary>Path of the SQLite database file. Created on first run, including parent directories.</summary>
    [Required]
    public string Path { get; set; } = "data/ledger.db";
}
using System.ComponentModel.DataAnnotations;

namespace PaymentFunds.Models;

public class LedgerAccount
{
    public int Id { get; set; }

    /// <summary>Stable identifier used in code, for example CASH or PAYABLE.</summary>
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public LedgerAccountType Type { get; set; }

    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = "usd";

    public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
}

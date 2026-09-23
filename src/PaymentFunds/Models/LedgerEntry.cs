using System.ComponentModel.DataAnnotations;

namespace PaymentFunds.Models;

public class LedgerEntry
{
    public long Id { get; set; }

    /// <summary>
    /// Groups the entries that were posted together. Debits and credits sharing a
    /// TransactionId must sum to zero.
    /// </summary>
    public Guid TransactionId { get; set; }

    public int AccountId { get; set; }
    public LedgerAccount Account { get; set; } = null!;

    public EntryDirection Direction { get; set; }

    /// <summary>Always positive, in minor units. The direction carries the sign.</summary>
    public long AmountMinor { get; set; }

    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = "usd";

    public int? PaymentRequestId { get; set; }
    public PaymentRequest? PaymentRequest { get; set; }

    [Required]
    [StringLength(200)]
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

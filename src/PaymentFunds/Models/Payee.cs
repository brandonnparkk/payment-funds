using System.ComponentModel.DataAnnotations;

namespace PaymentFunds.Models;

public class Payee
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public PayeeStatus Status { get; set; } = PayeeStatus.Unverified;
    /// <summary>
    /// Opaque reference issued by the payment provider, for example a Stripe
    /// connected account id. Never a bank account number.
    /// </summary>
    public string? ProviderAccountReference { get; set; }
    /// <summary>
    /// Display-only masked destination, for example "••••4321".
    /// </summary>
    public string? PayoutDestinationMask { get; set; }

    public bool TaxFormOnFile { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public ICollection<PaymentRequest> PaymentRequests { get; set; } = new List<PaymentRequest>();
}

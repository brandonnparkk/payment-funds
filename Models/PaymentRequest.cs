using System.ComponentModel.DataAnnotations;
namespace PaymentFunds.Models;

public class PaymentRequest
{
    public int Id { get; set; }
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public string RequestedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public string? RejectedBy { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
}
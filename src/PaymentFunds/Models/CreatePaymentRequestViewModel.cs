using System.ComponentModel.DataAnnotations;

namespace PaymentFunds.Models;

public class CreatePaymentRequestViewModel
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }

    [Required]
    [RegularExpression("^(usd|eur|gbp)$", ErrorMessage = "Invalid currency")]
    public string Currency { get; set; } = "usd";

    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}
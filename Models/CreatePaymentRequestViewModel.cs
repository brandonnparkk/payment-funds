using System.ComponentModel.DataAnnotations;

namespace PaymentFunds.Models;

public class CreatePaymentRequestViewModel
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }

    [Required]
    public string Currency { get; set; } = "USD";
    
    [Required]
    public string RequestedBy { get; set; } = string.Empty;

}
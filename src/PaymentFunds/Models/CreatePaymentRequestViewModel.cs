using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

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

    [Required]
    public RequestType Type { get; set; } = RequestType.Disbursement;

    [Display(Name = "Payee")]
    public int? PayeeId { get; set; }

    public IEnumerable<SelectListItem> AvailablePayees { get; set; } = [];
}
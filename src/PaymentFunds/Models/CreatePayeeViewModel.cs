using System.ComponentModel.DataAnnotations;

namespace PaymentFunds.Models;

public class CreatePayeeViewModel
{
  [Required]
  [StringLength(200)]
  [Display(Name = "Display name")]
  public string DisplayName { get; set; } = string.Empty;

  [Required]
  [EmailAddress]
  public string Email { get; set; } = string.Empty;

  [Required]
  [RegularExpression(@"^\d{4}$", ErrorMessage = "Enter exactly four digits.")]
  [Display(Name = "Payout account, last four digits only")]
  public string PayoutLastFour { get; set; } = string.Empty;

  [Display(Name = "Tax form on file")]
  public bool TaxFormOnFile { get; set; }
}
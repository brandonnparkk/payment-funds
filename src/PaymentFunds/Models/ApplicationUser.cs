using Microsoft.AspNetCore.Identity;

namespace PaymentFunds.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set;} = string.Empty;
}
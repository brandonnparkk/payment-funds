using System.Text.RegularExpressions;
using PaymentFunds.Models;

namespace PaymentFunds.Extensions;

public static class DisplayExtensions
{
    public static string ToMoney(this decimal amount, string? currency)
    {
        var symbol = currency?.ToLowerInvariant() switch
        {
            "usd" => "$",
            "eur" => "€",
            "gbp" => "£",
            _ => null
        };

        return symbol is not null
            ? $"{symbol}{amount:N2}"
            : $"{amount:N2} {currency?.ToUpperInvariant()}";
    }

    public static string BadgeClass(this PaymentStatus status) => status switch
    {
        PaymentStatus.Created => "text-bg-secondary",
        PaymentStatus.PendingApproval => "text-bg-warning",
        PaymentStatus.Approved => "text-bg-info",
        PaymentStatus.Processing => "text-bg-primary",
        PaymentStatus.Completed => "text-bg-success",
        PaymentStatus.Rejected => "text-bg-danger",
        PaymentStatus.Failed => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    public static string BadgeClass(this PayeeStatus status) => status switch
    {
        PayeeStatus.Unverified => "text-bg-warning",
        PayeeStatus.Verified => "text-bg-success",
        PayeeStatus.Suspended => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    /// <summary>Turns PendingApproval into "Pending approval".</summary>
    public static string Humanize(this Enum value)
    {
        var words = Regex.Replace(value.ToString(), "(?<!^)([A-Z])", " $1");
        return char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }
}
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Models;

namespace PaymentFunds.Data;

public static class LedgerSeeder
{
    private static readonly (string Code, string Name, LedgerAccountType Type)[] Accounts =
    [
        ("CASH",    "Platform cash",    LedgerAccountType.Asset),
        ("PAYABLE", "Payable to payees", LedgerAccountType.Liability),
        ("EXPENSE", "Disbursement expense", LedgerAccountType.Expense),
        ("REVENUE", "Collected revenue", LedgerAccountType.Revenue)
    ];

    private static readonly string[] Currencies = ["usd", "eur", "gbp"];

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await db.LedgerAccounts
            .Select(a => new { a.Code, a.Currency })
            .ToListAsync();

        foreach (var currency in Currencies)
        {
            foreach (var (code, name, type) in Accounts)
            {
                if (existing.Any(e => e.Code == code && e.Currency == currency))
                    continue;

                db.LedgerAccounts.Add(new LedgerAccount
                {
                    Code = code,
                    Name = $"{name} ({currency.ToUpperInvariant()})",
                    Type = type,
                    Currency = currency
                });
            }
        }

        await db.SaveChangesAsync();
    }
}

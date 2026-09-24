namespace PaymentFunds.Models;

public record AccountBalance(string Code, string Name, LedgerAccountType Type, long BalanceMinor);
public class LedgerViewModel
{
    public string Currency { get; set; } = "usd";
    public long AvailableMinor { get; set; }
    public List<AccountBalance> Balances { get; set; } = [];
    public List<LedgerEntry> Entries { get; set; } = [];
}
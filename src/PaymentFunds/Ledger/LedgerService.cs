using Microsoft.EntityFrameworkCore;
using PaymentFunds.Data;
using PaymentFunds.Models;

namespace PaymentFunds.Ledger;

public class LedgerService : ILedgerService
{
    private readonly ApplicationDbContext _context;

    public LedgerService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddPostingAsync(LedgerPosting posting, CancellationToken ct = default)
    {
        if (posting.Lines.Count < 2)
        {
            throw new InvalidOperationException("A posting needs at least two lines.");
        }

        if (posting.Lines.Any(l => l.AmountMinor <= 0))
        {
            throw new InvalidOperationException("Entry amounts must be positive.");
        }

        var debits = posting.Lines
          .Where(l => l.Direction == EntryDirection.Debit)
          .Sum(l => l.AmountMinor);

        var credits = posting.Lines
          .Where(l => l.Direction == EntryDirection.Credit)
          .Sum(l => l.AmountMinor);

        if (debits != credits)
        {
            throw new InvalidOperationException(
              $"Posting does not balance: debits {debits}, credits {credits}.");
        }

        var codes = posting.Lines.Select(l => l.AccountCode).Distinct().ToList();

        var accounts = await _context.LedgerAccounts
          .Where(a => a.Currency == posting.Currency && codes.Contains(a.Code))
          .ToDictionaryAsync(a => a.Code, ct);

        var missing = codes.Where(c => !accounts.ContainsKey(c)).ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
              $"No {posting.Currency} account for: {string.Join(", ", missing)}.");
        }

        var transactionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        foreach (var line in posting.Lines)
        {
            _context.LedgerEntries.Add(new LedgerEntry
            {
                TransactionId = transactionId,
                AccountId = accounts[line.AccountCode].Id,
                Direction = line.Direction,
                AmountMinor = line.AmountMinor,
                Currency = posting.Currency,
                PaymentRequestId = posting.PaymentRequestId,
                Description = posting.Description,
                CreatedAt = now
            });
        }
    }

    public async Task<long> BalanceMinorAsync(string accountCode, string currency, CancellationToken ct = default)
    {
        var account = await _context.LedgerAccounts
            .FirstOrDefaultAsync(a => a.Code == accountCode && a.Currency == currency, ct)
            ?? throw new InvalidOperationException($"No {currency} account with {accountCode}.");

        var debits = await _context.LedgerEntries
            .Where(e => e.AccountId == account.Id && e.Direction == EntryDirection.Debit)
            .SumAsync(e => (long?)e.AmountMinor, ct) ?? 0;

        var credits = await _context.LedgerEntries
            .Where(e => e.AccountId == account.Id && e.Direction == EntryDirection.Credit)
            .SumAsync(e => (long?)e.AmountMinor, ct) ?? 0;

        return account.Type is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? debits - credits
            : credits - debits;
    }

    public async Task<long> AvailableFundsMinorAsync(
        string currency,
        CancellationToken ct = default
    )
    {
        var cash = await BalanceMinorAsync("CASH", currency, ct);
        var payable = await BalanceMinorAsync("PAYABLE", currency, ct);

        return cash - payable;
    }
}

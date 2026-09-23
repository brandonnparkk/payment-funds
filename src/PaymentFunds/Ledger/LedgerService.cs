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
}

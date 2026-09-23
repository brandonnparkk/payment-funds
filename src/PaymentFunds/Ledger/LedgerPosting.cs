using PaymentFunds.Models;

namespace PaymentFunds.Ledger;

public record LedgerLine(string AccountCode, EntryDirection Direction, long AmountMinor);
public record LedgerPosting(
  string Currency,
  string Description,
  int? PaymentRequestId,
  IReadOnlyList<LedgerLine> Lines);

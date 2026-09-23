namespace PaymentFunds.Ledger;

public interface ILedgerService
{
  /// <summary>
  /// Stages a balanced set of entries on the current DbContext. The caller is
  /// responsible for SaveChangesAsync, so the posting commits in the same
  /// transaction as whatever state change caused it.
  /// </summary>
  Task AddPostingAsync(LedgerPosting posting, CancellationToken ct = default);

  /// <summary>
  ///Natural balance of one account in minor units.
  /// </summary>
  Task<long> BalanceMinorAsync(string accountCode, string currency, CancellationToken ct = default);

  /// <summary>
  /// Cash on hand less obligations already incurred but not yet paid.
  /// </summary>
  Task<long> AvailableFundsMinorAsync(string currency, CancellationToken ct = default);

  static long ToMinorUnits(decimal amount) =>
    (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}

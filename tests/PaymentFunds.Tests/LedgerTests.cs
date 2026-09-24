using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentFunds.Data;
using PaymentFunds.Ledger;
using PaymentFunds.Models;

namespace PaymentFunds.Tests;

[TestFixture]
public class LedgerTests : IntegrationTestBase
{
    private const string Currency = "usd";

    // ---- the core invariant ----

    [Test]
    public async Task Every_transaction_in_the_ledger_balances_to_zero()
    {
        await FundPlatformAsync(500m);

        var payeeId = await SeedVerifiedPayeeAsync();
        var approved = await SeedPendingRequestAsync(payeeId, 19.99m);
        await ApproveAsync(approved);

        var failed = await SeedPendingRequestAsync(payeeId, 5.00m);
        await ApproveAsync(failed);
        await SimulateFailureAsync(failed);

        var imbalanced = await ImbalancedTransactionsAsync();

        Assert.That(imbalanced, Is.Empty,
            "Every TransactionId must have debits equal to credits.");
    }

    [Test]
    public async Task An_unbalanced_posting_is_rejected_by_the_service()
    {
        using var scope = Factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();

        var posting = new LedgerPosting(Currency, "deliberately wrong", null,
        [
            new LedgerLine("CASH", EntryDirection.Debit, 1000),
            new LedgerLine("OPENING", EntryDirection.Credit, 999)
        ]);

        Assert.That(
            async () => await ledger.AddPostingAsync(posting),
            Throws.TypeOf<InvalidOperationException>()
                  .With.Message.Contains("does not balance"));
    }

    // ---- approval posts the obligation ----

    [Test]
    public async Task Approving_a_disbursement_posts_expense_and_payable()
    {
        await FundPlatformAsync(100m);

        var payeeId = await SeedVerifiedPayeeAsync();
        var requestId = await SeedPendingRequestAsync(payeeId, 19.99m);

        await ApproveAsync(requestId);

        var entries = await EntriesForRequestAsync(requestId);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(DirectionFor(entries, "EXPENSE"), Is.EqualTo(EntryDirection.Debit));
            Assert.That(DirectionFor(entries, "PAYABLE"), Is.EqualTo(EntryDirection.Credit));
            Assert.That(entries.All(e => e.AmountMinor == 1999), Is.True);
            Assert.That(entries.Select(e => e.TransactionId).Distinct().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Approving_a_disbursement_reduces_available_funds_before_any_money_moves()
    {
        await FundPlatformAsync(100m);

        var payeeId = await SeedVerifiedPayeeAsync();
        var requestId = await SeedPendingRequestAsync(payeeId, 19.99m);

        await ApproveAsync(requestId);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await BalanceAsync("CASH"), Is.EqualTo(10_000),
                "Cash is untouched until settlement.");
            Assert.That(await BalanceAsync("PAYABLE"), Is.EqualTo(1_999),
                "The obligation exists immediately.");
            Assert.That(await AvailableAsync(), Is.EqualTo(8_001),
                "Available funds already account for the obligation.");
        });
    }

    // ---- the guard ----

    [Test]
    public async Task Approval_is_refused_when_funds_are_short()
    {
        await FundPlatformAsync(10m);

        var payeeId = await SeedVerifiedPayeeAsync();
        var requestId = await SeedPendingRequestAsync(payeeId, 50m);

        await ApproveAsync(requestId);

        var request = await LoadRequestAsync(requestId);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(request.Status, Is.EqualTo(PaymentStatus.PendingApproval),
                "A refused approval must not change the status.");
            Assert.That(request.ApprovedBy, Is.Null);
            Assert.That(await EntriesForRequestAsync(requestId), Is.Empty,
                "A refused approval must not write ledger entries.");
            Assert.That(await BalanceAsync("PAYABLE"), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task A_second_approval_is_refused_once_the_first_consumed_the_funds()
    {
        await FundPlatformAsync(30m);

        var payeeId = await SeedVerifiedPayeeAsync();
        var first = await SeedPendingRequestAsync(payeeId, 20m);
        var second = await SeedPendingRequestAsync(payeeId, 20m);

        await ApproveAsync(first);
        await ApproveAsync(second);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That((await LoadRequestAsync(first)).Status,
                Is.EqualTo(PaymentStatus.Approved));
            Assert.That((await LoadRequestAsync(second)).Status,
                Is.EqualTo(PaymentStatus.PendingApproval));
            Assert.That(await AvailableAsync(), Is.EqualTo(1_000));
        });
    }

    // ---- reversal ----

    [Test]
    public async Task A_failed_disbursement_reverses_rather_than_deletes()
    {
        await FundPlatformAsync(100m);

        var payeeId = await SeedVerifiedPayeeAsync();
        var requestId = await SeedPendingRequestAsync(payeeId, 19.99m);

        await ApproveAsync(requestId);
        await SimulateFailureAsync(requestId);

        var entries = await EntriesForRequestAsync(requestId);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(entries, Has.Count.EqualTo(4),
                "The original pair is kept and an opposite pair is added.");
            Assert.That(entries.Select(e => e.TransactionId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(await BalanceAsync("PAYABLE"), Is.EqualTo(0),
                "The obligation nets out.");
            Assert.That(await BalanceAsync("EXPENSE"), Is.EqualTo(0));
            Assert.That(await AvailableAsync(), Is.EqualTo(10_000),
                "Funds are released.");
        });
    }

    // ---- balance semantics ----

    [Test]
    public async Task Funding_debits_cash_and_credits_opening_balance()
    {
        await FundPlatformAsync(250m);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await BalanceAsync("CASH"), Is.EqualTo(25_000));
            Assert.That(await BalanceAsync("OPENING"), Is.EqualTo(25_000));
            Assert.That(await AvailableAsync(), Is.EqualTo(25_000));
        });
    }

    [Test]
    public async Task Accounts_are_isolated_by_currency()
    {
        await FundPlatformAsync(100m, "usd");
        await FundPlatformAsync(50m, "eur");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await BalanceAsync("CASH", "usd"), Is.EqualTo(10_000));
            Assert.That(await BalanceAsync("CASH", "eur"), Is.EqualTo(5_000));
            Assert.That(await BalanceAsync("CASH", "gbp"), Is.EqualTo(0));
        });
    }

    // ---- helpers ----

    private async Task FundPlatformAsync(decimal amount, string currency = Currency)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();

        var minor = ILedgerService.ToMinorUnits(amount);

        await ledger.AddPostingAsync(new LedgerPosting(
            currency, "Test funding", null,
            [
                new LedgerLine("CASH", EntryDirection.Debit, minor),
                new LedgerLine("OPENING", EntryDirection.Credit, minor)
            ]));

        await db.SaveChangesAsync();
    }

    private async Task<int> SeedVerifiedPayeeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payee = new Payee
        {
            DisplayName = "Acme Consulting",
            Email = $"payee-{Guid.NewGuid():N}@test.local",
            Status = PayeeStatus.Verified,
            TaxFormOnFile = true,
            PayoutDestinationMask = "••••4321",
            CreatedBy = RequesterUser,
            CreatedAt = DateTime.UtcNow
        };

        db.Payees.Add(payee);
        await db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task<int> SeedPendingRequestAsync(int payeeId, decimal amount)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var request = new PaymentRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Amount = amount,
            Currency = Currency,
            Type = RequestType.Disbursement,
            PayeeId = payeeId,
            Status = PaymentStatus.PendingApproval,
            RequestedBy = RequesterUser,
            CreatedAt = DateTime.UtcNow
        };

        db.PaymentRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    /// <summary>Drives the real Approve endpoint so the guard and posting both run.</summary>
    private async Task ApproveAsync(int requestId)
    {
        var token = await GetTokenAsync("/PaymentRequests/Create", ApproverUser, "Approver");

        var request = Request(
            HttpMethod.Post, $"/PaymentRequests/Approve/{requestId}", ApproverUser, "Approver");

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        await Client.SendAsync(request);
    }

    /// <summary>
    /// Mimics what the worker does when a provider call fails after approval:
    /// mark it failed and post the reversing pair in the same transaction.
    /// </summary>
    private async Task SimulateFailureAsync(int requestId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();

        var request = await db.PaymentRequests.SingleAsync(r => r.Id == requestId);

        request.Status = PaymentStatus.Failed;
        request.SettledAt = DateTime.UtcNow;

        var minor = ILedgerService.ToMinorUnits(request.Amount);

        await ledger.AddPostingAsync(new LedgerPosting(
            request.Currency,
            $"Reversed disbursement #{request.Id}: provider rejected the payment",
            request.Id,
            [
                new LedgerLine("PAYABLE", EntryDirection.Debit, minor),
                new LedgerLine("EXPENSE", EntryDirection.Credit, minor)
            ]));

        await db.SaveChangesAsync();
    }

    private async Task<string> GetTokenAsync(string url, string user, string roles)
    {
        var response = await Client.SendAsync(Request(HttpMethod.Get, url, user, roles));
        response.EnsureSuccessStatusCode();
        return ExtractToken(await response.Content.ReadAsStringAsync());
    }

    private async Task<long> BalanceAsync(string code, string currency = Currency)
    {
        using var scope = Factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        return await ledger.BalanceMinorAsync(code, currency);
    }

    private async Task<long> AvailableAsync(string currency = Currency)
    {
        using var scope = Factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        return await ledger.AvailableFundsMinorAsync(currency);
    }

    private async Task<PaymentRequest> LoadRequestAsync(int id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.PaymentRequests.SingleAsync(r => r.Id == id);
    }

    private async Task<List<LedgerEntry>> EntriesForRequestAsync(int requestId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.LedgerEntries
            .Include(e => e.Account)
            .Where(e => e.PaymentRequestId == requestId)
            .OrderBy(e => e.Id)
            .ToListAsync();
    }

    private static EntryDirection DirectionFor(List<LedgerEntry> entries, string accountCode) =>
        entries.Single(e => e.Account.Code == accountCode).Direction;

    /// <summary>
    /// Any TransactionId whose debits do not equal its credits. Should always be empty.
    /// </summary>
    private async Task<List<Guid>> ImbalancedTransactionsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.LedgerEntries
            .GroupBy(e => e.TransactionId)
            .Where(g => g.Sum(e => e.Direction == EntryDirection.Debit
                    ? e.AmountMinor
                    : -e.AmountMinor) != 0)
            .Select(g => g.Key)
            .ToListAsync();
    }
}

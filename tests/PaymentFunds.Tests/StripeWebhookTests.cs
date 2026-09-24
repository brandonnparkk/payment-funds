using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PaymentFunds.Data;
using PaymentFunds.Ledger;
using PaymentFunds.Models;
using Stripe;

namespace PaymentFunds.Tests;

[TestFixture]
public class StripeWebhookTests : IntegrationTestBase
{
    private const string Url = "/api/stripe/webhook";

    private static string Sign(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string SucceededEvent(string eventId, string intentId) =>
    JsonSerializer.Serialize(new
    {
        id = eventId,
        @object = "event",
        type = "payment_intent.succeeded",
        api_version = StripeConfiguration.ApiVersion,
        data = new
        {
            @object = new
            {
                id = intentId,
                @object = "payment_intent",
                amount = 1999,
                currency = "usd"
            }
        }
    });

    private async Task<HttpResponseMessage> PostAsync(string payload, string signature)
    {
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("Stripe-Signature", signature);
        return await Client.PostAsync(Url, content);
    }

    [Test]
    public async Task Forged_signature_is_rejected()
    {
        var payload = SucceededEvent("evt_forged", "pi_forged");
        var response = await PostAsync(payload, "t=1, v1=deadbeef");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Valid_event_moves_request_to_completed()
    {
        await SeedProcessingRequestAsync("pi_real_1");
        var payload = SucceededEvent("evt_real_1", "pi_real_1");

        var response = await PostAsync(payload, Sign(payload, PaymentFundsFactory.WebhookSecret));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.PaymentRequests.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Status, Is.EqualTo(PaymentStatus.Completed));
            Assert.That(saved.SettledAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task Settling_a_collection_posts_cash_and_revenue()
    {
        var requestId = await SeedProcessingRequestAsync("pi_collection_1", RequestType.Collection);
        var payload = SucceededEvent("evt_collection_1", "pi_collection_1");

        await PostAsync(payload, Sign(payload, PaymentFundsFactory.WebhookSecret));

        var entries = await EntriesForRequestAsync(requestId);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(DirectionFor(entries, "CASH"), Is.EqualTo(EntryDirection.Debit));
            Assert.That(DirectionFor(entries, "REVENUE"), Is.EqualTo(EntryDirection.Credit));
            Assert.That(entries.All(e => e.AmountMinor == 1999), Is.True);
            Assert.That(entries.Select(e => e.TransactionId).Distinct().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Settling_a_disbursement_discharges_the_payable()
    {
        var requestId = await SeedProcessingRequestAsync("pi_disb_1", RequestType.Disbursement);
        await SeedApprovalPostingAsync(requestId, 19.99m);

        var payload = SucceededEvent("evt_disb_1", "pi_disb_1");
        await PostAsync(payload, Sign(payload, PaymentFundsFactory.WebhookSecret));

        var entries = await EntriesForRequestAsync(requestId);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(entries, Has.Count.EqualTo(4),
                "Two from the approval, two from the settlement.");
            Assert.That(await BalanceAsync("PAYABLE"), Is.EqualTo(0),
                "The obligation is discharged.");
            Assert.That(await BalanceAsync("CASH"), Is.EqualTo(-1999),
                "Cash leaves the platform. Negative here only because nothing funded it.");
        });
    }

    [Test]
    public async Task Duplicate_event_is_read_once()
    {
        await SeedProcessingRequestAsync("pi_dupe_1");
        var payload = SucceededEvent("evt_dupe_1", "pi_dupe_1");
        var signature = Sign(payload, PaymentFundsFactory.WebhookSecret);

        var first = await PostAsync(payload, signature);
        var second = await PostAsync(payload, signature);

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(await db.ProcessedStripeEvents.CountAsync(), Is.EqualTo(1));
    }

    // ---- helpers ----

    private async Task<int> SeedProcessingRequestAsync(
        string intentId, RequestType type = RequestType.Collection)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var request = new PaymentRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Amount = 19.99m,
            Currency = "usd",
            Type = type,
            Status = PaymentStatus.Processing,
            RequestedBy = "test",
            ProviderReference = intentId
        };

        db.PaymentRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    /// <summary>
    /// Recreates the posting that approval would have made, so a disbursement arrives
    /// at settlement with a payable to discharge rather than out of nowhere.
    /// </summary>
    private async Task SeedApprovalPostingAsync(int requestId, decimal amount)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();

        var minor = ILedgerService.ToMinorUnits(amount);

        await ledger.AddPostingAsync(new LedgerPosting(
            "usd",
            $"Approved disbursement #{requestId}",
            requestId,
            [
                new LedgerLine("EXPENSE", EntryDirection.Debit, minor),
                new LedgerLine("PAYABLE", EntryDirection.Credit, minor)
            ]));

        await db.SaveChangesAsync();
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

    private async Task<long> BalanceAsync(string code, string currency = "usd")
    {
        using var scope = Factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        return await ledger.BalanceMinorAsync(code, currency);
    }

    private static EntryDirection DirectionFor(List<LedgerEntry> entries, string accountCode) =>
        entries.Single(e => e.Account.Code == accountCode).Direction;
}

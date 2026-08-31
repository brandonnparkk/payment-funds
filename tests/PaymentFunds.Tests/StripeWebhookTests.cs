using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PaymentFunds.Data;
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
        var body = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine(body);
        
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.PaymentRequests.SingleAsync();
        Assert.That(saved.Status, Is.EqualTo(PaymentStatus.Completed));
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

    private async Task SeedProcessingRequestAsync(string intentId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.PaymentRequests.Add(new PaymentRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Amount = 19.99m,
            Currency = "usd",
            Status = PaymentStatus.Processing,
            RequestedBy = "test",
            StripePaymentIntentId = intentId
        });
        await db.SaveChangesAsync();
    }
}

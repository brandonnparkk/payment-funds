using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PaymentFunds.Data;
using PaymentFunds.Models;

namespace PaymentFunds.Tests;

[TestFixture]
public class PayeeTests : IntegrationTestBase
{
    [Test]
    public async Task Requester_cannot_verify_a_payee()
    {
        var id = await SeedPayeeAsync(createdBy: ApproverUser);

        var response = await PostVerifyAsync(id, RequesterUser, "Requester");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await AssertPayeeStatusAsync(id, PayeeStatus.Unverified);
    }

    [Test]
    public async Task Approver_cannot_verify_a_payee_they_created()
    {
        var id = await SeedPayeeAsync(createdBy: ApproverUser);

        var response = await PostVerifyAsync(id, ApproverUser, "Approver");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await AssertPayeeStatusAsync(id, PayeeStatus.Unverified);
    }

    [Test]
    public async Task Approver_can_verify_a_payee_created_by_someone_else()
    {
        var id = await SeedPayeeAsync(createdBy: RequesterUser);

        var response = await PostVerifyAsync(id, ApproverUser, "Approver");

        Assert.That((int)response.StatusCode, Is.LessThan(400));

        var payee = await LoadPayeeAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(payee.Status, Is.EqualTo(PayeeStatus.Verified));
            Assert.That(payee.VerifiedBy, Is.EqualTo(ApproverUser));
            Assert.That(payee.VerifiedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task Payee_without_a_tax_form_cannot_be_verified()
    {
        var id = await SeedPayeeAsync(createdBy: RequesterUser, taxFormOnFile: false);

        var response = await PostVerifyAsync(id, ApproverUser, "Approver");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await AssertPayeeStatusAsync(id, PayeeStatus.Unverified);
    }

    [Test]
    public async Task Disbursement_without_a_payee_is_rejected()
    {
        await PostRequestAsync(payeeId: null, RequestType.Disbursement);

        await AssertRequestCountAsync(0);
    }

    [Test]
    public async Task Disbursement_to_an_unverified_payee_is_rejected()
    {
        var id = await SeedPayeeAsync(createdBy: RequesterUser);

        await PostRequestAsync(id, RequestType.Disbursement);

        await AssertRequestCountAsync(0);
    }

    [Test]
    public async Task Disbursement_to_a_suspended_payee_is_rejected()
    {
        var id = await SeedPayeeAsync(createdBy: RequesterUser, status: PayeeStatus.Suspended);

        await PostRequestAsync(id, RequestType.Disbursement);

        await AssertRequestCountAsync(0);
    }

    [Test]
    public async Task Disbursement_to_a_verified_payee_is_created()
    {
        var id = await SeedPayeeAsync(createdBy: RequesterUser, status: PayeeStatus.Verified);

        await PostRequestAsync(id, RequestType.Disbursement);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.PaymentRequests.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.PayeeId, Is.EqualTo(id));
            Assert.That(saved.Type, Is.EqualTo(RequestType.Disbursement));
            Assert.That(saved.Status, Is.EqualTo(PaymentStatus.PendingApproval));
        });
    }

    // ---- helpers ----

    private async Task<int> SeedPayeeAsync(
        string createdBy,
        PayeeStatus status = PayeeStatus.Unverified,
        bool taxFormOnFile = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payee = new Payee
        {
            DisplayName = "Acme Consulting",
            Email = $"payee-{Guid.NewGuid():N}@test.local",
            Status = status,
            TaxFormOnFile = taxFormOnFile,
            PayoutDestinationMask = "••••4321",
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        db.Payees.Add(payee);
        await db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task<HttpResponseMessage> PostVerifyAsync(int id, string user, string roles)
    {
        var token = await GetTokenAsync("/Payees/Create", user, roles);

        var request = Request(HttpMethod.Post, $"/Payees/Verify/{id}", user, roles);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostRequestAsync(int? payeeId, RequestType type)
    {
        var getResponse = await Client.SendAsync(
            Request(HttpMethod.Get, "/PaymentRequests/Create", RequesterUser, "Requester"));

        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractField(html, "__RequestVerificationToken"),
            ["IdempotencyKey"] = ExtractField(html, "IdempotencyKey"),
            ["Amount"] = "19.99",
            ["Currency"] = "usd",
            ["Type"] = type.ToString()
        };

        if (payeeId is not null)
        {
            form["PayeeId"] = payeeId.Value.ToString();
        }

        var request = Request(HttpMethod.Post, "/PaymentRequests/Create", RequesterUser, "Requester");
        request.Content = new FormUrlEncodedContent(form);

        return await Client.SendAsync(request);
    }

    private async Task<string> GetTokenAsync(string url, string user, string roles)
    {
        var response = await Client.SendAsync(Request(HttpMethod.Get, url, user, roles));
        response.EnsureSuccessStatusCode();
        return ExtractToken(await response.Content.ReadAsStringAsync());
    }

    private async Task<Payee> LoadPayeeAsync(int id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Payees.SingleAsync(p => p.Id == id);
    }

    private async Task AssertPayeeStatusAsync(int id, PayeeStatus expected)
    {
        var payee = await LoadPayeeAsync(id);
        Assert.That(payee.Status, Is.EqualTo(expected));
    }

    private async Task AssertRequestCountAsync(int expected)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(await db.PaymentRequests.CountAsync(), Is.EqualTo(expected));
    }
}
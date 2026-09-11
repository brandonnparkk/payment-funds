using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PaymentFunds.Data;

namespace PaymentFunds.Tests;

[TestFixture]
public class PaymentRequestCreateTests : IntegrationTestBase
{
    [Test]
    public async Task Submitting_the_same_form_twice_creates_one_request()
    {
        var form = await LoadCreateFormAsync();

        var first = await PostFormAsync(form);
        var second = await PostFormAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That((int)first.StatusCode, Is.LessThan(400));
            Assert.That((int)second.StatusCode, Is.LessThan(400));
        });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var saved = await db.PaymentRequests.SingleAsync();
        Assert.That(saved.RequestedBy, Is.EqualTo(RequesterUser));
    }

    [Test]
    public async Task Two_separate_forms_create_two_requests()
    {
        await PostFormAsync(await LoadCreateFormAsync());
        await PostFormAsync(await LoadCreateFormAsync());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.That(await db.PaymentRequests.CountAsync(), Is.EqualTo(2));
    }

    private async Task<Dictionary<string, string>> LoadCreateFormAsync()
    {
        var response = await Client.SendAsync(
            Request(HttpMethod.Get, "/PaymentRequests/Create", RequesterUser, "Requester"));

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        return new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractField(html, "__RequestVerificationToken"),
            ["IdempotencyKey"] = ExtractField(html, "IdempotencyKey"),
            ["Amount"] = "19.99",
            ["Currency"] = "usd"
        };
    }

    private async Task<HttpResponseMessage> PostFormAsync(Dictionary<string, string> form)
    {
        var request = Request(
            HttpMethod.Post, "/PaymentRequests/Create", RequesterUser, "Requester");

        request.Content = new FormUrlEncodedContent(form);
        return await Client.SendAsync(request);
    }
}

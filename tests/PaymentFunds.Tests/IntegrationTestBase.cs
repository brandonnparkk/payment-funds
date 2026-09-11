using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentFunds.Data;
namespace PaymentFunds.Tests;

public abstract class IntegrationTestBase
{
    protected PaymentFundsFactory Factory = null!;
    protected HttpClient Client = null!;
    protected const string RequesterUser = "requester@test.local";
    protected const string ApproverUser = "approver@test.local";

    protected static HttpRequestMessage Request(
        HttpMethod method, string url, string? user = null, string? roles = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user);
            if (roles is not null) request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        }
        return request;
    }

    protected static string ExtractToken(string html) =>
        ExtractField(html, "__RequestVerificationToken");

    protected static string ExtractField(string html, string name) =>
        System.Text.RegularExpressions.Regex.Match(
            html, $"""name="{name}"[^>]*value="([^"]+)""").Groups[1].Value;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(PaymentFundsFactory.TestConnectionString)
        .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        Factory = new PaymentFundsFactory();
        Client = Factory.CreateClient();
    }

    [SetUp]
    public async Task ResetDatabase()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE "PaymentRequests", "ProcessedStripeEvents" RESTART IDENTITY CASCADE""");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
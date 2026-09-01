using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PaymentFunds.Data;

namespace PaymentFunds.Tests;

public abstract class IntegrationTestBase
{
    protected PaymentFundsFactory Factory = null!;
    protected HttpClient Client = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Factory = new PaymentFundsFactory();
        Client = Factory.CreateClient();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
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
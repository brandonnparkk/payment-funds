using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PaymentFunds.Payments;

namespace PaymentFunds.Tests;

public class PaymentFundsFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Not a Stripe secret. An arbitrary constant used to sign payloads the tests
    /// generate themselves, so signature verification can be exercised offline.
    /// </summary>
    public const string WebhookSecret = "whsec_fake_value_used_only_by_tests";

    private const string ConnectionStringKey = "ConnectionStrings:TestDatabase";

    private static readonly IConfiguration TestConfiguration =
        new ConfigurationBuilder()
            .AddUserSecrets<PaymentFundsFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>
    /// Read from configuration with no hardcoded fallback, so credentials never enter
    /// source control and a missing value fails loudly instead of silently connecting
    /// to an unexpected database.
    /// </summary>
    public static string TestConnectionString =>
        TestConfiguration[ConnectionStringKey]
        ?? throw new InvalidOperationException(
            $"""
            The test database connection string is not configured.

            Set it with user secrets:
                dotnet user-secrets set "{ConnectionStringKey}" "Host=localhost;Port=5432;Database=paymentfunds_test;Username=<user>;Password=<password>" --project tests/PaymentFunds.Tests

            Or with an environment variable:
                export ConnectionStrings__TestDatabase="Host=localhost;..."
            """);

    public FakePaymentProcessor Processor { get; } =
        new(PaymentResult.Succeeded("pi_fake_123"));
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedIdentity"] = "false",
                ["ConnectionStrings:DefaultConnection"] =
                    TestConnectionString,
                ["Stripe:WebhookSecret"] = WebhookSecret,
                ["Stripe:SecretKey"] = "sk_test_unused",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.Configure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IPaymentProcessor>();
            services.AddSingleton<IPaymentProcessor>(Processor);
        });
    }
}
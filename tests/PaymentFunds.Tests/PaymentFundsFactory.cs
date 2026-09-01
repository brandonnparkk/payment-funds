using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PaymentFunds.Payments;

namespace PaymentFunds.Tests;

public class PaymentFundsFactory : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "whsec_integration_test_secret";

    public FakePaymentProcessor Processor { get; } =
        new(PaymentResult.Succeeded("pi_fake_123"));
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=paymentfunds_test;Username=brandon;Password=devpassword",
                ["Stripe:WebhookSecret"] = WebhookSecret,
                ["Stripe:SecretKey"] = "sk_test_unused",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IPaymentProcessor>();
            services.AddSingleton<IPaymentProcessor>(Processor);
        });
    }
}
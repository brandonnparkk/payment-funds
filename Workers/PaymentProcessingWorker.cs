using Microsoft.EntityFrameworkCore;
using PaymentFunds.Data;
using PaymentFunds.Models;
using Stripe;

namespace PaymentFunds.Workers;

public class PaymentProcessingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PaymentProcessingWorker> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public PaymentProcessingWorker(
        IServiceProvider services,
        ILogger<PaymentProcessingWorker> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                try {
                    await ProcessApprovedRequestsAsync(stoppingToken);
                } catch (ex) {
                    _logger.LogError(ex, "Unhandled error in payment processing loop");
                }
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        private async Task ProcessApprovedRequestsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ids = await db.PaymentRequests
                .Where(pr => pr.Status == PaymentStatus.Approved)
                .OrderBy(pr => pr.ApprovedAt)
                .Select(pr => pr.Id)
                .Take(10)
                .ToListAsync(ct);

            foreach (var id in ids)
            {
                var claimed = await db.PaymentRequests
                    .Where(pr => pr.Id == id && pr.Status == PaymentStatus.Approved)
                    .ExecuteUpdateAsync(
                        set => set.SetProperty(pr => pr.Status, PaymentStatus.Processing), ct);

                    if (claimed == 0)
                    {
                        continue;
                    }

                    await ProcessOneAsync(id, ct);
            }

        }

        private async Task ProcessOneAsync(int id, CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var request = await db.PaymentRequests.AsTracking().FirstAsync(pr => pr.Id == id, ct);
            try {
                var intents = new PaymentIntentService();
                var intent = await intents.CreateAsync(
                    new PaymentIntentCreateOptions {
                        Amount = (long)(request.Amount * 100),
                        Currency = request.Currency.ToLowerInvariant(),
                        PaymentMethodTypes = new List<string> { "card" },
                        Description = $"Payment for request {request.Id}",
                    },
                    new RequestOptions {
                        IdempotencyKey = request.IdempotencyKey,
                    }, ct);
                
                request.StripePaymentIntentId = intent.Id;
                request.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("Created Payment Intent {IntentId} for request {RequestId}", intent.Id, request.Id);
                
            } catch (StripeException ex) {
                _logger.LogError(ex, "Stripe call failed for request {RequestId}", request.Id);
                request.Status = PaymentStatus.Failed;
                await db.SaveChangesAsync(ct);
            }
        }
    
}
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Data;
using PaymentFunds.Models;
using PaymentFunds.Payments;

namespace PaymentFunds.Workers;

public class PaymentProcessingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PaymentProcessingWorker> _logger;
    private readonly IPaymentProcessor _paymentProcessor;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public PaymentProcessingWorker(
        IServiceProvider services,
        IPaymentProcessor paymentProcessor,
        ILogger<PaymentProcessingWorker> logger)
        {
            _services = services;
            _paymentProcessor = paymentProcessor;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                try {
                    await ProcessApprovedRequestsAsync(stoppingToken);
                } catch (Exception ex) {
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
            var result = await _paymentProcessor.CreatePaymentAsync(
                new PaymentInstruction(
                    request.Amount,
                    request.Currency,
                    request.IdempotencyKey,
                    $"Payment for request {request.Id}"
                ),
                ct
                );
            
            if (result.Success)
            {
                request.StripePaymentIntentId = result.ProviderReference;
                request.ProcessedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "Created payment {Reference} for request {RequestId}",
                    result.ProviderReference,
                    request.Id);
            }
            else
            {
                request.Status = PaymentStatus.Failed;
                _logger.LogError(
                    "Payment failed for request {RequestId}: {Reason}",
                    request.Id,
                    result.FailureReason);
            }

            await db.SaveChangesAsync(ct);
        }
    
}
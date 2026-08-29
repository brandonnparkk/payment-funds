using Stripe;

namespace PaymentFunds.Payments;

public class StripePaymentProcessor : IPaymentProcessor
{
    private readonly PaymentIntentService _intents = new();

    public async Task<PaymentResult> CreatePaymentAsync(
        PaymentInstruction instruction, CancellationToken ct)
    {
        try
        {
            var intent = await _intents.CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = (long)(instruction.Amount * 100),
                    Currency = instruction.Currency.ToLowerInvariant(),
                    PaymentMethodTypes = new List<string> { "card" },
                    PaymentMethod = "pm_card_visa",
                    Confirm = true,
                    Description = instruction.Description
                },
                new RequestOptions { IdempotencyKey = instruction.IdempotencyKey }, ct);
            
            return PaymentResult.Succeeded(intent.Id);
        }
        catch (StripeException ex)
        {
            return PaymentResult.Failed(ex.Message);
        }
    }
}
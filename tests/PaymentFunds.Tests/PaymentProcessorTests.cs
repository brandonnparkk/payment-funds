using NUnit.Framework;
using PaymentFunds.Payments;

namespace PaymentFunds.Tests;

public class FakePaymentProcessor : IPaymentProcessor
{
    private readonly PaymentResult _result;
    public PaymentInstruction? LastInstruction { get; private set; }

    public FakePaymentProcessor(PaymentResult result) => _result = result;

    public Task<PaymentResult> CreatePaymentAsync(PaymentInstruction instruction, CancellationToken cancellationToken)
    {
        LastInstruction = instruction;
        return Task.FromResult(_result);
    }
}

[TestFixture]
public class PaymentProcessorTests
{
    [Test]
    public async Task Fake_processor_receives_the_idempotency_key()
    {
        var fake = new FakePaymentProcessor(PaymentResult.Succeeded("pi_test_123"));

        var result = await fake.CreatePaymentAsync(
            new PaymentInstruction(10.99m, "usd", "key-abc", "test"),
            CancellationToken.None);
        
        Assert.Multiple(() => 
        {
            Assert.That(result.Success, Is.True);
            Assert.That(fake.LastInstruction?.IdempotencyKey, Is.EqualTo("key-abc"));
        });
    }
}
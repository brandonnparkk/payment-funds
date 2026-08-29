namespace PaymentFunds.Payments;

public interface IPaymentProcessor
{
    Task<PaymentResult> CreatePaymentAsync(PaymentInstruction instruction, CancellationToken ct);
}
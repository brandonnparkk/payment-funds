namespace PaymentFunds.Payments;

public record PaymentInstruction(
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string Description);
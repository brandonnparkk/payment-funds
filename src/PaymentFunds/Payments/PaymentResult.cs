namespace PaymentFunds.Payments;

public record PaymentResult(
    bool Success,
    string? ProviderReference,
    string? FailureReason)
{
    public static PaymentResult Succeeded(string reference) => new(true, reference, null);
    public static PaymentResult Failed(string reason) => new(false, null, reason);
}
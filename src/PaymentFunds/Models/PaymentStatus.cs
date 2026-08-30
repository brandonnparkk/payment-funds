namespace PaymentFunds.Models;

public enum PaymentStatus
{
    Created,
    PendingApproval,
    Approved,
    Rejected,
    Processing,
    Completed,
    Failed
}
namespace PaymentFunds.Models;

public class DashboardViewModel
{
  public int PendingApproval { get; set; }
  public int Approved { get; set; }
  public int Processing { get; set; }
  public int Completed { get; set; }
  public int Failed { get; set; }

  public int AwaitingMyApproval { get; set; }
  public int PayeesAwaitingVerification { get; set; }

  public List<PaymentRequest> Recent { get; set; } = [];
}
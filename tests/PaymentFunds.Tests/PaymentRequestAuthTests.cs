using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PaymentFunds.Data;
using PaymentFunds.Models;

namespace PaymentFunds.Tests;

[TestFixture]
public class PaymentRequestAuthTests : IntegrationTestBase
{
  [Test]
  public async Task Anonymous_cannot_list_requests()
  {
    var response = await Client.SendAsync(
        Request(HttpMethod.Get, "/PaymentRequests"));

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
  }

  [Test]
  public async Task Anonymous_cannot_open_the_create_form()
  {
    var response = await Client.SendAsync(
        Request(HttpMethod.Get, "/PaymentRequests/Create"));

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
  }

  [Test]
  public async Task Requester_cannot_approve()
  {
    var id = await SeedPendingRequestAsync("someone.else@test.local");

    var response = await PostApproveAsync(id, RequesterUser, "Requester");

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    await AssertStatusAsync(id, PaymentStatus.PendingApproval);
  }

  [Test]
  public async Task Approver_cannot_approve_their_own_request()
  {
    var id = await SeedPendingRequestAsync(ApproverUser);

    var response = await PostApproveAsync(id, ApproverUser, "Approver");

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    await AssertStatusAsync(id, PaymentStatus.PendingApproval);
  }

  [Test]
  public async Task Approver_can_approve_someone_elses_request()
  {
    var id = await SeedPendingRequestAsync(RequesterUser);

    var response = await PostApproveAsync(id, ApproverUser, "Approver");

    Assert.That((int)response.StatusCode, Is.LessThan(400));

    var saved = await LoadAsync(id);
    Assert.Multiple(() =>
    {
      Assert.That(saved.Status, Is.EqualTo(PaymentStatus.Approved));
      Assert.That(saved.ApprovedBy, Is.EqualTo(ApproverUser));
      Assert.That(saved.ApprovedAt, Is.Not.Null);
    });
  }

  [Test]
  public async Task Approver_can_reject_someone_elses_request()
  {
    var id = await SeedPendingRequestAsync(RequesterUser);

    var response = await PostAsync("Reject", id, ApproverUser, "Approver");

    Assert.That((int)response.StatusCode, Is.LessThan(400));

    var saved = await LoadAsync(id);
    Assert.Multiple(() =>
    {
      Assert.That(saved.Status, Is.EqualTo(PaymentStatus.Rejected));
      Assert.That(saved.RejectedBy, Is.EqualTo(ApproverUser));
    });
  }

  [Test]
  public async Task Already_approved_request_cannot_be_approved_again()
  {
    var id = await SeedPendingRequestAsync(RequesterUser);
    await PostApproveAsync(id, ApproverUser, "Approver");

    var second = await PostApproveAsync(id, ApproverUser, "Approver");

    Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    await AssertStatusAsync(id, PaymentStatus.Approved);
  }

  // ---- helpers ----

  private Task<HttpResponseMessage> PostApproveAsync(int id, string user, string roles)
      => PostAsync("Approve", id, user, roles);

  private async Task<HttpResponseMessage> PostAsync(
      string action, int id, string user, string roles)
  {
    var token = await GetAntiforgeryTokenAsync(user, roles);

    var request = Request(HttpMethod.Post, $"/PaymentRequests/{action}/{id}", user, roles);
    request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
      ["__RequestVerificationToken"] = token
    });

    return await Client.SendAsync(request);
  }

  private async Task<string> GetAntiforgeryTokenAsync(string user, string roles)
  {
    var response = await Client.SendAsync(
        Request(HttpMethod.Get, "/PaymentRequests/Create", user, roles));

    response.EnsureSuccessStatusCode();
    return ExtractToken(await response.Content.ReadAsStringAsync());
  }

  private async Task<int> SeedPendingRequestAsync(string requestedBy)
  {
    using var scope = Factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var request = new PaymentRequest
    {
      IdempotencyKey = Guid.NewGuid().ToString(),
      Amount = 19.99m,
      Currency = "usd",
      Status = PaymentStatus.PendingApproval,
      RequestedBy = requestedBy,
      CreatedAt = DateTime.UtcNow
    };

    db.PaymentRequests.Add(request);
    await db.SaveChangesAsync();
    return request.Id;
  }

  private async Task<PaymentRequest> LoadAsync(int id)
  {
    using var scope = Factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    return await db.PaymentRequests.SingleAsync(r => r.Id == id);
  }

  private async Task AssertStatusAsync(int id, PaymentStatus expected)
  {
    var saved = await LoadAsync(id);
    Assert.That(saved.Status, Is.EqualTo(expected));
  }
}
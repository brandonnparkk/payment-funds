using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Models;
using PaymentFunds.Data;

namespace PaymentFunds.Controllers;

public class PaymentRequestsController : Controller
{
    private readonly ApplicationDbContext _context;

    public PaymentRequestsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var requests = await _context.PaymentRequests
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        
        return View(requests);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePaymentRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var paymentRequest = new PaymentRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Amount = model.Amount,
            Currency = model.Currency,
            RequestedBy = model.RequestedBy,
            Status = PaymentStatus.PendingApproval,
            CreatedAt = DateTime.UtcNow
        };

        _context.PaymentRequests.Add(paymentRequest);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var request = await _context.PaymentRequests.FindAsync(id);
        if (request == null) return NotFound();
        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string approvedBy)
    {
        var request = await _context.PaymentRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != PaymentStatus.PendingApproval)
        {
            return BadRequest("Only pending requests can be approved");
        }

        request.Status = PaymentStatus.Approved;
        request.ApprovedBy = approvedBy;
        request.ApprovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string rejectedBy)
    {
        var request = await _context.PaymentRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != PaymentStatus.PendingApproval)
        {
            return BadRequest("Only pending requests can be rejected");
        }

        request.Status = PaymentStatus.Rejected;
        request.RejectedBy = rejectedBy;
        request.RejectedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }
}
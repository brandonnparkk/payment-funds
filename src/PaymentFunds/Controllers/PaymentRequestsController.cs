using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Models;
using PaymentFunds.Data;
using Npgsql;
using Microsoft.AspNetCore.Authorization;

namespace PaymentFunds.Controllers;

[Authorize]
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
        return View(new CreatePaymentRequestViewModel
        {
            IdempotencyKey = Guid.NewGuid().ToString()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePaymentRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await _context.PaymentRequests.
            FirstOrDefaultAsync(r => r.IdempotencyKey == model.IdempotencyKey);

        if (existing != null)
        {
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }

        var paymentRequest = new PaymentRequest
        {
            IdempotencyKey = model.IdempotencyKey,
            Amount = model.Amount,
            Currency = model.Currency,
            RequestedBy = User.Identity!.Name!,
            Status = PaymentStatus.PendingApproval,
            CreatedAt = DateTime.UtcNow
        };

        _context.PaymentRequests.Add(paymentRequest);

        try {
            await _context.SaveChangesAsync();
        } catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            var winner = await _context.PaymentRequests.
                FirstAsync(r => r.IdempotencyKey == model.IdempotencyKey);
            return RedirectToAction(nameof(Details), new { id = winner.Id });
        }

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
    [Authorize(Roles = "Approver")]
    public async Task<IActionResult> Approve(int id)
    {
        var request = await _context.PaymentRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != PaymentStatus.PendingApproval)
        {
            return BadRequest("Only pending requests can be approved");
        }
        if (request.RequestedBy == User.Identity!.Name)
        {
            return Forbid();
        }

        request.Status = PaymentStatus.Approved;
        request.ApprovedBy = User.Identity!.Name;
        request.ApprovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Approver")]
    public async Task<IActionResult> Reject(int id)
    {
        var request = await _context.PaymentRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != PaymentStatus.PendingApproval)
        {
            return BadRequest("Only pending requests can be rejected");
        }

        request.Status = PaymentStatus.Rejected;
        request.RejectedBy = User.Identity!.Name;
        request.RejectedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }
}
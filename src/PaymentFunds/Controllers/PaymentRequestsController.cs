using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Models;
using PaymentFunds.Data;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PaymentFunds.Controllers;

[Authorize]
public class PaymentRequestsController : Controller
{
    private readonly ApplicationDbContext _context;

    public PaymentRequestsController(ApplicationDbContext context)
    {
        _context = context;
    }

        public async Task<IActionResult> Index(PaymentStatus? status)
    {
        var query = _context.PaymentRequests
            .Include(r => r.Payee)
            .AsQueryable();

        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        ViewData["StatusFilter"] = status;

        return View(await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        return View(new CreatePaymentRequestViewModel
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            AvailablePayees = await VerifiedPayeeOptionsAsync()
        });
    }

    private async Task<List<SelectListItem>> VerifiedPayeeOptionsAsync() =>
        await _context.Payees
            .Where(p => p.Status == PayeeStatus.Verified)
            .OrderBy(p => p.DisplayName)
            .Select(p => new SelectListItem
            {
                Value = p.Id.ToString(),
                Text = p.DisplayName + " (" + (p.PayoutDestinationMask ?? "no destination") + ")"
            })
            .ToListAsync();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePaymentRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailablePayees = await VerifiedPayeeOptionsAsync();
            return View(model);
        }

        Payee? payee = null;

        if (model.Type == RequestType.Disbursement)
        {
            if (model.PayeeId is null)
            {
                ModelState.AddModelError(nameof(model.PayeeId), "A disbursement requires a payee.");
                model.AvailablePayees = await VerifiedPayeeOptionsAsync();
                return View(model);
            }
            
            payee = await _context.Payees.FindAsync(model.PayeeId.Value);

            if (payee is null || payee.Status != PayeeStatus.Verified)
            {
                ModelState.AddModelError(nameof(model.PayeeId), "That payee is not verified.");
                model.AvailablePayees = await VerifiedPayeeOptionsAsync();
                return View(model);
            }
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
            CreatedAt = DateTime.UtcNow,
            Type = model.Type,
            PayeeId = payee?.Id
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
        var request = await _context.PaymentRequests
            .Include(r => r.Payee)
            .FirstOrDefaultAsync(r => r.Id == id);
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
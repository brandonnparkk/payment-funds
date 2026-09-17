using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Models;
using PaymentFunds.Data;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace PaymentFunds.Controllers;

[Authorize]
public class PayeesController : Controller
{
  private readonly ApplicationDbContext _context;
  
  public PayeesController(ApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<IActionResult> Index()
  {
    var payees = await _context.Payees
      .OrderBy(p => p.DisplayName)
      .ToListAsync();

    return View(payees);
  }

  public IActionResult Create()
  {
    return View();
  }

  [HttpPost]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> Create(CreatePayeeViewModel model)
  {
    if (!ModelState.IsValid)
    {
      return View(model);
    }

    if (await _context.Payees.AnyAsync(p => p.Email == model.Email))
    {
      ModelState.AddModelError(nameof(model.Email), "A Payee with that email already exists.");
      return View(model);
    }
    
    _context.Payees.Add(new Payee
    {
      DisplayName = model.DisplayName,
      Email = model.Email,
      PayoutDestinationMask = $"••••{model.PayoutLastFour}",
      TaxFormOnFile = model.TaxFormOnFile,
      Status = PayeeStatus.Unverified,
      CreatedBy = User.Identity!.Name!,
      CreatedAt = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    return RedirectToAction(nameof(Index));
  }

  [HttpPost]
  [Authorize(Roles = "Approver")]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> Verify(int id)
  {
    var payee = await _context.Payees.FindAsync(id);
    if (payee == null) return NotFound();

    if (payee.Status != PayeeStatus.Unverified)
    {
      return BadRequest("Only unverified payees can be verified.");
    }

    if (payee.CreatedBy == User.Identity!.Name)
    {
      return Forbid();
    }

    if (!payee.TaxFormOnFile)
    {
      return BadRequest("A tax form must be on file before verification.");
    }

    payee.Status = PayeeStatus.Verified;
    payee.VerifiedBy = User.Identity!.Name;
    payee.VerifiedAt = DateTime.UtcNow;
    await _context.SaveChangesAsync();

    return RedirectToAction(nameof(Index));
  }

  [HttpPost]
  [Authorize(Roles = "Approver")]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> Suspend(int id)
  {
    var payee = await _context.Payees.FindAsync(id);
    if (payee == null) return NotFound();

    payee.Status = PayeeStatus.Suspended;
    await _context.SaveChangesAsync();

    return RedirectToAction(nameof(Index));
  }
}
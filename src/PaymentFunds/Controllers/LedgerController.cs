using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Models;
using PaymentFunds.Ledger;
using PaymentFunds.Data;

namespace PaymentFunds.Controllers;

[Authorize]
public class LedgerController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILedgerService _ledger;

    public LedgerController(ApplicationDbContext context, ILedgerService ledger)
    {
        _context = context;
        _ledger = ledger;
    }

    public async Task<IActionResult> Index(string currency = "usd")
    {
        var accounts = await _context.LedgerAccounts
            .Where(a => a.Currency == currency)
            .OrderBy(a => a.Code)
            .ToListAsync();
        
        var balances = new List<AccountBalance>();

        foreach (var account in accounts)
        {
            balances.Add(new AccountBalance(
                account.Code,
                account.Name,
                account.Type,
                await _ledger.BalanceMinorAsync(account.Code, currency))
            );
        }

        return View(new LedgerViewModel
        {
            Currency = currency,
            AvailableMinor = await _ledger.AvailableFundsMinorAsync(currency),
            Balances = balances,
            Entries = await _context.LedgerEntries
                .Include(e => e.Account)
                .Where(e => e.Currency == currency)
                .OrderByDescending(e => e.Id)
                .Take(50)
                .ToListAsync()
        });
    }

    [HttpPost]
    [Authorize(Roles = "Approver")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fund(string currency, decimal amount)
    {
        if (amount <= 0)
        {
            TempData["Error"] = "Funding amount must be greater than zero.";
            return RedirectToAction(nameof(Index), new { currency });
        }

        var minor = ILedgerService.ToMinorUnits(amount);

        await _ledger.AddPostingAsync(new LedgerPosting(
            currency,
            $"Platform funded by {User.Identity!.Name}",
            null,
            [
                new LedgerLine("CASH", EntryDirection.Debit, minor),
                new LedgerLine("OPENING", EntryDirection.Credit, minor)
            ]
        ));

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { currency });
    }
}


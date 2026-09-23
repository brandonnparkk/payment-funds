using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Data;
using PaymentFunds.Models;

namespace PaymentFunds.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var me = User.Identity!.Name;

        var counts = await _context.PaymentRequests
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int CountOf(PaymentStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return View(new DashboardViewModel
        {
            PendingApproval = CountOf(PaymentStatus.PendingApproval),
            Approved = CountOf(PaymentStatus.Approved),
            Processing = CountOf(PaymentStatus.Processing),
            Completed = CountOf(PaymentStatus.Completed),
            Failed = CountOf(PaymentStatus.Failed),

            AwaitingMyApproval = User.IsInRole("Approver")
                ? await _context.PaymentRequests.CountAsync(r =>
                    r.Status == PaymentStatus.PendingApproval && r.RequestedBy != me)
                : 0,

            PayeesAwaitingVerification = User.IsInRole("Approver")
                ? await _context.Payees.CountAsync(p =>
                    p.Status == PayeeStatus.Unverified && p.CreatedBy != me)
                : 0,

            Recent = await _context.PaymentRequests
                .Include(r => r.Payee)
                .OrderByDescending(r => r.CreatedAt)
                .Take(5)
                .ToListAsync()
        });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}

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
        
        return View(requests)
    }
}
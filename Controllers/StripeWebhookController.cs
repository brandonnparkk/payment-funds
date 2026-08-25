using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentFunds.Data;
using PaymentFunds.Models;
using Stripe;

namespace PaymentFunds.Controllers;

[ApiController]
[Route("api/stripe/webhook")]
public class StripeWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<StripeWebhookController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }
    
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Handle()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"];
        var secret = _configuration["Stripe:WebhookSecret"];

        Event stripeEvent;
        try {
            stripeEvent = EventUtility.ConstructEvent(json, signature, secret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Webhook signature verification failed");
            return BadRequest(new { error = "Invalid signature" });
        }

        var alreadyProcessed = await _context.ProcessedStripeEvents
            .AnyAsync(e => e.EventId == stripeEvent.Id);
        
        if (alreadyProcessed)
        {
            _logger.LogInformation("Ignoring duplicate event: {EventId}", stripeEvent.Id);
            return Ok();
        }

        if (stripeEvent.Data.Object is PaymentIntent intent)
        {
            var request = await _context.PaymentRequests
                .FirstOrDefaultAsync(r => r.StripePaymentIntentId == intent.Id);

            if (request != null)
            {
                switch (stripeEvent.Type)
                {
                    case "payment_intent.succeeded":
                        request.Status = PaymentStatus.Completed;
                        request.ProcessedAt = DateTime.UtcNow;
                        break;

                    case "payment_intent.payment_failed":
                        request.Status = PaymentStatus.Failed;
                        break;

                    default:
                        _logger.LogInformation("Unhandled event type: {EventType}", stripeEvent.Type);
                        break;
                }
            }
            else
            {
                _logger.LogInformation("No local request matches PaymentIntent {IntentId}", intent.Id);
            }
        }

        _context.ProcessedStripeEvents.Add(new ProcessedStripeEvent
        {
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
        });

        await _context.SaveChangesAsync();
        return Ok();
    }
}
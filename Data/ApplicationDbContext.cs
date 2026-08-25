using Microsoft.EntityFrameworkCore;
using PaymentFunds.Models;

namespace PaymentFunds.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) {}

    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentRequest>()
            .HasIndex(p => p.IdempotencyKey)
            .IsUnique();
        
        modelBuilder.Entity<ProcessedStripeEvent>()
            .HasIndex(e => e.EventId)
            .IsUnique();
    }

    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
}
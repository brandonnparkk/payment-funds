using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using PaymentFunds.Models;

namespace PaymentFunds.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<Payee> Payees => Set<Payee>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentRequest>()
            .HasIndex(p => p.IdempotencyKey)
            .IsUnique();

        modelBuilder.Entity<ProcessedStripeEvent>()
            .HasIndex(e => e.EventId)
            .IsUnique();

        modelBuilder.Entity<Payee>()
            .HasIndex(p => p.Email)
            .IsUnique();

        modelBuilder.Entity<PaymentRequest>()
            .HasOne(r => r.Payee)
            .WithMany(p => p.PaymentRequests)
            .HasForeignKey(r => r.PayeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LedgerAccount>()
            .HasIndex(a => new { a.Code, a.Currency })
            .IsUnique();

        modelBuilder.Entity<LedgerEntry>()
            .HasIndex(e => e.TransactionId);

        modelBuilder.Entity<LedgerEntry>()
            .HasIndex(e => e.PaymentRequestId);

        modelBuilder.Entity<LedgerEntry>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_LedgerEntry_PositiveAmount", "\"AmountMinor\" > 0"));

        modelBuilder.Entity<LedgerEntry>()
            .HasOne(e => e.Account)
            .WithMany(a => a.Entries)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LedgerEntry>()
            .HasOne(e => e.PaymentRequest)
            .WithMany()
            .HasForeignKey(e => e.PaymentRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

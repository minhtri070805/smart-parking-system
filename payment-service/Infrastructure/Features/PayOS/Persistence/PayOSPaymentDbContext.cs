using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Features.PayOS.Persistence;

public sealed class PayOSPaymentDbContext(DbContextOptions<PayOSPaymentDbContext> options) : DbContext(options)
{
    public DbSet<PayOSPaymentAttempt> PaymentAttempts => Set<PayOSPaymentAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PayOSPaymentDbContext).Assembly);
    }
}

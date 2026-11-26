using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Infrastructure.Features.PayOS.Configurations;

namespace Infrastructure.Features.PayOS.Persistence;

public sealed class PayOSPaymentDbContextFactory : IDesignTimeDbContextFactory<PayOSPaymentDbContext>
{
    public PayOSPaymentDbContext CreateDbContext(string[] args)
    {
        var current = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(current, "payment-service", "API"),
            Path.GetFullPath(Path.Combine(current, "..", "API")),
            current
        };
        var basePath = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "appsettings.json"))) ?? current;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddDotEnvFile(Path.GetFullPath(Path.Combine(basePath, "..", ".env")))
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection before creating the PayOS payment database context.");
        var options = new DbContextOptionsBuilder<PayOSPaymentDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__PayOSSubscriptionMigrationsHistory"))
            .Options;
        return new PayOSPaymentDbContext(options);
    }
}

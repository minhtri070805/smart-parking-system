using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using recognition.Persistence.Data;

namespace recognition.Persistence;

public static class PersistenceDependencyInjection
{
    public static IServiceCollection AddRecognitionPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing for Recognition Persistence.");

        services.AddDbContext<AiRecognitionDbContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(AiRecognitionDbContext).Assembly.FullName)));

        return services;
    }
}

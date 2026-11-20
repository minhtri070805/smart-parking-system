using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingOperations.Persistence.Data;

namespace ParkingOperations.Persistence;

public static class PersistenceDependencyInjection
{
    public static IServiceCollection AddOperationsPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection to the shared Smart Parking database.");

        services.AddDbContext<OperationsDbContext>(options => options.UseNpgsql(connection,
            npgsql => npgsql.MigrationsHistoryTable("__OperationsMigrationsHistory")));
        return services;
    }
}

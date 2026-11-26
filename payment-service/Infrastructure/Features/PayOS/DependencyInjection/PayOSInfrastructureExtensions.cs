using Application.Interfaces.PayOS;
using Infrastructure.Features.PayOS.Clients;
using Infrastructure.Features.PayOS.Configurations;
using Infrastructure.Features.PayOS.Payments;
using Infrastructure.Features.PayOS.Persistence;
using Infrastructure.Features.PayOS.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.Features.PayOS.DependencyInjection;

public static class PayOSInfrastructureExtensions
{
    public static IServiceCollection AddPayOSInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayOSOptions>(configuration.GetSection(PayOSOptions.SectionName));
        services.AddDbContext<PayOSPaymentDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
                postgres => postgres.MigrationsHistoryTable("__PayOSSubscriptionMigrationsHistory")));
        services.AddScoped<IPayOSPaymentAttemptRepository, PayOSPaymentAttemptRepository>();
        services.AddScoped<IPayOSCheckoutProvider, PayOSCheckoutProvider>();
        services.AddHttpClient<ICoreSubscriptionClient, CoreSubscriptionClient>();
        return services;
    }
}

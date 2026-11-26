using Application.Interfaces.PayOS;
using Application.Services.PayOS;
using API.Configurations;
using API.Features.PayOS.BackgroundServices;
using Infrastructure.Features.PayOS.DependencyInjection;

namespace API.Features.PayOS.DependencyInjection;

public static class PayOSServiceCollectionExtensions
{
    public static IServiceCollection AddPayOSFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPayOSInfrastructure(configuration);
        services.AddScoped<IPayOSSubscriptionPaymentService, PayOSSubscriptionPaymentService>();
        services.AddOptions<PayOSConfirmationWorkerOptions>()
            .Bind(configuration.GetSection(PayOSConfirmationWorkerOptions.SectionName))
            .Validate(options => options.ConfirmationPollIntervalSeconds is >= 15 and <= 300,
                "PayOSWorker:ConfirmationPollIntervalSeconds must be between 15 and 300.")
            .ValidateOnStart();
        services.AddHostedService<PayOSCoreConfirmationWorker>();
        return services;
    }
}

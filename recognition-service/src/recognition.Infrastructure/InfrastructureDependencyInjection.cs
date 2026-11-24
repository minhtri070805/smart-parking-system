using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using recognition.Application.Common.Interfaces;
using recognition.Infrastructure.Clients;
using recognition.Infrastructure.Services;
using recognition.Persistence;

namespace recognition.Infrastructure;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddRecognitionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecognitionPersistence(configuration);

        var aiUrl = configuration["AiService:BaseUrl"] ?? "https://aiservice-production-9c362d03.up.railway.app/";
        services.AddHttpClient<IAiServiceClient, AiServiceClient>(client =>
        {
            client.BaseAddress = new Uri(aiUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        var coreUrl = configuration["CoreApi:BaseUrl"] ?? "http://localhost:5295/";
        services.AddHttpClient<ICoreContextClient, CoreContextClient>(client =>
        {
            client.BaseAddress = new Uri(coreUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<IStaffDetectionService, StaffDetectionService>();

        return services;
    }
}

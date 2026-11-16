using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.UseCases.CashHandovers;
using ParkingOperations.Application.UseCases.CashReconciliation;
using ParkingOperations.Application.UseCases.ShiftManagement;
using ParkingOperations.Persistence;

namespace ParkingOperations.Infrastructure;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddOperationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOperationsPersistence(configuration);
        services.AddScoped<ICashHandoverService, CashHandoverService>();
        services.AddScoped<IParkingShiftService, ParkingShiftService>();
        services.AddScoped<ICashReconciliationService, CashReconciliationService>();

        var coreBaseUrl = configuration["CoreApi:BaseUrl"];
        if (!Uri.TryCreate(coreBaseUrl, UriKind.Absolute, out var coreUri) ||
            (coreUri.Scheme != Uri.UriSchemeHttps && !(coreUri.Scheme == Uri.UriSchemeHttp && coreUri.IsLoopback)) ||
            !string.IsNullOrEmpty(coreUri.UserInfo) || coreUri.Query.Length > 0 || coreUri.Fragment.Length > 0)
            throw new InvalidOperationException("CoreApi:BaseUrl must be an HTTPS service origin (HTTP is allowed only on loopback).");

        services.AddHttpClient<ICoreStaffAccessClient, CoreStaffAccessClient>(client =>
        {
            client.BaseAddress = new Uri(coreUri.ToString().TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        return services;
    }
}

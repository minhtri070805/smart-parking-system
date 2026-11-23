using Microsoft.Extensions.Configuration;
using ParkingOperations.Application.Common.Interfaces;

namespace ParkingOperations.Infrastructure.Services;

public class RecognitionRouteConfig(IConfiguration configuration) : IRecognitionRouteConfig
{
    public RecognitionRouteMode Mode
    {
        get
        {
            var rawMode = configuration["Recognition:RouteMode"];
            if (Enum.TryParse<RecognitionRouteMode>(rawMode, ignoreCase: true, out var mode))
            {
                return mode;
            }

            return RecognitionRouteMode.Operations;
        }
    }
}

namespace ParkingOperations.Application.Common.Interfaces;

public enum RecognitionRouteMode
{
    Legacy,
    Operations,
    Shadow
}

public interface IRecognitionRouteConfig
{
    RecognitionRouteMode Mode { get; }
}

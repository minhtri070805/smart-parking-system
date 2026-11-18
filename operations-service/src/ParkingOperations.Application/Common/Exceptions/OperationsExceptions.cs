using System.Net;

namespace ParkingOperations.Application.Common.Exceptions;

public class OperationsException(string message) : Exception(message);

public class ImageValidationException(string message) : OperationsException(message);

public class RecognitionServiceException(HttpStatusCode statusCode, string message) : OperationsException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

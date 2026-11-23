using System.Net;

namespace recognition.Application.Common.Exceptions;

public class RecognitionException(string message) : Exception(message);

public class ImageValidationException(string message) : RecognitionException(message);

public class AiServiceException(HttpStatusCode statusCode, string message) : RecognitionException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

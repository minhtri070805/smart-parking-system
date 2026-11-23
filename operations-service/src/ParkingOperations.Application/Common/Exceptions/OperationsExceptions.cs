using System.Net;

namespace ParkingOperations.Application.Common.Exceptions;

public class OperationsException(string message) : Exception(message);

public class ImageValidationException(string message) : OperationsException(message);

public class RecognitionServiceException(HttpStatusCode statusCode, string message) : OperationsException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>
/// Thrown when the same operation (same idempotency key) is retried and already succeeded —
/// caller should receive the previously-cached result, not a new one.
/// </summary>
public class DuplicateOperationException(string idempotencyKey, string message) : OperationsException(message)
{
    public string IdempotencyKey { get; } = idempotencyKey;
}

/// <summary>
/// Thrown when two concurrent requests race on the same resource and the second one loses.
/// </summary>
public class OperationConflictException(string message) : OperationsException(message);

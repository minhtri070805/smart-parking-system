using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using recognition.Application.Common.Exceptions;

namespace recognition.Api.Filters;

public class ServiceKeyAuthFilter(string internalKey) : IEndpointFilter
{
    private const string KeyHeader = "X-Recognition-Service-Key";
    private readonly byte[] _keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(internalKey));

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!httpContext.Request.Headers.TryGetValue(KeyHeader, out var supplied) ||
            supplied.Count != 1 ||
            !CryptographicOperations.FixedTimeEquals(_keyHash, SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()))))
        {
            return Results.Unauthorized();
        }

        try
        {
            return await next(context);
        }
        catch (ImageValidationException error)
        {
            return Results.BadRequest(new { error.Message });
        }
        catch (AiServiceException error)
        {
            return Results.Json(new { error.Message }, statusCode: (int)error.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Results.Json(new { message = "Downstream engine is unavailable." }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException) when (!httpContext.RequestAborted.IsCancellationRequested)
        {
            return Results.Json(new { message = "Downstream engine timed out." }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { message = "Invalid or oversized upload." });
        }
        catch (Exception error) when (error is NpgsqlException or DbUpdateException)
        {
            return Results.Json(new { message = "Recognition database is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
